<script setup lang="ts">
import { ref } from 'vue'
import { register } from '@/api/auth'
import type { RegisterResponse } from '@/api/auth'

// 通知父组件注册成功或返回登录
const emit = defineEmits<{
  registered: [username: string]
  cancel: []
}>()

// 保存注册表单输入，密码不会写入浏览器存储
const username = ref<string>('')
const password = ref<string>('')
const confirmPassword = ref<string>('')

// 保存提交状态和错误提示
const isSubmitting = ref<boolean>(false)
const errorMessage = ref<string>('')

// 校验注册表单并调用后端注册接口
async function handleRegister(): Promise<void> {
  // 防止请求进行中重复提交
  if (isSubmitting.value) {
    return
  }

  errorMessage.value = ''

  // 账号去除首尾空格，两个密码保持原样
  const submittedUsername: string = username.value.trim()

  // 检查三个必填项
  if (!submittedUsername) {
    errorMessage.value = '请输入账号'
    return
  }
  if (!password.value) {
    errorMessage.value = '请输入密码'
    return
  }
  if (!confirmPassword.value) {
    errorMessage.value = '请再次输入密码'
    return
  }

  // 校验账号长度及允许的字符
  if (submittedUsername.length < 3 || submittedUsername.length > 50) {
    errorMessage.value = '账号长度必须为 3～50 个字符'
    return
  }
  if (!/^[a-zA-Z0-9_]+$/.test(submittedUsername)) {
    errorMessage.value = '账号只能包含英文字母、数字和下划线'
    return
  }

  // 校验密码长度以及两次密码是否一致
  if (password.value.length < 8 || password.value.length > 128) {
    errorMessage.value = '密码长度必须为 8～128 个字符'
    return
  }
  if (password.value !== confirmPassword.value) {
    errorMessage.value = '两次输入的密码不一致'
    return
  }

  isSubmitting.value = true

  try {
    // 使用现有请求方法提交注册数据
    const result: RegisterResponse = await register({
      username: submittedUsername,
      password: password.value,
      confirmPassword: confirmPassword.value,
    })

    // 清空密码，再通知父组件切回登录并回填账号
    password.value = ''
    confirmPassword.value = ''
    emit('registered', result.username)
  } catch (error: unknown) {
    // 失败时保留表单输入，方便用户修正后重试
    if (error instanceof TypeError || error instanceof SyntaxError) {
      errorMessage.value = '注册请求失败，请检查服务是否正常运行'
    } else {
      errorMessage.value = error instanceof Error ? error.message : '注册失败，请稍后重试'
    }
  } finally {
    // 请求结束后恢复表单操作
    isSubmitting.value = false
  }
}

// 清空注册表单，并通知父组件返回登录
function handleCancel(): void {
  if (isSubmitting.value) {
    return
  }

  username.value = ''
  password.value = ''
  confirmPassword.value = ''
  errorMessage.value = ''
  emit('cancel')
}
</script>

<template>
  <form class="register-form" novalidate :aria-busy="isSubmitting" @submit.prevent="handleRegister">
    <div>
      <label for="register-username">账号</label>
      <input
        id="register-username"
        v-model="username"
        type="text"
        placeholder="3～50 位英文字母、数字或下划线"
        autocomplete="username"
        required
        :disabled="isSubmitting"
      />
    </div>

    <div>
      <label for="register-password">密码</label>
      <input
        id="register-password"
        v-model="password"
        type="password"
        placeholder="请输入 8～128 个字符的密码"
        autocomplete="new-password"
        required
        :disabled="isSubmitting"
      />
    </div>

    <div>
      <label for="register-confirm-password">确认密码</label>
      <input
        id="register-confirm-password"
        v-model="confirmPassword"
        type="password"
        placeholder="请再次输入密码"
        autocomplete="new-password"
        required
        :disabled="isSubmitting"
      />
    </div>

    <button type="submit" :disabled="isSubmitting">
      {{ isSubmitting ? '注册中…' : '注册' }}
    </button>

    <button type="button" class="secondary-button" :disabled="isSubmitting" @click="handleCancel">
      返回登录
    </button>

    <!-- 显示前端校验或后端返回的错误，不展示密码 -->
    <p v-if="errorMessage" class="register-error" role="alert">
      {{ errorMessage }}
    </p>
  </form>
</template>

<style scoped>
/* 注册组件独立维护表单样式，沿用登录卡片的间距和配色 */
.register-form {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.register-form label {
  display: block;
  margin-bottom: 8px;
  font-size: 14px;
  color: #374151;
}

.register-form input {
  width: 100%;
  box-sizing: border-box;
  padding: 12px 14px;
  border: 1px solid #d1d5db;
  border-radius: 8px;
  background: #ffffff;
  font-size: 14px;
  color: #1f2937;
  transition:
    border-color 0.2s,
    box-shadow 0.2s;
}

.register-form input::placeholder {
  color: #9ca3af;
}

.register-form input:focus {
  outline: none;
  border-color: #2563eb;
  box-shadow: 0 0 0 3px rgba(37, 99, 235, 0.12);
}

.register-form button {
  margin-top: 4px;
  padding: 12px;
  border: none;
  border-radius: 8px;
  background: #2563eb;
  font-size: 15px;
  font-weight: 500;
  color: #ffffff;
  cursor: pointer;
  transition: background 0.2s;
}

.register-form button:hover {
  background: #1d4ed8;
}

.register-form button:focus-visible {
  outline: 3px solid #93c5fd;
  outline-offset: 3px;
}

/* 返回登录使用次要按钮样式 */
.register-form .secondary-button {
  border: 1px solid #d1d5db;
  background: #ffffff;
  color: #374151;
}

.register-form .secondary-button:hover {
  background: #f9fafb;
}

.register-form button:disabled {
  opacity: 0.65;
  cursor: not-allowed;
}

.register-form input:disabled {
  background: #f9fafb;
  cursor: not-allowed;
}

.register-form .register-error {
  margin: 0;
  text-align: center;
  font-size: 14px;
  color: #dc2626;
}
</style>
