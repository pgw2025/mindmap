import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import * as authApi from '@/api/auth'

const TOKEN_KEY = 'mindmap-access-token'
const REFRESH_KEY = 'mindmap-refresh-token'

export const useAuthStore = defineStore('auth', () => {
  const accessToken = ref<string | null>(localStorage.getItem(TOKEN_KEY))
  const refreshToken = ref<string | null>(localStorage.getItem(REFRESH_KEY))
  const user = ref<authApi.UserDto | null>(null)

  const isAuthenticated = computed(() => !!accessToken.value)
  const isAdmin = computed(() => user.value?.isAdmin === true)

  function persist() {
    if (accessToken.value) localStorage.setItem(TOKEN_KEY, accessToken.value)
    else localStorage.removeItem(TOKEN_KEY)
    if (refreshToken.value) localStorage.setItem(REFRESH_KEY, refreshToken.value)
    else localStorage.removeItem(REFRESH_KEY)
  }

  function applyAuth(res: authApi.AuthResponse) {
    accessToken.value = res.accessToken
    refreshToken.value = res.refreshToken
    user.value = res.user
    persist()
  }

  async function init() {
    if (!accessToken.value) return
    try {
      user.value = await authApi.fetchMe()
    } catch {
      // access token 失效，尝试刷新
      if (refreshToken.value) {
        try {
          const res = await authApi.refresh(refreshToken.value)
          applyAuth(res)
          return
        } catch {
          // 刷新失败，清空
        }
      }
      clear()
    }
  }

  async function register(payload: authApi.RegisterPayload) {
    const res = await authApi.register(payload)
    applyAuth(res)
  }

  async function login(payload: authApi.LoginPayload) {
    const res = await authApi.login(payload)
    applyAuth(res)
  }

  async function logout() {
    if (refreshToken.value) {
      try { await authApi.logout(refreshToken.value) } catch { /* 忽略 */ }
    }
    clear()
  }

  function clear() {
    accessToken.value = null
    refreshToken.value = null
    user.value = null
    persist()
  }

  return {
    accessToken,
    refreshToken,
    user,
    isAuthenticated,
    isAdmin,
    init,
    register,
    login,
    logout,
    clear
  }
})
