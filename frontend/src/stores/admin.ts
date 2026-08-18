import { defineStore } from 'pinia'
import { ref } from 'vue'
import * as adminApi from '@/api/admin'
import type {
  AdminStats,
  AdminUserListItem,
  AdminUserListQuery,
  AdminMindMapListItem,
  AdminMindMapListQuery,
  AdminReportListItem,
  AdminReportListQuery
} from '@/api/admin'
import type { PageResult } from '@/api/types'

export const useAdminStore = defineStore('admin', () => {
  const stats = ref<AdminStats | null>(null)
  const users = ref<AdminUserListItem[]>([])
  const usersTotal = ref(0)
  const mindMaps = ref<AdminMindMapListItem[]>([])
  const mindMapsTotal = ref(0)
  const reports = ref<AdminReportListItem[]>([])
  const reportsTotal = ref(0)
  const loading = ref(false)

  async function loadStats(): Promise<void> {
    stats.value = await adminApi.fetchAdminStats()
  }

  async function loadUsers(query: AdminUserListQuery = {}): Promise<void> {
    loading.value = true
    try {
      const res: PageResult<AdminUserListItem> = await adminApi.fetchAdminUsers(query)
      users.value = res.items
      usersTotal.value = res.total
    } finally {
      loading.value = false
    }
  }

  async function loadMindMaps(query: AdminMindMapListQuery = {}): Promise<void> {
    loading.value = true
    try {
      const res: PageResult<AdminMindMapListItem> = await adminApi.fetchAdminMindMaps(query)
      mindMaps.value = res.items
      mindMapsTotal.value = res.total
    } finally {
      loading.value = false
    }
  }

  async function loadReports(query: AdminReportListQuery = {}): Promise<void> {
    loading.value = true
    try {
      const res: PageResult<AdminReportListItem> = await adminApi.fetchAdminReports(query)
      reports.value = res.items
      reportsTotal.value = res.total
    } finally {
      loading.value = false
    }
  }

  function reset(): void {
    stats.value = null
    users.value = []
    usersTotal.value = 0
    mindMaps.value = []
    mindMapsTotal.value = 0
    reports.value = []
    reportsTotal.value = 0
  }

  return {
    stats,
    users,
    usersTotal,
    mindMaps,
    mindMapsTotal,
    reports,
    reportsTotal,
    loading,
    loadStats,
    loadUsers,
    loadMindMaps,
    loadReports,
    reset
  }
})
