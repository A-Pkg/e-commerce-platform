<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import {
  cancelOrder,
  confirmOrderReceipt,
  type ConfirmReceiptResult,
  getOrders,
  type CancelOrderResponse,
  OrderUnauthorizedError,
  type OrderListItemResponse,
  type OrderStatus,
} from '@/api/orders'
import StoreHeader from '@/components/layout/StoreHeader.vue'
import { useAuthStore } from '@/stores/auth'
import { openOwnedMessageBox, type OwnedMessageBox } from '@/utils/ownedMessageBox'

const router = useRouter()
const authStore = useAuthStore()

// 本人订单列表及查询状态
const orders = ref<OrderListItemResponse[]>([])
const isLoading = ref<boolean>(false)
const loadError = ref<string>('')

// 列表共用取消操作锁，记录当前操作的订单和结果
const cancelAction = ref<'confirm' | 'request' | null>(null)
const cancelOrderId = ref<number | null>(null)
const cancelMessage = ref<string>('')
const cancelError = ref<boolean>(false)
// 收货与取消、刷新共用操作锁
const receiptAction = ref<'confirm' | 'request' | null>(null)
const receiptOrderId = ref<number | null>(null)
const receiptMessage = ref<string>('')
const receiptError = ref<boolean>(false)
let receiptMessageBox: OwnedMessageBox | null = null

const isBusy = computed<boolean>(
  () => isLoading.value || cancelAction.value !== null || receiptAction.value !== null,
)

// 仅保存当前列表页面拥有的确认框
let cancelMessageBox: OwnedMessageBox | null = null

// 本地时钟只用于切换按钮文案，不请求接口或修改订单状态
const currentTime = ref<number>(Date.now())
const clockTimer: ReturnType<typeof setInterval> = setInterval(() => {
  currentTime.value = Date.now()
}, 1000)

// 仅待支付且付款期限未到的订单显示去付款按钮
function canPayOrder(order: OrderListItemResponse): boolean {
  return (
    order.status === 'PendingPayment' &&
    Date.parse(order.expiresAtUtc) > currentTime.value
  )
}

// 与现有订单详情保持相同的状态文案
const statusLabels: Record<OrderStatus, string> = {
  PendingPayment: '待支付',
  Paid: '待发货',
  Shipped: '待收货',
  Completed: '已完成',
  Cancelled: '已取消',
}

// 页面离开或会话变化后，不再接收原请求结果
let isPageActive: boolean = true
let requestVersion: number = 0

// 确认响应仍属于当前页面、当前请求和原登录会话
function isCurrentRequest(version: number, accessToken: string): boolean {
  return (
    isPageActive &&
    version === requestVersion &&
    authStore.isAuthenticated &&
    authStore.isCurrentSession(accessToken)
  )
}

// 查询本人订单，顺序和金额均使用后端返回值
async function loadOrders(): Promise<void> {
  if (!isPageActive || isBusy.value || !authStore.isAuthenticated) {
    return
  }

  const accessToken: string = authStore.accessToken
  const version: number = ++requestVersion

  isLoading.value = true
  loadError.value = ''
  receiptOrderId.value = null
  receiptMessage.value = ''
  receiptError.value = false
  cancelOrderId.value = null
  cancelMessage.value = ''
  cancelError.value = false
  orders.value = []

  try {
    const result: OrderListItemResponse[] = await getOrders(accessToken)

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    orders.value = result
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
      error instanceof Error ? error.message : '获取订单列表失败，请稍后重试'
  } finally {
    if (isCurrentRequest(version, accessToken)) {
      isLoading.value = false
    }
  }
}

// 会话变化或离页时，只关闭本页面拥有的确认框
function closeCancelConfirmation(): void {
  const box: OwnedMessageBox | null = cancelMessageBox
  cancelMessageBox = null
  box?.close()
}

// 从列表直接取消待支付订单，确认结果后刷新列表
async function handleCancelOrder(id: number): Promise<void> {
  const currentOrder: OrderListItemResponse | undefined = orders.value.find(
    item => item.id === id,
  )

  if (
    !isPageActive ||
    isBusy.value ||
    !authStore.isAuthenticated ||
    !currentOrder ||
    currentOrder.status !== 'PendingPayment'
  ) {
    return
  }

  const accessToken: string = authStore.accessToken
  const version: number = requestVersion
  const trigger: HTMLElement | null =
    document.activeElement instanceof HTMLElement ? document.activeElement : null

  let box: OwnedMessageBox | null = null
  let result: CancelOrderResponse | null = null

  cancelAction.value = 'confirm'
  cancelOrderId.value = id
  cancelMessage.value = ''
  cancelError.value = false

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

    // 确认后再次核对条目仍在当前列表中且处于待支付状态
    if (
      !confirmed ||
      orders.value.find(item => item.id === id)?.status !== 'PendingPayment'
    ) {
      return
    }

    cancelAction.value = 'request'
    result = await cancelOrder(id, accessToken)

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    cancelMessage.value = result.message
    cancelError.value = result.outcome === 'conflict'

    // 仅在后端确认取消后更新状态，刷新失败时也不再显示去付款
    if (result.outcome === 'cancelled') {
      currentOrder.status = 'Cancelled'
    }

    // 保持取消操作锁和结果提示，直接刷新列表数据
    const refreshedOrders: OrderListItemResponse[] = await getOrders(accessToken)

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    orders.value = refreshedOrders
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
      error instanceof Error ? error.message : '操作失败，请刷新订单列表后重试'

    cancelError.value = true
    cancelMessage.value =
      result === null
        ? message
        : result.message + '；订单列表刷新失败：' + message
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
async function handleConfirmReceipt(id: number): Promise<void> {
  const currentOrder: OrderListItemResponse | undefined = orders.value.find(
    item => item.id === id,
  )

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
  receiptOrderId.value = currentOrder.id
  receiptMessage.value = ''
  receiptError.value = false
  cancelMessage.value = ''
  cancelError.value = false

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
    if (!confirmed || orders.value.find(item => item.id === currentOrder.id)?.status !== 'Shipped') {
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
    const refreshedOrders: OrderListItemResponse[] = await getOrders(accessToken)

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    orders.value = refreshedOrders
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
        : result.message + '；订单列表刷新失败：' + message
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

// 首次进入时加载；切换账号时立即清除旧账号的订单
watch(
  () => [authStore.sessionId, authStore.isAuthenticated] as const,
  () => {
    requestVersion += 1
    closeReceiptConfirmation()
    receiptAction.value = null
    receiptOrderId.value = null
    receiptMessage.value = ''
    receiptError.value = false
    closeCancelConfirmation()
    cancelAction.value = null
    cancelOrderId.value = null
    cancelMessage.value = ''
    cancelError.value = false
    orders.value = []
    loadError.value = ''
    isLoading.value = false

    if (authStore.isAuthenticated) {
      void loadOrders()
    }
  },
  { immediate: true, flush: 'sync' },
)

// 等会话更新结束后再跳转，沿用现有地址页面的处理方式
watch(
  () => authStore.isAuthenticated,
  authenticated => {
    if (!authenticated && isPageActive) {
      void router.replace({ name: 'login' })
    }
  },
  { flush: 'post' },
)

// 离页后丢弃尚未返回的请求结果
onBeforeUnmount(() => {
  isPageActive = false
  requestVersion += 1
  clearInterval(clockTimer)
  closeReceiptConfirmation()
  closeCancelConfirmation()
})
</script>

<template>
  <div class="orders-page">
    <StoreHeader />

    <main class="orders-content">
      <header class="page-heading">
        <h1>我的订单</h1>

        <button
          class="secondary-button"
          type="button"
          :disabled="isBusy"
          @click="loadOrders()"
        >
          {{ isLoading ? '刷新中…' : '刷新列表' }}
        </button>
      </header>

      <section aria-label="本人订单" :aria-busy="isBusy">
        <p v-if="isLoading" class="message-panel muted-message" role="status">
          正在加载订单…
        </p>

        <div v-else-if="loadError" class="message-panel">
          <p class="error-message" role="alert">{{ loadError }}</p>
          <button class="secondary-button" type="button" @click="loadOrders()">
            重新加载
          </button>
        </div>

        <div v-else-if="orders.length === 0" class="message-panel">
          <p class="muted-message" role="status">你还没有订单。</p>
          <RouterLink class="primary-button" :to="{ name: 'home' }">
            去逛逛
          </RouterLink>
        </div>

        <ul v-else class="order-list">
          <li v-for="order in orders" :key="order.id" class="order-card">
            <header class="order-heading">
              <div class="order-meta">
                <p class="order-number">订单号：{{ order.orderNo }}</p>
                <p class="order-time">
                  下单时间：{{
                    new Date(order.createdAtUtc).toLocaleString('zh-CN', {
                      hour12: false,
                    })
                  }}
                </p>
              </div>

              <span class="status-label" :class="order.status">
                {{ statusLabels[order.status] }}
              </span>
            </header>

            <ul class="product-list" aria-label="订单商品">
              <li v-for="item in order.items" :key="item.id" class="product-item">
                <div class="product-info">
                  <p class="product-name">{{ item.productName }}</p>
                  <p v-if="item.specificationText" class="product-specification">
                    {{ item.specificationText }}
                  </p>
                </div>

                <span class="product-quantity">× {{ item.quantity }}</span>
              </li>
            </ul>

            <footer class="order-footer">
              <p class="payable-total">
                <span>应付金额</span>
                <strong>¥{{ order.payableAmount.toFixed(2) }}</strong>
              </p>

              <div class="order-actions">
                <button
                  v-if="order.status === 'Shipped'"
                  class="primary-button"
                  type="button"
                  :disabled="isBusy"
                  @click="handleConfirmReceipt(order.id)"
                >
                  {{ receiptOrderId === order.id && receiptAction === 'request' ? '确认中…' : '确认收货' }}
                </button>
                <button
                  v-if="order.status === 'PendingPayment'"
                  class="secondary-button"
                  type="button"
                  :disabled="isBusy"
                  @click="handleCancelOrder(order.id)"
                >
                  {{
                    cancelOrderId === order.id && cancelAction === 'request'
                      ? '正在取消…'
                      : '取消订单'
                  }}
                </button>

                <RouterLink
                  class="secondary-button"
                  :to="{ name: 'order-detail', params: { id: order.id } }"
                >
                  查看详情
                </RouterLink>

                <RouterLink
                  v-if="canPayOrder(order)"
                  class="primary-button"
                  :to="{ name: 'order-detail', params: { id: order.id } }"
                >
                  去付款
                </RouterLink>
              </div>
            </footer>
            <p
              v-if="cancelOrderId === order.id && cancelMessage"
              class="cancel-message"
              :class="{ 'error-message': cancelError }"
              :role="cancelError ? 'alert' : 'status'"
            >
              {{ cancelMessage }}
            </p>
            <p
              v-if="receiptOrderId === order.id && receiptMessage"
              class="cancel-message"
              :class="{ 'error-message': receiptError }"
              :role="receiptError ? 'alert' : 'status'"
            >
              {{ receiptMessage }}
            </p>
          </li>
        </ul>
      </section>
    </main>
  </div>
</template>

<style scoped>
.orders-page {
  min-height: 100vh;
  background: #f4f6fa;
  color: #253248;
}

.orders-content {
  box-sizing: border-box;
  width: 100%;
  max-width: 1240px;
  margin: 0 auto;
  padding: 28px 28px 48px;
}

.page-heading,
.order-heading,
.order-footer,
.order-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 16px;
}

.page-heading,
.order-heading,
.order-footer {
  justify-content: space-between;
}

.page-heading {
  margin-bottom: 20px;
}

.page-heading h1 {
  margin: 0;
  font-size: 25px;
  font-weight: 600;
}

.primary-button,
.secondary-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 9px 16px;
  border: 1px solid #dfe6ef;
  border-radius: 7px;
  background: #ffffff;
  color: #52617a;
  font: inherit;
  font-size: 14px;
  text-decoration: none;
  cursor: pointer;
}

.primary-button {
  border-color: #2864e8;
  background: #2864e8;
  color: #ffffff;
}

.secondary-button:hover {
  background: #f7f9fc;
}

.primary-button:hover {
  background: #1b52cd;
}

.secondary-button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.message-panel {
  display: grid;
  justify-items: center;
  gap: 16px;
  margin: 0;
  padding: 48px 20px;
  border: 1px solid #e5eaf2;
  border-radius: 12px;
  background: #ffffff;
  text-align: center;
}

.message-panel p {
  margin: 0;
  line-height: 1.7;
}

.muted-message {
  color: #8793a5;
}

.error-message {
  color: #bd4848;
}

.order-list,
.product-list {
  margin: 0;
  padding: 0;
  list-style: none;
}

.order-list {
  display: grid;
  gap: 20px;
}

.order-card {
  min-width: 0;
  overflow: hidden;
  border: 1px solid #e5eaf2;
  border-radius: 12px;
  background: #ffffff;
}

.order-heading {
  padding: 18px 22px;
  border-bottom: 1px solid #edf0f5;
  background: #fafbfd;
}

.order-meta {
  min-width: 0;
}

.order-number,
.order-time {
  margin: 0;
  overflow-wrap: anywhere;
}

.order-number {
  font-size: 14px;
}

.order-time {
  margin-top: 8px;
  color: #738096;
  font-size: 13px;
}

.status-label {
  flex-shrink: 0;
  padding: 6px 10px;
  border-radius: 6px;
  background: #eef3fc;
  color: #2864e8;
  font-size: 13px;
}

.status-label.PendingPayment {
  background: #fff4e5;
  color: #a56516;
}

.status-label.Completed,
.status-label.Cancelled {
  background: #f0f2f5;
  color: #66738a;
}

.product-list {
  padding: 0 22px;
}

.product-item {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 20px;
  padding: 20px 0;
}

.product-item + .product-item {
  border-top: 1px solid #edf0f5;
}

.product-info {
  min-width: 0;
}

.product-name {
  margin: 0;
  font-size: 15px;
  line-height: 1.6;
  overflow-wrap: anywhere;
}

.product-specification {
  margin: 8px 0 0;
  color: #738096;
  font-size: 13px;
  line-height: 1.6;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}

.product-quantity {
  flex-shrink: 0;
  color: #66738a;
  font-size: 14px;
  line-height: 1.7;
}

.order-footer {
  padding: 18px 22px;
  border-top: 1px solid #edf0f5;
}

.payable-total {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 10px;
  margin: 0;
  color: #66738a;
  font-size: 14px;
}

.payable-total strong {
  color: #2864e8;
  font-size: 22px;
}

.order-actions {
  gap: 10px;
}

.cancel-message {
  margin: 0;
  padding: 0 22px 18px;
  font-size: 14px;
  line-height: 1.7;
  overflow-wrap: anywhere;
}

@media (max-width: 700px) {
  .orders-content {
    padding: 24px 18px 36px;
  }

  .order-heading,
  .order-footer {
    padding: 16px;
  }

  .product-list {
    padding: 0 16px;
  }

  .cancel-message {
    padding: 0 16px 16px;
  }

  .order-actions {
    width: 100%;
    justify-content: flex-end;
  }
}
</style>
