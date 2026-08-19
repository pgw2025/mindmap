import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const routes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'login',
    component: () => import('@/views/auth/LoginView.vue'),
    meta: { title: '登录', public: true, guestOnly: true }
  },
  {
    path: '/register',
    name: 'register',
    component: () => import('@/views/auth/RegisterView.vue'),
    meta: { title: '注册', public: true, guestOnly: true }
  },
  {
    path: '/',
    component: () => import('@/layouts/DefaultLayout.vue'),
    children: [
      {
        path: '',
        name: 'home',
        component: () => import('@/views/HomeView.vue'),
        meta: { title: '首页' }
      }
    ]
  },
  {
    path: '/admin',
    component: () => import('@/layouts/AdminLayout.vue'),
    meta: { title: '管理后台', requireAdmin: true },
    children: [
      {
        path: '',
        name: 'admin-dashboard',
        component: () => import('@/views/admin/AdminDashboardView.vue'),
        meta: { title: '管理看板' }
      },
      {
        path: 'users',
        name: 'admin-users',
        component: () => import('@/views/admin/AdminUsersView.vue'),
        meta: { title: '用户管理' }
      },
      {
        path: 'mindmaps',
        name: 'admin-mindmaps',
        component: () => import('@/views/admin/AdminMindMapsView.vue'),
        meta: { title: '导图管理' }
      },
      {
        path: 'reports',
        name: 'admin-reports',
        component: () => import('@/views/admin/AdminReportsView.vue'),
        meta: { title: '举报审核' }
      },
      {
        path: 'templates',
        name: 'admin-templates',
        component: () => import('@/views/admin/AdminTemplatesView.vue'),
        meta: { title: '模板管理' }
      }
    ]
  },
  {
    path: '/mindmaps/:id/edit',
    name: 'mindmap-edit',
    component: () => import('@/views/editor/MindMapEditorView.vue'),
    meta: { title: '编辑导图' }
  },
  {
    path: '/mindmaps/:id/preview',
    name: 'mindmap-preview',
    component: () => import('@/views/editor/MindMapEditorView.vue'),
    meta: { title: '预览导图' }
  },
  {
    path: '/share/:token',
    name: 'share-view',
    component: () => import('@/views/ShareView.vue'),
    meta: { title: '分享导图', public: true }
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior() {
    return { top: 0 }
  }
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()
  if (!auth.user && auth.accessToken) {
    // 首次进入受保护页，懒加载用户信息
    try {
      await auth.init()
    } catch {
      /* 忽略 */
    }
  }

  if (!to.meta.public && !auth.isAuthenticated) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }
  if (to.meta.guestOnly && auth.isAuthenticated) {
    return { name: 'home' }
  }
  // 管理后台仅管理员可访问
  if (to.meta.requireAdmin && !auth.isAdmin) {
    return { name: 'home' }
  }
})

router.afterEach((to) => {
  const title = (to.meta?.title as string | undefined) ?? '思维导图'
  document.title = `${title} · 思维导图`
})

export default router
