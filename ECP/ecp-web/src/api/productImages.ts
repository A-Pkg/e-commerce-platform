import { sessionFetch } from '@/api/http'
// 上传成功后，后端返回的数据
export interface ProductCoverResponse {
  productId: number
  imageUrl: string
}

// 用于区分登录失效和普通上传错误
export class ProductImageUnauthorizedError extends Error {}

// 图片接口可能返回的业务错误和参数错误
interface ProductImageErrorResponse {
  message?: string
  errors?: Record<string, string[]>
}

// 上传或更换指定商品的封面
export async function uploadProductCover(
  productId: number,
  file: File,
  accessToken: string,
): Promise<ProductCoverResponse> {
  const formData: FormData = new FormData()
  formData.append('file', file)

  // Content-Type 由浏览器自动生成，包含 multipart boundary
  const response: Response = await sessionFetch('/api/admin/products/' + productId + '/cover-image', {
    method: 'PUT',
    headers: {
      Authorization: 'Bearer ' + accessToken,
    },
    body: formData,
  }).catch(() => {
    throw new Error('网络异常，请重新打开编辑，确认封面是否已保存')
  })

  if (response.status === 401) {
    throw new ProductImageUnauthorizedError('登录已失效，请重新登录')
  }

  if (response.status === 403) {
    throw new Error('没有权限上传商品封面')
  }

  if (response.status === 413) {
    throw new Error('上传请求过大，请选择不超过 2MB 的图片')
  }

  if (response.status >= 500) {
    throw new Error('图片服务暂时不可用，请稍后重试')
  }

  if (!response.ok) {
    const errorBody: ProductImageErrorResponse | null = await response.json().catch(() => null)

    const validationMessage: string | undefined = Object.values(errorBody?.errors ?? {}).flat()[0]

    throw new Error(errorBody?.message ?? validationMessage ?? '上传封面失败，请稍后重试')
  }

  const result: ProductCoverResponse = await response.json()

  return result
}
