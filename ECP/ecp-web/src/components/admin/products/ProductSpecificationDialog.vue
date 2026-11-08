<script setup lang="ts">
import { computed, h, nextTick, onBeforeUnmount, reactive, ref, useId, watch } from 'vue'
import {
  getAdminProductSpecifications,
  createAdminProductSpecification,
  createAdminProductSpecificationValue,
  deleteAdminProductSpecification,
  deleteAdminProductSpecificationValue,
  type ProductSpecificationResponse,
  type ProductSpecificationValueResponse,
  type CreateProductSpecificationRequest,
  type CreateProductSpecificationValueRequest,
} from '@/api/productSpecifications'
import AdminDialog from '@/components/admin/AdminDialog.vue'
import { useAuthStore } from '@/stores/auth'
import { openOwnedMessageBox, type OwnedMessageBox } from '@/utils/ownedMessageBox'

// 新增规格表单，数字输入清空时会变成空字符串
interface SpecificationForm {
  name: string
  sortOrder: number | ''
}

// 新增规格值表单
interface SpecificationValueForm {
  value: string
  sortOrder: number | ''
}

// 规格和规格值共有的排序字段
interface OrderedItem {
  id: number
  sortOrder: number
}

// 待删除对象，区分删除整个规格和单个规格值
type DeleteTarget =
  | {
      kind: 'specification'
      specificationId: number
      label: string
    }
  | {
      kind: 'value'
      specificationId: number
      valueId: number
      label: string
    }

// 接收当前商品及弹窗显示状态
const props = withDefaults(
  defineProps<{
    modelValue: boolean
    productId: number
    productName?: string
  }>(),
  {
    productName: '',
  },
)

// 通知父组件更新弹窗显示状态
const emit = defineEmits<{
  'update:modelValue': [value: boolean]
}>()

// 获取当前登录会话
const authStore = useAuthStore()

// 为当前组件的表单控件生成独立编号
const componentId: string = useId()

// 保存当前商品的规格及规格值
const specifications = ref<ProductSpecificationResponse[]>([])

// 保存查询状态和失败提示
const isLoading = ref<boolean>(false)
const loadError = ref<string>('')

// 标记当前正在执行的新增或删除操作，避免重复提交
const savingKind = ref<
  '' | 'specification' | 'value' | 'delete-specification' | 'delete-value'
>('')

// 保存等待管理员确认的删除对象
const pendingDelete = ref<DeleteTarget | null>(null)

// 删除提醒弹窗的模式、说明和失败原因
const deleteDialogKind = ref<'confirm' | 'notice' | null>(null)
const deleteDialogMessage = ref<string>('')
const deleteDialogError = ref<string>('')

// 只持有本组件自己的提醒框；关闭后将焦点返回规格管理
let deleteMessageBox: OwnedMessageBox | null = null
const specificationListHeading = ref<HTMLHeadingElement | null>(null)
let deleteReturnFocus: HTMLElement | null = null

// 保存左侧选择和右侧操作模式
const selectedSpecificationId = ref<number | null>(null)
const isCreatingSpecification = ref<boolean>(false)

// 右侧始终只读取当前选中的规格
const selectedSpecification = computed<ProductSpecificationResponse | null>(
  () =>
    specifications.value.find(
      (specification: ProductSpecificationResponse) =>
        specification.id === selectedSpecificationId.value,
    ) ?? null,
)

// 新增规格后，便于直接继续添加它的值
const specificationNameInput = ref<HTMLInputElement | null>(null)
const specificationValueInput = ref<HTMLInputElement | null>(null)

// 左右列表分别持有自己的滚动容器
const specificationScroll = ref<HTMLDivElement | null>(null)
const valueScroll = ref<HTMLDivElement | null>(null)

// 保存新增规格的输入
const specificationForm = reactive<SpecificationForm>({
  name: '',
  sortOrder: 0,
})

// 保存新增规格值的输入
const valueForm = reactive<SpecificationValueForm>({
  value: '',
  sortOrder: 0,
})

// 仅保存当前新增表单的错误，不显示成功提示条
const formError = ref<string>('')

// 标记当前请求版本和组件是否仍然存在
let requestVersion: number = 0
let isComponentActive: boolean = true

// 查询、新增或删除请求期间标记为忙碌
const isBusy = computed<boolean>(() => isLoading.value || savingKind.value !== '')

// 提交或删除确认期间，不允许切换操作对象
const isInteractionDisabled = computed<boolean>(() => isBusy.value || deleteDialogKind.value !== null)

// 转发弹窗状态，保存期间保持弹窗打开
const dialogVisible = computed<boolean>({
  get: () => props.modelValue,
  set: (visible: boolean) => {
    if (!visible && (savingKind.value !== '' || deleteDialogKind.value !== null)) {
      return
    }

    emit('update:modelValue', visible)
  },
})

// 根据确认、限制提示和失败状态显示标题
const deleteDialogTitle = computed<string>(() => {
  if (deleteDialogKind.value === 'notice') {
    return '无法删除'
  }

  return deleteDialogError.value ? '删除失败' : '确认删除'
})

// 按排序值、编号排列规格或规格值
function compareOrder(first: OrderedItem, second: OrderedItem): number {
  return first.sortOrder - second.sortOrder || first.id - second.id
}

// 排序值必须为后端 int 范围内的非负整数
function isValidSortOrder(value: number | ''): value is number {
  return (
    typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= 2_147_483_647
  )
}

// 判断请求结果是否仍属于当前弹窗和当前商品
function isCurrentRequest(version: number, productId: number): boolean {
  return (
    isComponentActive &&
    version === requestVersion &&
    props.modelValue &&
    props.productId === productId
  )
}

// 查询、新增、删除或等待删除确认时，不接受新的新增操作
function canSubmit(): boolean {
  return (
    props.modelValue &&
    !isBusy.value &&
    loadError.value === '' &&
    deleteDialogKind.value === null
  )
}

// 显示输入校验或保存失败提示
function showError(message: string): void {
  formError.value = message
}

// 清理当前商品的数据和表单，同时使旧请求失效
function resetState(): void {
  requestVersion += 1
  closeDeleteMessageBox()

  specifications.value = []
  selectedSpecificationId.value = null
  isCreatingSpecification.value = false
  isLoading.value = false
  loadError.value = ''
  savingKind.value = ''
  pendingDelete.value = null
  deleteDialogKind.value = null
  deleteDialogMessage.value = ''
  deleteDialogError.value = ''
  deleteReturnFocus = null
  formError.value = ''

  specificationForm.name = ''
  specificationForm.sortOrder = 0
  valueForm.value = ''
  valueForm.sortOrder = 0
}

// 查询当前商品的规格，也作为查询失败后的重试入口
async function loadSpecifications(): Promise<void> {
  if (!props.modelValue || isInteractionDisabled.value) {
    return
  }

  const productId: number = props.productId

  if (!Number.isInteger(productId) || productId <= 0 || productId > 2_147_483_647) {
    loadError.value = '商品编号无效，请关闭弹窗后重新打开'
    return
  }

  const version: number = ++requestVersion

  isLoading.value = true
  loadError.value = ''
  formError.value = ''
  specifications.value = []
  selectedSpecificationId.value = null

  try {
    const result: ProductSpecificationResponse[] = await getAdminProductSpecifications(
      productId,
      authStore.accessToken,
    )

    if (!isCurrentRequest(version, productId)) {
      return
    }

    specifications.value = result
    selectedSpecificationId.value = result[0]?.id ?? null
    isCreatingSpecification.value = result.length === 0
  } catch (error: unknown) {
    if (!isCurrentRequest(version, productId)) {
      return
    }

    loadError.value = error instanceof Error ? error.message : '获取商品规格失败，请稍后重试'
  } finally {
    if (isCurrentRequest(version, productId)) {
      isLoading.value = false
    }
  }
}

// 清空未提交表单及当前表单错误
function resetForms(): void {
  specificationForm.name = ''
  specificationForm.sortOrder = 0
  valueForm.value = ''
  valueForm.sortOrder = 0
  formError.value = ''
}

// 仅在当前商品和当前操作仍有效时移动输入焦点
function focusCurrentForm(): void {
  const productId: number = props.productId
  const creating: boolean = isCreatingSpecification.value
  const specificationId: number | null = selectedSpecificationId.value

  void nextTick(() => {
    if (
      !isComponentActive ||
      !props.modelValue ||
      props.productId !== productId ||
      isInteractionDisabled.value ||
      isCreatingSpecification.value !== creating ||
      selectedSpecificationId.value !== specificationId
    ) {
      return
    }

    if (creating) {
      specificationNameInput.value?.focus({ preventScroll: true })
    } else {
      specificationValueInput.value?.focus({ preventScroll: true })
    }
  })
}

// 只移动指定列表的滚动位置，不带动弹窗或另一个 box
function revealListItem(
  container: HTMLElement,
  item: HTMLElement,
  topInset: number = 0,
): void {
  if (container.clientHeight <= 0) {
    return
  }

  const containerRect: DOMRect = container.getBoundingClientRect()
  const itemRect: DOMRect = item.getBoundingClientRect()
  const visibleTop: number = containerRect.top + container.clientTop + topInset
  const visibleBottom: number =
    containerRect.top + container.clientTop + container.clientHeight

  if (itemRect.top < visibleTop) {
    container.scrollTop = Math.max(0, container.scrollTop - (visibleTop - itemRect.top))
  } else if (itemRect.bottom > visibleBottom) {
    container.scrollTop += itemRect.bottom - visibleBottom
  }
}

// 切换规格后，将右侧值列表回到开头
function resetValueScroll(): void {
  const productId: number = props.productId
  const specificationId: number | null = selectedSpecificationId.value

  void nextTick(() => {
    if (
      !isComponentActive ||
      !props.modelValue ||
      props.productId !== productId ||
      isCreatingSpecification.value ||
      selectedSpecificationId.value !== specificationId
    ) {
      return
    }

    if (valueScroll.value !== null) {
      valueScroll.value.scrollTop = 0
      valueScroll.value.scrollLeft = 0
    }
  })
}

// 新增或自动切换后，仅让左侧选中规格完整可见
function revealSpecification(specificationId: number): void {
  const productId: number = props.productId

  void nextTick(() => {
    if (
      !isComponentActive ||
      !props.modelValue ||
      props.productId !== productId ||
      isCreatingSpecification.value ||
      selectedSpecificationId.value !== specificationId
    ) {
      return
    }

    const container: HTMLDivElement | null = specificationScroll.value
    const item: HTMLElement | null =
      container?.querySelector<HTMLElement>(
        `[data-specification-id="${specificationId}"]`,
      ) ?? null

    if (container !== null && item !== null) {
      revealListItem(container, item)
    }
  })
}

// 新增值后仅滚动右侧列表，并避开固定表头
function revealValue(specificationId: number, valueId: number): void {
  const productId: number = props.productId

  void nextTick(() => {
    if (
      !isComponentActive ||
      !props.modelValue ||
      props.productId !== productId ||
      isCreatingSpecification.value ||
      selectedSpecificationId.value !== specificationId
    ) {
      return
    }

    const container: HTMLDivElement | null = valueScroll.value
    const item: HTMLElement | null =
      container?.querySelector<HTMLElement>(`[data-value-id="${valueId}"]`) ?? null

    if (container !== null && item !== null) {
      const headerHeight: number =
        container.querySelector('thead')?.getBoundingClientRect().height ?? 0
      revealListItem(container, item, headerHeight)
    }
  })
}

// 选中已有规格，切换时丢弃未提交输入
function selectSpecification(specificationId: number): void {
  if (
    !canSubmit() ||
    !specifications.value.some(
      (specification: ProductSpecificationResponse) => specification.id === specificationId,
    )
  ) {
    return
  }

  // 重复点击当前规格时，保留当前输入
  if (!isCreatingSpecification.value && selectedSpecificationId.value === specificationId) {
    return
  }

  resetForms()
  selectedSpecificationId.value = specificationId
  isCreatingSpecification.value = false
}

// 右侧切换到新增规格表单
function startCreateSpecification(): void {
  if (!canSubmit() || isCreatingSpecification.value) {
    return
  }

  resetForms()
  isCreatingSpecification.value = true
  focusCurrentForm()
}

// 取消新增规格，返回此前选中的规格
function cancelCreateSpecification(): void {
  if (!canSubmit()) {
    return
  }

  resetForms()

  if (
    !specifications.value.some(
      (specification: ProductSpecificationResponse) =>
        specification.id === selectedSpecificationId.value,
    )
  ) {
    selectedSpecificationId.value = specifications.value[0]?.id ?? null
  }

  isCreatingSpecification.value = specifications.value.length === 0
}

// 校验并新增商品规格，成功后进入这个规格的值管理
async function handleCreateSpecification(): Promise<void> {
  if (!canSubmit() || !isCreatingSpecification.value) {
    return
  }

  formError.value = ''

  const name: string = specificationForm.name.trim()
  const sortOrder: number | '' = specificationForm.sortOrder

  if (name.length === 0 || name.length > 50) {
    showError('请输入 1 到 50 个字符的规格名称')
    return
  }

  if (!isValidSortOrder(sortOrder)) {
    showError('排序值必须为 0 到 2147483647 之间的整数')
    return
  }

  const productId: number = props.productId
  const version: number = ++requestVersion

  // 创建请求快照，保存期间不再读取可变表单
  const request: CreateProductSpecificationRequest = {
    name,
    sortOrder,
  }

  savingKind.value = 'specification'

  try {
    const result: ProductSpecificationResponse = await createAdminProductSpecification(
      productId,
      request,
      authStore.accessToken,
    )

    if (!isCurrentRequest(version, productId)) {
      return
    }

    specifications.value = [...specifications.value, result].sort(compareOrder)
    selectedSpecificationId.value = result.id
    isCreatingSpecification.value = false
    resetForms()
    revealSpecification(result.id)
    focusCurrentForm()
  } catch (error: unknown) {
    if (!isCurrentRequest(version, productId)) {
      return
    }

    // 保存失败时保留规格输入，并在当前表单显示原因
    showError(error instanceof Error ? error.message : '新增商品规格失败，请稍后重试')
  } finally {
    if (isCurrentRequest(version, productId)) {
      savingKind.value = ''
    }
  }
}

// 为右侧当前规格新增规格值，归属不再由下拉框选择
async function handleCreateValue(): Promise<void> {
  if (!canSubmit() || isCreatingSpecification.value) {
    return
  }

  formError.value = ''

  const specification: ProductSpecificationResponse | null = selectedSpecification.value

  if (specification === null) {
    showError('请先在左侧选择规格')
    return
  }

  const value: string = valueForm.value.trim()
  const sortOrder: number | '' = valueForm.sortOrder

  if (value.length === 0 || value.length > 100) {
    showError('请输入 1 到 100 个字符的规格值')
    return
  }

  if (!isValidSortOrder(sortOrder)) {
    showError('排序值必须为 0 到 2147483647 之间的整数')
    return
  }

  const productId: number = props.productId
  const version: number = ++requestVersion

  // 创建请求快照，固定当前规格和本次提交的数据
  const request: CreateProductSpecificationValueRequest = {
    value,
    sortOrder,
  }

  savingKind.value = 'value'

  try {
    const result: ProductSpecificationValueResponse = await createAdminProductSpecificationValue(
      productId,
      specification.id,
      request,
      authStore.accessToken,
    )

    if (!isCurrentRequest(version, productId)) {
      return
    }

    specification.values = [...specification.values, result].sort(compareOrder)
    valueForm.value = ''
    valueForm.sortOrder = 0
    revealValue(specification.id, result.id)
    focusCurrentForm()
  } catch (error: unknown) {
    if (!isCurrentRequest(version, productId)) {
      return
    }

    // 保存失败时保留规格值输入和当前选中规格
    showError(error instanceof Error ? error.message : '新增规格值失败，请稍后重试')
  } finally {
    if (isCurrentRequest(version, productId)) {
      savingKind.value = ''
    }
  }
}

// 强制关闭当前业务拥有的提醒框，不影响其他弹窗
function closeDeleteMessageBox(): void {
  const box: OwnedMessageBox | null = deleteMessageBox
  deleteMessageBox = null
  box?.close()
}

// 确认与提醒使用 Element MessageBox，删除失败保留原框供重试
function openDeleteDialog(kind: 'confirm' | 'notice', message: string): void {
  if (deleteDialogKind.value === null) {
    deleteReturnFocus = document.activeElement instanceof HTMLElement
      ? document.activeElement : null
  }

  formError.value = ''
  deleteDialogMessage.value = message
  deleteDialogError.value = ''
  deleteDialogKind.value = kind

  // 删除对象失效时可在同一提醒框中切换为说明，避免叠加提示框
  if (deleteMessageBox !== null) {
    return
  }

  const productId: number = props.productId
  const accessToken: string = authStore.accessToken
  const box: OwnedMessageBox = openOwnedMessageBox({
    title: deleteDialogTitle.value,
    type: 'warning',
    showCancelButton: kind === 'confirm',
    confirmButtonText: kind === 'confirm' ? '确认删除' : '知道了',
    message: () => h('div', [
      props.productName ? h('p', { class: 'ecp-message-context' }, props.productName) : null,
      h('p', deleteDialogMessage.value),
      deleteDialogError.value
        ? h('p', { class: 'ecp-message-error', role: 'alert' }, deleteDialogError.value)
        : null,
    ]),
    beforeClose: (action, instance, done) => {
      if (!isComponentActive || !props.modelValue || props.productId !== productId
        || !authStore.isCurrentSession(accessToken) || !authStore.isAuthenticated) {
        done()
        return
      }
      // 请求期间，关闭按钮、Escape、取消和重复确认均不执行
      if (isBusy.value) {
        return
      }
      if (action !== 'confirm' || deleteDialogKind.value === 'notice') {
        done()
        return
      }

      instance.confirmButtonLoading = true
      instance.confirmButtonText = '正在删除…'
      void confirmDelete().then(() => {
        if (deleteMessageBox !== box) {
          return
        }
        instance.confirmButtonLoading = false
        instance.title = deleteDialogTitle.value
        instance.showCancelButton = deleteDialogKind.value === 'confirm'
        instance.confirmButtonText = deleteDialogKind.value === 'notice'
          ? '知道了' : deleteDialogError.value ? '重试删除' : '确认删除'
      })
    },
  })
  deleteMessageBox = box

  void box.result.then(() => {
    if (deleteMessageBox === box) {
      dismissDeleteDialog()
    }
  }).catch((error: unknown) => {
    if (deleteMessageBox === box) {
      dismissDeleteDialog()
      showError(error instanceof Error ? error.message : '无法显示删除提醒，请重试')
    }
  })
}

// 清理上层弹窗，并将焦点返回规格管理
function dismissDeleteDialog(): void {
  closeDeleteMessageBox()
  const returnFocus: HTMLElement | null = deleteReturnFocus
  const productId: number = props.productId

  pendingDelete.value = null
  deleteDialogKind.value = null
  deleteDialogMessage.value = ''
  deleteDialogError.value = ''
  deleteReturnFocus = null

  void nextTick(() => {
    if (
      !isComponentActive ||
      !props.modelValue ||
      props.productId !== productId ||
      deleteDialogKind.value !== null
    ) {
      return
    }

    if (returnFocus?.isConnected) {
      returnFocus.focus()
    } else {
      specificationListHeading.value?.focus()
    }
  })
}

// 展示不能删除的原因，仅提供关闭提示的操作
function showDeleteNotice(message: string): void {
  pendingDelete.value = null
  openDeleteDialog('notice', message)
}

// 保存待删除对象，并打开二次确认弹窗
function beginDelete(target: DeleteTarget): void {
  pendingDelete.value = target
  openDeleteDialog('confirm', `确定删除${target.label}吗？删除后无法恢复。`)
}

// 检查规格是否已清空，允许删除时进入确认
function prepareDeleteSpecification(
  specification: ProductSpecificationResponse,
): void {
  if (!canSubmit()) {
    return
  }

  // 有规格值时展示名称，要求管理员先逐个清空
  if (specification.values.length > 0) {
    const valueNames: string = specification.values
      .slice(0, 3)
      .map((item: ProductSpecificationValueResponse) => `“${item.value}”`)
      .join('、')

    const suffix: string = specification.values.length > 3 ? '等' : ''

    showDeleteNotice(
      `规格“${specification.name}”下还有${valueNames}${suffix}，请先删除规格值`,
    )
    return
  }

  // 创建待确认的规格删除对象
  beginDelete({
    kind: 'specification',
    specificationId: specification.id,
    label: `规格“${specification.name}”`,
  })
}

// 为单个规格值显示删除确认，SKU 引用情况由后端检查
function prepareDeleteValue(
  specification: ProductSpecificationResponse,
  value: ProductSpecificationValueResponse,
): void {
  if (!canSubmit()) {
    return
  }

  // 创建待确认的规格值删除对象
  beginDelete({
    kind: 'value',
    specificationId: specification.id,
    valueId: value.id,
    label: `“${specification.name}”下的规格值“${value.value}”`,
  })
}

// 管理员确认后执行删除
async function confirmDelete(): Promise<void> {
  const target: DeleteTarget | null = pendingDelete.value

  if (
    !props.modelValue ||
    isBusy.value ||
    loadError.value !== '' ||
    deleteDialogKind.value !== 'confirm' ||
    target === null
  ) {
    return
  }

  // 找到当前列表中的所属规格
  const specification: ProductSpecificationResponse | undefined =
    specifications.value.find(
      (item: ProductSpecificationResponse) =>
        item.id === target.specificationId,
    )

  if (specification === undefined) {
    showDeleteNotice('当前规格已不存在，请关闭规格管理后重新打开')
    return
  }

  const productId: number = props.productId
  const version: number = ++requestVersion

  deleteDialogError.value = ''
  savingKind.value =
    target.kind === 'specification' ? 'delete-specification' : 'delete-value'

  try {
    if (target.kind === 'specification') {
      await deleteAdminProductSpecification(
        productId,
        target.specificationId,
        authStore.accessToken,
      )

      // 弹窗已关闭、商品已切换或组件已卸载时，不更新界面
      if (!isCurrentRequest(version, productId)) {
        return
      }

      // 删除成功后移除当前规格
      specifications.value = specifications.value.filter(
        (item: ProductSpecificationResponse) =>
          item.id !== target.specificationId,
      )

      // 删除了当前选中的规格时，重新选择并清空对应输入
      if (selectedSpecificationId.value === target.specificationId) {
        selectedSpecificationId.value = specifications.value[0]?.id ?? null
        resetForms()
      }

      isCreatingSpecification.value = specifications.value.length === 0
      if (isCreatingSpecification.value) {
        resetForms()
      } else if (selectedSpecificationId.value !== null) {
        revealSpecification(selectedSpecificationId.value)
      }
    } else {
      await deleteAdminProductSpecificationValue(
        productId,
        target.specificationId,
        target.valueId,
        authStore.accessToken,
      )

      if (!isCurrentRequest(version, productId)) {
        return
      }

      // 删除成功后只移除对应规格值
      specification.values = specification.values.filter(
        (item: ProductSpecificationValueResponse) => item.id !== target.valueId,
      )
    }

    dismissDeleteDialog()
  } catch (error: unknown) {
    if (!isCurrentRequest(version, productId)) {
      return
    }

    // 失败原因直接显示在上层弹窗，保留数据、输入和重试对象
    deleteDialogError.value =
      error instanceof Error ? error.message : '删除失败，请稍后重试'
  } finally {
    if (isCurrentRequest(version, productId)) {
      savingKind.value = ''
    }
  }
}

// 转发共用弹窗的关闭事件
function handleClosed(): void {
  if (savingKind.value === '' && deleteDialogKind.value === null) {
    emit('update:modelValue', false)
  }
}

// 只重置右侧列表，左侧列表的滚动位置保留
watch(
  () => [selectedSpecificationId.value, isCreatingSpecification.value] as const,
  resetValueScroll,
  { flush: 'post' },
)

// 每次打开或切换商品时清理旧状态，重新查询
watch(
  () => [
    props.modelValue,
    props.productId,
    authStore.sessionId,
    authStore.isAuthenticated,
  ] as const,
  ([visible]) => {
    resetState()

    if (visible && authStore.isAuthenticated) {
      void loadSpecifications()
    }
  },
  { immediate: true, flush: 'sync' },
)

// 离开页面后，丢弃尚未完成的查询或保存结果
onBeforeUnmount(() => {
  isComponentActive = false
  requestVersion += 1
  closeDeleteMessageBox()
})
</script>

<template>
  <AdminDialog
    v-model="dialogVisible"
    class="specification-manager-dialog"
    width="960px"
    :close-disabled="savingKind !== '' || deleteDialogKind !== null"
    title="规格管理"
    :description="props.productName"
    close-text="关闭"
    @closed="handleClosed"
  >
    <div class="specification-manager" :aria-busy="isBusy">
      <p v-if="isLoading" class="state-message" role="status">正在加载商品规格…</p>

      <div v-else-if="loadError" class="state-message">
        <p class="error-text" role="alert">{{ loadError }}</p>
        <button class="button" type="button" @click="loadSpecifications">重新加载</button>
      </div>

      <div v-else class="manager-layout">
        <!-- 左侧只选择规格，不混入规格值编辑 -->
        <section class="specification-pane" :aria-labelledby="`${componentId}-list-title`">
          <header class="pane-heading">
            <div class="pane-title">
              <h3
                :id="`${componentId}-list-title`"
                ref="specificationListHeading"
                tabindex="-1"
              >
                规格列表
              </h3>
              <span class="item-count">{{ specifications.length }}</span>
            </div>
            <button
              class="button create-specification-button"
              type="button"
              :disabled="isInteractionDisabled || isCreatingSpecification"
              @click="startCreateSpecification"
            >
              新增规格
            </button>
          </header>

          <div
            ref="specificationScroll"
            class="specification-scroll"
            role="region"
            aria-label="规格列表，可独立滚动"
            tabindex="0"
          >
            <ul v-if="specifications.length > 0" class="specification-list">
              <li v-for="specification in specifications" :key="specification.id">
                <button
                  class="specification-item"
                  :data-specification-id="specification.id"
                  :class="{
                    'is-selected':
                      !isCreatingSpecification && selectedSpecificationId === specification.id,
                  }"
                  type="button"
                  :aria-pressed="
                    !isCreatingSpecification && selectedSpecificationId === specification.id
                  "
                  :disabled="isInteractionDisabled"
                  @click="selectSpecification(specification.id)"
                >
                  <span class="item-heading">
                    <span class="item-name">{{ specification.name }}</span>
                    <span class="item-count">{{ specification.values.length }} 个值</span>
                  </span>
                  <span class="item-order">规格排序：{{ specification.sortOrder }}</span>
                </button>
              </li>
            </ul>
  
            <p v-else class="empty-list">暂无规格</p>
          </div>
        </section>

        <!-- 右侧一次只显示新增规格或当前规格的值 -->
        <section class="detail-pane" :aria-labelledby="`${componentId}-detail-title`">
          <template v-if="isCreatingSpecification">
            <header class="detail-heading">
              <div>
                <h3 :id="`${componentId}-detail-title`">新增规格</h3>
                <p class="section-description">规格名称例如“颜色”“容量”，具体选项在保存后添加。</p>
              </div>
            </header>

            <form class="specification-form" novalidate @submit.prevent="handleCreateSpecification">
              <fieldset class="specification-fields" :disabled="isInteractionDisabled">
                <div class="form-field">
                  <label :for="`${componentId}-name`">规格名称</label>
                  <input
                    :id="`${componentId}-name`"
                    ref="specificationNameInput"
                    v-model="specificationForm.name"
                    type="text"
                    maxlength="50"
                    placeholder="例如：颜色、容量"
                  />
                </div>

                <div class="form-field order-field">
                  <label :for="`${componentId}-sort`">规格排序</label>
                  <input
                    :id="`${componentId}-sort`"
                    v-model.number="specificationForm.sortOrder"
                    type="number"
                    min="0"
                    max="2147483647"
                    step="1"
                  />
                </div>

                <p v-if="formError" class="form-error" role="alert">{{ formError }}</p>

                <div class="form-actions">
                  <button
                    v-if="specifications.length > 0"
                    class="button"
                    type="button"
                    @click="cancelCreateSpecification"
                  >
                    取消
                  </button>
                  <button class="button primary-button" type="submit">
                    {{ savingKind === 'specification' ? '正在保存…' : '保存规格' }}
                  </button>
                </div>
              </fieldset>
            </form>
          </template>

          <template v-else-if="selectedSpecification">
            <header class="detail-heading">
              <h3 :id="`${componentId}-detail-title`">
                {{ selectedSpecification.name }}的规格值
              </h3>
              <button
                class="delete-button"
                type="button"
                :disabled="isInteractionDisabled"
                @click="prepareDeleteSpecification(selectedSpecification)"
              >
                删除当前规格
              </button>
            </header>

            <form class="value-form" novalidate @submit.prevent="handleCreateValue">
              <fieldset class="value-form-grid" :disabled="isInteractionDisabled">
                <div class="form-field value-input-field">
                  <label :for="`${componentId}-value`">新增规格值</label>
                  <input
                    :id="`${componentId}-value`"
                    ref="specificationValueInput"
                    v-model="valueForm.value"
                    type="text"
                    maxlength="100"
                    placeholder="请输入一个规格值"
                  />
                </div>
                <div class="form-field">
                  <label :for="`${componentId}-value-sort`">值排序</label>
                  <input
                    :id="`${componentId}-value-sort`"
                    v-model.number="valueForm.sortOrder"
                    type="number"
                    min="0"
                    max="2147483647"
                    step="1"
                  />
                </div>
                <button class="button primary-button" type="submit">
                  {{ savingKind === 'value' ? '正在保存…' : '添加' }}
                </button>
              </fieldset>
              <p v-if="formError" class="form-error" role="alert">{{ formError }}</p>
            </form>

            <div
              ref="valueScroll"
              class="table-wrapper"
              role="region"
              :aria-label="selectedSpecification.name + '的规格值列表'"
              tabindex="0"
            >
              <table class="value-table">
                <thead>
                  <tr>
                    <th scope="col">规格值</th>
                    <th scope="col" class="order-column">排序</th>
                    <th scope="col" class="action-column">操作</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-if="selectedSpecification.values.length === 0">
                    <td colspan="3" class="empty-cell">暂无规格值，请在上方添加。</td>
                  </tr>
                  <tr
                    v-for="item in selectedSpecification.values"
                    :key="item.id"
                    :data-value-id="item.id"
                  >
                    <td class="value-content">{{ item.value }}</td>
                    <td>{{ item.sortOrder }}</td>
                    <td>
                      <button
                        class="delete-button"
                        type="button"
                        :aria-label="'删除' + selectedSpecification.name + '规格值：' + item.value"
                        :disabled="isInteractionDisabled"
                        @click="prepareDeleteValue(selectedSpecification, item)"
                      >
                        删除
                      </button>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </template>

          <div v-else class="state-message">
            <h3 :id="`${componentId}-detail-title`">请选择左侧规格</h3>
          </div>
        </section>
      </div>
    </div>
  </AdminDialog>

</template>

<style scoped>
.specification-manager {
  display: flex;
  flex: 1;
  min-width: 0;
  min-height: 0;
  flex-direction: column;
  color: #334155;
  font-size: 13px;
}

/* 两个独立 box，列表只在各自的剩余空间内滚动 */
.manager-layout {
  display: grid;
  flex: 1;
  grid-template-columns: 260px minmax(0, 1fr);
  gap: 16px;
  min-height: 0;
}

.specification-pane,
.detail-pane {
  display: flex;
  min-width: 0;
  min-height: 0;
  flex-direction: column;
  overflow: hidden;
  border: 1px solid #dfe5ed;
  border-radius: 8px;
  background: #ffffff;
}

.specification-scroll {
  flex: 1;
  min-height: 0;
  padding: 12px;
  overflow: auto;
  overscroll-behavior: contain;
  scrollbar-gutter: stable;
}

.pane-heading,
.detail-heading,
.item-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.pane-heading,
.detail-heading {
  flex-shrink: 0;
  margin: 0;
  padding: 14px 16px;
  border-bottom: 1px solid #e3e8ef;
}

.pane-title {
  display: flex;
  min-width: 0;
  align-items: center;
  gap: 8px;
}

.detail-heading {
  flex-wrap: wrap;
  align-items: flex-start;
  margin-bottom: 0;
}

.pane-heading h3,
.detail-heading h3,
.state-message h3 {
  min-width: 0;
  margin: 0;
  color: #29384e;
  font-size: 15px;
  font-weight: 600;
  overflow-wrap: anywhere;
}

.section-description {
  margin: 9px 0 0;
  color: #7b8798;
  line-height: 1.7;
}

/* 用选中状态明确右侧操作归属 */
.specification-list {
  display: grid;
  gap: 8px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.specification-item {
  width: 100%;
  padding: 12px;
  border: 1px solid #e0e6ee;
  border-radius: 6px;
  background: #ffffff;
  color: inherit;
  font: inherit;
  text-align: left;
}

.specification-item:hover:not(:disabled) {
  border-color: #a9bfe5;
}

.specification-item.is-selected {
  border-color: #477ad1;
  background: #edf3ff;
  box-shadow: inset 3px 0 #477ad1;
}

.item-name {
  min-width: 0;
  font-weight: 600;
  overflow-wrap: anywhere;
}

.item-count {
  flex-shrink: 0;
  color: #7b8798;
  font-size: 12px;
  white-space: nowrap;
}

.item-order {
  display: block;
  margin-top: 8px;
  color: #7b8798;
  font-size: 12px;
}

.create-specification-button {
  flex-shrink: 0;
  min-height: 32px;
  padding: 6px 10px;
  font-size: 12px;
}

.empty-list {
  margin: 0;
  padding: 24px 0;
  color: #7b8798;
  text-align: center;
}

/* 右侧规格值始终按表格展示 */
.table-wrapper {
  flex: 1;
  min-height: 0;
  overflow: auto;
  overscroll-behavior: contain;
  scrollbar-gutter: stable;
}

.value-table {
  width: 100%;
  min-width: 280px;
  border-collapse: collapse;
  table-layout: fixed;
  text-align: left;
}

.value-table th,
.value-table td {
  padding: 11px 12px;
  border-bottom: 1px solid #e8edf3;
  vertical-align: top;
}

.value-table th {
  position: sticky;
  z-index: 1;
  top: 0;
  background: #f7f9fc;
  color: #64748b;
  font-weight: 500;
}

.value-table tbody tr:last-child td {
  border-bottom: none;
}

.order-column,
.action-column {
  width: 60px;
}

.value-content {
  overflow-wrap: anywhere;
}

.value-table .empty-cell {
  padding: 28px 12px;
  color: #7b8798;
  text-align: center;
}

/* 当前右侧区域只有一个新增表单 */
.specification-fields,
.value-form-grid {
  min-width: 0;
  margin: 0;
  padding: 0;
  border: none;
}

.specification-fields {
  display: grid;
  gap: 18px;
  max-width: 480px;
}

.specification-form {
  flex: 1;
  min-height: 0;
  padding: 16px;
  overflow: auto;
  overscroll-behavior: contain;
}

.value-form {
  flex-shrink: 0;
  margin: 0;
  padding: 14px 16px;
  border-bottom: 1px solid #e3e8ef;
}

.value-form-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 90px auto;
  align-items: end;
  gap: 12px;
}

.form-field {
  display: flex;
  min-width: 0;
  flex-direction: column;
  gap: 8px;
}

.form-field label {
  color: #536176;
}

.form-field input {
  box-sizing: border-box;
  width: 100%;
  min-width: 0;
  min-height: 38px;
  padding: 8px 10px;
  border: 1px solid #cfd8e5;
  border-radius: 5px;
  background: #ffffff;
  color: #334155;
  font: inherit;
}

.form-field input::placeholder {
  color: #9aa5b5;
}

.form-field input:disabled {
  background: #f7f9fc;
}

.order-field {
  max-width: 120px;
}

.form-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 10px;
}

.form-error {
  margin: 12px 0 0;
  color: #b33f3f;
  line-height: 1.7;
  overflow-wrap: anywhere;
}

.specification-fields .form-error {
  margin-top: 0;
}

/* 普通操作与删除操作保持明确区分 */
.button {
  min-height: 38px;
  padding: 8px 14px;
  border: 1px solid #cfd8e5;
  border-radius: 5px;
  background: #ffffff;
  color: #536176;
  font: inherit;
  white-space: nowrap;
}

.button:hover:not(:disabled) {
  border-color: #a9bfe5;
  background: #f5f8fd;
}

.primary-button {
  border-color: #2864e8;
  background: #2864e8;
  color: #ffffff;
}

.primary-button:hover:not(:disabled) {
  border-color: #1e55d0;
  background: #1e55d0;
}

.delete-button {
  flex-shrink: 0;
  padding: 2px 0;
  border: none;
  background: transparent;
  color: #b33f3f;
  font: inherit;
  white-space: nowrap;
}

.delete-button:hover:not(:disabled) {
  color: #8e3030;
  text-decoration: underline;
}

.button:disabled,
.delete-button:disabled,
.specification-item:disabled {
  cursor: default;
  opacity: 0.55;
}

.form-field input:focus,
.button:focus-visible,
.delete-button:focus-visible,
.specification-item:focus-visible,
.table-wrapper:focus-visible,
.specification-scroll:focus-visible {
  outline: 2px solid #91b3ef;
  outline-offset: 2px;
}

/* 独立删除提醒 */
.state-message {
  margin: 0;
  padding: 36px 0;
  color: #7b8798;
  text-align: center;
}

.error-text {
  margin: 0 0 14px;
  color: #b33f3f;
}

.delete-dialog-message {
  margin: 0;
  color: #52617a;
  line-height: 1.8;
  overflow-wrap: anywhere;
}

.delete-dialog-error {
  margin: 16px 0 0;
  padding: 10px 12px;
  border: 1px solid #f0d4d4;
  border-radius: 6px;
  background: #fff8f8;
  color: #b33f3f;
  line-height: 1.7;
  overflow-wrap: anywhere;
}

.danger-button {
  border-color: #b33f3f;
  background: #b33f3f;
  color: #ffffff;
}

.danger-button:hover:not(:disabled) {
  border-color: #922f2f;
  background: #922f2f;
}

/* 窄屏上方选规格，值区域使用剩余空间 */
@media (max-width: 760px) {
  .manager-layout {
    grid-template-columns: minmax(0, 1fr);
    grid-template-rows: minmax(112px, 30%) minmax(0, 1fr);
    gap: 12px;
  }

  .specification-list {
    grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  }
}

@media (max-width: 480px) {
  .value-form-grid {
    grid-template-columns: minmax(0, 1fr) 90px;
    gap: 10px;
  }

  .value-input-field {
    grid-column: 1 / -1;
  }

  .value-form-grid .button {
    width: 100%;
  }
}

/* 极低窗口允许外层滚动，避免固定操作区被裁掉 */
@media (max-height: 520px), (max-width: 760px) and (max-height: 700px) {
  .manager-layout {
    height: 560px;
    min-height: 560px;
  }
}

@media (max-width: 760px) and (max-height: 700px) {
  .manager-layout {
    grid-template-rows: 160px minmax(0, 1fr);
  }
}
</style>
