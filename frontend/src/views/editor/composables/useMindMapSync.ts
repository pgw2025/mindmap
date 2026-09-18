import { ref, shallowRef, type ComputedRef } from 'vue'
import type MindMap from 'simple-mind-map'
import type { NodeDto, NodeBatchItem } from '@/api/nodes'
import { useNodesStore } from '@/stores/nodes'

type NodesStore = ReturnType<typeof useNodesStore>

/** 同步状态类型 */
export type SyncStatus = 'idle' | 'syncing' | 'saved' | 'error'

/** 后端 NodeShape 数字 → simple-mind-map 形状字符串 */
const shapeMap: Record<number, string> = {
  0: 'rectangle',
  1: 'roundedRectangle',
  2: 'circle',
  3: 'ellipse',
  4: 'diamond',
  5: 'parallelogram'
}

/** 后端 EdgeStyle 数字 → simple-mind-map lineDasharray */
const edgeStyleMap: Record<number, string> = {
  0: 'none',
  1: '6,4',
  2: '2,2',
  3: 'none'
}

/** ============================================================
 *  data_change_detail 事件结构（来自 simple-mind-map Command.js）
 *  ============================================================ */
interface DetailNode {
  isRoot?: boolean
  data: Record<string, unknown> & {
    uid: string
    text?: string
    expand?: boolean
    id?: string
    backendId?: string
    dir?: string
    note?: string
  }
  children: DetailNode[]
}

interface DiffItem {
  action: 'create' | 'update' | 'delete'
  data: DetailNode
  oldData?: DetailNode
}

/** ============================================================
 *  增量同步 composable
 *  ============================================================ */
export function useMindMapSync(opts: {
  getMindMapInstance: () => MindMap | null
  nodesStore: NodesStore
  readonly: ComputedRef<boolean>
}) {
  const { getMindMapInstance, nodesStore, readonly } = opts

  /** 防止 setData 触发 data_change 循环 */
  const isSettingData = ref(false)

  /** ============================================================
   *  同步状态（供 UI 实时展示保存进度）
   *  ============================================================ */
  const syncStatus = ref<SyncStatus>('idle')
  /** pending 操作计数（debounce 定时器 + 结构队列 + pendingCreates） */
  const pendingCount = ref(0)
  /** 累计错误数（用于提示用户） */
  const errorCount = ref(0)
  /** 最后一次成功保存的时间 */
  const lastSavedAt = shallowRef<Date | null>(null)

  /** 标记开始一个同步操作 */
  function markSyncing() {
    pendingCount.value++
    syncStatus.value = 'syncing'
  }

  /** 标记一个同步操作成功完成 */
  function markSaved() {
    pendingCount.value = Math.max(0, pendingCount.value - 1)
    if (pendingCount.value === 0) {
      // 存在历史失败时保持 error 状态，避免 UI 误报“已保存”
      if (errorCount.value > 0) {
        syncStatus.value = 'error'
        return
      }
      syncStatus.value = 'saved'
      lastSavedAt.value = new Date()
      // 2 秒后回到 idle
      setTimeout(() => {
        if (syncStatus.value === 'saved') syncStatus.value = 'idle'
      }, 2000)
    }
  }

  /** 标记一个同步操作失败 */
  function markError() {
    pendingCount.value = Math.max(0, pendingCount.value - 1)
    errorCount.value++
    syncStatus.value = 'error'
  }

  /** 标记一个同步操作被跳过（目标节点已不存在，语义上视为已作废：
   *  只回退计数，不计错误、不改 error/saved 状态；计数清零且无错误时回到 idle） */
  function markSkipped() {
    pendingCount.value = Math.max(0, pendingCount.value - 1)
    if (pendingCount.value === 0 && errorCount.value === 0) {
      syncStatus.value = 'idle'
    }
  }

  /** 记录最新鼠标屏幕坐标，供 beforeDragEnd 判定方向用 */
  let lastMouseClientX = 0
  let lastMouseClientY = 0

  /** ============================================================
   *  ID 映射：simple-mind-map 内部 uid → 后端数据库 ID
   *  这是增量同步最核心的状态
   *  ============================================================ */
  const uidToBackendId = new Map<string, string>()

  /**
   * 根据 uid 获取后端 ID（找不到返回 null）
   */
  function getBackendId(uid: string): string | null {
    return uidToBackendId.get(uid) ?? null
  }

  /**
   * 写入后端 ID 到 simple-mind-map 实际渲染节点
   * 同时写入：
   *   1. nodeData.id = backendId           ← 供 flatten 兼容使用
   *   2. nodeData.data.backendId = backendId ← 存在 data 内部，随数据序列化/拷贝保留
   *   3. uidToBackendId.set(uid, backendId)
   */
  function writeBackendIdToNode(uid: string, backendId: string) {
    const inst = getMindMapInstance()
    const root = inst?.renderer.root
    if (!root) return
    const walk = (node: any) => {
      if (node.getData?.('uid') === uid) {
        // simple-mind-map 渲染节点：通过 setData 修改 nodeData.data
        node.setData({ backendId, id: backendId })
        // 同时修改渲染节点本身的 id 属性（某些情况下 nodeData.id 直接映射 node.id）
        if (node.nodeData) {
          node.nodeData.id = backendId
        }
        return true
      }
      if (node.children) {
        for (const c of node.children) {
          if (walk(c)) return true
        }
      }
      return false
    }
    walk(root)
    // 注册映射
    uidToBackendId.set(uid, backendId)
  }

  /**
   * 写入后端 ID 到一个纯数据节点（data_change_detail 里的 DetailNode 树）
   * 保证后续 diff 时数据中已经带上 backendId
   */
  function writeBackendIdToDetailNode(node: DetailNode, backendId: string) {
    node.data.id = backendId
    node.data.backendId = backendId
  }

  /** ============================================================
   *  并发控制：
   *    - 结构性操作（create/move/remove/reorder）：串行 Promise 队列
   *    - 文本修改：每个节点独立的 debounce
   *    - 展开/折叠：每个节点独立的 debounce
   *    - 新建中的节点：pendingCreates 保存 create Promise，供 update 等待
   *  ============================================================ */
  let opQueue: Promise<void> = Promise.resolve()

  /** 失败操作条目：key 用于同类操作去重（debounce 类=backendId；结构操作=null 不去重） */
  interface FailedOp {
    key: string | null
    fn: () => Promise<void>
  }

  /** 失败操作重试队列：保存可重放的 API 调用闭包，供主动保存/重试时重放。
   *  失败不再直接丢弃，避免“点了保存但数据丢失”。 */
  const failedOps = ref<Array<FailedOp>>([])

  /** 淘汰某个 key 的失败项：调度到该节点的新修改时，旧失败值重放已无意义 */
  function dropFailedOpsByKey(key: string) {
    failedOps.value = failedOps.value.filter((op) => op.key !== key)
  }

  /** 重放所有失败操作（逐个串行入队）；再次失败的会重新入队，等待下次重试 */
  async function retryFailedOps(): Promise<void> {
    const failed = failedOps.value.slice()
    failedOps.value = []
    if (failed.length === 0) return
    const tasks = failed.map((op) => enqueueStructuralOp(op.fn, op.key))
    await Promise.allSettled(tasks)
  }

  /** 判断错误是否为“目标资源不存在”（404）。这类失败重放永远 404，重试无意义。 */
  function isNotFoundError(e: unknown): boolean {
    return typeof e === 'object' && e !== null &&
      'response' in e && (e as { response?: { status?: number } }).response?.status === 404
  }

  function enqueueStructuralOp(
    fn: () => Promise<void>,
    key?: string | null
  ): Promise<void> {
    markSyncing()
    opQueue = opQueue
      .catch(() => { })
      .then(async () => {
        await fn()
      })
      .then(() => {
        markSaved()
      })
      .catch((e: unknown) => {
        // 目标节点已被删除（404）→ 重放永远 404，语义上等同于操作已作废，
        // 丢弃且不计错误，仅回退计数，避免永久霸占 errorCount。
        if (isNotFoundError(e)) {
          // 该 key 若还有失败项残留（首次失败入队前的旧值），一并作废，避免写回死节点
          if (key != null) dropFailedOpsByKey(key)
          markSkipped()
          return
        }
        // 其余失败（网络错误等）不丢弃：加入重试队列，供主动保存/重试时重放。
        // 带 key 的操作（debounce 类）同 key 只保留最新一次，避免重放旧值覆盖新值。
        if (key != null) {
          failedOps.value = failedOps.value.filter((op) => op.key !== key)
        }
        failedOps.value.push({ key: key ?? null, fn })
        markError()
      })
    return opQueue
  }

  /** 正在 create 中的节点：uid → Promise<backendId>
   *  用于：创建节点后用户立即修改文字时，update 先 await 这个 Promise 再执行。 */
  const pendingCreates = new Map<string, Promise<string>>()

  /**
   * 获取节点 backendId：
   *   1. 先从 uidToBackendId 取（立即命中的直接返回）
   *   2. 如果没有，看 pendingCreates 中是否有 create 在飞 → await 它，返回 backendId
   *   3. 都没有返回 null（非关键路径调用方自行处理）
   */
  async function getBackendIdOrWait(uid: string): Promise<string | null> {
    const direct = getBackendId(uid)
    if (direct) return direct
    const pending = pendingCreates.get(uid)
    if (pending) {
      try {
        return await pending
      } catch {
        return null
      }
    }
    return null
  }

  /** 节点级 debounce 条目：定时器 + flush 函数，flush 时可直接调用 */
  interface DebounceEntry {
    timer: ReturnType<typeof setTimeout>
    flush: () => Promise<void>
  }

  /** 节点级文本 debounce（key = backendId，保证 data_change_detail 和 node_text_edit_change 共用同一把锁） */
  const textDebounceTimers = new Map<string, DebounceEntry>()
  /** 节点级折叠 debounce（key = backendId） */
  const collapseDebounceTimers = new Map<string, DebounceEntry>()
  /** 节点级备注 debounce（key = backendId） */
  const noteDebounceTimers = new Map<string, DebounceEntry>()
  /** 节点级 extraData debounce（key = backendId，关联线数据同步用） */
  const extraDataDebounceTimers = new Map<string, DebounceEntry>()

  /** 是否存在仍未完成写入的操作（防抖窗口中的修改 + 飞行中请求 + 待创建节点）。
   *  用于 beforeunload 判断是否需要拦截刷新/关闭。 */
  function hasPendingWriteOps(): boolean {
    if (pendingCount.value > 0) return true
    if (pendingCreates.size > 0) return true
    if (failedOps.value.length > 0) return true
    if (textDebounceTimers.size > 0) return true
    if (collapseDebounceTimers.size > 0) return true
    if (noteDebounceTimers.size > 0) return true
    if (extraDataDebounceTimers.size > 0) return true
    return false
  }

  function clearPerNodeTimers(uid: string, backendId?: string | null) {
    // 清除 uid → backendId 对应的 timer（可能只知道其中一个）
    const bid = backendId ?? getBackendId(uid)
    if (bid) {
      const t = textDebounceTimers.get(bid)
      if (t) clearTimeout(t.timer)
      textDebounceTimers.delete(bid)
      const c = collapseDebounceTimers.get(bid)
      if (c) clearTimeout(c.timer)
      collapseDebounceTimers.delete(bid)
      const n = noteDebounceTimers.get(bid)
      if (n) clearTimeout(n.timer)
      noteDebounceTimers.delete(bid)
      const e = extraDataDebounceTimers.get(bid)
      if (e) clearTimeout(e.timer)
      extraDataDebounceTimers.delete(bid)
    }
  }

  /** 画布整体重建（undo/redo/版本恢复）前作废所有挂起的节点级写入：
   *  debounce 定时器与失败队列里的闭包都捕获着 pre-reload 旧值，
   *  画布已被后端状态覆盖，重放/延迟触发只会把旧数据写回去。 */
  function invalidatePendingNodeWrites() {
    const clearMap = (map: Map<string, DebounceEntry>) => {
      map.forEach((entry) => clearTimeout(entry.timer))
      map.clear()
    }
    clearMap(textDebounceTimers)
    clearMap(collapseDebounceTimers)
    clearMap(noteDebounceTimers)
    clearMap(extraDataDebounceTimers)
    failedOps.value = []
  }

  /** ============================================================
   *  全局鼠标位置追踪
   *  ============================================================ */
  function bindGlobalMouseTracker() {
    const updatePos = (clientX: number, clientY: number) => {
      lastMouseClientX = clientX
      lastMouseClientY = clientY
    }
    document.addEventListener('mousemove', (e) => {
      updatePos(e.clientX, e.clientY)
    }, { passive: true })
    document.addEventListener('touchmove', (e) => {
      if (e.touches && e.touches.length > 0) {
        updatePos(e.touches[0].clientX, e.touches[0].clientY)
      } else if (e.changedTouches && e.changedTouches.length > 0) {
        updatePos(e.changedTouches[0].clientX, e.changedTouches[0].clientY)
      }
    }, { passive: true })
    document.addEventListener('touchstart', (e) => {
      if (e.touches && e.touches.length > 0) {
        updatePos(e.touches[0].clientX, e.touches[0].clientY)
      }
    }, { passive: true })
  }

  /** ============================================================
   *  后端节点 → simple-mind-map 数据
   *
   *  【关键修复】：给每个节点的 data.uid 直接设置为后端 ID。
   *
   *  原因：simple-mind-map 的 handleData() → createUidForAppointNodes()
   *  会对没有 data.uid 的节点自动生成随机 uid。
   *  如果我们不设置 uid，每次 reloadMindMap() 后所有节点都会获得新的随机 uid，
   *  导致 uidToBackendId 映射全部失效（uid 变了，但映射还没重建）。
   *  在 scanAndRegisterIdMappingsAfterSetData() 跑完之前（150ms 窗口），
   *  任何 getBackendId() 都返回 null → parentId = null。
   *
   *  修复后：uid === backendId === 后端 GUID，三者恒等，映射永不需要重建。
   *  ============================================================ */
  function convertToMindMapData(nodes: NodeDto[]): unknown {
    if (nodes.length === 0) return null

    // 清空旧映射（下面会立即重新注册，不存在空窗口）
    uidToBackendId.clear()

    const nodeMap = new Map<string, Record<string, unknown>>()
    const roots: unknown[] = []

    // 1. 创建节点，uid / id / backendId 三者统一为后端 ID
    for (const n of nodes) {
      const data: Record<string, unknown> = {
        text: n.icon ? `${n.icon} ${n.title}` : n.title,
        expand: !n.isCollapsed,
        uid: n.id,         // ★ 关键：用后端 ID 作为 uid，阻止 simple-mind-map 重新生成
        id: n.id,          // simple-mind-map 节点 id
        backendId: n.id    // 稳定的后端 ID 标记（会随拷贝/序列化保留）
      }
      if (n.color) data.color = n.color
      if (n.fontSize) data.fontSize = n.fontSize
      if (n.fontFamily) data.fontFamily = n.fontFamily
      if (n.backgroundColor) data.fillColor = n.backgroundColor
      if (n.borderColor) data.borderColor = n.borderColor
      if (n.shape != null && n.shape in shapeMap) data.shape = shapeMap[n.shape]
      if (n.edgeColor) data.lineColor = n.edgeColor
      if (n.edgeStyle != null && n.edgeStyle in edgeStyleMap) data.lineDasharray = edgeStyleMap[n.edgeStyle]
      if (n.note) data.note = n.note
      // 从 ExtraData 还原关联线数据（associativeLine* 系列字段）
      if (n.extraData) {
        try {
          const extra = JSON.parse(n.extraData)
          if (Array.isArray(extra.associativeLineTargets) && extra.associativeLineTargets.length > 0) {
            data.associativeLineTargets = extra.associativeLineTargets
          }
          if (Array.isArray(extra.associativeLinePoint)) {
            data.associativeLinePoint = extra.associativeLinePoint
          }
          if (Array.isArray(extra.associativeLineTargetControlOffsets)) {
            data.associativeLineTargetControlOffsets = extra.associativeLineTargetControlOffsets
          }
          if (extra.associativeLineText && typeof extra.associativeLineText === 'object') {
            data.associativeLineText = extra.associativeLineText
          }
          if (extra.associativeLineStyle && typeof extra.associativeLineStyle === 'object') {
            data.associativeLineStyle = extra.associativeLineStyle
          }
          // 摘要数据（单节点摘要，数组结构）
          if (Array.isArray(extra.generalization) && extra.generalization.length > 0) {
            data.generalization = extra.generalization
          }
          // 外框数据（对象，包含 groupId/radius/strokeWidth/strokeColor/strokeDasharray/fill/text 等）
          if (extra.outerFrame && typeof extra.outerFrame === 'object') {
            data.outerFrame = extra.outerFrame
          }
        } catch {
          // extraData 不是合法 JSON，忽略
        }
      }
      // 仅根节点的直接子节点设置明确的 dir，非根直接子节点不显式设置 dir，交由 simple-mind-map 向上继承分支方向
      if (n.parentId && n.parentId === nodesStore.rootNode?.id) {
        if (n.direction === 0) data.dir = 'left'
        else data.dir = 'right'
      }

      const nodeData = {
        id: n.id,
        data,
        children: [] as unknown[]
      }
      nodeMap.set(String(n.id), nodeData)

      // ★ 立即注册映射，不需要等 scanAndRegister
      uidToBackendId.set(n.id, n.id)
    }

    // 2. 构建父子关系
    for (const n of nodes) {
      const nodeData = nodeMap.get(String(n.id)) as { children: unknown[] }
      const parentIdStr = n.parentId != null ? String(n.parentId) : null
      if (parentIdStr && nodeMap.has(parentIdStr)) {
        const parentData = nodeMap.get(parentIdStr) as { children: unknown[] }
        parentData.children.push(nodeData)
      } else {
        roots.push(nodeData)
      }
    }

    if (roots.length === 0) return null

    const root = roots[0] as { children: { data: Record<string, unknown> }[] }
    for (const child of root.children) {
      if (!child.data.dir) child.data.dir = 'right'
    }

    // 3. 【关键】：首次 setData 后 simple-mind-map 会给每个节点生成 data.uid
    //    我们在 reloadMindMap 中扫描一次渲染树，建立 uid → backendId 的映射。
    //    这里把 backendId 都写到了 data.backendId，扫描时通过 data.backendId 反查即可。

    return roots[0]
  }

  /**
   * 在 setData 之后遍历渲染树，按 data.backendId 建立 uid → backendId 映射
   */
  function scanAndRegisterIdMappingsAfterSetData() {
    const inst = getMindMapInstance()
    const root = inst?.renderer.root
    if (!root) return
    const walk = (node: any) => {
      const uid = node.getData?.('uid') as string | undefined
      const backendId = node.getData?.('backendId') as string | undefined
        ?? node.nodeData?.id
        ?? node.getData?.('id') as string | undefined
      if (uid && backendId) {
        uidToBackendId.set(uid, backendId)
      }
      if (node.children) {
        node.children.forEach(walk)
      }
    }
    walk(root)
  }

  /** 重新加载画布数据 */
  function reloadMindMap() {
    const inst = getMindMapInstance()
    if (!inst) return
    // 画布即将被后端状态整体覆盖：作废所有携带旧值的挂起写入（debounce 定时器 + 失败队列）
    invalidatePendingNodeWrites()
    isSettingData.value = true
    const mindMapData = convertToMindMapData(nodesStore.nodes)
    if (mindMapData) {
      inst.setData(mindMapData)
    }
    setTimeout(() => {
      isSettingData.value = false
      // 数据渲染完成后扫描一次，建立 uid↔backendId 映射
      scanAndRegisterIdMappingsAfterSetData()
    }, 150)
  }

  /** ============================================================
   *  新增节点增量处理
   *
   *  输入：data_change_detail 中 action=create 的 DetailNode（一棵新增子树）
   *  过程：深度优先前序（父先建，再建子）
   *        建完每个节点，把后端 ID 写回渲染节点和 data 对象
   *        ★ create Promise 存到 pendingCreates，供后续 update 等待
   *  ============================================================ */
  async function handleCreateTree(root: DetailNode) {
    const parentUid = getParentUidFromFullTree(root.data.uid)
    const parentBackendId = parentUid ? getBackendId(parentUid) ?? null : null

    const tasks: Array<{ node: DetailNode; parentBackendId: string | null; sortOrder: number }> = []
    const collect = (
      node: DetailNode,
      pBackendId: string | null,
      sortIdx: number
    ) => {
      tasks.push({ node, parentBackendId: pBackendId, sortOrder: sortIdx })
      node.children.forEach((c, i) => {
        // 子节点的 parentBackendId 要等父节点创建完才能知道；先占位，后续动态替换
        collect(c, `__PENDING_PARENT_${node.data.uid}__` as any, i)
      })
    }
    collect(root, parentBackendId, computeSortOrderForNewNode(parentUid, root.data.uid))

    // 依次创建，每创建一个就写回后端 ID
    for (const task of tasks) {
      let pId: string | null = task.parentBackendId as any
      if (typeof pId === 'string' && pId.startsWith('__PENDING_PARENT_')) {
        const pendingUid = pId.slice('__PENDING_PARENT_'.length, -2)
        // await 父节点的 create（如还在 pending），保证 parentId 必取到
        pId = await getBackendIdOrWait(pendingUid)
      }

      const uid = task.node.data.uid
      // 已存在映射，说明是 undo 恢复？直接跳过
      if (uidToBackendId.has(uid)) continue
      if (pendingCreates.has(uid)) {
        // 同一个 uid 已经在 create 中（data_change_detail 多次触发？）等待完成即可
        try { await pendingCreates.get(uid)! } catch { }
        continue
      }

      const title = extractTitleFromText(task.node.data.text ?? '')

      // 检测是否根节点直接子节点 → 设置 direction
      let direction: 0 | 1 | undefined = undefined
      if (pId && pId === nodesStore.rootNode?.id) {
        direction = task.node.data.dir === 'left' ? 0 : 1
      }

      // ★ 先把 create Promise 注册到 pendingCreates，让后续 handleUpdate 能 await
      const createPromise = (async (): Promise<string> => {
        try {
          const created = await nodesStore.create({
            parentId: pId,
            title,
            sortOrder: task.sortOrder,
            isCollapsed: task.node.data.expand === false,
            direction
          })
          // 写回渲染节点 + DetailNode + 映射表
          writeBackendIdToNode(uid, created.id)
          writeBackendIdToDetailNode(task.node, created.id)
          return created.id
        } finally {
          pendingCreates.delete(uid)
        }
      })()
      pendingCreates.set(uid, createPromise)
      // 等这个节点 create 完成再建下一个（保证子节点创建时父 backendId 已可用）
      await createPromise
    }
  }

  /** ============================================================
   *  删除节点增量处理
   *
   *  输入：action=delete 的 DetailNode 树根
   *  过程：用根节点的 backendId 调一次 remove()
   *        后端支持级联删除，不需要遍历子节点
   *  ============================================================ */
  async function handleDeleteTree(root: DetailNode) {
    const uid = root.data.uid
    const backendId = getBackendId(uid)
    // 清理该子树所有 uid 映射
    const walk = (n: DetailNode) => {
      uidToBackendId.delete(n.data.uid)
      clearPerNodeTimers(n.data.uid)
      n.children.forEach(walk)
    }
    walk(root)

    if (backendId) {
      await nodesStore.remove(backendId)
    }
  }

  /** ============================================================
   *  更新节点增量处理
   *
   *  可能包含：
   *    1. 文本变化 → debounced nodesStore.update(title)
   *    2. 展开/折叠变化 → debounced nodesStore.update(isCollapsed)
   *    3. direction 变化 → nodesStore.update(direction)
   *  结构性变化（父节点/同级排序）由 handleStructuralChangesBatch 统一处理，
   *  入口在 processDataChangeDetail（按整批 update diff 合并处理）。
   *  ============================================================ */
  async function handleUpdate(diff: DiffItem) {
    const { data, oldData } = diff
    if (!oldData) return
    const uid = data.data.uid

    // ★ 关键：如果 backendId 暂时没有，先等 create Promise（可能正在飞）
    // 避免「新增节点后立即改文字 → backendId=null → 直接 return 丢弃修改」
    const backendId = await getBackendIdOrWait(uid)
    if (!backendId) {
      console.warn('[sync] handleUpdate skipped: no backendId for uid', uid)
      return
    }

    // ---------- 1. 文本变化（debounced，key=backendId，与 node_text_edit_change 共用一把锁）----------
    const oldText = (oldData.data.text as string | undefined) ?? ''
    const newText = (data.data.text as string | undefined) ?? ''
    if (oldText !== newText) {
      scheduleTextUpdate(backendId, newText)
    }

    // ---------- 2. 展开/折叠变化（debounced，key=backendId） ----------
    const oldExpand = oldData.data.expand !== false
    const newExpand = data.data.expand !== false
    if (oldExpand !== newExpand && !collapseBatchSyncActive) {
      scheduleCollapseUpdate(backendId, !newExpand)
    }
    // 若 collapseBatchSyncActive（批量展开/折叠进行中），折叠变化的目标值
    // 已由 applyExpandCollapse 内的单次 batchUpdate 统一落库，跳过逐节点同步，
    // 避免 N 个节点触发 N 个防抖请求；其余字段分支照常处理。

    // ---------- 2.5 备注变化（debounced，key=backendId） ----------
    // note 可能为空字符串（清空备注），用 ?? '' 归一化比较
    const oldNote = (oldData.data.note as string | undefined) ?? ''
    const newNote = (data.data.note as string | undefined) ?? ''
    if (oldNote !== newNote) {
      scheduleNoteUpdate(backendId, newNote)
    }

    // ---------- 2.6 关联线 + 摘要数据变化（associativeLine* / generalization，debounced） ----------
    // 这些字段由 AssociativeLine 插件 / 核心库通过 SET_NODE_DATA 写入，触发 data_change_detail。
    // 比较序列化后的 JSON 即可判断是否变化，变化时打包所有 extraData 相关字段同步到后端。
    const extraFields = ['associativeLineTargets', 'associativeLinePoint',
      'associativeLineTargetControlOffsets', 'associativeLineText', 'associativeLineStyle',
      'generalization', 'outerFrame'] as const
    let extraChanged = false
    for (const f of extraFields) {
      const oldVal = JSON.stringify((oldData.data as any)[f] ?? null)
      const newVal = JSON.stringify((data.data as any)[f] ?? null)
      if (oldVal !== newVal) {
        extraChanged = true
        break
      }
    }
    if (extraChanged) {
      // 收集当前节点所有 extraData 相关字段，打包成 JSON 同步
      const extraObj: Record<string, unknown> = {}
      for (const f of extraFields) {
        const v = (data.data as any)[f]
        if (v !== undefined && v !== null) {
          extraObj[f] = v
        }
      }
      scheduleExtraDataUpdate(backendId, JSON.stringify(extraObj))
    }

    // ---------- 3. direction 变化（如果是根节点直接子节点则同步到后端） ----------
    const oldDir = (oldData.data.dir as string | undefined) ?? ''
    const newDir = (data.data.dir as string | undefined) ?? ''
    if (oldDir !== newDir && (newDir === 'left' || newDir === 'right')) {
      const parentUid = getParentUidFromFullTree(uid)
      const isRootChild = parentUid != null && getBackendId(parentUid) === nodesStore.rootNode?.id
      if (isRootChild) {
        const backendDir: 0 | 1 = newDir === 'left' ? 0 : 1
        const currentBackend = nodesStore.findNode(backendId)?.direction
        if (currentBackend !== backendDir) {
          enqueueStructuralOp(async () => {
            await nodesStore.update(backendId, { direction: backendDir })
          })
        }
      }
    }
  }

  /** 文本更新核心逻辑（不带同步状态标记，供 flush 与失败重试复用） */
  async function runTextUpdate(backendId: string, rawText: string): Promise<void> {
    const title = extractTitleFromText(rawText, backendId)
    await nodesStore.update(backendId, { title })
  }

  /** 文本更新调度（debounce 400ms，key = backendId）
   *  key 统一使用 backendId，保证 data_change_detail 和 node_text_edit_change
   *  对同一节点的连续文本触发最终只产生一次 API 请求。
   *  实际执行并入 opQueue 串行队列，保证与结构变更（移动/排序）顺序一致，
   *  失败重试与状态标记由 enqueueStructuralOp 统一处理。 */
  function scheduleTextUpdate(backendId: string, rawText: string) {
    const existing = textDebounceTimers.get(backendId)
    if (existing) clearTimeout(existing.timer)
    // 该节点有新的文字修改待同步 → 淘汰该节点之前的文字失败项（前缀隔离字段类型）
    dropFailedOpsByKey(`text:${backendId}`)
    // flush 函数闭包捕获最新 rawText，确保 flush 时用的是最后一次修改的值
    const flush = () => {
      textDebounceTimers.delete(backendId)
      return enqueueStructuralOp(() => runTextUpdate(backendId, rawText), `text:${backendId}`)
    }
    textDebounceTimers.set(backendId, {
      timer: setTimeout(flush, 400),
      flush
    })
  }

  /** 折叠更新核心逻辑（不带同步状态标记，供 flush 与失败重试复用） */
  async function runCollapseUpdate(backendId: string, isCollapsed: boolean): Promise<void> {
    await nodesStore.update(backendId, { isCollapsed })
  }

  /** 折叠更新调度（debounce 250ms，key = backendId） */
  function scheduleCollapseUpdate(backendId: string, isCollapsed: boolean) {
    const existing = collapseDebounceTimers.get(backendId)
    if (existing) clearTimeout(existing.timer)
    // 该节点有新的折叠修改待同步 → 淘汰该节点之前的折叠失败项（前缀隔离字段类型）
    dropFailedOpsByKey(`collapse:${backendId}`)
    const flush = () => {
      collapseDebounceTimers.delete(backendId)
      return enqueueStructuralOp(() => runCollapseUpdate(backendId, isCollapsed), `collapse:${backendId}`)
    }
    collapseDebounceTimers.set(backendId, {
      timer: setTimeout(flush, 250),
      flush
    })
  }

  /** 批量展开/折叠同步抑制标记：
   *  「层级」菜单操作会一次性改变大量节点的 expand 状态，
   *  data_change_detail 会为每个节点派发 update diff，若不抑制将触发
   *  逐节点 debounced update（N 个请求）。抑制窗口内由 applyExpandCollapse
   *  自行完成单次 batchUpdate 落库，diff 的折叠分支被跳过。 */
  let collapseBatchSyncActive = false

  /** ============================================================
   *  批量展开/折叠（层级菜单）
   *
   *  mode:
   *    - 'expand-all'   全部展开（simple-mind-map EXPAND_ALL）
   *    - 'collapse-all' 全部折叠（UNEXPAND_ALL，仅根节点保持展开，收起后根节点居中）
   *    - 数字 N         收起到第 N+1 级（UNEXPAND_TO_LEVEL，N=库内层级，
   *                     layerIndex < N 的节点展开，其余有子节点的收起）
   *
   *  流程：
   *    1. 与库内命令一致的逻辑预计算将发生变化的节点（仅统计有子节点的节点，
   *       叶子节点的 isCollapsed 是无意义状态，不写库）
   *    2. 置抑制标记 → execCommand 交由库修改 expand 并重渲染
   *    3. 单次 batchUpdate 落库（入 undo/redo 栈，Ctrl+Z 一次整体还原）
   *    4. 全部展开完成后自适应居中视图（node_tree_render_end 一次性监听）
   *  ============================================================ */
  async function applyExpandCollapse(mode: 'expand-all' | 'collapse-all' | number): Promise<void> {
    const inst = getMindMapInstance()
    if (!inst) return
    const renderTree = (inst as any).renderer?.renderTree
    if (!renderTree) return

    // readonly 模式：纯前端视图操作，不涉及后端同步，直接执行后返回
    if (readonly.value) {
      if (mode === 'expand-all') {
        ; (inst as any).execCommand('EXPAND_ALL')
        const onRenderEnd = () => {
          ; (inst as any).off('node_tree_render_end', onRenderEnd)
          inst.view?.reset()
        }
        ; (inst as any).on('node_tree_render_end', onRenderEnd)
        setTimeout(() => { (inst as any).off('node_tree_render_end', onRenderEnd) }, 5000)
      } else if (mode === 'collapse-all') {
        ; (inst as any).execCommand('UNEXPAND_ALL')
      } else {
        ; (inst as any).execCommand('UNEXPAND_TO_LEVEL', mode)
      }
      return
    }

    // 1. 预计算将发生变化的节点（与 expandAllNode / unexpandAllNode / expandToLevel 逻辑一致）
    const changed: Array<{ backendId: string; isCollapsed: boolean }> = []
    const walk = (node: any, depth: number) => {
      const hasChildren = Array.isArray(node.children) && node.children.length > 0
      if (hasChildren) {
        const targetExpand = mode === 'expand-all'
          ? true
          : mode === 'collapse-all'
            ? depth !== 0 // 根节点永不折叠
            : depth < mode
        if (node.data.expand !== targetExpand) {
          const backendId = getBackendId(node.data.uid)
          if (backendId) {
            changed.push({ backendId, isCollapsed: !targetExpand })
          }
        }
        node.children.forEach((c: any) => walk(c, depth + 1))
      }
    }
    walk(renderTree, 0)

    // 2. 置抑制标记（覆盖 addHistory 节流 + data_change_detail 派发窗口）
    collapseBatchSyncActive = true

    try {
      // 3. 交给库执行状态修改与重渲染
      if (mode === 'expand-all') {
        ; (inst as any).execCommand('EXPAND_ALL')
        // 全部展开后画布通常溢出 → 渲染完成后自适应居中
        const onRenderEnd = () => {
          ; (inst as any).off('node_tree_render_end', onRenderEnd)
          inst.view?.reset()
        }
        ; (inst as any).on('node_tree_render_end', onRenderEnd)
        // 兜底清理：若 5 秒内没有渲染完成事件，移除一次性监听防止泄漏
        setTimeout(() => { (inst as any).off('node_tree_render_end', onRenderEnd) }, 5000)
      } else if (mode === 'collapse-all') {
        ; (inst as any).execCommand('UNEXPAND_ALL') // 库内自带收起后根节点居中
      } else {
        ; (inst as any).execCommand('UNEXPAND_TO_LEVEL', mode)
      }

      // 4. 单次批量落库（走结构操作队列：统一同步状态标记与失败重试；
      //    batchUpdate 成功后 pushHistory，产生一条 undo 记录）
      if (changed.length > 0) {
        const items = changed.map((c) => ({ id: c.backendId, isCollapsed: c.isCollapsed }))
        await enqueueStructuralOp(
          () => nodesStore.batchUpdate(items),
          'collapse-batch'
        )
      }
    } finally {
      // 等待被抑制的 diff 派发完毕（addHistory 节流默认窗口内），再恢复逐节点同步
      setTimeout(() => { collapseBatchSyncActive = false }, 600)
    }
  }

  /** 备注更新核心逻辑（不带同步状态标记，供 flush 与失败重试复用） */
  async function runNoteUpdate(backendId: string, note: string): Promise<void> {
    await nodesStore.update(backendId, { note })
  }

  /** 备注更新调度（debounce 600ms，key = backendId）
   *  备注是富文本 HTML，内容较长且编辑频繁，用较长 debounce 减少请求次数。
   *  note 为空字符串时也要同步（清空操作），后端 Note is not null 判断会接受空串。 */
  function scheduleNoteUpdate(backendId: string, note: string) {
    const existing = noteDebounceTimers.get(backendId)
    if (existing) clearTimeout(existing.timer)
    // 该节点有新的备注修改待同步 → 淘汰该节点之前的备注失败项（前缀隔离字段类型）
    dropFailedOpsByKey(`note:${backendId}`)
    const flush = () => {
      noteDebounceTimers.delete(backendId)
      return enqueueStructuralOp(() => runNoteUpdate(backendId, note), `note:${backendId}`)
    }
    noteDebounceTimers.set(backendId, {
      timer: setTimeout(flush, 600),
      flush
    })
  }

  /** 关联线数据更新核心逻辑（不带同步状态标记，供 flush 与失败重试复用） */
  async function runExtraDataUpdate(backendId: string, extraData: string): Promise<void> {
    await nodesStore.update(backendId, { extraData })
  }

  /** 关联线数据更新调度（debounce 500ms，key = backendId）
   *  将 associativeLine* 系列字段序列化为 JSON 存入 Node.ExtraData。
   *  连线操作（创建/删除/改样式/改文字）都会触发，用 debounce 合并连续操作。 */
  function scheduleExtraDataUpdate(backendId: string, extraData: string) {
    const existing = extraDataDebounceTimers.get(backendId)
    if (existing) clearTimeout(existing.timer)
    // 该节点有新的关联线/摘要修改待同步 → 淘汰该节点之前的 extraData 失败项（前缀隔离字段类型）
    dropFailedOpsByKey(`extraData:${backendId}`)
    const flush = () => {
      extraDataDebounceTimers.delete(backendId)
      return enqueueStructuralOp(() => runExtraDataUpdate(backendId, extraData), `extraData:${backendId}`)
    }
    extraDataDebounceTimers.set(backendId, {
      timer: setTimeout(flush, 500),
      flush
    })
  }

  /**
   * 结构性变化批量处理（父节点变更 / 同级排序），输入为一次 data_change_detail
   * 事件中的全部 update diff。
   *
   * 【关键背景】simple-mind-map 的 diff 按 uid 对比节点对象（含 children 数组）。
   * 拖拽移动节点时（moveNodeTo / insertTo），被移动节点自身的 data 不变，
   * 它自己不会出现在 diff 里；diff 只会发给旧父节点和新父节点（children 变了）。
   * 因此除了检查 diff 节点自身，还必须检查 children 数组发生变化的每个子节点，
   * 否则普通节点之间的拖拽移动永远不会同步到后端（刷新后回到原位）。
   */
  async function handleStructuralChangesBatch(diffs: DiffItem[]) {
    const inst = getMindMapInstance()
    const renderRoot = inst?.renderer?.root
    if (!inst || !renderRoot) return

    // 取当前整棵渲染树的 data 快照，构造关系图
    const currentSnapshot = inst.getData() as any
    if (!currentSnapshot) return
    const { parentOf, sortOrderOf } = buildRelationalMapsFromRaw(currentSnapshot)

    // 1. 收集需要结构检查的 uid：每个 diff 节点自身 + children 数组发生变化的全部新子节点
    const targetUids = new Set<string>()
    for (const diff of diffs) {
      const uid = diff.data.data.uid
      if (!uid) continue
      targetUids.add(uid)
      const oldUids = (diff.oldData?.children ?? []).map((c) => c.data.uid)
      const newUids = (diff.data.children ?? []).map((c) => c.data.uid)
      const childrenChanged =
        oldUids.length !== newUids.length ||
        newUids.some((u, i) => u !== oldUids[i])
      if (childrenChanged) {
        for (const cUid of newUids) targetUids.add(cUid)
      }
    }
    if (targetUids.size === 0) return

    // 2. 第一遍：父节点变化的节点执行 move。
    //    必须先移动再做排序修正，否则旧父节点兄弟压缩排序时会与
    //    尚未移走的节点发生 (MindMapId, ParentId, SortOrder) 唯一索引冲突。
    for (const uid of targetUids) {
      const backendId = await getBackendIdOrWait(uid)
      if (!backendId) continue
      const backendNode = nodesStore.findNode(backendId)
      if (!backendNode) continue

      const newParentUid = parentOf.get(uid) ?? null
      const newParentBackendId = newParentUid
        ? await getBackendIdOrWait(newParentUid) ?? null
        : null
      if (newParentBackendId === (backendNode.parentId ?? null)) continue

      const newSortOrder = sortOrderOf.get(uid) ?? 0
      const isNewParentRoot = newParentBackendId === nodesStore.rootNode?.id
      let newDirection: 0 | 1 | undefined = undefined
      if (isNewParentRoot) {
        const { x: mouseCanvasX } = inst.toPos(lastMouseClientX, lastMouseClientY)
        const { scaleX = 1, translateX = 0 } = inst.draw.transform()
        const rootCanvasCenterX = (renderRoot.left + (renderRoot.width || 0) / 2) * scaleX + translateX
        const targetDir = mouseCanvasX < rootCanvasCenterX ? 'left' : 'right'
        newDirection = targetDir === 'left' ? 0 : 1
      }

      await nodesStore.move(backendId, {
        parentId: newParentBackendId,
        sortOrder: newSortOrder,
        direction: newDirection
      })
    }

    // 3. 第二遍：提交「受影响父节点下完整兄弟集合」的 sortOrder。
    //    修复 Duplicate entry 问题：此前只提交 targetUids 里排序变化的节点，
    //    可能与该父节点下未提交的兄弟节点现有 sortOrder 撞 (MindMapId, ParentId, SortOrder)
    //    唯一索引。现改为对每个受影响父节点，把其全部子节点按渲染树顺序
    //    重编为 0..n-1 一并提交，保证请求自洽、无重复 sortOrder。
    const affectedParentKeys = new Set<string | null>()
    for (const uid of targetUids) {
      const newParentUid = parentOf.get(uid) ?? null
      affectedParentKeys.add(newParentUid)
    }

    const items: NodeBatchItem[] = []
    const seenBackendIds = new Set<string>()
    for (const parentUid of affectedParentKeys) {
      // 收集该父节点下的全部子节点 uid（渲染树顺序）
      const siblingUids: string[] = []
      for (const [uid, p] of parentOf) {
        if ((p ?? null) === (parentUid ?? null)) siblingUids.push(uid)
      }
      siblingUids.sort((a, b) => (sortOrderOf.get(a) ?? 0) - (sortOrderOf.get(b) ?? 0))

      let order = 0
      for (const uid of siblingUids) {
        const backendId = await getBackendIdOrWait(uid)
        if (!backendId) continue
        // 跳过已提交的节点（避免同一次 batch 内重复 id）
        if (seenBackendIds.has(backendId)) continue
        seenBackendIds.add(backendId)
        items.push({ id: backendId, sortOrder: order })
        order++
      }
    }
    if (items.length > 0) {
      await nodesStore.batchUpdate(items)
    }
  }

  /** 从 raw getData() 输出构建 uid → parentUid / sortOrder 映射 */
  function buildRelationalMapsFromRaw(root: any) {
    const parentOf = new Map<string, string | null>()
    const sortOrderOf = new Map<string, number>()
    const walk = (node: any, parentUid: string | null, idx: number) => {
      const uid = node.data?.uid ?? node.id
      if (!uid) return
      parentOf.set(uid, parentUid)
      sortOrderOf.set(uid, idx)
      if (node.children) {
        node.children.forEach((c: any, i: number) => walk(c, uid, i))
      }
    }
    walk(root, null, 0)
    return { parentOf, sortOrderOf }
  }

  /** 计算新增节点在 full tree 下的 sortOrder */
  function computeSortOrderForNewNode(parentUid: string | null, childUid: string): number {
    const inst = getMindMapInstance()
    const raw = inst?.getData() as any
    if (!raw) return 0
    const { sortOrderOf } = buildRelationalMapsFromRaw(raw)
    // 找到该父节点下的最大 sortOrder
    const { parentOf } = buildRelationalMapsFromRaw(raw)
    const siblingOrders: number[] = []
    parentOf.forEach((p, uid) => {
      if (p === parentUid && uid !== childUid) {
        siblingOrders.push(sortOrderOf.get(uid) ?? 0)
      }
    })
    return siblingOrders.length === 0 ? 0 : Math.max(...siblingOrders) + 1
  }

  /** 从 full tree 找到某个 uid 的父 uid */
  function getParentUidFromFullTree(uid: string): string | null {
    const inst = getMindMapInstance()
    const raw = inst?.getData() as any
    if (!raw) return null
    const { parentOf } = buildRelationalMapsFromRaw(raw)
    return parentOf.get(uid) ?? null
  }

  /** 从文本中提取 title（去除 icon 前缀） */
  function extractTitleFromText(text: string, backendId?: string): string {
    if (!text) return ''
    if (backendId) {
      const backendNode = nodesStore.findNode(backendId)
      if (backendNode?.icon && text.startsWith(backendNode.icon + ' ')) {
        return text.substring(backendNode.icon.length + 1)
      }
    }
    return text
  }

  /** ============================================================
   *  data_change_detail 总入口：simple-mind-map 已经帮我们 diff 好了
   *  ============================================================ */
  async function processDataChangeDetail(items: DiffItem[]) {
    if (readonly.value) return
    if (isSettingData.value) return
    if (!Array.isArray(items) || items.length === 0) return

    // 先处理所有 create（需要写回 ID，后续 update/delete 依赖它）
    const creates = items.filter((i) => i.action === 'create')
    // 再处理 delete
    const deletes = items.filter((i) => i.action === 'delete')
    // 最后处理 update（可能引用 create 产生的 backendId）
    const updates = items.filter((i) => i.action === 'update')

    enqueueStructuralOp(async () => {
      for (const c of creates) {
        try {
          await handleCreateTree(c.data)
        } catch (e) {
          console.error('[sync] create failed:', e)
        }
      }
      for (const d of deletes) {
        try {
          await handleDeleteTree(d.data)
        } catch (e) {
          console.error('[sync] delete failed:', e)
        }
      }
    })

    // update 中纯文本/折叠变化是 debounced 的，不需要排队；
    // 注意：handleUpdate 是 async（内部 await getBackendIdOrWait），不 await 在这里
    //       因为它的结果不影响后续批次；所有副作用最终都流进 opQueue
    for (const u of updates) {
      handleUpdate(u).catch((e) => console.error('[sync] handleUpdate failed:', e))
    }

    // 结构性变化（拖拽移动 / 同级排序）：一次事件的所有 update diff 合并为
    // 一个串行操作处理，保证 move 先于兄弟排序修正执行，
    // 避免旧父节点兄弟压缩与尚未移动的节点发生 sortOrder 唯一索引冲突。
    if (updates.length > 0) {
      enqueueStructuralOp(async () => {
        await handleStructuralChangesBatch(updates)
      })
    }
  }

  /** ============================================================
   *  node_text_edit_change：正在编辑中实时文本变化（也是 debounced）
   *  这个事件比 data_change_detail 响应更快，体验更好
   *
   *  关键：scheduleTextUpdate 与 data_change_detail 中的文本分支共用
   *       同一个 key = backendId 的定时器，所以最终只合并成一次请求。
   *  ============================================================ */
  async function handleTextEditChange(payload: {
    node: { getData: (k?: string) => unknown }
    text: string
  }) {
    if (readonly.value) return
    const uid = payload.node.getData?.('uid') as string | undefined
    if (!uid) return
    // ★ 如果 backendId 暂时没有（新创建节点后立即输入），等 create 完成
    const backendId = await getBackendIdOrWait(uid)
    if (!backendId) return
    // 仅用 backendId 作为 key → 与 data_change_detail 中的 scheduleTextUpdate 互相覆盖，
    // 保证连续触发下同一节点最终只产生一次 API 请求
    scheduleTextUpdate(backendId, payload.text)
  }

  /** ============================================================
   *  拖拽方向处理（保持原有逻辑）
   *  ============================================================ */
  function findNodeParentUid(uid: string): unknown {
    const inst = getMindMapInstance()
    const root = inst?.renderer.root
    if (!root) return null
    let result: unknown = null
    const walk = (node: any) => {
      if (result) return
      if (node.getData?.('uid') === uid) {
        result = node.parent?.getData?.('uid') ?? null
        return
      }
      node.children?.forEach(walk)
    }
    walk(root)
    return result
  }

  function cleanDescendantDirs(node: any) {
    if (!node) return
    const recurse = (item: any) => {
      if (!item) return
      if (item.nodeData && item.nodeData.data) {
        delete item.nodeData.data.dir
      }
      if (item.data && typeof item.data === 'object') {
        delete item.data.dir
      }
      if (typeof item.setData === 'function') {
        item.setData({ dir: undefined })
      }
      const children = item.children || item.nodeData?.children
      if (Array.isArray(children)) {
        children.forEach(recurse)
      }
    }
    const children = node.children || node.nodeData?.children
    if (Array.isArray(children)) {
      children.forEach(recurse)
    }
  }

  function handleDragEnd(info: {
    overlapNodeUid?: string
    prevNodeUid?: string
    nextNodeUid?: string
    beingDragNodeList: Array<{
      parent?: { getData: (k?: string) => unknown; isRoot?: boolean }
      setData: (data: Record<string, unknown>) => void
      getData: (k?: string) => unknown
      children?: unknown[]
      nodeData?: { children?: unknown[] }
    }>
  }) {
    const inst = getMindMapInstance()
    if (!inst || readonly.value) return

    const root = inst.renderer.root
    if (!root) return

    const rootUid = root.getData?.('uid')

    const willBeRootChild =
      info.overlapNodeUid === rootUid ||
      (info.prevNodeUid && findNodeParentUid(info.prevNodeUid) === rootUid) ||
      (info.nextNodeUid && findNodeParentUid(info.nextNodeUid) === rootUid)

    // —— P1-3：优化根节点方向预判 ——
    // 规则：
    //   1. 拖到兄弟节点旁边（prev/next 存在）→ 继承该兄弟节点的方向（更符合直觉）
    //   2. 直接拖到根节点上 → 按鼠标位置决定方向
    //   3. 拖到非根节点子节点下 → 清除 dir，继承分支方向
    let targetDir: 'left' | 'right' = 'right'

    if (info.overlapNodeUid === rootUid) {
      // 直接拖到根节点上 → 按鼠标位置判定
      const { x: mouseCanvasX } = inst.toPos(lastMouseClientX, lastMouseClientY)
      const { scaleX = 1, translateX = 0 } = inst.draw.transform()
      const rootCanvasCenterX = (root.left + (root.width || 0) / 2) * scaleX + translateX
      targetDir = mouseCanvasX < rootCanvasCenterX ? 'left' : 'right'
    } else if (info.prevNodeUid || info.nextNodeUid) {
      // 拖到兄弟节点旁边 → 继承兄弟节点的方向
      const siblingUid = info.prevNodeUid || info.nextNodeUid
      const siblingNode = siblingUid ? findRenderNodeByUid(siblingUid) : null
      const siblingDir = siblingNode?.getData?.('dir')
      targetDir = siblingDir === 'left' ? 'left' : 'right'
    }

    if (willBeRootChild) {
      for (const node of info.beingDragNodeList) {
        node.setData({ dir: targetDir })
        cleanDescendantDirs(node)
      }
    } else {
      // 成为非根节点子节点时，清除自身及后代的显式 dir，使之完全继承分支方向
      for (const node of info.beingDragNodeList) {
        node.setData({ dir: undefined })
        cleanDescendantDirs(node)
      }
    }
  }

  /** 根据 uid 在渲染树中查找节点 */
  function findRenderNodeByUid(uid: string): any {
    const inst = getMindMapInstance()
    const root = inst?.renderer?.root
    if (!root) return null
    let result: any = null
    const walk = (node: any) => {
      if (result) return
      if (node.getData?.('uid') === uid) {
        result = node
        return
      }
      node.children?.forEach(walk)
    }
    walk(root)
    return result
  }

  /** ============================================================
   *  每次 layout 完成后扫描根节点直接子节点方向（保持原有逻辑）
   *  ============================================================ */
  async function normalizeRootChildDirections() {
    const inst = getMindMapInstance()
    if (!inst || readonly.value) return
    const root = inst.renderer.root
    if (!root) return

    const rootCenterX = root.left + (root.width || 0) / 2
    const updates: Array<{
      node: { setData: (d: Record<string, unknown>) => void; getData: (k?: string) => unknown }
      uid: string
      targetDir: 'left' | 'right'
      backendDir: 0 | 1
    }> = []

    const scan = (node: any) => {
      if (node.isRoot) {
        node.children?.forEach(scan)
        return
      }
      if (!node.parent || !node.parent.isRoot) return
      const uid = node.getData?.('uid') as string | undefined
      if (!uid) return
      const id = getBackendId(uid)
      if (!id) return
      const centerX = node.left + (node.width || 0) / 2
      const targetDir: 'left' | 'right' = centerX < rootCenterX ? 'left' : 'right'
      const backendDir: 0 | 1 = targetDir === 'left' ? 0 : 1
      const currentBackend = nodesStore.findNode(id)?.direction
      const currentDataDir = node.getData?.('dir')
      if (currentDataDir !== targetDir || currentBackend !== backendDir) {
        updates.push({ node, uid, targetDir, backendDir })
      }
    }
    scan(root)

    if (updates.length === 0) return

    for (const u of updates) {
      u.node.setData({ dir: u.targetDir })
    }

    enqueueStructuralOp(async () => {
      for (const u of updates) {
        const backendId = getBackendId(u.uid)
        if (!backendId) continue
        try {
          await nodesStore.update(backendId, { direction: u.backendDir })
        } catch {
          // 单个失败忽略
        }
      }
    })
  }

  /** ============================================================
   *  Flush：立即触发所有 pending 的 debounce 定时器，返回所有 flush Promise
   *  在路由离开 / 页面刷新 / 返回前调用，防止防抖窗口内修改丢失。
   *  ============================================================ */
  function flushPendingUpdates(): Promise<void>[] {
    const promises: Promise<void>[] = []
    const flushMap = (map: Map<string, DebounceEntry>) => {
      map.forEach((entry, key) => {
        clearTimeout(entry.timer)
        map.delete(key)
        promises.push(entry.flush())
      })
    }
    flushMap(textDebounceTimers)
    flushMap(collapseDebounceTimers)
    flushMap(noteDebounceTimers)
    flushMap(extraDataDebounceTimers)
    return promises
  }

  /** ============================================================
   *  等待所有 pending 操作完成（debounce flush + opQueue + pendingCreates）
   *  返回一个 Promise，resolve 后所有挂起的 API 请求都已飞出。
   *  ============================================================ */
  async function waitForPendingOps(): Promise<void> {
    // 1. 立即触发所有 pending debounce 定时器（不等 400/250/600/500ms 了）
    const flushPromises = flushPendingUpdates()

    // 2. 等待结构操作队列完成
    const queuePromise = opQueue.catch(() => {})

    // 3. 等待所有 pending create 完成
    const createPromises = Array.from(pendingCreates.values())

    await Promise.allSettled([...flushPromises, queuePromise, ...createPromises])
  }

  /** ============================================================
   *  所有事件绑定入口（由 MindMapEditorView.vue 调用）
   *  ============================================================ */
  function bindIncrementalSyncHandlers() {
    const inst = getMindMapInstance()
    if (!inst) return

      // 核心事件：simple-mind-map 内置 diff
      ; (inst as any).on('data_change_detail', processDataChangeDetail)

      // 文本编辑实时 debounce（优先于 data_change_detail 触发的文本更新）
      ; (inst as any).on('node_text_edit_change', handleTextEditChange)
  }

  /** 手动重试：清除错误状态 */
  function clearError() {
    errorCount.value = 0
    if (syncStatus.value === 'error') syncStatus.value = 'idle'
  }

  return {
    isSettingData,
    syncStatus,
    pendingCount,
    errorCount,
    lastSavedAt,
    clearError,
    bindGlobalMouseTracker,
    convertToMindMapData,
    reloadMindMap,
    applyExpandCollapse,
    handleDragEnd,
    normalizeRootChildDirections,
    bindIncrementalSyncHandlers,
    flushPendingUpdates,
    waitForPendingOps,
    retryFailedOps,
    hasPendingWriteOps,
    // 暴露给外部调试
    _debugIdMap: uidToBackendId
  }
}
