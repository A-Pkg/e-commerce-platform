import { sessionFetch } from '@/api/http'
import type { OrderDetailResponse, OrderStatus } from '@/api/orders'
import type { PagedResponse } from '@/types/pagination'

// 管理员订单查询条件，空字符串表示不筛选
export interface AdminOrderFilters {
  keyword: string
  status: '' | OrderStatus
}

// 管理员订单列表，与后端列表 DTO 对应
export interface AdminOrderListItemResponse {
  id: number
  orderNo: string
  username: string
  status: OrderStatus
  totalQuantity: number
  payableAmount: number
  createdAtUtc: string
}

// 管理员详情，在现有订单详情上补充账号和履约时间
export interface AdminOrderDetailResponse extends OrderDetailResponse {
  username: string
  remark: string | null
  shippedAtUtc: string | null
  completedAtUtc: string | null
}

// 后端返回的业务或参数校验错误
interface AdminOrderErrorResponse {
  message?: string
  errors?: Record<string, string[]>
}

// 登录失效，由页面清理会话
export class AdminOrderUnauthorizedError extends Error {}

// 无管理员权限，由页面清理已显示的订单数据
export class AdminOrderForbiddenError extends Error {}

// 两个只读接口共用请求和错误处理
async function requestAdminOrders<T>(
  url: string,
  accessToken: string,
  fallbackMessage: string,
): Promise<T> {
  const response: Response = await sessionFetch(url, {
    method: 'GET',
    headers: {
      Authorization: 'Bearer ' + accessToken,
    },
    cache: 'no-store',
  }).catch(() => {
    throw new Error('请求后台订单失败，请检查网络后重试')
  })

  if (response.status === 401) {
    throw new AdminOrderUnauthorizedError('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new AdminOrderForbiddenError('没有管理员订单查询权限')
  }

  if (!response.ok) {
    const body: AdminOrderErrorResponse | null =
      await response.json().catch(() => null)

    const validationMessage: string | undefined =
      Object.values(body?.errors ?? {}).flat()[0]

    throw new Error(body?.message ?? validationMessage ?? fallbackMessage)
  }

  const result: T = await response.json().catch(() => {
    throw new Error('后台订单响应异常，请重新加载')
  })

  return result
}

// 按筛选条件和页码查询订单，使用共用分页响应
export function getAdminOrders(
  accessToken: string,
  filters?: AdminOrderFilters,
  pageNumber: number = 1,
): Promise<PagedResponse<AdminOrderListItemResponse>> {
  const parameters = new URLSearchParams()
  const keyword: string = filters?.keyword.trim() ?? ''
  const status: string = filters?.status ?? ''

  if (keyword !== '') {
    parameters.set('keyword', keyword)
  }

  if (status !== '') {
    parameters.set('status', status)
  }

  parameters.set('pageNumber', String(pageNumber))

  const queryString: string = parameters.toString()
  const url: string = '/api/admin/orders' + (queryString ? '?' + queryString : '')

  return requestAdminOrders<PagedResponse<AdminOrderListItemResponse>>(
    url,
    accessToken,
    '获取后台订单列表失败，请稍后重试',
  )
}

// 查询指定订单的完整详情
export function getAdminOrderDetail(
  id: number,
  accessToken: string,
): Promise<AdminOrderDetailResponse> {
  return requestAdminOrders<AdminOrderDetailResponse>(
    '/api/admin/orders/' + id,
    accessToken,
    '获取后台订单详情失败，请稍后重试',
  )
}

// 前端根据发货接口的状态码整理操作结果
export interface AdminOrderShipResult {
  outcome: 'shipped' | 'conflict'
  message: string
}

// 管理员直接发货，不提交物流信息或请求正文
export async function shipAdminOrder(
  id: number,
  accessToken: string,
): Promise<AdminOrderShipResult> {
  const response: Response = await sessionFetch('/api/admin/orders/' + id + '/ship', {
    method: 'POST',
    headers: {
      Authorization: 'Bearer ' + accessToken,
    },
    cache: 'no-store',
  }).catch(() => {
    throw new Error('网络异常，暂时无法确认发货结果，请刷新订单列表后确认')
  })

  if (response.status === 401) {
    throw new AdminOrderUnauthorizedError('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new AdminOrderForbiddenError('没有管理员订单操作权限')
  }

  // 204 没有正文，直接确认本次发货成功
  if (response.status === 204) {
    return {
      outcome: 'shipped',
      message: '发货成功',
    }
  }

  const body: AdminOrderErrorResponse | null =
    await response.json().catch(() => null)

  const validationMessage: string | undefined =
    Object.values(body?.errors ?? {}).flat()[0]

  const message: string =
    body?.message ??
    validationMessage ??
    '暂时无法确认发货结果，请刷新订单列表后确认'

  // 状态冲突时重新查询列表，可能已被其他请求完成发货
  if (response.status === 409) {
    return {
      outcome: 'conflict',
      message,
    }
  }

  throw new Error(message)
}
