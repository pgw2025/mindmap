import axios, { AxiosError, type AxiosInstance } from 'axios'
import { useAuthStore } from '@/stores/auth'
import * as authApi from '@/api/auth'

const http: AxiosInstance = axios.create({
  baseURL: import.meta.env.VITE_API_BASE ?? '/api',
  timeout: 30000
})

// 请求拦截器：注入 JWT
http.interceptors.request.use((config) => {
  const auth = useAuthStore()
  if (auth.accessToken) {
    config.headers.Authorization = `Bearer ${auth.accessToken}`
  }
  return config
})

// 共享刷新 Promise：多请求同时 401 时只发起一次 /auth/refresh。
// 后端 Refresh Token 一次性轮换，重复用旧 token 会失败，必须去重。
let refreshPromise: Promise<authApi.AuthResponse> | null = null

async function doRefresh(): Promise<authApi.AuthResponse> {
  if (refreshPromise) return refreshPromise
  refreshPromise = (async () => {
    const auth = useAuthStore()
    const res = await authApi.refresh(auth.refreshToken!)
    auth.applyAuth(res) // 同时更新 accessToken 与轮换后的 refreshToken
    return res
  })()
  try {
    return await refreshPromise
  } finally {
    refreshPromise = null
  }
}

// 响应拦截器：解包 ApiResult，处理 401（静默刷新）
http.interceptors.response.use(
  (response) => {
    const payload = response.data
    if (payload && typeof payload === 'object' && 'code' in payload) {
      const result = payload as { code: number; message?: string; data: unknown }
      if (result.code === 0) {
        return result.data
      }
      return Promise.reject(new Error(result.message || '业务错误'))
    }
    return payload
  },
  async (error: AxiosError) => {
    const status = error.response?.status
    if (status !== 401) {
      const payload = error.response?.data as { message?: string } | undefined
      const message = payload?.message || error.message || '网络错误'
      return Promise.reject(new Error(message))
    }

    const auth = useAuthStore()
    const config = error.config
    const isRefreshRequest = config?.url?.includes('/auth/refresh')

    // 刷新请求自身 401、已重试过、或无 refreshToken → 无法恢复，跳登录
    if (isRefreshRequest || (config as any)?._retry || !auth.refreshToken) {
      auth.clear()
      if (location.pathname !== '/login') {
        location.href = '/login'
      }
      return Promise.reject(new Error('登录已过期，请重新登录'))
    }

    try {
      await doRefresh()
      ;(config as any)._retry = true
      // 用新 token 重发原请求（会再次经过请求拦截器注入新 JWT）
      return http(config as any)
    } catch {
      auth.clear()
      if (location.pathname !== '/login') {
        location.href = '/login'
      }
      return Promise.reject(new Error('登录已过期，请重新登录'))
    }
  }
)

export { http }
