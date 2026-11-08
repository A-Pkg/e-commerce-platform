import { sessionFetch } from '@/api/http'
// 加入购物车的请求数据
export interface AddCartItemRequest {
  skuId: number
  quantity: number
}

// 后端可能返回的错误信息
interface CartErrorResponse {
  message?: string
  errors?: Record<string, string[]>
}

// 区分登录失效和普通业务错误
export class CartUnauthorizedError extends Error {}

// 将选中的 SKU 加入当前登录用户的购物车
export async function addCartItem(
  request: AddCartItemRequest,
  accessToken: string,
): Promise<void> {
  const response: Response = await sessionFetch('/api/cart/items', {
    method: 'POST',
    headers: {
      Authorization: 'Bearer ' + accessToken,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
  }).catch(() => {
    throw new Error('网络异常，暂时无法确认是否加入成功')
  })

  if (response.status === 401) {
    throw new CartUnauthorizedError('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new Error('没有权限加入购物车')
  }

  if (response.status >= 500) {
    throw new Error('购物车服务暂时不可用，请稍后重试')
  }

  if (!response.ok) {
    const errorBody: CartErrorResponse | null =
      await response.json().catch(() => null)
    const validationMessage: string | undefined =
      Object.values(errorBody?.errors ?? {}).flat()[0]

    throw new Error(
      errorBody?.message ?? validationMessage ?? '加入购物车失败，请稍后重试',
    )
  }
}

// 购物车列表条目，与后端 CartItemResponse 对应
export interface CartItemResponse {
  id: number
  productId: number
  productName: string
  skuId: number
  skuCode: string
  specificationText: string
  price: number
  stock: number
  quantity: number
  isSelected: boolean
  isAvailable: boolean
  unavailableReason: string
}

// 查询当前登录用户的购物车
export async function getCartItems(
  accessToken: string,
): Promise<CartItemResponse[]> {
  const response: Response = await sessionFetch('/api/cart/items', {
    method: 'GET',
    headers: {
      Authorization: 'Bearer ' + accessToken,
    },
    cache: 'no-store',
  }).catch(() => {
    throw new Error('获取购物车失败，请检查网络后重试')
  })

  if (response.status === 401) {
    throw new CartUnauthorizedError('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new Error('没有权限查看购物车')
  }

  if (response.status >= 500) {
    throw new Error('购物车服务暂时不可用，请稍后重试')
  }

  if (!response.ok) {
    const errorBody: CartErrorResponse | null =
      await response.json().catch(() => null)
    const validationMessage: string | undefined =
      Object.values(errorBody?.errors ?? {}).flat()[0]

    throw new Error(
      errorBody?.message ?? validationMessage ?? '获取购物车失败，请稍后重试',
    )
  }

  const items: CartItemResponse[] = await response.json()

  return items
}

// 删除当前用户的购物车条目
export async function deleteCartItem(
  id: number,
  accessToken: string,
): Promise<void> {
  const response: Response = await sessionFetch(`/api/cart/items/${id}`, {
    method: 'DELETE',
    headers: {
      Authorization: 'Bearer ' + accessToken,
    },
  }).catch(() => {
    throw new Error('网络异常，暂时无法确认是否删除成功，请刷新列表检查')
  })

  if (response.status === 401) {
    throw new CartUnauthorizedError('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new Error('没有权限删除购物车条目')
  }

  // 204 表示删除成功；404 表示条目已经不存在
  if (response.status === 204 || response.status === 404) {
    return
  }

  if (response.status >= 500) {
    throw new Error('购物车服务暂时不可用，请稍后重试')
  }

  if (!response.ok) {
    const errorBody: CartErrorResponse | null =
      await response.json().catch(() => null)

    throw new Error(errorBody?.message ?? '删除失败，请稍后重试')
  }
}

// 修改购物车条目的最终数量
export async function updateCartItemQuantity(
  id: number,
  quantity: number,
  accessToken: string,
): Promise<void> {
  const response: Response = await sessionFetch(
    `/api/cart/items/${id}/quantity`,
    {
      method: 'PUT',
      headers: {
        Authorization: 'Bearer ' + accessToken,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ quantity }),
    },
  ).catch(() => {
    throw new Error('网络异常，暂时无法确认是否保存成功，请刷新列表检查')
  })

  if (response.status === 401) {
    throw new CartUnauthorizedError('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new Error('没有权限修改购物车数量')
  }

  if (response.status >= 500) {
    throw new Error('购物车服务暂时不可用，请稍后重试')
  }

  if (!response.ok) {
    const errorBody: CartErrorResponse | null =
      await response.json().catch(() => null)
    const validationMessage: string | undefined =
      Object.values(errorBody?.errors ?? {}).flat()[0]

    throw new Error(
      errorBody?.message ?? validationMessage ?? '修改数量失败，请稍后重试',
    )
  }
}

// 设置当前用户购物车条目的最终勾选状态
export async function updateCartItemSelection(
  id: number,
  isSelected: boolean,
  accessToken: string,
): Promise<void> {
  const response: Response = await sessionFetch(
    `/api/cart/items/${id}/selection`,
    {
      method: 'PUT',
      headers: {
        Authorization: 'Bearer ' + accessToken,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ isSelected }),
    },
  ).catch(() => {
    throw new Error('网络异常，暂时无法确认勾选状态是否保存，请刷新列表检查')
  })

  if (response.status === 401) {
    throw new CartUnauthorizedError('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new Error('没有权限修改购物车勾选状态')
  }

  if (response.status >= 500) {
    throw new Error('购物车服务暂时不可用，请稍后重试')
  }

  if (!response.ok) {
    const errorBody: CartErrorResponse | null =
      await response.json().catch(() => null)
    const validationMessage: string | undefined =
      Object.values(errorBody?.errors ?? {}).flat()[0]

    throw new Error(
      errorBody?.message ??
        validationMessage ??
        '修改勾选状态失败，请稍后重试',
    )
  }

  // 成功返回 204，没有 JSON 正文
}

// 批量设置当前用户购物车条目的最终勾选状态
export async function updateCartItemsSelection(
  cartItemIds: number[],
  isSelected: boolean,
  accessToken: string,
): Promise<void> {
  const response: Response = await sessionFetch('/api/cart/items/selection', {
    method: 'PUT',
    headers: {
      Authorization: 'Bearer ' + accessToken,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ cartItemIds, isSelected }),
  }).catch(() => {
    throw new Error('网络异常，暂时无法确认勾选状态是否保存，请刷新列表检查')
  })

  if (response.status === 401) {
    throw new CartUnauthorizedError('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new Error('没有权限修改购物车勾选状态')
  }

  if (response.status >= 500) {
    throw new Error('购物车服务暂时不可用，请稍后重试')
  }

  if (!response.ok) {
    const errorBody: CartErrorResponse | null =
      await response.json().catch(() => null)
    const validationMessage: string | undefined =
      Object.values(errorBody?.errors ?? {}).flat()[0]

    throw new Error(
      errorBody?.message ??
        validationMessage ??
        '批量修改勾选状态失败，请稍后重试',
    )
  }

  // 成功返回 204，没有 JSON 正文
}
