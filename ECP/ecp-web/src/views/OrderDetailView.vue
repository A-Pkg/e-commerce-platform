<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import {
  cancelOrder,
  confirmOrderReceipt,
  type ConfirmReceiptResult,
  getOrderDetail,
  prepareOrderPayment,
  queryOrderPayment,
  type CancelOrderResponse,
  type PrepareOrderPaymentResponse,
  type QueryOrderPaymentResponse,
  OrderUnauthorizedError,
  type OrderDetailResponse,
  type OrderStatus,
} from '@/api/orders'
import StoreHeader from '@/components/layout/StoreHeader.vue'
import { useAuthStore } from '@/stores/auth'
import { openOwnedMessageBox, type OwnedMessageBox } from '@/utils/ownedMessageBox'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()

// 当前订单及查询状态
const order = ref<OrderDetailResponse | null>(null)
const isLoading = ref<boolean>(false)
const loadError = ref<string>('')

// 付款与查询共用操作状态，避免重复提交或与其他订单操作交错
const paymentAction = ref<'prepare' | 'query' | null>(null)
const paymentMessage = ref<string>('')
const paymentError = ref<boolean>(false)
// 确认框打开时即锁定操作，确认后切换为请求状态
const cancelAction = ref<'confirm' | 'request' | null>(null)
const cancelMessage = ref<string>('')
const cancelError = ref<boolean>(false)

// 当前详情页拥有的取消确认框
let cancelMessageBox: OwnedMessageBox | null = null

// 收货确认与页面其他操作互斥
const receiptAction = ref<'confirm' | 'request' | null>(null)
const receiptMessage = ref<string>('')
const receiptError = ref<boolean>(false)
let receiptMessageBox: OwnedMessageBox | null = null

// 刷新、付款、查询、取消和确认收货互斥，避免重复操作
const isBusy = computed<boolean>(
  () =>
    isLoading.value ||
    paymentAction.value !== null ||
    cancelAction.value !== null ||
    receiptAction.value !== null,
)

// 状态文案对应后端保存的订单状态
const statusLabels: Record<OrderStatus, string> = {
  PendingPayment: '待支付',
  Paid: '待发货',
  Shipped: '待收货',
  Completed: '已完成',
  Cancelled: '已取消',
}

const statusText = computed<string>(() =>
  order.value ? statusLabels[order.value.status] : '',
)

// 页面离开、订单编号变化或会话变化后，丢弃旧请求结果
let isPageActive: boolean = true
let requestVersion: number = 0

// 仅保存尚未提交支付表单的等待窗口
let waitingWindow: Window | null = null

// 请求失败、会话切换和离页时清理等待窗口
function closeWaitingWindow(): void {
  if (waitingWindow && !waitingWindow.closed) {
    waitingWindow.close()
  }

  waitingWindow = null
}

// 确认响应仍属于当前页面和原登录会话
function isCurrentRequest(version: number, accessToken: string): boolean {
  return (
    isPageActive &&
    version === requestVersion &&
    authStore.isAuthenticated &&
    authStore.isCurrentSession(accessToken)
  )
}

// 将后端 UTC 时间转换为浏览器本地时间
function formatTime(value: string): string {
  return new Date(value).toLocaleString('zh-CN', {
    hour12: false,
  })
}

// 查询本人订单详情，金额和状态均使用后端返回值
async function loadOrder(): Promise<void> {
  if (!isPageActive || isBusy.value || !authStore.isAuthenticated) {
    return
  }

  const id: number = Number(route.params.id)

  if (!Number.isInteger(id) || id <= 0) {
    loadError.value = '订单编号无效'
    order.value = null
    return
  }

  const accessToken: string = authStore.accessToken
  const version: number = ++requestVersion

  isLoading.value = true
  loadError.value = ''
  receiptMessage.value = ''
  receiptError.value = false
  paymentMessage.value = ''
  paymentError.value = false
  cancelMessage.value = ''
  cancelError.value = false
  order.value = null

  try {
    const result: OrderDetailResponse = await getOrderDetail(id, accessToken)

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    order.value = result
  } catch (error: unknown) {
    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    if (error instanceof OrderUnauthorizedError) {
      authStore.clearSession(true)
      authStore.sessionExpired = true
      return
    }

    loadError.value =
      error instanceof Error ? error.message : '获取订单详情失败，请稍后重试'
  } finally {
    if (isCurrentRequest(version, accessToken)) {
      isLoading.value = false
    }
  }
}

// 准备付款与查询结果共用操作锁、会话校验和错误处理
async function handlePayment(action: 'prepare' | 'query'): Promise<void> {
  if (
    !isPageActive ||
    isBusy.value ||
    !authStore.isAuthenticated ||
    !order.value ||
    order.value.status !== 'PendingPayment'
  ) {
    return
  }

  const currentOrder: OrderDetailResponse = order.value
  const accessToken: string = authStore.accessToken
  const version: number = requestVersion

  receiptMessage.value = ''
  receiptError.value = false
  paymentAction.value = action
  paymentMessage.value = ''
  paymentError.value = false
  cancelMessage.value = ''
  cancelError.value = false

  try {
    // 在点击事件内同步开窗，避免等待接口后被浏览器拦截；零元订单无需开窗
    if (action === 'prepare' && currentOrder.payableAmount > 0) {
      waitingWindow = window.open('', '_blank')

      if (!waitingWindow) {
        paymentMessage.value = '付款窗口被浏览器拦截，请允许本网站打开弹出窗口后重试'
        paymentError.value = true
        return
      }

      // 收银台无需访问原页面，等待期间仅显示文本
      waitingWindow.opener = null
      waitingWindow.document.title = '正在打开支付宝收银台'
      waitingWindow.document.body.textContent = '正在准备付款，请稍候…'
    }

    const result: PrepareOrderPaymentResponse | QueryOrderPaymentResponse =
      action === 'prepare'
        ? await prepareOrderPayment(currentOrder.id, accessToken)
        : await queryOrderPayment(currentOrder.id, accessToken)

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    // 只使用后端已确认的状态，打开或关闭收银台本身不代表付款成功
    currentOrder.status = result.orderStatus
    currentOrder.paidAtUtc = result.paidAtUtc

    if (result.paymentStatus === 'Succeeded') {
      closeWaitingWindow()
      paymentMessage.value = '付款成功'
      return
    }

    if ('message' in result) {
      paymentMessage.value = result.message
      return
    }

    if (!result.paymentForm) {
      throw new Error('未取得收银台表单，请查询支付结果后重试')
    }

    if (!waitingWindow || waitingWindow.closed) {
      throw new Error('付款窗口已关闭，请重新点击立即付款；如已完成付款，请查询支付结果')
    }

    // 只复制后端 SDK 表单的地址和具名参数，不执行返回 HTML 中的脚本
    const paymentDocument: Document = new DOMParser().parseFromString(
      result.paymentForm,
      'text/html',
    )
    const sourceForm: HTMLFormElement | null = paymentDocument.querySelector('form')

    if (!sourceForm || !sourceForm.getAttribute('action')) {
      throw new Error('收银台表单无法读取，请查询支付结果后重试')
    }

    const cashierDocument: Document = waitingWindow.document
    const form: HTMLFormElement = cashierDocument.createElement('form')
    // SDK 包含名为 method 的参数，读取属性以避免同名控件遮蔽表单属性
    form.method = sourceForm.getAttribute('method') ?? 'post'
    form.action = sourceForm.getAttribute('action')!
    form.acceptCharset = 'utf-8'
    form.target = '_self'

    // SDK 参数 input 没有 type 属性；具名参数均复制为隐藏字段，保留原值
    for (const field of sourceForm.querySelectorAll<HTMLInputElement>('input[name]')) {
      const input: HTMLInputElement = cashierDocument.createElement('input')
      input.type = 'hidden'
      input.name = field.name
      input.value = field.value
      form.append(input)
    }

    cashierDocument.body.replaceChildren(form)
    form.submit()

    // 表单提交后由用户操作收银台；本页只持有尚未提交表单的等待窗口
    waitingWindow = null
    paymentMessage.value = '已打开支付宝收银台，请完成付款后回到本页点击“查询支付结果”'
  } catch (error: unknown) {
    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    closeWaitingWindow()

    if (error instanceof OrderUnauthorizedError) {
      authStore.clearSession(true)
      authStore.sessionExpired = true
      return
    }

    paymentError.value = true
    paymentMessage.value =
      error instanceof Error ? error.message : '暂时无法确认付款结果，请稍后查询支付结果'
  } finally {
    if (isCurrentRequest(version, accessToken)) {
      paymentAction.value = null
    }
  }
}

// 只关闭当前详情页拥有的取消确认框
function closeCancelConfirmation(): void {
  const box: OwnedMessageBox | null = cancelMessageBox
  cancelMessageBox = null
  box?.close()
}

// 确认后取消订单，并重新读取后端保存的状态和时间
async function handleCancelOrder(): Promise<void> {
  if (
    !isPageActive ||
    isBusy.value ||
    !authStore.isAuthenticated ||
    !order.value ||
    order.value.status !== 'PendingPayment'
  ) {
    return
  }

  const currentOrder: OrderDetailResponse = order.value
  const accessToken: string = authStore.accessToken
  const version: number = requestVersion

  // 操作结束后恢复焦点，避开按钮仍被禁用的阶段
  const trigger: HTMLElement | null =
    document.activeElement instanceof HTMLElement ? document.activeElement : null

  let box: OwnedMessageBox | null = null
  let result: CancelOrderResponse | null = null

  receiptMessage.value = ''
  receiptError.value = false
  cancelAction.value = 'confirm'
  cancelMessage.value = ''
  cancelError.value = false
  paymentMessage.value = ''
  paymentError.value = false

  try {
    box = openOwnedMessageBox({
      title: '取消订单',
      message:
        '确定取消订单“' +
        currentOrder.orderNo +
        '”吗？取消成功后会归还库存，商品不会自动放回购物车。',
      type: 'warning',
      showCancelButton: true,
      confirmButtonText: '确认取消订单',
      cancelButtonText: '暂不取消',
    })

    cancelMessageBox = box
    const confirmed: boolean = await box.result

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    cancelMessageBox = null

    // 用户放弃，或当前订单已变化时，不发送取消请求
    if (
      !confirmed ||
      order.value?.id !== currentOrder.id ||
      order.value.status !== 'PendingPayment'
    ) {
      return
    }

    cancelAction.value = 'request'
    result = await cancelOrder(currentOrder.id, accessToken)

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    cancelMessage.value = result.message
    cancelError.value = result.outcome === 'conflict'

    // 后端已经确认取消，即使随后刷新失败，也不再显示付款按钮
    // 取消时间仍等待详情接口返回，不使用前端时间代替
    if (result.outcome === 'cancelled') {
      currentOrder.status = 'Cancelled'
    }

    // 在取消操作锁内刷新详情，保留本次结果提示
    const detail: OrderDetailResponse = await getOrderDetail(
      currentOrder.id,
      accessToken,
    )

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    order.value = detail
  } catch (error: unknown) {
    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    if (error instanceof OrderUnauthorizedError) {
      authStore.clearSession(true)
      authStore.sessionExpired = true
      return
    }

    const message: string =
      error instanceof Error ? error.message : '操作失败，请刷新订单后重试'

    cancelError.value = true

    // 区分取消请求失败和已取得取消结果后的详情刷新失败
    cancelMessage.value =
      result === null
        ? message
        : result.message + '；订单详情刷新失败：' + message
  } finally {
    if (box !== null && cancelMessageBox === box) {
      closeCancelConfirmation()
    }

    if (isCurrentRequest(version, accessToken)) {
      cancelAction.value = null

      await nextTick()

      if (
        isCurrentRequest(version, accessToken) &&
        trigger?.isConnected &&
        !trigger.matches(':disabled')
      ) {
        trigger.focus({ preventScroll: true })
      }
    }
  }
}

// 只关闭当前页面拥有的收货确认框
function closeReceiptConfirmation(): void {
  const box: OwnedMessageBox | null = receiptMessageBox
  receiptMessageBox = null
  box?.close()
}

// 确认收到全部商品后，提交本人订单的确认收货请求
async function handleConfirmReceipt(): Promise<void> {
  const currentOrder: OrderDetailResponse | null = order.value

  if (
    !isPageActive ||
    isBusy.value ||
    !authStore.isAuthenticated ||
    !currentOrder ||
    currentOrder.status !== 'Shipped'
  ) {
    return
  }

  const accessToken: string = authStore.accessToken
  const version: number = requestVersion
  const trigger: HTMLElement | null =
    document.activeElement instanceof HTMLElement ? document.activeElement : null

  let box: OwnedMessageBox | null = null
  let result: ConfirmReceiptResult | null = null

  receiptAction.value = 'confirm'
  receiptMessage.value = ''
  receiptError.value = false
  cancelMessage.value = ''
  cancelError.value = false
  paymentMessage.value = ''
  paymentError.value = false

  try {
    box = openOwnedMessageBox({
      title: '确认收货',
      message: '请确认已收到订单“' + currentOrder.orderNo + '”中的全部商品，确认后订单将完成。',
      type: 'warning',
      showCancelButton: true,
      confirmButtonText: '确认已收货',
      cancelButtonText: '暂不确认',
    })

    receiptMessageBox = box
    const confirmed: boolean = await box.result

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    receiptMessageBox = null

    // 确认后再次核对当前订单，取消确认时不发送请求
    if (!confirmed || (order.value?.id !== currentOrder.id || order.value.status !== 'Shipped')) {
      return
    }

    receiptAction.value = 'request'
    result = await confirmOrderReceipt(currentOrder.id, accessToken)

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    receiptMessage.value = result.message
    receiptError.value = result.outcome === 'conflict'

    // 后端确认完成后隐藏收货按钮；完成时间等待详情接口返回
    if (result.outcome === 'completed') {
      currentOrder.status = 'Completed'
    }

    // 在收货操作锁内刷新数据，并保留本次结果提示
    const detail: OrderDetailResponse = await getOrderDetail(currentOrder.id, accessToken)

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    order.value = detail
  } catch (error: unknown) {
    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    if (error instanceof OrderUnauthorizedError) {
      authStore.clearSession(true)
      authStore.sessionExpired = true
      return
    }

    const message: string =
      error instanceof Error ? error.message : '暂时无法确认收货结果，请刷新订单后确认'

    receiptError.value = true
    receiptMessage.value =
      result === null
        ? message
        : result.message + '；订单详情刷新失败：' + message
  } finally {
    if (box !== null && receiptMessageBox === box) {
      closeReceiptConfirmation()
    }

    if (isCurrentRequest(version, accessToken)) {
      receiptAction.value = null

      await nextTick()

      if (
        isCurrentRequest(version, accessToken) &&
        trigger?.isConnected &&
        !trigger.matches(':disabled')
      ) {
        trigger.focus({ preventScroll: true })
      }
    }
  }
}

// 首次进入、切换订单或更换会话时，清理旧数据后重新查询
watch(
  () => [route.params.id, authStore.sessionId, authStore.isAuthenticated] as const,
  () => {
    requestVersion += 1
    closeReceiptConfirmation()
    receiptAction.value = null
    receiptMessage.value = ''
    receiptError.value = false
    closeWaitingWindow()
    closeCancelConfirmation()
    cancelAction.value = null
    cancelMessage.value = ''
    cancelError.value = false
    paymentAction.value = null
    paymentMessage.value = ''
    paymentError.value = false
    order.value = null
    loadError.value = ''
    isLoading.value = false

    if (authStore.isAuthenticated) {
      void loadOrder()
    }
  },
  { immediate: true, flush: 'sync' },
)

// 等会话同步更新完成后，再处理退出登录
watch(
  () => authStore.isAuthenticated,
  authenticated => {
    if (!authenticated && isPageActive) {
      void router.replace({ name: 'login' })
    }
  },
  { flush: 'post' },
)

// 离页后丢弃旧响应，并关闭本页拥有的确认框和等待窗口
onBeforeUnmount(() => {
  isPageActive = false
  requestVersion += 1
  closeReceiptConfirmation()
  closeWaitingWindow()
  closeCancelConfirmation()
})
</script>

<template>
  <div class="order-page">
    <StoreHeader />

    <main class="order-content" :aria-busy="isBusy">
      <header class="page-heading">
        <h1>订单详情</h1>

        <div class="page-actions">
          <button
            class="refresh-button"
            type="button"
            :disabled="isBusy"
            @click="loadOrder"
          >
            {{ isLoading ? '加载中…' : '刷新订单' }}
          </button>

          <RouterLink :to="{ name: 'cart' }">返回购物车</RouterLink>
        </div>
      </header>

      <p v-if="isLoading" class="message-panel" role="status">正在加载订单…</p>

      <p v-else-if="loadError" class="message-panel error-message" role="alert">
        {{ loadError }}
      </p>

      <template v-else-if="order">
        <section class="order-panel" aria-label="订单信息">
          <div class="order-heading">
            <h2>订单信息</h2>
            <strong class="status-label">{{ statusText }}</strong>
          </div>

          <dl class="order-meta">
            <div>
              <dt>订单编号</dt>
              <dd>{{ order.orderNo }}</dd>
            </div>
            <div>
              <dt>下单时间</dt>
              <dd>{{ formatTime(order.createdAtUtc) }}</dd>
            </div>
            <div v-if="order.status === 'PendingPayment'">
              <dt>付款截止</dt>
              <dd>{{ formatTime(order.expiresAtUtc) }}</dd>
            </div>
            <div v-if="order.paidAtUtc">
              <dt>支付时间</dt>
              <dd>{{ formatTime(order.paidAtUtc) }}</dd>
            </div>
            <div v-if="order.shippedAtUtc">
              <dt>发货时间</dt>
              <dd>{{ formatTime(order.shippedAtUtc) }}</dd>
            </div>
            <div v-if="order.completedAtUtc">
              <dt>完成时间</dt>
              <dd>{{ formatTime(order.completedAtUtc) }}</dd>
            </div>
            <div v-if="order.cancelledAtUtc">
              <dt>取消时间</dt>
              <dd>{{ formatTime(order.cancelledAtUtc) }}</dd>
            </div>
          </dl>

          <p v-if="order.status === 'PendingPayment'" class="order-notice">
            超过付款截止时间后，系统会核对支付结果，确认可关闭后取消订单并归还库存。
          </p>
        </section>

        <section class="order-panel" aria-label="收货信息">
          <h2>收货信息</h2>
          <p class="recipient">
            <strong>{{ order.recipientName }}</strong>
            <span>{{ order.recipientPhone }}</span>
          </p>
          <p class="shipping-address">{{ order.shippingAddress }}</p>
        </section>

        <section class="order-panel" aria-label="订单商品">
          <h2>订单商品</h2>
          <ul class="order-items">
            <li v-for="item in order.items" :key="item.id" class="order-item">
              <div class="product-info">
                <h3>{{ item.productName }}</h3>
                <p>{{ item.specificationText || '默认规格' }}</p>
                <p>SKU：{{ item.skuCode }}</p>
              </div>
              <p class="item-number">
                <span>单价</span>
                <strong>¥{{ item.unitPrice.toFixed(2) }}</strong>
              </p>
              <p class="item-number">
                <span>数量</span>
                <strong>{{ item.quantity }}</strong>
              </p>
              <p class="item-number">
                <span>小计</span>
                <strong>¥{{ item.subtotal.toFixed(2) }}</strong>
              </p>
            </li>
          </ul>

          <footer class="order-total">
            <span class="shipping-note">
              {{
                order.shippingFee === 0
                  ? '免运费'
                  : '运费：¥' + order.shippingFee.toFixed(2)
              }}
            </span>
            <p class="payable-total">
              <span>应付金额</span>
              <strong>¥{{ order.payableAmount.toFixed(2) }}</strong>
            </p>
          </footer>

          <div v-if="order.status === 'PendingPayment'" class="payment-area">
            <div class="payment-actions">
              <button
                class="refresh-button"
                type="button"
                :disabled="isBusy"
                @click="handleCancelOrder"
              >
                {{ cancelAction === 'request' ? '正在取消…' : '取消订单' }}
              </button>
              <button
                class="pay-button"
                type="button"
                :disabled="isBusy"
                @click="handlePayment('prepare')"
              >
                {{ paymentAction === 'prepare' ? '正在准备付款…' : '立即付款' }}
              </button>
              <button
                class="refresh-button"
                type="button"
                :disabled="isBusy"
                @click="handlePayment('query')"
              >
                {{ paymentAction === 'query' ? '正在查询…' : '查询支付结果' }}
              </button>
            </div>
            <p class="payment-hint">
              {{
                order.payableAmount === 0
                  ? '零元订单点击立即付款后直接完成付款。'
                  : '支付宝收银台将在新窗口打开，完成付款后请回到本页查询支付结果。'
              }}
            </p>
          </div>
          <div v-if="order.status === 'Shipped'" class="payment-area">
            <div class="payment-actions">
              <button
                class="pay-button"
                type="button"
                :disabled="isBusy"
                @click="handleConfirmReceipt"
              >
                {{ receiptAction === 'request' ? '确认中…' : '确认收货' }}
              </button>
            </div>
          </div>
          <p
            v-if="receiptMessage"
            class="payment-message"
            :class="{ 'error-message': receiptError }"
            :role="receiptError ? 'alert' : 'status'"
          >
            {{ receiptMessage }}
          </p>
          <p
            v-if="paymentMessage"
            class="payment-message"
            :class="{ 'error-message': paymentError }"
            :role="paymentError ? 'alert' : 'status'"
          >
            {{ paymentMessage }}
          </p>
          <p
            v-if="cancelMessage"
            class="payment-message"
            :class="{ 'error-message': cancelError }"
            :role="cancelError ? 'alert' : 'status'"
          >
            {{ cancelMessage }}
          </p>
        </section>
      </template>
    </main>
  </div>
</template>

<style scoped>
.order-page {
  min-height: 100vh;
  background: #f4f6fa;
  color: #253248;
}

.order-content {
  box-sizing: border-box;
  max-width: 1120px;
  margin: 0 auto;
  padding: 28px 28px 48px;
}

.page-heading,
.page-actions,
.order-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
}

.page-heading {
  margin-bottom: 20px;
}

.page-heading h1 {
  margin: 0;
  font-size: 25px;
}

.page-actions a {
  color: #2864e8;
  text-decoration: none;
}

.page-actions a:hover {
  text-decoration: underline;
}

.refresh-button,
.pay-button {
  padding: 9px 16px;
  border: 1px solid #d7dfeb;
  border-radius: 6px;
  background: #fff;
  color: #253248;
  font: inherit;
  cursor: pointer;
}

.pay-button {
  border-color: #2864e8;
  background: #2864e8;
  color: #fff;
}

.pay-button:hover:not(:disabled) {
  background: #1b52cd;
}

.refresh-button:disabled,
.pay-button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.order-panel,
.message-panel {
  margin: 0 0 20px;
  padding: 24px;
  border: 1px solid #e5eaf2;
  border-radius: 12px;
  background: #fff;
}

.message-panel {
  padding: 48px 24px;
  text-align: center;
}

.error-message {
  color: #c33b3b;
}

.order-panel h2 {
  margin: 0;
  font-size: 18px;
}

.status-label {
  color: #2864e8;
}

.order-meta {
  display: grid;
  gap: 14px;
  margin: 22px 0 0;
}

.order-meta > div {
  display: grid;
  grid-template-columns: 80px minmax(0, 1fr);
  gap: 12px;
}

.order-meta dt,
.order-notice,
.product-info p,
.item-number > span,
.shipping-note {
  color: #738096;
}

.order-meta dd {
  margin: 0;
  overflow-wrap: anywhere;
}

.order-notice {
  margin: 20px 0 0;
  font-size: 14px;
}

.recipient {
  display: flex;
  gap: 16px;
  flex-wrap: wrap;
  margin: 20px 0 12px;
}

.shipping-address {
  margin: 0;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  line-height: 1.7;
}

.order-items {
  margin: 8px 0 0;
  padding: 0;
  list-style: none;
}

.order-item {
  display: grid;
  grid-template-columns: minmax(0, 1fr) repeat(3, minmax(80px, auto));
  align-items: center;
  gap: 24px;
  padding: 22px 0;
  border-bottom: 1px solid #edf0f5;
}

.product-info {
  min-width: 0;
  overflow-wrap: anywhere;
}

.product-info h3 {
  margin: 0 0 10px;
  font-size: 16px;
}

.product-info p {
  margin: 6px 0 0;
  font-size: 14px;
}

.item-number {
  display: grid;
  gap: 10px;
  margin: 0;
  text-align: right;
  font-size: 14px;
}

.order-total {
  display: flex;
  justify-content: flex-end;
  align-items: center;
  flex-wrap: wrap;
  gap: 24px;
  padding-top: 24px;
}

.payable-total {
  display: flex;
  align-items: baseline;
  gap: 12px;
  margin: 0;
}

.payable-total strong {
  color: #2864e8;
  font-size: 26px;
}

.payment-area {
  margin-top: 24px;
  padding-top: 24px;
  border-top: 1px solid #edf0f5;
}

.payment-actions {
  display: flex;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 12px;
}

.payment-hint,
.payment-message {
  margin: 14px 0 0;
  font-size: 14px;
  line-height: 1.7;
  overflow-wrap: anywhere;
}

.payment-hint {
  color: #738096;
}

@media (max-width: 700px) {
  .order-content {
    padding: 20px 16px 32px;
  }

  .order-panel {
    padding: 18px;
  }

  .order-item {
    grid-template-columns: repeat(3, minmax(0, 1fr));
    gap: 18px 12px;
  }

  .product-info {
    grid-column: 1 / -1;
  }

  .item-number {
    text-align: left;
    overflow-wrap: anywhere;
  }
}
</style>
