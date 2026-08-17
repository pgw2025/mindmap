// 后端统一响应结构
export interface ApiResult<T = unknown> {
  code: number
  message?: string
  data: T
}

// 分页结果
export interface PageResult<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}
