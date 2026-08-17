import { http } from './http'
import type { NodeTreeNodeDto } from './nodes'
import type { MindMapDetail } from './mindmaps'

export interface ShareDto {
  id: string
  mindMapId: string
  shareToken: string
  hasPassword: boolean
  expiresAt?: string | null
  maxAccessCount?: number | null
  accessCount: number
  allowCopy: boolean
  isDisabled: boolean
  createdAt: string
  lastAccessedAt?: string | null
}

export interface ShareCreatePayload {
  setPublic?: boolean
  password?: string
  expiresAt?: string | null
  maxAccessCount?: number | null
  allowCopy?: boolean
}

export interface ShareVerifyPayload {
  token: string
  password?: string
}

export interface ShareVerifyResponse {
  success: boolean
  message?: string | null
  needsPassword?: boolean
  mindMapId?: string
  title?: string
  ownerId?: string
  ownerName?: string
  allowCopy?: boolean
  accessToken?: string
}

export interface ShareMindMapResponse {
  mindMap: MindMapDetail
  nodes: NodeTreeNodeDto[]
}

export async function fetchShares(mindMapId: string): Promise<ShareDto[]> {
  return (await http.get(`/mindmaps/${mindMapId}/shares`)) as unknown as ShareDto[]
}

export async function createShare(
  mindMapId: string,
  payload: ShareCreatePayload
): Promise<ShareDto> {
  return (await http.post(`/mindmaps/${mindMapId}/shares`, payload)) as unknown as ShareDto
}

export async function deleteShare(shareId: string): Promise<void> {
  await http.delete(`/shares/${shareId}`)
}

export async function verifyShare(
  payload: ShareVerifyPayload
): Promise<ShareVerifyResponse> {
  return (await http.post('/shares/verify', payload)) as unknown as ShareVerifyResponse
}

export async function fetchSharedMindMap(token: string): Promise<ShareMindMapResponse> {
  return (await http.get(`/shares/${token}/mindmap`)) as unknown as ShareMindMapResponse
}
