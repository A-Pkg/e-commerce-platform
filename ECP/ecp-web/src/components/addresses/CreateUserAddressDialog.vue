<script setup lang="ts">
import { reactive, ref, useId, watch } from 'vue'
import type {
  CreateUserAddressRequest,
  UserAddressResponse,
} from '@/api/userAddresses'
import AdminDialog from '@/components/admin/AdminDialog.vue'

const props = defineProps<{
  modelValue: boolean
  saving: boolean
  errorMessage: string

  // 新增时为 null，编辑时为当前地址
  initialAddress: UserAddressResponse | null
}>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  submit: [request: CreateUserAddressRequest]
}>()

// 保存按钮位于表单外，通过唯一编号关联表单
const formId: string = useId()

// 表单输入与接口请求分别保存
const form = reactive<CreateUserAddressRequest>({
  recipientName: '',
  phone: '',
  address: '',
})
const validationError = ref<string>('')

// 清理输入和前端校验信息
function resetForm(): void {
  form.recipientName = ''
  form.phone = ''
  form.address = ''
  validationError.value = ''
}

// 保存期间拒绝关闭
function handleVisibilityChange(visible: boolean): void {
  if (!props.saving) {
    emit('update:modelValue', visible)
  }
}

// 实际关闭后再清理表单
function handleClosed(): void {
  if (!props.modelValue) {
    resetForm()
  }
}

// 校验后提交独立的请求数据，不将响应式表单直接交给父组件
function handleSubmit(): void {
  if (!props.modelValue || props.saving) {
    return
  }

  validationError.value = ''
  const request: CreateUserAddressRequest = {
    recipientName: form.recipientName.trim(),
    phone: form.phone.trim(),
    address: form.address.trim(),
  }

  if (!request.recipientName) {
    validationError.value = '请输入收货人姓名'
    return
  }
  if (request.recipientName.length > 50) {
    validationError.value = '收货人姓名最多 50 个字符'
    return
  }
  if (!request.phone) {
    validationError.value = '请输入联系电话'
    return
  }
  if (request.phone.length > 32) {
    validationError.value = '联系电话最多 32 位'
    return
  }
  if (!/^[0-9]+$/.test(request.phone)) {
    validationError.value = '联系电话只能包含数字'
    return
  }
  if (!request.address) {
    validationError.value = '请输入完整收货地址'
    return
  }
  if (request.address.length > 500) {
    validationError.value = '完整收货地址最多 500 个字符'
    return
  }

  emit('submit', request)
}

// 打开时复制原地址到表单；新增时使用空内容
watch(
  () => props.modelValue,
  visible => {
    if (visible) {
      form.recipientName = props.initialAddress?.recipientName ?? ''
      form.phone = props.initialAddress?.phone ?? ''
      form.address = props.initialAddress?.address ?? ''
      validationError.value = ''
    }
  },
)
</script>

<template>
  <AdminDialog
    :model-value="props.modelValue"
    :title="props.initialAddress ? '编辑收货地址' : '新增收货地址'"
    description="请填写收货人、联系电话和完整收货地址。"
    :close-disabled="props.saving"
    @update:model-value="handleVisibilityChange"
    @closed="handleClosed"
  >
    <form :id="formId" novalidate @submit.prevent="handleSubmit">
      <fieldset class="address-fields" :disabled="props.saving">
        <label class="address-field">
          <span>收货人</span>
          <input
            v-model="form.recipientName"
            type="text"
            autocomplete="name"
            maxlength="50"
            placeholder="请输入收货人姓名"
            required
          />
        </label>

        <label class="address-field">
          <span>联系电话</span>
          <input
            v-model="form.phone"
            type="tel"
            autocomplete="tel"
            maxlength="32"
            placeholder="请输入联系电话"
            required
          />
        </label>

        <label class="address-field">
          <span>完整收货地址</span>
          <textarea
            v-model="form.address"
            autocomplete="street-address"
            maxlength="500"
            rows="4"
            placeholder="请填写省、市、区及街道门牌等完整地址"
            required
          ></textarea>
        </label>
      </fieldset>

      <p v-if="validationError || props.errorMessage" class="form-error" role="alert">
        {{ validationError || props.errorMessage }}
      </p>
    </form>

    <template #actions>
      <button class="save-button" type="submit" :form="formId" :disabled="props.saving">
        {{ props.saving ? '保存中…' : '保存地址' }}
      </button>
    </template>
  </AdminDialog>
</template>

<style scoped>
.address-fields {
  display: grid;
  gap: 18px;
  min-width: 0;
  margin: 0;
  padding: 0;
  border: none;
}

.address-field {
  display: grid;
  gap: 8px;
  min-width: 0;
  color: #52617a;
  font-size: 14px;
}

.address-field input,
.address-field textarea {
  box-sizing: border-box;
  width: 100%;
  min-width: 0;
  padding: 10px 12px;
  border: 1px solid #dfe6ef;
  border-radius: 7px;
  background: #ffffff;
  color: #253248;
  font: inherit;
  line-height: 1.6;
}

.address-field textarea {
  resize: vertical;
}

.address-field input:focus,
.address-field textarea:focus {
  outline: 2px solid #b9d0ff;
  outline-offset: 1px;
  border-color: #7ca5ff;
}

.address-field input:disabled,
.address-field textarea:disabled {
  background: #f6f8fb;
  cursor: not-allowed;
}

.form-error {
  margin: 16px 0 0;
  color: #bd4848;
  font-size: 14px;
  line-height: 1.7;
}

.save-button {
  padding: 8px 18px;
  border: 1px solid #2864e8;
  border-radius: 6px;
  background: #2864e8;
  color: #ffffff;
  font: inherit;
  cursor: pointer;
}

.save-button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}
</style>
