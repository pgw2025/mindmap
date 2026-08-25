# 思维导图离线查看 — 实现方案设计

> 状态：待审批  
> 目标：离线（无网络 / 服务器不可达）状态下，仍可打开应用并查看**所有**思维导图的完整内容（只读）。  
> 非目标（本方案不含）：离线编辑与自动同步（见 §9 展望）。

---

## 1. 结论

**可以实现。** 采用「IndexedDB 全量快照 + 网络优先本地兜底」方案：

- 在线时静默把全部导图（列表 + 详情 + 节点）同步到浏览器 IndexedDB；
- 离线时所有读请求自动降级读本地快照，UI 标注「离线数据 · 最后同步时间」；
- 配合已写好的 Service Worker 外壳缓存，离线可完整打开应用并浏览所有导图。

## 2. 现状关键事实（方案依据）

| 事实                                | 出处                                                                                                                                                        | 对方案的意义                                                             |
| --------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------ |
| 数据加载路径高度集中                        | `stores/mindmaps.ts` → `GET /api/mindmaps`；`MindMapEditorView.vue` → `fetchMindMap(id)`；`stores/nodes.ts load()` → `fetchNodes(id)` + `fetchNodeTree(id)` | 一张导图离线渲染只需 **3 个 GET**（detail / nodes / nodes/tree），兜底点少、改动集中      |
| JWT 持久化在 localStorage             | `stores/auth.ts`（`isAuthenticated = !!accessToken`，纯本地判断）                                                                                                 | 路由守卫 `auth.init()` 失败会被 try/catch 忽略 → **离线不会被踢回登录页**              |
| 导图内容全部在服务器数据库                     | 后端 MySQL                                                                                                                                                  | 离线必须有本地副本，否则无从谈起                                                   |
| SW 配置 `devOptions.enabled: false` | `vite.config.ts`                                                                                                                                          | **`npm run dev` 下 Service Worker 不挂载**，离线外壳必须 `build + preview` 验证 |
| PWA 配置已就绪但未构建                     | dist 构建于 12:40，早于配置修改 13:36                                                                                                                               | 重新 build 后 sw.js 才会生成                                              |
| 鉴权数据刻意不进 Cache Storage            | `vite.config.ts` 注释                                                                                                                                       | 本方案延续该安全原则：JWT 保护的响应**不进 SW 缓存**，改用 IndexedDB 显式快照                 |

**重要说明——dev 与离线的关系**：`npm run dev` 时前后端都在本机 localhost，断外网对它毫无影响，「离线」实际作用于**部署后**的场景（手机/其他设备访问服务器）。因此验证离线功能必须走 `npm run build && npm run preview`（或部署到测试环境）。

## 3. 方案选型

| 维度    | 方案 A：SW runtimeCaching 扩展            | 方案 B：IndexedDB 快照（推荐）        |
| ----- | ------------------------------------ | ---------------------------- |
| 改动位置  | 只改 `vite.config.ts`                  | 新增 offline 模块 + 3 处 store 兜底 |
| 覆盖范围  | **只缓存访问过的 URL**（惰性），没打开过的导图离线看不到     | 登录后**全量预取**，所有导图离线可看 ✅       |
| 分页列表  | 列表接口带 query 参数，缓存 key 爆炸，翻页/筛选组合很难命中 | 本地存全量列表，本地过滤分页 ✅             |
| 安全性   | JWT 保护的响应落 Cache Storage，违背现有安全原则    | IDB 中数据同源隔离、可主动清除，风险可控 ✅     |
| 数据新鲜度 | 缓存过期策略粗糙                             | 每条记录带 syncedAt，UI 可展示 ✅      |
| 依赖    | 零                                    | 零（原生 IndexedDB，不引第三方库）       |

结论：**B 为主**。A 最多作为补充（如给公开分享页 ShareView 加 NetworkFirst），不承担"全部导图离线"的职责。

## 4. 架构与数据流

```
┌────────────── 在线（正常/同步） ──────────────┐   ┌────────────── 离线（兜底） ──────────────┐
│                                            │   │                                        │
│  登录成功 / HomeView 挂载(节流) / 手动刷新   │   │  stores/mindmaps.load()                │
│           │                                │   │  stores/nodes.load()                   │
│           ▼                                │   │  EditorView fetchMindMap(id)           │
│  syncAllOffline()                          │   │           │                            │
│   ├─ 翻页拉列表(scope=mine/public)          │   │           ▼ 网络失败 & IDB 有快照      │
│   ├─ 每图并发3: detail+nodes(限并发)        │   │   读 IndexedDB → 渲染                  │
│   └─ 写 IndexedDB + lastSync               │   │   顶栏: 「离线数据 · 最后同步 HH:mm」   │
└────────────────────────────────────────────┘   └────────────────────────────────────────┘
```

## 5. IndexedDB 设计

- 数据库名 `mindmap-offline`，版本 `1`
- **object stores：**

| store   | keyPath | 值结构                                                                        |
| ------- | ------- | -------------------------------------------------------------------------- |
| `maps`  | `id`    | `{ id, detail: MindMapDetail, nodes: NodeDto[], syncedAt: number }`        |
| `lists` | `scope` | `{ scope: 'mine' \| 'public', items: MindMapListItem[], total, syncedAt }` |
| `meta`  | `key`   | `{ key: 'lastSync' \| 'syncOwner', value: string \| number }`              |

- **换号处理**：`meta.syncOwner` 记录 userId；登录用户与快照所有者不一致时，先清空全部 store 再同步，避免串数据。
- **删除同步**：同步完成后，`maps` 中不在最新列表里的 id 予以清除（服务器已删的导图本地也删）。

## 6. 文件级改动清单

### 新增（3 个文件，约 400 行）

| 文件                        | 职责                                                                                                                      |
| ------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| `src/offline/db.ts`       | 原生 IndexedDB 封装：openDB / get / put / getAll / clear，Promise 化，零依赖                                                       |
| `src/offline/sync.ts`     | `syncAllOffline()`：翻页拉全量列表 → 限并发拉每图 detail+nodes → 事务写入；含节流（距上次同步 < 10 分钟则跳过，可强制）；`getOfflineStatus()` 供 UI 查询 lastSync |
| `src/offline/fallback.ts` | `withFallback<T>(networkCall, idbRead)`：在线走网络并回写 IDB（单图粒度的增量更新），网络异常时读 IDB，两处都没有则抛出友好错误                                 |

### 修改（4 个文件，均为小改）

| 文件                                            | 改动                                                                                                                           |
| --------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `src/stores/mindmaps.ts`                      | `load()` 包一层 `withFallback`：网络失败时读 `lists` store + 本地按 folderId/tagId/keyword 过滤分页；新增 `isOfflineSnapshot` 状态供 HomeView 显示提示条 |
| `src/stores/nodes.ts`                         | `load(mindMapId)` 同样加 `withFallback` 兜底                                                                                      |
| `src/views/editor/MindMapEditorView.vue`      | `fetchMindMap(id)` 调用处改走 `withFallback`；离线打开时顶栏显示「离线数据 · 最后同步时间」，并禁用分享/版本等强依赖网络的按钮                                           |
| `src/views/home/HomeView.vue`（或 HomeView.vue） | 挂载时在线则触发 `syncAllOffline()`（节流）；离线状态显示全局提示条「当前离线，展示本地快照」                                                                     |

### 不动

- `vite.config.ts`：不新增任何 runtimeCaching，维持"鉴权数据不落 Cache Storage"原则。
- `http.ts` / `auth.ts` / 后端：零改动。

## 7. 关键代码骨架

```ts
// src/offline/db.ts（节选）
const DB_NAME = 'mindmap-offline'
const DB_VERSION = 1

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

// src/offline/sync.ts（节选）
export async function syncAllOffline(force = false): Promise<void> {
  if (navigator.onLine === false) return
  if (!force && Date.now() - (await getLastSync()) < 10 * 60_000) return

  const userId = useAuthStore().user?.id
  if ((await getSyncOwner()) !== userId) await clearAll()

  for (const scope of ['mine', 'public'] as const) {
    const all = await fetchAllPages(scope)        // pageSize=100 翻页取完
    await saveList(scope, all)
    await Promise.all(chunk(all, 3).map(batch =>   // 并发=3，避免压垮服务器
      Promise.all(batch.map(async item => {
        const [detail, nodes] = await Promise.all([
          fetchMindMap(item.id),
          fetchNodes(item.id),                     // tree 可由 nodes 前端组装，省 1/3 请求
        ])
        await saveMap(item.id, detail, nodes)
      }))
    ))
  }
  await setLastSync(Date.now())
}

// src/offline/fallback.ts（节选）
export async function withFallback<T>(
  network: () => Promise<T>,
  cached: () => Promise<T | null>,
  onSuccess?: (data: T) => Promise<void>,
): Promise<T> {
  try {
    const data = await network()
    await onSuccess?.(data)      // 顺手增量回写 IDB，单图打开也保持快照新鲜
    return data
  } catch (err) {
    const local = await cached()
    if (local !== null) return local
    throw err                    // 本地也没有 → 维持原错误，UI 走空态
  }
}
```

## 8. 风险与边界

| 风险            | 评估                                                                               | 对策                                                                     |
| ------------- | -------------------------------------------------------------------------------- | ---------------------------------------------------------------------- |
| 数据量           | 500 节点/图 ≈ 200KB，100 张图 ≈ 20MB；IndexedDB 配额 = 磁盘 60%（Chrome），远超 localStorage 5MB | 无压力；sync.ts 里记录总条目数，>50MB 时告警日志                                        |
| 快照过期          | 离线看的是最后同步时的数据                                                                    | UI 强制显示「最后同步时间」；搜索/筛选基于快照数据（本地执行，完全可用）                                 |
| token 过期      | 离线读取 IDB 不需要 token；仅在线同步需要                                                       | 离线查看不受影响                                                               |
| 换账号登录         | 快照串号                                                                             | `meta.syncOwner` 校验 + 清库重同步                                            |
| iOS Safari    | 加主屏的 PWA 长期不用，IDB 可能被系统清理（ITP）                                                   | 提示用户定期打开；Android/桌面 Chrome 无此问题                                        |
| dev 模式验证不了 SW | `devOptions.enabled: false` 是刻意配置（避免 dev 缓存脏数据）                                  | 数据层兜底在 dev 下也能测（DevTools Offline + IDB 有数据）；外壳离线用 `build + preview` 验证 |

## 9. 验证步骤（实施完成后）

```bash
cd D:/CSharp/MindMap/frontend
npm run build && npm run preview   # localhost:4173，localhost 属安全上下文，SW 可注册
```

1. 登录 → 等待同步完成（DevTools → Application → IndexedDB → mindmap-offline，确认 maps/lists 有数据）；
2. DevTools → Network → **Offline** → 刷新页面：应用正常打开（SW 外壳）；
3. 首页列表可见，顶栏显示离线提示；
4. 逐张打开导图（含从未在线打开过的）：正常渲染，标注「离线数据」；
5. 搜索、文件夹/标签筛选：基于本地快照正常工作；
6. 恢复 Online → 刷新 → 提示消失，数据回到最新；
7. 真机验收：部署后手机开启飞行模式，重复 2–4。

## 10. 实施顺序与工作量

| 阶段          | 内容                                                                 | 预估                |
| ----------- | ------------------------------------------------------------------ | ----------------- |
| P1          | offline 模块 3 文件 + stores/editor 兜底（= 本方案核心）                        | 1 个工作日内           |
| P2          | 重新 build + 部署，sw.js 生效（外壳离线）                                       | 半小时               |
| P3（展望，单独立项） | 离线**编辑**：写操作入 IDB 队列，online 事件重放；编辑器已有 `useMindMapSync` 重试队列，方向可延续 | 1–2 天，涉及冲突策略需另行设计 |

## 11. 审批点

- [ ] 方案 B（IndexedDB 快照）是否确认？（替代：方案 A 仅改 vite.config.ts，但只能覆盖"看过的导图"）
- [ ] 同步触发策略：登录后 + HomeView 节流 10 分钟 + 手动刷新，是否接受？
- [ ] `nodes/tree` 由前端从 nodes 组装（省 1/3 同步请求）还是原样缓存 tree 接口？默认前者。
- [ ] 是否需要 PWA「更新提示弹窗」在离线场景下静默？（现有 PwaManager 已处理，无需改动，仅确认）

