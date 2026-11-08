<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useRoute, useRouter } from 'vue-router'
import RegisterForm from '@/components/auth/RegisterForm.vue'
import { useAuthStore } from '@/stores/auth'

// 获取路由实例，用于登录成功或游客访问后的页面跳转
const router = useRouter()
const route = useRoute()

// 获取统一管理登录状态的仓库
const authStore = useAuthStore()

// 提取上次账号和会话过期标记，保留响应式关联
const { lastUsername, sessionExpired } = storeToRefs(authStore)

// 保存登录表单输入，密码仅保存在当前组件内
const username = ref<string>(lastUsername.value)
const password = ref<string>('')

// 保存提交状态和页面提示
const isSubmitting = ref<boolean>(false)
const errorMessage = ref<string>('')
const successMessage = ref<string>('')

// 控制登录和注册卡片切换
const authMode = ref<'login' | 'register'>('login')

// 标记页面是否仍然存在，避免离开后处理旧登录请求的结果
let isPageActive = true

// 处理会话过期；进入登录页时也检查一次
watch(
  sessionExpired,
  (expired: boolean) => {
    if (!expired) {
      return
    }

    username.value = lastUsername.value
    password.value = ''
    successMessage.value = ''
    errorMessage.value = '登录已过期，请重新登录'
    authMode.value = 'login'
  },
  { immediate: true },
)

// 显示修改密码后的结果提示，与普通会话过期提示区分
watch(
  () => route.query.reason,
  reason => {
    if (
      reason !== 'password-changed' &&
      reason !== 'password-change-uncertain' &&
      reason !== 'password-change-conflict'
    ) {
      return
    }

    authMode.value = 'login'
    password.value = ''
    successMessage.value = ''
    errorMessage.value = ''

    if (reason === 'password-changed') {
      successMessage.value = '密码修改成功，请重新登录'
    } else if (reason === 'password-change-uncertain') {
      errorMessage.value =
        '暂时无法确认密码是否已修改，请先尝试使用新密码登录；若失败，再使用原密码。'
    } else {
      errorMessage.value = '账号状态或密码已发生变化，请重新登录后重试'
    }

    // 提示保留在当前页面，移除路由标记，避免刷新后重复显示
    void router.replace({
      query: { ...route.query, reason: undefined },
    })
  },
  { immediate: true },
)

// 清除密码和旧提示，切换到注册组件
function openRegistration(): void {
  if (isSubmitting.value) {
    return
  }

  password.value = ''
  errorMessage.value = ''
  successMessage.value = ''
  authMode.value = 'register'
}

// 返回登录表单，清除旧提示和密码
function returnToLogin(): void {
  password.value = ''
  errorMessage.value = ''
  successMessage.value = ''
  authMode.value = 'login'
}

// 注册成功后切回登录，并回填注册账号
function handleRegistered(registeredUsername: string): void {
  returnToLogin()
  username.value = registeredUsername
  successMessage.value = '注册成功，请登录'
}

// 页面管理表单，登录请求、会话保存和标签页同步交给仓库
async function handleLogin(): Promise<void> {
  if (isSubmitting.value) return
  errorMessage.value = ''
  successMessage.value = ''

  if (!username.value.trim() || !password.value) {
    errorMessage.value = '请输入账号和密码'
    return
  }

  isSubmitting.value = true
  try {
    const signedIn: boolean = await authStore.signIn({
      username: username.value.trim(),
      password: password.value,
    })
    if (!isPageActive || !signedIn) return

    password.value = ''
    await router.replace({ name: authStore.isAdmin ? 'admin-products' : 'home' })
  } catch (error: unknown) {
    if (!isPageActive) return
    errorMessage.value = error instanceof Error ? error.message : '登录失败，请稍后重试'
  } finally {
    if (isPageActive) isSubmitting.value = false
  }
}

// 先结束本浏览器可能保留的会话，再以游客身份进入商城
async function enterAsGuest(): Promise<void> {
  if (isSubmitting.value) return
  isSubmitting.value = true
  try {
    const signedOut: boolean = await authStore.signOut()
    if (!isPageActive || !signedOut) return
    username.value = ''
    password.value = ''
    errorMessage.value = ''
    successMessage.value = ''
    await router.replace({ name: 'home' })
  } finally {
    if (isPageActive) isSubmitting.value = false
  }
}

// 离开页面时清除密码，并停止处理未完成登录请求的结果
onBeforeUnmount(() => {
  isPageActive = false
  password.value = ''
})
</script>

<template>
  <main class="login-page">
    <section class="login-box">
      <h1>ECP 商城</h1>
      <p>{{ authMode === 'register' ? '账号注册' : '账号登录' }}</p>

      <!-- 注册组件继续自行管理输入、校验和请求状态 -->
      <RegisterForm
        v-if="authMode === 'register'"
        @registered="handleRegistered"
        @cancel="returnToLogin"
      />

      <form v-else novalidate :aria-busy="isSubmitting" @submit.prevent="handleLogin">
        <div>
          <label for="username">账号</label>
          <input
            id="username"
            v-model="username"
            type="text"
            placeholder="请输入账号"
            autocomplete="username"
            :disabled="isSubmitting"
          />
        </div>

        <div>
          <label for="password">密码</label>
          <input
            id="password"
            v-model="password"
            type="password"
            placeholder="请输入密码"
            autocomplete="current-password"
            :disabled="isSubmitting"
          />
        </div>

        <button type="submit" :disabled="isSubmitting">
          {{ isSubmitting ? '登录中…' : '登录' }}
        </button>

        <button
          type="button"
          class="secondary-button"
          :disabled="isSubmitting"
          @click="openRegistration"
        >
          去注册
        </button>

        <button type="button" class="guest-button" :disabled="isSubmitting" @click="enterAsGuest">
          以游客身份进入商城
        </button>

        <!-- 显示登录失败或会话过期提示 -->
        <p v-if="errorMessage" class="login-error" role="alert">
          {{ errorMessage }}
        </p>

        <!-- 注册成功后提示用户登录 -->
        <p v-if="successMessage" class="login-success" role="status">
          {{ successMessage }}
        </p>
      </form>
    </section>
  </main>
</template>

<style scoped>
/* 登录页背景和居中布局 */
.login-page {
  min-height: 100vh;
  box-sizing: border-box;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 24px;
  background: #f3f5f9;
}

/* 登录卡片 */
.login-box {
  width: 100%;
  max-width: 380px;
  box-sizing: border-box;
  padding: 36px;
  border: 1px solid #e5e7eb;
  border-radius: 16px;
  background: #ffffff;
  box-shadow: 0 12px 36px rgba(30, 50, 80, 0.08);
}

.login-box h1 {
  margin: 0;
  text-align: center;
  font-size: 26px;
  font-weight: 600;
  color: #1f2937;
}

.login-box p {
  margin: 10px 0 30px;
  text-align: center;
  font-size: 14px;
  color: #6b7280;
}

/* 表单排列 */
.login-box form {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.login-box label {
  display: block;
  margin-bottom: 8px;
  font-size: 14px;
  color: #374151;
}

.login-box input {
  width: 100%;
  box-sizing: border-box;
  padding: 12px 14px;
  border: 1px solid #d1d5db;
  border-radius: 8px;
  background: #ffffff;
  font-size: 14px;
  color: #1f2937;
}

.login-box input::placeholder {
  color: #9ca3af;
}

.login-box input:focus {
  outline: none;
  border-color: #2563eb;
  box-shadow: 0 0 0 3px rgba(37, 99, 235, 0.12);
}

/* 登录主按钮 */
.login-box button {
  padding: 12px;
  border: none;
  border-radius: 8px;
  background: #2563eb;
  font-size: 15px;
  font-weight: 500;
  color: #ffffff;
  cursor: pointer;
}

.login-box button:hover {
  background: #1d4ed8;
}

.login-box button:focus-visible {
  outline: 3px solid #93c5fd;
  outline-offset: 3px;
}

/* 注册入口 */
.login-box .secondary-button {
  border: 1px solid #d1d5db;
  background: #ffffff;
  color: #374151;
}

.login-box .secondary-button:hover {
  background: #f9fafb;
}

/* 游客入口 */
.login-box .guest-button {
  background: transparent;
  font-size: 14px;
  color: #2563eb;
}

.login-box .guest-button:hover {
  background: #eff6ff;
}

/* 请求期间禁用表单操作 */
.login-box button:disabled {
  opacity: 0.65;
  cursor: not-allowed;
}

.login-box input:disabled {
  background: #f9fafb;
  cursor: not-allowed;
}

/* 错误和成功提示 */
.login-box .login-error {
  margin: 0;
  color: #dc2626;
}

.login-box .login-success {
  margin: 0;
  color: #15803d;
}

/* 小屏幕缩小卡片内边距 */
@media (max-width: 480px) {
  .login-box {
    padding: 28px 24px;
  }
}
</style>
