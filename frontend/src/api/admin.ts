import { http } from './http'
import type { PageResult } from './types'

// ===================== 用户管理 =====================

export interface AdminUserListItem {
  id: string
  username: string
  email: string
  avatar?: string | null
  isAdmin: boolean
  status: number // 0=Active 1=Disabled
  lastLoginAt?: string | null
  createdAt: string
  mindMapCount: number
}

export interface AdminUserListQuery {
  scope?: 'all' | 'active' | 'disabled' | 'admin'
  keyword?: string
  page?: number
  pageSize?: number
}

export interface AdminUserUpdatePayload {
  isAdmin?: boolean
  status?: number // 0=Active 1=Disabled
}

export async function fetchAdminUsers(
  query: AdminUserListQuery = {}
): Promise<PageResult<AdminUserListItem>> {
  const params: Record<string, unknown> = {
    page: query.page ?? 1,
    pageSize: query.pageSize ?? 20
  }
  if (query.scope) params.scope = query.scope
  if (query.keyword) params.keyword = query.keyword
  return (await http.get('/admin/users', { params })) as unknown as PageResult<AdminUserListItem>
}

export async function updateAdminUser(
  id: string,
  payload: AdminUserUpdatePayload
): Promise<void> {
  await http.put(`/admin/users/${id}`, payload)
}

export async function deleteAdminUser(id: string): Promise<void> {
  await http.delete(`/admin/users/${id}`)
}

// ===================== 导图管理 =====================

export interface AdminMindMapListItem {
  id: string
  title: string
  description?: string | null
  isPublic: boolean
  isTakenDown: boolean
  takenDownReason?: string | null
  takenDownAt?: string | null
  nodeCount: number
  createdAt: string
  lastEditedAt: string
  ownerId: string
  ownerName: string
}

export interface AdminMindMapListQuery {
  scope?: 'all' | 'public' | 'takenDown'
  keyword?: string
  page?: number
  pageSize?: number
}

export async function fetchAdminMindMaps(
  query: AdminMindMapListQuery = {}
): Promise<PageResult<AdminMindMapListItem>> {
  const params: Record<string, unknown> = {
    page: query.page ?? 1,
    pageSize: query.pageSize ?? 20
  }
  if (query.scope) params.scope = query.scope
  if (query.keyword) params.keyword = query.keyword
  return (await http.get('/admin/mindmaps', { params })) as unknown as PageResult<AdminMindMapListItem>
}

export async function takeDownMindMap(id: string, reason?: string): Promise<void> {
  await http.post(`/admin/mindmaps/${id}/takedown`, { reason })
}

export async function restoreMindMap(id: string): Promise<void> {
  await http.post(`/admin/mindmaps/${id}/restore`)
}

export async function deleteAdminMindMap(id: string): Promise<void> {
  await http.delete(`/admin/mindmaps/${id}`)
}

// ===================== 统计 =====================

export interface AdminDailyCount {
  date: string
  count: number
}

export interface AdminStats {
  userCount: number
  activeUserCount: number
  disabledUserCount: number
  adminCount: number
  mindMapCount: number
  publicMindMapCount: number
  takenDownMindMapCount: number
  shareCount: number
  activeShareCount: number
  pendingReportCount: number
  totalReportCount: number
  newUsersLast7Days: AdminDailyCount[]
  newMindMapsLast7Days: AdminDailyCount[]
}

export async function fetchAdminStats(): Promise<AdminStats> {
  return (await http.get('/admin/stats')) as unknown as AdminStats
}

// ===================== 举报管理 =====================

export interface AdminReportListItem {
  id: string
  mindMapId: string
  mindMapTitle: string
  mindMapOwnerId: string
  mindMapOwnerName: string
  reporterId?: string | null
  reporterName?: string | null
  reason: string
  status: number // 0=Pending 1=Rejected 2=TakenDown
  resolutionNote?: string | null
  createdAt: string
  resolvedAt?: string | null
}

export interface AdminReportListQuery {
  scope?: 'pending' | 'resolved' | 'all'
  keyword?: string
  page?: number
  pageSize?: number
}

export interface AdminReportResolvePayload {
  takeDown: boolean
  note?: string
}

export async function fetchAdminReports(
  query: AdminReportListQuery = {}
): Promise<PageResult<AdminReportListItem>> {
  const params: Record<string, unknown> = {
    page: query.page ?? 1,
    pageSize: query.pageSize ?? 20
  }
  if (query.scope) params.scope = query.scope
  if (query.keyword) params.keyword = query.keyword
  return (await http.get('/admin/reports', { params })) as unknown as PageResult<AdminReportListItem>
}

export async function resolveAdminReport(
  id: string,
  payload: AdminReportResolvePayload
): Promise<void> {
  await http.post(`/admin/reports/${id}/resolve`, payload)
}

// ===================== 用户端举报 =====================

export async function reportMindMap(mindMapId: string, reason: string): Promise<void> {
  await http.post(`/mindmaps/${mindMapId}/reports`, { reason })
}
