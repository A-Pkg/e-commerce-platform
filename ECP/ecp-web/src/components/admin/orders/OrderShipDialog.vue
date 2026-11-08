<script setup lang="ts">
import { computed, reactive, ref, useId, watch } from 'vue'
import AdminDialog from '@/components/admin/AdminDialog.vue'

// 定义发货弹窗需要展示的订单信息
interface OrderShippingData {
  id: number
  orderNo: string
  recipientName: string
  recipientPhone: string
  shippingAddress: string
}

// 定义物流表单的数据结构
interface ShippingFormData {
  shippingCompany: string
  trackingNumber: string
}

// 接收显示状态和待发货订单
const props = withDefaults(
  defineProps<{
    modelValue: boolean
    order?: OrderShippingData | null
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

// 为表单和输入框生成独立编号
const formId: string = `${useId()}-order-shipping-form`

// 保存管理员填写的物流信息
const form = reactive<ShippingFormData>({
  shippingCompany: '',
  trackingNumber: '',
})

// 保存点击确认后显示的提示
const noticeMessage = ref<string>('')

// 清空物流输入和旧提示
function resetForm(): void {
  form.shippingCompany = ''
  form.trackingNumber = ''
  noticeMessage.value = ''
}

// 当前阶段仅显示提示，不调用接口或改变订单状态
function handleSubmit(): void {
  if (props.order === null) {
    return
  }

  noticeMessage.value = '功能即将开放'
}

// 弹窗关闭后丢弃未保存输入，并通知父组件
function handleClosed(): void {
  resetForm()
  emit('closed')
}

// 每次打开弹窗时清除上一次填写的物流信息
watch(
  () => props.modelValue,
  (visible: boolean) => {
    if (visible) {
      resetForm()
    }
  },
  { immediate: true },
)
</script>

<template>
  <AdminDialog
    v-model="dialogVisible"
    title="订单发货"
    description="核对收货信息，填写物流公司和运单号"
    @closed="handleClosed"
  >
    <!-- 没有订单数据时，不展示虚构信息 -->
    <p v-if="props.order === null" class="empty-state" role="status">暂无待发货订单信息</p>

    <div v-else>
      <!-- 使用订单中的收货快照，供管理员核对 -->
      <section class="order-summary" aria-label="订单及收货信息">
        <h3>订单信息</h3>

        <dl class="detail-grid">
          <div class="detail-field full-width">
            <dt>订单号</dt>
            <dd>{{ props.order.orderNo || '—' }}</dd>
          </div>

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

      <!-- 当前只完成界面，暂不进行业务提交校验 -->
      <form :id="formId" class="shipping-form" novalidate @submit.prevent="handleSubmit">
        <h3>物流信息</h3>

        <div class="form-grid">
          <div class="form-field">
            <label :for="`${formId}-company`">物流公司</label>

            <input
              :id="`${formId}-company`"
              v-model="form.shippingCompany"
              type="text"
              maxlength="100"
              placeholder="请输入物流公司"
            />
          </div>

          <div class="form-field">
            <label :for="`${formId}-tracking`">运单号</label>

            <input
              :id="`${formId}-tracking`"
              v-model="form.trackingNumber"
              type="text"
              maxlength="64"
              placeholder="请输入运单号"
            />
          </div>
        </div>
      </form>
    </div>

    <!-- 确认时保留弹窗和输入，只显示功能提示 -->
    <div role="status" aria-live="polite" aria-atomic="true">
      <p v-if="noticeMessage" class="notice-message">
        {{ noticeMessage }}
      </p>
    </div>

    <!-- 没有订单时禁用确认按钮 -->
    <template #actions>
      <button class="submit-button" type="submit" :form="formId" :disabled="props.order === null">
        确认发货
      </button>
    </template>
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

/* 信息区域标题 */
.order-summary h3,
.shipping-form h3 {
  margin: 0 0 16px;
  color: #3c4b63;
  font-size: 14px;
  font-weight: 600;
}

/* 订单信息使用两列展示 */
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

/* 物流表单与订单信息之间使用分隔线 */
.shipping-form {
  margin-top: 24px;
  padding-top: 22px;
  border-top: 1px solid #edf1f6;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 20px;
}

.form-field {
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.form-field label {
  color: #6f7f96;
  font-size: 12px;
}

/* 物流输入框 */
.form-field input {
  width: 100%;
  min-width: 0;
  min-height: 40px;
  padding: 9px 11px;
  border: 1px solid #e0e6f0;
  border-radius: 7px;
  background: #ffffff;
  color: #52617a;
  font-size: 13px;
}

.form-field input::placeholder {
  color: #b0b9c8;
}

.form-field input:focus {
  outline: none;
  border-color: #83a9fa;
  box-shadow: 0 0 0 3px rgba(40, 100, 232, 0.08);
}

/* 功能提示 */
.notice-message {
  margin: 18px 0 0;
  padding: 10px 14px;
  border: 1px solid #e4edff;
  border-radius: 7px;
  background: #f6f9ff;
  color: #6f8bb9;
  font-size: 12px;
}

/* 确认发货按钮 */
.submit-button {
  padding: 8px 15px;
  border: 1px solid #2864e8;
  border-radius: 7px;
  background: #2864e8;
  color: #ffffff;
  font-size: 12px;
}

.submit-button:hover:not(:disabled) {
  background: #1e55d0;
}

.submit-button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

/* 手机上改成单列 */
@media (max-width: 600px) {
  .detail-grid,
  .form-grid {
    grid-template-columns: minmax(0, 1fr);
    gap: 16px;
  }
}
</style>
