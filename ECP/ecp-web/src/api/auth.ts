// 登录请求数据
export interface LoginRequest {
  username: string
  password: string
}

// 登录和刷新接口共用的访问令牌信息
export interface AccessTokenResponse {
  accessToken: string
  tokenType: string

  // 当前访问令牌的过期时间
  expiresAtUtc: string

  // 同一次登录保持不变，续期时不会更换
  sessionId: string

  // 整个登录会话的固定截止时间，续期不会延长
  sessionExpiresAtUtc: string
}

// 登录成功后，在令牌信息之外返回账号基本信息
export interface LoginResponse extends AccessTokenResponse {
  message: string
  userId: number
  username: string
}

// 账号接口可能返回的业务错误或参数校验错误
interface AuthErrorResponse {
  message?: string
  errors?: Record<string, string[]>
}

// 获取防伪请求令牌；浏览器会同时保存后端设置的防伪 Cookie
export async function getCsrfToken(): Promise<string> {
  const response: Response = await fetch('/api/auth/csrf', {
    credentials: 'same-origin',
    cache: 'no-store',
  })

  if (!response.ok) {
    throw new Error('获取登录校验信息失败，请稍后重试')
  }

  const result: { requestToken: string } = await response.json()
  return result.requestToken
}

// 获取防伪校验值后提交登录，失败信息交给登录页面显示
export async function login(request: LoginRequest): Promise<LoginResponse> {
  // 先获取校验值，获取失败时不会继续提交账号密码
  const csrfToken: string = await getCsrfToken()

  // 浏览器自动发送防伪 Cookie，请求头发送与其配对的校验值
  const response: Response = await fetch('/api/auth/login', {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      'X-CSRF-TOKEN': csrfToken,
    },
    body: JSON.stringify(request),
    cache: 'no-store',
  })

  // 服务异常时，使用统一提示
  if (response.status >= 500) {
    throw new Error('登录服务暂时不可用，请稍后重试')
  }

  // 处理账号密码错误、参数不合法等情况
  if (!response.ok) {
    // 兼容错误响应没有 JSON 正文的情况
    const errorBody: AuthErrorResponse | null = await response.json().catch(() => null)

    // 提取后端参数校验错误中的第一条提示
    const validationMessage: string | undefined = Object.values(errorBody?.errors ?? {}).flat()[0]

    throw new Error(errorBody?.message ?? validationMessage ?? '登录失败，请稍后重试')
  }

  // 读取登录结果，时间字段在 JSON 中是字符串
  const result: LoginResponse = await response.json()

  return result
}

// 刷新接口明确返回 401 时，表示当前登录会话已经失效
export class SessionUnauthorizedError extends Error {}

// 使用浏览器保存的刷新 Cookie，换取新的访问令牌
export async function refreshAccessToken(): Promise<AccessTokenResponse> {
  const csrfToken: string = await getCsrfToken()

  // 不携带 Authorization，避免依赖已经过期的访问令牌
  const response: Response = await fetch('/api/auth/refresh', {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
      'X-CSRF-TOKEN': csrfToken,
    },
    cache: 'no-store',
  })

  if (response.status === 401) {
    throw new SessionUnauthorizedError('登录已失效，请重新登录')
  }

  if (!response.ok) {
    const errorBody: AuthErrorResponse | null = await response.json().catch(() => null)
    throw new Error(errorBody?.message ?? '登录续期失败，请稍后重试')
  }

  const result: AccessTokenResponse = await response.json()
  return result
}

// 请求后端撤销当前浏览器的会话，并由后端删除刷新 Cookie
export async function logoutSession(): Promise<void> {
  const csrfToken: string = await getCsrfToken()

  // 退出接口同样不依赖访问令牌，令牌过期后也可以主动退出
  const response: Response = await fetch('/api/auth/logout', {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
      'X-CSRF-TOKEN': csrfToken,
    },
    cache: 'no-store',
  })

  if (!response.ok) {
    const errorBody: AuthErrorResponse | null = await response.json().catch(() => null)
    throw new Error(errorBody?.message ?? '退出登录失败，请稍后重试')
  }

  // 后端成功返回 204，没有正文，不调用 response.json()
}

// 当前用户接口返回的数据
export interface CurrentUserResponse {
  userId: number
  username: string

  // 用户角色：User（普通用户）、Admin（管理员）
  role: 'User' | 'Admin'
}

// 携带访问令牌，获取当前登录用户
export async function getCurrentUser(accessToken: string): Promise<CurrentUserResponse> {
  // 将令牌放入 Authorization 请求头，不通过请求参数传递用户编号
  const response: Response = await fetch('/api/auth/me', {
    method: 'GET',
    headers: {
      Authorization: `Bearer ${accessToken}`,
    },
    cache: 'no-store',
  })

  // 令牌无效或过期时，提示重新登录
  if (response.status === 401) {
    throw new SessionUnauthorizedError('登录已失效，请重新登录')
  }

  // 单独处理权限不足的情况
  if (response.status === 403) {
    throw new Error('没有权限获取当前用户信息')
  }

  // 处理其他失败情况
  if (!response.ok) {
    throw new Error('获取用户信息失败，请稍后重试')
  }

  // 读取后端返回的当前用户信息
  const result: CurrentUserResponse = await response.json()

  return result
}

// 注册请求数据，与后端 RegisterRequest 对应
export interface RegisterRequest {
  username: string
  password: string
  confirmPassword: string
}

// 注册成功后，后端返回的数据
export interface RegisterResponse {
  message: string
  userId: number
  username: string
}

// 调用注册接口，失败时抛出错误供页面处理
export async function register(request: RegisterRequest): Promise<RegisterResponse> {
  // 通过开发代理发送注册请求
  const response: Response = await fetch('/api/auth/register', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
  })

  // 服务异常时，使用统一提示
  if (response.status >= 500) {
    throw new Error('注册服务暂时不可用，请稍后重试')
  }

  // 处理账号重复、参数不合法等情况
  if (!response.ok) {
    // 读取后端错误信息，兼容没有 JSON 正文的情况
    const errorBody: AuthErrorResponse | null = await response.json().catch(() => null)

    // 提取参数校验错误中的第一条提示
    const validationMessage: string | undefined = Object.values(errorBody?.errors ?? {}).flat()[0]

    throw new Error(errorBody?.message ?? validationMessage ?? '注册失败，请稍后重试')
  }

  // 读取注册成功的用户编号和账号
  const result: RegisterResponse = await response.json()

  return result
}

// 修改密码请求，与后端 ChangePasswordRequest 对应
export interface ChangePasswordRequest {
  currentPassword: string
  newPassword: string
  confirmNewPassword: string
}

// 登录失效
export class PasswordUnauthorizedError extends Error {}

// 没有修改密码权限
export class PasswordForbiddenError extends Error {}

// 账号或密码已被其他请求修改
export class PasswordConflictError extends Error {}

// 网络或服务端异常，无法确定密码是否已经修改
export class PasswordChangeUncertainError extends Error {}

// 修改当前普通用户的密码
export async function changePassword(
  request: ChangePasswordRequest,
  accessToken: string,
): Promise<void> {
  const response: Response = await fetch('/api/auth/password', {
    method: 'PUT',
    headers: {
      Authorization: 'Bearer ' + accessToken,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
    cache: 'no-store',
  }).catch(() => {
    throw new PasswordChangeUncertainError('网络异常，暂时无法确认密码修改结果')
  })

  if (response.status === 401) {
    throw new PasswordUnauthorizedError('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new PasswordForbiddenError('只有普通用户可以使用此功能')
  }

  // 成功响应没有正文，不解析 JSON
  if (response.status === 204) {
    return
  }

  if (response.status >= 500) {
    throw new PasswordChangeUncertainError('服务响应异常，暂时无法确认密码修改结果')
  }

  const body: AuthErrorResponse | null = await response.json().catch(() => null)
  const validationMessage: string | undefined = Object.values(body?.errors ?? {}).flat()[0]
  const message: string = body?.message ?? validationMessage ?? '修改密码失败，请稍后重试'

  if (response.status === 409) {
    throw new PasswordConflictError(message)
  }

  throw new Error(message)
}
