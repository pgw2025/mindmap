import { registerSW } from 'virtual:pwa-register'

export interface PwaCallbacks {
  /** 发现新版本：UI 层弹窗询问，用户确认后调用 updateSW(true) 刷新生效 */
  onNeedRefresh?: (updateSW: (reloadPage?: boolean) => Promise<void>) => void
  /** SW 首次安装完成、离线资源就绪 */
  onOfflineReady?: () => void
  /** 网络状态变化：isOffline 为 true 表示当前离线 */
  onNetworkChange?: (isOffline: boolean) => void
}

export interface PwaHandle {
  /** 停止网络监听（组件卸载时调用） */
  dispose: () => void
}

/**
 * 注册 Service Worker（prompt 模式）+ 网络状态监听。
 *
 * - 发现新版本时不自动刷新，通过 onNeedRefresh 交给 UI 层弹窗确认，避免编辑中途被重载；
 * - online/offline 事件驱动全局离线提示，防止用户离线状态下白写数据。
 *
 * 由 UI 层（PwaManager.vue）调用并传入回调以展示 naive-ui 提示。
 */
export function setupPwa(callbacks: PwaCallbacks = {}): PwaHandle {
  // registerSW 同步返回 updateSW；onNeedRefresh 在 SW 更新事件时异步触发，闭包内已赋值
  const updateSW = registerSW({
    immediate: true,
    onNeedRefresh() {
      callbacks.onNeedRefresh?.(updateSW)
    },
    onOfflineReady() {
      callbacks.onOfflineReady?.()
    },
  })

  // 网络状态监听：离线时全局提示，恢复在线时自动关闭
  const handleOnline = () => callbacks.onNetworkChange?.(false)
  const handleOffline = () => callbacks.onNetworkChange?.(true)

  window.addEventListener('online', handleOnline)
  window.addEventListener('offline', handleOffline)
  // 页面在断网状态下加载时，初始化即反映离线状态
  if (navigator.onLine === false) {
    callbacks.onNetworkChange?.(true)
  }

  return {
    dispose() {
      window.removeEventListener('online', handleOnline)
      window.removeEventListener('offline', handleOffline)
    },
  }
}
