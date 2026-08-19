import { http } from './http'
import type { PageResult } from './types'

/** 模板缩略图色板 */
export interface TemplateSwatch {
  rootFill: string
  secondFill: string
  lineColor: string
  bg: string
}

/** 模板列表项（不含完整 JSON） */
export interface TemplateListItem {
  id: string
  name: string
  description?: string | null
  sortOrder: number
  swatchJson?: string | null
  updatedAt: string
}

/** 模板详情（含完整样式 + 初始结构 JSON） */
export interface TemplateDetail extends TemplateListItem {
  configJson: string
  initialStructureJson: string
  isEnabled: boolean
  createdById: string
  createdByName?: string | null
  createdAt: string
}

/** 管理端列表项 */
export interface AdminTemplateListItem {
  id: string
  name: string
  description?: string | null
  sortOrder: number
  isEnabled: boolean
  swatchJson?: string | null
  createdById: string
  createdByName?: string | null
  createdAt: string
  updatedAt: string
}

export interface AdminTemplateListQuery {
  scope?: 'all' | 'enabled' | 'disabled'
  keyword?: string
  page?: number
  pageSize?: number
}

export interface TemplateCreatePayload {
  name: string
  description?: string
  sortOrder?: number
  isEnabled?: boolean
  configJson: string
  initialStructureJson?: string
  swatchJson?: string | null
}

export interface TemplateUpdatePayload {
  name?: string
  description?: string
  sortOrder?: number
  isEnabled?: boolean
  configJson?: string
  initialStructureJson?: string
  swatchJson?: string | null
}

// ---------- 公共接口（普通用户） ----------

export async function fetchEnabledTemplates(): Promise<TemplateListItem[]> {
  return (await http.get('/templates')) as unknown as TemplateListItem[]
}

export async function fetchTemplate(id: string): Promise<TemplateDetail> {
  return (await http.get(`/templates/${id}`)) as unknown as TemplateDetail
}

// ---------- 管理端接口 ----------

export async function fetchAdminTemplates(
  query: AdminTemplateListQuery = {}
): Promise<PageResult<AdminTemplateListItem>> {
  const params: Record<string, unknown> = {
    page: query.page ?? 1,
    pageSize: query.pageSize ?? 20
  }
  if (query.scope) params.scope = query.scope
  if (query.keyword) params.keyword = query.keyword
  return (await http.get('/admin/templates', { params })) as unknown as PageResult<AdminTemplateListItem>
}

export async function fetchAdminTemplate(id: string): Promise<TemplateDetail> {
  return (await http.get(`/admin/templates/${id}`)) as unknown as TemplateDetail
}

export async function createTemplate(payload: TemplateCreatePayload): Promise<TemplateDetail> {
  return (await http.post('/admin/templates', payload)) as unknown as TemplateDetail
}

export async function updateTemplate(id: string, payload: TemplateUpdatePayload): Promise<TemplateDetail> {
  return (await http.put(`/admin/templates/${id}`, payload)) as unknown as TemplateDetail
}

export async function deleteTemplate(id: string): Promise<void> {
  await http.delete(`/admin/templates/${id}`)
}

// ---------- 辅助：解析 swatchJson ----------

export function parseSwatch(swatchJson?: string | null): TemplateSwatch | null {
  if (!swatchJson) return null
  try {
    return JSON.parse(swatchJson) as TemplateSwatch
  } catch {
    return null
  }
}
