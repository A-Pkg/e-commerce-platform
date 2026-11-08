<script setup lang="ts">
import { computed } from 'vue'
import AdminDialog from '@/components/admin/AdminDialog.vue'

// 定义订单状态
type OrderStatus = 'PendingPayment' | 'Paid' | 'Shipped' | 'Completed' | 'Cancelled'

// 定义订单商品明细的界面数据
interface OrderItemData {
  id: number
  productName: string
  skuCode: string
  specificationText: string
  unitPrice: number
  quantity: number
  subtotal: number
}

// 定义订单详情的界面数据
interface OrderDetailData {
  id: number
  orderNo: string
  username: string
  status: OrderStatus
  itemsAmount: number
  shippingFee: number
  payableAmount: number
  recipientName: string
  recipientPhone: string
  shippingAddress: string
  remark: string | null
  createdAtUtc: string
  expiresAtUtc: string
  paidAtUtc: string | null
  shippedAtUtc: string | null
  completedAtUtc: string | null
  cancelledAtUtc: string | null
  items: OrderItemData[]
}

// 接收显示状态和订单详情
const props = withDefaults(
  defineProps<{
    modelValue: boolean
    order?: OrderDetailData | null
  }>(),
  {
    order: null,
  },
)

// 将显示状态和关闭事件传给父组件
const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  closed: []
}>()

// 转发 v-model，让共用弹窗与父组件保持同步
const dialogVisible = computed<boolean>({
  get: () => props.modelValue,
  set: (value: boolean) => {
    emit('update:modelValue', value)
  },
})

// 配置订单状态对应的中文文字
const statusLabels: Record<OrderStatus, string> = {
  PendingPayment: '待支付',
  Paid: '已支付',
  Shipped: '已发货',
  Completed: '已完成',
  Cancelled: '已取消',
}

// 获取订单状态的中文文字
function getStatusText(status: OrderStatus): string {
  return statusLabels[status]
}

// 将金额格式化为人民币展示文字
function formatAmount(value: number): string {
  if (!Number.isFinite(value)) {
    return '—'
  }

  return `¥${value.toFixed(2)}`
}

// 将 UTC 时间转换为浏览器所在时区的显示时间
function formatDate(value: string | null): string {
  if (!value) {
    return '—'
  }

  const date: Date = new Date(value)

  if (Number.isNaN(date.getTime())) {
    return '—'
  }

  return date.toLocaleString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  })
}

// 通知父组件弹窗已经关闭，供其清除当前订单
function handleClosed(): void {
  emit('closed')
}
</script>

<template>
  <AdminDialog v-model="dialogVisible" title="订单详情" close-text="关闭" @closed="handleClosed">
    <!-- 没有订单数据时，不展示虚构信息 -->
    <p v-if="props.order === null" class="empty-state" role="status">暂无订单详情</p>

    <div v-else class="order-details">
      <!-- 订单基本信息 -->
      <section class="detail-section" aria-label="订单基本信息">
        <h3>基本信息</h3>

        <dl class="detail-grid">
          <div class="detail-field full-width">
            <dt>订单号</dt>
            <dd>{{ props.order.orderNo || '—' }}</dd>
          </div>

          <div class="detail-field">
            <dt>用户账号</dt>
            <dd>{{ props.order.username || '—' }}</dd>
          </div>

          <div class="detail-field">
            <dt>订单状态</dt>
            <dd>
              <span class="status-badge" :class="`status-${props.order.status}`">
                {{ getStatusText(props.order.status) }}
              </span>
            </dd>
          </div>

          <div class="detail-field">
            <dt>创建时间</dt>
            <dd>{{ formatDate(props.order.createdAtUtc) }}</dd>
          </div>

          <div class="detail-field">
            <dt>支付截止时间</dt>
            <dd>{{ formatDate(props.order.expiresAtUtc) }}</dd>
          </div>
        </dl>
      </section>

      <!-- 下单时保存的商品信息 -->
      <section class="detail-section" aria-label="订单商品明细">
        <h3>商品明细</h3>

        <div class="table-wrapper" role="region" aria-label="订单商品明细，可横向滚动" tabindex="0">
          <table class="items-table">
            <thead>
              <tr>
                <th scope="col">商品信息</th>
                <th scope="col">单价</th>
                <th scope="col">数量</th>
                <th scope="col">小计</th>
              </tr>
            </thead>

            <tbody>
              <tr v-if="props.order.items.length === 0">
                <td colspan="4" class="empty-cell">暂无商品明细</td>
              </tr>

              <tr v-for="item in props.order.items" :key="item.id">
                <td>
                  <div class="product-name">{{ item.productName }}</div>
                  <div class="product-meta">SKU：{{ item.skuCode }}</div>
                  <div class="product-meta">
                    {{ item.specificationText || '默认规格' }}
                  </div>
                </td>

                <td class="amount-cell">
                  {{ formatAmount(item.unitPrice) }}
                </td>

                <td>{{ item.quantity }}</td>

                <td class="amount-cell">
                  {{ formatAmount(item.subtotal) }}
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <!-- 金额直接读取订单数据，不在前端重新计算 -->
      <section class="detail-section" aria-label="订单金额">
        <h3>订单金额</h3>

        <dl class="amount-summary">
          <div>
            <dt>商品金额</dt>
            <dd>{{ formatAmount(props.order.itemsAmount) }}</dd>
          </div>

          <div>
            <dt>运费</dt>
            <dd>{{ formatAmount(props.order.shippingFee) }}</dd>
          </div>

          <div class="payable-row">
            <dt>应付金额</dt>
            <dd>{{ formatAmount(props.order.payableAmount) }}</dd>
          </div>
        </dl>
      </section>

      <!-- 读取订单中的收货快照，不读取用户当前地址 -->
      <section class="detail-section" aria-label="收货信息快照">
        <h3>收货信息</h3>

        <dl class="detail-grid">
          <div class="detail-field">
            <dt>收货人</dt>
            <dd>{{ props.order.recipientName || '—' }}</dd>
          </div>

          <div class="detail-field">
            <dt>联系电话</dt>
            <dd>{{ props.order.recipientPhone || '—' }}</dd>
          </div>

          <div class="detail-field full-width">
            <dt>收货地址</dt>
            <dd>{{ props.order.shippingAddress || '—' }}</dd>
          </div>
        </dl>
      </section>

      <!-- 尚未发生的阶段，其时间显示横线 -->
      <section class="detail-section" aria-label="订单阶段时间">
        <h3>阶段时间</h3>

        <dl class="detail-grid">
          <div class="detail-field">
            <dt>支付时间</dt>
            <dd>{{ formatDate(props.order.paidAtUtc) }}</dd>
          </div>

          <div class="detail-field">
            <dt>发货时间</dt>
            <dd>{{ formatDate(props.order.shippedAtUtc) }}</dd>
          </div>

          <div class="detail-field">
            <dt>完成时间</dt>
            <dd>{{ formatDate(props.order.completedAtUtc) }}</dd>
          </div>

          <div class="detail-field">
            <dt>取消时间</dt>
            <dd>{{ formatDate(props.order.cancelledAtUtc) }}</dd>
          </div>
        </dl>
      </section>

      <!-- 有备注时才展示备注区域 -->
      <section v-if="props.order.remark" class="detail-section" aria-label="订单备注">
        <h3>订单备注</h3>
        <p class="remark-text">{{ props.order.remark }}</p>
      </section>
    </div>
  </AdminDialog>
</template>

<style scoped>
/* 没有订单数据时的提示 */
.empty-state {
  margin: 0;
  padding: 36px 20px;
  color: #9aa7ba;
  font-size: 13px;
  text-align: center;
}

.order-details {
  min-width: 0;
}

/* 不同信息区域使用分隔线区分 */
.detail-section + .detail-section {
  margin-top: 24px;
  padding-top: 22px;
  border-top: 1px solid #edf1f6;
}

.detail-section h3 {
  margin: 0 0 16px;
  color: #3c4b63;
  font-size: 14px;
  font-weight: 600;
}

/* 基本信息使用两列展示 */
.detail-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 18px 24px;
  margin: 0;
}

.detail-field {
  min-width: 0;
}

.detail-field dt {
  color: #9aa7ba;
  font-size: 12px;
}

.detail-field dd {
  margin: 5px 0 0;
  color: #52617a;
  font-size: 13px;
  overflow-wrap: anywhere;
}

.full-width {
  grid-column: 1 / -1;
}

/* 不同订单状态使用不同配色 */
.status-badge {
  display: inline-block;
  padding: 3px 8px;
  border-radius: 5px;
  font-size: 12px;
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

/* 商品明细只在自身容器内横向滚动 */
.table-wrapper {
  width: 100%;
  overflow-x: auto;
  border: 1px solid #e6ecf5;
  border-radius: 8px;
}

.table-wrapper:focus-visible {
  outline: 3px solid #93b6ff;
  outline-offset: 3px;
}

.items-table {
  width: 100%;
  min-width: 560px;
  border-collapse: collapse;
  text-align: left;
}

.items-table th {
  padding: 12px;
  border-bottom: 1px solid #e6ecf5;
  background: #f8fafe;
  color: #8c99ad;
  font-size: 12px;
  font-weight: 400;
  white-space: nowrap;
}

.items-table td {
  padding: 14px 12px;
  border-bottom: 1px solid #edf1f7;
  color: #67768e;
  font-size: 12px;
  vertical-align: top;
}

.items-table tbody tr:last-child td {
  border-bottom: none;
}

.product-name {
  min-width: 150px;
  max-width: 240px;
  color: #3c4b63;
  overflow-wrap: anywhere;
}

.product-meta {
  max-width: 240px;
  margin-top: 4px;
  color: #9aa7ba;
  font-size: 11px;
  overflow-wrap: anywhere;
}

.amount-cell {
  white-space: nowrap;
}

.items-table .empty-cell {
  padding: 36px 20px;
  color: #9aa7ba;
  text-align: center;
}

/* 订单金额单独汇总展示 */
.amount-summary {
  margin: 0;
}

.amount-summary > div {
  display: flex;
  justify-content: space-between;
  gap: 20px;
  padding: 7px 0;
}

.amount-summary dt {
  color: #8b99ad;
  font-size: 12px;
}

.amount-summary dd {
  margin: 0;
  color: #52617a;
  font-size: 13px;
}

.amount-summary .payable-row {
  margin-top: 8px;
  padding-top: 14px;
  border-top: 1px dashed #e6ecf5;
}

.payable-row dd {
  color: #2864e8;
  font-size: 16px;
  font-weight: 600;
}

/* 保留备注中的换行 */
.remark-text {
  margin: 0;
  color: #67768e;
  font-size: 13px;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}

/* 手机上信息改为单列 */
@media (max-width: 600px) {
  .detail-grid {
    grid-template-columns: minmax(0, 1fr);
    gap: 16px;
  }
}
</style>
