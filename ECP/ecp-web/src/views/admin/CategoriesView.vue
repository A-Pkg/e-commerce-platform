<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue'
import { getAdminCategories, type CategoryResponse } from '@/api/categories'
import CategoryFormDialog from '@/components/admin/categories/CategoryFormDialog.vue'
import { useAuthStore } from '@/stores/auth'

// 定义分类列表的数据结构
interface CategoryItem {
  id: number
  name: string
  parentId: number | null
  sortOrder: number
  isActive: boolean
}

// 定义分类筛选条件
interface CategoryFilters {
  name: string
  status: '' | 'on' | 'off'
}

// 定义表格显示行，记录是否为子分类以及是否存在子分类
interface CategoryRow {
  category: CategoryItem
  isChild: boolean
  hasChildren: boolean
}

// 获取当前登录会话，使用内存中的访问令牌请求后台接口
const authStore = useAuthStore()
let isPageActive: boolean = true
let requestVersion: number = 0

// 换令牌仍可接收同一会话的结果；退出、换账号或离页后丢弃
function isCurrentRequest(version: number, token: string): boolean {
  return isPageActive && version === requestVersion
    && authStore.isAdmin && authStore.isCurrentSession(token)
}

// 保存筛选输入
const filters = reactive<CategoryFilters>({
  name: '',
  status: '',
})

// 保存点击查询后生效的条件，输入时暂不改变列表
const appliedFilters = reactive<CategoryFilters>({
  name: '',
  status: '',
})

// 保存后端返回的分类，供表格和父分类选项使用
const categories = ref<CategoryItem[]>([])

// 标记分类请求是否正在进行，避免重复加载
const isLoading = ref<boolean>(false)

// 保存分类加载失败信息，与空数据状态区分
const loadError = ref<string>('')

// 保存已经展开的大分类编号
const expandedCategoryIds = ref<number[]>([])

// 控制分类新增和编辑弹窗
const isCategoryDialogVisible = ref<boolean>(false)

// 保存待编辑分类，null 表示新增
const editingCategory = ref<CategoryItem | null>(null)

// 保存分类操作结果提示
const noticeMessage = ref<string>('')

// 提取大分类，供表格和弹窗中的父分类选项使用
const parentCategories = computed<CategoryItem[]>(() =>
  categories.value
    .filter((category: CategoryItem) => category.parentId === null)
    .sort(
      (first: CategoryItem, second: CategoryItem) =>
        first.sortOrder - second.sortOrder || first.id - second.id,
    ),
)

// 判断当前是否存在已经提交的筛选条件
const hasActiveFilters = computed<boolean>(
  () => appliedFilters.name !== '' || appliedFilters.status !== '',
)

// 根据名称和启用状态筛选，保留完整分类数组
const matchedCategories = computed<CategoryItem[]>(() => {
  // 名称忽略英文大小写，支持包含匹配
  const keyword: string = appliedFilters.name.toLowerCase()

  return categories.value.filter((category: CategoryItem) => {
    const matchesName: boolean = category.name.toLowerCase().includes(keyword)

    const matchesStatus: boolean =
      appliedFilters.status === '' ||
      (appliedFilters.status === 'on' ? category.isActive : !category.isActive)

    return matchesName && matchesStatus
  })
})

// 按父子关系组织筛选结果，子分类命中时保留其父分类作为层级定位
const visibleRows = computed<CategoryRow[]>(() => {
  const rows: CategoryRow[] = []

  // 创建匹配编号集合，供父子分类判断使用
  const matchedIds: Set<number> = new Set<number>(
    matchedCategories.value.map((category: CategoryItem) => category.id),
  )

  parentCategories.value.forEach((parent: CategoryItem) => {
    // 仅显示属于当前大分类且符合筛选条件的子分类
    const children: CategoryItem[] = categories.value
      .filter(
        (category: CategoryItem) => category.parentId === parent.id && matchedIds.has(category.id),
      )
      .sort(
        (first: CategoryItem, second: CategoryItem) =>
          first.sortOrder - second.sortOrder || first.id - second.id,
      )

    // 大分类和它的子分类都未命中时，不显示这个分类分组
    if (!matchedIds.has(parent.id) && children.length === 0) {
      return
    }

    // 创建大分类显示行，供匹配的子分类定位层级
    rows.push({
      category: parent,
      isChild: false,
      hasChildren: children.length > 0,
    })

    if (!expandedCategoryIds.value.includes(parent.id)) {
      return
    }

    // 创建已经展开的子分类显示行
    children.forEach((child: CategoryItem) => {
      rows.push({
        category: child,
        isChild: true,
        hasChildren: false,
      })
    })
  })

  return rows
})

// 加载后台全部分类，失败时保留明确提示并允许重新加载
async function loadCategories(): Promise<void> {
  if (!isPageActive || !authStore.isAdmin || isLoading.value) return
  const version: number = ++requestVersion
  const token: string = authStore.accessToken

  isLoading.value = true
  loadError.value = ''

  try {
    // 使用当前登录会话的令牌获取真实分类数据
    const result: CategoryResponse[] = await getAdminCategories(token)
    if (!isCurrentRequest(version, token)) return

    categories.value = result
  } catch (error: unknown) {
    if (!isCurrentRequest(version, token)) return
    // 请求失败时清空列表，避免将旧数据误认为最新结果
    categories.value = []
    loadError.value = error instanceof Error ? error.message : '获取后台分类列表失败，请稍后重试'
  } finally {
    if (isCurrentRequest(version, token)) isLoading.value = false
  }
}

// 打开新增分类弹窗
function openCreateDialog(): void {
  editingCategory.value = null
  isCategoryDialogVisible.value = true
}

// 打开编辑分类弹窗，表单内部会创建编辑副本
function openEditDialog(category: CategoryItem): void {
  editingCategory.value = category
  isCategoryDialogVisible.value = true
}

// 用后端实际保存的分类更新列表，并展开其所属的大分类
function handleCategorySaved(category: CategoryResponse): void {
  const categoryIndex: number = categories.value.findIndex(
    (item: CategoryItem) => item.id === category.id,
  )

  if (categoryIndex >= 0) {
    categories.value[categoryIndex] = category
  } else {
    categories.value.push(category)
  }

  if (category.parentId !== null && !expandedCategoryIds.value.includes(category.parentId)) {
    expandedCategoryIds.value = [...expandedCategoryIds.value, category.parentId]
  }

  noticeMessage.value = '分类已保存'
}

// 分类弹窗关闭后，清除当前编辑对象
function handleCategoryDialogClosed(): void {
  editingCategory.value = null
}

// 展开或收起指定大分类
function toggleCategory(categoryId: number): void {
  if (expandedCategoryIds.value.includes(categoryId)) {
    expandedCategoryIds.value = expandedCategoryIds.value.filter((id: number) => id !== categoryId)
    return
  }

  expandedCategoryIds.value = [...expandedCategoryIds.value, categoryId]
}

// 根据父分类编号取得显示名称
function getParentName(parentId: number | null): string {
  if (parentId === null) {
    return '—'
  }

  const parent: CategoryItem | undefined = categories.value.find(
    (category: CategoryItem) => category.id === parentId,
  )

  return parent?.name ?? '—'
}

// 点击查询后应用筛选条件，并展开匹配子分类所属的大分类
function handleSearch(): void {
  appliedFilters.name = filters.name.trim()
  appliedFilters.status = filters.status
  noticeMessage.value = ''

  if (!hasActiveFilters.value) {
    return
  }

  // 创建需要自动展开的父分类编号集合，避免重复编号
  const parentIds: Set<number> = new Set<number>()

  matchedCategories.value.forEach((category: CategoryItem) => {
    if (category.parentId !== null) {
      parentIds.add(category.parentId)
    }
  })

  expandedCategoryIds.value = Array.from(parentIds)
}

// 首次进入和更换账号时重新加载，同一会话续期不清空筛选或表单
watch(
  () => [authStore.sessionId, authStore.isAdmin] as const,
  () => {
    requestVersion += 1
    categories.value = []
    isLoading.value = false
    loadError.value = ''
    noticeMessage.value = ''
    filters.name = ''
    filters.status = ''
    appliedFilters.name = ''
    appliedFilters.status = ''
    expandedCategoryIds.value = []
    isCategoryDialogVisible.value = false
    editingCategory.value = null
    if (authStore.isAdmin) void loadCategories()
  },
  { immediate: true, flush: 'sync' },
)

onBeforeUnmount(() => {
  isPageActive = false
  requestVersion += 1
})
</script>

<template>
  <div class="categories-page">
    <!-- 页面标题和主要操作 -->
    <header class="page-heading">
      <div>
        <h1>分类管理</h1>
        <p>维护商品分类、层级、排序和启用状态</p>
      </div>

      <button class="button primary-button" type="button" @click="openCreateDialog">
        <svg viewBox="0 0 24 24" fill="none" aria-hidden="true">
          <path
            d="M12 5v14M5 12h14"
            stroke="currentColor"
            stroke-width="1.7"
            stroke-linecap="round"
          />
        </svg>
        新增分类
      </button>
    </header>

    <section class="table-panel" aria-labelledby="category-list-title">
      <!-- 筛选条件 -->
      <form class="filter-form" novalidate @submit.prevent="handleSearch">
        <div class="filter-field name-filter">
          <label for="admin-category-name">分类名称</label>

          <input
            id="admin-category-name"
            v-model="filters.name"
            type="search"
            maxlength="100"
            placeholder="请输入分类名称"
          />
        </div>

        <div class="filter-field">
          <label for="admin-category-status">启用状态</label>

          <select id="admin-category-status" v-model="filters.status">
            <option value="">全部状态</option>
            <option value="on">启用</option>
            <option value="off">停用</option>
          </select>
        </div>

        <div class="filter-actions">
          <button class="button primary-button" type="submit">查询</button>
        </div>
      </form>

      <!-- 分类操作结果提示 -->
      <div role="status" aria-live="polite" aria-atomic="true">
        <p v-if="noticeMessage" class="notice-message">
          {{ noticeMessage }}
        </p>
      </div>

      <div class="table-heading">
        <h2 id="category-list-title">分类列表</h2>
        <span>按父子层级展示</span>
      </div>

      <!-- 手机宽度下，表格在自身容器内滚动 -->
      <div
        class="table-wrapper"
        role="region"
        aria-label="分类列表，可横向滚动"
        :aria-busy="isLoading"
        tabindex="0"
      >
        <table class="category-table">
          <thead>
            <tr>
              <th scope="col">分类名称</th>
              <th scope="col">父分类</th>
              <th scope="col">排序</th>
              <th scope="col">状态</th>
              <th scope="col">操作</th>
            </tr>
          </thead>

          <tbody>
            <!-- 分别展示加载中、失败和成功后的空列表 -->
            <tr v-if="isLoading">
              <td colspan="5" class="empty-cell" role="status">正在加载分类…</td>
            </tr>

            <tr v-else-if="loadError">
              <td colspan="5" class="empty-cell">
                <p class="load-error-message" role="alert">{{ loadError }}</p>
                <button class="button" type="button" @click="loadCategories">重新加载</button>
              </td>
            </tr>

            <tr v-else-if="visibleRows.length === 0">
              <td colspan="5" class="empty-cell">
                {{ hasActiveFilters ? '没有符合条件的分类' : '暂无商品分类' }}
              </td>
            </tr>

            <!-- 加载成功后，按现有展开状态展示真实父子分类 -->
            <template v-else>
              <tr v-for="row in visibleRows" :key="row.category.id">
                <td>
                  <div class="category-name" :class="{ 'is-child': row.isChild }">
                    <button
                      v-if="row.hasChildren"
                      class="toggle-button"
                      :class="{
                        'is-expanded': expandedCategoryIds.includes(row.category.id),
                      }"
                      type="button"
                      :aria-expanded="expandedCategoryIds.includes(row.category.id)"
                      :aria-label="`展开或收起${row.category.name}`"
                      @click="toggleCategory(row.category.id)"
                    >
                      <svg viewBox="0 0 16 16" fill="none" aria-hidden="true">
                        <path
                          d="m6 3 5 5-5 5"
                          stroke="currentColor"
                          stroke-width="1.6"
                          stroke-linecap="round"
                          stroke-linejoin="round"
                        />
                      </svg>
                    </button>

                    <span v-else class="toggle-placeholder" aria-hidden="true"></span>

                    <span class="category-text">{{ row.category.name }}</span>
                  </div>
                </td>

                <td>{{ getParentName(row.category.parentId) }}</td>
                <td>{{ row.category.sortOrder }}</td>

                <td>
                  <span class="status-badge" :class="{ 'is-active': row.category.isActive }">
                    {{ row.category.isActive ? '启用' : '停用' }}
                  </span>
                </td>

                <td>
                  <button class="text-button" type="button" @click="openEditDialog(row.category)">
                    编辑
                  </button>
                </td>
              </tr>
            </template>
          </tbody>
        </table>
      </div>

    </section>

    <!-- 分类新增和编辑共用一个弹窗 -->
    <CategoryFormDialog
      v-model="isCategoryDialogVisible"
      :category="editingCategory"
      :parent-categories="parentCategories"
      @saved="handleCategorySaved"
      @closed="handleCategoryDialogClosed"
    />
  </div>
</template>

<style scoped>
/* 页面允许在后台内容容器中缩小 */
.categories-page {
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

/* 页面内按钮的公共样式 */
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

/* 筛选和表格放在白色卡片中 */
.table-panel {
  min-width: 0;
  overflow: hidden;
  border: 1px solid #e6ebf3;
  border-radius: 12px;
  background: #ffffff;
}

/* 筛选条件 */
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

/* 功能提示 */
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

/* 表格内部横向滚动 */
.table-wrapper {
  width: 100%;
  overflow-x: auto;
}

.table-wrapper:focus-visible {
  outline: 3px solid #93b6ff;
  outline-offset: -3px;
}

.category-table {
  width: 100%;
  min-width: 660px;
  border-collapse: collapse;
  text-align: left;
}

.category-table th {
  padding: 12px 16px;
  border-top: 1px solid #f0f3f8;
  border-bottom: 1px solid #edf1f7;
  background: #f8fafe;
  color: #8c99ad;
  font-size: 12px;
  font-weight: 400;
  white-space: nowrap;
}

.category-table td {
  padding: 16px;
  border-bottom: 1px solid #f0f3f7;
  color: #67768e;
  font-size: 12px;
}

.category-table th:first-child,
.category-table td:first-child {
  padding-left: 23px;
}

.category-table th:last-child,
.category-table td:last-child {
  padding-right: 23px;
}

.category-table tbody tr:last-child td {
  border-bottom: none;
}

/* 分类名称和展开按钮 */
.category-name {
  position: relative;
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 180px;
}

.category-text {
  max-width: 240px;
  color: #3c4b63;
  overflow-wrap: anywhere;
}

.toggle-button,
.toggle-placeholder {
  width: 22px;
  height: 26px;
  flex-shrink: 0;
}

.toggle-button {
  display: grid;
  place-items: center;
  padding: 0;
  border: none;
  border-radius: 5px;
  background: transparent;
  color: #8c9bb1;
}

.toggle-button:hover {
  background: #eef4ff;
  color: #2864e8;
}

.toggle-button svg {
  width: 13px;
  height: 13px;
}

.toggle-button.is-expanded svg {
  transform: rotate(90deg);
}

/* 子分类缩进，并显示层级连接线 */
.category-name.is-child {
  padding-left: 30px;
}

.category-name.is-child::before {
  content: '';
  position: absolute;
  top: 5px;
  left: 20px;
  width: 10px;
  height: 10px;
  border-bottom: 1px solid #dce4f1;
  border-left: 1px solid #dce4f1;
}

/* 启用状态 */
.status-badge {
  display: inline-block;
  padding: 3px 8px;
  border-radius: 5px;
  background: #f0f3f8;
  color: #96a1b3;
  font-size: 11px;
  white-space: nowrap;
}

.status-badge.is-active {
  background: #eef8f2;
  color: #68a789;
}

.text-button {
  padding: 2px 0;
  border: none;
  background: transparent;
  color: #648dd6;
  font-size: 12px;
}

.text-button:hover {
  color: #2864e8;
}

/* 空列表提示 */
.category-table .empty-cell {
  padding: 64px 20px;
  color: #a1adc0;
  text-align: center;
}

/* 加载失败信息与重新加载按钮分开显示 */
.category-table .load-error-message {
  margin: 0 0 12px;
  color: #bd4848;
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

  .table-heading {
    padding-right: 16px;
    padding-left: 16px;
  }
}
</style>
