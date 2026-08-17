<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import {
  NButton,
  NCard,
  NEmpty,
  NIcon,
  NInput,
  NModal,
  NPagination,
  NSpace,
  NSpin,
  NTag,
  useMessage,
  useDialog
} from 'naive-ui'
import {
  AddOutline,
  CopyOutline,
  DocumentTextOutline,
  LockClosedOutline,
  SearchOutline,
  TrashOutline,
  GlobeOutline,
  CreateOutline
} from '@vicons/ionicons5'
import { useMindMapsStore } from '@/stores/mindmaps'
import { useTagsStore } from '@/stores/tags'
import { useFoldersStore } from '@/stores/folders'
import SidebarView from './home/SidebarView.vue'

const router = useRouter()
const message = useMessage()
const dialog = useDialog()
const mapsStore = useMindMapsStore()
const tagsStore = useTagsStore()
const foldersStore = useFoldersStore()

const keywordInput = ref('')

// 新建导图 Modal
const createModalVisible = ref(false)
const createTitleInput = ref('')
const createSubmitting = ref(false)

const titleText = computed(() =>
  mapsStore.scope === 'mine' ? '我的思维导图' : '公开广场'
)

const showFolderFilter = computed(
  () => mapsStore.scope === 'mine' && mapsStore.folderId
)

const currentFolderName = computed(() => {
  if (!mapsStore.folderId) return null
  function find(nodes: typeof foldersStore.tree): string | null {
    for (const n of nodes) {
      if (n.id === mapsStore.folderId) return n.name
      const sub = find(n.children)
      if (sub) return sub
    }
    return null
  }
  return find(foldersStore.tree)
})

async function onSearch() {
  await mapsStore.setKeyword(keywordInput.value)
}

function openCreateModal() {
  createTitleInput.value = ''
  createModalVisible.value = true
}

async function submitCreate(): Promise<boolean> {
  const title = createTitleInput.value.trim()
  if (!title) {
    message.warning('请输入导图标题')
    return false
  }
  createSubmitting.value = true
  try {
    await mapsStore.create({
      title,
      folderId: mapsStore.folderId,
      isPublic: false
    })
    message.success('已创建')
    createModalVisible.value = false
    return true
  } catch (e) {
    message.error((e as Error).message)
    return false
  } finally {
    createSubmitting.value = false
  }
}

async function onCopy(id: string) {
  try {
    await mapsStore.copy(id)
    message.success('已复制')
  } catch (e) {
    message.error((e as Error).message)
  }
}

function onEdit(id: string) {
  router.push({ name: 'mindmap-edit', params: { id } })
}

async function onTogglePublic(id: string, isPublic: boolean) {
  try {
    await mapsStore.update(id, { isPublic: !isPublic })
    message.success(isPublic ? '已设为私有' : '已公开')
  } catch (e) {
    message.error((e as Error).message)
  }
}

function onRemove(id: string, title: string) {
  dialog.warning({
    title: '确认删除',
    content: `删除「${title}」？该操作不可恢复。`,
    positiveText: '删除',
    negativeText: '取消',
    onPositiveClick: async () => {
      try {
        await mapsStore.remove(id)
        message.success('已删除')
      } catch (e) {
        message.error((e as Error).message)
      }
    }
  })
}

function formatTime(s: string): string {
  return new Date(s).toLocaleString('zh-CN', { hour12: false })
}

onMounted(async () => {
  if (mapsStore.scope === 'mine') {
    await Promise.all([
      tagsStore.load().catch(() => {}),
      foldersStore.load().catch(() => {})
    ])
  }
  if (!mapsStore.items.length && !mapsStore.loading) {
    await mapsStore.load().catch(() => {})
  }
})
</script>

<template>
  <div class="home">
    <!-- 侧边栏（仅「我的导图」时显示） -->
    <SidebarView v-if="mapsStore.scope === 'mine'" class="home-sidebar" />

    <!-- 主内容区 -->
    <div class="home-content">
    <div class="home-header">
      <div class="title-row">
        <h1 class="title">{{ titleText }}</h1>
        <NButton
          v-if="mapsStore.scope === 'mine'"
          type="primary"
          size="small"
          @click="openCreateModal"
        >
          <template #icon><NIcon><AddOutline /></NIcon></template>
          新建导图
        </NButton>
      </div>
      <p v-if="showFolderFilter" class="folder-filter">
        <span>文件夹：</span>
        <NTag size="small" type="info" closable @close="mapsStore.setFolderFilter(null)">
          {{ currentFolderName }}
        </NTag>
      </p>
    </div>

    <div class="search-bar">
      <NInput
        v-model:value="keywordInput"
        placeholder="搜索导图标题或描述"
        clearable
        @keyup.enter="onSearch"
        @clear="onSearch"
      >
        <template #prefix>
          <NIcon><SearchOutline /></NIcon>
        </template>
      </NInput>
      <NButton @click="onSearch">搜索</NButton>
    </div>

    <NSpin :show="mapsStore.loading">
      <div v-if="mapsStore.items.length === 0 && !mapsStore.loading" class="empty-wrap">
        <NEmpty description="暂无思维导图">
          <template v-if="mapsStore.scope === 'mine'" #extra>
            <NButton size="small" type="primary" @click="openCreateModal">
              立即创建
            </NButton>
          </template>
        </NEmpty>
      </div>

      <div v-else class="grid">
        <NCard
          v-for="map in mapsStore.items"
          :key="map.id"
          class="map-card"
          :title="map.title"
          size="small"
          hoverable
        >
          <template #header-extra>
            <NIcon v-if="map.isPublic" size="14" color="#18a058">
              <GlobeOutline />
            </NIcon>
            <NIcon v-else size="14" color="#999">
              <LockClosedOutline />
            </NIcon>
          </template>

          <div class="card-body">
            <p v-if="map.description" class="desc">{{ map.description }}</p>
            <p v-else class="desc muted">（无描述）</p>

            <div class="meta-row">
              <span class="meta-item">
                <NIcon size="12"><DocumentTextOutline /></NIcon>
                {{ map.nodeCount }} 节点
              </span>
              <span class="meta-item">{{ map.ownerName }}</span>
              <span class="meta-item muted">{{ formatTime(map.lastEditedAt) }}</span>
            </div>

            <div v-if="map.tags.length > 0" class="tag-row">
              <NTag
                v-for="t in map.tags"
                :key="t.id"
                size="tiny"
                :color="{ color: t.color, textColor: '#fff', borderColor: t.color }"
              >
                {{ t.name }}
              </NTag>
            </div>
            <div v-else-if="map.folderName" class="meta-row">
              <NTag size="tiny">{{ map.folderName }}</NTag>
            </div>
          </div>

          <template #action>
            <NSpace size="small" justify="end" align="center" :wrap="false">
              <NButton
                text
                size="small"
                type="primary"
                @click="onEdit(map.id)"
              >
                <template #icon><NIcon><CreateOutline /></NIcon></template>
                编辑
              </NButton>
              <NButton
                v-if="mapsStore.scope === 'mine'"
                text
                size="small"
                @click="onTogglePublic(map.id, map.isPublic)"
              >
                {{ map.isPublic ? '设私有' : '设公开' }}
              </NButton>
              <NButton
                v-if="mapsStore.scope === 'mine'"
                text
                size="small"
                @click="onCopy(map.id)"
              >
                <template #icon><NIcon><CopyOutline /></NIcon></template>
                复制
              </NButton>
              <NButton
                v-if="mapsStore.scope === 'mine'"
                text
                size="small"
                type="error"
                @click="onRemove(map.id, map.title)"
              >
                <template #icon><NIcon><TrashOutline /></NIcon></template>
                删除
              </NButton>
            </NSpace>
          </template>
        </NCard>
      </div>
    </NSpin>

    <div v-if="mapsStore.totalPages > 1" class="pager">
      <NPagination
        :page="mapsStore.page"
        :page-count="mapsStore.totalPages"
        :page-size="mapsStore.pageSize"
        :item-count="mapsStore.total"
        show-quick-jumper
        @update:page="mapsStore.gotoPage"
      />
    </div>

    <!-- 新建导图 Modal -->
    <NModal
      v-model:show="createModalVisible"
      preset="card"
      title="新建思维导图"
      display-directive="if"
      style="max-width: 420px"
    >
      <NInput
        v-model:value="createTitleInput"
        placeholder="请输入导图标题"
        maxlength="128"
        :autofocus="true"
        @keyup.enter="submitCreate"
      />
      <template #footer>
        <NSpace justify="end">
          <NButton @click="createModalVisible = false">取消</NButton>
          <NButton
            type="primary"
            :loading="createSubmitting"
            :disabled="!createTitleInput.trim()"
            @click="submitCreate"
          >
            创建
          </NButton>
        </NSpace>
      </template>
    </NModal>
    </div>
  </div>
</template>

<style scoped lang="scss">
.home {
  display: flex;
  padding: 16px;
  gap: 16px;
  max-width: 1400px;
  margin: 0 auto;
}

.home-sidebar {
  width: 240px;
  flex-shrink: 0;
  background: var(--app-card-bg, #fff);
  border-radius: 8px;
  border: 1px solid var(--app-border, #e0e0e6);
  overflow: hidden;
}

.home-content {
  flex: 1;
  min-width: 0;
}

.home-header {
  margin-bottom: 12px;
}

.title-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.title {
  font-size: 20px;
  font-weight: 600;
  margin: 0;
}

.folder-filter {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  color: var(--app-text-secondary);
  margin-top: 6px;
}

.search-bar {
  display: flex;
  gap: 8px;
  margin-bottom: 12px;
}

.empty-wrap {
  padding: 32px 0;
}

.grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
  gap: 12px;
}

.map-card {
  background: var(--app-card-bg);
  border-radius: 8px;
}

.card-body {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-height: 60px;
}

.desc {
  font-size: 13px;
  color: var(--app-text-primary);
  margin: 0;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;

  &.muted {
    color: var(--app-text-secondary);
  }
}

.meta-row {
  display: flex;
  align-items: center;
  gap: 12px;
  font-size: 12px;
  color: var(--app-text-secondary);
  flex-wrap: wrap;

  .meta-item {
    display: inline-flex;
    align-items: center;
    gap: 4px;

    &.muted {
      color: var(--app-text-tertiary, #aaa);
    }
  }
}

.tag-row {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.pager {
  margin-top: 16px;
  display: flex;
  justify-content: center;
}

@media (max-width: 767px) {
  .home {
    flex-direction: column;
    padding: 10px;
    gap: 10px;
  }
  .home-sidebar {
    width: 100%;
    max-height: 200px;
  }
  .grid {
    grid-template-columns: 1fr;
    gap: 10px;
  }
  .title {
    font-size: 18px;
  }
  .search-bar {
    flex-wrap: nowrap;
    gap: 6px;
    .n-button {
      flex: 0 0 auto;
    }
  }
  .map-card {
    :deep(.n-card__action) {
      padding: 8px 10px;
    }
  }
  .card-body .meta-row {
    gap: 8px;
    font-size: 11px;
  }
  .pager {
    :deep(.n-pagination) {
      .n-pagination-item {
        min-width: 32px;
        height: 32px;
      }
    }
  }
}
</style>
