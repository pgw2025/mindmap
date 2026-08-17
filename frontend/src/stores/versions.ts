import { defineStore } from 'pinia'
import {
  createVersion,
  deleteVersion,
  listVersions,
  rollbackVersion,
  type CreateVersionRequest,
  type MindMapVersionDto
} from '@/api/versions'

interface VersionState {
  items: MindMapVersionDto[]
  loading: boolean
  loaded: boolean
  mindMapId: string | null
}

export const useVersionsStore = defineStore('versions', {
  state: (): VersionState => ({
    items: [],
    loading: false,
    loaded: false,
    mindMapId: null
  }),
  actions: {
    async load(mindMapId: string, force = false) {
      if (this.loading) return
      if (!force && this.loaded && this.mindMapId === mindMapId) return
      this.mindMapId = mindMapId
      this.loading = true
      try {
        const data = await listVersions(mindMapId)
        this.items = (data ?? []) as MindMapVersionDto[]
        this.loaded = true
      } finally {
        this.loading = false
      }
    },

    async create(mindMapId: string, req: CreateVersionRequest) {
      const data = await createVersion(mindMapId, req)
      this.items = [data, ...this.items].sort((a, b) => b.versionNumber - a.versionNumber)
      this.loaded = true
      this.mindMapId = mindMapId
      return data
    },

    async rollback(mindMapId: string, versionId: string) {
      await rollbackVersion(mindMapId, versionId)
    },

    async remove(mindMapId: string, versionId: string) {
      await deleteVersion(mindMapId, versionId)
      this.items = this.items.filter((v) => v.id !== versionId)
    },

    clear() {
      this.items = []
      this.loaded = false
      this.mindMapId = null
    }
  }
})
