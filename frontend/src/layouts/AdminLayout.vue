<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NButton, NIcon, NLayout, NLayoutContent, NLayoutHeader, NLayoutSider, NDrawer, NDrawerContent, useMessage } from 'naive-ui'
import {
  GridOutline,
  PeopleOutline,
  MapOutline,
  FlagOutline,
  MenuOutline,
  ArrowBackOutline,
  MoonOutline,
  SunnyOutline,
  LayersOutline
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
}

const navItems: NavItem[] = [
  { name: 'admin-dashboard', label: '管理看板', icon: GridOutline },
  { name: 'admin-users', label: '用户管理', icon: PeopleOutline },
  { name: 'admin-mindmaps', label: '导图管理', icon: MapOutline },
  { name: 'admin-reports', label: '举报审核', icon: FlagOutline },
  { name: 'admin-templates', label: '模板管理', icon: LayersOutline }
]

const activeKey = computed(() => route.name as string)
const username = computed(() => authStore.user?.username ?? '管理员')

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
})
</script>

<template>
  <NLayout position="absolute">
    <NLayoutHeader bordered class="app-header">
      <div class="left">
        <NButton text class="menu-btn" @click="toggleSider">
          <template #icon><NIcon size="22"><MenuOutline /></NIcon></template>
        </NButton>
        <span class="brand">管理后台</span>
      </div>
      <div class="right">
        <span class="username">{{ username }}</span>
        <NButton text @click="backToHome">
          <template #icon><NIcon size="20"><ArrowBackOutline /></NIcon></template>
          返回前台
        </NButton>
        <NButton text @click="logout">退出</NButton>
        <NButton text @click="themeStore.toggle">
          <template #icon>
            <NIcon size="20">
              <MoonOutline v-if="!themeStore.isDark" />
              <SunnyOutline v-else />
            </NIcon>
          </template>
        </NButton>
      </div>
    </NLayoutHeader>

    <NLayout has-sider position="absolute" class="app-body">
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
            <template #icon><NIcon><component :is="item.icon" /></NIcon></template>
            {{ item.label }}
          </NButton>
        </div>
      </NLayoutSider>

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
              <template #icon><NIcon><component :is="item.icon" /></NIcon></template>
              {{ item.label }}
            </NButton>
          </div>
        </NDrawerContent>
      </NDrawer>

      <NLayoutContent :native-scrollbar="false" class="app-content">
        <RouterView />
      </NLayoutContent>
    </NLayout>
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
  gap: 8px;
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

.app-content {
  background: var(--app-bg);
  padding: 16px;
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

  .app-content {
    padding: 8px;
  }
}
</style>
