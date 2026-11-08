<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import {
  getCartItems,
  deleteCartItem,
  updateCartItemQuantity,
  updateCartItemSelection,
  updateCartItemsSelection,
  CartUnauthorizedError,
  type CartItemResponse,
} from '@/api/cart'
import {
  getUserAddresses,
  UserAddressUnauthorizedError,
  type UserAddressResponse,
} from '@/api/userAddresses'
import {
  createOrder,
  OrderUnauthorizedError,
  OrderSubmissionUncertainError,
  type CreateOrderRequest,
  type CreateOrderResponse,
} from '@/api/orders'
import AdminDialog from '@/components/admin/AdminDialog.vue'
import StoreHeader from '@/components/layout/StoreHeader.vue'
import { useAuthStore } from '@/stores/auth'
import { openOwnedMessageBox, type OwnedMessageBox } from '@/utils/ownedMessageBox'

const authStore = useAuthStore()

// 下单成功后进入订单详情
const router = useRouter()

// 保存后端返回的购物车数据
const items = ref<CartItemResponse[]>([])
const isLoading = ref<boolean>(false)
const loadError = ref<string>('')

// 数量输入草稿，不直接修改后端返回的数量
const quantityDrafts = ref<Record<number, number | string>>({})
const savingQuantityId = ref<number | null>(null)
const deletingId = ref<number | null>(null)
const actionError = ref<string>('')

// 当前正在保存勾选状态的条目编号
const savingSelectionId = ref<number | null>(null)

// 批量保存期间锁定购物车操作
const isSavingSelections = ref<boolean>(false)

// 非空列表中，全部条目都已勾选时显示全选
const isAllSelected = computed<boolean>(
  () =>
    items.value.length > 0 &&
    items.value.every(item => item.isSelected),
)

// 只有部分条目勾选时显示半选状态
const isPartiallySelected = computed<boolean>(
  () =>
    items.value.some(item => item.isSelected) &&
    !isAllSelected.value,
)

// 只有已勾选且当前可购买的条目参与汇总
const selectedAvailableItems = computed<CartItemResponse[]>(
  () => items.value.filter(item => item.isSelected && item.isAvailable),
)

// 商品种数按商品编号去重，同一商品不同规格只算一种
const selectedProductCount = computed<number>(
  () =>
    new Set(
      selectedAvailableItems.value.map(item => item.productId),
    ).size,
)

// 件数使用已保存数量，不使用输入框中的数量草稿
const selectedQuantity = computed<number>(
  () =>
    selectedAvailableItems.value.reduce(
      (total, item) => total + item.quantity,
      0,
    ),
)

// 单价先转换为整数分，再乘以数量并累加
const selectedTotalCents = computed<number>(
  () =>
    selectedAvailableItems.value.reduce(
      (total, item) =>
        total + Math.round(item.price * 100) * item.quantity,
      0,
    ),
)

// 展示金额时转换回元，固定保留两位小数
const selectedTotalText = computed<string>(
  () => (selectedTotalCents.value / 100).toFixed(2),
)

// 已勾选但不可购买的条目不参与金额和件数计算
const selectedUnavailableCount = computed<number>(
  () =>
    items.value.filter(
      item => item.isSelected && !item.isAvailable,
    ).length,
)

// 任意一行有未保存的数量修改时，提示汇总仍按已保存数量计算
const hasUnsavedQuantity = computed<boolean>(
  () =>
    items.value.some(
      item =>
        Number(quantityDrafts.value[item.id]) !== item.quantity,
    ),
)

// 确认框打开期间即锁定购物车操作
const confirmingDeleteId = ref<number | null>(null)
let deleteMessageBox: OwnedMessageBox | null = null

// 本次选择的收货地址，不自动选中第一条
const addresses = ref<UserAddressResponse[]>([])
const selectedAddressId = ref<number | null>(null)

const selectedAddress = computed<UserAddressResponse | null>(
  () =>
    addresses.value.find(
      address => address.id === selectedAddressId.value,
    ) ?? null,
)

// 弹窗内先修改草稿，点击确定后才应用
const isAddressDialogOpen = ref<boolean>(false)
const draftAddressId = ref<number | null>(null)
const isLoadingAddresses = ref<boolean>(false)
const addressError = ref<string>('')

// 地址查询独立计数，避免影响购物车请求
let addressRequestVersion: number = 0

// 下单期间及结果不确定时保留原始请求
const isSubmittingOrder = ref<boolean>(false)
const isOrderOutcomeUnknown = ref<boolean>(false)
let pendingOrderRequest: CreateOrderRequest | null = null

// 已经创建成功的订单只允许进入详情，不再重新下单
const createdOrder = ref<CreateOrderResponse | null>(null)

// 原有购物车请求或删除确认是否正在进行
const isCartRequestBusy = computed<boolean>(
  () =>
    isLoading.value ||
    savingQuantityId.value !== null ||
    savingSelectionId.value !== null ||
    isSavingSelections.value ||
    deletingId.value !== null ||
    confirmingDeleteId.value !== null,
)

// 下单结果不确定或已经成功时，继续锁定购物车编辑
const isBusy = computed<boolean>(
  () =>
    isCartRequestBusy.value ||
    isAddressDialogOpen.value ||
    isSubmittingOrder.value ||
    isOrderOutcomeUnknown.value ||
    createdOrder.value !== null,
)

// 下单按钮允许重试原请求，也允许进入已经创建的订单
const isCheckoutDisabled = computed<boolean>(
  () =>
    isCartRequestBusy.value ||
    isAddressDialogOpen.value ||
    isSubmittingOrder.value,
)

// 页面离开或会话变化后，旧请求不能更新列表
let isPageActive: boolean = true
let requestVersion: number = 0

// 确认结果属于当前页面、当前请求和当前登录会话
function isCurrentRequest(
  version: number,
  accessToken: string,
): boolean {
  return (
    isPageActive &&
    version === requestVersion &&
    authStore.isAuthenticated &&
    authStore.isCurrentSession(accessToken)
  )
}

// 查询购物车；quantitySaved 用于区分保存成功后的刷新失败
async function loadCart(
  quantitySaved: boolean = false,
): Promise<void> {
  if (
    !isPageActive ||
    isBusy.value ||
    !authStore.isAuthenticated
  ) {
    return
  }

  const accessToken: string = authStore.accessToken
  const version: number = ++requestVersion

  isLoading.value = true
  loadError.value = ''
  actionError.value = ''

  try {
    const result: CartItemResponse[] = await getCartItems(accessToken)

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    // 初始化数量草稿；有库存时限制上限，无库存时保留原数量供查看
    const drafts: Record<number, number | string> = {}

    for (const item of result) {
      drafts[item.id] = item.stock > 0
        ? Math.min(item.quantity, item.stock)
        : item.quantity
    }

    items.value = result
    quantityDrafts.value = drafts
  } catch (error: unknown) {
    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    if (error instanceof CartUnauthorizedError) {
      authStore.clearSession(true)
      authStore.sessionExpired = true
      return
    }

    items.value = []
    quantityDrafts.value = {}

    const message: string =
      error instanceof Error ? error.message : '获取购物车失败，请稍后重试'

    loadError.value = quantitySaved
      ? `数量已保存，但列表刷新失败，请重新加载。${message}`
      : message
  } finally {
    if (isCurrentRequest(version, accessToken)) {
      isLoading.value = false
    }
  }
}

// 将数量草稿修正为 1～页面当前库存的整数，不自动提交
function normalizeQuantity(item: CartItemResponse): number {
  if (item.stock <= 0) {
    return 0
  }

  const draft: number = Number(quantityDrafts.value[item.id])
  const quantity: number = Math.min(
    item.stock,
    Math.max(1, Math.trunc(Number.isFinite(draft) ? draft : 1)),
  )

  quantityDrafts.value[item.id] = quantity
  return quantity
}

// 保存输入框中的最终数量
async function handleSaveQuantity(
  item: CartItemResponse,
): Promise<void> {
  if (
    !isPageActive ||
    isBusy.value ||
    item.stock <= 0 ||
    !authStore.isAuthenticated
  ) {
    return
  }

  actionError.value = ''

  // 保存前再次修正，覆盖按回车或尚未离开输入框的情况
  const quantity: number = normalizeQuantity(item)

  if (quantity === item.quantity) {
    return
  }

  const accessToken: string = authStore.accessToken
  const version: number = requestVersion

  savingQuantityId.value = item.id

  try {
    await updateCartItemQuantity(item.id, quantity, accessToken)

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    // 解除保存锁，再由加载状态锁定操作并刷新库存、数量和状态
    savingQuantityId.value = null
    await loadCart(true)
  } catch (error: unknown) {
    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    if (error instanceof CartUnauthorizedError) {
      authStore.clearSession(true)
      authStore.sessionExpired = true
      return
    }

    actionError.value =
      error instanceof Error ? error.message : '修改数量失败，请稍后重试'
  } finally {
    if (isCurrentRequest(version, accessToken)) {
      savingQuantityId.value = null
    }
  }
}

// 保存单条勾选状态，失败时恢复原来的显示
async function handleSelectionChange(
  item: CartItemResponse,
  event: Event,
): Promise<void> {
  const input: EventTarget | null = event.target

  if (!(input instanceof HTMLInputElement)) {
    return
  }

  const previousIsSelected: boolean = item.isSelected
  const targetIsSelected: boolean = input.checked

  if (
    !isPageActive ||
    isBusy.value ||
    !authStore.isAuthenticated ||
    targetIsSelected === previousIsSelected
  ) {
    input.checked = previousIsSelected
    return
  }

  const accessToken: string = authStore.accessToken
  const version: number = requestVersion

  savingSelectionId.value = item.id
  actionError.value = ''

  // 先展示本次选择；请求失败时再恢复
  item.isSelected = targetIsSelected

  try {
    await updateCartItemSelection(
      item.id,
      targetIsSelected,
      accessToken,
    )

    // 保留目标状态，不刷新列表，避免清空尚未保存的数量草稿
  } catch (error: unknown) {
    // 离页或切换会话后，不修改新页面的数据或登录状态
    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    if (error instanceof CartUnauthorizedError) {
      authStore.clearSession(true)
      authStore.sessionExpired = true
      return
    }

    item.isSelected = previousIsSelected
    input.checked = previousIsSelected

    actionError.value =
      error instanceof Error
        ? error.message
        : '修改勾选状态失败，请稍后重试'
  } finally {
    if (isCurrentRequest(version, accessToken)) {
      savingSelectionId.value = null
    }
  }
}

// 将表头复选框恢复为当前列表对应的状态
function syncAllSelectionInput(input: HTMLInputElement): void {
  input.checked = isAllSelected.value
  input.indeterminate = isPartiallySelected.value
}

// 全选或取消全选，一次请求保存当前列表全部条目的状态
async function handleAllSelectionChange(event: Event): Promise<void> {
  const input: EventTarget | null = event.target

  if (!(input instanceof HTMLInputElement)) {
    return
  }

  const targetIsSelected: boolean = input.checked

  if (
    !isPageActive ||
    isBusy.value ||
    !authStore.isAuthenticated ||
    items.value.length === 0
  ) {
    syncAllSelectionInput(input)
    return
  }

  // 状态没有变化时不重复提交
  if (items.value.every(item => item.isSelected === targetIsSelected)) {
    syncAllSelectionInput(input)
    return
  }

  const accessToken: string = authStore.accessToken
  const version: number = requestVersion

  // 保存每个条目的原状态，失败时恢复原来的混合勾选状态
  const previousSelections: {
    item: CartItemResponse
    isSelected: boolean
  }[] = items.value.map(item => ({
    item,
    isSelected: item.isSelected,
  }))

  // 提交购物车条目编号，不是商品编号或 SKU 编号
  const cartItemIds: number[] = previousSelections.map(
    selection => selection.item.id,
  )

  isSavingSelections.value = true
  actionError.value = ''

  // 先展示目标状态；请求失败时恢复
  for (const selection of previousSelections) {
    selection.item.isSelected = targetIsSelected
  }

  syncAllSelectionInput(input)

  try {
    await updateCartItemsSelection(
      cartItemIds,
      targetIsSelected,
      accessToken,
    )

    // 成功后不刷新列表，保留尚未保存的数量草稿
  } catch (error: unknown) {
    // 离页或切换会话后，不影响新页面的数据和登录状态
    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    if (error instanceof CartUnauthorizedError) {
      authStore.clearSession(true)
      authStore.sessionExpired = true
      return
    }

    for (const selection of previousSelections) {
      selection.item.isSelected = selection.isSelected
    }

    syncAllSelectionInput(input)

    actionError.value =
      error instanceof Error
        ? error.message
        : '批量修改勾选状态失败，请稍后重试'
  } finally {
    if (isCurrentRequest(version, accessToken)) {
      isSavingSelections.value = false
    }
  }
}

// 只清理当前购物车拥有的确认框
function closeDeleteConfirmation(): void {
  const box: OwnedMessageBox | null = deleteMessageBox
  deleteMessageBox = null
  confirmingDeleteId.value = null
  box?.close()
}

// 确认后删除条目，等待期间保留原会话和请求版本
async function handleDelete(item: CartItemResponse): Promise<void> {
  if (!isPageActive || isBusy.value || !authStore.isAuthenticated) {
    return
  }

  const accessToken: string = authStore.accessToken
  const version: number = requestVersion
  confirmingDeleteId.value = item.id
  actionError.value = ''

  let box: OwnedMessageBox | null = null
  try {
    box = openOwnedMessageBox({
      title: '确认删除',
      message: `确定将“${item.productName}”移出购物车吗？`,
      type: 'warning',
      showCancelButton: true,
      confirmButtonText: '确认删除',
    })
    deleteMessageBox = box

    const confirmed: boolean = await box.result
    if (deleteMessageBox !== box) {
      return
    }
    deleteMessageBox = null
    confirmingDeleteId.value = null

    // 确认期间可能离页、切换账号或移除条目，不能借用新的登录会话
    if (!confirmed || !isCurrentRequest(version, accessToken)
      || !items.value.some(current => current.id === item.id && current.skuId === item.skuId)) {
      return
    }

    deletingId.value = item.id
    await deleteCartItem(item.id, accessToken)

    if (isCurrentRequest(version, accessToken)) {
      items.value = items.value.filter(currentItem => currentItem.id !== item.id)
      delete quantityDrafts.value[item.id]
    }
  } catch (error: unknown) {
    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    if (error instanceof CartUnauthorizedError) {
      authStore.clearSession(true)
      authStore.sessionExpired = true
      return
    }

    actionError.value = error instanceof Error ? error.message : '删除失败，请稍后重试'
  } finally {
    if (box !== null && deleteMessageBox === box) {
      closeDeleteConfirmation()
    }
    if (isCurrentRequest(version, accessToken)) {
      confirmingDeleteId.value = null
      deletingId.value = null
    }
  }
}

// 地址响应必须属于当前弹窗和原登录会话
function isCurrentAddressRequest(version: number, accessToken: string): boolean {
  return (
    isPageActive &&
    isAddressDialogOpen.value &&
    version === addressRequestVersion &&
    authStore.isAuthenticated &&
    authStore.isCurrentSession(accessToken)
  )
}

// 打开弹窗或点击重新加载时，查询最新的本人地址
async function loadAddresses(): Promise<void> {
  if (
    !isPageActive ||
    !isAddressDialogOpen.value ||
    isLoadingAddresses.value ||
    !authStore.isAuthenticated
  ) {
    return
  }

  const accessToken: string = authStore.accessToken
  const version: number = ++addressRequestVersion

  isLoadingAddresses.value = true
  addressError.value = ''

  try {
    const result: UserAddressResponse[] = await getUserAddresses(accessToken)

    if (!isCurrentAddressRequest(version, accessToken)) {
      return
    }

    addresses.value = result

    // 已删除的地址不能继续作为本次收货地址
    if (
      selectedAddressId.value !== null &&
      !result.some(address => address.id === selectedAddressId.value)
    ) {
      selectedAddressId.value = null
    }

    if (
      draftAddressId.value !== null &&
      !result.some(address => address.id === draftAddressId.value)
    ) {
      draftAddressId.value = null
    }
  } catch (error: unknown) {
    if (!isCurrentAddressRequest(version, accessToken)) {
      return
    }

    if (error instanceof UserAddressUnauthorizedError) {
      authStore.clearSession(true)
      authStore.sessionExpired = true
      return
    }

    addressError.value =
      error instanceof Error ? error.message : '获取收货地址失败，请稍后重试'
  } finally {
    if (isCurrentAddressRequest(version, accessToken)) {
      isLoadingAddresses.value = false
    }
  }
}

// 复制已确认的选择，每次打开都重新查询地址
function handleOpenAddressDialog(): void {
  if (!isPageActive || isBusy.value || !authStore.isAuthenticated) {
    return
  }

  // 重新打开时，使上一轮地址请求失效
  addressRequestVersion += 1
  isLoadingAddresses.value = false
  addressError.value = ''
  draftAddressId.value = selectedAddressId.value
  isAddressDialogOpen.value = true

  void loadAddresses()
}

// 确认有效的草稿地址，不自动提交订单
function handleConfirmAddress(): void {
  if (
    !isAddressDialogOpen.value ||
    isLoadingAddresses.value ||
    addressError.value ||
    !authStore.isAuthenticated ||
    !addresses.value.some(address => address.id === draftAddressId.value)
  ) {
    return
  }

  selectedAddressId.value = draftAddressId.value
  isAddressDialogOpen.value = false
}

// 弹窗实际关闭后清理草稿，取消不会修改已确认地址
function handleAddressDialogClosed(): void {
  addressRequestVersion += 1
  draftAddressId.value = null
  isLoadingAddresses.value = false
  addressError.value = ''
}

// 校验后直接创建订单；结果不确定时只重试原始请求
async function handleCheckout(): Promise<void> {
  if (
    !isPageActive ||
    isCheckoutDisabled.value ||
    !authStore.isAuthenticated ||
    loadError.value
  ) {
    return
  }

  // 已经成功创建时，只进入原订单
  if (createdOrder.value !== null) {
    void router.replace({
      name: 'order-detail',
      params: { id: createdOrder.value.id },
    })
    return
  }

  actionError.value = ''

  // 首次提交才读取页面数据，未知结果的重试不重新生成请求
  if (!isOrderOutcomeUnknown.value) {
    const selectedItems: CartItemResponse[] =
      items.value.filter(item => item.isSelected)

    if (selectedItems.length === 0) {
      actionError.value = '请先勾选需要结算的商品'
      return
    }

    if (selectedItems.some(item => !item.isAvailable)) {
      actionError.value =
        '有商品暂不可购买，请调整勾选或处理这些商品后再结算'
      return
    }

    // 未勾选商品的数量草稿不影响本次结算
    if (
      selectedItems.some(
        item => Number(quantityDrafts.value[item.id]) !== item.quantity,
      )
    ) {
      actionError.value = '已勾选商品的数量有修改，请先保存后再结算'
      return
    }

    // 缺少地址时只打开选择弹窗，选好后再次点击去结算
    const address: UserAddressResponse | null = selectedAddress.value

    if (address === null) {
      handleOpenAddressDialog()
      return
    }

    // 生成与现有 SKU 保存方式一致的 32 位十六进制标识
    const randomBytes: Uint8Array<ArrayBuffer> = new Uint8Array(16)
    crypto.getRandomValues(randomBytes)

    const requestId: string = Array.from(
      randomBytes,
      byte => byte.toString(16).padStart(2, '0'),
    ).join('')

    // 保存提交快照；后续重试不能重新读取或替换这些数据
    pendingOrderRequest = {
      requestId,
      addressId: address.id,
      items: selectedItems.map(item => ({
        cartItemId: item.id,
        quantity: item.quantity,
        unitPrice: item.price,
      })),
    }
  }

  const request: CreateOrderRequest | null = pendingOrderRequest

  if (request === null) {
    return
  }

  const accessToken: string = authStore.accessToken
  const version: number = requestVersion

  isSubmittingOrder.value = true

  try {
    const result: CreateOrderResponse = await createOrder(request, accessToken)

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    createdOrder.value = result
    isOrderOutcomeUnknown.value = false
  } catch (error: unknown) {
    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    if (error instanceof OrderUnauthorizedError) {
      authStore.clearSession(true)
      authStore.sessionExpired = true
      return
    }

    if (error instanceof OrderSubmissionUncertainError) {
      // 保留请求及操作锁，下次点击只重试同一次下单
      isOrderOutcomeUnknown.value = true
    } else {
      // 明确业务失败后允许调整页面，再发起新的一次下单
      pendingOrderRequest = null
      isOrderOutcomeUnknown.value = false
    }

    actionError.value =
      error instanceof Error ? error.message : '创建订单失败，请稍后重试'
  } finally {
    if (isCurrentRequest(version, accessToken)) {
      isSubmittingOrder.value = false
    }
  }

  // 页面跳转与创建请求的错误处理分开
  if (isCurrentRequest(version, accessToken) && createdOrder.value !== null) {
    void router.replace({
      name: 'order-detail',
      params: { id: createdOrder.value.id },
    })
  }
}

// 首次进入时加载；退出或更换会话时立即清理数据
watch(
  () => [authStore.sessionId, authStore.isAuthenticated] as const,
  () => {
    requestVersion += 1
    closeDeleteConfirmation()
    items.value = []
    quantityDrafts.value = {}
    loadError.value = ''
    actionError.value = ''
    isLoading.value = false
    savingQuantityId.value = null
    savingSelectionId.value = null
    isSavingSelections.value = false
    deletingId.value = null

    addressRequestVersion += 1
    addresses.value = []
    selectedAddressId.value = null
    isAddressDialogOpen.value = false
    draftAddressId.value = null
    isLoadingAddresses.value = false
    addressError.value = ''

    isSubmittingOrder.value = false
    isOrderOutcomeUnknown.value = false
    pendingOrderRequest = null
    createdOrder.value = null

    if (authStore.isAuthenticated) {
      void loadCart()
    }
  },
  { immediate: true, flush: 'sync' },
)

// 页面卸载后关闭业务弹窗，并丢弃旧请求结果
onBeforeUnmount(() => {
  isPageActive = false
  requestVersion += 1
  addressRequestVersion += 1

  isAddressDialogOpen.value = false
  pendingOrderRequest = null
  createdOrder.value = null

  closeDeleteConfirmation()
})
</script>

<template>
  <div class="cart-page">
    <StoreHeader />

    <main class="cart-content">
      <header class="page-heading">
        <h1>购物车</h1>
        <RouterLink class="continue-link" :to="{ name: 'home' }">
          继续购物
        </RouterLink>
      </header>

      <section
        v-if="!isLoading && !loadError && items.length > 0"
        class="cart-address"
        aria-label="本次收货地址"
      >
        <div class="cart-address-heading">
          <h2>收货地址</h2>
          <button
            class="retry-button"
            type="button"
            :disabled="isBusy"
            @click="handleOpenAddressDialog"
          >
            {{ selectedAddress ? '更换地址' : '选择收货地址' }}
          </button>
        </div>

        <template v-if="selectedAddress">
          <p class="cart-address-recipient">
            <strong>{{ selectedAddress.recipientName }}</strong>
            <span>{{ selectedAddress.phone }}</span>
          </p>
          <p class="cart-address-text">{{ selectedAddress.address }}</p>
        </template>
        <p v-else class="cart-address-placeholder">请先选择本次订单的收货地址</p>
      </section>

      <section
        class="cart-panel"
        aria-label="购物车商品"
        :aria-busy="isBusy"
      >
        <p v-if="isLoading" class="panel-message" role="status">
          正在加载购物车…
        </p>

        <div v-else-if="loadError" class="message-panel">
          <p class="error-message" role="alert">{{ loadError }}</p>
          <button class="retry-button" type="button" @click="loadCart()">
            重新加载
          </button>
        </div>

        <div v-else-if="items.length === 0" class="message-panel">
          <p class="empty-message" role="status">购物车里还没有商品</p>
          <RouterLink class="continue-link" :to="{ name: 'home' }">
            去逛逛
          </RouterLink>
        </div>

        <div v-else class="table-container">
          <div v-if="actionError" class="delete-feedback">
            <p class="error-message" role="alert">
              {{ actionError }}
            </p>
            <button
              class="retry-button"
              type="button"
              :disabled="isBusy"
              @click="loadCart()"
            >
              刷新列表
            </button>
          </div>

          <table class="cart-table">
            <thead>
              <tr>
                <th scope="col" class="selection-cell">
                  <label class="selection-all-label">
                    <input
                      class="selection-checkbox"
                      type="checkbox"
                      :checked="isAllSelected"
                      :indeterminate.prop="isPartiallySelected"
                      :disabled="isBusy"
                      @change="handleAllSelectionChange($event)"
                    />
                    <span>全选</span>
                  </label>

                  <span
                    v-if="isSavingSelections"
                    class="selection-saving"
                    role="status"
                  >
                    保存中…
                  </span>
                </th>
                <th scope="col">商品信息</th>
                <th scope="col">单价</th>
                <th scope="col">库存</th>
                <th scope="col">数量</th>
                <th scope="col">状态</th>
                <th scope="col">操作</th>
              </tr>
            </thead>

            <tbody>
              <tr v-for="item in items" :key="item.id">
                <td class="selection-cell">
                  <input
                    class="selection-checkbox"
                    type="checkbox"
                    :checked="item.isSelected"
                    :disabled="isBusy"
                    :aria-label="
                      '选择' +
                      item.productName +
                      '，' +
                      (item.specificationText || '默认规格')
                    "
                    @change="handleSelectionChange(item, $event)"
                  />

                  <span
                    v-if="savingSelectionId === item.id"
                    class="selection-saving"
                    role="status"
                  >
                    保存中…
                  </span>
                </td>

                <td class="product-cell">
                  <p class="product-name">{{ item.productName }}</p>
                  <p class="specification">
                    {{ item.specificationText || '默认规格' }}
                  </p>
                </td>

                <td class="price">¥{{ item.price.toFixed(2) }}</td>
                <td>{{ item.stock }}</td>
                <td>
                  <div class="quantity-editor">
                    <input
                      v-model.number="quantityDrafts[item.id]"
                      class="quantity-input"
                      type="number"
                      min="1"
                      :max="item.stock"
                      step="1"
                      aria-label="购买数量"
                      :disabled="isBusy || item.stock <= 0"
                      @blur="normalizeQuantity(item)"
                      @keydown.enter.prevent="handleSaveQuantity(item)"
                    />

                    <button
                      class="quantity-save"
                      type="button"
                      :disabled="
                        isBusy ||
                        item.stock <= 0 ||
                        Number(quantityDrafts[item.id]) === item.quantity
                      "
                      @click="handleSaveQuantity(item)"
                    >
                      {{ savingQuantityId === item.id ? '保存中…' : '保存' }}
                    </button>
                  </div>
                </td>

                <td class="status-cell">
                  <span v-if="item.isAvailable" class="available">
                    可购买
                  </span>
                  <span v-else class="unavailable">
                    {{ item.unavailableReason || '暂不可购买' }}
                  </span>
                </td>

                <td>
                  <button
                    class="delete-button"
                    type="button"
                    :disabled="isBusy"
                    @click="handleDelete(item)"
                  >
                    {{ deletingId === item.id ? '删除中…' : '删除' }}
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <footer
          v-if="!isLoading && !loadError && items.length > 0"
          class="cart-summary"
          aria-label="购物车汇总"
          aria-live="polite"
          aria-atomic="true"
        >
          <div class="summary-main">
            <p class="summary-count">
              已选可购买商品
              <strong>{{ selectedProductCount }}</strong>
              种，共
              <strong>{{ selectedQuantity }}</strong>
              件
            </p>

            <div class="summary-checkout">
              <p class="summary-total">
                <span>商品合计：</span>
                <strong>¥{{ selectedTotalText }}</strong>
              </p>

              <button
                class="checkout-button"
                type="button"
                :disabled="isCheckoutDisabled"
                @click="handleCheckout"
              >
                {{
                  isSubmittingOrder
                    ? '正在下单…'
                    : createdOrder
                      ? '查看订单'
                      : isOrderOutcomeUnknown
                        ? '重试本次下单'
                        : '去结算'
                }}
              </button>
            </div>
          </div>

          <p
            v-if="selectedUnavailableCount > 0"
            class="summary-notice"
          >
            另有 {{ selectedUnavailableCount }} 项已选商品暂不可购买，
            未计入上述种数、件数和合计。请调整勾选或处理这些商品后再结算。
          </p>

          <p
            v-if="hasUnsavedQuantity"
            class="summary-notice"
          >
            有数量修改尚未保存，汇总仍按已保存数量计算。
          </p>
          <p v-if="isOrderOutcomeUnknown" class="summary-notice" role="status">
            暂时无法确认下单结果，已保留本次商品和地址。
            请点击“重试本次下单”，使用原请求确认结果。
          </p>

          <p v-if="createdOrder" class="summary-notice" role="status">
            订单 {{ createdOrder.orderNo }} 已创建，可点击“查看订单”进入详情。
          </p>
        </footer>
      </section>
    </main>

    <AdminDialog
      v-model="isAddressDialogOpen"
      title="选择收货地址"
      :close-disabled="isSubmittingOrder"
      @closed="handleAddressDialogClosed"
    >
      <div class="cart-address-dialog-heading">
        <RouterLink :to="{ name: 'user-addresses' }">管理收货地址</RouterLink>
      </div>

      <p v-if="isLoadingAddresses" role="status">正在加载收货地址…</p>

      <div v-else-if="addressError">
        <p class="error-message" role="alert">{{ addressError }}</p>
        <button class="retry-button" type="button" @click="loadAddresses">
          重新加载
        </button>
      </div>

      <p v-else-if="addresses.length === 0">
        还没有收货地址，请先进入“管理收货地址”添加。
      </p>

      <fieldset v-else class="cart-address-options">
        <legend class="cart-address-legend">请选择本次收货地址</legend>
        <label
          v-for="address in addresses"
          :key="address.id"
          class="cart-address-option"
          :class="{ selected: draftAddressId === address.id }"
        >
          <input
            v-model="draftAddressId"
            type="radio"
            name="cart-shipping-address"
            :value="address.id"
          />
          <span class="cart-address-details">
            <span class="cart-address-recipient">
              <strong>{{ address.recipientName }}</strong>
              <span>{{ address.phone }}</span>
            </span>
            <span class="cart-address-text">{{ address.address }}</span>
          </span>
        </label>
      </fieldset>

      <template #actions>
        <button
          class="checkout-button"
          type="button"
          :disabled="isLoadingAddresses || !!addressError || draftAddressId === null"
          @click="handleConfirmAddress"
        >
          确定
        </button>
      </template>
    </AdminDialog>
  </div>
</template>

<style scoped>
.cart-page {
  min-height: 100vh;
  background: #f4f6fa;
  color: #253248;
}

.cart-content {
  box-sizing: border-box;
  width: 100%;
  max-width: 1240px;
  margin: 0 auto;
  padding: 28px 28px 48px;
}

.page-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 20px;
  margin-bottom: 20px;
}

.page-heading h1 {
  margin: 0;
  font-size: 25px;
  font-weight: 600;
}

.continue-link {
  color: #2864e8;
  font-size: 14px;
  text-decoration: none;
}

.continue-link:hover {
  text-decoration: underline;
}

.cart-panel {
  overflow: hidden;
  border: 1px solid #e5eaf2;
  border-radius: 12px;
  background: #ffffff;
}

.panel-message,
.message-panel {
  margin: 0;
  padding: 56px 24px;
  text-align: center;
}

.panel-message,
.empty-message {
  color: #8793a5;
}

.message-panel {
  display: grid;
  justify-items: center;
  gap: 16px;
}

.message-panel p {
  margin: 0;
  line-height: 1.7;
}

.error-message,
.unavailable {
  color: #bd4848;
}

.retry-button {
  padding: 9px 16px;
  border: 1px solid #dfe6ef;
  border-radius: 7px;
  background: #ffffff;
  color: #52617a;
  font: inherit;
  cursor: pointer;
}

.retry-button:hover {
  background: #f6f9ff;
}

.table-container {
  overflow-x: auto;
}

.cart-table {
  width: 100%;
  min-width: 760px;
  border-collapse: collapse;
  font-size: 14px;
}

.cart-table th,
.cart-table td {
  padding: 20px;
  text-align: left;
  vertical-align: middle;
}

.cart-table th {
  background: #f8faff;
  color: #8793a5;
  font-weight: 400;
}

.cart-table td {
  border-top: 1px solid #edf1f6;
}

/* 单条勾选及保存状态 */
.selection-cell {
  width: 100px;
  white-space: nowrap;
}

/* 表头全选复选框与文字 */
.selection-all-label {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  vertical-align: middle;
  cursor: pointer;
}

.selection-checkbox {
  width: 16px;
  height: 16px;
  margin: 0;
  vertical-align: middle;
  accent-color: #2864e8;
  cursor: pointer;
}

.selection-checkbox:disabled {
  cursor: not-allowed;
}

.selection-saving {
  margin-left: 6px;
  color: #8793a5;
  font-size: 12px;
}

.product-cell {
  min-width: 200px;
  max-width: 360px;
  overflow-wrap: anywhere;
}

.product-name {
  margin: 0 0 8px;
  color: #253248;
}

.specification {
  margin: 0;
  color: #8793a5;
  font-size: 13px;
  line-height: 1.6;
}

.price {
  color: #2864e8;
  white-space: nowrap;
}

.unavailable {
  margin: 0;
  font-size: 13px;
  line-height: 1.6;
}

/* 操作失败提示和刷新入口 */
.delete-feedback {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
  padding: 16px 20px;
}

.delete-feedback p {
  margin: 0;
  line-height: 1.6;
}

/* 状态直接展示后端返回的具体原因 */
.status-cell {
  min-width: 160px;
  max-width: 240px;
  font-size: 13px;
  line-height: 1.6;
  overflow-wrap: anywhere;
}

.available {
  color: #27845b;
}

.delete-button {
  padding: 6px 0;
  border: none;
  background: transparent;
  color: #bd4848;
  font: inherit;
  white-space: nowrap;
  cursor: pointer;
}

.delete-button:disabled,
.retry-button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

/* 数量输入框和保存按钮 */
.quantity-editor {
  display: flex;
  align-items: center;
  gap: 8px;
}

.quantity-input {
  width: 76px;
  padding: 7px 8px;
  border: 1px solid #dfe6ef;
  border-radius: 6px;
  background: #ffffff;
  color: #253248;
  font: inherit;
}

.quantity-save {
  padding: 7px 10px;
  border: 1px solid #dfe6ef;
  border-radius: 6px;
  background: #ffffff;
  color: #2864e8;
  font: inherit;
  white-space: nowrap;
  cursor: pointer;
}

.quantity-input:disabled,
.quantity-save:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

/* 购物车底部汇总 */
.cart-summary {
  padding: 20px;
  border-top: 1px solid #e5eaf2;
  background: #fbfcff;
}

.summary-main {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px 24px;
}

.summary-count {
  margin: 0;
  color: #52617a;
  font-size: 14px;
  line-height: 1.8;
}

.summary-count strong {
  color: #2864e8;
}

.summary-total {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 6px;
  margin: 0;
  color: #52617a;
  font-size: 14px;
}

.summary-total strong {
  color: #2864e8;
  font-size: 22px;
  font-weight: 600;
  overflow-wrap: anywhere;
}

.summary-notice {
  margin: 10px 0 0;
  color: #946a30;
  font-size: 13px;
  line-height: 1.7;
}

.summary-checkout {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 16px;
}

.checkout-button {
  padding: 10px 22px;
  border: 1px solid #2864e8;
  border-radius: 6px;
  background: #2864e8;
  color: #ffffff;
  font: inherit;
  cursor: pointer;
}

.checkout-button:hover:not(:disabled) {
  background: #1e54cc;
}

.checkout-button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.cart-address {
  margin-bottom: 20px;
  padding: 20px;
  border: 1px solid #e5eaf2;
  border-radius: 12px;
  background: #fff;
}

.cart-address-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 12px;
}

.cart-address-heading h2 {
  margin: 0;
  font-size: 18px;
}

.cart-address-recipient {
  display: flex;
  align-items: baseline;
  flex-wrap: wrap;
  gap: 8px 16px;
  margin: 16px 0 8px;
  overflow-wrap: anywhere;
}

.cart-address-text {
  display: block;
  margin: 0;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  line-height: 1.7;
}

.cart-address-placeholder {
  margin: 16px 0 0;
  color: #738096;
}

.cart-address-dialog-heading {
  margin-bottom: 16px;
  text-align: right;
}

.cart-address-dialog-heading a {
  color: #2864e8;
  text-decoration: none;
}

.cart-address-dialog-heading a:hover {
  text-decoration: underline;
}

.cart-address-options {
  min-width: 0;
  margin: 0;
  padding: 0;
  border: 0;
}

.cart-address-legend {
  margin-bottom: 4px;
  padding: 0;
  color: #738096;
}

.cart-address-option {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  margin-top: 12px;
  padding: 16px;
  border: 1px solid #dfe5ee;
  border-radius: 8px;
  cursor: pointer;
}

.cart-address-option.selected {
  border-color: #2864e8;
  background: #f4f7ff;
}

.cart-address-option input {
  flex-shrink: 0;
  margin-top: 5px;
  accent-color: #2864e8;
}

.cart-address-details {
  min-width: 0;
}

.cart-address-details .cart-address-recipient {
  margin-top: 0;
}

@media (max-width: 700px) {
  .cart-content {
    padding: 24px 18px 32px;
  }

  .page-heading h1 {
    font-size: 22px;
  }
}
</style>
