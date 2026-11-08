import { sessionFetch } from '@/api/http'
// 新增地址的请求数据，与后端 CreateUserAddressRequest 对应
export interface CreateUserAddressRequest {
  recipientName: string
  phone: string
  address: string
}

// 修改地址的请求数据，与后端 UpdateUserAddressRequest 对应
export interface UpdateUserAddressRequest {
  recipientName: string
  phone: string
  address: string
}

// 地址响应数据，与后端 UserAddressResponse 对应
export interface UserAddressResponse {
  id: number
  recipientName: string
  phone: string
  address: string

  // JSON 中的 UTC 时间是字符串
  createdAtUtc: string
}

// 后端可能返回的业务错误和参数校验错误
interface UserAddressErrorResponse {
  message?: string
  errors?: Record<string, string[]>
}

// 区分登录失效和普通业务错误，供页面分别处理
export class UserAddressUnauthorizedError extends Error {}

// 查询当前登录用户的收货地址
export async function getUserAddresses(accessToken: string): Promise<UserAddressResponse[]> {
  const response: Response = await sessionFetch('/api/user/addresses', {
    method: 'GET',
    headers: {
      Authorization: 'Bearer ' + accessToken,
    },
    cache: 'no-store',
  }).catch(() => {
    throw new Error('获取收货地址失败，请检查网络后重试')
  })

  if (response.status === 401) {
    throw new UserAddressUnauthorizedError('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new Error('没有权限查看收货地址')
  }

  if (response.status >= 500) {
    throw new Error('收货地址服务暂时不可用，请稍后重试')
  }

  if (!response.ok) {
    const errorBody: UserAddressErrorResponse | null = await response.json().catch(() => null)

    const validationMessage: string | undefined = Object.values(errorBody?.errors ?? {}).flat()[0]

    throw new Error(errorBody?.message ?? validationMessage ?? '获取收货地址失败，请稍后重试')
  }

  const addresses: UserAddressResponse[] = await response.json().catch(() => {
    throw new Error('收货地址列表响应异常，请重新加载')
  })

  return addresses
}

// 为当前登录用户新增收货地址
export async function createUserAddress(
  request: CreateUserAddressRequest,
  accessToken: string,
): Promise<UserAddressResponse> {
  const response: Response = await sessionFetch('/api/user/addresses', {
    method: 'POST',
    headers: {
      Authorization: 'Bearer ' + accessToken,
      'Content-Type': 'application/json',
    },

    // 只提交地址内容，所属用户由后端根据登录身份确定
    body: JSON.stringify({
      recipientName: request.recipientName.trim(),
      phone: request.phone.trim(),
      address: request.address.trim(),
    }),
  }).catch(() => {
    throw new Error('网络异常，暂时无法确认地址是否保存成功，请刷新地址列表核对')
  })

  if (response.status === 401) {
    throw new UserAddressUnauthorizedError('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new Error('没有权限新增收货地址')
  }

  if (response.status >= 500) {
    throw new Error('收货地址服务暂时不可用，请稍后重试')
  }

  if (!response.ok) {
    const errorBody: UserAddressErrorResponse | null = await response.json().catch(() => null)

    const validationMessage: string | undefined = Object.values(errorBody?.errors ?? {}).flat()[0]

    throw new Error(errorBody?.message ?? validationMessage ?? '新增收货地址失败，请稍后重试')
  }

  // 后端成功返回 201，正文包含新地址编号和保存后的内容
  const address: UserAddressResponse = await response.json().catch(() => {
    throw new Error('新增地址请求已处理，但无法读取返回结果，请刷新地址列表核对')
  })

  return address
}

// 修改当前登录用户的收货地址
export async function updateUserAddress(
  id: number,
  request: UpdateUserAddressRequest,
  accessToken: string,
): Promise<void> {
  const response: Response = await sessionFetch(`/api/user/addresses/${id}`, {
    method: 'PUT',
    headers: {
      Authorization: 'Bearer ' + accessToken,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({
      recipientName: request.recipientName.trim(),
      phone: request.phone.trim(),
      address: request.address.trim(),
    }),
  }).catch(() => {
    throw new Error('网络异常，暂时无法确认地址是否修改成功，请刷新地址列表核对')
  })

  if (response.status === 401) {
    throw new UserAddressUnauthorizedError('登录已失效，请重新登录')
  }
  if (response.status === 403) {
    throw new Error('没有权限修改收货地址')
  }
  if (response.status >= 500) {
    throw new Error('收货地址服务暂时不可用，请稍后重试')
  }
  if (!response.ok) {
    const errorBody: UserAddressErrorResponse | null = await response.json().catch(() => null)
    const validationMessage: string | undefined = Object.values(errorBody?.errors ?? {}).flat()[0]
    throw new Error(errorBody?.message ?? validationMessage ?? '修改收货地址失败，请稍后重试')
  }

  // 成功返回 204，没有 JSON 正文
}

// 删除当前登录用户的收货地址
export async function deleteUserAddress(
  id: number,
  accessToken: string,
): Promise<void> {
  const response: Response = await sessionFetch(`/api/user/addresses/${id}`, {
    method: 'DELETE',
    headers: {
      Authorization: 'Bearer ' + accessToken,
    },
  }).catch(() => {
    throw new Error('网络异常，暂时无法确认地址是否删除成功，请刷新地址列表核对')
  })

  if (response.status === 401) {
    throw new UserAddressUnauthorizedError('登录已失效，请重新登录')
  }
  if (response.status === 403) {
    throw new Error('没有权限删除收货地址')
  }

  // 删除成功或当前账号下该地址已经不存在，都可以移除页面卡片
  if (response.status === 204 || response.status === 404) {
    return
  }
  if (response.status >= 500) {
    throw new Error('收货地址服务暂时不可用，请稍后重试')
  }
  if (!response.ok) {
    const errorBody: UserAddressErrorResponse | null = await response.json().catch(() => null)
    const validationMessage: string | undefined = Object.values(errorBody?.errors ?? {}).flat()[0]
    throw new Error(errorBody?.message ?? validationMessage ?? '删除收货地址失败，请稍后重试')
  }
}
