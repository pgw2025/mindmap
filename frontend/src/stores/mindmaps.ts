import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import * as mapsApi from '@/api/mindmaps'
import type { MindMapListItem, MindMapListQuery } from '@/api/mindmaps'
import { useFoldersStore } from './folders'
import { useTagsStore } from './tags'
import * as offlineDb from '@/offline/db'

async function refreshFolders(): Promise<void> {
  try {
    await useFoldersStore().load(true)
  } catch {
  }
}

async function refreshTags(): Promise<void> {
  try {
    await useTagsStore().load(true)
  } catch {
  }
}

export const useMindMapsStore = defineStore('mindmaps', () => {
  const items = ref<MindMapListItem[]>([])
  const total = ref(0)
  const page = ref(1)
  const pageSize = ref(20)
  const loading = ref(false)

  const scope = ref<'mine' | 'public'>('mine')
  const folderId = ref<string | null>(null)
  const tagId = ref<string | null>(null)
  const keyword = ref('')

  /** 当前列表数据是否来自离线快照（供 HomeView 显示提示条） */
  const isOfflineSnapshot = ref(false)
  /** 离线快照的最后同步时间戳（ms） */
  const lastSyncedAt = ref(0)

  const totalPages = computed(() =>
    pageSize.value <= 0 ? 1 : Math.max(1, Math.ceil(total.value / pageSize.value))
  )

  function buildQuery(): MindMapListQuery {
    const q: MindMapListQuery = {
      scope: scope.value,
      page: page.value,
      pageSize: pageSize.value
    }
    if (folderId.value) q.folderId = folderId.value
    if (tagId.value) q.tagId = tagId.value
    if (keyword.value.trim()) q.keyword = keyword.value.trim()
    return q
  }

  async function load() {
    loading.value = true
    try {
      const res = await mapsApi.fetchMindMaps(buildQuery())
      items.value = res.items
      total.value = res.total
      page.value = res.page
      pageSize.value = res.pageSize
      isOfflineSnapshot.value = false
    } catch (err) {
      // 离线兜底：读 lists 快照并按当前筛选条件本地过滤 + 分页
      const loaded = await loadFromSnapshot()
      if (!loaded) throw err
    } finally {
      loading.value = false
    }
  }

  /** 离线兜底：读 IndexedDB 列表快照，本地执行 folderId/tagId/keyword 过滤与分页 */
  async function loadFromSnapshot(): Promise<boolean> {
    const snapshot = await offlineDb.getList(scope.value)
    if (!snapshot) return false

    const kw = keyword.value.trim().toLowerCase()
    let filtered = snapshot.items
    if (folderId.value) filtered = filtered.filter((m) => m.folderId === folderId.value)
    if (tagId.value) filtered = filtered.filter((m) => (m.tags ?? []).some((t) => t.id === tagId.value))
    if (kw) {
      filtered = filtered.filter(
        (m) =>
          m.title.toLowerCase().includes(kw) ||
          (m.description ?? '').toLowerCase().includes(kw)
      )
    }
    // 与服务端排序保持一致：最后编辑时间倒序
    filtered = [...filtered].sort((a, b) => (a.lastEditedAt < b.lastEditedAt ? 1 : -1))

    const size = pageSize.value > 0 ? pageSize.value : 20
    const maxPage = Math.max(1, Math.ceil(filtered.length / size))
    const p = Math.min(page.value, maxPage)
    items.value = filtered.slice((p - 1) * size, p * size)
    total.value = filtered.length
    page.value = p
    isOfflineSnapshot.value = true
    lastSyncedAt.value = snapshot.syncedAt
    return true
  }

  async function setScope(s: 'mine' | 'public') {
    scope.value = s
    folderId.value = null
    tagId.value = null
    page.value = 1
    await load()
  }

  async function setFolderFilter(id: string | null) {
    folderId.value = id
    page.value = 1
    await load()
  }

  async function setTagFilter(id: string | null) {
    tagId.value = id
    page.value = 1
    await load()
  }

  async function setKeyword(kw: string) {
    keyword.value = kw
    page.value = 1
    await load()
  }

  async function gotoPage(p: number) {
    page.value = Math.min(Math.max(1, p), totalPages.value)
    await load()
  }

  async function create(payload: mapsApi.MindMapCreatePayload) {
    const map = await mapsApi.createMindMap(payload)
    await load()
    await refreshFolders()
    return map
  }

  async function update(id: string, payload: mapsApi.MindMapUpdatePayload) {
    const map = await mapsApi.updateMindMap(id, payload)
    await load()
    if ('folderId' in payload) await refreshFolders()
    return map
  }

  async function copy(id: string, newTitle?: string) {
    const map = await mapsApi.copyMindMap(id, newTitle)
    await load()
    await refreshFolders()
    return map
  }

  async function remove(id: string) {
    await mapsApi.deleteMindMap(id)
    await load()
    await refreshFolders()
  }

  async function setTags(id: string, tagIds: string[]) {
    await mapsApi.setMindMapTags(id, tagIds)
    await load()
    await refreshTags()
  }

  async function importFile(payload: mapsApi.MindMapImportPayload) {
    const map = await mapsApi.importMindMap(payload)
    await load()
    await refreshFolders()
    return map
  }

  function reset() {
    items.value = []
    total.value = 0
    page.value = 1
    scope.value = 'mine'
    folderId.value = null
    tagId.value = null
    keyword.value = ''
  }

  return {
    items,
    total,
    page,
    pageSize,
    totalPages,
    loading,
    scope,
    folderId,
    tagId,
    keyword,
    isOfflineSnapshot,
    lastSyncedAt,
    load,
    setScope,
    setFolderFilter,
    setTagFilter,
    setKeyword,
    gotoPage,
    create,
    update,
    copy,
    remove,
    setTags,
    importFile,
    reset
  }
})
