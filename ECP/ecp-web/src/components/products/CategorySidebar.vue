<script setup lang="ts">
import { ref } from 'vue'

// 定义子分类的数据结构
interface SubcategoryItem {
  id: number
  name: string
}

// 定义大分类的数据结构
interface CategoryItem {
  id: number
  name: string
  children: SubcategoryItem[]
}

// 接收分类数据、加载状态和失败提示
const props = withDefaults(
  defineProps<{
    categories: CategoryItem[]
    selectedCategoryId?: number | null
    loading?: boolean
    error?: string
  }>(),
  {
    selectedCategoryId: null,
    loading: false,
    error: '',
  },
)

// 通知首页选择分类或重新加载
const emit = defineEmits<{
  select: [categoryId: number | null]
  retry: []
}>()

// 保存当前展开的大分类编号，null 表示全部收起
const expandedCategoryId = ref<number | null>(null)

// 切换大类展开状态，同时筛选该大类商品
function toggleCategory(categoryId: number): void {
  expandedCategoryId.value = expandedCategoryId.value === categoryId ? null : categoryId
  emit('select', categoryId)
}
</script>

<template>
  <aside
    class="category-sidebar"
    aria-labelledby="category-sidebar-title"
    :aria-busy="props.loading"
  >
    <!-- 分类栏标题 -->
    <div class="sidebar-heading">
      <svg class="heading-icon" viewBox="0 0 24 24" fill="none" aria-hidden="true">
        <rect
          x="3.5"
          y="3.5"
          width="6"
          height="6"
          rx="1.4"
          stroke="currentColor"
          stroke-width="1.6"
        />
        <rect
          x="14.5"
          y="3.5"
          width="6"
          height="6"
          rx="1.4"
          stroke="currentColor"
          stroke-width="1.6"
        />
        <rect
          x="3.5"
          y="14.5"
          width="6"
          height="6"
          rx="1.4"
          stroke="currentColor"
          stroke-width="1.6"
        />
        <rect
          x="14.5"
          y="14.5"
          width="6"
          height="6"
          rx="1.4"
          stroke="currentColor"
          stroke-width="1.6"
        />
      </svg>

      <h2 id="category-sidebar-title">商品分类</h2>
    </div>

    <!-- 清除分类筛选，保留当前展开状态 -->
    <button
      class="category-button all-category-button"
      :class="{ 'is-selected': props.selectedCategoryId === null }"
      type="button"
      :aria-pressed="props.selectedCategoryId === null"
      @click="emit('select', null)"
    >
      <span class="category-dot" aria-hidden="true"></span>
      <span class="category-name">全部商品</span>
    </button>

    <!-- 分类加载中 -->
    <p v-if="props.loading" class="category-empty" role="status">正在加载分类…</p>

    <!-- 加载失败时显示原因，并允许重试 -->
    <div v-else-if="props.error">
      <p class="category-empty" role="alert">{{ props.error }}</p>

      <button class="category-retry" type="button" @click="emit('retry')">重新加载</button>
    </div>

    <!-- 加载成功但没有可展示的分类 -->
    <p v-else-if="props.categories.length === 0" class="category-empty">暂无商品分类</p>

    <!-- 大分类纵向排列，使用编号作为稳定的列表标识 -->
    <ul v-else class="category-list">
      <li v-for="category in props.categories" :key="category.id" class="category-group">
        <!-- 原生按钮支持鼠标点击、回车和空格操作 -->
        <button
          :id="`category-toggle-${category.id}`"
          class="category-button"
          :class="{
            'is-expanded': expandedCategoryId === category.id,
            'is-selected': props.selectedCategoryId === category.id,
          }"
          type="button"
          :aria-expanded="expandedCategoryId === category.id"
          :aria-pressed="props.selectedCategoryId === category.id"
          :aria-controls="`category-panel-${category.id}`"
          @click="toggleCategory(category.id)"
        >
          <span class="category-dot" aria-hidden="true"></span>

          <span class="category-name">{{ category.name }}</span>

          <svg class="chevron-icon" viewBox="0 0 16 16" fill="none" aria-hidden="true">
            <path
              d="m6 3 5 5-5 5"
              stroke="currentColor"
              stroke-width="1.6"
              stroke-linecap="round"
              stroke-linejoin="round"
            />
          </svg>
        </button>

        <!-- 保留面板节点，让按钮始终能够关联对应内容 -->
        <div
          v-show="expandedCategoryId === category.id"
          :id="`category-panel-${category.id}`"
          class="subcategory-panel"
          role="region"
          :aria-labelledby="`category-toggle-${category.id}`"
        >
          <!-- 当前大分类没有可展示的子分类时显示提示 -->
          <p v-if="category.children.length === 0" class="subcategory-empty">暂无子分类</p>

          <!-- 展示接口返回的真实子分类名称 -->
          <ul v-else class="subcategory-list">
            <li
              v-for="subcategory in category.children"
              :key="subcategory.id"
              class="subcategory-item"
            >
              <button
                class="subcategory-button"
                :class="{
                  'is-selected': props.selectedCategoryId === subcategory.id,
                }"
                type="button"
                :aria-pressed="props.selectedCategoryId === subcategory.id"
                @click="emit('select', subcategory.id)"
              >
                {{ subcategory.name }}
              </button>
            </li>
          </ul>
        </div>
      </li>
    </ul>
  </aside>
</template>

<style scoped>
/* 分类栏独立维护卡片样式 */
.category-sidebar {
  width: 100%;
  min-width: 0;
  padding: 22px 12px 14px;
  border: 1px solid #e5eaf2;
  border-radius: 14px;
  background: #ffffff;
  color: #253248;
}

/* 标题图标和文字横向排列 */
.sidebar-heading {
  display: flex;
  align-items: center;
  gap: 9px;
  padding: 0 9px 18px;
}

.heading-icon {
  width: 17px;
  height: 17px;
  flex-shrink: 0;
  color: #6b7b93;
}

.sidebar-heading h2 {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
}

/* 清除列表默认缩进和标记 */
.category-list,
.subcategory-list {
  margin: 0;
  padding: 0;
  list-style: none;
}

/* 大分类上下排列 */
.category-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.category-group {
  min-width: 0;
}

/* 分类按钮铺满侧栏，文字较长时允许换行 */
.category-button {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  padding: 12px 11px;
  border: 1px solid transparent;
  border-radius: 8px;
  background: transparent;
  color: #68758a;
  text-align: left;
  cursor: pointer;
  transition:
    background 0.16s,
    color 0.16s;
}

.category-button:hover {
  background: #f5f8fd;
  color: #2864e8;
}

/* 当前选中的分类 */
.category-button.is-selected {
  border-color: #e2ebff;
  background: #edf3ff;
  color: #2864e8;
}

/* 焦点框放在按钮内部，避免超出卡片 */
.category-button:focus-visible,
.subcategory-button:focus-visible {
  outline: 3px solid #93b6ff;
  outline-offset: -2px;
}

/* 分类名称前的小圆点 */
.category-dot {
  width: 6px;
  height: 6px;
  flex-shrink: 0;
  border-radius: 50%;
  background: #aebbd0;
}

.is-selected .category-dot {
  background: #2864e8;
}

.category-name {
  min-width: 0;
  flex: 1;
  overflow-wrap: anywhere;
}

/* 展开后箭头朝下 */
.chevron-icon {
  width: 13px;
  height: 13px;
  flex-shrink: 0;
  color: #9aa6b8;
  transition: transform 0.18s;
}

.is-expanded .chevron-icon {
  transform: rotate(90deg);
  color: #2864e8;
}

/* 子分类区域缩进，并使用竖线体现层级 */
.subcategory-panel {
  margin: 8px 10px 10px 24px;
  padding: 4px 0 4px 14px;
  border-left: 1px solid #e7edf7;
}

/* 分类及子分类为空时的提示 */
.category-empty {
  margin: 0;
  padding: 0 9px 10px;
  color: #98a3b3;
  font-size: 12px;
}

.subcategory-empty {
  margin: 0;
  padding: 7px 0;
  color: #98a3b3;
  font-size: 12px;
}

.subcategory-item {
  min-width: 0;
}

/* 子分类按钮铺满可点击区域 */
.subcategory-button {
  display: block;
  width: 100%;
  padding: 8px 10px;
  border: 1px solid transparent;
  border-radius: 6px;
  background: transparent;
  color: #68758a;
  font-size: 12px;
  text-align: left;
  overflow-wrap: anywhere;
  cursor: pointer;
}

.subcategory-button:hover {
  background: #f5f8fd;
  color: #2864e8;
}

.subcategory-button.is-selected {
  border-color: #e2ebff;
  background: #edf3ff;
  color: #2864e8;
}

.all-category-button {
  margin-bottom: 8px;
}

/* 分类加载失败后的重试按钮 */
.category-retry {
  margin: 0 9px 10px;
  padding: 6px 10px;
  border: 1px solid #dce6f7;
  border-radius: 6px;
  background: #f5f8fd;
  color: #2864e8;
  font-size: 12px;
  cursor: pointer;
}

.category-retry:hover {
  background: #edf3ff;
}

.category-retry:focus-visible {
  outline: 3px solid #93b6ff;
  outline-offset: 2px;
}

/* 桌面滚动页面时，分类栏保持在视口顶部附近 */
@media (min-width: 1000px) {
  .category-sidebar {
    position: sticky;
    top: 24px;
  }
}

/* 手机布局由首页将分类栏放到商品区域上方 */
@media (max-width: 700px) {
  .category-sidebar {
    padding: 17px 14px;
  }

  .sidebar-heading {
    padding: 0 8px 14px;
  }

  .category-button {
    padding: 10px 11px;
    font-size: 13px;
  }
}

/* 尊重系统减少动态效果的设置 */
@media (prefers-reduced-motion: reduce) {
  .category-button,
  .chevron-icon {
    transition: none;
  }
}
</style>
