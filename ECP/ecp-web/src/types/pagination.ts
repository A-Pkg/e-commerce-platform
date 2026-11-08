// 共用分页响应，与后端 PagedResponse<T> 的 JSON 字段对应
export interface PagedResponse<T> {
  // 后端实际返回的页码，从 1 开始
  pageNumber: number
  // 每页最多返回的条数
  pageSize: number
  // 满足筛选条件的总条数
  totalCount: number
  // 总页数，空列表也保留第 1 页
  totalPages: number
  // 当前页数据，元素类型由调用方指定
  items: T[]
}
