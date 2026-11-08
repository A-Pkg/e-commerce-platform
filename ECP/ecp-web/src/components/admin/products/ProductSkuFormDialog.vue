<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, ref, useId, watch } from 'vue'
import {
  createAdminProductSku,
  updateAdminProductSku,
  type CreateProductSkuRequest,
  type UpdateProductSkuRequest,
  type ProductSkuResponse,
} from '@/api/products'
import {
  getAdminProductSpecifications,
  type ProductSpecificationResponse,
} from '@/api/productSpecifications'
import AdminDialog from '@/components/admin/AdminDialog.vue'
import { useAuthStore } from '@/stores/auth'

// sku 为空表示新增，有值表示编辑
const props = withDefaults(
  defineProps<{
    modelValue: boolean
    productId: number
    productName?: string
    sku?: ProductSkuResponse | null
  }>(),
  {
    productName: '',
    sku: null,
  },
)

// 保存成功后通知父组件重新查询对应商品
const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  saved: [productId: number]
}>()

const authStore = useAuthStore()
const formId: string = `${useId()}-sku-form`

// 使用字符串保留原始输入，避免空值被转换成零
const form = reactive({
  price: '',
  stock: '0',
  isActive: true,
})

// 保存打开编辑时的副本，避免直接修改列表数据
const editingSku = ref<ProductSkuResponse | null>(null)
const isEditing = computed<boolean>(() => editingSku.value !== null)

// 新增时加载可选规格，编辑时直接展示原规格文字
const specifications = ref<ProductSpecificationResponse[]>([])
const selectedValueIds = ref<Record<number, number | null>>({})

const isLoading = ref<boolean>(false)
const isSaving = ref<boolean>(false)
const loadError = ref<string>('')
const saveError = ref<string>('')

const isBusy = computed<boolean>(() => isLoading.value || isSaving.value)
const cannotSubmit = computed<boolean>(
  () => isBusy.value || loadError.value !== '',
)

const selectableSpecifications = computed<ProductSpecificationResponse[]>(() =>
  specifications.value.filter((specification) => specification.values.length > 0),
)

// 每次打开、关闭或切换对象，使旧请求失效
let sessionVersion: number = 0

// 相同数据重试时复用标识，提交不同数据时生成新标识
let lastEditPayload: string = ''
let editRequestId: string = ''

// 保存期间拦截取消、关闭和 Escape
const dialogVisible = computed<boolean>({
  get: () => props.modelValue,
  set: (visible: boolean) => {
    if (!visible && isSaving.value) {
      return
    }

    emit('update:modelValue', visible)
  },
})

// 判断结果是否仍属于当前表单
function isCurrentSession(version: number, productId: number): boolean {
  return (
    version === sessionVersion &&
    props.modelValue &&
    props.productId === productId
  )
}

// 清理表单，并为编辑模式创建数据副本
function resetForm(): void {
  form.price = ''
  form.stock = '0'
  form.isActive = true
  editingSku.value = null
  specifications.value = []
  selectedValueIds.value = {}
  isLoading.value = false
  isSaving.value = false
  loadError.value = ''
  saveError.value = ''
  lastEditPayload = ''
  editRequestId = ''

  if (!props.modelValue) {
    return
  }

  if (!Number.isInteger(props.productId) || props.productId <= 0) {
    loadError.value = '商品编号无效，请关闭后重新打开'
    return
  }

  if (props.sku !== null) {
    if (
      props.sku.productId !== props.productId ||
      !Number.isInteger(props.sku.id) ||
      props.sku.id <= 0
    ) {
      loadError.value = 'SKU 信息无效，请关闭后重新打开'
      return
    }

    editingSku.value = { ...props.sku }
    form.price = props.sku.price.toFixed(2)
    form.stock = String(props.sku.stock)
    form.isActive = props.sku.isActive
  }
}

// 新增时查询规格，失败后允许重试
async function loadSpecifications(): Promise<void> {
  if (
    !props.modelValue ||
    props.sku !== null ||
    isBusy.value ||
    !Number.isInteger(props.productId) ||
    props.productId <= 0
  ) {
    return
  }

  const productId: number = props.productId
  const version: number = sessionVersion

  isLoading.value = true
  loadError.value = ''

  try {
    const result: ProductSpecificationResponse[] =
      await getAdminProductSpecifications(productId, authStore.accessToken)

    if (!isCurrentSession(version, productId)) {
      return
    }

    const selections: Record<number, number | null> = {}

    for (const specification of result) {
      selections[specification.id] = null
    }

    specifications.value = result
    selectedValueIds.value = selections
  } catch (error: unknown) {
    if (!isCurrentSession(version, productId)) {
      return
    }

    loadError.value =
      error instanceof Error ? error.message : '加载商品规格失败，请稍后重试'
  } finally {
    if (isCurrentSession(version, productId)) {
      isLoading.value = false
    }
  }
}

// 校验表单，并根据模式新增或编辑 SKU
async function handleSubmit(): Promise<void> {
  if (!props.modelValue || cannotSubmit.value) {
    return
  }

  saveError.value = ''

  const priceText: string = form.price.trim()

  if (!/^\d+(?:\.\d{1,2})?$/.test(priceText)) {
    saveError.value = '请输入有效售价，最多保留两位小数'
    return
  }

  const price: number = Number(priceText)
  const priceParts: string[] = priceText.split('.')
  const integerPart: string = (priceParts[0] ?? '').replace(/^0+(?=\d)/, '')
  const decimalPart: string = (priceParts[1] ?? '').padEnd(2, '0')
  const normalizedPrice: string = `${integerPart}.${decimalPart}`

  // 防止金额在转换为 JavaScript 数字后发生精度变化
  if (
    !Number.isFinite(price) ||
    price >= 10_000_000_000_000_000 ||
    price.toFixed(2) !== normalizedPrice
  ) {
    saveError.value = '售价过大，无法准确提交，请调小金额'
    return
  }

  const stockText: string = form.stock.trim()

  if (!/^\d+$/.test(stockText)) {
    saveError.value = '库存必须为非负整数'
    return
  }

  const stock: number = Number(stockText)

  if (!Number.isInteger(stock) || stock > 2_147_483_647) {
    saveError.value = '库存必须在 0 到 2147483647 之间'
    return
  }

  const productId: number = props.productId
  const version: number = sessionVersion
  const sku: ProductSkuResponse | null = editingSku.value

  isSaving.value = true

  try {
    if (sku !== null) {
      // 原库存始终取打开编辑时的副本，不随用户输入改变
      const payload: Omit<UpdateProductSkuRequest, 'requestId'> = {
        price,
        stock,
        isActive: form.isActive,
        expectedStock: sku.stock,
      }

      const payloadKey: string = JSON.stringify(payload)

      if (payloadKey !== lastEditPayload || editRequestId === '') {
        // 为新的保存内容生成 32 位随机请求标识
        const randomBytes: Uint8Array<ArrayBuffer> = new Uint8Array(16)
        crypto.getRandomValues(randomBytes)

        editRequestId = Array.from(
          randomBytes,
          (value: number) => value.toString(16).padStart(2, '0'),
        ).join('')

        lastEditPayload = payloadKey
      }

      const request: UpdateProductSkuRequest = {
        ...payload,
        requestId: editRequestId,
      }

      await updateAdminProductSku(
        productId,
        sku.id,
        request,
        authStore.accessToken,
      )
    } else {
      // 新增时只提交实际选择的规格值
      const specificationValueIds: number[] = []

      for (const specification of selectableSpecifications.value) {
        const selectedId: number | null | undefined =
          selectedValueIds.value[specification.id]

        if (selectedId === null || selectedId === undefined) {
          continue
        }

        if (!specification.values.some((value) => value.id === selectedId)) {
          throw new Error('所选规格值无效，请关闭后重新打开')
        }

        specificationValueIds.push(selectedId)
      }

      const request: CreateProductSkuRequest = {
        price,
        initialStock: stock,
        specificationValueIds,
      }

      await createAdminProductSku(productId, request, authStore.accessToken)
    }

    if (!isCurrentSession(version, productId)) {
      return
    }

    // 由父组件重新读取后端实际保存的数据
    isSaving.value = false
    emit('saved', productId)

    if (isCurrentSession(version, productId)) {
      emit('update:modelValue', false)
    }
  } catch (error: unknown) {
    if (!isCurrentSession(version, productId)) {
      return
    }

    // 保存失败时保留输入，允许重试
    saveError.value =
      error instanceof Error ? error.message : '保存 SKU 失败，请稍后重试'
  } finally {
    if (isCurrentSession(version, productId)) {
      isSaving.value = false
    }
  }
}

// 每次打开或切换商品、SKU 时重新准备表单
watch(
  () => [props.modelValue, props.productId, props.sku?.id ?? null, authStore.sessionId, authStore.isAdmin] as const,
  ([visible]) => {
    sessionVersion += 1
    resetForm()

    if (visible && authStore.isAdmin && loadError.value === '' && !isEditing.value) {
      void loadSpecifications()
    }
  },
  { immediate: true, flush: 'sync' },
)

// 卸载后丢弃旧请求结果
onBeforeUnmount(() => {
  sessionVersion += 1
})
</script>

<template>
  <AdminDialog
    v-model="dialogVisible"
    :close-disabled="isSaving"
    :title="isEditing ? '编辑 SKU' : '新增 SKU'"
    :description="props.productName"
  >
    <form
      :id="formId"
      class="sku-form"
      novalidate
      :aria-busy="isBusy"
      @submit.prevent="handleSubmit"
    >
      <fieldset class="form-controls" :disabled="isBusy">
        <!-- 规格区只负责选择已有规格值 -->
        <section class="form-box" :aria-labelledby="`${formId}-specifications`">
          <h3 :id="`${formId}-specifications`">规格组合</h3>

          <!-- 编辑时规格固定，只展示原有组合 -->
          <p v-if="isEditing" class="state-message">
            {{ editingSku?.specificationText || '默认 SKU（未选择规格）' }}
          </p>

          <p v-else-if="isLoading" class="state-message" role="status">
            正在加载商品规格…
          </p>

          <div v-else-if="loadError" class="load-error">
            <p class="error-message" role="alert">{{ loadError }}</p>

            <button
              class="retry-button"
              type="button"
              @click="loadSpecifications"
            >
              重新加载
            </button>
          </div>

          <template v-else>
            <p class="box-description">
              每种规格最多选择一个值；全部不选时创建默认 SKU。
            </p>

            <div v-if="selectableSpecifications.length > 0" class="specification-fields">
              <div
                v-for="specification in selectableSpecifications"
                :key="specification.id"
                class="form-field"
              >
                <label :for="`${formId}-specification-${specification.id}`">
                  {{ specification.name }}
                </label>

                <select
                  :id="`${formId}-specification-${specification.id}`"
                  v-model="selectedValueIds[specification.id]"
                >
                  <option :value="null">不选择</option>

                  <option
                    v-for="value in specification.values"
                    :key="value.id"
                    :value="value.id"
                  >
                    {{ value.value }}
                  </option>
                </select>
              </div>
            </div>

            <p v-else class="state-message">当前商品没有可选规格值。</p>

          </template>
        </section>

        <!-- 售价和库存与规格选择分开，避免概念混淆 -->
        <section class="form-box" :aria-labelledby="`${formId}-pricing`">
          <h3 :id="`${formId}-pricing`">售价与库存</h3>

          <div class="pricing-fields">
            <div class="form-field">
              <label :for="`${formId}-price`">售价（元）</label>

              <input
                :id="`${formId}-price`"
                v-model="form.price"
                type="text"
                inputmode="decimal"
                maxlength="32"
                placeholder="例如：19.90"
                autocomplete="off"
              />
            </div>

            <div class="form-field">
              <label :for="`${formId}-stock`">库存</label>

              <input
                :id="`${formId}-stock`"
                v-model="form.stock"
                type="text"
                inputmode="numeric"
                maxlength="10"
                autocomplete="off"
              />
            </div>

            <!-- 创建时沿用后端默认启用，编辑时允许设置状态 -->
            <div v-if="isEditing" class="form-field">
              <label :for="`${formId}-active`">状态</label>
              <select :id="`${formId}-active`" v-model="form.isActive">
                <option :value="true">启用</option>
                <option :value="false">停用</option>
              </select>
            </div>
          </div>
        </section>
      </fieldset>

      <p v-if="saveError" class="submit-error" role="alert">
        {{ saveError }}
      </p>
    </form>

    <template #actions>
      <button
        class="save-button"
        type="submit"
        :form="formId"
        :disabled="cannotSubmit"
      >
        {{ isSaving ? '正在保存…' : '保存 SKU' }}
      </button>
    </template>
  </AdminDialog>
</template>

<style scoped>
.sku-form,
.form-controls {
  min-width: 0;
}

.form-controls {
  display: grid;
  gap: 16px;
  margin: 0;
  padding: 0;
  border: none;
}

/* 两个独立 box 区分规格选择与价格库存 */
.form-box {
  min-width: 0;
  padding: 18px;
  border: 1px solid #e0e6f0;
  border-radius: 8px;
  background: #ffffff;
}

.form-box h3 {
  margin: 0 0 14px;
  color: #28364b;
  font-size: 14px;
  font-weight: 600;
}

.box-description,
.state-message {
  color: #7b899d;
  font-size: 12px;
  line-height: 1.6;
}

.box-description {
  margin: 0 0 14px;
}

.state-message {
  margin: 0;
}

/* 规格较多时，只滚动下拉框所在区域 */
.specification-fields,
.pricing-fields {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
}

.specification-fields {
  max-height: 240px;
  padding: 3px;
  overflow-y: auto;
}

.form-field {
  display: flex;
  min-width: 0;
  flex-direction: column;
  gap: 8px;
}

.form-field label {
  color: #6f7f96;
  font-size: 12px;
}

.form-field input,
.form-field select {
  box-sizing: border-box;
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

.form-field input:focus,
.form-field select:focus {
  outline: none;
  border-color: #83a9fa;
  box-shadow: 0 0 0 3px rgb(40 100 232 / 8%);
}

.form-field input::placeholder {
  color: #b0b9c8;
}

.load-error {
  display: grid;
  justify-items: start;
  gap: 12px;
}

.error-message,
.submit-error {
  margin: 0;
  color: #bc4545;
  font-size: 12px;
  line-height: 1.6;
}

.submit-error {
  margin-top: 16px;
  padding: 10px 12px;
  border: 1px solid #f0d5d5;
  border-radius: 7px;
  background: #fff7f7;
}

.retry-button,
.save-button {
  padding: 8px 15px;
  border: 1px solid #dfe6ef;
  border-radius: 7px;
  background: #ffffff;
  color: #52617a;
  font-size: 12px;
  cursor: pointer;
}

.save-button {
  border-color: #2864e8;
  background: #2864e8;
  color: #ffffff;
}

.save-button:hover:not(:disabled) {
  background: #1e55d0;
}

.form-controls:disabled input,
.form-controls:disabled select,
button:disabled {
  opacity: 0.65;
  cursor: not-allowed;
}

/* 窄屏改为单列，避免输入控件拥挤 */
@media (max-width: 600px) {
  .form-box {
    padding: 14px;
  }

  .specification-fields,
  .pricing-fields {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>