/**
 * 离线快照 IndexedDB 封装（原生 API，零第三方依赖）
 *
 * 依据 docs/offline-mindmap-design.md §5：
 * - 库名 mindmap-offline，版本 1
 * - maps : { id, detail?, nodes?, syncedAt }            单图快照（keyPath=id）
 * - lists: { scope, items, total, syncedAt }             全量列表快照（keyPath=scope）
 * - meta : { key, value }                                lastSync / syncOwner
 *
 * 安全原则（延续 vite.config.ts「鉴权数据不落 Cache Storage」）：
 * JWT 保护的响应只进同源隔离的 IndexedDB，可随时 clearAllOffline() 主动清除。
 */
import type { MindMapDetail, MindMapListItem } from '@/api/mindmaps'
import type { NodeDto, NodeTreeNodeDto } from '@/api/nodes'

const DB_NAME = 'mindmap-offline'
const DB_VERSION = 1

export type ListScope = 'mine' | 'public'

export interface MapSnapshot {
  id: string
  detail?: MindMapDetail
  nodes?: NodeDto[]
  syncedAt: number
}

export interface ListSnapshot {
  scope: ListScope
  items: MindMapListItem[]
  total: number
  syncedAt: number
}

export interface MetaEntry {
  key: string
  value: string | number
}

/** 隐私模式等环境下 IndexedDB 可能不可用：所有操作安全降级为 no-op / null */
let dbAvailable = typeof indexedDB !== 'undefined'

export function openOfflineDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, DB_VERSION)
    req.onupgradeneeded = () => {
      const db = req.result
      if (!db.objectStoreNames.contains('maps')) db.createObjectStore('maps', { keyPath: 'id' })
      if (!db.objectStoreNames.contains('lists')) db.createObjectStore('lists', { keyPath: 'scope' })
      if (!db.objectStoreNames.contains('meta')) db.createObjectStore('meta', { keyPath: 'key' })
    }
    req.onsuccess = () => resolve(req.result)
    req.onerror = () => reject(req.error)
  })
}

/**
 * 在单个事务内执行 store 操作，事务完成（oncomplete）后才 resolve，
 * 保证写入持久化、读取拿到最终结果。
 */
async function withStore<T>(
  storeName: string,
  mode: IDBTransactionMode,
  fn: (store: IDBObjectStore) => IDBRequest<T> | void
): Promise<T | null> {
  if (!dbAvailable) return null
  try {
    const db = await openOfflineDb()
    return await new Promise<T | null>((resolve, reject) => {
      let result: T | null = null
      const tx = db.transaction(storeName, mode)
      const store = tx.objectStore(storeName)
      const req = fn(store)
      if (req) {
        req.onsuccess = () => {
          result = req.result
        }
      }
      tx.oncomplete = () => {
        db.close()
        resolve(result)
      }
      tx.onerror = () => {
        db.close()
        reject(tx.error)
      }
      tx.onabort = () => {
        db.close()
        reject(tx.error)
      }
    })
  } catch (err) {
    console.warn(`[offline] IndexedDB「${storeName}」操作失败，离线功能降级`, err)
    dbAvailable = false
    return null
  }
}

// ---------- maps ----------

export function getMap(id: string): Promise<MapSnapshot | null> {
  return withStore<MapSnapshot | undefined>('maps', 'readonly', (s) =>
    s.get(id) as IDBRequest<MapSnapshot | undefined>
  ).then((r) => r ?? null)
}

export async function saveMap(snapshot: MapSnapshot): Promise<void> {
  await withStore('maps', 'readwrite', (s) => {
    s.put(snapshot)
  })
}

/** 增量回写：仅更新单图详情（在线打开导图时保持快照新鲜） */
export async function updateMapDetail(id: string, detail: MindMapDetail): Promise<void> {
  const snap = (await getMap(id)) ?? { id, syncedAt: Date.now() }
  snap.detail = detail
  snap.syncedAt = Date.now()
  await saveMap(snap)
}

/** 增量回写：仅更新单图节点列表（tree 不落库，读取时由 nodes 前端组装） */
export async function updateMapNodes(id: string, nodes: NodeDto[]): Promise<void> {
  const snap = (await getMap(id)) ?? { id, syncedAt: Date.now() }
  snap.nodes = nodes
  snap.syncedAt = Date.now()
  await saveMap(snap)
}

/** 删除同步：清掉不在最新列表（服务器已删）里的本地快照，返回清理数量 */
export async function deleteMapsNotIn(aliveIds: Set<string>): Promise<number> {
  const keys =
    (await withStore<IDBValidKey[]>('maps', 'readonly', (s) =>
      s.getAllKeys() as IDBRequest<IDBValidKey[]>
    )) ?? []
  const dead = keys.filter((k): k is string => typeof k === 'string' && !aliveIds.has(k))
  if (dead.length > 0) {
    await withStore('maps', 'readwrite', (s) => {
      for (const id of dead) s.delete(id)
    })
  }
  return dead.length
}

export async function countMaps(): Promise<number> {
  return (await withStore<number>('maps', 'readonly', (s) => s.count())) ?? 0
}

// ---------- lists ----------

export function getList(scope: ListScope): Promise<ListSnapshot | null> {
  return withStore<ListSnapshot | undefined>('lists', 'readonly', (s) =>
    s.get(scope) as IDBRequest<ListSnapshot | undefined>
  ).then((r) => r ?? null)
}

export async function saveList(snapshot: ListSnapshot): Promise<void> {
  await withStore('lists', 'readwrite', (s) => {
    s.put(snapshot)
  })
}

// ---------- meta ----------

export function getMeta(key: string): Promise<MetaEntry | null> {
  return withStore<MetaEntry | undefined>('meta', 'readonly', (s) =>
    s.get(key) as IDBRequest<MetaEntry | undefined>
  ).then((r) => r ?? null)
}

export async function getMetaValue(key: string): Promise<string | number | null> {
  return (await getMeta(key))?.value ?? null
}

export async function setMeta(key: string, value: string | number): Promise<void> {
  await withStore('meta', 'readwrite', (s) => {
    s.put({ key, value } satisfies MetaEntry)
  })
}

// ---------- 全库 ----------

/** 清空全部离线数据（换号 / 退出登录场景） */
export async function clearAllOffline(): Promise<void> {
  await withStore('maps', 'readwrite', (s) => {
    s.clear()
  })
  await withStore('lists', 'readwrite', (s) => {
    s.clear()
  })
  await withStore('meta', 'readwrite', (s) => {
    s.clear()
  })
}

// ---------- 工具 ----------

/**
 * 由扁平节点列表组装树结构（替代离线场景下的 GET /nodes/tree 请求，
 * 同步时可省 1/3 请求量）。
 */
export function buildNodeTree(nodes: NodeDto[]): NodeTreeNodeDto[] {
  const map = new Map<string, NodeTreeNodeDto>()
  for (const n of nodes) {
    map.set(n.id, { ...n, children: [] })
  }
  const roots: NodeTreeNodeDto[] = []
  for (const node of map.values()) {
    const parent = node.parentId ? map.get(node.parentId) : undefined
    if (parent) {
      parent.children.push(node)
    } else {
      roots.push(node)
    }
  }
  const sortRec = (list: NodeTreeNodeDto[]): void => {
    list.sort((a, b) => a.sortOrder - b.sortOrder)
    for (const n of list) sortRec(n.children)
  }
  sortRec(roots)
  return roots
}
