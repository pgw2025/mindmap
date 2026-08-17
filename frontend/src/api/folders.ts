import { http } from './http'

export interface FolderDto {
  id: string
  parentId: string | null
  name: string
  sortOrder: number
  createdAt: string
  updatedAt: string
}

export interface FolderNode extends FolderDto {
  children: FolderNode[]
  mindMapCount: number
}

export interface FolderCreatePayload {
  name: string
  parentId?: string | null
  sortOrder?: number | null
}

export interface FolderUpdatePayload {
  name: string
  sortOrder?: number | null
}

export async function fetchFolderTree(): Promise<FolderNode[]> {
  return (await http.get('/folders/tree')) as unknown as FolderNode[]
}

export async function createFolder(payload: FolderCreatePayload): Promise<FolderDto> {
  return (await http.post('/folders', payload)) as unknown as FolderDto
}

export async function updateFolder(id: string, payload: FolderUpdatePayload): Promise<FolderDto> {
  return (await http.put(`/folders/${id}`, payload)) as unknown as FolderDto
}

export async function moveFolder(id: string, parentId: string | null): Promise<void> {
  await http.post(`/folders/${id}/move`, { parentId })
}

export async function deleteFolder(id: string): Promise<void> {
  await http.delete(`/folders/${id}`)
}
