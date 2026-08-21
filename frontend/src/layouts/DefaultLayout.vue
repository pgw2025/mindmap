<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import {
  NButton,
  NDrawer,
  NDrawerContent,
  NEmpty,
  NIcon,
  NInput,
  NLayout,
  NLayoutContent,
  NLayoutHeader,
  NLayoutSider,
  NModal,
  NPopconfirm,
  NSpace,
  NSpin,
  NTree,
  NDivider,
  NTooltip,
  useMessage,
  type TreeOption
} from 'naive-ui'
import {
  AddOutline,
  FolderOpenOutline,
  FolderOutline,
  MenuOutline,
  MoonOutline,
  SunnyOutline,
  CloudOutline,
  ShieldCheckmarkOutline,
  LogOutOutline,
  CreateOutline,
  TrashOutline,
  PricetagOutline
} from '@vicons/ionicons5'
import { useThemeStore } from '@/stores/theme'
import { useAuthStore } from '@/stores/auth'
import { useFoldersStore } from '@/stores/folders'
import { useMindMapsStore } from '@/stores/mindmaps'
import { useTagsStore } from '@/stores/tags'

const themeStore = useThemeStore()
const authStore = useAuthStore()
const foldersStore = useFoldersStore()
const tagsStore = useTagsStore()
const mapsStore = useMindMapsStore()
const message = useMessage()
const router = useRouter()

const isAdmin = computed(() => authStore.isAdmin)

function goAdmin(): void {
  router.push({ name: 'admin-dashboard' })
}

const collapsed = ref(false)
const drawerVisible = ref(false)

function toggleSider() {
  if (window.innerWidth < 768) {
    drawerVisible.value = true
  } else {
    collapsed.value = !collapsed.value
  }
}

async function logout() {
  await authStore.logout()
  foldersStore.reset()
  tagsStore.reset()
  mapsStore.reset()
  location.href = '/login'
}

// ====================== 文件夹管理 ======================
const folderModalVisible = ref(false)
const folderModalMode = ref<'create' | 'edit'>('create')
const folderModalTitle = ref('')
const folderEditId = ref<string | null>(null)
const folderParentId = ref<string | null>(null)

// 文件夹树选项（含"全部导图"根节点）
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

const username = computed(() => authStore.user?.username ?? '用户')

onMounted(async () => {
  if (authStore.isAuthenticated) {
    await Promise.all([foldersStore.load(), tagsStore.load(), mapsStore.load()])
  }
})
</script>

<template>
  <NLayout position="absolute">
    <NLayoutHeader bordered class="app-header">
      <div class="left">
        <NButton text class="menu-btn" @click="toggleSider">
          <template #icon>
            <NIcon size="22">
              <MenuOutline />
            </NIcon>
          </template>
        </NButton>
        <span class="brand">思维导图</span>
      </div>
      <div class="right">
        <span class="username">{{ username }}</span>
        <NDivider vertical class="header-divider" />
        <NTooltip trigger="hover">
          <template #trigger>
            <NButton quaternary circle size="small" class="action-icon-btn" @click="themeStore.toggle">
              <template #icon>
                <NIcon size="18">
                  <MoonOutline v-if="!themeStore.isDark" />
                  <SunnyOutline v-else />
                </NIcon>
              </template>
            </NButton>
          </template>
          {{ themeStore.isDark ? '切换为明亮模式' : '切换为暗黑模式' }}
        </NTooltip>
        <NButton quaternary size="small" class="logout-btn" @click="logout">
          <template #icon>
            <NIcon size="16">
              <LogOutOutline />
            </NIcon>
          </template>
          退出
        </NButton>
      </div>
    </NLayoutHeader>

    <NLayout has-sider position="absolute" class="app-body">
      <!-- 桌面/平板侧边栏 -->
      <NLayoutSider bordered :collapsed="collapsed" :collapsed-width="0" :width="260" collapse-mode="width"
        :native-scrollbar="true" class="app-sider-desktop">
        <div class="sider-inner">
          <div class="sider-section">
            <div class="sider-title">
              <span>视图</span>
            </div>
            <NButton quaternary block :type="mapsStore.scope === 'mine' ? 'primary' : 'default'"
              @click="mapsStore.setScope('mine')">
              <template #icon>
                <NIcon>
                  <FolderOpenOutline />
                </NIcon>
              </template>
              我的导图
            </NButton>
            <NButton quaternary block :type="mapsStore.scope === 'public' ? 'primary' : 'default'"
              @click="mapsStore.setScope('public')">
              <template #icon>
                <NIcon>
                  <CloudOutline />
                </NIcon>
              </template>
              公开广场
            </NButton>
            <NButton v-if="isAdmin" quaternary block type="error" @click="goAdmin">
              <template #icon>
                <NIcon>
                  <ShieldCheckmarkOutline />
                </NIcon>
              </template>
              管理后台
            </NButton>
          </div>

          <template v-if="mapsStore.scope === 'mine'">
            <div class="sider-divider"></div>

            <!-- 文件夹 -->
            <div class="sider-section">
              <div class="sider-title">
                <span class="section-title">
                  <NIcon size="14"><FolderOutline /></NIcon>
                  文件夹
                </span>
                <NButton text size="tiny" @click="openCreateFolder(null)">
                  <NIcon size="16"><AddOutline /></NIcon>
                </NButton>
              </div>
              <NSpin :show="foldersStore.loading" size="small">
                <NTree
                  v-if="folderTreeOptions[0]?.children?.length || folderTreeOptions.length"
                  :data="folderTreeOptions"
                  :selected-keys="selectedFolderKeys"
                  :default-expand-all="true"
                  key-field="key"
                  label-field="label"
                  children-field="children"
                  selectable
                  block-line
                  @update:selected-keys="handleFolderSelect"
                  class="folder-tree"
                />
                <NEmpty v-else description="暂无文件夹" size="small" />
              </NSpin>
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

            <div class="sider-divider"></div>

            <!-- 标签 -->
            <div class="sider-section">
              <div class="sider-title">
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
                <div v-if="tagsStore.items.length === 0 && !tagsStore.loading" class="empty-hint">
                  暂无标签
                </div>
              </div>
            </div>
          </template>
        </div>
      </NLayoutSider>

      <!-- 移动端抽屉侧边栏 -->
      <NDrawer v-if="drawerVisible" v-model:show="drawerVisible" :width="280" placement="left">
        <NDrawerContent :title="'侧边栏'" :native-scrollbar="true">
          <div class="sider-inner">
            <div class="sider-section">
              <NButton quaternary block :type="mapsStore.scope === 'mine' ? 'primary' : 'default'"
                @click="mapsStore.setScope('mine'); drawerVisible = false">
                我的导图
              </NButton>
              <NButton quaternary block :type="mapsStore.scope === 'public' ? 'primary' : 'default'"
                @click="mapsStore.setScope('public'); drawerVisible = false">
                公开广场
              </NButton>
              <NButton v-if="isAdmin" quaternary block type="error" @click="drawerVisible = false; goAdmin()">
                管理后台
              </NButton>
            </div>

            <template v-if="mapsStore.scope === 'mine'">
              <div class="sider-divider"></div>

              <!-- 文件夹 -->
              <div class="sider-section">
                <div class="sider-title">
                  <span class="section-title">
                    <NIcon size="14"><FolderOutline /></NIcon>
                    文件夹
                  </span>
                  <NButton text size="tiny" @click="openCreateFolder(null)">
                    <NIcon size="16"><AddOutline /></NIcon>
                  </NButton>
                </div>
                <NSpin :show="foldersStore.loading" size="small">
                  <NTree
                    v-if="folderTreeOptions[0]?.children?.length || folderTreeOptions.length"
                    :data="folderTreeOptions"
                    :selected-keys="selectedFolderKeys"
                    :default-expand-all="true"
                    key-field="key"
                    label-field="label"
                    children-field="children"
                    selectable
                    block-line
                    @update:selected-keys="(keys: string[]) => handleFolderSelect(keys)"
                    class="folder-tree"
                  />
                  <NEmpty v-else description="暂无文件夹" size="small" />
                </NSpin>
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

              <div class="sider-divider"></div>

              <!-- 标签 -->
              <div class="sider-section">
                <div class="sider-title">
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
                  <div v-if="tagsStore.items.length === 0 && !tagsStore.loading" class="empty-hint">
                    暂无标签
                  </div>
                </div>
              </div>
            </template>
          </div>
        </NDrawerContent>
      </NDrawer>

      <NLayoutContent :native-scrollbar="false" class="app-content">
        <RouterView />
      </NLayoutContent>
    </NLayout>

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
  </NLayout>
</template>

<style scoped lang="scss">
.app-header {
  height: var(--layout-header-h);
  padding: 0 12px;
  padding-top: var(--safe-top);
  display: flex;
  align-items: center;
  justify-content: space-between;
  background: var(--app-card-bg);
}

.left,
.right {
  display: flex;
  align-items: center;
  gap: 12px;
}

.header-divider {
  height: 16px;
  margin: 0 2px;
  background-color: var(--app-border, rgba(0, 0, 0, 0.08));
}

.action-icon-btn {
  color: var(--app-text-secondary);
  transition: all 0.2s ease;

  &:hover {
    color: var(--app-text);
  }
}

.logout-btn {
  color: var(--app-text-secondary);
  transition: all 0.2s ease;

  &:hover {
    color: #e03131;
  }
}

.brand {
  font-weight: 600;
  font-size: 18px;
}

.username {
  font-size: 13px;
  color: var(--app-text-secondary);
}

.app-body {
  top: var(--layout-header-h);
}

.app-sider-desktop {
  display: block;
}

.sider-inner {
  padding: 12px 8px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.sider-section {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.sider-title {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-size: 12px;
  font-weight: 600;
  color: var(--app-text-secondary);
  padding: 0 4px 4px;
}

.section-title {
  display: flex;
  align-items: center;
  gap: 4px;
  text-transform: none;
  letter-spacing: 0;
}

.sider-divider {
  height: 1px;
  background: var(--app-border, #e0e0e6);
}

.folder-tree {
  background: transparent;
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

.app-content {
  background: var(--app-bg);
}

@media (max-width: 767px) {
  .app-sider-desktop {
    display: none;
  }

  .app-header {
    padding: 0 8px;
  }

  .brand {
    font-size: 16px;
  }

  .username {
    display: none;
  }

  .left,
  .right {
    gap: 4px;
  }
}
</style>
