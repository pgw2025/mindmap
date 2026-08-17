import axios, { AxiosError, type AxiosInstance } from 'axios'
import { useAuthStore } from '@/stores/auth'

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

// 响应拦截器：解包 ApiResult，处理 401
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
  (error: AxiosError) => {
    if (error.response?.status === 401) {
      const auth = useAuthStore()
      auth.clear()
      if (location.pathname !== '/login') {
        location.href = '/login'
      }
    }
    const payload = error.response?.data as { message?: string } | undefined
    const message = payload?.message || error.message || '网络错误'
    return Promise.reject(new Error(message))
  }
)

export { http }
