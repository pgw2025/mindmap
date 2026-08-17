<script setup lang="ts">
import { ref, computed } from 'vue'
import {
  NButton,
  NIcon,
  NInput,
  NModal,
  NSpace,
  NTree,
  NPopconfirm,
  useMessage
} from 'naive-ui'
import {
  AddOutline,
  FolderOutline,
  CreateOutline,
  TrashOutline,
  PricetagOutline
} from '@vicons/ionicons5'
import { useFoldersStore } from '@/stores/folders'
import { useTagsStore } from '@/stores/tags'
import { useMindMapsStore } from '@/stores/mindmaps'
import type { TreeOption } from 'naive-ui'

const foldersStore = useFoldersStore()
const tagsStore = useTagsStore()
const mapsStore = useMindMapsStore()
const message = useMessage()

// ====================== 文件夹管理 ======================
const folderModalVisible = ref(false)
const folderModalMode = ref<'create' | 'edit'>('create')
const folderModalTitle = ref('')
const folderEditId = ref<string | null>(null)
const folderParentId = ref<string | null>(null)

// 文件夹树选项
const folderTreeOptions = computed<TreeOption[]>(() => {
  const root: TreeOption = {
    key: '__root__',
    label: '全部导图',
    children: buildFolderTree(foldersStore.tree)
  }
  return [root]
})

function buildFolderTree(nodes: typeof foldersStore.tree): TreeOption[] {
  return nodes.map((n) => ({
    key: n.id,
    label: `${n.name} (${n.mindMapCount})`,
    children: n.children.length > 0 ? buildFolderTree(n.children) : undefined
  }))
}

const selectedFolderKeys = computed(() => {
  if (mapsStore.folderId) return [mapsStore.folderId]
  return ['__root__']
})

function handleFolderSelect(keys: string[]) {
  const key = keys[0]
  if (key === '__root__' || !key) {
    mapsStore.setFolderFilter(null)
  } else {
    mapsStore.setFolderFilter(key)
  }
}

function getFolderName(id: string): string {
  function find(nodes: typeof foldersStore.tree): string | null {
    for (const n of nodes) {
      if (n.id === id) return n.name
      const sub = find(n.children)
      if (sub) return sub
    }
    return null
  }
  return find(foldersStore.tree) ?? ''
}

function openCreateFolder(parentId: string | null = null) {
  folderModalMode.value = 'create'
  folderEditId.value = null
  folderParentId.value = parentId
  folderModalTitle.value = ''
  folderModalVisible.value = true
}

function openEditFolder(id: string, name: string) {
  folderModalMode.value = 'edit'
  folderEditId.value = id
  folderParentId.value = null
  folderModalTitle.value = name
  folderModalVisible.value = true
}

async function submitFolder() {
  const name = folderModalTitle.value.trim()
  if (!name) {
    message.warning('请输入文件夹名称')
    return
  }
  try {
    if (folderModalMode.value === 'create') {
      await foldersStore.create({ name, parentId: folderParentId.value })
      message.success('已创建')
    } else if (folderEditId.value) {
      await foldersStore.update(folderEditId.value, { name })
      message.success('已更新')
    }
    folderModalVisible.value = false
  } catch (e) {
    message.error((e as Error).message)
  }
}

async function deleteFolder(id: string) {
  try {
    await foldersStore.remove(id)
    if (mapsStore.folderId === id) {
      mapsStore.setFolderFilter(null)
    }
    message.success('已删除')
  } catch (e) {
    message.error((e as Error).message)
  }
}

// ====================== 标签管理 ======================
const tagModalVisible = ref(false)
const tagModalMode = ref<'create' | 'edit'>('create')
const tagModalName = ref('')
const tagModalColor = ref('#18a058')
const tagEditId = ref<string | null>(null)

const presetColors = [
  '#18a058', '#2080f0', '#f0a020', '#d03050',
  '#7048e8', '#00b894', '#6c5ce7', '#fd79a8'
]

function openCreateTag() {
  tagModalMode.value = 'create'
  tagEditId.value = null
  tagModalName.value = ''
  tagModalColor.value = '#18a058'
  tagModalVisible.value = true
}

function openEditTag(id: string, name: string, color: string) {
  tagModalMode.value = 'edit'
  tagEditId.value = id
  tagModalName.value = name
  tagModalColor.value = color
  tagModalVisible.value = true
}

async function submitTag() {
  const name = tagModalName.value.trim()
  if (!name) {
    message.warning('请输入标签名称')
    return
  }
  try {
    if (tagModalMode.value === 'create') {
      await tagsStore.create({ name, color: tagModalColor.value })
      message.success('已创建')
    } else if (tagEditId.value) {
      await tagsStore.update(tagEditId.value, { name, color: tagModalColor.value })
      message.success('已更新')
    }
    tagModalVisible.value = false
  } catch (e) {
    message.error((e as Error).message)
  }
}

async function deleteTag(id: string) {
  try {
    await tagsStore.remove(id)
    if (mapsStore.tagId === id) {
      mapsStore.setTagFilter(null)
    }
    message.success('已删除')
  } catch (e) {
    message.error((e as Error).message)
  }
}

function toggleTagFilter(id: string) {
  if (mapsStore.tagId === id) {
    mapsStore.setTagFilter(null)
  } else {
    mapsStore.setTagFilter(id)
  }
}
</script>

<template>
  <aside class="sidebar">
    <!-- 文件夹树 -->
    <div class="sidebar-section">
      <div class="section-header">
        <span class="section-title">
          <NIcon size="14"><FolderOutline /></NIcon>
          文件夹
        </span>
        <NButton text size="tiny" @click="openCreateFolder(null)">
          <NIcon size="16"><AddOutline /></NIcon>
        </NButton>
      </div>
      <NTree
        :data="folderTreeOptions"
        :selected-keys="selectedFolderKeys"
        :default-expand-all="true"
        key-field="key"
        label-field="label"
        children-field="children"
        selectable
        block-line
        @update:selected-keys="handleFolderSelect"
      />
      <!-- 选中文件夹的操作按钮 -->
      <div v-if="mapsStore.folderId" class="folder-actions">
        <NButton text size="tiny" @click="openEditFolder(mapsStore.folderId, getFolderName(mapsStore.folderId))">
          <NIcon size="14"><CreateOutline /></NIcon>
          重命名
        </NButton>
        <NPopconfirm @positive-click="deleteFolder(mapsStore.folderId)">
          <template #trigger>
            <NButton text size="tiny" type="error">
              <NIcon size="14"><TrashOutline /></NIcon>
              删除
            </NButton>
          </template>
          删除文件夹？文件夹内导图将移至根目录。
        </NPopconfirm>
        <NButton text size="tiny" @click="openCreateFolder(mapsStore.folderId)">
          <NIcon size="14"><AddOutline /></NIcon>
          子文件夹
        </NButton>
      </div>
    </div>

    <div class="sidebar-divider"></div>

    <!-- 标签列表 -->
    <div class="sidebar-section">
      <div class="section-header">
        <span class="section-title">
          <NIcon size="14"><PricetagOutline /></NIcon>
          标签
        </span>
        <NButton text size="tiny" @click="openCreateTag">
          <NIcon size="16"><AddOutline /></NIcon>
        </NButton>
      </div>
      <div class="tag-list">
        <div
          v-for="tag in tagsStore.items"
          :key="tag.id"
          class="tag-item"
          :class="{ active: mapsStore.tagId === tag.id }"
          @click="toggleTagFilter(tag.id)"
        >
          <span class="tag-dot" :style="{ background: tag.color }"></span>
          <span class="tag-name">{{ tag.name }}</span>
          <span class="tag-count">{{ tag.mindMapCount }}</span>
          <button class="tag-edit" @click.stop="openEditTag(tag.id, tag.name, tag.color)">
            <NIcon size="12"><CreateOutline /></NIcon>
          </button>
          <NPopconfirm @positive-click="deleteTag(tag.id)">
            <template #trigger>
              <button class="tag-del" @click.stop>
                <NIcon size="12"><TrashOutline /></NIcon>
              </button>
            </template>
            确认删除标签「{{ tag.name }}」？
          </NPopconfirm>
        </div>
        <div v-if="tagsStore.items.length === 0" class="empty-hint">
          暂无标签
        </div>
      </div>
    </div>

    <!-- 文件夹 Modal -->
    <NModal
      v-model:show="folderModalVisible"
      preset="card"
      :title="folderModalMode === 'create' ? '新建文件夹' : '编辑文件夹'"
      display-directive="if"
      style="max-width: 380px"
    >
      <NInput
        v-model:value="folderModalTitle"
        placeholder="文件夹名称"
        maxlength="64"
        :autofocus="true"
        @keyup.enter="submitFolder"
      />
      <template #footer>
        <NSpace justify="end">
          <NButton @click="folderModalVisible = false">取消</NButton>
          <NButton type="primary" @click="submitFolder">
            {{ folderModalMode === 'create' ? '创建' : '保存' }}
          </NButton>
        </NSpace>
      </template>
    </NModal>

    <!-- 标签 Modal -->
    <NModal
      v-model:show="tagModalVisible"
      preset="card"
      :title="tagModalMode === 'create' ? '新建标签' : '编辑标签'"
      display-directive="if"
      style="max-width: 380px"
    >
      <div class="tag-form">
        <NInput
          v-model:value="tagModalName"
          placeholder="标签名称"
          maxlength="32"
          :autofocus="true"
          @keyup.enter="submitTag"
        />
        <div class="color-picker">
          <span class="picker-label">颜色</span>
          <div class="color-list">
            <button
              v-for="c in presetColors"
              :key="c"
              class="color-dot"
              :style="{ background: c }"
              :class="{ active: tagModalColor === c }"
              @click="tagModalColor = c"
            ></button>
          </div>
        </div>
      </div>
      <template #footer>
        <NSpace justify="end">
          <NButton @click="tagModalVisible = false">取消</NButton>
          <NButton type="primary" @click="submitTag">
            {{ tagModalMode === 'create' ? '创建' : '保存' }}
          </NButton>
        </NSpace>
      </template>
    </NModal>
  </aside>
</template>

<style scoped lang="scss">
.sidebar {
  display: flex;
  flex-direction: column;
  gap: 12px;
  height: 100%;
  overflow-y: auto;
  padding: 12px;
}

.sidebar-section {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.section-title {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 12px;
  font-weight: 600;
  color: var(--app-text-secondary, #666);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.sidebar-divider {
  height: 1px;
  background: var(--app-border, #e0e0e6);
}

.folder-actions {
  display: flex;
  gap: 8px;
  padding: 4px 0;
  flex-wrap: wrap;
}

.tag-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.tag-item {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 8px;
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.2s;
  font-size: 13px;

  &:hover {
    background: var(--app-hover-bg, #f0f0f0);
  }

  &.active {
    background: rgba(24, 160, 88, 0.1);
  }
}

.tag-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  flex-shrink: 0;
}

.tag-name {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.tag-count {
  font-size: 11px;
  color: var(--app-text-tertiary, #999);
}

.tag-edit, .tag-del {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  border: none;
  background: transparent;
  border-radius: 4px;
  cursor: pointer;
  color: var(--app-text-tertiary, #999);
  opacity: 0;
  transition: all 0.2s;

  &:hover {
    background: var(--app-hover-bg, #e0e0e0);
    color: var(--app-text-primary, #333);
  }
}

.tag-item:hover .tag-edit,
.tag-item:hover .tag-del {
  opacity: 1;
}

.tag-del:hover {
  color: #d03050 !important;
}

.empty-hint {
  font-size: 12px;
  color: var(--app-text-tertiary, #999);
  padding: 8px;
  text-align: center;
}

.tag-form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.color-picker {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.picker-label {
  font-size: 12px;
  color: var(--app-text-secondary, #666);
}

.color-list {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.color-dot {
  width: 24px;
  height: 24px;
  border-radius: 50%;
  border: 2px solid transparent;
  cursor: pointer;
  transition: all 0.2s;

  &:hover {
    transform: scale(1.15);
  }

  &.active {
    border-color: var(--app-primary, #18a058);
    box-shadow: 0 0 0 2px rgba(24, 160, 88, 0.3);
  }
}
</style>
