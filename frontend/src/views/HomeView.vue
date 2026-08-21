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
  CloudUploadOutline,
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
import { useTemplatesStore } from '@/stores/templates'
import { parseSwatch } from '@/api/templates'
import { reportMindMap } from '@/api/admin'
import { THEMES } from '@/themes/presets'
import SidebarView from './home/SidebarView.vue'

const router = useRouter()
const message = useMessage()
const mapsStore = useMindMapsStore()
const tagsStore = useTagsStore()
const foldersStore = useFoldersStore()
const authStore = useAuthStore()
const templatesStore = useTemplatesStore()

const keywordInput = ref('')

// 删除导图确认弹窗
const removeModalVisible = ref(false)
const removeTargetId = ref('')
const removeTargetTitle = ref('')
const removeSubmitting = ref(false)

// 新建导图 Modal
const createModalVisible = ref(false)
const createTitleInput = ref('')
const createThemeId = ref('classic')
const createTemplateId = ref<string | null>(null)
const createSubmitting = ref(false)

// 导入导图 Modal
const importModalVisible = ref(false)
const importTitleInput = ref('')
const importThemeId = ref('classic')
const importFolderId = ref<string | null>(null)
const importFile = ref<File | null>(null)
const importFileDragOver = ref(false)
const importSubmitting = ref(false)
const importFileInputEl = ref<HTMLInputElement | null>(null)

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

// 编辑标签
const tagsModalVisible = ref(false)
const tagsTargetId = ref('')
const tagsTargetTitle = ref('')
const tagsSelectedIds = ref<string[]>([])
const tagsSubmitting = ref(false)
const newTagName = ref('')
const newTagColor = ref('#18a058')
const creatingTag = ref(false)

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
  createThemeId.value = 'classic'
  createTemplateId.value = null
  createModalVisible.value = true
  // 懒加载启用的模板列表
  templatesStore.loadEnabled().catch(() => { /* ignore */ })
}

async function submitCreate(): Promise<boolean> {
  const title = createTitleInput.value.trim()
  if (!title) {
    message.warning('请输入导图标题')
    return false
  }
  createSubmitting.value = true
  try {
    // 选择了模板则优先用模板（含初始结构 + 完整样式），否则用主题
    const payload: Parameters<typeof mapsStore.create>[0] = {
      title,
      folderId: mapsStore.folderId,
      isPublic: false
    }
    if (createTemplateId.value) {
      payload.templateId = createTemplateId.value
    } else {
      payload.theme = createThemeId.value
    }
    await mapsStore.create(payload)
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

// ---------- 导入导图 ----------
function openImportModal() {
  importModalVisible.value = true
  importTitleInput.value = ''
  importThemeId.value = 'classic'
  importFolderId.value = mapsStore.folderId
  importFile.value = null
  importFileDragOver.value = false
  importSubmitting.value = false
  if (importFileInputEl.value) importFileInputEl.value.value = ''
  // 懒加载模板列表
  templatesStore.loadEnabled().catch(() => { /* ignore */ })
}

function handleImportFileInputChange(e: Event): void {
  const t = e.target as HTMLInputElement
  const file = t.files?.[0] ?? null
  setImportFile(file)
}

function handleImportDrop(e: DragEvent): void {
  e.preventDefault()
  importFileDragOver.value = false
  const file = e.dataTransfer?.files?.[0] ?? null
  setImportFile(file)
}

function setImportFile(file: File | null): void {
  if (!file) {
    importFile.value = null
    importTitleInput.value = ''
    return
  }
  // 大小校验
  if (file.size > 5 * 1024 * 1024) {
    message.warning('文件超过 5MB 上限')
    return
  }
  const ext = (file.name.split('.').pop() ?? '').toLowerCase()
  const allowed = new Set(['mm', 'json', 'smm', 'md', 'markdown', 'xmind'])
  if (!allowed.has(ext)) {
    message.warning('暂不支持的文件格式，支持 .mm / .json / .smm / .md / .xmind')
    return
  }
  importFile.value = file
  // 默认标题 = 文件名去扩展名
  const base = file.name.replace(/\.[^.]+$/, '')
  if (!importTitleInput.value.trim()) {
    importTitleInput.value = base
  }
}

function removeImportFile(): void {
  importFile.value = null
  if (importFileInputEl.value) importFileInputEl.value.value = ''
}

async function submitImport(): Promise<boolean> {
  if (!importFile.value) {
    message.warning('请选择要导入的文件')
    return false
  }
  const title = importTitleInput.value.trim()
  if (!title) {
    message.warning('请输入导图标题')
    return false
  }
  importSubmitting.value = true
  try {
    const map = await mapsStore.importFile({
      file: importFile.value,
      title,
      folderId: importFolderId.value,
      theme: importThemeId.value,
      defaultLayout: 0
    })
    message.success(`导入成功，共 ${map.nodeCount} 个节点`)
    importModalVisible.value = false
    // 跳转到编辑器
    router.push({ name: 'mindmap-edit', params: { id: map.id } })
    return true
  } catch (e) {
    message.error((e as Error).message)
    return false
  } finally {
    importSubmitting.value = false
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

function onCardClick(id: string) {
  router.push({ name: 'mindmap-preview', params: { id } })
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

async function submitRemove(): Promise<boolean> {
  if (!removeTargetId.value) return true
  removeSubmitting.value = true
  try {
    await mapsStore.remove(removeTargetId.value)
    message.success('已删除')
    removeModalVisible.value = false
    return true
  } catch (e) {
    message.error((e as Error).message)
    return false
  } finally {
    removeSubmitting.value = false
  }
}

function formatTime(s: string): string {
  return new Date(s).toLocaleString('zh-CN', { hour12: false })
}

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(2)} MB`
}

const folderOptions = computed(() => {
  const options: { label: string; value: any }[] = [
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

const tagSelectOptions = computed(() =>
  tagsStore.items.map(t => ({ label: t.name, value: t.id }))
)

function openTagsModal(id: string, title: string, currentTags: { id: string }[]): void {
  tagsTargetId.value = id
  tagsTargetTitle.value = title
  tagsSelectedIds.value = currentTags.map(t => t.id)
  newTagName.value = ''
  newTagColor.value = '#18a058'
  tagsModalVisible.value = true
}

async function handleCreateNewTag(): Promise<void> {
  const name = newTagName.value.trim()
  if (!name) {
    message.warning('请输入标签名称')
    return
  }
  creatingTag.value = true
  try {
    const tag = await tagsStore.create({ name, color: newTagColor.value })
    message.success(`标签「${tag.name}」已创建`)
    newTagName.value = ''
    tagsSelectedIds.value.push(tag.id)
  } catch (e) {
    message.error((e as Error).message)
  } finally {
    creatingTag.value = false
  }
}

async function submitTags(): Promise<void> {
  if (!tagsTargetId.value) return
  tagsSubmitting.value = true
  try {
    await mapsStore.setTags(tagsTargetId.value, tagsSelectedIds.value)
    message.success('标签已更新')
    tagsModalVisible.value = false
  } catch (e) {
    message.error((e as Error).message)
  } finally {
    tagsSubmitting.value = false
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
        <NSpace v-if="mapsStore.scope === 'mine'" size="small" :wrap="false">
          <NButton
            type="primary"
            size="small"
            @click="openCreateModal"
          >
            <template #icon><NIcon><AddOutline /></NIcon></template>
            新建导图
          </NButton>
          <NButton
            size="small"
            @click="openImportModal"
          >
            <template #icon><NIcon><CloudUploadOutline /></NIcon></template>
            导入导图
          </NButton>
        </NSpace>
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
          class="map-card map-card-clickable"
          :title="map.title"
          size="small"
          hoverable
          @click="onCardClick(map.id)"
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
            <NSpace size="small" justify="end" align="center" :wrap="true">
              <NButton
                text
                size="small"
                type="primary"
                @click.stop="onEdit(map.id)"
              >
                <template #icon><NIcon><CreateOutline /></NIcon></template>
                编辑
              </NButton>
              <NButton
                v-if="mapsStore.scope === 'mine'"
                text
                size="small"
                @click.stop="onTogglePublic(map.id, map.isPublic)"
              >
                {{ map.isPublic ? '设私有' : '设公开' }}
              </NButton>
              <NButton
                v-if="mapsStore.scope === 'mine'"
                text
                size="small"
                @click.stop="openMoveModal(map.id, map.title, map.folderId ?? null)"
              >
                移动
              </NButton>
              <NButton
                v-if="mapsStore.scope === 'mine'"
                text
                size="small"
                @click.stop="openTagsModal(map.id, map.title, map.tags)"
              >
                标签
              </NButton>
              <NButton
                v-if="mapsStore.scope === 'mine'"
                text
                size="small"
                @click.stop="onCopy(map.id)"
              >
                <template #icon><NIcon><CopyOutline /></NIcon></template>
                复制
              </NButton>
              <NButton
                v-if="mapsStore.scope === 'mine'"
                text
                size="small"
                type="error"
                @click.stop="onRemove(map.id, map.title)"
              >
                <template #icon><NIcon><TrashOutline /></NIcon></template>
                删除
              </NButton>
              <NButton
                v-if="mapsStore.scope === 'public' && !authStore.isAdmin"
                text
                size="small"
                type="warning"
                @click.stop="openReportModal(map.id, map.title)"
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
      style="max-width: 520px"
    >
      <div class="create-form">
        <div class="create-field">
          <label class="create-label">标题</label>
          <NInput
            v-model:value="createTitleInput"
            placeholder="请输入导图标题"
            maxlength="128"
            :autofocus="true"
            @keyup.enter="submitCreate"
          />
        </div>
        <div v-if="templatesStore.enabledList.length > 0" class="create-field">
          <label class="create-label">
            选择模板（含初始结构 + 样式，优先于主题）
            <span
              v-if="createTemplateId"
              class="clear-template"
              @click="createTemplateId = null"
            >不使用模板</span>
          </label>
          <div class="theme-grid">
            <div
              v-for="tpl in templatesStore.enabledList"
              :key="tpl.id"
              class="theme-card"
              :class="{ 'is-active': createTemplateId === tpl.id }"
              @click="createTemplateId = tpl.id"
            >
              <div
                class="theme-preview"
                :style="{ background: (parseSwatch(tpl.swatchJson)?.bg ?? '#fafafa') }"
              >
                <div
                  class="preview-root"
                  :style="{
                    background: parseSwatch(tpl.swatchJson)?.rootFill ?? '#549688',
                    borderColor: parseSwatch(tpl.swatchJson)?.rootFill ?? '#549688'
                  }"
                ></div>
                <div
                  class="preview-line"
                  :style="{ background: parseSwatch(tpl.swatchJson)?.lineColor ?? '#549688' }"
                ></div>
                <div
                  class="preview-second"
                  :style="{
                    background: parseSwatch(tpl.swatchJson)?.secondFill ?? '#fff',
                    borderColor: parseSwatch(tpl.swatchJson)?.lineColor ?? '#549688'
                  }"
                ></div>
              </div>
              <div class="theme-name">{{ tpl.name }}</div>
            </div>
          </div>
        </div>
        <div class="create-field" :class="{ 'is-dimmed': !!createTemplateId }">
          <label class="create-label">
            选择主题
            <span v-if="createTemplateId" class="theme-hint">（已使用模板，主题被覆盖）</span>
          </label>
          <div class="theme-grid">
            <div
              v-for="t in THEMES"
              :key="t.id"
              class="theme-card"
              :class="{ 'is-active': !createTemplateId && createThemeId === t.id }"
              @click="createTemplateId = null; createThemeId = t.id"
            >
              <div class="theme-preview" :style="{ background: t.swatch.bg }">
                <div class="preview-root" :style="{ background: t.swatch.rootFill, borderColor: t.swatch.rootFill }"></div>
                <div class="preview-line" :style="{ background: t.swatch.lineColor }"></div>
                <div class="preview-second" :style="{ background: t.swatch.secondFill, borderColor: t.swatch.lineColor }"></div>
              </div>
              <div class="theme-name">{{ t.name }}</div>
            </div>
          </div>
        </div>
      </div>
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

    <!-- 导入导图 Modal -->
    <NModal
      v-model:show="importModalVisible"
      preset="card"
      title="导入思维导图"
      display-directive="if"
      style="max-width: 560px"
    >
      <div class="create-form">
        <!-- 文件拖放区域 -->
        <div class="create-field">
          <label class="create-label">选择文件</label>
          <div
            class="import-dropzone"
            :class="{ 'is-dragover': importFileDragOver, 'is-filled': !!importFile }"
            @dragover.prevent="importFileDragOver = true"
            @dragleave="importFileDragOver = false"
            @drop="handleImportDrop"
            @click="importFileInputEl?.click()"
          >
            <input
              ref="importFileInputEl"
              type="file"
              accept=".mm,.json,.smm,.md,.markdown,.xmind"
              style="display: none"
              @change="handleImportFileInputChange"
            />
            <template v-if="!importFile">
              <div class="dropzone-icon">📥</div>
              <div class="dropzone-text">
                <div class="dropzone-title">点击选择文件，或拖拽文件到此处</div>
                <div class="dropzone-hint">
                  支持：FreeMind (.mm)、simple-mind-map (.json/.smm)、Markdown (.md)、XMind (.xmind)，单文件 ≤ 5MB
                </div>
              </div>
            </template>
            <template v-else>
              <div class="dropzone-filled">
                <div class="file-info">
                  <div class="file-icon">📄</div>
                  <div class="file-meta">
                    <div class="file-name">{{ importFile.name }}</div>
                    <div class="file-size">{{ formatFileSize(importFile.size) }}</div>
                  </div>
                </div>
                <NButton
                  text
                  size="small"
                  type="error"
                  @click.stop="removeImportFile"
                >
                  移除
                </NButton>
              </div>
            </template>
          </div>
        </div>

        <!-- 标题 -->
        <div class="create-field">
          <label class="create-label">导图标题</label>
          <NInput
            v-model:value="importTitleInput"
            placeholder="请输入导图标题（默认取文件名）"
            maxlength="128"
            :autofocus="!!importFile"
            @keyup.enter="submitImport"
          />
        </div>

        <!-- 文件夹 -->
        <div class="create-field">
          <label class="create-label">保存到文件夹</label>
          <NSelect
            v-model:value="importFolderId"
            :options="folderOptions"
            placeholder="选择目标文件夹（可选）"
            style="width: 100%"
            clearable
            :loading="foldersStore.loading"
          />
        </div>

        <!-- 主题 -->
        <div class="create-field">
          <label class="create-label">选择主题</label>
          <div class="theme-grid">
            <div
              v-for="t in THEMES"
              :key="t.id"
              class="theme-card"
              :class="{ 'is-active': importThemeId === t.id }"
              @click="importThemeId = t.id"
            >
              <div class="theme-preview" :style="{ background: t.swatch.bg }">
                <div class="preview-root" :style="{ background: t.swatch.rootFill, borderColor: t.swatch.rootFill }"></div>
                <div class="preview-line" :style="{ background: t.swatch.lineColor }"></div>
                <div class="preview-second" :style="{ background: t.swatch.secondFill, borderColor: t.swatch.lineColor }"></div>
              </div>
              <div class="theme-name">{{ t.name }}</div>
            </div>
          </div>
        </div>
      </div>
      <template #footer>
        <NSpace justify="end">
          <NButton @click="importModalVisible = false">取消</NButton>
          <NButton
            type="primary"
            :loading="importSubmitting"
            :disabled="!importFile || !importTitleInput.trim()"
            @click="submitImport"
          >
            <template #icon><NIcon><CloudUploadOutline /></NIcon></template>
            开始导入
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

    <!-- 编辑标签 Modal -->
    <NModal
      v-model:show="tagsModalVisible"
      preset="card"
      title="编辑标签"
      display-directive="if"
      style="max-width: 460px"
    >
      <p class="tags-tip">为「{{ tagsTargetTitle }}」选择标签（可多选）：</p>
      <NSelect
        v-model:value="tagsSelectedIds"
        :options="tagSelectOptions"
        multiple
        placeholder="选择已有标签"
        style="width: 100%"
        :loading="tagsStore.loading"
      />
      <div class="tags-create-row">
        <NInput
          v-model:value="newTagName"
          placeholder="新建标签名称"
          maxlength="20"
        />
        <NButton
          type="primary"
          size="small"
          :loading="creatingTag"
          :disabled="!newTagName.trim()"
          @click="handleCreateNewTag"
        >
          + 新建
        </NButton>
      </div>
      <template #footer>
        <NSpace justify="end">
          <NButton @click="tagsModalVisible = false">取消</NButton>
          <NButton
            type="primary"
            :loading="tagsSubmitting"
            @click="submitTags"
          >
            确认
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
      preset="card"
      title="确认删除"
      style="max-width: 420px"
      :bordered="false"
      size="medium"
    >
      <div style="font-size: 14px; color: #334155; line-height: 1.6;">
        删除「<b>{{ removeTargetTitle }}</b>」？该操作不可恢复。
      </div>
      <template #footer>
        <div style="display: flex; justify-content: flex-end; gap: 10px;">
          <NButton size="small" @click="removeModalVisible = false">
            取消
          </NButton>
          <NButton type="error" size="small" :loading="removeSubmitting" @click="submitRemove">
            删除
          </NButton>
        </div>
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

.map-card-clickable {
  cursor: pointer;
  transition: transform 0.15s ease, box-shadow 0.15s ease;
}
.map-card-clickable:hover {
  transform: translateY(-2px);
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
  max-height: 48px;
  overflow-y: auto;
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

.tags-tip {
  margin: 0 0 10px;
  font-size: 13px;
  color: var(--app-text-secondary);
}

.tags-create-row {
  display: flex;
  gap: 8px;
  margin-top: 12px;
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

.create-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.create-field {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.create-label {
  font-size: 13px;
  color: var(--app-text-secondary, #666);
  font-weight: 500;
}

.theme-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 10px;
}

.theme-card {
  cursor: pointer;
  border: 2px solid transparent;
  border-radius: 8px;
  padding: 6px;
  transition: all 0.2s;

  &:hover {
    border-color: var(--app-primary, #18a058);
  }

  &.is-active {
    border-color: var(--app-primary, #18a058);
    background: rgba(24, 160, 88, 0.06);
  }
}

.theme-preview {
  height: 60px;
  border-radius: 6px;
  position: relative;
  overflow: hidden;
  display: flex;
  align-items: center;
  padding: 0 10px;
  gap: 6px;
}

.preview-root {
  width: 20px;
  height: 20px;
  border-radius: 4px;
  border: 1px solid;
  flex-shrink: 0;
}

.preview-line {
  flex: 1;
  height: 2px;
  border-radius: 1px;
}

.preview-second {
  width: 24px;
  height: 14px;
  border-radius: 3px;
  border: 1px solid;
  flex-shrink: 0;
}

.theme-name {
  text-align: center;
  font-size: 12px;
  color: var(--app-text-primary, #333);
  margin-top: 6px;
}

.clear-template {
  float: right;
  font-size: 12px;
  color: var(--app-primary, #18a058);
  cursor: pointer;
  font-weight: normal;
}

.theme-hint {
  font-size: 12px;
  color: var(--app-text-secondary, #999);
  font-weight: normal;
}

.create-field.is-dimmed {
  opacity: 0.5;
  pointer-events: none;
}

/* ---------- 导入拖放区 ---------- */
.import-dropzone {
  border: 2px dashed var(--app-border, #d9d9d9);
  border-radius: 10px;
  padding: 24px 16px;
  cursor: pointer;
  transition: all 0.2s ease;
  background: var(--app-bg, #fafafa);
  user-select: none;

  &:hover {
    border-color: var(--app-primary, #18a058);
    background: rgba(24, 160, 88, 0.04);
  }

  &.is-dragover {
    border-color: var(--app-primary, #18a058);
    background: rgba(24, 160, 88, 0.1);
    transform: scale(1.01);
  }

  &.is-filled {
    border-color: var(--app-primary, #18a058);
    background: #fff;
    cursor: default;
  }

  .dropzone-icon {
    font-size: 36px;
    text-align: center;
    margin-bottom: 10px;
  }

  .dropzone-text {
    text-align: center;
    .dropzone-title {
      font-size: 14px;
      font-weight: 500;
      color: var(--app-text-primary, #333);
      margin-bottom: 4px;
    }
    .dropzone-hint {
      font-size: 12px;
      color: var(--app-text-secondary, #999);
      line-height: 1.6;
    }
  }

  .dropzone-filled {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    .file-info {
      display: flex;
      align-items: center;
      gap: 12px;
      min-width: 0;
      .file-icon {
        font-size: 28px;
        flex-shrink: 0;
      }
      .file-meta {
        min-width: 0;
        .file-name {
          font-size: 14px;
          font-weight: 500;
          color: var(--app-text-primary, #333);
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
        }
        .file-size {
          font-size: 12px;
          color: var(--app-text-secondary, #999);
          margin-top: 2px;
        }
      }
    }
  }
}

@media (max-width: 767px) {
  .import-dropzone {
    padding: 18px 12px;
    .dropzone-icon {
      font-size: 28px;
    }
  }
}
</style>
