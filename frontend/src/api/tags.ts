import { http } from './http'

export interface TagDto {
  id: string
  name: string
  color: string
  createdAt: string
  mindMapCount: number
}

export interface TagCreatePayload {
  name: string
  color?: string
}

export interface TagUpdatePayload {
  name?: string
  color?: string
}

export async function fetchTags(): Promise<TagDto[]> {
  return (await http.get('/tags')) as unknown as TagDto[]
}

export async function createTag(payload: TagCreatePayload): Promise<TagDto> {
  return (await http.post('/tags', payload)) as unknown as TagDto
}

export async function updateTag(id: string, payload: TagUpdatePayload): Promise<TagDto> {
  return (await http.put(`/tags/${id}`, payload)) as unknown as TagDto
}

export async function deleteTag(id: string): Promise<void> {
  await http.delete(`/tags/${id}`)
}
