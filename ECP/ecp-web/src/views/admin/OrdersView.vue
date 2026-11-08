<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import {
  getAdminOrders,
  getAdminOrderDetail,
  shipAdminOrder,
  AdminOrderUnauthorizedError,
  AdminOrderForbiddenError,
  type AdminOrderFilters,
  type AdminOrderListItemResponse,
  type AdminOrderDetailResponse,
  type AdminOrderShipResult,
} from '@/api/adminOrders'
import type { OrderStatus } from '@/api/orders'
import type { PagedResponse } from '@/types/pagination'
import OrderDetailDialog from '@/components/admin/orders/OrderDetailDialog.vue'
import { useAuthStore } from '@/stores/auth'

const router = useRouter()
const authStore = useAuthStore()

// 输入中的草稿与已提交条件分开，编辑时不自动查询
const filters = reactive<AdminOrderFilters>({ keyword: '', status: '' })
const appliedFilters = reactive<AdminOrderFilters>({ keyword: '', status: '' })
const hasActiveFilters = computed<boolean>(
  () => appliedFilters.keyword !== '' || appliedFilters.status !== '',
)

// 保存真实订单列表和加载状态
const orders = ref<AdminOrderListItemResponse[]>([])
const isLoading = ref<boolean>(false)
const loadError = ref<string>('')

// 分页信息以后端响应为准，页数减少时采用后端返回的有效页码
const pageNumber = ref<number>(1)
const pageSize = ref<number>(20)
const totalCount = ref<number>(0)
const totalPages = ref<number>(1)

// 详情查询完成后，再打开现有弹窗
const loadingDetailId = ref<number | null>(null)
const detailError = ref<string>('')
const isDetailDialogVisible = ref<boolean>(false)
const selectedDetailOrder = ref<AdminOrderDetailResponse | null>(null)

// 会话变化时重新创建弹窗，清除上一账号的数据
const dialogSessionKey = ref<number>(0)

// 保存发货中的订单及操作结果
const shippingOrderId = ref<number | null>(null)
const shippingMessage = ref<string>('')
const shippingError = ref<boolean>(false)

// 列表查询、详情查询、发货互斥；详情关闭完成前不重复操作
const isBusy = computed<boolean>(
  () =>
    isLoading.value ||
    loadingDetailId.value !== null ||
    selectedDetailOrder.value !== null ||
    shippingOrderId.value !== null,
)

// 后台沿用现有状态文案
const statusLabels: Record<OrderStatus, string> = {
  PendingPayment: '待支付',
  Paid: '已支付',
  Shipped: '已发货',
  Completed: '已完成',
  Cancelled: '已取消',
}

let isPageActive: boolean = true
let requestVersion: number = 0

// 只处理当前页面、当前请求和原管理员会话的响应
function isCurrentRequest(version: number, accessToken: string): boolean {
  return (
    isPageActive &&
    version === requestVersion &&
    authStore.isAdmin &&
    authStore.isCurrentSession(accessToken)
  )
}

// 列表和详情共用权限错误处理
function handleAccessError(error: unknown): boolean {
  if (error instanceof AdminOrderUnauthorizedError) {
    authStore.clearSession(true)
    authStore.sessionExpired = true
    return true
  }

  if (error instanceof AdminOrderForbiddenError) {
    // 权限不足时清除已显示的订单和详情
    orders.value = []
    pageNumber.value = 1
    pageSize.value = 20
    totalCount.value = 0
    totalPages.value = 1
    isDetailDialogVisible.value = false
    selectedDetailOrder.value = null
    detailError.value = ''
    shippingMessage.value = ''
    shippingError.value = false
    loadError.value = error.message
    return true
  }

  return false
}

// 查询订单列表，顺序和金额使用后端返回值
async function loadOrders(targetPage: number = pageNumber.value): Promise<void> {
  if (!isPageActive || isBusy.value || !authStore.isAdmin) {
    return
  }

  pageNumber.value = targetPage

  const filterSnapshot: AdminOrderFilters = {
    keyword: appliedFilters.keyword,
    status: appliedFilters.status,
  }
  const accessToken: string = authStore.accessToken
  const version: number = ++requestVersion

  isLoading.value = true
  loadError.value = ''
  detailError.value = ''
  shippingMessage.value = ''
  shippingError.value = false
  orders.value = []

  try {
    const result: PagedResponse<AdminOrderListItemResponse> =
      await getAdminOrders(accessToken, filterSnapshot, targetPage)

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    orders.value = result.items
    pageNumber.value = result.pageNumber
    pageSize.value = result.pageSize
    totalCount.value = result.totalCount
    totalPages.value = result.totalPages
  } catch (error: unknown) {
    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    if (handleAccessError(error)) {
      return
    }

    loadError.value =
      error instanceof Error ? error.message : '获取后台订单列表失败，请稍后重试'
  } finally {
    if (isCurrentRequest(version, accessToken)) {
      isLoading.value = false
    }
  }
}

// 点击查询或按回车后才提交条件；刷新和发货后沿用这些条件
function handleSearch(): void {
  if (!isPageActive || isBusy.value || !authStore.isAdmin) {
    return
  }

  appliedFilters.keyword = filters.keyword.trim()
  appliedFilters.status = filters.status
  pageNumber.value = 1
  totalCount.value = 0
  totalPages.value = 1
  void loadOrders(1)
}

// 翻页沿用已提交条件，失败后通过重新加载重试当前请求页
function handleChangePage(targetPage: number): void {
  if (
    !isPageActive ||
    isBusy.value ||
    !authStore.isAdmin ||
    loadError.value !== '' ||
    targetPage < 1 ||
    targetPage > totalPages.value ||
    targetPage === pageNumber.value
  ) {
    return
  }

  void loadOrders(targetPage)
}

// 单独查询详情，不把列表概要当作完整详情使用
async function openDetailDialog(id: number): Promise<void> {
  if (!isPageActive || isBusy.value || !authStore.isAdmin) {
    return
  }

  const accessToken: string = authStore.accessToken
  const version: number = requestVersion

  loadingDetailId.value = id
  detailError.value = ''

  try {
    const result: AdminOrderDetailResponse =
      await getAdminOrderDetail(id, accessToken)

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    selectedDetailOrder.value = result
    isDetailDialogVisible.value = true
  } catch (error: unknown) {
    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    if (handleAccessError(error)) {
      return
    }

    detailError.value =
      error instanceof Error ? error.message : '获取后台订单详情失败，请稍后重试'
  } finally {
    if (isCurrentRequest(version, accessToken)) {
      loadingDetailId.value = null
    }
  }
}

// 点击后直接发货，不打开表单或确认弹窗
async function handleShipOrder(id: number): Promise<void> {
  const currentOrder: AdminOrderListItemResponse | undefined =
    orders.value.find(item => item.id === id)

  if (
    !isPageActive ||
    isBusy.value ||
    !authStore.isAdmin ||
    !currentOrder ||
    currentOrder.status !== 'Paid'
  ) {
    return
  }

  const filterSnapshot: AdminOrderFilters = {
    keyword: appliedFilters.keyword,
    status: appliedFilters.status,
  }
  const requestedPage: number = pageNumber.value
  const accessToken: string = authStore.accessToken
  const version: number = requestVersion

  let result: AdminOrderShipResult | null = null

  shippingOrderId.value = id
  shippingMessage.value = ''
  shippingError.value = false
  detailError.value = ''

  try {
    result = await shipAdminOrder(id, accessToken)

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    shippingMessage.value = '订单 ' + currentOrder.orderNo + '：' + result.message
    shippingError.value = result.outcome === 'conflict'

    // 后端已确认发货，后续列表刷新失败也不再显示发货按钮
    if (result.outcome === 'shipped') {
      currentOrder.status = 'Shipped'
    }

    // 在发货操作锁内刷新列表，并保留本次结果提示
    const refreshedPage: PagedResponse<AdminOrderListItemResponse> =
      await getAdminOrders(accessToken, filterSnapshot, requestedPage)

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    orders.value = refreshedPage.items
    pageNumber.value = refreshedPage.pageNumber
    pageSize.value = refreshedPage.pageSize
    totalCount.value = refreshedPage.totalCount
    totalPages.value = refreshedPage.totalPages
  } catch (error: unknown) {
    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    if (handleAccessError(error)) {
      return
    }

    const message: string =
      error instanceof Error
        ? error.message
        : '暂时无法确认发货结果，请刷新订单列表后确认'

    shippingError.value = true

    // 区分发货请求失败与取得发货结果后的列表刷新失败
    shippingMessage.value =
      result === null
        ? message
        : '订单 ' +
          currentOrder.orderNo +
          '：' +
          result.message +
          '；订单列表刷新失败：' +
          message
  } finally {
    if (isCurrentRequest(version, accessToken)) {
      shippingOrderId.value = null
    }
  }
}

// 等实际关闭动画结束后清理详情
function handleDetailDialogClosed(): void {
  if (!isDetailDialogVisible.value) {
    selectedDetailOrder.value = null
  }
}

// 格式化金额，保留原有显示方式
function formatAmount(value: number): string {
  if (!Number.isFinite(value)) {
    return '—'
  }

  return '¥' + value.toFixed(2)
}

// 将后端 UTC 时间转换为浏览器本地时间
function formatDate(value: string): string {
  return new Date(value).toLocaleString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  })
}

// 首次进入时加载；会话或角色变化时立即清理原账号数据
watch(
  () =>
    [
      authStore.sessionId,
      authStore.isAuthenticated,
      authStore.isAdmin,
    ] as const,
  () => {
    requestVersion += 1
    filters.keyword = ''
    filters.status = ''
    appliedFilters.keyword = ''
    appliedFilters.status = ''
    pageNumber.value = 1
    pageSize.value = 20
    totalCount.value = 0
    totalPages.value = 1
    shippingOrderId.value = null
    shippingMessage.value = ''
    shippingError.value = false
    orders.value = []
    loadError.value = ''
    detailError.value = ''
    isLoading.value = false
    loadingDetailId.value = null
    isDetailDialogVisible.value = false
    selectedDetailOrder.value = null
    dialogSessionKey.value += 1

    if (authStore.isAdmin) {
      void loadOrders()
    }
  },
  { immediate: true, flush: 'sync' },
)

// 等会话更新结束后处理退出或角色变化
watch(
  () => [authStore.isAuthenticated, authStore.isAdmin] as const,
  ([authenticated, isAdmin]) => {
    if (!isPageActive) {
      return
    }

    if (!authenticated) {
      void router.replace({ name: 'login' })
    } else if (!isAdmin) {
      void router.replace({ name: 'home' })
    }
  },
  { flush: 'post' },
)

// 离页后关闭详情，尚未返回的响应不再更新页面
onBeforeUnmount(() => {
  isPageActive = false
  requestVersion += 1
  isDetailDialogVisible.value = false
  selectedDetailOrder.value = null
})
</script>

<template>
  <div class="orders-page">
    <header class="page-heading">
      <h1>订单管理</h1>
      <p>查看订单列表、商品明细和收货信息</p>
    </header>

    <section
      class="table-panel"
      aria-labelledby="order-list-title"
      :aria-busy="isLoading || loadingDetailId !== null || shippingOrderId !== null"
    >
      <!-- 查询条件在提交时生效 -->
      <form class="filter-form" novalidate @submit.prevent="handleSearch">
        <div class="filter-field keyword-filter">
          <label for="admin-order-keyword">订单号／用户账号</label>

          <input
            id="admin-order-keyword"
            v-model="filters.keyword"
            type="search"
            maxlength="100"
            placeholder="请输入订单号或用户账号"
            :disabled="isBusy"
          />
        </div>

        <div class="filter-field">
          <label for="admin-order-status">订单状态</label>

          <select id="admin-order-status" v-model="filters.status" :disabled="isBusy">
            <option value="">全部状态</option>

            <option
              v-for="(label, status) in statusLabels"
              :key="status"
              :value="status"
            >
              {{ label }}
            </option>
          </select>
        </div>

        <div class="filter-actions">
          <button class="button primary-button" type="submit" :disabled="isBusy">
            查询
          </button>
        </div>
      </form>

      <p v-if="detailError" class="notice-message error-message" role="alert">
        {{ detailError }}
      </p>

      <p
        v-if="shippingMessage"
        class="notice-message"
        :class="{ 'error-message': shippingError }"
        :role="shippingError ? 'alert' : 'status'"
      >
        {{ shippingMessage }}
      </p>

      <div class="table-heading">
        <h2 id="order-list-title">订单列表</h2>

        <button
          class="button"
          type="button"
          :disabled="isBusy"
          @click="loadOrders()"
        >
          {{ isLoading ? '刷新中…' : '刷新列表' }}
        </button>
      </div>

      <div
        class="table-wrapper"
        role="region"
        aria-label="订单列表，可横向滚动"
        tabindex="0"
      >
        <table class="order-table">
          <thead>
            <tr>
              <th scope="col">订单信息</th>
              <th scope="col">用户账号</th>
              <th scope="col">商品数量</th>
              <th scope="col">应付金额</th>
              <th scope="col">订单状态</th>
              <th scope="col">操作</th>
            </tr>
          </thead>

          <tbody>
            <tr v-if="isLoading">
              <td colspan="6" class="empty-cell" role="status">
                正在加载订单…
              </td>
            </tr>

            <tr v-else-if="loadError">
              <td colspan="6" class="empty-cell">
                <p class="error-message" role="alert">{{ loadError }}</p>
                <button
                  class="button"
                  type="button"
                  :disabled="isBusy"
                  @click="loadOrders()"
                >
                  重新加载
                </button>
              </td>
            </tr>

            <tr v-else-if="orders.length === 0">
              <td colspan="6" class="empty-cell">
                {{ hasActiveFilters ? '没有符合条件的订单' : '暂无订单' }}
              </td>
            </tr>

            <tr v-for="order in orders" :key="order.id">
              <td>
                <div class="order-number">{{ order.orderNo }}</div>
                <div class="order-time">{{ formatDate(order.createdAtUtc) }}</div>
              </td>

              <td>{{ order.username }}</td>
              <td>{{ order.totalQuantity }} 件</td>
              <td class="amount-cell">{{ formatAmount(order.payableAmount) }}</td>

              <td>
                <span class="status-badge" :class="'status-' + order.status">
                  {{ statusLabels[order.status] }}
                </span>
              </td>

              <td>
                <div class="row-actions">
                  <button
                    class="text-button"
                    type="button"
                    :disabled="isBusy"
                    @click="openDetailDialog(order.id)"
                  >
                    {{ loadingDetailId === order.id ? '加载中…' : '查看详情' }}
                  </button>

                  <button
                    v-if="order.status === 'Paid'"
                    class="text-button"
                    type="button"
                    :disabled="isBusy"
                    @click="handleShipOrder(order.id)"
                  >
                    {{ shippingOrderId === order.id ? '发货中…' : '发货' }}
                  </button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <footer class="pagination">
        <span>共 {{ totalCount }} 条，每页 {{ pageSize }} 条</span>

        <div class="pagination-actions">
          <button
            class="button page-button"
            type="button"
            :disabled="isBusy || loadError !== '' || pageNumber <= 1"
            @click="handleChangePage(pageNumber - 1)"
          >
            上一页
          </button>
          <span class="page-number" aria-live="polite" aria-atomic="true">
            第 {{ pageNumber }} / {{ totalPages }} 页
          </span>
          <button
            class="button page-button"
            type="button"
            :disabled="isBusy || loadError !== '' || pageNumber >= totalPages"
            @click="handleChangePage(pageNumber + 1)"
          >
            下一页
          </button>
        </div>
      </footer>
    </section>

    <OrderDetailDialog
      :key="dialogSessionKey"
      v-model="isDetailDialogVisible"
      :order="selectedDetailOrder"
      @closed="handleDetailDialogClosed"
    />
  </div>
</template>

<style scoped>
/* 页面允许在后台内容容器中缩小 */
.orders-page {
  min-width: 0;
}

/* 页面标题 */
.page-heading {
  margin-bottom: 24px;
}

.page-heading h1 {
  margin: 0 0 5px;
  font-size: 25px;
  font-weight: 600;
  letter-spacing: -0.6px;
}

.page-heading p {
  margin: 0;
  color: #919eb2;
  font-size: 12px;
}

/* 页面内按钮的公共样式 */
.button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: 37px;
  padding: 8px 15px;
  border: 1px solid #dfe6ef;
  border-radius: 7px;
  background: #ffffff;
  color: #65738b;
  font-size: 12px;
  white-space: nowrap;
}

.button:hover:not(:disabled) {
  border-color: #c9d9f5;
  background: #f6f9ff;
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

/* 筛选和表格放在白色卡片中 */
.table-panel {
  min-width: 0;
  overflow: hidden;
  border: 1px solid #e6ebf3;
  border-radius: 12px;
  background: #ffffff;
}

/* 筛选区域 */
.filter-form {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 14px;
  padding: 22px 23px;
  border-bottom: 1px solid #eef1f6;
}

.filter-field {
  min-width: 140px;
  display: flex;
  flex-direction: column;
  gap: 7px;
}

.keyword-filter {
  min-width: 240px;
}

.filter-field label {
  color: #7f8ba0;
  font-size: 12px;
}

.filter-field input,
.filter-field select {
  width: 100%;
  min-width: 0;
  min-height: 37px;
  padding: 8px 11px;
  border: 1px solid #e0e6f0;
  border-radius: 6px;
  background: #ffffff;
  color: #52617a;
  font-size: 12px;
}

.filter-field input::placeholder {
  color: #b0b9c8;
}

.filter-field input:focus,
.filter-field select:focus {
  outline: none;
  border-color: #83a9fa;
  box-shadow: 0 0 0 3px rgba(40, 100, 232, 0.08);
}

.filter-actions {
  display: flex;
  gap: 9px;
}

/* 功能提示 */
.notice-message {
  margin: 16px 23px 0;
  padding: 10px 14px;
  border: 1px solid #e4edff;
  border-radius: 7px;
  background: #f6f9ff;
  color: #6f8bb9;
  font-size: 12px;
}

/* 表格标题 */
.table-heading {
  padding: 19px 23px 15px;
}

.table-heading h2 {
  margin: 0;
  font-size: 13px;
  font-weight: 600;
}

/* 表格内部横向滚动 */
.table-wrapper {
  width: 100%;
  overflow-x: auto;
}

.table-wrapper:focus-visible {
  outline: 3px solid #93b6ff;
  outline-offset: -3px;
}

.order-table {
  width: 100%;
  min-width: 800px;
  border-collapse: collapse;
  text-align: left;
}

.order-table th {
  padding: 12px 16px;
  border-top: 1px solid #f0f3f8;
  border-bottom: 1px solid #edf1f7;
  background: #f8fafe;
  color: #8c99ad;
  font-size: 12px;
  font-weight: 400;
  white-space: nowrap;
}

.order-table td {
  padding: 18px 16px;
  border-bottom: 1px solid #f0f3f7;
  color: #67768e;
  font-size: 12px;
}

.order-table th:first-child,
.order-table td:first-child {
  padding-left: 23px;
}

.order-table th:last-child,
.order-table td:last-child {
  padding-right: 23px;
}

.order-table tbody tr:last-child td {
  border-bottom: none;
}

/* 较长的订单号允许换行 */
.order-number {
  min-width: 180px;
  max-width: 260px;
  color: #617b9e;
  overflow-wrap: anywhere;
}

.order-time {
  margin-top: 5px;
  color: #a8b2c2;
  font-size: 11px;
}

.amount-cell {
  color: #4b5e7d;
  white-space: nowrap;
}

/* 订单状态 */
.status-badge {
  display: inline-block;
  padding: 3px 8px;
  border-radius: 5px;
  font-size: 11px;
  white-space: nowrap;
}

.status-PendingPayment {
  background: #fff5e9;
  color: #d39b51;
}

.status-Paid {
  background: #eef4ff;
  color: #6f91d1;
}

.status-Shipped {
  background: #f3efff;
  color: #9a83cc;
}

.status-Completed {
  background: #eef8f2;
  color: #68a789;
}

.status-Cancelled {
  background: #f0f3f8;
  color: #96a1b3;
}

/* 行操作 */
.row-actions {
  display: flex;
  align-items: center;
  gap: 12px;
  white-space: nowrap;
}

.text-button {
  padding: 2px 0;
  border: none;
  background: transparent;
  color: #648dd6;
  font-size: 12px;
}

.text-button:hover {
  color: #2864e8;
}

/* 空列表提示 */
.order-table .empty-cell {
  padding: 64px 20px;
  color: #a1adc0;
  text-align: center;
}

/* 分页区域 */
.pagination {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 18px 23px;
  border-top: 1px solid #edf1f7;
  color: #9ba8ba;
  font-size: 12px;
}

.pagination-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.page-button {
  min-height: 29px;
  padding: 4px 9px;
}

.page-button:disabled {
  opacity: 0.45;
}

.page-number {
  min-width: 29px;
  height: 29px;
  display: grid;
  place-items: center;
  border-radius: 5px;
  background: #2864e8;
  color: #ffffff;
}

/* 手机上筛选条件上下排列 */
@media (max-width: 700px) {
  .page-heading h1 {
    font-size: 23px;
  }

  .filter-form {
    padding: 18px 16px;
    gap: 12px;
  }

  .filter-field,
  .keyword-filter {
    width: 100%;
    min-width: 0;
  }

  .filter-actions {
    width: 100%;
  }

  .filter-actions .button {
    flex: 1;
  }

  .notice-message {
    margin-right: 16px;
    margin-left: 16px;
  }

  .table-heading,
  .pagination {
    padding-right: 16px;
    padding-left: 16px;
  }
}

/* 列表标题与刷新按钮横向排列 */
.table-heading {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

/* 查询错误与普通提示区分 */
.error-message {
  color: #bd4848;
}

/* 明确展示禁用状态 */
.button:disabled,
.text-button:disabled,
.filter-field input:disabled,
.filter-field select:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}
/* 较长的订单号和操作提示允许换行 */
.notice-message {
  overflow-wrap: anywhere;
}
</style>
