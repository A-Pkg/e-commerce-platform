<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { ProductSkuResponse } from '@/api/products'
import AdminDialog from '@/components/admin/AdminDialog.vue'
import ProductSkuFormDialog from '@/components/admin/products/ProductSkuFormDialog.vue'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    productId: number
    productName?: string
    skus?: ProductSkuResponse[]
    loading?: boolean
    error?: string
  }>(),
  {
    productName: '',
    skus: () => [],
    loading: false,
    error: '',
  },
)

// 新增和编辑统一通知商品页刷新
const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  retry: []
  saved: [productId: number]
}>()

const isFormDialogVisible = ref<boolean>(false)
const editingSku = ref<ProductSkuResponse | null>(null)

// 加载失败或表单已经打开时，禁止再次打开操作
const cannotCreate = computed<boolean>(
  () =>
    !props.modelValue ||
    props.loading ||
    props.error !== '' ||
    isFormDialogVisible.value ||
    !Number.isInteger(props.productId) ||
    props.productId <= 0,
)

// 子表单打开期间，保持下层列表弹窗打开
const dialogVisible = computed<boolean>({
  get: () => props.modelValue,
  set: (visible: boolean) => {
    if (!visible && isFormDialogVisible.value) {
      return
    }

    emit('update:modelValue', visible)
  },
})

// 打开新增表单
function openCreateDialog(): void {
  if (cannotCreate.value) {
    return
  }

  editingSku.value = null
  isFormDialogVisible.value = true
}

// 打开编辑表单，创建当前行的数据副本
function openEditDialog(sku: ProductSkuResponse): void {
  if (cannotCreate.value || sku.productId !== props.productId) {
    return
  }

  editingSku.value = { ...sku }
  isFormDialogVisible.value = true
}

// 保存成功后关闭子表单，并刷新当前商品
function handleSkuSaved(productId: number): void {
  if (
    !props.modelValue ||
    !isFormDialogVisible.value ||
    productId !== props.productId
  ) {
    return
  }

  isFormDialogVisible.value = false
  emit('saved', productId)
}

// 子表单关闭后清理编辑对象
watch(
  isFormDialogVisible,
  (visible: boolean) => {
    if (!visible) {
      editingSku.value = null
    }
  },
  { flush: 'sync' },
)

// 外层关闭或切换商品时，一并关闭子表单
watch(
  () => [props.modelValue, props.productId] as const,
  () => {
    isFormDialogVisible.value = false
    editingSku.value = null
  },
  { flush: 'sync' },
)
</script>

<template>
  <AdminDialog
    v-model="dialogVisible"
    :close-disabled="isFormDialogVisible"
    title="SKU 管理"
    :description="props.productName"
    close-text="关闭"
  >
    <!-- 新增入口紧挨 SKU 列表 -->
    <div class="list-toolbar">
      <h3>SKU 列表</h3>

      <button
        class="create-button"
        type="button"
        :disabled="cannotCreate"
        @click="openCreateDialog"
      >
        新增 SKU
      </button>
    </div>

    <p class="list-description">
      每个 SKU 分别管理自己的规格组合、售价和库存。
    </p>

    <!-- 窄屏下只在表格区域横向滚动 -->
    <div
      class="table-wrapper"
      role="region"
      aria-label="SKU 列表，可横向滚动"
      :aria-busy="props.loading"
      tabindex="0"
    >
      <table class="sku-table">
        <thead>
          <tr>
            <th scope="col">SKU 编码</th>
            <th scope="col">规格组合</th>
            <th scope="col">价格（元）</th>
            <th scope="col">库存</th>
            <th scope="col">状态</th>
            <th scope="col">操作</th>
          </tr>
        </thead>

        <tbody>
          <!-- 查询尚未结束 -->
          <tr v-if="props.loading">
            <td colspan="6" class="message-cell" role="status">正在加载 SKU…</td>
          </tr>

          <!-- 查询失败，可重新请求 -->
          <tr v-else-if="props.error">
            <td colspan="6" class="message-cell">
              <p class="error-message" role="alert">{{ props.error }}</p>
              <button class="retry-button" type="button" @click="emit('retry')">重新加载</button>
            </td>
          </tr>

          <!-- 查询成功，但该商品没有 SKU -->
          <tr v-else-if="props.skus.length === 0">
            <td colspan="6" class="message-cell">暂无 SKU</td>
          </tr>

          <!-- 查询成功，展示真实 SKU -->
          <template v-else>
            <tr v-for="sku in props.skus" :key="sku.id">
              <td>
                <span class="sku-code">{{ sku.skuCode }}</span>
              </td>

              <td>
                <span class="specification-text">
                  {{ sku.specificationText || '—' }}
                </span>
              </td>

              <td class="numeric-cell">{{ sku.price.toFixed(2) }}</td>
              <td class="numeric-cell">{{ sku.stock }}</td>

              <td>
                <span class="status-badge" :class="{ 'is-active': sku.isActive }">
                  {{ sku.isActive ? '启用' : '停用' }}
                </span>
              </td>
              <td>
                <button
                  class="status-button"
                  type="button"
                  :disabled="cannotCreate"
                  :aria-label="'编辑 SKU：' + sku.skuCode"
                  @click="openEditDialog(sku)"
                >
                  编辑
                </button>
              </td>
            </tr>
          </template>
        </tbody>
      </table>
    </div>
  </AdminDialog>

  <!-- 新增和编辑共用表单，关闭后卸载 -->
  <ProductSkuFormDialog
    v-if="props.modelValue && isFormDialogVisible"
    :key="props.productId + ':' + (editingSku?.id ?? 'new')"
    v-model="isFormDialogVisible"
    :product-id="props.productId"
    :product-name="props.productName"
    :sku="editingSku"
    @saved="handleSkuSaved"
  />
</template>

<style scoped>
/* 列表说明 */
.list-description {
  margin: 0 0 18px;
  color: #8b99ad;
  font-size: 12px;
}

/* 表格滚动容器 */
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

.sku-table {
  width: 100%;
  min-width: 680px;
  border-collapse: collapse;
  text-align: left;
}

.sku-table th {
  padding: 12px;
  border-bottom: 1px solid #e6ecf5;
  background: #f8fafe;
  color: #8c99ad;
  font-size: 12px;
  font-weight: 400;
  white-space: nowrap;
}

.sku-table td {
  padding: 14px 12px;
  border-bottom: 1px solid #edf1f7;
  color: #67768e;
  font-size: 12px;
}

.sku-table tbody tr:last-child td {
  border-bottom: none;
}

/* 编码和规格较长时允许换行 */
.sku-code,
.specification-text {
  display: block;
  min-width: 90px;
  max-width: 180px;
  overflow-wrap: anywhere;
}

.numeric-cell {
  white-space: nowrap;
  font-variant-numeric: tabular-nums;
}

/* 只读状态标签 */
.status-badge {
  display: inline-block;
  padding: 3px 8px;
  border-radius: 5px;
  background: #f0f3f8;
  color: #7f8ba0;
  white-space: nowrap;
}

.status-badge.is-active {
  background: #eef8f2;
  color: #468467;
}

/* 加载、错误和空数据提示 */
.sku-table .message-cell {
  padding: 48px 20px;
  color: #9aa7ba;
  text-align: center;
}

.error-message {
  margin: 0 0 12px;
  color: #bd4848;
}

.retry-button {
  padding: 8px 15px;
  border: 1px solid #dfe6ef;
  border-radius: 7px;
  background: #ffffff;
  color: #65738b;
  font-size: 12px;
}

.retry-button:hover {
  border-color: #c9d9f5;
  background: #f6f9ff;
}

/* 标题和新增按钮放在同一行 */
.list-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
}

.list-toolbar h3 {
  margin: 0;
  color: #28364b;
  font-size: 14px;
  font-weight: 600;
}

.create-button {
  flex-shrink: 0;
  padding: 8px 15px;
  border: 1px solid #2864e8;
  border-radius: 7px;
  background: #2864e8;
  color: #ffffff;
  font-size: 12px;
  cursor: pointer;
}

.create-button:hover:not(:disabled) {
  background: #1e55d0;
}

.create-button:disabled {
  opacity: 0.65;
  cursor: not-allowed;
}

/* SKU 编辑操作 */
.status-button {
  padding: 4px 0;
  border: none;
  background: transparent;
  color: #648dd6;
  font-size: 12px;
  white-space: nowrap;
  cursor: pointer;
}

.status-button:hover:not(:disabled) {
  color: #2864e8;
}

.status-button:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.status-button:focus-visible {
  outline: 2px solid #93b6ff;
  outline-offset: 3px;
}
</style>
