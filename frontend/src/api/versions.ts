import { http } from './http'

export interface MindMapVersionDto {
  id: string
  versionNumber: number
  remark: string | null
  nodeCount: number
  createdById: string
  createdByName: string
  createdAt: string
}

export interface CreateVersionRequest {
  remark?: string
}

export async function listVersions(mindMapId: string): Promise<MindMapVersionDto[]> {
  return (await http.get(`/mindmaps/${mindMapId}/versions`)) as unknown as MindMapVersionDto[]
}

export async function createVersion(mindMapId: string, req: CreateVersionRequest): Promise<MindMapVersionDto> {
  return (await http.post(`/mindmaps/${mindMapId}/versions`, req)) as unknown as MindMapVersionDto
}

export async function rollbackVersion(mindMapId: string, versionId: string): Promise<string> {
  return (await http.post(`/mindmaps/${mindMapId}/versions/${versionId}/rollback`)) as unknown as string
}

export async function deleteVersion(mindMapId: string, versionId: string): Promise<string> {
  return (await http.delete(`/mindmaps/${mindMapId}/versions/${versionId}`)) as unknown as string
}
