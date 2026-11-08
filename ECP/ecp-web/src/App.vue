<script setup lang="ts">
import { watch } from 'vue'
import { storeToRefs } from 'pinia'
import { RouterView, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

// 获取路由实例，用于会话过期后的页面跳转
const router = useRouter()

// 获取统一管理登录状态的仓库
const authStore = useAuthStore()

// 提取会话过期标记，保留响应式关联
const { sessionExpired } = storeToRefs(authStore)

// 另一标签页主动退出时也返回登录页
watch(() => authStore.externalLogout, () => {
  void router.replace({ name: 'login' })
})

// 其他标签页更换账号后，重新检查当前页面权限
watch(
  () => [authStore.isAuthenticated, authStore.isSigningIn, authStore.currentUser?.role] as const,
  ([authenticated, signingIn]) => {
    if (!authenticated || signingIn) return
    const route = router.currentRoute.value
    if (route.meta.requiresAdmin === true && !authStore.isAdmin) {
      void router.replace({ name: 'home' })
    } else if (route.meta.requiresUser === true && authStore.currentUser?.role !== 'User') {
      void router.replace({ name: 'admin-products' })
    } else if (route.name === 'login') {
      void router.replace({ name: authStore.isAdmin ? 'admin-products' : 'home' })
    }
  },
)

// 初次恢复遇到网络问题时允许用户重试，不清除浏览器中的刷新 Cookie
async function retrySession(): Promise<void> {
  await authStore.refreshSession()
}

// 会话过期后返回登录页，提示和账号回填由登录页处理
watch(
  sessionExpired,
  (expired: boolean) => {
    if (!expired) {
      return
    }

    // 保留过期标记，让登录页显示重新登录提示
    void router.replace({ name: 'login' })
  },
  { immediate: true },
)
</script>

<template>
  <div v-if="authStore.sessionNotice" class="session-notice" role="alert">
    <span>{{ authStore.sessionNotice }}</span>
    <button
      v-if="!authStore.isAuthenticated"
      type="button"
      :disabled="authStore.isRestoring"
      @click="retrySession"
    >
      {{ authStore.isRestoring ? '正在重试…' : '重试恢复登录' }}
    </button>
  </div>
  <p v-if="!authStore.initialized" class="session-loading" role="status">正在恢复登录…</p>
  <!-- 根据当前地址显示首页或登录页 -->
  <RouterView v-else />
</template>

<style scoped>
.session-notice {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  padding: 12px 20px;
  color: #35445d;
  background: #eaf2ff;
}

.session-notice button {
  padding: 6px 10px;
  color: #fff;
  background: #3978d7;
  border: 0;
  border-radius: 6px;
  cursor: pointer;
}

.session-notice button:disabled { opacity: .6; cursor: wait; }
.session-loading { padding: 32px; text-align: center; color: #60708a; }
</style>
