import { http } from './http'

export interface HealthInfo {
  service: string
  status: string
  timestamp: string
}

export async function getHealth(): Promise<HealthInfo> {
  // 后端响应已被拦截器解包为 ApiResult.data
  return (await http.get('/health')) as unknown as HealthInfo
}
