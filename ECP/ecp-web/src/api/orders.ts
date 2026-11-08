import { sessionFetch } from '@/api/http'
// 订单状态，与后端 OrderStatuses 对应
export type OrderStatus = 'PendingPayment' | 'Paid' | 'Shipped' | 'Completed' | 'Cancelled'

// 提交订单时携带的商品确认信息
export interface CreateOrderItemRequest {
  cartItemId: number
  quantity: number
  unitPrice: number
}

// 创建订单的请求数据，不提交用户编号或应付总额
export interface CreateOrderRequest {
  // 同一次下单重试时，必须复用该标识及原始请求内容
  requestId: string
  addressId: number
  items: CreateOrderItemRequest[]
}

// 创建订单或重复提交时返回的订单基本信息
export interface CreateOrderResponse {
  id: number
  orderNo: string
  status: OrderStatus
  itemsAmount: number
  shippingFee: number
  payableAmount: number
  createdAtUtc: string
  expiresAtUtc: string
}

// 订单商品快照，不包含图片
export interface OrderItemResponse {
  id: number
  productId: number
  productName: string
  skuId: number
  skuCode: string
  specificationText: string
  unitPrice: number
  quantity: number
  subtotal: number
}

// 本人订单详情，在订单基本信息上增加收货快照和商品明细
export interface OrderDetailResponse extends CreateOrderResponse {
  recipientName: string
  recipientPhone: string
  shippingAddress: string
  paidAtUtc: string | null
  shippedAtUtc: string | null
  completedAtUtc: string | null
  cancelledAtUtc: string | null
  items: OrderItemResponse[]
}

// 后端可能返回的业务错误和参数校验错误
interface OrderErrorResponse {
  message?: string
  errors?: Record<string, string[]>
}

// 登录失效，由页面统一清理会话
export class OrderUnauthorizedError extends Error {}

// 请求可能已经成功，页面应保留原始请求，并使用同一个请求标识重试
export class OrderSubmissionUncertainError extends Error {}

// 创建和查询订单共用的错误文案解析
async function readOrderErrorMessage(response: Response, fallbackMessage: string): Promise<string> {
  const errorBody: OrderErrorResponse | null = await response.json().catch(() => null)

  const validationMessage: string | undefined = Object.values(errorBody?.errors ?? {}).flat()[0]

  return errorBody?.message ?? validationMessage ?? fallbackMessage
}

// 根据选中的购物车条目和收货地址创建订单
export async function createOrder(
  request: CreateOrderRequest,
  accessToken: string,
): Promise<CreateOrderResponse> {
  const response: Response = await sessionFetch('/api/orders', {
    method: 'POST',
    headers: {
      Authorization: 'Bearer ' + accessToken,
      'Content-Type': 'application/json',
    },

    // 请求数据由页面生成；重试时传入保存的原始请求
    body: JSON.stringify(request),
  }).catch(() => {
    throw new OrderSubmissionUncertainError(
      '网络异常，暂时无法确认订单是否创建成功，请使用原请求重试',
    )
  })

  if (response.status === 401) {
    throw new OrderUnauthorizedError('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new Error('没有权限创建订单')
  }

  // 服务端异常时，不能仅凭响应断定订单没有创建
  if (response.status >= 500) {
    throw new OrderSubmissionUncertainError(
      '订单服务响应异常，暂时无法确认下单结果，请使用原请求重试',
    )
  }

  if (!response.ok) {
    const message: string = await readOrderErrorMessage(
      response,
      '创建订单失败，请检查商品和收货地址后重试',
    )

    throw new Error(message)
  }

  // 新订单返回 201；相同请求重复提交时，后端返回 200
  const order: CreateOrderResponse = await response.json().catch(() => {
    throw new OrderSubmissionUncertainError('订单响应解析失败，订单可能已经创建，请使用原请求重试')
  })

  return order
}

// 查询当前登录用户的订单详情
export async function getOrderDetail(
  id: number,
  accessToken: string,
): Promise<OrderDetailResponse> {
  const response: Response = await sessionFetch('/api/orders/' + id, {
    method: 'GET',
    headers: {
      Authorization: 'Bearer ' + accessToken,
    },
    cache: 'no-store',
  }).catch(() => {
    throw new Error('获取订单详情失败，请检查网络后重试')
  })

  if (response.status === 401) {
    throw new OrderUnauthorizedError('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new Error('没有权限查看订单详情')
  }

  if (response.status >= 500) {
    throw new Error('订单服务暂时不可用，请稍后重试')
  }

  if (!response.ok) {
    const message: string = await readOrderErrorMessage(response, '获取订单详情失败，请稍后重试')

    throw new Error(message)
  }

  const order: OrderDetailResponse = await response.json().catch(() => {
    throw new Error('订单详情响应异常，请重新加载')
  })

  return order
}

// 支付记录状态，与后端 PaymentStatuses 对应
export type PaymentStatus = 'Pending' | 'Succeeded' | 'Failed' | 'Closed'

// 付款准备和查询共用的支付结果
interface OrderPaymentResponse {
  orderId: number
  orderNo: string
  orderStatus: OrderStatus
  paymentNo: string
  provider: 'Alipay' | 'Local' | 'Mock'
  paymentStatus: PaymentStatus
  amount: number
  paidAtUtc: string | null
}

// 付款准备结果；已付款或零元订单没有收银台表单
export interface PrepareOrderPaymentResponse extends OrderPaymentResponse {
  expiresAtUtc: string
  paymentForm: string | null
}

// 查询成功只代表取得结果，是否付款成功由 paymentStatus 判断
export interface QueryOrderPaymentResponse extends OrderPaymentResponse {
  message: string
}

// 两个支付接口共用身份、业务错误和响应解析处理
async function requestOrderPayment<T>(
  id: number,
  action: 'prepare' | 'query',
  accessToken: string,
): Promise<T> {
  const response: Response = await sessionFetch('/api/orders/' + id + '/payments/' + action, {
    method: 'POST',
    headers: {
      Authorization: 'Bearer ' + accessToken,
    },
    cache: 'no-store',
  }).catch(() => {
    throw new Error('网络异常，暂时无法确认付款结果，请稍后查询支付结果')
  })

  if (response.status === 401) {
    throw new OrderUnauthorizedError('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new Error('没有权限操作该订单的付款')
  }

  if (!response.ok) {
    const message: string = await readOrderErrorMessage(
      response,
      '暂时无法确认付款结果，请稍后查询支付结果',
    )

    throw new Error(message)
  }

  const result: T = await response.json().catch(() => {
    throw new Error('支付响应异常，暂时无法确认付款结果，请稍后查询支付结果')
  })

  return result
}

// 准备本人订单付款，不向后端提交金额或用户编号
export function prepareOrderPayment(
  id: number,
  accessToken: string,
): Promise<PrepareOrderPaymentResponse> {
  return requestOrderPayment<PrepareOrderPaymentResponse>(id, 'prepare', accessToken)
}

// 主动向后端查询并同步本人订单的付款结果
export function queryOrderPayment(
  id: number,
  accessToken: string,
): Promise<QueryOrderPaymentResponse> {
  return requestOrderPayment<QueryOrderPaymentResponse>(id, 'query', accessToken)
}

// 本人订单列表中的一条订单，与后端列表 DTO 对应
export interface OrderListItemResponse {
  id: number
  orderNo: string
  status: OrderStatus
  payableAmount: number
  createdAtUtc: string
  expiresAtUtc: string
  items: OrderItemResponse[]
}

// 查询当前登录用户的订单列表
export async function getOrders(accessToken: string): Promise<OrderListItemResponse[]> {
  const response: Response = await sessionFetch('/api/orders', {
    method: 'GET',
    headers: {
      Authorization: 'Bearer ' + accessToken,
    },
    cache: 'no-store',
  }).catch(() => {
    throw new Error('获取订单列表失败，请检查网络后重试')
  })

  if (response.status === 401) {
    throw new OrderUnauthorizedError('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new Error('没有权限查看订单列表')
  }

  if (response.status >= 500) {
    throw new Error('订单服务暂时不可用，请稍后重试')
  }

  if (!response.ok) {
    const message: string = await readOrderErrorMessage(response, '获取订单列表失败，请稍后重试')
    throw new Error(message)
  }

  const orders: OrderListItemResponse[] = await response.json().catch(() => {
    throw new Error('订单列表响应异常，请重新加载')
  })

  return orders
}

// 取消结果；只有 cancelled 表示后端已确认取消
export interface CancelOrderResponse {
  outcome: 'cancelled' | 'pending' | 'conflict'
  message: string
}

// 取消本人待支付订单，不向后端提交用户编号
export async function cancelOrder(
  id: number,
  accessToken: string,
): Promise<CancelOrderResponse> {
  const response: Response = await sessionFetch('/api/orders/' + id + '/cancel', {
    method: 'POST',
    headers: {
      Authorization: 'Bearer ' + accessToken,
    },
    cache: 'no-store',
  }).catch(() => {
    throw new Error('网络异常，暂时无法确认取消结果，请刷新订单后重试')
  })

  if (response.status === 401) {
    throw new OrderUnauthorizedError('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new Error('没有权限取消该订单')
  }

  // 204 没有响应正文，不能调用 response.json()
  if (response.status === 204) {
    return {
      outcome: 'cancelled',
      message: '订单已取消',
    }
  }

  // 202 表示尚未完成取消，不能当作取消成功
  if (response.status === 202) {
    return {
      outcome: 'pending',
      message: await readOrderErrorMessage(
        response,
        '订单尚未取消，请稍后重试；超过付款截止时间后，系统会继续检查处理',
      ),
    }
  }

  // 可能已付款或出现状态冲突，页面收到后重新查询订单
  if (response.status === 409) {
    return {
      outcome: 'conflict',
      message: await readOrderErrorMessage(
        response,
        '当前暂时无法取消，请刷新订单后重试',
      ),
    }
  }

  const message: string = await readOrderErrorMessage(
    response,
    '暂时无法确认取消结果，请刷新订单后重试',
  )

  throw new Error(message)
}

// 确认收货结果；只有 completed 表示本次请求已确认完成
export interface ConfirmReceiptResult {
  outcome: 'completed' | 'conflict'
  message: string
}

// 确认本人订单收货，不提交用户编号或请求正文
export async function confirmOrderReceipt(
  id: number,
  accessToken: string,
): Promise<ConfirmReceiptResult> {
  const response: Response = await sessionFetch('/api/orders/' + id + '/confirm-receipt', {
    method: 'POST',
    headers: {
      Authorization: 'Bearer ' + accessToken,
    },
    cache: 'no-store',
  }).catch(() => {
    throw new Error('网络异常，暂时无法确认收货结果，请刷新订单后确认')
  })

  if (response.status === 401) {
    throw new OrderUnauthorizedError('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new Error('没有权限确认该订单收货')
  }

  // 204 没有响应正文，直接确认本次操作成功
  if (response.status === 204) {
    return {
      outcome: 'completed',
      message: '已确认收货，订单已完成',
    }
  }

  const message: string = await readOrderErrorMessage(
    response,
    '暂时无法确认收货结果，请刷新订单后确认',
  )

  // 订单可能已由其他请求完成，页面重新查询实际状态
  if (response.status === 409) {
    return { outcome: 'conflict', message }
  }

  throw new Error(message)
}
