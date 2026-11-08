import { sessionFetch } from '@/api/http'
// 分类接口返回的数据，与后端 CategoryResponse 对应
export interface CategoryResponse {
  // 分类编号
  id: number

  // 父分类编号，null 表示大分类
  parentId: number | null

  // 分类名称
  name: string

  // 展示顺序
  sortOrder: number

  // 是否启用
  isActive: boolean

  // 创建时间，后端时间字段在 JSON 中是字符串
  createdAtUtc: string
}

// 管理员新增分类的请求，与后端 CreateCategoryRequest 对应
export interface CreateCategoryRequest {
  // 父分类编号，null 表示大分类
  parentId: number | null

  // 分类名称
  name: string

  // 非负整数排序值
  sortOrder: number

  // 是否启用
  isActive: boolean
}

// 管理员修改分类的请求，与后端 UpdateCategoryRequest 对应
export interface UpdateCategoryRequest {
  // 父分类编号，null 表示大分类
  parentId: number | null

  // 分类名称
  name: string

  // 非负整数排序值
  sortOrder: number

  // 是否启用
  isActive: boolean
}

// 分类接口可能返回的业务错误及参数校验错误
interface CategoryErrorResponse {
  message?: string
  errors?: Record<string, string[]>
}

// 获取商城端已启用的分类，失败时抛出错误供页面处理
export async function getCategories(): Promise<CategoryResponse[]> {
  // 通过 Vite 的 /api 代理请求分类列表，无需携带 Token
  const response: Response = await sessionFetch('/api/categories', {
    method: 'GET',
    cache: 'no-store',
  })

  // 服务异常时，使用统一提示
  if (response.status >= 500) {
    throw new Error('分类服务暂时不可用，请稍后重试')
  }

  // 处理其他失败情况，兼容错误响应没有 JSON 正文的情况
  if (!response.ok) {
    const errorBody: CategoryErrorResponse | null = await response.json().catch(() => null)

    throw new Error(errorBody?.message ?? '获取分类列表失败，请稍后重试')
  }

  // 读取分类数组，没有已启用的分类时结果为 []
  const categories: CategoryResponse[] = await response.json()

  return categories
}

// 获取后台全部分类，携带当前管理员的访问令牌
export async function getAdminCategories(accessToken: string): Promise<CategoryResponse[]> {
  // 通过 Vite 代理请求后台分类接口
  const response: Response = await sessionFetch('/api/admin/categories', {
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
    throw new Error('没有权限访问分类管理')
  }

  // 服务异常时，显示统一提示
  if (response.status >= 500) {
    throw new Error('分类服务暂时不可用，请稍后重试')
  }

  // 处理其他失败响应，兼容没有 JSON 正文的情况
  if (!response.ok) {
    const errorBody: CategoryErrorResponse | null = await response.json().catch(() => null)

    throw new Error(errorBody?.message ?? '获取后台分类列表失败，请稍后重试')
  }

  // 读取分类数组，包含启用和停用的分类
  const categories: CategoryResponse[] = await response.json()

  return categories
}

// 保存管理员编辑的分类，返回后端实际保存后的数据
export async function updateAdminCategory(
  categoryId: number,
  request: UpdateCategoryRequest,
  accessToken: string,
): Promise<CategoryResponse> {
  // 携带管理员令牌和完整的分类更新数据
  const response: Response = await sessionFetch('/api/admin/categories/' + categoryId, {
    method: 'PUT',
    headers: {
      Authorization: 'Bearer ' + accessToken,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
  })

  // 身份失效时提示重新登录
  if (response.status === 401) {
    throw new Error('登录已失效，请重新登录')
  }

  // 非管理员身份无法保存分类
  if (response.status === 403) {
    throw new Error('没有权限修改分类')
  }

  // 服务异常时使用统一提示
  if (response.status >= 500) {
    throw new Error('分类服务暂时不可用，请稍后重试')
  }

  // 显示后端的层级、重名或参数校验提示
  if (!response.ok) {
    const errorBody: CategoryErrorResponse | null = await response.json().catch(() => null)
    const validationMessage: string | undefined = Object.values(errorBody?.errors ?? {}).flat()[0]

    throw new Error(errorBody?.message ?? validationMessage ?? '保存分类失败，请稍后重试')
  }

  // 使用后端返回的数据更新页面，避免将未保存的输入当成结果
  const category: CategoryResponse = await response.json()

  return category
}

// 新增分类，返回后端创建成功后的完整分类信息
export async function createAdminCategory(
  request: CreateCategoryRequest,
  accessToken: string,
): Promise<CategoryResponse> {
  // 携带管理员令牌和分类数据
  const response: Response = await sessionFetch('/api/admin/categories', {
    method: 'POST',
    headers: {
      Authorization: 'Bearer ' + accessToken,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
  })

  // 身份失效时提示重新登录
  if (response.status === 401) {
    throw new Error('登录已失效，请重新登录')
  }

  // 非管理员身份无法新增分类
  if (response.status === 403) {
    throw new Error('没有权限新增分类')
  }

  // 服务异常时使用统一提示
  if (response.status >= 500) {
    throw new Error('分类服务暂时不可用，请稍后重试')
  }

  // 显示后端的父分类、重名或参数校验提示
  if (!response.ok) {
    const errorBody: CategoryErrorResponse | null = await response.json().catch(() => null)
    const validationMessage: string | undefined = Object.values(errorBody?.errors ?? {}).flat()[0]

    throw new Error(errorBody?.message ?? validationMessage ?? '新增分类失败，请稍后重试')
  }

  // 使用后端返回的实际编号和分类信息更新页面
  const category: CategoryResponse = await response.json()

  return category
}
