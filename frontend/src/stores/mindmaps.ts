import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import * as mapsApi from '@/api/mindmaps'
import type { MindMapListItem, MindMapListQuery } from '@/api/mindmaps'

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
    } finally {
      loading.value = false
    }
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
    return map
  }

  async function update(id: string, payload: mapsApi.MindMapUpdatePayload) {
    const map = await mapsApi.updateMindMap(id, payload)
    await load()
    return map
  }

  async function copy(id: string, newTitle?: string) {
    const map = await mapsApi.copyMindMap(id, newTitle)
    await load()
    return map
  }

  async function remove(id: string) {
    await mapsApi.deleteMindMap(id)
    await load()
  }

  async function setTags(id: string, tagIds: string[]) {
    await mapsApi.setMindMapTags(id, tagIds)
    await load()
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
    reset
  }
})
