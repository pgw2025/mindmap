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

// ---------- 导入导出 ----------

export interface TemplateImportFailedItem {
  name: string
  reason: string
}

export interface TemplateImportResult {
  created: number
  skipped: number
  failed: TemplateImportFailedItem[]
  total: number
}

/** 导出单个模板（触发浏览器下载）。文件名优先取后端返回的 Content-Disposition（即模板名）。 */
export async function exportTemplate(id: string): Promise<void> {
  await downloadExport(`/admin/templates/${id}/export`)
}

/** 导出全部模板（触发浏览器下载）。 */
export async function exportAllTemplates(): Promise<void> {
  await downloadExport('/admin/templates/export')
}

/**
 * 以 blob 方式下载导出文件。
 * 优先从响应头 Content-Disposition 解析后端给定的文件名（单模板导出时为模板名），
 * 解析失败时回退到默认名。
 */
async function downloadExport(url: string): Promise<void> {
  // blob 请求的响应拦截器会返回完整 AxiosResponse（含 data 与 headers）
  const response = await http.get<Blob>(url, { responseType: 'blob' })
  const axiosResponse = response as unknown as { data: Blob; headers?: Record<string, string> }
  const blob = axiosResponse.data

  let filename =
    extractFilename(axiosResponse.headers?.['content-disposition']) ||
    extractFilename(axiosResponse.headers?.['Content-Disposition'])
  if (!filename) {
    // 兜底：单模板导出回退 template，全量导出回退 templates
    filename = url.endsWith('/export') ? 'templates.mmtpl.json' : 'template.mmtpl.json'
  }
  triggerDownload(blob, filename)
}

/** 从 Content-Disposition 头解析文件名（支持 filename= 与 filename*= UTF-8 编码）。 */
function extractFilename(disposition?: string): string | null {
  if (!disposition) return null
  // 优先解析 filename*=UTF-8''xxx
  const star = disposition.match(/filename\*=UTF-8''([^;]+)/i)
  if (star) {
    try {
      return decodeURIComponent(star[1])
    } catch {
      /* 忽略，继续尝试普通 filename */
    }
  }
  const plain = disposition.match(/filename="?([^";]+)"?/i)
  if (plain) return plain[1]
  return null
}

/** 导入模板文件，返回导入报告。 */
export async function importTemplate(file: File): Promise<TemplateImportResult> {
  const form = new FormData()
  form.append('file', file)
  return (await http.post('/admin/templates/import', form, {
    headers: { 'Content-Type': 'multipart/form-data' }
  })) as unknown as TemplateImportResult
}

function triggerDownload(blob: Blob, fallbackName: string): void {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = fallbackName
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  URL.revokeObjectURL(url)
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
