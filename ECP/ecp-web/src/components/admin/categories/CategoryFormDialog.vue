<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, ref, useId, watch } from 'vue'
import {
  createAdminCategory,
  updateAdminCategory,
  type CategoryResponse,
  type CreateCategoryRequest,
  type UpdateCategoryRequest,
} from '@/api/categories'
import AdminDialog from '@/components/admin/AdminDialog.vue'
import { useAuthStore } from '@/stores/auth'

// 定义待编辑分类的数据结构
interface CategoryData {
  id: number
  name: string
  parentId: number | null
  sortOrder: number
  isActive: boolean
}

// 定义分类表单，排序输入框清空时的值为空字符串
interface CategoryFormData {
  name: string
  parentId: number | null
  sortOrder: number | ''
  isActive: boolean
}

// 定义父分类选项，页面后续只传入大分类
interface ParentCategoryOption {
  id: number
  name: string
}

// 接收显示状态、待编辑分类和父分类选项
const props = withDefaults(
  defineProps<{
    modelValue: boolean
    category?: CategoryData | null
    parentCategories?: ParentCategoryOption[]
  }>(),
  {
    category: null,
    parentCategories: () => [],
  },
)

// 通知父组件更新显示状态、保存结果及关闭事件
const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  saved: [category: CategoryResponse]
  closed: []
}>()

// 从当前登录会话读取管理员令牌
const authStore = useAuthStore()
let formVersion: number = 0

function isCurrentSave(version: number, token: string): boolean {
  return version === formVersion && props.modelValue
    && authStore.isAdmin && authStore.isCurrentSession(token)
}

// 标记保存状态，避免重复提交
const isSaving = ref<boolean>(false)

// 转发 v-model，让共用弹窗与父组件保持同步
const dialogVisible = computed<boolean>({
  get: () => props.modelValue,
  set: (value: boolean) => {
    // 保存期间保持弹窗打开，避免关闭后丢失提交结果
    if (isSaving.value && !value) {
      return
    }

    emit('update:modelValue', value)
  },
})

// 根据是否传入分类，区分新增和编辑
const dialogTitle = computed<string>(() => (props.category === null ? '新增分类' : '编辑分类'))

// 编辑时从父分类选项中排除自身
const parentOptions = computed<ParentCategoryOption[]>(() =>
  props.parentCategories.filter(
    (category: ParentCategoryOption) => category.id !== props.category?.id,
  ),
)

// 为表单和输入框生成独立编号
const formId: string = `${useId()}-category-form`

// 保存当前表单输入
const form = reactive<CategoryFormData>({
  name: '',
  parentId: null,
  sortOrder: 0,
  isActive: true,
})

// 保存点击保存后显示的提示
const noticeMessage = ref<string>('')

// 清空表单和旧提示
function resetForm(): void {
  form.name = ''
  form.parentId = null
  form.sortOrder = 0
  form.isActive = true
  noticeMessage.value = ''
}

// 打开弹窗时准备新增表单，或者回填待编辑分类
function prepareForm(): void {
  resetForm()

  if (props.category === null) {
    return
  }

  form.name = props.category.name
  form.parentId = props.category.parentId
  form.sortOrder = props.category.sortOrder
  form.isActive = props.category.isActive
}

// 校验新增或编辑输入，保存成功后将后端结果交给父组件
async function handleSubmit(): Promise<void> {
  if (isSaving.value) {
    return
  }

  noticeMessage.value = ''

  // 保存当前编辑对象，为空时表示新增分类
  const currentCategory: CategoryData | null = props.category

  // 分类名称去除首尾空格后必须为 1 到 100 个字符
  const categoryName: string = form.name.trim()

  if (categoryName.length === 0 || categoryName.length > 100) {
    noticeMessage.value = '请输入 1 到 100 个字符的分类名称'
    return
  }

  // 排序值必须与后端 int 范围一致，不能留空或填写小数
  const sortOrder: number | '' = form.sortOrder

  if (
    sortOrder === '' ||
    !Number.isInteger(sortOrder) ||
    sortOrder < 0 ||
    sortOrder > 2_147_483_647
  ) {
    noticeMessage.value = '排序值必须为 0 到 2147483647 之间的整数'
    return
  }

  // 父分类编号只能为空或正整数
  const parentId: number | null = form.parentId

  if (
    parentId !== null &&
    (!Number.isInteger(parentId) || parentId <= 0 || parentId > 2_147_483_647)
  ) {
    noticeMessage.value = '请选择有效的父分类'
    return
  }

  // 编辑时禁止将当前分类设为自己的父分类
  if (currentCategory !== null && parentId === currentCategory.id) {
    noticeMessage.value = '不能将分类自身设为父分类'
    return
  }

  const version: number = formVersion
  const token: string = authStore.accessToken
  isSaving.value = true

  try {
    // 保存后端返回的分类，新增时包含数据库生成的编号
    let category: CategoryResponse

    if (currentCategory === null) {
      // 创建新增分类请求
      const request: CreateCategoryRequest = {
        parentId,
        name: categoryName,
        sortOrder,
        isActive: form.isActive,
      }

      category = await createAdminCategory(request, token)
    } else {
      // 创建编辑分类请求，明确保留或修改父分类归属
      const request: UpdateCategoryRequest = {
        parentId,
        name: categoryName,
        sortOrder,
        isActive: form.isActive,
      }

      category = await updateAdminCategory(currentCategory.id, request, token)
    }

    if (!isCurrentSave(version, token)) return
    // 将真实保存结果交给列表，并关闭弹窗
    emit('saved', category)
    emit('update:modelValue', false)
  } catch (error: unknown) {
    if (!isCurrentSave(version, token)) return
    // 保存失败时保留输入，显示具体原因以便修改后重试
    noticeMessage.value = error instanceof Error ? error.message : '保存分类失败，请稍后重试'
  } finally {
    if (isCurrentSave(version, token)) isSaving.value = false
  }
}

// 弹窗关闭后丢弃未保存输入，并通知父组件
function handleClosed(): void {
  resetForm()
  emit('closed')
}

// 打开、切换分类或更换会话时，使上一次保存结果失效
watch(
  () => [props.modelValue, props.category?.id, authStore.sessionId] as const,
  ([visible]) => {
    formVersion += 1
    isSaving.value = false
    if (visible) prepareForm()
    else resetForm()
  },
  { immediate: true, flush: 'sync' },
)

onBeforeUnmount(() => { formVersion += 1 })
</script>

<template>
  <AdminDialog
    v-model="dialogVisible"
    :close-disabled="isSaving"
    :title="dialogTitle"
    description="设置分类名称、父分类、排序和启用状态"
    @closed="handleClosed"
  >
    <!-- 新增与编辑共用校验，保存期间禁用输入 -->
    <form :id="formId" class="category-form" novalidate @submit.prevent="handleSubmit">
      <div class="form-field">
        <label :for="`${formId}-name`">分类名称</label>

        <input
          :id="`${formId}-name`"
          v-model="form.name"
          :disabled="isSaving"
          type="text"
          maxlength="100"
          placeholder="请输入分类名称"
        />
      </div>

      <div class="form-field">
        <label :for="`${formId}-parent`">父分类</label>

        <select :id="`${formId}-parent`" v-model="form.parentId" :disabled="isSaving">
          <option :value="null">无（作为大分类）</option>

          <option v-for="category in parentOptions" :key="category.id" :value="category.id">
            {{ category.name }}
          </option>
        </select>
      </div>

      <div class="form-field">
        <label :for="`${formId}-sort`">排序值</label>

        <input
          :id="`${formId}-sort`"
          v-model.number="form.sortOrder"
          :disabled="isSaving"
          type="number"
          min="0"
          max="2147483647"
          step="1"
          placeholder="默认 0"
        />

        <p class="field-hint">数值越小越靠前</p>
      </div>

      <div class="form-field">
        <label :for="`${formId}-status`">是否启用</label>

        <select :id="`${formId}-status`" v-model="form.isActive" :disabled="isSaving">
          <option :value="true">启用</option>
          <option :value="false">停用</option>
        </select>
      </div>
    </form>

    <!-- 保存失败时保留输入并显示校验或接口错误 -->
    <div role="status" aria-live="polite" aria-atomic="true">
      <p v-if="noticeMessage" class="notice-message">
        {{ noticeMessage }}
      </p>
    </div>

    <!-- 保存按钮通过 form 属性关联表单 -->
    <template #actions>
      <button class="save-button" type="submit" :form="formId" :disabled="isSaving">
        {{ isSaving ? '正在保存…' : '保存分类' }}
      </button>
    </template>
  </AdminDialog>
</template>

<style scoped>
/* 桌面表单使用两列排列 */
.category-form {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  align-items: start;
  gap: 20px;
}

/* 标签和输入控件上下排列 */
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

/* 输入框和下拉框 */
.form-field input,
.form-field select {
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

.form-field input:focus,
.form-field select:focus {
  outline: none;
  border-color: #83a9fa;
  box-shadow: 0 0 0 3px rgba(40, 100, 232, 0.08);
}

/* 字段补充说明 */
.field-hint {
  margin: 0;
  color: #9aa7ba;
  font-size: 11px;
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

/* 保存按钮 */
.save-button {
  padding: 8px 15px;
  border: 1px solid #2864e8;
  border-radius: 7px;
  background: #2864e8;
  color: #ffffff;
  font-size: 12px;
}

.save-button:hover:not(:disabled) {
  background: #1e55d0;
}

/* 保存期间禁用输入和重复提交 */
.form-field input:disabled,
.form-field select:disabled,
.save-button:disabled {
  opacity: 0.65;
  cursor: wait;
}

/* 手机上改为单列表单 */
@media (max-width: 600px) {
  .category-form {
    grid-template-columns: minmax(0, 1fr);
    gap: 18px;
  }
}
</style>
