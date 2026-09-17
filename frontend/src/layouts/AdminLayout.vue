<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NButton, NIcon, NLayout, NLayoutContent, NLayoutHeader, NLayoutSider, NDrawer, NDrawerContent, NDivider, NTooltip, useMessage, NBadge } from 'naive-ui'
import {
  GridOutline,
  PeopleOutline,
  MapOutline,
  FlagOutline,
  MenuOutline,
  ArrowBackOutline,
  MoonOutline,
  SunnyOutline,
  LayersOutline,
  LogOutOutline,
  HomeOutline
} from '@vicons/ionicons5'
import { useThemeStore } from '@/stores/theme'
import { useAuthStore } from '@/stores/auth'
import { useAdminStore } from '@/stores/admin'

const route = useRoute()
const router = useRouter()
const themeStore = useThemeStore()
const authStore = useAuthStore()
const adminStore = useAdminStore()
const message = useMessage()

const collapsed = ref(false)
const drawerVisible = ref(false)

interface NavItem {
  name: string
  label: string
  icon: typeof GridOutline
  showInBottom?: boolean
}

const navItems: NavItem[] = [
  { name: 'admin-dashboard', label: '看板', icon: GridOutline, showInBottom: true },
  { name: 'admin-users', label: '用户', icon: PeopleOutline, showInBottom: true },
  { name: 'admin-mindmaps', label: '导图', icon: MapOutline, showInBottom: true },
  { name: 'admin-templates', label: '模板', icon: LayersOutline, showInBottom: true },
  { name: 'admin-reports', label: '举报审核', icon: FlagOutline }
]

const bottomNavItems = computed(() => navItems.filter((item) => item.showInBottom))
const activeKey = computed(() => route.name as string)
const username = computed(() => authStore.user?.username ?? '管理员')

// 待处理举报数量（用于底部导航角标）
const pendingReportCount = computed(() => adminStore.stats?.pendingReportCount ?? 0)

function toggleSider() {
  if (window.innerWidth < 768) {
    drawerVisible.value = true
  } else {
    collapsed.value = !collapsed.value
  }
}

function go(name: string) {
  drawerVisible.value = false
  router.push({ name })
}

function backToHome() {
  router.push({ name: 'home' })
}

async function logout() {
  await authStore.logout()
  adminStore.reset()
  location.href = '/login'
}

onMounted(async () => {
  if (!authStore.user && authStore.accessToken) {
    try {
      await authStore.init()
    } catch {
      /* ignore */
    }
  }
  // 非管理员被路由守卫拦截，这里二次保险
  if (!authStore.isAdmin) {
    message.warning('无管理员权限')
    router.push({ name: 'home' })
  }
  // 加载统计数据（供底部导航角标使用）
  if (!adminStore.stats) {
    adminStore.loadStats().catch(() => {})
  }
})
</script>

<template>
  <NLayout position="absolute" class="admin-layout">
    <NLayoutHeader bordered class="app-header">
      <div class="left">
        <NButton text class="menu-btn desktop-only" @click="toggleSider">
          <template #icon>
            <NIcon size="22">
              <MenuOutline />
            </NIcon>
          </template>
        </NButton>
        <NButton text class="menu-btn mobile-only" @click="backToHome">
          <template #icon>
            <NIcon size="20">
              <ArrowBackOutline />
            </NIcon>
          </template>
        </NButton>
        <span class="brand desktop-only">管理后台</span>
        <span class="brand mobile-only">{{ route.meta?.title as string || '管理' }}</span>
      </div>
      <div class="right">
        <span class="username desktop-only">{{ username }}</span>
        <NButton quaternary size="small" class="desktop-only" @click="backToHome">
          <template #icon>
            <NIcon size="18">
              <ArrowBackOutline />
            </NIcon>
          </template>
          返回前台
        </NButton>
        <NDivider vertical class="header-divider desktop-only" />
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
        <NButton quaternary size="small" class="logout-btn desktop-only" @click="logout">
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
      <!-- 桌面端侧边栏 -->
      <NLayoutSider
        bordered
        :collapsed="collapsed"
        :collapsed-width="0"
        :width="220"
        collapse-mode="width"
        :native-scrollbar="true"
        class="app-sider-desktop"
      >
        <div class="sider-inner">
          <NButton
            v-for="item in navItems"
            :key="item.name"
            quaternary
            block
            :type="activeKey === item.name ? 'primary' : 'default'"
            @click="go(item.name)"
          >
            <template #icon>
              <NIcon>
                <component :is="item.icon" />
              </NIcon>
            </template>
            {{ item.label }}
          </NButton>
        </div>
      </NLayoutSider>

      <!-- 移动端抽屉菜单 -->
      <NDrawer v-if="drawerVisible" v-model:show="drawerVisible" :width="240" placement="left">
        <NDrawerContent title="管理后台">
          <div class="sider-inner">
            <NButton
              v-for="item in navItems"
              :key="item.name"
              quaternary
              block
              :type="activeKey === item.name ? 'primary' : 'default'"
              @click="go(item.name)"
            >
              <template #icon>
                <NIcon>
                  <component :is="item.icon" />
                </NIcon>
              </template>
              {{ item.label }}
            </NButton>
            <NDivider style="margin: 12px 0" />
            <div class="drawer-user-info">
              <span class="drawer-username">{{ username }}</span>
            </div>
            <NButton quaternary block type="error" @click="logout">
              <template #icon>
                <NIcon size="16">
                  <LogOutOutline />
                </NIcon>
              </template>
              退出登录
            </NButton>
          </div>
        </NDrawerContent>
      </NDrawer>

      <NLayoutContent :native-scrollbar="false" class="app-content">
        <RouterView />
      </NLayoutContent>
    </NLayout>

    <!-- 移动端底部导航栏 -->
    <div class="mobile-bottom-nav">
      <div
        v-for="item in bottomNavItems"
        :key="item.name"
        class="bottom-nav-item"
        :class="{ active: activeKey === item.name }"
        @click="go(item.name)"
      >
        <div class="bottom-nav-icon">
          <NIcon size="20">
            <component :is="item.icon" />
          </NIcon>
        </div>
        <span class="bottom-nav-label">{{ item.label }}</span>
      </div>
    </div>
  </NLayout>
</template>

<style scoped lang="scss">
.admin-layout {
  --admin-bottom-nav-height: 60px;
}

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
  gap: 10px;
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
  gap: 4px;
}

.drawer-user-info {
  padding: 8px;
  .drawer-username {
    font-size: 14px;
    font-weight: 500;
    color: var(--app-text-primary);
  }
}

.app-content {
  background: var(--app-bg);
  padding: 16px;
  padding-bottom: 16px;
}

/* 移动端底部导航 */
.mobile-bottom-nav {
  display: none;
  position: fixed;
  bottom: 0;
  left: 0;
  right: 0;
  height: var(--admin-bottom-nav-height);
  background: var(--app-card-bg);
  border-top: 1px solid var(--app-border);
  z-index: 100;
  padding-bottom: env(safe-area-inset-bottom);
  box-shadow: 0 -2px 12px rgba(0, 0, 0, 0.06);
}

.bottom-nav-item {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 2px;
  cursor: pointer;
  transition: all 0.2s ease;
  color: var(--app-text-secondary);

  &.active {
    color: var(--app-primary, #18a058);
  }

  &:active {
    transform: scale(0.95);
  }
}

.bottom-nav-icon {
  display: flex;
  align-items: center;
  justify-content: center;
}

.bottom-nav-label {
  font-size: 11px;
  font-weight: 500;
}

/* 响应式 */
@media (max-width: 767px) {
  .desktop-only {
    display: none !important;
  }
  .mobile-only {
    display: flex !important;
  }

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

  .app-content {
    padding: 10px;
    padding-bottom: calc(var(--admin-bottom-nav-height) + 12px);
  }

  .mobile-bottom-nav {
    display: flex;
  }
}

@media (min-width: 768px) {
  .desktop-only {
    display: flex;
  }
  .mobile-only {
    display: none !important;
  }
}
</style>
