import { http } from './http'
import type { PageResult } from './types'
import type { TagDto } from './tags'

/** 后端枚举 MindMapLayout：0=Left 1=Right 2=Top 3=Bottom 4=Radial */
export type MindMapLayout = 0 | 1 | 2 | 3 | 4

export interface MindMapListItem {
  id: string
  title: string
  description?: string | null
  isPublic: boolean
  coverImage?: string | null
  defaultLayout: number
  nodeCount: number
  createdAt: string
  lastEditedAt: string
  ownerId: string
  ownerName: string
  folderId?: string | null
  folderName?: string | null
  tags: Pick<TagDto, 'id' | 'name' | 'color'>[]
}

export interface MindMapDetail extends MindMapListItem {
  updatedAt: string
  theme?: string | null
  rootNodeId?: string | null
}

export interface MindMapListQuery {
  scope?: 'mine' | 'public'
  folderId?: string | null
  tagId?: string | null
  keyword?: string
  page?: number
  pageSize?: number
}

export interface MindMapCreatePayload {
  title: string
  description?: string
  folderId?: string | null
  isPublic?: boolean
  defaultLayout?: MindMapLayout
  tagIds?: string[]
}

export interface MindMapUpdatePayload {
  title?: string
  description?: string
  folderId?: string | null
  isPublic?: boolean
  defaultLayout?: MindMapLayout
}

export async function fetchMindMaps(
  query: MindMapListQuery = {}
): Promise<PageResult<MindMapListItem>> {
  const params: Record<string, unknown> = {
    page: query.page ?? 1,
    pageSize: query.pageSize ?? 20
  }
  if (query.scope) params.scope = query.scope
  if (query.folderId) params.folderId = query.folderId
  if (query.tagId) params.tagId = query.tagId
  if (query.keyword) params.keyword = query.keyword
  return (await http.get('/mindmaps', { params })) as unknown as PageResult<MindMapListItem>
}

export async function fetchMindMap(id: string): Promise<MindMapDetail> {
  return (await http.get(`/mindmaps/${id}`)) as unknown as MindMapDetail
}

export async function createMindMap(payload: MindMapCreatePayload): Promise<MindMapDetail> {
  return (await http.post('/mindmaps', payload)) as unknown as MindMapDetail
}

export async function updateMindMap(
  id: string,
  payload: MindMapUpdatePayload
): Promise<MindMapDetail> {
  return (await http.put(`/mindmaps/${id}`, payload)) as unknown as MindMapDetail
}

export async function deleteMindMap(id: string): Promise<void> {
  await http.delete(`/mindmaps/${id}`)
}

export async function copyMindMap(id: string, newTitle?: string): Promise<MindMapDetail> {
  return (await http.post(`/mindmaps/${id}/copy`, { newTitle })) as unknown as MindMapDetail
}

export async function setMindMapTags(id: string, tagIds: string[]): Promise<void> {
  await http.put(`/mindmaps/${id}/tags`, { tagIds })
}
