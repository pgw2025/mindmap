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
  MenuOutline,
  MoonOutline,
  SunnyOutline,
  CloudOutline,
  ShieldCheckmarkOutline,
  LogOutOutline
} from '@vicons/ionicons5'
import { useThemeStore } from '@/stores/theme'
import { useAuthStore } from '@/stores/auth'
import { useFoldersStore } from '@/stores/folders'
import { useMindMapsStore } from '@/stores/mindmaps'

const themeStore = useThemeStore()
const authStore = useAuthStore()
const foldersStore = useFoldersStore()
const mapsStore = useMindMapsStore()
const message = useMessage()
const router = useRouter()

const isAdmin = computed(() => authStore.isAdmin)

function goAdmin(): void {
  router.push({ name: 'admin-dashboard' })
}

const collapsed = ref(false)
const drawerVisible = ref(false)

// 文件夹创建 modal 状态
const folderModalVisible = ref(false)
const folderNameInput = ref('')
const folderCreating = ref(false)

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
  mapsStore.reset()
  location.href = '/login'
}

const folderTreeData = computed<TreeOption[]>(() => {
  function build(nodes: typeof foldersStore.tree): TreeOption[] {
    return nodes.map((node) => ({
      key: node.id,
      label: node.name,
      extra: node.mindMapCount > 0 ? String(node.mindMapCount) : undefined,
      children: node.children.length > 0 ? build(node.children) : undefined,
      isLeaf: node.children.length === 0
    }))
  }
  return build(foldersStore.tree)
})

function selectFolder(keys: string[]) {
  const id = keys[0] ?? null
  mapsStore.setFolderFilter(id)
}

function openCreateFolderModal() {
  folderNameInput.value = ''
  folderModalVisible.value = true
}

async function submitCreateFolder(): Promise<boolean> {
  const name = folderNameInput.value.trim()
  if (!name) {
    message.warning('请输入文件夹名称')
    return false
  }
  folderCreating.value = true
  try {
    await foldersStore.create({ name, parentId: null })
    message.success('文件夹已创建')
    folderModalVisible.value = false
    return true
  } catch (e) {
    message.error((e as Error).message)
    return false
  } finally {
    folderCreating.value = false
  }
}

const username = computed(() => authStore.user?.username ?? '用户')

onMounted(async () => {
  if (authStore.isAuthenticated) {
    await Promise.all([foldersStore.load(), mapsStore.load()])
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
      <NLayoutSider bordered :collapsed="collapsed" :collapsed-width="0" :width="240" collapse-mode="width"
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

          <div v-if="mapsStore.scope === 'mine'" class="sider-section">
            <div class="sider-title">
              <span>文件夹</span>
              <NButton text size="tiny" @click="openCreateFolderModal">
                <template #icon>
                  <NIcon>
                    <AddOutline />
                  </NIcon>
                </template>
              </NButton>
            </div>
            <NSpin :show="foldersStore.loading" size="small">
              <NTree v-if="folderTreeData.length > 0" :data="folderTreeData" block-line expand-on-click
                :selected-keys="mapsStore.folderId ? [mapsStore.folderId] : []" @update:selected-keys="selectFolder"
                class="folder-tree" />
              <NEmpty v-else description="暂无文件夹" size="small" />
            </NSpin>
            <NButton v-if="mapsStore.folderId" quaternary block size="small" @click="mapsStore.setFolderFilter(null)">
              清除筛选
            </NButton>
          </div>
        </div>
      </NLayoutSider>

      <!-- 移动端抽屉侧边栏（仅在 drawerVisible 时挂载，避免桌面端遮罩遮挡） -->
      <NDrawer v-if="drawerVisible" v-model:show="drawerVisible" :width="260" placement="left">
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
            <div v-if="mapsStore.scope === 'mine'" class="sider-section">
              <div class="sider-title">
                <span>文件夹</span>
                <NButton text size="tiny" @click="openCreateFolderModal">
                  <template #icon>
                    <NIcon>
                      <AddOutline />
                    </NIcon>
                  </template>
                </NButton>
              </div>
              <NSpin :show="foldersStore.loading" size="small">
                <NTree v-if="folderTreeData.length > 0" :data="folderTreeData" block-line expand-on-click
                  :selected-keys="mapsStore.folderId ? [mapsStore.folderId] : []" @update:selected-keys="selectFolder"
                  class="folder-tree" />
                <NEmpty v-else description="暂无文件夹" size="small" />
              </NSpin>
            </div>
          </div>
        </NDrawerContent>
      </NDrawer>

      <NLayoutContent :native-scrollbar="false" class="app-content">
        <RouterView />
      </NLayoutContent>
    </NLayout>

    <!-- 文件夹创建 Modal -->
    <NModal v-model:show="folderModalVisible" preset="card" title="新建文件夹" display-directive="if"
      style="max-width: 380px">
      <NInput v-model:value="folderNameInput" placeholder="请输入文件夹名称" maxlength="64" :autofocus="true"
        @keyup.enter="submitCreateFolder" />
      <template #footer>
        <NSpace justify="end">
          <NButton @click="folderModalVisible = false">取消</NButton>
          <NButton type="primary" :loading="folderCreating" :disabled="!folderNameInput.trim()"
            @click="submitCreateFolder">
            创建
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
  gap: 16px;
}

.sider-section {
  display: flex;
  flex-direction: column;
  gap: 4px;
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

.folder-tree {
  background: transparent;
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
