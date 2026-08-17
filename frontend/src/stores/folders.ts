import { defineStore } from 'pinia'
import { ref } from 'vue'
import * as foldersApi from '@/api/folders'

export const useFoldersStore = defineStore('folders', () => {
  const tree = ref<foldersApi.FolderNode[]>([])
  const loaded = ref(false)
  const loading = ref(false)

  async function load(force = false) {
    if (loaded.value && !force) return
    loading.value = true
    try {
      tree.value = await foldersApi.fetchFolderTree()
      loaded.value = true
    } finally {
      loading.value = false
    }
  }

  async function create(payload: foldersApi.FolderCreatePayload) {
    const folder = await foldersApi.createFolder(payload)
    await load(true)
    return folder
  }

  async function update(id: string, payload: foldersApi.FolderUpdatePayload) {
    const folder = await foldersApi.updateFolder(id, payload)
    await load(true)
    return folder
  }

  async function move(id: string, parentId: string | null) {
    await foldersApi.moveFolder(id, parentId)
    await load(true)
  }

  async function remove(id: string) {
    await foldersApi.deleteFolder(id)
    await load(true)
  }

  function reset() {
    tree.value = []
    loaded.value = false
  }

  return { tree, loaded, loading, load, create, update, move, remove, reset }
})
