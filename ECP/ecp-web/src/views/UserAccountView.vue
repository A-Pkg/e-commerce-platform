<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import {
  PasswordUnauthorizedError,
  PasswordForbiddenError,
  PasswordConflictError,
  PasswordChangeUncertainError,
  type ChangePasswordRequest,
} from '@/api/auth'
import StoreHeader from '@/components/layout/StoreHeader.vue'
import { useAuthStore } from '@/stores/auth'

// 只传递提示类型，不在路由中传递密码
type PasswordLoginReason =
  | 'password-changed'
  | 'password-change-uncertain'
  | 'password-change-conflict'
  | 'session-expired'

const router = useRouter()
const authStore = useAuthStore()

// 三个密码仅保存在当前页面内存中
const form = reactive<ChangePasswordRequest>({
  currentPassword: '',
  newPassword: '',
  confirmNewPassword: '',
})

const isSubmitting = ref<boolean>(false)
const isRedirecting = ref<boolean>(false)
const errorMessage = ref<string>('')

const canManageAccount = computed<boolean>(
  () => authStore.isAuthenticated && authStore.currentUser?.role === 'User',
)

const isBusy = computed<boolean>(() => isSubmitting.value || isRedirecting.value)

let isPageActive: boolean = true
let requestVersion: number = 0

// 清空页面中的密码
function clearPasswords(): void {
  form.currentPassword = ''
  form.newPassword = ''
  form.confirmNewPassword = ''
}

// 只处理当前页面、当前请求和原登录会话的响应
function isCurrentRequest(version: number, accessToken: string): boolean {
  return (
    isPageActive &&
    version === requestVersion &&
    canManageAccount.value &&
    authStore.isCurrentSession(accessToken)
  )
}

// 清除旧会话，保留账号供登录页回填
async function redirectToLogin(reason: PasswordLoginReason): Promise<void> {
  // 阻止会话监听中的普通跳转覆盖本次结果提示
  isRedirecting.value = true
  clearPasswords()
  const ended: boolean = await authStore.endLocalSession(reason === 'password-changed')
  if (!ended || !isPageActive) {
    isRedirecting.value = false
    return
  }

  if (reason === 'session-expired') {
    authStore.sessionExpired = true
    await router.replace({ name: 'login' })
    return
  }

  await router.replace({
    name: 'login',
    query: { reason },
  })
}

// 校验并提交修改密码请求
async function handleSubmit(): Promise<void> {
  if (!isPageActive || !canManageAccount.value || isBusy.value) {
    return
  }

  errorMessage.value = ''

  // 密码保持原样，不去除首尾空格
  if (!form.currentPassword) {
    errorMessage.value = '请输入原密码'
    return
  }

  if (form.currentPassword.length > 128) {
    errorMessage.value = '原密码不能超过 128 个字符'
    return
  }

  if (!form.newPassword) {
    errorMessage.value = '请输入新密码'
    return
  }

  if (form.newPassword.length < 8 || form.newPassword.length > 128) {
    errorMessage.value = '新密码长度必须为 8～128 个字符'
    return
  }

  if (!form.confirmNewPassword) {
    errorMessage.value = '请再次输入新密码'
    return
  }

  if (form.newPassword !== form.confirmNewPassword) {
    errorMessage.value = '两次输入的新密码不一致'
    return
  }

  if (form.newPassword === form.currentPassword) {
    errorMessage.value = '新密码不能与原密码相同'
    return
  }

  const accessToken: string = authStore.accessToken
  const version: number = requestVersion
  const request: ChangePasswordRequest = { ...form }
  let loginReason: PasswordLoginReason | null = null

  isSubmitting.value = true

  try {
    await authStore.changeAccountPassword(request, accessToken)

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    loginReason = 'password-changed'
  } catch (error: unknown) {
    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    if (error instanceof PasswordUnauthorizedError) {
      loginReason = 'session-expired'
    } else if (error instanceof PasswordChangeUncertainError) {
      loginReason = 'password-change-uncertain'
    } else if (error instanceof PasswordConflictError) {
      loginReason = 'password-change-conflict'
    } else if (error instanceof PasswordForbiddenError) {
      clearPasswords()
      errorMessage.value = error.message
    } else {
      // 原密码错误、参数错误、限流等情况留在当前页面提示
      errorMessage.value =
        error instanceof Error ? error.message : '修改密码失败，请稍后重试'
    }
  } finally {
    if (isCurrentRequest(version, accessToken)) {
      isSubmitting.value = false
    }
  }

  // 页面跳转与修改请求的错误处理分开
  if (loginReason !== null && isCurrentRequest(version, accessToken)) {
    await redirectToLogin(loginReason)
  }
}

// 会话变化时清空密码，并使上一会话的请求失效
watch(
  () =>
    [authStore.sessionId, authStore.isAuthenticated, authStore.currentUser?.role] as const,
  () => {
    requestVersion += 1
    clearPasswords()
    isSubmitting.value = false
    errorMessage.value = ''

    if (!isPageActive || isRedirecting.value) {
      return
    }

    if (!authStore.isAuthenticated) {
      void router.replace({ name: 'login' })
    } else if (authStore.currentUser?.role !== 'User') {
      void router.replace({ name: 'admin-products' })
    }
  },
  { immediate: true, flush: 'sync' },
)

// 离页后清空密码，不处理尚未返回的旧请求
onBeforeUnmount(() => {
  isPageActive = false
  requestVersion += 1
  clearPasswords()
})
</script>

<template>
  <div class="account-page">
    <StoreHeader />

    <main class="account-content">
      <header class="page-heading">
        <h1>账号管理</h1>
        <p>修改登录密码，修改成功后需要重新登录。</p>
      </header>

      <section
        v-if="canManageAccount"
        class="account-panel"
        aria-labelledby="change-password-title"
      >
        <h2 id="change-password-title">修改密码</h2>

        <form novalidate :aria-busy="isBusy" @submit.prevent="handleSubmit">
          <div class="form-field">
            <label for="account-current-password">原密码</label>
            <input
              id="account-current-password"
              v-model="form.currentPassword"
              type="password"
              autocomplete="current-password"
              maxlength="128"
              placeholder="请输入原密码"
              :disabled="isBusy"
            />
          </div>

          <div class="form-field">
            <label for="account-new-password">新密码</label>
            <input
              id="account-new-password"
              v-model="form.newPassword"
              type="password"
              autocomplete="new-password"
              maxlength="128"
              placeholder="请输入 8～128 个字符的新密码"
              :disabled="isBusy"
            />
          </div>

          <div class="form-field">
            <label for="account-confirm-password">确认新密码</label>
            <input
              id="account-confirm-password"
              v-model="form.confirmNewPassword"
              type="password"
              autocomplete="new-password"
              maxlength="128"
              placeholder="请再次输入新密码"
              :disabled="isBusy"
            />
          </div>

          <p v-if="errorMessage" class="error-message" role="alert">
            {{ errorMessage }}
          </p>

          <button class="submit-button" type="submit" :disabled="isBusy">
            {{ isRedirecting ? '正在返回登录…' : isSubmitting ? '正在修改…' : '修改密码' }}
          </button>
        </form>
      </section>
    </main>
  </div>
</template>

<style scoped>
.account-page {
  min-height: 100vh;
  background: #f6f8fc;
}

.account-content {
  max-width: 760px;
  margin: 0 auto;
  padding: 28px 24px 48px;
}

.page-heading {
  margin-bottom: 22px;
}

.page-heading h1 {
  margin: 0 0 8px;
  color: #35445d;
  font-size: 25px;
  font-weight: 600;
}

.page-heading p {
  margin: 0;
  color: #919eb2;
  font-size: 13px;
}

.account-panel {
  padding: 26px;
  border: 1px solid #e6ebf3;
  border-radius: 12px;
  background: #ffffff;
}

.account-panel h2 {
  margin: 0 0 24px;
  color: #52617a;
  font-size: 16px;
  font-weight: 600;
}

form {
  display: grid;
  gap: 20px;
}

.form-field {
  display: grid;
  gap: 8px;
}

.form-field label {
  color: #67768e;
  font-size: 13px;
}

.form-field input {
  width: 100%;
  min-height: 42px;
  padding: 10px 12px;
  border: 1px solid #dfe6ef;
  border-radius: 7px;
  background: #ffffff;
  color: #3c4b63;
  font-size: 14px;
}

.form-field input:focus {
  outline: none;
  border-color: #83a9fa;
  box-shadow: 0 0 0 3px rgba(40, 100, 232, 0.08);
}

.form-field input::placeholder {
  color: #a8b2c2;
}

.submit-button {
  justify-self: start;
  min-height: 40px;
  padding: 9px 22px;
  border: 1px solid #2864e8;
  border-radius: 7px;
  background: #2864e8;
  color: #ffffff;
  font-size: 13px;
}

.submit-button:hover:not(:disabled) {
  background: #1e55d0;
}

.form-field input:disabled,
.submit-button:disabled {
  cursor: not-allowed;
  opacity: 0.6;
}

.error-message {
  margin: 0;
  color: #bd4848;
  font-size: 13px;
}

@media (max-width: 600px) {
  .account-content {
    padding: 22px 16px 36px;
  }

  .account-panel {
    padding: 22px 18px;
  }
}
</style>
