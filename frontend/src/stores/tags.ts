import { defineStore } from 'pinia'
import { ref } from 'vue'
import * as tagsApi from '@/api/tags'

export const useTagsStore = defineStore('tags', () => {
  const items = ref<tagsApi.TagDto[]>([])
  const loaded = ref(false)
  const loading = ref(false)

  async function load(force = false) {
    if (loaded.value && !force) return
    loading.value = true
    try {
      items.value = await tagsApi.fetchTags()
      loaded.value = true
    } finally {
      loading.value = false
    }
  }

  async function create(payload: tagsApi.TagCreatePayload) {
    const tag = await tagsApi.createTag(payload)
    await load(true)
    return tag
  }

  async function update(id: string, payload: tagsApi.TagUpdatePayload) {
    const tag = await tagsApi.updateTag(id, payload)
    await load(true)
    return tag
  }

  async function remove(id: string) {
    await tagsApi.deleteTag(id)
    await load(true)
  }

  function reset() {
    items.value = []
    loaded.value = false
  }

  return { items, loaded, loading, load, create, update, remove, reset }
})
