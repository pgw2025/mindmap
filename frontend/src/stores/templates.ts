import { defineStore } from 'pinia'
import { ref } from 'vue'
import * as templatesApi from '@/api/templates'
import type {
  TemplateListItem,
  TemplateDetail,
  AdminTemplateListItem,
  AdminTemplateListQuery
} from '@/api/templates'

export const useTemplatesStore = defineStore('templates', () => {
  // —— 普通用户：启用模板列表 ——
  const enabledList = ref<TemplateListItem[]>([])
  const enabledLoaded = ref(false)
  const enabledLoading = ref(false)

  async function loadEnabled(force = false): Promise<void> {
    if (enabledLoaded.value && !force) return
    enabledLoading.value = true
    try {
      enabledList.value = await templatesApi.fetchEnabledTemplates()
      enabledLoaded.value = true
    } finally {
      enabledLoading.value = false
    }
  }

  async function getEnabled(id: string): Promise<TemplateDetail> {
    return await templatesApi.fetchTemplate(id)
  }

  // —— 管理端：分页列表 ——
  const adminItems = ref<AdminTemplateListItem[]>([])
  const adminTotal = ref(0)
  const adminPage = ref(1)
  const adminPageSize = ref(20)
  const adminLoading = ref(false)
  const adminScope = ref<'all' | 'enabled' | 'disabled'>('all')
  const adminKeyword = ref('')

  async function loadAdmin(): Promise<void> {
    adminLoading.value = true
    try {
      const q: AdminTemplateListQuery = {
        scope: adminScope.value,
        page: adminPage.value,
        pageSize: adminPageSize.value
      }
      if (adminKeyword.value.trim()) q.keyword = adminKeyword.value.trim()
      const res = await templatesApi.fetchAdminTemplates(q)
      adminItems.value = res.items
      adminTotal.value = res.total
      adminPage.value = res.page
      adminPageSize.value = res.pageSize
    } finally {
      adminLoading.value = false
    }
  }

  async function setAdminScope(s: 'all' | 'enabled' | 'disabled'): Promise<void> {
    adminScope.value = s
    adminPage.value = 1
    await loadAdmin()
  }

  async function setAdminKeyword(kw: string): Promise<void> {
    adminKeyword.value = kw
    adminPage.value = 1
    await loadAdmin()
  }

  async function gotoAdminPage(p: number): Promise<void> {
    adminPage.value = Math.max(1, p)
    await loadAdmin()
  }

  async function create(payload: templatesApi.TemplateCreatePayload): Promise<TemplateDetail> {
    const t = await templatesApi.createTemplate(payload)
    await loadAdmin()
    return t
  }

  async function update(id: string, payload: templatesApi.TemplateUpdatePayload): Promise<TemplateDetail> {
    const t = await templatesApi.updateTemplate(id, payload)
    await loadAdmin()
    return t
  }

  async function remove(id: string): Promise<void> {
    await templatesApi.deleteTemplate(id)
    await loadAdmin()
  }

  function reset() {
    enabledList.value = []
    enabledLoaded.value = false
    adminItems.value = []
    adminTotal.value = 0
    adminPage.value = 1
    adminScope.value = 'all'
    adminKeyword.value = ''
  }

  return {
    enabledList,
    enabledLoaded,
    enabledLoading,
    adminItems,
    adminTotal,
    adminPage,
    adminPageSize,
    adminLoading,
    adminScope,
    adminKeyword,
    loadEnabled,
    getEnabled,
    loadAdmin,
    setAdminScope,
    setAdminKeyword,
    gotoAdminPage,
    create,
    update,
    remove,
    reset
  }
})
