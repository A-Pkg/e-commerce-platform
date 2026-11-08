import { sessionFetch } from '@/api/http'
import type { PagedResponse } from '@/types/pagination'

// 沿用后端已有的两个用户角色
export type AdminUserRole = 'User' | 'Admin'

// 用户列表和详情共用的基本信息，与后端 AdminUserResponse 对应
export interface AdminUserResponse {
  id: number
  username: string
  role: AdminUserRole
  isActive: boolean
  createdAtUtc: string
}

// 已提交的查询条件，空字符串表示不筛选
export interface AdminUserFilters {
  username: string
  role: '' | AdminUserRole
}

// 后端返回的业务或参数校验错误
interface AdminUserErrorResponse {
  message?: string
  errors?: Record<string, string[]>
}

// 登录失效，由页面清理会话
export class AdminUserUnauthorizedError extends Error {}

// 无管理员权限，由页面清理已显示的用户数据
export class AdminUserForbiddenError extends Error {}

// 列表和详情共用只读请求及错误处理
async function requestAdminUsers<T>(
  url: string,
  accessToken: string,
  fallbackMessage: string,
): Promise<T> {
  const response: Response = await sessionFetch(url, {
    method: 'GET',
    headers: { Authorization: 'Bearer ' + accessToken },
    cache: 'no-store',
  }).catch(() => {
    throw new Error('请求后台用户失败，请检查网络后重试')
  })

  if (response.status === 401) {
    throw new AdminUserUnauthorizedError('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new AdminUserForbiddenError('没有管理员用户查询权限')
  }

  if (!response.ok) {
    const body: AdminUserErrorResponse | null = await response.json().catch(() => null)
    const validationMessage: string | undefined = Object.values(body?.errors ?? {}).flat()[0]
    throw new Error(body?.message ?? validationMessage ?? fallbackMessage)
  }

  const result: T = await response.json().catch(() => {
    throw new Error('后台用户响应异常，请重新加载')
  })

  return result
}

// 按账号、角色和页码查询用户，使用共用分页响应
export function getAdminUsers(
  accessToken: string,
  filters?: AdminUserFilters,
  pageNumber: number = 1,
): Promise<PagedResponse<AdminUserResponse>> {
  const parameters = new URLSearchParams()
  const username: string = filters?.username.trim() ?? ''

  if (username !== '') {
    parameters.set('username', username)
  }

  if (filters?.role) {
    parameters.set('role', filters.role)
  }

  parameters.set('pageNumber', String(pageNumber))

  return requestAdminUsers<PagedResponse<AdminUserResponse>>(
    '/api/admin/users?' + parameters.toString(),
    accessToken,
    '获取后台用户列表失败，请稍后重试',
  )
}

// 打开详情时重新查询指定用户的基本信息
export function getAdminUserDetail(id: number, accessToken: string): Promise<AdminUserResponse> {
  return requestAdminUsers<AdminUserResponse>(
    '/api/admin/users/' + id,
    accessToken,
    '获取后台用户详情失败，请稍后重试',
  )
}


// 根据后端状态码整理停用结果
export interface AdminUserDisableResult {
  outcome: 'disabled' | 'conflict'
  message: string
}

// 停用普通用户，不提交请求正文
export async function disableAdminUser(
  id: number,
  accessToken: string,
): Promise<AdminUserDisableResult> {
  const response: Response = await sessionFetch('/api/admin/users/' + id + '/disable', {
    method: 'POST',
    headers: { Authorization: 'Bearer ' + accessToken },
    cache: 'no-store',
  }).catch(() => {
    throw new Error('网络异常，暂时无法确认停用结果，请刷新用户列表后确认')
  })

  if (response.status === 401) {
    throw new AdminUserUnauthorizedError('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new AdminUserForbiddenError('没有管理员用户操作权限')
  }

  // 204 没有正文，不解析 JSON
  if (response.status === 204) {
    return { outcome: 'disabled', message: '账号已停用' }
  }

  const body: AdminUserErrorResponse | null = await response.json().catch(() => null)
  const validationMessage: string | undefined = Object.values(body?.errors ?? {}).flat()[0]
  const message: string =
    body?.message ?? validationMessage ?? '停用用户失败，请刷新用户列表后重试'

  if (response.status === 409) {
    return { outcome: 'conflict', message }
  }

  throw new Error(message)
}
