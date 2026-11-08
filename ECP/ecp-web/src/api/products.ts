import { sessionFetch } from '@/api/http'
import type { PagedResponse } from '@/types/pagination'

// 商品接口返回的数据，与后端 ProductResponse 对应
export interface ProductResponse {
  // 商品编号
  id: number

  // 所属分类编号
  categoryId: number

  // 所属分类名称
  categoryName: string

  // 商品名称
  name: string

  // 商品描述
  description: string

  // 是否上架
  isOnSale: boolean

  // 创建时间，后端时间字段在 JSON 中是字符串
  createdAtUtc: string
}

// 商品 SKU 查询结果，与后端 ProductSkuResponse 对应
export interface ProductSkuResponse {
  // SKU 内部编号
  id: number

  // 所属商品编号
  productId: number

  // SKU 业务编码
  skuCode: string

  // 规格组合展示文字
  specificationText: string

  // 当前售价，单位为人民币元
  price: number

  // 当前可售库存
  stock: number

  // 是否启用
  isActive: boolean
}

// 管理员创建 SKU 的请求，与后端 CreateProductSkuRequest 对应
export interface CreateProductSkuRequest {
  // 售价，单位为人民币元
  price: number

  // 初始库存
  initialStock: number

  // 已选择的规格值编号，未选择任何规格时传空数组
  specificationValueIds: number[]
}

// 管理员编辑 SKU 的请求
export interface UpdateProductSkuRequest {
  // 修改后的售价
  price: number

  // 修改后的库存
  stock: number

  // 修改后的启用状态
  isActive: boolean

  // 打开编辑时的库存，用于防止覆盖已经变化的库存
  expectedStock: number

  // 同一次保存重试复用的请求标识
  requestId: string
}

// 管理员新增商品的请求
export interface CreateProductRequest {
  // 所属分类编号
  categoryId: number

  // 商品名称
  name: string

  // 商品描述
  description: string
}

// 管理员修改商品公共信息的请求
export interface UpdateProductRequest {
  // 所属分类编号
  categoryId: number

  // 商品名称
  name: string

  // 商品描述
  description: string
}

// 管理员设置商品上下架状态的请求
export interface UpdateProductSaleStatusRequest {
  // 明确提交目标状态
  isOnSale: boolean
}

// 后台商品查询条件，空值表示不筛选
export interface AdminProductFilters {
  name: string
  categoryId: number | null
  status: '' | 'on' | 'off'
}

// 商品接口可能返回的业务错误及参数校验错误
interface ProductErrorResponse {
  message?: string
  errors?: Record<string, string[]>
}

// 获取商城已上架商品，允许游客访问
export async function getProducts(): Promise<ProductResponse[]> {
  // 请求公开接口，无需携带访问令牌
  const response: Response = await sessionFetch('/api/products', {
    method: 'GET',
    cache: 'no-store',
  })

  if (response.status >= 500) {
    throw new Error('商品服务暂时不可用，请稍后重试')
  }

  // 兼容没有 JSON 正文的错误响应
  if (!response.ok) {
    const errorBody: ProductErrorResponse | null = await response.json().catch(() => null)

    throw new Error(errorBody?.message ?? '获取商品列表失败，请稍后重试')
  }

  // 没有已上架商品时返回空数组
  const products: ProductResponse[] = await response.json()

  return products
}

// 查询商城商品的启用 SKU，允许游客访问
export async function getProductSkus(
  productId: number,
): Promise<ProductSkuResponse[]> {
  const response: Response = await sessionFetch('/api/products/' + productId + '/skus', {
    method: 'GET',
    cache: 'no-store',
  })

  if (response.status >= 500) {
    throw new Error('商品服务暂时不可用，请稍后重试')
  }

  // 显示后端业务提示，例如商品已经下架
  if (!response.ok) {
    const errorBody: ProductErrorResponse | null = await response.json().catch(() => null)
    const fallbackMessage: string =
      response.status === 404
        ? '商品不存在或已下架'
        : '获取商品规格失败，请稍后重试'

    throw new Error(errorBody?.message ?? fallbackMessage)
  }

  const skus: ProductSkuResponse[] = await response.json()

  return skus
}

// 按筛选条件分页获取后台商品，携带当前管理员的访问令牌
export async function getAdminProducts(
  accessToken: string,
  filters?: AdminProductFilters,
  pageNumber: number = 1,
): Promise<PagedResponse<ProductResponse>> {
  const parameters = new URLSearchParams()
  const name: string = filters?.name.trim() ?? ''

  if (name !== '') {
    parameters.set('name', name)
  }

  if (filters?.categoryId != null) {
    parameters.set('categoryId', String(filters.categoryId))
  }

  if (filters?.status) {
    parameters.set('status', filters.status)
  }

  parameters.set('pageNumber', String(pageNumber))

  // 通过 Vite 代理请求后台商品接口
  const response: Response = await sessionFetch('/api/admin/products?' + parameters.toString(), {
    method: 'GET',
    headers: {
      Authorization: `Bearer ${accessToken}`,
    },
    cache: 'no-store',
  })

  // 令牌缺失、无效或过期时，提示重新登录
  if (response.status === 401) {
    throw new Error('登录已失效，请重新登录')
  }

  // 当前身份没有管理员权限时，提示权限不足
  if (response.status === 403) {
    throw new Error('没有权限访问商品管理')
  }

  // 服务异常时显示统一提示
  if (response.status >= 500) {
    throw new Error('商品服务暂时不可用，请稍后重试')
  }

  // 处理其他失败响应，兼容没有 JSON 正文的情况
  if (!response.ok) {
    const errorBody: ProductErrorResponse | null = await response.json().catch(() => null)

    throw new Error(errorBody?.message ?? '获取后台商品列表失败，请稍后重试')
  }

  // 读取分页信息和当前页商品，空结果的 items 为 []
  const result: PagedResponse<ProductResponse> = await response.json()

  return result
}

// 查询指定商品的全部 SKU，包含启用和停用项
export async function getAdminProductSkus(
  productId: number,
  accessToken: string,
): Promise<ProductSkuResponse[]> {
  // 每次查询都重新读取后台数据
  const response: Response = await sessionFetch('/api/admin/products/' + productId + '/skus', {
    method: 'GET',
    headers: {
      Authorization: 'Bearer ' + accessToken,
    },
    cache: 'no-store',
  })

  if (response.status === 401) {
    throw new Error('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new Error('没有权限查看商品 SKU')
  }

  if (response.status >= 500) {
    throw new Error('商品服务暂时不可用，请稍后重试')
  }

  // 优先显示后端业务提示，兼容没有 JSON 正文的错误响应
  if (!response.ok) {
    const errorBody: ProductErrorResponse | null = await response.json().catch(() => null)
    const validationMessage: string | undefined = Object.values(errorBody?.errors ?? {}).flat()[0]
    const fallbackMessage: string =
      response.status === 404 ? '该商品不存在' : '获取商品 SKU 失败，请稍后重试'

    throw new Error(errorBody?.message ?? validationMessage ?? fallbackMessage)
  }

  // 商品没有 SKU 时，结果为空数组
  const skus: ProductSkuResponse[] = await response.json()

  return skus
}

// 为指定商品创建一个 SKU
export async function createAdminProductSku(
  productId: number,
  request: CreateProductSkuRequest,
  accessToken: string,
): Promise<ProductSkuResponse> {
  // 提交售价、初始库存和已选规格值，编码由后端生成
  const response: Response = await sessionFetch('/api/admin/products/' + productId + '/skus', {
    method: 'POST',
    headers: {
      Authorization: 'Bearer ' + accessToken,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
  })

  // 令牌缺失、无效或过期
  if (response.status === 401) {
    throw new Error('登录已失效，请重新登录')
  }

  // 当前身份没有管理员权限
  if (response.status === 403) {
    throw new Error('没有权限创建商品 SKU')
  }

  // 服务端异常时显示统一提示
  if (response.status >= 500) {
    throw new Error('商品服务暂时不可用，请稍后重试')
  }

  // 优先显示后端业务提示，例如规格组合重复
  if (!response.ok) {
    const errorBody: ProductErrorResponse | null = await response.json().catch(() => null)
    const validationMessage: string | undefined = Object.values(errorBody?.errors ?? {}).flat()[0]
    const fallbackMessage: string =
      response.status === 404 ? '该商品不存在' : '创建 SKU 失败，请稍后重试'

    throw new Error(errorBody?.message ?? validationMessage ?? fallbackMessage)
  }

  // 返回后端实际创建的 SKU，供列表更新使用
  const sku: ProductSkuResponse = await response.json()

  return sku
}

// 编辑指定商品下的 SKU
export async function updateAdminProductSku(
  productId: number,
  skuId: number,
  request: UpdateProductSkuRequest,
  accessToken: string,
): Promise<void> {
  // 统一保存售价、库存和启用状态
  const response: Response = await sessionFetch(
    '/api/admin/products/' + productId + '/skus/' + skuId,
    {
      method: 'PUT',
      headers: {
        Authorization: 'Bearer ' + accessToken,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    },
  )

  if (response.status === 401) {
    throw new Error('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new Error('没有权限编辑商品 SKU')
  }

  if (response.status >= 500) {
    throw new Error('商品服务暂时不可用，请稍后重试')
  }

  // 显示业务错误及参数校验错误，包括库存已变化的提示
  if (!response.ok) {
    const errorBody: ProductErrorResponse | null = await response.json().catch(() => null)
    const validationMessage: string | undefined = Object.values(errorBody?.errors ?? {}).flat()[0]

    throw new Error(errorBody?.message ?? validationMessage ?? '保存 SKU 失败，请稍后重试')
  }

  // 成功响应为 204，没有 JSON 正文
}

// 新增商品，由后端设置默认下架状态
export async function createAdminProduct(
  request: CreateProductRequest,
  accessToken: string,
): Promise<ProductResponse> {
  // 提交商品公共信息
  const response: Response = await sessionFetch('/api/admin/products', {
    method: 'POST',
    headers: {
      Authorization: 'Bearer ' + accessToken,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
  })

  return readSavedProductResponse(response)
}

// 修改商品公共信息，保留原有上下架状态
export async function updateAdminProduct(
  productId: number,
  request: UpdateProductRequest,
  accessToken: string,
): Promise<ProductResponse> {
  // 提交指定商品的修改内容
  const response: Response = await sessionFetch('/api/admin/products/' + productId, {
    method: 'PUT',
    headers: {
      Authorization: 'Bearer ' + accessToken,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
  })

  return readSavedProductResponse(response)
}

// 设置指定商品的上下架状态
export async function updateAdminProductSaleStatus(
  productId: number,
  request: UpdateProductSaleStatusRequest,
  accessToken: string,
): Promise<void> {
  // 提交明确的目标状态
  const response: Response = await sessionFetch('/api/admin/products/' + productId + '/sale-status', {
    method: 'PUT',
    headers: {
      Authorization: 'Bearer ' + accessToken,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
  })

  if (response.status === 401) {
    throw new Error('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new Error('没有权限修改商品上下架状态')
  }

  if (response.status >= 500) {
    throw new Error('商品服务暂时不可用，请稍后重试')
  }

  // 优先显示后端业务提示，其次显示参数校验提示
  if (!response.ok) {
    const errorBody: ProductErrorResponse | null = await response.json().catch(() => null)
    const validationMessage: string | undefined = Object.values(errorBody?.errors ?? {}).flat()[0]

    throw new Error(errorBody?.message ?? validationMessage ?? '修改商品上下架状态失败，请稍后重试')
  }

  // 成功响应为 204，没有 JSON 正文
}

// 统一读取新增、修改商品的响应
async function readSavedProductResponse(response: Response): Promise<ProductResponse> {
  if (response.status === 401) {
    throw new Error('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new Error('没有权限保存商品')
  }

  if (response.status >= 500) {
    throw new Error('商品服务暂时不可用，请稍后重试')
  }

  // 优先显示后端业务提示，其次显示参数校验提示
  if (!response.ok) {
    const errorBody: ProductErrorResponse | null = await response.json().catch(() => null)
    const validationMessage: string | undefined = Object.values(errorBody?.errors ?? {}).flat()[0]

    throw new Error(errorBody?.message ?? validationMessage ?? '保存商品失败，请稍后重试')
  }

  // 返回后端实际保存的商品信息
  const product: ProductResponse = await response.json()

  return product
}
