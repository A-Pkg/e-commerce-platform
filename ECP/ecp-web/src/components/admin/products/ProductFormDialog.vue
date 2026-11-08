<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, ref, useId, watch } from 'vue'
import {
  createAdminProduct,
  updateAdminProduct,
  type CreateProductRequest,
  type UpdateProductRequest,
  type ProductResponse,
} from '@/api/products'
import {
  uploadProductCover,
  ProductImageUnauthorizedError,
  type ProductCoverResponse,
} from '@/api/productImages'
import AdminDialog from '@/components/admin/AdminDialog.vue'
import { useAuthStore } from '@/stores/auth'

// 商品公共信息表单
interface ProductFormData {
  name: string
  categoryId: number | null
  description: string
  isOnSale: boolean
}

// 分类下拉框选项
interface CategoryOption {
  id: number
  name: string
}

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    product?: ProductResponse | null
    categories?: CategoryOption[]
  }>(),
  {
    product: null,
    categories: () => [],
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  saved: [product: ProductResponse]
  closed: []
}>()

const authStore = useAuthStore()
const formId: string = useId() + '-product-form'

// 商品保存和封面上传分别记录状态
const isSaving = ref<boolean>(false)
const isUploading = ref<boolean>(false)
const isBusy = computed<boolean>(
  () => isSaving.value || isUploading.value,
)

// 封面选择、预览和操作提示
const coverInput = ref<HTMLInputElement | null>(null)
const selectedCoverFile = ref<File | null>(null)
const coverPreviewUrl = ref<string>('')
const coverPreviewFailed = ref<boolean>(false)
const coverError = ref<string>('')
const coverSuccess = ref<string>('')

// 打开、关闭或切换商品后，使旧请求结果失效
let sessionVersion: number = 0

// 保存或上传期间，拦截关闭按钮和 Escape
const dialogVisible = computed<boolean>({
  get: () => props.modelValue,
  set: (value: boolean) => {
    if (isBusy.value && !value) {
      return
    }

    emit('update:modelValue', value)
  },
})

const dialogTitle = computed<string>(
  () => props.product === null ? '新增商品' : '编辑商品',
)

const form = reactive<ProductFormData>({
  name: '',
  categoryId: null,
  description: '',
  isOnSale: false,
})

const noticeMessage = ref<string>('')

// 判断结果是否仍属于当前打开的商品表单
function isCurrentSession(
  version: number,
  productId: number | null,
): boolean {
  return (
    version === sessionVersion &&
    props.modelValue &&
    (props.product?.id ?? null) === productId
  )
}

// 清理并回填商品信息
function prepareForm(): void {
  form.name = props.product?.name ?? ''
  form.categoryId = props.product?.categoryId ?? null
  form.description = props.product?.description ?? ''
  form.isOnSale = props.product?.isOnSale ?? false
  noticeMessage.value = ''
}

// 清理封面操作状态，编辑时读取当前封面
function prepareCover(): void {
  selectedCoverFile.value = null
  coverPreviewUrl.value = ''
  coverPreviewFailed.value = false
  coverError.value = ''
  coverSuccess.value = ''

  if (coverInput.value !== null) {
    coverInput.value.value = ''
  }

  if (props.modelValue && props.product !== null) {
    coverPreviewUrl.value =
      '/api/products/' + props.product.id +
      '/cover-image?preview=' + sessionVersion
  }
}

// 选择图片时只校验，点击上传按钮后才保存
function handleCoverSelected(event: Event): void {
  if (isBusy.value) {
    return
  }

  const input: HTMLInputElement = event.target as HTMLInputElement
  const file: File | undefined = input.files?.[0]

  selectedCoverFile.value = null
  coverError.value = ''
  coverSuccess.value = ''

  if (file === undefined) {
    return
  }

  if (!/\.(jpe?g|png)$/i.test(file.name)) {
    coverError.value = '请选择 JPG/JPEG 或 PNG 图片'
    input.value = ''
    return
  }

  if (file.size === 0 || file.size > 2 * 1024 * 1024) {
    coverError.value = '图片不能为空，且不能超过 2MB'
    input.value = ''
    return
  }

  selectedCoverFile.value = file
}

// 图片不存在或无法加载时显示占位
function handleCoverPreviewError(event: Event): void {
  const image: HTMLImageElement = event.target as HTMLImageElement

  if (
    props.modelValue &&
    image.getAttribute('src') === coverPreviewUrl.value
  ) {
    coverPreviewFailed.value = true
  }
}

// 上传封面，成功后立即更新预览
async function handleCoverUpload(): Promise<void> {
  if (!props.modelValue || isBusy.value || props.product === null) {
    return
  }

  const file: File | null = selectedCoverFile.value

  if (file === null) {
    coverError.value = '请先选择图片'
    return
  }

  const productId: number = props.product.id
  const version: number = sessionVersion
  const accessToken: string = authStore.accessToken

  isUploading.value = true
  coverError.value = ''
  coverSuccess.value = ''

  try {
    const result: ProductCoverResponse = await uploadProductCover(
      productId,
      file,
      accessToken,
    )

    if (
      !isCurrentSession(version, productId) ||
      !authStore.isCurrentSession(accessToken)
    ) {
      return
    }

    coverPreviewFailed.value = false
    coverPreviewUrl.value = result.imageUrl
    selectedCoverFile.value = null
    coverSuccess.value = '封面已保存'

    if (coverInput.value !== null) {
      coverInput.value.value = ''
    }
  } catch (error: unknown) {
    if (
      !isCurrentSession(version, productId) ||
      !authStore.isCurrentSession(accessToken)
    ) {
      return
    }

    if (error instanceof ProductImageUnauthorizedError) {
      authStore.clearSession(true)
      authStore.sessionExpired = true
      return
    }

    coverError.value =
      error instanceof Error ? error.message : '上传封面失败，请稍后重试'
  } finally {
    if (isCurrentSession(version, productId)) {
      isUploading.value = false
    }
  }
}

// 校验并保存名称、分类和描述
async function handleSubmit(): Promise<void> {
  if (!props.modelValue || isBusy.value) {
    return
  }

  noticeMessage.value = ''

  const productName: string = form.name.trim()

  if (productName.length === 0 || productName.length > 200) {
    noticeMessage.value = '请输入 1 到 200 个字符的商品名称'
    return
  }

  const categoryId: number | null = form.categoryId

  if (
    categoryId === null ||
    !Number.isInteger(categoryId) ||
    categoryId <= 0 ||
    categoryId > 2_147_483_647
  ) {
    noticeMessage.value = '请选择有效的商品分类'
    return
  }

  if (form.description.length > 2000) {
    noticeMessage.value = '商品描述不能超过 2000 个字符'
    return
  }

  const currentProduct: ProductResponse | null = props.product
  const productId: number | null = currentProduct?.id ?? null
  const version: number = sessionVersion
  const accessToken: string = authStore.accessToken

  isSaving.value = true

  try {
    let product: ProductResponse

    if (currentProduct === null) {
      const request: CreateProductRequest = {
        categoryId,
        name: productName,
        description: form.description,
      }

      product = await createAdminProduct(request, accessToken)
    } else {
      const request: UpdateProductRequest = {
        categoryId,
        name: productName,
        description: form.description,
      }

      product = await updateAdminProduct(
        currentProduct.id,
        request,
        accessToken,
      )
    }

    if (
      !isCurrentSession(version, productId) ||
      !authStore.isCurrentSession(accessToken)
    ) {
      return
    }

    emit('saved', product)
    emit('update:modelValue', false)
  } catch (error: unknown) {
    if (
      isCurrentSession(version, productId) &&
      authStore.isCurrentSession(accessToken)
    ) {
      noticeMessage.value =
        error instanceof Error ? error.message : '保存商品失败，请稍后重试'
    }
  } finally {
    if (isCurrentSession(version, productId)) {
      isSaving.value = false
    }
  }
}

// 原生弹窗关闭后通知父组件
function handleClosed(): void {
  emit('closed')
}

// 每次打开、关闭或切换商品，都准备独立的表单状态
watch(
  () => [props.modelValue, props.product?.id ?? null, authStore.sessionId, authStore.isAdmin] as const,
  ([visible]) => {
    sessionVersion += 1
    isSaving.value = false
    isUploading.value = false
    prepareCover()

    if (visible) {
      prepareForm()
    }
  },
  { immediate: true, flush: 'sync' },
)

// 离页后不再处理旧请求
onBeforeUnmount(() => {
  sessionVersion += 1
})
</script>

<template>
  <AdminDialog
    v-model="dialogVisible"
    :close-disabled="isBusy"
    :title="dialogTitle"
    description="设置商品名称、所属分类、描述和封面"
    @closed="handleClosed"
  >
    <!-- 新增与编辑共用校验，保存期间禁用输入 -->
    <form :id="formId" class="product-form" novalidate @submit.prevent="handleSubmit">
      <div class="form-field">
        <label :for="`${formId}-name`">商品名称</label>

        <input
          :id="`${formId}-name`"
          v-model="form.name"
          :disabled="isBusy"
          type="text"
          maxlength="200"
          placeholder="请输入商品名称"
        />
      </div>

      <div class="form-field">
        <label :for="`${formId}-category`">所属分类</label>

        <select
          :id="`${formId}-category`"
          v-model="form.categoryId"
          :disabled="isBusy || props.categories.length === 0"
        >
          <option :value="null" disabled>
            {{ props.categories.length === 0 ? '暂无可选分类' : '请选择分类' }}
          </option>

          <option v-for="category in props.categories" :key="category.id" :value="category.id">
            {{ category.name }}
          </option>
        </select>
      </div>

      <!-- 封面占据整行，位于名称、分类下面 -->
      <div class="form-field full-width">
        <label v-if="props.product !== null" :for="formId + '-cover'">商品封面</label>
        <span v-else class="cover-label">商品封面</span>

        <template v-if="props.product !== null">
          <div class="cover-layout" :aria-busy="isUploading">
            <div class="cover-preview">
              <img
                v-if="coverPreviewUrl && !coverPreviewFailed"
                :key="coverPreviewUrl"
                :src="coverPreviewUrl"
                :alt="(props.product?.name ?? '商品') + '的封面'"
                @error="handleCoverPreviewError"
              />
              <span v-else>暂无可用封面</span>
            </div>

            <div class="cover-controls">
              <input
                :id="formId + '-cover'"
                ref="coverInput"
                type="file"
                accept=".jpg,.jpeg,.png,image/jpeg,image/png"
                :disabled="isBusy"
                @change="handleCoverSelected"
              />

              <p class="cover-hint">
                支持 JPG、PNG，大小不超过 2MB。点击上传后立即保存封面。
              </p>

              <button
                class="save-button"
                type="button"
                :disabled="isBusy || selectedCoverFile === null"
                @click="handleCoverUpload"
              >
                {{ isUploading ? '正在上传…' : '上传封面' }}
              </button>

              <p v-if="coverError" class="cover-error" role="alert">
                {{ coverError }}
              </p>
              <p v-if="coverSuccess" class="cover-success" role="status">
                {{ coverSuccess }}
              </p>
            </div>
          </div>
        </template>

        <p v-else class="cover-hint">
          保存商品后，可在编辑中上传封面。
        </p>
      </div>

      <div class="form-field full-width">
        <label :for="`${formId}-description`">商品描述</label>

        <textarea
          :id="`${formId}-description`"
          v-model="form.description"
          :disabled="isBusy"
          rows="5"
          maxlength="2000"
          placeholder="请输入商品描述"
        ></textarea>
      </div>

      <div class="form-field">
        <label :for="`${formId}-status`">上架状态</label>

        <select :id="`${formId}-status`" v-model="form.isOnSale" disabled>
          <option :value="false">未上架</option>
          <option :value="true">已上架</option>
        </select>
      </div>
    </form>

    <!-- 保持提示区域存在，便于辅助工具读取内容变化 -->
    <div role="status" aria-live="polite" aria-atomic="true">
      <p v-if="noticeMessage" class="notice-message">
        {{ noticeMessage }}
      </p>
    </div>

    <!-- 通过 form 属性，让底部保存按钮关联上面的表单 -->
    <template #actions>
      <button class="save-button" type="submit" :form="formId" :disabled="isBusy">
        {{ isSaving ? '正在保存…' : '保存商品' }}
      </button>
    </template>
  </AdminDialog>
</template>

<style scoped>
/* 桌面表单使用两列排列 */
.product-form {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
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

/* 描述区域占据整行 */
.full-width {
  grid-column: 1 / -1;
}

/* 表单控件的公共样式 */
.form-field input,
.form-field select,
.form-field textarea {
  width: 100%;
  min-width: 0;
  padding: 9px 11px;
  border: 1px solid #e0e6f0;
  border-radius: 7px;
  background: #ffffff;
  color: #52617a;
  font-size: 13px;
}

.form-field input,
.form-field select {
  min-height: 40px;
}

.form-field textarea {
  min-height: 120px;
  resize: vertical;
}

.form-field input::placeholder,
.form-field textarea::placeholder {
  color: #b0b9c8;
}

/* 输入时显示焦点 */
.form-field input:focus,
.form-field select:focus,
.form-field textarea:focus {
  outline: none;
  border-color: #83a9fa;
  box-shadow: 0 0 0 3px rgba(40, 100, 232, 0.08);
}

/* 分类不可编辑或只读展示上架状态时使用禁用样式 */
.form-field select:disabled {
  background: #f7f9fc;
  color: #a1adbf;
  cursor: not-allowed;
}

/* 输入校验及保存失败提示 */
.notice-message {
  margin: 18px 0 0;
  padding: 10px 14px;
  border: 1px solid #e4edff;
  border-radius: 7px;
  background: #f6f9ff;
  color: #6f8bb9;
  font-size: 12px;
}

/* 保存按钮放入共用弹窗的底部操作区域 */
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
.form-field textarea:disabled,
.save-button:disabled {
  opacity: 0.65;
  cursor: not-allowed;
}

/* 手机上改成单列表单 */
@media (max-width: 600px) {
  .product-form {
    grid-template-columns: minmax(0, 1fr);
    gap: 18px;
  }
}

.cover-label {
  color: #6f7f96;
  font-size: 12px;
}

.cover-layout {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  gap: 16px;
}

.cover-preview {
  display: grid;
  flex-shrink: 0;
  place-items: center;
  width: 120px;
  height: 120px;
  overflow: hidden;
  border: 1px solid #e0e6f0;
  border-radius: 8px;
  background: #f8faff;
  color: #9aa7b8;
  font-size: 12px;
}

.cover-preview img {
  width: 100%;
  height: 100%;
  object-fit: contain;
}

.cover-controls {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 10px;
  min-width: 180px;
}

.cover-controls .save-button {
  align-self: flex-start;
}

.cover-hint,
.cover-error,
.cover-success {
  margin: 0;
  font-size: 12px;
  line-height: 1.7;
}

.cover-hint {
  color: #8793a5;
}

.cover-error {
  color: #bd4848;
}

.cover-success {
  color: #21834b;
}
</style>
