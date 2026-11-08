import { useAuthStore } from '@/stores/auth'

// 公开请求直接发送；本人及后台请求先取得当前会话的可用访问令牌
export async function sessionFetch(input: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers)
  const authorization: string | null = headers.get('Authorization')
  if (!authorization) return fetch(input, init)

  const authStore = useAuthStore()
  const requestedToken: string = authorization.slice('Bearer '.length)
  if (!authStore.isCurrentSession(requestedToken)) throw new Error('登录状态已变化，请重新操作')

  let token: string = await authStore.ensureAccessToken()
  if (!authStore.isCurrentSession(requestedToken)) throw new Error('登录状态已变化，请重新操作')
  headers.set('Authorization', 'Bearer ' + token)
  let response: Response = await fetch(input, { ...init, headers })

  // 仅重试认证阶段明确报告的令牌过期，网络异常和业务错误不自动重发
  if (response.status === 401 && authStore.isCurrentSession(requestedToken)) {
    const body: { code?: string } | null = await response.clone().json().catch(() => null)
    if (body?.code === 'access_token_expired') {
      token = await authStore.ensureAccessToken(token === authStore.accessToken)
      if (!authStore.isCurrentSession(requestedToken)) throw new Error('登录状态已变化，请重新操作')
      headers.set('Authorization', 'Bearer ' + token)
      response = await fetch(input, { ...init, headers })
    }
  }

  if (response.status === 401 && authStore.isCurrentSession(requestedToken)) {
    authStore.expireSession()
  }
  return response
}
