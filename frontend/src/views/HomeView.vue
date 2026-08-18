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
  NSelect,
  NSpace,
  NSpin,
  NTag,
  useMessage
} from 'naive-ui'
import {
  AddOutline,
  CopyOutline,
  DocumentTextOutline,
  LockClosedOutline,
  SearchOutline,
  TrashOutline,
  GlobeOutline,
  CreateOutline,
  FlagOutline
} from '@vicons/ionicons5'
import { useMindMapsStore } from '@/stores/mindmaps'
import { useTagsStore } from '@/stores/tags'
import { useFoldersStore } from '@/stores/folders'
import { useAuthStore } from '@/stores/auth'
import { reportMindMap } from '@/api/admin'
import SidebarView from './home/SidebarView.vue'

const router = useRouter()
const message = useMessage()
const mapsStore = useMindMapsStore()
const tagsStore = useTagsStore()
const foldersStore = useFoldersStore()
const authStore = useAuthStore()

const keywordInput = ref('')

// 删除导图确认弹窗
const removeModalVisible = ref(false)
const removeTargetId = ref('')
const removeTargetTitle = ref('')
const removeSubmitting = ref(false)

// 新建导图 Modal
const createModalVisible = ref(false)
const createTitleInput = ref('')
const createSubmitting = ref(false)

// 举报 Modal
const reportModalVisible = ref(false)
const reportTargetId = ref<string | null>(null)
const reportTargetTitle = ref('')
const reportReason = ref('')
const reportSubmitting = ref(false)

// 移动到文件夹
const moveModalVisible = ref(false)
const moveTargetId = ref('')
const moveTargetTitle = ref('')
const moveTargetFolderId = ref<string | null>(null)
const moveSubmitting = ref(false)

function openReportModal(id: string, title: string): void {
  if (!authStore.isAuthenticated) {
    message.warning('请先登录后再举报')
    return
  }
  reportTargetId.value = id
  reportTargetTitle.value = title
  reportReason.value = ''
  reportModalVisible.value = true
}

async function submitReport(): Promise<void> {
  if (!reportTargetId.value) return
  const reason = reportReason.value.trim()
  if (!reason) {
    message.warning('请填写举报理由')
    return
  }
  reportSubmitting.value = true
  try {
    await reportMindMap(reportTargetId.value, reason)
    message.success('举报已提交，管理员将在后台审核')
    reportModalVisible.value = false
  } catch (e) {
    message.error((e as Error).message)
  } finally {
    reportSubmitting.value = false
  }
}

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
  removeTargetId.value = id
  removeTargetTitle.value = title
  removeModalVisible.value = true
}

async function submitRemove(): Promise<void> {
  if (!removeTargetId.value) return
  removeSubmitting.value = true
  try {
    await mapsStore.remove(removeTargetId.value)
    message.success('已删除')
    removeModalVisible.value = false
  } catch (e) {
    message.error((e as Error).message)
  } finally {
    removeSubmitting.value = false
  }
}

function formatTime(s: string): string {
  return new Date(s).toLocaleString('zh-CN', { hour12: false })
}

const folderOptions = computed<{ label: string; value: string | null }[]>(() => {
  const options: { label: string; value: string | null }[] = [
    { label: '根目录（不放入文件夹）', value: null }
  ]
  function walk(nodes: typeof foldersStore.tree, depth: number) {
    for (const n of nodes) {
      const prefix = ' '.repeat(depth)
      options.push({ label: `${prefix}📁 ${n.name}`, value: n.id })
      walk(n.children, depth + 1)
    }
  }
  walk(foldersStore.tree, 0)
  return options
})

function openMoveModal(id: string, title: string, currentFolderId: string | null): void {
  moveTargetId.value = id
  moveTargetTitle.value = title
  moveTargetFolderId.value = currentFolderId
  moveModalVisible.value = true
}

async function submitMove(): Promise<void> {
  if (!moveTargetId.value) return
  moveSubmitting.value = true
  try {
    await mapsStore.update(moveTargetId.value, { folderId: moveTargetFolderId.value })
    message.success('已移动')
    moveModalVisible.value = false
  } catch (e) {
    message.error((e as Error).message)
  } finally {
    moveSubmitting.value = false
  }
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
                @click="openMoveModal(map.id, map.title, map.folderId ?? null)"
              >
                移动
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
              <NButton
                v-if="mapsStore.scope === 'public' && !authStore.isAdmin"
                text
                size="small"
                type="warning"
                @click="openReportModal(map.id, map.title)"
              >
                <template #icon><NIcon><FlagOutline /></NIcon></template>
                举报
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

    <!-- 举报 Modal -->
    <NModal
      v-model:show="reportModalVisible"
      preset="card"
      title="举报导图"
      display-directive="if"
      style="max-width: 460px"
    >
      <p class="report-tip">举报导图「{{ reportTargetTitle }}」？请填写理由，管理员将审核处理。</p>
      <NInput
        v-model:value="reportReason"
        type="textarea"
        placeholder="请填写举报理由（如违规内容、侵权、广告等）"
        :rows="4"
        maxlength="512"
      />
      <template #footer>
        <NSpace justify="end">
          <NButton @click="reportModalVisible = false">取消</NButton>
          <NButton
            type="warning"
            :loading="reportSubmitting"
            :disabled="!reportReason.trim()"
            @click="submitReport"
          >
            提交举报
          </NButton>
        </NSpace>
      </template>
    </NModal>

    <!-- 移动到文件夹 Modal -->
    <NModal
      v-model:show="moveModalVisible"
      preset="card"
      title="移动到文件夹"
      display-directive="if"
      style="max-width: 420px"
    >
      <p class="move-tip">将「{{ moveTargetTitle }}」移动到：</p>
      <NSelect
        v-model:value="moveTargetFolderId"
        :options="folderOptions"
        placeholder="请选择目标文件夹"
        style="width: 100%"
        :loading="foldersStore.loading"
      />
      <template #footer>
        <NSpace justify="end">
          <NButton @click="moveModalVisible = false">取消</NButton>
          <NButton
            type="primary"
            :loading="moveSubmitting"
            @click="submitMove"
          >
            确认移动
          </NButton>
        </NSpace>
      </template>
    </NModal>

    <!-- 删除导图确认弹窗 -->
    <NModal
      v-model:show="removeModalVisible"
      preset="dialog"
      type="warning"
      title="确认删除"
      positive-text="删除"
      negative-text="取消"
      :positive-button-props="{ type: 'error', loading: removeSubmitting }"
      display-directive="if"
      style="max-width: 420px"
      @positive-click="submitRemove"
    >
      删除「{{ removeTargetTitle }}」？该操作不可恢复。
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

.report-tip {
  margin: 0 0 8px;
  font-size: 13px;
  color: var(--app-text-secondary);
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
