import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import {
  changePassword,
  getCurrentUser,
  login,
  logoutSession,
  refreshAccessToken,
  SessionUnauthorizedError,
} from '@/api/auth'
import type {
  AccessTokenResponse,
  ChangePasswordRequest,
  CurrentUserResponse,
  LoginRequest,
} from '@/api/auth'
import { withAuthLock } from '@/utils/authLock'
import { readTokenSessionId } from '@/utils/tokenSession'

// localStorage 只保存不含身份凭据的事件标记；令牌和用户信息仍只放内存
const AUTH_EVENT_KEY = 'ecp-auth-event'
type AuthEvent = 'login' | 'logout' | 'expired' | 'password-changed'

export const useAuthStore = defineStore('auth', () => {
  // 访问令牌仅保存在当前标签页内存中
  const accessToken = ref<string>('')
  const expiresAtUtc = ref<string>('')

  // sid 标识一次登录，换访问令牌时保持不变；整个会话截止时间由后端决定
  const sessionId = ref<string>('')
  const sessionExpiresAtUtc = ref<string>('')

  // 用户信息与账号回填均不持久化到浏览器存储
  const currentUser = ref<CurrentUserResponse | null>(null)
  const lastUsername = ref<string>('')

  // 页面提示和初始化、提交状态
  const sessionExpired = ref<boolean>(false)
  const sessionNotice = ref<string>('')
  const initialized = ref<boolean>(false)
  const isSigningIn = ref<boolean>(false)
  const isSigningOut = ref<boolean>(false)
  const isRestoring = ref<boolean>(false)

  // 仅通知 App 处理其他标签页的主动退出，不覆盖本页的改密结果提示
  const externalLogout = ref<number>(0)
  let knownEvent: string = localStorage.getItem(AUTH_EVENT_KEY) ?? ''
  // 前端请求版本；清理会话后，旧异步响应不能重新写回状态
  let generation: number = 0
  let refreshTimer: ReturnType<typeof setTimeout> | null = null
  let refreshPromise: Promise<boolean> | null = null
  let initializePromise: Promise<void> | null = null

  const isAuthenticated = computed<boolean>(
    () => accessToken.value !== '' && currentUser.value !== null,
  )
  const isAdmin = computed<boolean>(
    () => isAuthenticated.value && currentUser.value?.role === 'Admin',
  )

  function currentEvent(): string {
    return localStorage.getItem(AUTH_EVENT_KEY) ?? ''
  }

  function publish(event: AuthEvent): void {
    knownEvent = event + ':' + crypto.randomUUID()
    localStorage.setItem(AUTH_EVENT_KEY, knownEvent)
  }

  function clearTimer(): void {
    if (refreshTimer !== null) clearTimeout(refreshTimer)
    refreshTimer = null
  }

  // 清理本页状态并作废尚未完成的请求；不会调用后端退出接口
  function clearSession(preserveUsername: boolean = false): void {
    generation += 1
    clearTimer()
    accessToken.value = ''
    expiresAtUtc.value = ''
    sessionId.value = ''
    sessionExpiresAtUtc.value = ''
    currentUser.value = null
    sessionExpired.value = false
    sessionNotice.value = ''
    if (!preserveUsername) lastUsername.value = ''
  }

  // 同一会话换令牌后，旧请求仍属于该会话；更换账号或退出后则丢弃
  function isCurrentSession(token: string): boolean {
    return isAuthenticated.value && knownEvent === currentEvent()
      && sessionId.value !== '' && readTokenSessionId(token) === sessionId.value
  }

  function expireSession(): void {
    // 休眠标签页的旧计时器不能结束其他标签页刚建立的新登录
    if (knownEvent !== currentEvent()) {
      synchronizeEvent(currentEvent())
      return
    }
    clearSession(true)
    publish('expired')
    sessionExpired.value = true
  }

  // 正常在访问令牌到期前一分钟续期；会话最后一分钟只等待固定截止时间
  function scheduleRefresh(retry: boolean = false): void {
    clearTimer()
    if (!isAuthenticated.value) return

    const sessionRemaining: number = Date.parse(sessionExpiresAtUtc.value) - Date.now()
    if (sessionRemaining <= 0) {
      expireSession()
      return
    }

    const tokenRemaining: number = Date.parse(expiresAtUtc.value) - Date.now()
    const wait: number = retry ? 30_000
      : sessionRemaining <= 60_000 ? sessionRemaining : tokenRemaining - 60_000

    refreshTimer = setTimeout(() => {
      refreshTimer = null
      if (Date.parse(sessionExpiresAtUtc.value) <= Date.now()) expireSession()
      else void refreshSession()
    }, Math.min(Math.max(wait, 1000), sessionRemaining, 2_147_483_647))
  }

  // 同一 sid 续期时直接更新令牌，不触发退出或更换账号的页面清理
  function setSession(token: AccessTokenResponse, user: CurrentUserResponse): void {
    if (!token.sessionId || !Number.isFinite(Date.parse(token.expiresAtUtc))
      || !Number.isFinite(Date.parse(token.sessionExpiresAtUtc))) {
      throw new Error('登录响应异常，请稍后重试')
    }
    if (Date.parse(token.expiresAtUtc) <= Date.now()) {
      throw new SessionUnauthorizedError('登录已过期，请重新登录')
    }

    if (sessionId.value !== token.sessionId) clearSession(true)
    sessionId.value = token.sessionId
    expiresAtUtc.value = token.expiresAtUtc
    sessionExpiresAtUtc.value = token.sessionExpiresAtUtc
    currentUser.value = user
    accessToken.value = token.accessToken
    lastUsername.value = user.username
    sessionExpired.value = false
    sessionNotice.value = ''
    scheduleRefresh()
  }

  // 本页并发请求共用一个 Promise；不同标签页通过浏览器锁依次刷新
  function refreshSession(): Promise<boolean> {
    if (refreshPromise !== null) return refreshPromise

    const version: number = generation
    const event: string = knownEvent
    isRestoring.value = true
    clearTimer()

    const pending: Promise<boolean> = withAuthLock(async () => {
      if (version !== generation || event !== currentEvent()) return false

      const token: AccessTokenResponse = await refreshAccessToken()
      const user: CurrentUserResponse = sessionId.value === token.sessionId && currentUser.value
        ? currentUser.value : await getCurrentUser(token.accessToken)

      if (version !== generation || event !== currentEvent()) return false
      setSession(token, user)
      return true
    }).catch((error: unknown) => {
      if (version !== generation || event !== currentEvent()) return false

      if (error instanceof SessionUnauthorizedError) {
        const hadLogin: boolean = isAuthenticated.value || event.startsWith('login:')
        clearSession(true)
        if (hadLogin) {
          publish('expired')
          sessionExpired.value = true
        }
      } else {
        // 网络、限流和服务异常不等于会话失效，保留原页面状态
        sessionNotice.value = error instanceof Error
          ? error.message : '暂时无法恢复登录，请稍后重试'
        scheduleRefresh(true)
      }
      return false
    }).finally(() => {
      if (refreshPromise === pending) {
        refreshPromise = null
        isRestoring.value = false
      }
    })

    refreshPromise = pending
    return pending
  }

  // 路由首次检查前调用一次；主动退出标记阻止旧 Cookie 恢复已退出的页面
  function initialize(): Promise<void> {
    if (initializePromise !== null) return initializePromise

    initializePromise = (async () => {
      if (!knownEvent || knownEvent.startsWith('login:')) await refreshSession()
      initialized.value = true
    })()
    return initializePromise
  }

  async function ensureAccessToken(force: boolean = false): Promise<string> {
    if (!isAuthenticated.value) throw new SessionUnauthorizedError('请先登录')
    if (Date.parse(sessionExpiresAtUtc.value) <= Date.now()) expireSession()
    if (!isAuthenticated.value) throw new SessionUnauthorizedError('登录已失效，请重新登录')

    if (force || Date.parse(expiresAtUtc.value) - Date.now() <= 60_000) {
      const refreshed: boolean = await refreshSession()
      if (!refreshed) {
        if (!isAuthenticated.value) throw new SessionUnauthorizedError('登录已失效，请重新登录')
        throw new Error(sessionNotice.value || '登录状态已变化，请重新操作')
      }
    }
    return accessToken.value
  }

  // 锁覆盖登录、查询用户、写入状态与通知，避免其他标签页夹入退出操作
  async function signIn(request: LoginRequest): Promise<boolean> {
    clearSession()
    const version: number = generation
    isSigningIn.value = true

    try {
      return await withAuthLock(async () => {
        if (version !== generation) return false
        const result = await login(request)
        publish('login')
        const event: string = knownEvent
        const user: CurrentUserResponse = await getCurrentUser(result.accessToken)
        if (version !== generation || event !== currentEvent()) return false
        setSession(result, user)
        return true
      })
    } finally {
      isSigningIn.value = false
    }
  }

  // 后端确认撤销后才清理本页并通知其他标签页；失败时可原地重试
  async function signOut(): Promise<boolean> {
    const version: number = generation
    const event: string = knownEvent
    isSigningOut.value = true

    try {
      return await withAuthLock(async () => {
        if (version !== generation || event !== currentEvent()) return false
        await logoutSession()
        clearSession()
        publish('logout')
        return true
      })
    } catch (error: unknown) {
      if (version === generation && event === currentEvent()) {
        sessionNotice.value = error instanceof Error ? error.message : '退出登录失败，请稍后重试'
      }
      return false
    } finally {
      isSigningOut.value = false
    }
  }

  // 改密由当前页面显示结果，Cookie 更新仍与登录、刷新、退出共用同一把锁
  async function changeAccountPassword(request: ChangePasswordRequest, token: string): Promise<void> {
    await ensureAccessToken()
    await withAuthLock(async () => {
      if (!isCurrentSession(token)) throw new Error('登录状态已变化，请重新操作')
      await changePassword(request, accessToken.value)
    })
  }

  // 改密成功或结果不确定时阻止自动恢复；不冒充后端退出成功
  async function endLocalSession(passwordChanged: boolean = false): Promise<boolean> {
    const version: number = generation
    const event: string = knownEvent
    return withAuthLock(async () => {
      if (version !== generation || event !== currentEvent()) return false
      clearSession(true)
      publish(passwordChanged ? 'password-changed' : 'logout')
      return true
    })
  }

  // storage 事件只传递事件类型与随机编号，不传递密码或访问令牌
  function synchronizeEvent(event: string): void {
    if (event === knownEvent || event !== currentEvent()) return
    knownEvent = event
    clearSession(true)
    // 新会话不能复用旧会话仍未返回的刷新 Promise
    refreshPromise = null
    isRestoring.value = false

    if (event.startsWith('login:')) void refreshSession()
    else {
      sessionExpired.value = event.startsWith('expired:') || event.startsWith('password-changed:')
      externalLogout.value += 1
    }
  }

  window.addEventListener('storage', event => {
    if (event.key === AUTH_EVENT_KEY) synchronizeEvent(event.newValue ?? '')
  })
  window.addEventListener('focus', () => {
    synchronizeEvent(currentEvent())
    if (isAuthenticated.value && Date.parse(expiresAtUtc.value) - Date.now() <= 60_000) {
      void refreshSession()
    }
  })

  return {
    accessToken, expiresAtUtc, sessionId, sessionExpiresAtUtc, currentUser, lastUsername,
    sessionExpired, sessionNotice, initialized, isSigningIn, isSigningOut, isRestoring,
    externalLogout, isAuthenticated, isAdmin, clearSession, setSession, isCurrentSession,
    initialize, refreshSession, ensureAccessToken, signIn, signOut, expireSession,
    changeAccountPassword, endLocalSession,
  }
})
