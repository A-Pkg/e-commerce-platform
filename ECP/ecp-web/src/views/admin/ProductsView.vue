<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { getAdminCategories, type CategoryResponse } from '@/api/categories'
import {
  getAdminProducts,
  getAdminProductSkus,
  updateAdminProductSaleStatus,
  type UpdateProductSaleStatusRequest,
  type AdminProductFilters,
  type ProductResponse,
  type ProductSkuResponse,
} from '@/api/products'
import ProductFormDialog from '@/components/admin/products/ProductFormDialog.vue'
import ProductSkuDialog from '@/components/admin/products/ProductSkuDialog.vue'
import ProductSpecificationDialog from '@/components/admin/products/ProductSpecificationDialog.vue'
import { useAuthStore } from '@/stores/auth'
import type { PagedResponse } from '@/types/pagination'

// 定义分类选项，父编号用于查询大分类下的商品
interface CategoryOption {
  id: number
  parentId: number | null
  name: string
}

// 在商品公共信息上补充 SKU 查询数据，null 表示尚未查询
interface ProductListItem extends ProductResponse {
  skus: ProductSkuResponse[] | null
}

// 保存筛选输入
const filters = reactive<AdminProductFilters>({
  name: '',
  categoryId: null,
  status: '',
})

// 保存点击查询后生效的条件，输入时暂不改变列表
const appliedFilters = reactive<AdminProductFilters>({
  name: '',
  categoryId: null,
  status: '',
})

// 获取当前登录会话
const authStore = useAuthStore()

// 保存真实分类选项和商品列表
const categoryOptions = ref<CategoryOption[]>([])
const products = ref<ProductListItem[]>([])

// 分页信息以后端实际返回值为准
const pageNumber = ref<number>(1)
const pageSize = ref<number>(20)
const totalCount = ref<number>(0)
const totalPages = ref<number>(1)
let productRequestVersion: number = 0

// 保存加载状态和失败提示
const isLoading = ref<boolean>(false)
const loadError = ref<string>('')

// 控制商品新增和编辑弹窗
const isProductDialogVisible = ref<boolean>(false)

// 保存待编辑商品，null 表示新增
const editingProduct = ref<ProductListItem | null>(null)

// 控制规格管理弹窗
const isSpecificationDialogVisible = ref<boolean>(false)

// 保存当前管理规格的商品
const selectedSpecificationProduct = ref<ProductListItem | null>(null)

// 控制 SKU 管理弹窗
const isSkuDialogVisible = ref<boolean>(false)

// 保存当前管理 SKU 的商品
const selectedSkuProduct = ref<ProductListItem | null>(null)

// 保存本次弹窗查询的数据和状态
const skuRows = ref<ProductSkuResponse[]>([])
const isSkuLoading = ref<boolean>(false)
const skuLoadError = ref<string>('')

// 标记最新请求，防止旧请求覆盖当前弹窗的数据
let skuRequestVersion: number = 0

// 保存商品操作结果提示
const noticeMessage = ref<string>('')

// 保存正在设置上下架状态的商品编号，防止重复提交
const changingSaleProductId = ref<number | null>(null)

// 保存上下架操作失败原因
const saleError = ref<string>('')

// 页面卸载后，不再接收上下架请求结果
let isPageActive: boolean = true

// 判断是否存在已提交的筛选条件
const hasActiveFilters = computed<boolean>(
  () =>
    appliedFilters.name !== '' ||
    appliedFilters.categoryId !== null ||
    appliedFilters.status !== '',
)

// 查询、上下架和弹窗期间保持当前列表，避免同时切换分页
const isBusy = computed<boolean>(() =>
  isLoading.value ||
  changingSaleProductId.value !== null ||
  isProductDialogVisible.value ||
  isSpecificationDialogVisible.value ||
  isSkuDialogVisible.value,
)

// 加载当前页商品和分类选项，新增、编辑和上下架后也复用此入口
async function loadProducts(targetPage: number = pageNumber.value): Promise<void> {
  if (!isPageActive || !authStore.isAdmin || isLoading.value) {
    return
  }

  pageNumber.value = targetPage
  const version: number = ++productRequestVersion
  const accessToken: string = authStore.accessToken
  const filterSnapshot: AdminProductFilters = { ...appliedFilters }
  isLoading.value = true
  loadError.value = ''
  products.value = []

  try {
    const [productData, categoryData]: [PagedResponse<ProductResponse>, CategoryResponse[]] =
      await Promise.all([
        getAdminProducts(accessToken, filterSnapshot, targetPage),
        getAdminCategories(accessToken),
      ])

    // 离页、账号或权限改变后，丢弃旧查询结果
    if (
      !isPageActive || version !== productRequestVersion ||
      !authStore.isCurrentSession(accessToken) || !authStore.isAdmin
    ) {
      return
    }

    // 当前页只补充 SKU 界面状态，不再进行本地筛选
    products.value = productData.items.map((product: ProductResponse): ProductListItem => ({
      ...product,
      skus: null,
    }))
    pageNumber.value = productData.pageNumber
    pageSize.value = productData.pageSize
    totalCount.value = productData.totalCount
    totalPages.value = productData.totalPages

    categoryOptions.value = categoryData.map((category: CategoryResponse): CategoryOption => ({
      id: category.id,
      parentId: category.parentId,
      name: category.name,
    }))
  } catch (error: unknown) {
    if (
      !isPageActive || version !== productRequestVersion ||
      !authStore.isCurrentSession(accessToken) || !authStore.isAdmin
    ) {
      return
    }

    products.value = []
    loadError.value = error instanceof Error ? error.message : '获取商品管理数据失败，请稍后重试'
  } finally {
    if (isPageActive && version === productRequestVersion && authStore.isCurrentSession(accessToken)) {
      isLoading.value = false
    }
  }
}

// 打开新增商品弹窗
function openCreateDialog(): void {
  if (!isPageActive || !authStore.isAdmin || isBusy.value) {
    return
  }

  editingProduct.value = null
  isProductDialogVisible.value = true
}

// 打开编辑商品弹窗，表单组件内部会创建编辑副本
function openEditDialog(product: ProductListItem): void {
  if (!isPageActive || !authStore.isAdmin || isBusy.value) {
    return
  }

  editingProduct.value = product
  isProductDialogVisible.value = true
}

// 保存后重新查询，确保筛选、总数和每页条数仍然正确
function handleProductSaved(): void {
  if (!isPageActive || !authStore.isAdmin) {
    return
  }

  const targetPage: number = editingProduct.value === null ? 1 : pageNumber.value
  noticeMessage.value = '商品已保存'
  void loadProducts(targetPage)
}

// 商品弹窗关闭后，清除当前编辑对象
function handleProductDialogClosed(): void {
  editingProduct.value = null
}

// 打开当前商品的规格管理弹窗，由弹窗内部查询规格
function openSpecificationDialog(product: ProductListItem): void {
  if (!isPageActive || !authStore.isAdmin || isBusy.value) {
    return
  }

  selectedSpecificationProduct.value = product
  isSpecificationDialogVisible.value = true
}

// 关闭规格弹窗时，清除当前商品选择
watch(
  isSpecificationDialogVisible,
  (visible: boolean) => {
    if (!visible) {
      selectedSpecificationProduct.value = null
    }
  },
  { flush: 'sync' },
)

// 先打开 SKU 弹窗，再查询当前商品的数据
function openSkuDialog(product: ProductListItem): void {
  if (!isPageActive || !authStore.isAdmin || isBusy.value) {
    return
  }

  selectedSkuProduct.value = product
  isSkuDialogVisible.value = true
  void loadProductSkus()
}

// 判断结果是否属于当前商品和最新请求
function isCurrentSkuRequest(requestVersion: number, productId: number): boolean {
  return (
    isPageActive && authStore.isAdmin &&
    requestVersion === skuRequestVersion &&
    isSkuDialogVisible.value &&
    selectedSkuProduct.value?.id === productId
  )
}

// 查询当前商品的 SKU，也作为失败后的重试入口
async function loadProductSkus(): Promise<void> {
  const productId: number | undefined = selectedSkuProduct.value?.id

  if (!isSkuDialogVisible.value || productId === undefined) {
    return
  }

  const requestVersion: number = ++skuRequestVersion
  isSkuLoading.value = true
  skuLoadError.value = ''
  skuRows.value = []

  try {
    const skus: ProductSkuResponse[] = await getAdminProductSkus(productId, authStore.accessToken)

    // 弹窗已关闭或当前商品已改变时，丢弃这个结果
    if (!isCurrentSkuRequest(requestVersion, productId)) {
      return
    }

    skuRows.value = skus

    // 更新对应商品的 SKU 数据和数量
    products.value = products.value.map((product: ProductListItem): ProductListItem =>
      product.id === productId ? { ...product, skus } : product,
    )
  } catch (error: unknown) {
    if (!isCurrentSkuRequest(requestVersion, productId)) {
      return
    }

    skuLoadError.value = error instanceof Error ? error.message : '获取商品 SKU 失败，请稍后重试'
  } finally {
    // 旧请求结束时，不能关闭新请求的加载状态
    if (isCurrentSkuRequest(requestVersion, productId)) {
      isSkuLoading.value = false
    }
  }
}

// 新增或编辑成功后，重新读取 SKU 列表和数量
function handleSkuSaved(productId: number): void {
  if (
    !isSkuDialogVisible.value ||
    selectedSkuProduct.value?.id !== productId
  ) {
    return
  }

  // 沿用现有加载、失败重试和旧请求隔离逻辑
  void loadProductSkus()
}

// 清理弹窗状态，同时使尚未完成的请求失效
function clearSkuDialog(): void {
  skuRequestVersion += 1
  selectedSkuProduct.value = null
  skuRows.value = []
  isSkuLoading.value = false
  skuLoadError.value = ''
}

// 关闭弹窗时立即清理，不等待原生弹窗的关闭事件
watch(
  isSkuDialogVisible,
  (visible: boolean) => {
    if (!visible) {
      clearSkuDialog()
    }
  },
  { flush: 'sync' },
)

// 离开页面后，不再接收尚未完成的请求结果
onBeforeUnmount(() => {
  isPageActive = false
  productRequestVersion += 1
  clearSkuDialog()
})

// 点击查询后向后端提交三个条件，并回到第一页
function handleSearch(): void {
  if (!isPageActive || !authStore.isAdmin || isBusy.value) {
    return
  }

  appliedFilters.name = filters.name.trim()
  appliedFilters.categoryId = filters.categoryId
  appliedFilters.status = filters.status
  noticeMessage.value = ''
  saleError.value = ''
  totalCount.value = 0
  totalPages.value = 1
  void loadProducts(1)
}

// 翻页保留已提交的查询条件，失败后重试请求的页码
function handleChangePage(targetPage: number): void {
  if (
    !isPageActive || !authStore.isAdmin || isBusy.value || loadError.value !== '' ||
    targetPage < 1 || targetPage > totalPages.value || targetPage === pageNumber.value
  ) {
    return
  }

  void loadProducts(targetPage)
}

// 上下架成功后重新读取当前页，保持筛选和分页数量准确
async function handleSaleChange(product: ProductListItem): Promise<void> {
  if (!isPageActive || !authStore.isAdmin || isBusy.value) {
    return
  }

  const productId: number = product.id
  const accessToken: string = authStore.accessToken
  const version: number = productRequestVersion
  const request: UpdateProductSaleStatusRequest = { isOnSale: !product.isOnSale }
  changingSaleProductId.value = productId
  noticeMessage.value = ''
  saleError.value = ''

  try {
    await updateAdminProductSaleStatus(productId, request, accessToken)

    if (
      !isPageActive || version !== productRequestVersion ||
      !authStore.isCurrentSession(accessToken) || !authStore.isAdmin
    ) {
      return
    }

    noticeMessage.value = request.isOnSale ? '商品已上架' : '商品已下架'
    await loadProducts()
  } catch (error: unknown) {
    if (!isPageActive || !authStore.isCurrentSession(accessToken) || !authStore.isAdmin) {
      return
    }

    saleError.value =
      error instanceof Error ? error.message : '修改商品上下架状态失败，请稍后重试'
  } finally {
    if (isPageActive && authStore.isCurrentSession(accessToken)) {
      changingSaleProductId.value = null
    }
  }
}

// 账号或管理员权限改变时清理本页数据，使旧请求失效
watch(
  () => [authStore.sessionId, authStore.isAdmin] as const,
  () => {
    productRequestVersion += 1
    isLoading.value = false
    changingSaleProductId.value = null
    products.value = []
    categoryOptions.value = []
    pageNumber.value = 1
    pageSize.value = 20
    totalCount.value = 0
    totalPages.value = 1
    filters.name = ''
    filters.categoryId = null
    filters.status = ''
    appliedFilters.name = ''
    appliedFilters.categoryId = null
    appliedFilters.status = ''
    loadError.value = ''
    noticeMessage.value = ''
    saleError.value = ''
    isProductDialogVisible.value = false
    editingProduct.value = null
    isSpecificationDialogVisible.value = false
    selectedSpecificationProduct.value = null
    isSkuDialogVisible.value = false
    clearSkuDialog()

    if (isPageActive && authStore.isAdmin) {
      void loadProducts(1)
    }
  },
  { flush: 'sync' },
)

// 页面进入后加载真实商品数据
onMounted(() => {
  void loadProducts()
})
</script>

<template>
  <div class="products-page">
    <!-- 页面标题和主要操作 -->
    <header class="page-heading">
      <div>
        <h1>商品管理</h1>
        <p>维护商品信息、规格库存与上下架状态</p>
      </div>

      <button class="button primary-button" type="button" :disabled="isBusy" @click="openCreateDialog">
        <svg viewBox="0 0 24 24" fill="none" aria-hidden="true">
          <path
            d="M12 5v14M5 12h14"
            stroke="currentColor"
            stroke-width="1.7"
            stroke-linecap="round"
          />
        </svg>
        新增商品
      </button>
    </header>

    <section class="table-panel" aria-labelledby="product-list-title">
      <!-- 筛选条件 -->
      <form class="filter-form" novalidate @submit.prevent="handleSearch">
        <div class="filter-field name-filter">
          <label for="admin-product-name">商品名称</label>

          <input
            id="admin-product-name"
            v-model="filters.name"
            :disabled="isBusy"
            type="search"
            maxlength="200"
            placeholder="请输入商品名称"
          />
        </div>

        <div class="filter-field">
          <label for="admin-product-category">所属分类</label>

          <select id="admin-product-category" v-model="filters.categoryId" :disabled="isBusy">
            <option :value="null">全部分类</option>

            <option v-for="category in categoryOptions" :key="category.id" :value="category.id">
              {{ category.name }}
            </option>
          </select>
        </div>

        <div class="filter-field">
          <label for="admin-product-status">商品状态</label>

          <select id="admin-product-status" v-model="filters.status" :disabled="isBusy">
            <option value="">全部状态</option>
            <option value="on">已上架</option>
            <option value="off">未上架</option>
          </select>
        </div>

        <div class="filter-actions">
          <button class="button primary-button" type="submit" :disabled="isBusy">查询</button>
        </div>
      </form>

      <!-- 商品操作结果提示 -->
      <div role="status" aria-live="polite" aria-atomic="true">
        <p v-if="noticeMessage" class="notice-message">
          {{ noticeMessage }}
        </p>
      </div>

      <!-- 上下架失败时显示原因 -->
      <p v-if="saleError" class="notice-message sale-error-message" role="alert">
        {{ saleError }}
      </p>

      <div class="table-heading">
        <h2 id="product-list-title">商品列表</h2>
        <span>价格与库存按 SKU 管理</span>
      </div>

      <!-- 表格在自身容器内横向滚动 -->
      <div
        class="table-wrapper"
        role="region"
        aria-label="商品列表，可横向滚动"
        :aria-busy="isLoading || changingSaleProductId !== null"
        tabindex="0"
      >
        <table class="product-table">
          <thead>
            <tr>
              <th scope="col">商品信息</th>
              <th scope="col">分类</th>
              <th scope="col">SKU 数量</th>
              <th scope="col">状态</th>
              <th scope="col">操作</th>
            </tr>
          </thead>

          <tbody>
            <!-- 分别展示加载中、失败和成功后的空列表 -->
            <tr v-if="isLoading">
              <td colspan="5" class="empty-cell" role="status">正在加载商品…</td>
            </tr>

            <tr v-else-if="loadError">
              <td colspan="5" class="empty-cell">
                <p class="load-error-message" role="alert">{{ loadError }}</p>
                <button class="button" type="button" @click="loadProducts()">重新加载</button>
              </td>
            </tr>

            <tr v-else-if="products.length === 0">
              <td colspan="5" class="empty-cell">
                {{ hasActiveFilters ? '没有符合条件的商品' : '暂无商品' }}
              </td>
            </tr>

            <!-- 加载成功后展示真实商品 -->
            <template v-else>
              <tr v-for="product in products" :key="product.id">
                <td>
                  <div class="product-name">{{ product.name }}</div>
                  <div class="product-id">编号：{{ product.id }}</div>
                </td>

                <td>{{ product.categoryName || '—' }}</td>
                <td>{{ product.skus?.length ?? '—' }}</td>

                <td>
                  <span class="status-badge" :class="{ 'is-on-sale': product.isOnSale }">
                    {{ product.isOnSale ? '已上架' : '未上架' }}
                  </span>
                </td>

                <td>
                  <div class="row-actions">
                    <button
                      class="text-button"
                      type="button"
                      :disabled="isBusy"
                      @click="openEditDialog(product)"
                    >
                      编辑
                    </button>

                    <button
                      class="text-button"
                      type="button"
                      :disabled="isBusy"
                      @click="openSpecificationDialog(product)"
                    >
                      规格管理
                    </button>

                    <button
                      class="text-button"
                      type="button"
                      :disabled="isBusy"
                      @click="openSkuDialog(product)"
                    >
                      SKU 管理
                    </button>

                    <button
                      class="text-button"
                      type="button"
                      :disabled="isBusy"
                      @click="handleSaleChange(product)"
                    >
                      {{
                        changingSaleProductId === product.id
                          ? product.isOnSale
                            ? '正在下架…'
                            : '正在上架…'
                          : product.isOnSale
                            ? '下架'
                            : '上架'
                      }}
                    </button>
                  </div>
                </td>
              </tr>
            </template>
          </tbody>
        </table>
      </div>

      <!-- 总数和有效页码均采用后端返回值 -->
      <footer class="pagination">
        <span v-if="!isLoading && !loadError">共 {{ totalCount }} 条，每页 {{ pageSize }} 条</span>
        <span v-else>{{ isLoading ? '正在读取商品' : '商品加载失败' }}</span>

        <div class="pagination-actions">
          <button
            class="button page-button"
            type="button"
            :disabled="isBusy || loadError !== '' || pageNumber <= 1"
            @click="handleChangePage(pageNumber - 1)"
          >
            上一页
          </button>
          <span class="page-number" aria-live="polite">第 {{ pageNumber }} / {{ totalPages }} 页</span>
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

    <!-- 商品新增和编辑共用一个弹窗 -->
    <ProductFormDialog
      v-model="isProductDialogVisible"
      :product="editingProduct"
      :categories="categoryOptions"
      @saved="handleProductSaved"
      @closed="handleProductDialogClosed"
    />

    <!-- 每次打开都创建规格弹窗，关闭后卸载并使旧请求失效 -->
    <ProductSpecificationDialog
      v-if="isSpecificationDialogVisible && selectedSpecificationProduct !== null"
      v-model="isSpecificationDialogVisible"
      :product-id="selectedSpecificationProduct.id"
      :product-name="selectedSpecificationProduct.name"
    />

    <!-- 每次打开重新查询，切换商品或关闭后卸载旧弹窗 -->
    <ProductSkuDialog
      v-if="isSkuDialogVisible && selectedSkuProduct !== null"
      :key="selectedSkuProduct.id"
      v-model="isSkuDialogVisible"
      :product-id="selectedSkuProduct.id"
      :product-name="selectedSkuProduct.name"
      :skus="skuRows"
      :loading="isSkuLoading"
      :error="skuLoadError"
      @retry="loadProductSkus"
      @saved="handleSkuSaved"
    />
  </div>
</template>

<style scoped>
/* 页面允许在后台内容容器中缩小 */
.products-page {
  min-width: 0;
}

/* 页面标题与新增按钮 */
.page-heading {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
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

/* 页面内按钮的基础样式 */
.button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 7px;
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

.button svg {
  width: 16px;
  height: 16px;
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

/* 筛选和表格放在同一张白色卡片中 */
.table-panel {
  min-width: 0;
  overflow: hidden;
  border: 1px solid #e6ebf3;
  border-radius: 12px;
  background: #ffffff;
}

/* 筛选条件自动换行 */
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

.name-filter {
  min-width: 200px;
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

/* 操作提示 */
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
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 19px 23px 15px;
}

.table-heading h2 {
  margin: 0;
  font-size: 13px;
  font-weight: 600;
}

.table-heading > span {
  color: #9ca8bb;
  font-size: 11px;
}

/* 宽表格只在内部横向滚动 */
.table-wrapper {
  width: 100%;
  overflow-x: auto;
}

.table-wrapper:focus-visible {
  outline: 3px solid #93b6ff;
  outline-offset: -3px;
}

.product-table {
  width: 100%;
  min-width: 720px;
  border-collapse: collapse;
  text-align: left;
}

.product-table th {
  padding: 12px 16px;
  border-top: 1px solid #f0f3f8;
  border-bottom: 1px solid #edf1f7;
  background: #f8fafe;
  color: #8c99ad;
  font-size: 12px;
  font-weight: 400;
  white-space: nowrap;
}

.product-table td {
  padding: 18px 16px;
  border-bottom: 1px solid #f0f3f7;
  color: #67768e;
  font-size: 12px;
}

.product-table th:first-child,
.product-table td:first-child {
  padding-left: 23px;
}

.product-table th:last-child,
.product-table td:last-child {
  padding-right: 23px;
}

.product-table tbody tr:last-child td {
  border-bottom: none;
}

.product-name {
  min-width: 150px;
  max-width: 240px;
  color: #3c4b63;
  overflow-wrap: anywhere;
}

.product-id {
  margin-top: 4px;
  color: #a8b2c2;
  font-size: 11px;
}

.status-badge {
  display: inline-block;
  padding: 3px 8px;
  border-radius: 5px;
  background: #f0f3f8;
  color: #96a1b3;
  font-size: 11px;
  white-space: nowrap;
}

.status-badge.is-on-sale {
  background: #eef8f2;
  color: #68a789;
}

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

.text-button:hover:not(:disabled) {
  color: #2864e8;
}

.text-button:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

/* 上下架失败提示 */
.sale-error-message {
  border-color: #f0d7d7;
  background: #fff6f6;
  color: #bd4848;
}

/* 空列表提示 */
.product-table .empty-cell {
  padding: 64px 20px;
  color: #a1adc0;
  text-align: center;
}

/* 加载失败提示与重试按钮分开显示 */
.product-table .load-error-message {
  margin: 0 0 12px;
  color: #bd4848;
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
  padding: 0 8px;
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
  .name-filter {
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
</style>
