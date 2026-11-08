<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { getCategories, type CategoryResponse } from '@/api/categories'
import { getProducts, type ProductResponse } from '@/api/products'
import StoreHeader from '@/components/layout/StoreHeader.vue'
import CategorySidebar from '@/components/products/CategorySidebar.vue'
import ProductSkuSelectDialog from '@/components/products/ProductSkuSelectDialog.vue'

// 定义子分类的数据结构
interface SubcategoryItem {
  id: number
  name: string
}

// 定义首页传给分类侧栏的数据结构
interface CategoryItem {
  id: number
  name: string
  children: SubcategoryItem[]
}

// 保存公开接口返回的启用分类
const categoryData = ref<CategoryResponse[]>([])

// 保存分类加载状态和失败提示
const isLoading = ref<boolean>(false)
const loadError = ref<string>('')

// 保存商城商品列表
const products = ref<ProductResponse[]>([])

// 单独记录图片加载失败，避免某张图片影响商品列表
const failedProductImages = ref<Record<number, boolean>>({})

// 控制商城 SKU 选择弹窗
const isSkuSelectDialogVisible = ref<boolean>(false)

// 保存本次查看的商品
const selectedProduct = ref<ProductResponse | null>(null)

// 商品与分类分别记录加载和错误状态
const isProductLoading = ref<boolean>(false)
const productLoadError = ref<string>('')

// 离开首页后，不再接收商品请求结果
let isPageActive: boolean = true

// 将扁平分类整理为两级结构，保留接口返回的排序
const categories = computed<CategoryItem[]>(() =>
  categoryData.value
    .filter((category: CategoryResponse) => category.parentId === null)
    .map((parent: CategoryResponse): CategoryItem => ({
      id: parent.id,
      name: parent.name,
      children: categoryData.value
        .filter((child: CategoryResponse) => child.parentId === parent.id)
        .map((child: CategoryResponse): SubcategoryItem => ({
          id: child.id,
          name: child.name,
        })),
    })),
)

// null 表示展示全部商品
const selectedCategoryId = ref<number | null>(null)

// 商品区域标题使用当前分类名称
const selectedCategoryName = computed<string>(() => {
  const category: CategoryResponse | undefined = categoryData.value.find(
    (item: CategoryResponse) => item.id === selectedCategoryId.value,
  )

  return category?.name ?? '全部商品'
})

// 根据当前分类筛选已加载商品，保留原有排列顺序
const filteredProducts = computed<ProductResponse[]>(() => {
  const categoryId: number | null = selectedCategoryId.value

  if (categoryId === null) {
    return products.value
  }

  // 大类筛选包含自身及当前分类树中的直接子类
  const categoryIds: Set<number> = new Set<number>([categoryId])
  const parent: CategoryItem | undefined = categories.value.find(
    (category: CategoryItem) => category.id === categoryId,
  )

  if (parent !== undefined) {
    for (const child of parent.children) {
      categoryIds.add(child.id)
    }
  }

  return products.value.filter(
    (product: ProductResponse) => categoryIds.has(product.categoryId),
  )
})

// 接收侧栏选择，并在选项消失时恢复全部商品
function handleCategorySelect(categoryId: number | null): void {
  const exists: boolean =
    categoryId === null ||
    categories.value.some(
      (category: CategoryItem) =>
        category.id === categoryId ||
        category.children.some(
          (child: SubcategoryItem) => child.id === categoryId,
        ),
    )

  selectedCategoryId.value = exists ? categoryId : null
}

// 加载商城分类，公开查询无需登录令牌
async function loadCategories(): Promise<void> {
  if (!isPageActive || isLoading.value) {
    return
  }

  isLoading.value = true
  loadError.value = ''

  try {
    const result: CategoryResponse[] = await getCategories()

    if (!isPageActive) {
      return
    }

    categoryData.value = result

    // 分类已不在返回的分类树中时，恢复全部商品
    handleCategorySelect(selectedCategoryId.value)
  } catch (error: unknown) {
    if (!isPageActive) {
      return
    }

    categoryData.value = []
    selectedCategoryId.value = null
    loadError.value = error instanceof Error ? error.message : '获取分类列表失败，请稍后重试'
  } finally {
    if (isPageActive) {
      isLoading.value = false
    }
  }
}

// 查询已上架商品，也作为加载失败后的重试入口
async function loadProducts(): Promise<void> {
  if (!isPageActive || isProductLoading.value) {
    return
  }

  isProductLoading.value = true
  productLoadError.value = ''

  try {
    const result: ProductResponse[] = await getProducts()

    if (!isPageActive) {
      return
    }

    failedProductImages.value = {}
    products.value = result
  } catch (error: unknown) {
    if (!isPageActive) {
      return
    }

    products.value = []
    productLoadError.value =
      error instanceof Error ? error.message : '获取商品列表失败，请稍后重试'
  } finally {
    if (isPageActive) {
      isProductLoading.value = false
    }
  }
}

// 图片缺失或无法读取时，仅让对应卡片显示占位
function handleProductImageError(productId: number, event: Event): void {
  const image: HTMLImageElement = event.target as HTMLImageElement

  if (!isPageActive || isProductLoading.value || !image.isConnected) {
    return
  }

  failedProductImages.value = {
    ...failedProductImages.value,
    [productId]: true,
  }
}

// 打开商品规格选择弹窗
function openSkuSelectDialog(product: ProductResponse): void {
  selectedProduct.value = { ...product }
  isSkuSelectDialogVisible.value = true
}

// 关闭后清理商品，弹窗组件随之卸载
watch(
  isSkuSelectDialogVisible,
  (visible: boolean) => {
    if (!visible) {
      selectedProduct.value = null
    }
  },
  { flush: 'sync' },
)

// 分类和商品独立加载，一方失败不影响另一方展示
onMounted(() => {
  void loadCategories()
  void loadProducts()
})

// 离开页面后，丢弃尚未返回的商品请求结果
onBeforeUnmount(() => {
  isPageActive = false
})
</script>

<template>
  <div class="home-page">
    <!-- 顶部组件管理搜索、频道导航和账号操作 -->
    <StoreHeader fluid />

    <!-- 桌面左右排列，手机上下排列 -->
    <main class="home-content">
      <!-- 侧栏管理展开状态，首页管理分类筛选 -->
      <CategorySidebar
        :categories="categories"
        :selected-category-id="selectedCategoryId"
        :loading="isLoading"
        :error="loadError"
        @select="handleCategorySelect"
        @retry="loadCategories"
      />

      <!-- 展示公开接口返回的已上架商品 -->
      <section
        class="products-area"
        aria-labelledby="products-title"
        :aria-busy="isProductLoading"
      >
        <h1 id="products-title" class="products-title">
          {{ selectedCategoryName }}
        </h1>

        <!-- 加载中 -->
        <div v-if="isProductLoading" class="product-empty" role="status">
          <p>正在加载商品…</p>
        </div>

        <!-- 加载失败，允许重新请求 -->
        <div v-else-if="productLoadError" class="product-empty">
          <p class="product-error" role="alert">{{ productLoadError }}</p>

          <button class="retry-button" type="button" @click="loadProducts">
            重新加载
          </button>
        </div>

        <!-- 查询成功但没有商品 -->
        <div v-else-if="filteredProducts.length === 0" class="product-empty" role="status">
          <div class="empty-icon" aria-hidden="true">
            <svg viewBox="0 0 24 24" fill="none">
              <path
                d="M5 8h14l1 13H4L5 8Z"
                stroke="currentColor"
                stroke-width="1.5"
                stroke-linejoin="round"
              />
              <path
                d="M8 8V6a4 4 0 0 1 8 0v2"
                stroke="currentColor"
                stroke-width="1.5"
                stroke-linecap="round"
              />
            </svg>
          </div>

          <p>
            {{
              selectedCategoryId === null
                ? '暂无商品，稍后再来看看'
                : '该分类下暂无商品'
            }}
          </p>
        </div>

        <!-- 查询成功后展示真实商品 -->
        <ul v-else class="product-list">
          <li v-for="product in filteredProducts" :key="product.id" class="product-card">
            <div class="product-image">
              <img
                v-if="!failedProductImages[product.id]"
                :src="'/api/products/' + product.id + '/cover-image'"
                :alt="product.name"
                loading="lazy"
                decoding="async"
                @error="handleProductImageError(product.id, $event)"
              />

              <div v-else class="product-image-placeholder">
                <svg viewBox="0 0 24 24" fill="none" aria-hidden="true">
                  <rect x="3" y="3" width="18" height="18" rx="3" />
                  <circle cx="8.5" cy="8.5" r="1.5" />
                  <path d="m4 18 5-5 3 3 4-5 5 6" />
                </svg>
                <span>暂无图片</span>
              </div>
            </div>

            <h2>{{ product.name }}</h2>
            <p class="product-category">{{ product.categoryName }}</p>
            <p class="product-description">
              {{ product.description || '暂无描述' }}
            </p>

            <button
              class="product-select-button"
              type="button"
              aria-haspopup="dialog"
              :aria-label="'选择' + product.name + '的规格'"
              @click="openSkuSelectDialog(product)"
            >
              选择规格
            </button>
          </li>
        </ul>
      </section>
    </main>

    <!-- 每次打开重新查询，关闭后卸载 -->
    <ProductSkuSelectDialog
      v-if="isSkuSelectDialogVisible && selectedProduct !== null"
      :key="selectedProduct.id"
      v-model="isSkuSelectDialogVisible"
      :product-id="selectedProduct.id"
      :product-name="selectedProduct.name"
    />
  </div>
</template>

<style scoped>
/* 首页背景铺满窗口 */
.home-page {
  min-height: 100vh;
  background: #f4f6fa;
  color: #253248;
}

/* 首页主体随视口展开，左右内边距与顶部保持一致 */
.home-content {
  width: 100%;
  max-width: none;
  margin: 0 auto;
  padding: 28px 28px 48px;
  display: grid;
  grid-template-columns: 208px minmax(0, 1fr);
  align-items: start;
  gap: 28px;
}

/* 商品区域允许随窗口宽度缩小 */
.products-area {
  min-width: 0;
}

/* 商品区域标题 */
.products-title {
  margin: 0 0 20px;
  font-size: 25px;
  font-weight: 600;
  letter-spacing: -0.6px;
}

/* 商品为空时显示白色卡片 */
.product-empty {
  min-height: 340px;
  padding: 40px 24px;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 18px;
  border: 1px solid #e5eaf2;
  border-radius: 14px;
  background: #ffffff;
  text-align: center;
}

/* 空状态图标 */
.empty-icon {
  width: 72px;
  height: 72px;
  display: grid;
  place-items: center;
  border-radius: 50%;
  background: #f0f5ff;
  color: #91aadd;
}

.empty-icon svg {
  width: 34px;
  height: 34px;
}

.product-empty p {
  margin: 0;
  color: #98a3b3;
  font-size: 14px;
  overflow-wrap: anywhere;
}

/* 中等宽度缩小侧栏和列间距 */
@media (max-width: 1000px) {
  .home-content {
    grid-template-columns: 184px minmax(0, 1fr);
    gap: 22px;
  }
}

/* 手机上分类在上，商品区域在下 */
@media (max-width: 700px) {
  .home-content {
    grid-template-columns: minmax(0, 1fr);
    padding: 24px 18px 32px;
    gap: 24px;
  }

  .products-title {
    margin-bottom: 16px;
    font-size: 22px;
  }

  .product-empty {
    min-height: 280px;
    padding: 32px 20px;
  }
}
/* 保持卡片尺寸，按商品区域宽度自动排列 */
.product-list {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(0, 192px));
  gap: 12px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.product-card {
  display: flex;
  flex-direction: column;
  min-width: 0;
  padding: 14px;
  border: 1px solid #e5eaf2;
  border-radius: 10px;
  background: #ffffff;
  overflow-wrap: anywhere;
}

/* 封面保持正方形区域，完整展示图片 */
.product-image {
  display: grid;
  flex-shrink: 0;
  place-items: center;
  width: 100%;
  aspect-ratio: 1;
  margin: 0 0 12px;
  overflow: hidden;
  border: 1px solid #edf1f6;
  border-radius: 8px;
  background: #ffffff;
}

.product-image img {
  display: block;
  width: 100%;
  height: 100%;
  object-fit: contain;
}

.product-image-placeholder {
  display: grid;
  justify-items: center;
  gap: 8px;
  color: #a1adbf;
  font-size: 12px;
}

.product-image-placeholder svg {
  width: 32px;
  height: 32px;
  stroke: currentColor;
  stroke-width: 1.4;
  stroke-linecap: round;
  stroke-linejoin: round;
}

.product-card h2 {
  margin: 0 0 8px;
  color: #253248;
  font-size: 15px;
  font-weight: 600;
}

.product-category {
  margin: 0 0 8px;
  color: #648dd6;
  font-size: 12px;
}

.product-description {
  margin: 0 0 12px;
  color: #8793a5;
  font-size: 12px;
  line-height: 1.5;
  white-space: pre-wrap;
}

/* 商品加载失败提示 */
.product-empty .product-error {
  color: #bd4848;
}

.retry-button {
  padding: 9px 16px;
  border: 1px solid #dfe6ef;
  border-radius: 7px;
  background: #ffffff;
  color: #52617a;
  cursor: pointer;
}

.retry-button:hover {
  border-color: #83a9fa;
  background: #f6f9ff;
}

.retry-button:focus-visible {
  outline: 2px solid #83a9fa;
  outline-offset: 3px;
}

/* 商品规格选择入口 */
.product-select-button {
  align-self: flex-start;
  margin-top: auto;
  padding: 6px 10px;
  border: 1px solid #2864e8;
  border-radius: 7px;
  background: #2864e8;
  color: #ffffff;
  font-size: 12px;
  cursor: pointer;
}

.product-select-button:hover {
  border-color: #1e55d0;
  background: #1e55d0;
}

.product-select-button:focus-visible {
  outline: 2px solid #83a9fa;
  outline-offset: 3px;
}
</style>
