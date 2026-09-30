import { http } from './http'
import type { PageResult } from './types'

/** 回收站条目 */
export interface RecycleBinItem {
  id: string
  title: string
  description?: string | null
  nodeCount: number
  /** 移入回收站时间（UTC ISO） */
  deletedAt: string
  /** 预计物理清除时间（UTC ISO） */
  expiresAt: string
  /** 剩余保留天数（0 = 今天到期） */
  daysLeft: number
  folderId?: string | null
  folderName?: string | null
}

/** 回收站保留天数设置 */
export interface RecycleBinSettings {
  /** 当前生效保留天数（个人覆盖或全局默认） */
  effectiveRetentionDays: number
  /** 个人覆盖天数；null = 跟随全局默认 */
  customRetentionDays: number | null
}

export interface RecycleBinListQuery {
  keyword?: string
  page?: number
  pageSize?: number
}

export async function fetchRecycleBin(
  query: RecycleBinListQuery = {}
): Promise<PageResult<RecycleBinItem>> {
  const params: Record<string, unknown> = {
    page: query.page ?? 1,
    pageSize: query.pageSize ?? 20
  }
  if (query.keyword) params.keyword = query.keyword
  return (await http.get('/recycle-bin', { params })) as unknown as PageResult<RecycleBinItem>
}

export async function restoreFromRecycleBin(id: string): Promise<void> {
  await http.post(`/recycle-bin/${id}/restore`)
}

export async function deleteFromRecycleBin(id: string): Promise<void> {
  await http.delete(`/recycle-bin/${id}`)
}

export async function fetchRecycleBinSettings(): Promise<RecycleBinSettings> {
  return (await http.get('/recycle-bin/settings')) as unknown as RecycleBinSettings
}

export async function updateRecycleBinSettings(
  retentionDays: number | null
): Promise<RecycleBinSettings> {
  return (await http.put('/recycle-bin/settings', { retentionDays })) as unknown as RecycleBinSettings
}
