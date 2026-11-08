<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { getCategories } from '@/api/categories'
import type { CategoryResponse } from '@/api/categories'

// 保存后端返回的分类列表
const categories = ref<CategoryResponse[]>([])

// 标记分类请求是否正在进行
const isLoading = ref<boolean>(false)

// 保存加载失败时显示的提示
const errorMessage = ref<string>('')

// 加载分类列表，也供失败后的重新加载按钮调用
async function loadCategories(): Promise<void> {
  // 防止请求进行中重复加载
  if (isLoading.value) {
    return
  }

  // 开始加载，清除旧提示和旧列表
  isLoading.value = true
  errorMessage.value = ''
  categories.value = []

  try {
    // 调用商城分类接口，沿用后端返回的排列顺序
    categories.value = await getCategories()
  } catch (error: unknown) {
    // 网络或响应解析异常时，显示统一提示
    if (error instanceof TypeError || error instanceof SyntaxError) {
      errorMessage.value = '分类加载失败，请检查网络连接或服务是否正常运行'
    } else {
      errorMessage.value = error instanceof Error ? error.message : '分类加载失败，请稍后重试'
    }
  } finally {
    // 请求结束后恢复加载状态
    isLoading.value = false
  }
}

// 组件挂载到页面后，自动加载分类
onMounted(loadCategories)
</script>

<template>
  <section class="category-list" :aria-busy="isLoading">
    <h2>商品分类</h2>

    <!-- 请求期间显示加载状态 -->
    <p v-if="isLoading" class="category-message" role="status">加载中…</p>

    <!-- 请求失败时显示提示，并允许重新加载 -->
    <div v-else-if="errorMessage" class="category-error-panel">
      <p class="category-error" role="alert">
        {{ errorMessage }}
      </p>

      <button type="button" class="retry-button" :disabled="isLoading" @click="loadCategories">
        重新加载
      </button>
    </div>

    <!-- 请求成功，但没有已启用的分类 -->
    <p v-else-if="categories.length === 0" class="category-message" role="status">暂无商品分类</p>

    <!-- 展示分类名称，使用分类编号作为稳定的列表标识 -->
    <ul v-else class="category-items">
      <li v-for="category in categories" :key="category.id" class="category-item">
        {{ category.name }}
      </li>
    </ul>
  </section>
</template>

<style scoped>
/* 分类组件独立维护卡片样式 */
.category-list {
  width: 100%;
  box-sizing: border-box;
  padding: 24px;
  border: 1px solid #e5e7eb;
  border-radius: 16px;
  background: #ffffff;
  color: #1f2937;
}

.category-list h2 {
  margin: 0 0 20px;
  font-size: 20px;
  font-weight: 600;
}

/* 加载中或没有分类时的提示 */
.category-list .category-message {
  margin: 0;
  font-size: 14px;
  color: #6b7280;
}

/* 分类名称自动换行排列 */
.category-items {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.category-item {
  max-width: 100%;
  box-sizing: border-box;
  padding: 10px 16px;
  border-radius: 8px;
  background: #eff6ff;
  font-size: 14px;
  color: #1d4ed8;
  overflow-wrap: anywhere;
}

/* 错误提示和重试按钮 */
.category-error-panel {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 16px;
}

.category-list .category-error {
  margin: 0;
  font-size: 14px;
  color: #dc2626;
}

.category-list .retry-button {
  padding: 10px 16px;
  border: none;
  border-radius: 8px;
  background: #2563eb;
  font-size: 14px;
  color: #ffffff;
  cursor: pointer;
}

.category-list .retry-button:hover {
  background: #1d4ed8;
}

.category-list .retry-button:focus-visible {
  outline: 3px solid #93c5fd;
  outline-offset: 3px;
}

.category-list .retry-button:disabled {
  opacity: 0.65;
  cursor: not-allowed;
}
</style>
