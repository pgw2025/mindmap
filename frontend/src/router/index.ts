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
    path: '/mindmaps/:id/edit',
    name: 'mindmap-edit',
    component: () => import('@/views/editor/MindMapEditorView.vue'),
    meta: { title: '编辑导图' }
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
})

router.afterEach((to) => {
  const title = (to.meta?.title as string | undefined) ?? '思维导图'
  document.title = `${title} · 思维导图`
})

export default router
