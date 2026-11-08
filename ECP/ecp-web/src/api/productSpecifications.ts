import { sessionFetch } from '@/api/http'
// 单个规格值，与后端 ProductSpecificationValueResponse 对应
export interface ProductSpecificationValueResponse {
  // 规格值编号
  id: number

  // 所属规格编号
  specificationId: number

  // 规格值内容
  value: string

  // 展示顺序
  sortOrder: number

  // 创建时间，后端 UTC 时间在 JSON 中是字符串
  createdAtUtc: string
}

// 商品规格，与后端 ProductSpecificationResponse 对应
export interface ProductSpecificationResponse {
  // 规格编号
  id: number

  // 所属商品编号
  productId: number

  // 规格名称
  name: string

  // 展示顺序
  sortOrder: number

  // 创建时间
  createdAtUtc: string

  // 当前规格下的规格值，没有规格值时为空数组
  values: ProductSpecificationValueResponse[]
}

// 新增规格的请求
export interface CreateProductSpecificationRequest {
  // 规格名称
  name: string

  // 展示顺序
  sortOrder: number
}

// 新增规格值的请求
export interface CreateProductSpecificationValueRequest {
  // 规格值内容
  value: string

  // 展示顺序
  sortOrder: number
}

// 后端可能返回的业务错误和参数校验错误
interface SpecificationErrorResponse {
  message?: string
  errors?: Record<string, string[]>
}

// 查询指定商品的规格及其规格值
export async function getAdminProductSpecifications(
  productId: number,
  accessToken: string,
): Promise<ProductSpecificationResponse[]> {
  // 每次查询都重新读取后台数据
  const response: Response = await sessionFetch('/api/admin/products/' + productId + '/specifications', {
    method: 'GET',
    headers: {
      Authorization: 'Bearer ' + accessToken,
    },
    cache: 'no-store',
  })

  return readSpecificationResponse<ProductSpecificationResponse[]>(
    response,
    '获取商品规格失败，请稍后重试',
  )
}

// 为指定商品新增规格
export async function createAdminProductSpecification(
  productId: number,
  request: CreateProductSpecificationRequest,
  accessToken: string,
): Promise<ProductSpecificationResponse> {
  // 商品编号来自接口路径，请求正文只提交名称和排序
  const response: Response = await sessionFetch('/api/admin/products/' + productId + '/specifications', {
    method: 'POST',
    headers: {
      Authorization: 'Bearer ' + accessToken,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
  })

  return readSpecificationResponse<ProductSpecificationResponse>(
    response,
    '新增商品规格失败，请稍后重试',
  )
}

// 为指定商品下的规格新增规格值
export async function createAdminProductSpecificationValue(
  productId: number,
  specificationId: number,
  request: CreateProductSpecificationValueRequest,
  accessToken: string,
): Promise<ProductSpecificationValueResponse> {
  // 同时传递商品编号和规格编号，由后端检查归属关系
  const response: Response = await sessionFetch(
    '/api/admin/products/' + productId + '/specifications/' + specificationId + '/values',
    {
      method: 'POST',
      headers: {
        Authorization: 'Bearer ' + accessToken,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    },
  )

  return readSpecificationResponse<ProductSpecificationValueResponse>(
    response,
    '新增规格值失败，请稍后重试',
  )
}

// 删除指定商品下已经清空规格值的规格
export async function deleteAdminProductSpecification(
  productId: number,
  specificationId: number,
  accessToken: string,
): Promise<void> {
  // 商品编号和规格编号通过接口路径传递
  const response: Response = await sessionFetch(
    '/api/admin/products/' + productId + '/specifications/' + specificationId,
    {
      method: 'DELETE',
      headers: {
        Authorization: 'Bearer ' + accessToken,
      },
    },
  )

  // 删除成功返回 204，无需读取 JSON
  await ensureSpecificationResponse(response, '删除商品规格失败，请稍后重试')
}

// 删除指定商品、指定规格下未被 SKU 使用的规格值
export async function deleteAdminProductSpecificationValue(
  productId: number,
  specificationId: number,
  valueId: number,
  accessToken: string,
): Promise<void> {
  // 同时传递三级编号，由后端检查完整归属关系
  const response: Response = await sessionFetch(
    '/api/admin/products/' +
      productId +
      '/specifications/' +
      specificationId +
      '/values/' +
      valueId,
    {
      method: 'DELETE',
      headers: {
        Authorization: 'Bearer ' + accessToken,
      },
    },
  )

  // 删除成功返回 204，无需读取 JSON
  await ensureSpecificationResponse(response, '删除规格值失败，请稍后重试')
}

// 统一检查规格接口的响应状态，供查询、新增和删除请求复用
async function ensureSpecificationResponse(
  response: Response,
  fallbackMessage: string,
): Promise<void> {
  // 令牌缺失、无效或过期
  if (response.status === 401) {
    throw new Error('登录已失效，请重新登录')
  }

  // 当前身份没有管理员权限
  if (response.status === 403) {
    throw new Error('没有权限管理商品规格')
  }

  // 服务端异常时显示统一提示
  if (response.status >= 500) {
    throw new Error('商品服务暂时不可用，请稍后重试')
  }

  // 优先显示业务提示，其次显示 DTO 参数校验提示
  if (!response.ok) {
    const errorBody: SpecificationErrorResponse | null = await response.json().catch(() => null)

    const validationMessage: string | undefined = Object.values(errorBody?.errors ?? {}).flat()[0]

    throw new Error(errorBody?.message ?? validationMessage ?? fallbackMessage)
  }
}

// 查询和新增接口成功后读取 JSON，T 表示调用方法需要的返回类型
async function readSpecificationResponse<T>(
  response: Response,
  fallbackMessage: string,
): Promise<T> {
  // 先检查权限、参数校验及业务错误
  await ensureSpecificationResponse(response, fallbackMessage)

  // 读取后端实际返回的数据
  const result: T = await response.json()

  return result
}
