<script setup lang="ts">
import { computed, onBeforeUnmount, ref, useId, watch } from 'vue'
import { getProductSkus, type ProductSkuResponse } from '@/api/products'
import { addCartItem, CartUnauthorizedError } from '@/api/cart'
import { useAuthStore } from '@/stores/auth'
import BaseDialog from '@/components/admin/AdminDialog.vue'

// 接收当前商品及弹窗显示状态
const props = withDefaults(
  defineProps<{
    modelValue: boolean
    productId: number
    productName?: string
  }>(),
  { productName: '' },
)

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
}>()

const authStore = useAuthStore()
const selectId: string = `${useId()}-sku-select`
const quantityId: string = `${selectId}-quantity`

// 当前商品的 SKU 和用户选择
const skus = ref<ProductSkuResponse[]>([])
const selectedSkuId = ref<number | null>(null)
const quantity = ref<number | string>(1)

// 商品规格加载状态
const isLoading = ref<boolean>(false)
const loadError = ref<string>('')

// 加入购物车的提交状态和提示
const isSubmitting = ref<boolean>(false)
const submitError = ref<string>('')
const successMessage = ref<string>('')

// 关闭或切换商品后，让旧请求失效
let requestVersion: number = 0

// 取得当前选中的 SKU
const selectedSku = computed<ProductSkuResponse | null>(
  () => skus.value.find((sku) => sku.id === selectedSkuId.value) ?? null,
)

// 有可售 SKU 且没有其他操作时，允许点击加入购物车
const canAddToCart = computed<boolean>(() => {
  const sku: ProductSkuResponse | null = selectedSku.value

  return (
    props.modelValue &&
    !isLoading.value &&
    !loadError.value &&
    !isSubmitting.value &&
    sku !== null &&
    sku.isActive &&
    sku.stock > 0
  )
})

// 提交期间阻止关闭，避免重复打开后再次提交
const dialogVisible = computed<boolean>({
  get: () => props.modelValue,
  set: (visible: boolean) => {
    if (!isSubmitting.value) {
      emit('update:modelValue', visible)
    }
  },
})

// 判断异步结果是否仍属于当前弹窗
function isCurrentRequest(version: number, productId: number): boolean {
  return (
    version === requestVersion &&
    props.modelValue &&
    props.productId === productId
  )
}

// 查询当前商品的 SKU，失败后可重新加载
async function loadSkus(): Promise<void> {
  if (!props.modelValue || isLoading.value || isSubmitting.value) {
    return
  }

  const productId: number = props.productId

  if (!Number.isInteger(productId) || productId <= 0) {
    loadError.value = '商品编号无效，请关闭后重新打开'
    return
  }

  const version: number = ++requestVersion

  isLoading.value = true
  loadError.value = ''
  skus.value = []
  selectedSkuId.value = null

  try {
    const result: ProductSkuResponse[] = await getProductSkus(productId)

    if (isCurrentRequest(version, productId)) {
      skus.value = result
    }
  } catch (error: unknown) {
    if (isCurrentRequest(version, productId)) {
      loadError.value =
        error instanceof Error ? error.message : '获取商品规格失败，请稍后重试'
    }
  } finally {
    if (isCurrentRequest(version, productId)) {
      isLoading.value = false
    }
  }
}

// 校验规格、数量和登录状态，然后加入购物车
async function handleAddToCart(): Promise<void> {
  if (!props.modelValue || isSubmitting.value) {
    return
  }

  submitError.value = ''
  successMessage.value = ''

  if (!authStore.isAuthenticated) {
    submitError.value = '请先登录，再加入购物车'
    return
  }

  const sku: ProductSkuResponse | null = selectedSku.value

  if (!canAddToCart.value || sku === null) {
    submitError.value = '请选择有库存的商品规格'
    return
  }

  const purchaseQuantity: number | string = quantity.value

  if (
    typeof purchaseQuantity !== 'number' ||
    !Number.isSafeInteger(purchaseQuantity) ||
    purchaseQuantity <= 0
  ) {
    submitError.value = '购买数量必须为正整数'
    return
  }

  if (purchaseQuantity > sku.stock) {
    submitError.value = `购买数量不能超过 ${sku.stock} 件`
    return
  }

  const version: number = requestVersion
  const productId: number = props.productId
  const accessToken: string = authStore.accessToken

  isSubmitting.value = true

  try {
    await addCartItem(
      {
        skuId: sku.id,
        quantity: purchaseQuantity,
      },
      accessToken,
    )

    if (
      isCurrentRequest(version, productId) &&
      authStore.isCurrentSession(accessToken)
    ) {
      successMessage.value = '已加入购物车'
    }
  } catch (error: unknown) {
    const sameSession: boolean = authStore.isCurrentSession(accessToken)

    // 只清理本次请求所属的登录状态
    if (error instanceof CartUnauthorizedError && sameSession) {
      authStore.clearSession(true)
      authStore.sessionExpired = true
    }

    if (isCurrentRequest(version, productId) && sameSession) {
      submitError.value =
        error instanceof Error ? error.message : '加入购物车失败，请稍后重试'
    }
  } finally {
    if (isCurrentRequest(version, productId)) {
      isSubmitting.value = false
    }
  }
}

// 切换规格时恢复数量，并清理上次提示
watch(
  selectedSkuId,
  () => {
    quantity.value = 1
    submitError.value = ''
    successMessage.value = ''
  },
  { flush: 'sync' },
)

// 修改数量或切换登录状态后，清理上次提示
watch(
  [quantity, () => authStore.sessionId],
  () => {
    submitError.value = ''
    successMessage.value = ''
  },
  { flush: 'sync' },
)

// 打开或切换商品时重新查询，关闭时清理状态
watch(
  () => [props.modelValue, props.productId] as const,
  ([visible]) => {
    requestVersion += 1
    skus.value = []
    selectedSkuId.value = null
    quantity.value = 1
    isLoading.value = false
    loadError.value = ''
    isSubmitting.value = false
    submitError.value = ''
    successMessage.value = ''

    if (visible) {
      void loadSkus()
    }
  },
  { immediate: true, flush: 'sync' },
)

// 离页后忽略旧请求结果
onBeforeUnmount(() => {
  requestVersion += 1
})
</script>

<template>
  <BaseDialog
    v-model="dialogVisible"
    :close-disabled="isSubmitting"
    title="选择规格"
    :description="props.productName"
    close-text="关闭"
  >
    <div class="sku-selector" :aria-busy="isLoading || isSubmitting">
      <p v-if="isLoading" class="message" role="status">
        正在加载商品规格…
      </p>

      <div v-else-if="loadError" class="error-panel">
        <p class="error-message" role="alert">{{ loadError }}</p>
        <button class="retry-button" type="button" @click="loadSkus">
          重新加载
        </button>
      </div>

      <p v-else-if="skus.length === 0" class="message" role="status">
        当前商品暂无可选规格。
      </p>

      <template v-else>
        <div class="selection-field">
          <label :for="selectId">规格组合</label>

          <select
            :id="selectId"
            v-model="selectedSkuId"
            class="sku-select"
            :disabled="isSubmitting"
          >
            <option :value="null" disabled>请选择规格</option>

            <option v-for="sku in skus" :key="sku.id" :value="sku.id">
              {{ sku.specificationText || '默认规格' }}
              {{ sku.stock === 0 ? '（售罄）' : '' }}
            </option>
          </select>
        </div>

        <template v-if="selectedSku !== null">
          <dl class="sku-information" aria-live="polite">
            <div>
              <dt>售价</dt>
              <dd class="price">¥{{ selectedSku.price.toFixed(2) }}</dd>
            </div>

            <div>
              <dt>库存</dt>
              <dd :class="{ 'sold-out': selectedSku.stock === 0 }">
                {{ selectedSku.stock === 0 ? '售罄' : selectedSku.stock + ' 件' }}
              </dd>
            </div>
          </dl>

          <div class="selection-field quantity-field">
            <label :for="quantityId">购买数量</label>
            <input
              :id="quantityId"
              v-model.number="quantity"
              class="sku-select"
              type="number"
              min="1"
              :max="Math.max(1, selectedSku.stock)"
              step="1"
              :disabled="isSubmitting || selectedSku.stock === 0"
            />
          </div>
        </template>

        <p v-else class="selection-hint">
          请选择规格，查看对应售价和库存。
        </p>
      </template>

      <p v-if="submitError" class="error-message cart-result" role="alert">
        {{ submitError }}
      </p>

      <p v-if="successMessage" class="success-message cart-result" role="status">
        {{ successMessage }}
      </p>
    </div>

    <template #actions>
      <button
        class="cart-button"
        type="button"
        :disabled="!canAddToCart"
        @click="handleAddToCart"
      >
        {{ isSubmitting ? '正在加入…' : '加入购物车' }}
      </button>
    </template>
  </BaseDialog>
</template>

<style scoped>
.sku-selector {
  min-width: 0;
  font-size: 13px;
}

.message,
.error-message {
  margin: 0;
  line-height: 1.7;
}

.message,
.selection-hint {
  color: #8793a5;
}

.error-message {
  color: #bd4848;
}

.error-panel {
  display: grid;
  justify-items: start;
  gap: 14px;
}

.selection-field {
  display: grid;
  gap: 10px;
}

.selection-field label {
  color: #6f7f96;
}

.sku-select {
  box-sizing: border-box;
  width: 100%;
  min-width: 0;
  min-height: 42px;
  padding: 10px 12px;
  border: 1px solid #e0e6f0;
  border-radius: 7px;
  background: #ffffff;
  color: #334155;
  font: inherit;
}

.sku-select:focus {
  outline: none;
  border-color: #83a9fa;
  box-shadow: 0 0 0 3px rgb(40 100 232 / 8%);
}

.sku-information {
  margin: 18px 0 0;
}

.sku-information > div {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 14px 0;
  border-bottom: 1px solid #edf1f6;
}

.sku-information dt {
  color: #8793a5;
}

.sku-information dd {
  margin: 0;
  color: #334155;
}

.sku-information .price {
  color: #2864e8;
  font-size: 21px;
}

.sku-information .sold-out {
  color: #bd4848;
}

.selection-hint {
  margin: 18px 0 0;
  line-height: 1.7;
}

.retry-button {
  padding: 9px 15px;
  border: 1px solid #dfe6ef;
  border-radius: 7px;
  background: #ffffff;
  color: #52617a;
  font: inherit;
  cursor: pointer;
}

.retry-button:hover {
  background: #f6f9ff;
}

.retry-button:focus-visible {
  outline: 2px solid #83a9fa;
  outline-offset: 3px;
}

.quantity-field {
  margin-top: 18px;
}

.cart-result {
  margin-top: 16px;
  line-height: 1.7;
}

.success-message {
  color: #21834b;
}

.cart-button {
  padding: 9px 16px;
  border: none;
  border-radius: 7px;
  background: #2864e8;
  color: #ffffff;
  font: inherit;
  cursor: pointer;
}

.cart-button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.cart-button:focus-visible {
  outline: 2px solid #83a9fa;
  outline-offset: 3px;
}
</style>
