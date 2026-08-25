/**
 * 离线全量快照同步（docs/offline-mindmap-design.md §4 / §6）
 *
 * 触发点：HomeView 挂载（节流） / 网络恢复（online 事件） / 手动 force。
 * 流程：翻页拉全量列表(mine+public) → 每图限并发拉 detail+nodes → 事务写入 IndexedDB。
 */
import { reactive } from 'vue'
import { fetchMindMap, fetchMindMaps, type MindMapListItem } from '@/api/mindmaps'
import { fetchNodes } from '@/api/nodes'
import { useAuthStore } from '@/stores/auth'
import * as db from './db'

/** 同步节流窗口：距上次成功同步不足 10 分钟则跳过（force 可越过） */
const THROTTLE_MS = 10 * 60_000
/** 单图 detail+nodes 请求并发上限，避免压垮服务器 */
const CONCURRENCY = 3
/** 列表翻页大小 */
const PAGE_SIZE = 100

/** 全局离线状态（响应式）：供各视图展示「最后同步时间」与同步中状态 */
export const offlineState = reactive({
  /** 上次全量同步成功的时间戳（ms），0 表示从未同步 */
  lastSync: 0,
  syncing: false
})

/** 去重：同一时刻只允许一轮同步在跑 */
let inflight: Promise<void> | null = null

/**
 * 查询离线状态。同步返回当前值，同时异步从 IDB meta 补齐 lastSync
 * （冷启动时 reactive 状态为 0，需要读一次持久化的值）。
 */
export function getOfflineStatus(): { lastSync: number; syncing: boolean } {
  db.getMetaValue('lastSync')
    .then((v) => {
      if (typeof v === 'number' && v > offlineState.lastSync) offlineState.lastSync = v
    })
    .catch(() => {
      /* ignore */
    })
  return { lastSync: offlineState.lastSync, syncing: offlineState.syncing }
}

/** 格式化「最后同步时间」：同天只显 HH:mm，跨天显 MM-DD HH:mm */
export function formatSyncTime(ts: number): string {
  if (!ts) return ''
  const d = new Date(ts)
  const hh = String(d.getHours()).padStart(2, '0')
  const mm = String(d.getMinutes()).padStart(2, '0')
  if (d.toDateString() === new Date().toDateString()) return `${hh}:${mm}`
  const M = String(d.getMonth() + 1).padStart(2, '0')
  const D = String(d.getDate()).padStart(2, '0')
  return `${M}-${D} ${hh}:${mm}`
}

/**
 * 全量同步所有导图到 IndexedDB。
 * - 离线 / 未登录：直接返回
 * - 节流：距上次成功同步 < 10 分钟跳过（force=true 越过）
 * - 并发去重：已有同步在跑时复用同一个 Promise
 */
export async function syncAllOffline(force = false): Promise<void> {
  if (typeof navigator !== 'undefined' && navigator.onLine === false) return
  const auth = useAuthStore()
  if (!auth.isAuthenticated) return

  if (!force) {
    const last = await db.getMetaValue('lastSync').catch(() => null)
    const lastSync = typeof last === 'number' ? last : offlineState.lastSync
    if (lastSync > 0 && Date.now() - lastSync < THROTTLE_MS) return
  }
  if (inflight) return inflight

  inflight = doSync().finally(() => {
    inflight = null
  })
  return inflight
}

async function doSync(): Promise<void> {
  offlineState.syncing = true
  try {
    // 换号处理：快照所有者与当前用户不一致 → 清空全部 store 再同步，避免串数据
    const auth = useAuthStore()
    const userId = auth.user?.id ?? ''
    const owner = await db.getMetaValue('syncOwner')
    if (owner !== null && owner !== userId) {
      await db.clearAllOffline()
    }
    await db.setMeta('syncOwner', userId)

    const aliveIds = new Set<string>()
    for (const scope of ['mine', 'public'] as const) {
      const all = await fetchAllPages(scope)
      await db.saveList({ scope, items: all, total: all.length, syncedAt: Date.now() })
      for (const item of all) aliveIds.add(item.id)

      // 每图并发 CONCURRENCY：detail + nodes（tree 由前端从 nodes 组装，省 1/3 请求）
      for (let i = 0; i < all.length; i += CONCURRENCY) {
        const batch = all.slice(i, i + CONCURRENCY)
        await Promise.all(
          batch.map(async (item) => {
            try {
              const [detail, nodes] = await Promise.all([
                fetchMindMap(item.id),
                fetchNodes(item.id)
              ])
              await db.saveMap({ id: item.id, detail, nodes, syncedAt: Date.now() })
            } catch (err) {
              // 单图失败不阻断整体同步，保留该图旧快照
              console.warn(`[offline] 同步导图「${item.title}」失败，保留旧快照`, err)
            }
          })
        )
      }
    }

    // 删除同步：服务器已删的导图，本地快照一并清除
    const removed = await db.deleteMapsNotIn(aliveIds)

    const now = Date.now()
    await db.setMeta('lastSync', now)
    offlineState.lastSync = now
    console.info(
      `[offline] 快照同步完成：${aliveIds.size} 张导图` +
        (removed > 0 ? `，清理 ${removed} 张已删除` : '')
    )
  } finally {
    offlineState.syncing = false
  }
}

/** 翻页拉取某 scope 的全量列表（pageSize=100，直到取满 total） */
async function fetchAllPages(scope: 'mine' | 'public'): Promise<MindMapListItem[]> {
  const items: MindMapListItem[] = []
  let page = 1
  // 上限保护：最多翻 200 页（约 2 万张），避免异常 total 导致死循环
  for (let guard = 0; guard < 200; guard++) {
    const res = await fetchMindMaps({ scope, page, pageSize: PAGE_SIZE })
    items.push(...res.items)
    if (res.items.length === 0 || items.length >= res.total) break
    page++
  }
  return items
}
