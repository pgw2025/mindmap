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

  function enqueueStructuralOp(fn: () => Promise<void>): Promise<void> {
    markSyncing()
    opQueue = opQueue
      .catch(() => { })
      .then(async () => {
        await fn()
      })
      .then(() => {
        markSaved()
      })
      .catch(() => {
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
   *    4. 父节点变化 → nodesStore.move(parentId, sortOrder)
   *    5. 同级排序变化 → nodesStore.batchUpdate([{id, sortOrder}])
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
    if (oldExpand !== newExpand) {
      scheduleCollapseUpdate(backendId, !newExpand)
    }

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
            try {
              await nodesStore.update(backendId, { direction: backendDir })
            } catch (e) {
              console.error('[sync] direction update failed:', e)
            }
          })
        }
      }
    }

    // ---------- 4. 结构性变化（父节点、排序）：并入串行队列 ----------
    enqueueStructuralOp(async () => {
      await handleStructuralChanges(diff)
    })
  }

  /** 文本更新调度（debounce 400ms，key = backendId）
   *  key 统一使用 backendId，保证 data_change_detail 和 node_text_edit_change
   *  对同一节点的连续文本触发最终只产生一次 API 请求。 */
  function scheduleTextUpdate(backendId: string, rawText: string) {
    const existing = textDebounceTimers.get(backendId)
    if (existing) clearTimeout(existing.timer)
    // flush 函数闭包捕获最新 rawText，确保 flush 时用的是最后一次修改的值
    const flush = async () => {
      textDebounceTimers.delete(backendId)
      const title = extractTitleFromText(rawText, backendId)
      markSyncing()
      try {
        await nodesStore.update(backendId, { title })
        markSaved()
      } catch (e) {
        console.error('[sync] text update failed:', e)
        markError()
      }
    }
    textDebounceTimers.set(backendId, {
      timer: setTimeout(flush, 400),
      flush
    })
  }

  /** 折叠更新调度（debounce 250ms，key = backendId） */
  function scheduleCollapseUpdate(backendId: string, isCollapsed: boolean) {
    const existing = collapseDebounceTimers.get(backendId)
    if (existing) clearTimeout(existing.timer)
    const flush = async () => {
      collapseDebounceTimers.delete(backendId)
      markSyncing()
      try {
        await nodesStore.update(backendId, { isCollapsed })
        markSaved()
      } catch (e) {
        console.error('[sync] collapse update failed:', e)
        markError()
      }
    }
    collapseDebounceTimers.set(backendId, {
      timer: setTimeout(flush, 250),
      flush
    })
  }

  /** 备注更新调度（debounce 600ms，key = backendId）
   *  备注是富文本 HTML，内容较长且编辑频繁，用较长 debounce 减少请求次数。
   *  note 为空字符串时也要同步（清空操作），后端 Note is not null 判断会接受空串。 */
  function scheduleNoteUpdate(backendId: string, note: string) {
    const existing = noteDebounceTimers.get(backendId)
    if (existing) clearTimeout(existing.timer)
    const flush = async () => {
      noteDebounceTimers.delete(backendId)
      markSyncing()
      try {
        await nodesStore.update(backendId, { note })
        markSaved()
      } catch (e) {
        console.error('[sync] note update failed:', e)
        markError()
      }
    }
    noteDebounceTimers.set(backendId, {
      timer: setTimeout(flush, 600),
      flush
    })
  }

  /** 关联线数据更新调度（debounce 500ms，key = backendId）
   *  将 associativeLine* 系列字段序列化为 JSON 存入 Node.ExtraData。
   *  连线操作（创建/删除/改样式/改文字）都会触发，用 debounce 合并连续操作。 */
  function scheduleExtraDataUpdate(backendId: string, extraData: string) {
    const existing = extraDataDebounceTimers.get(backendId)
    if (existing) clearTimeout(existing.timer)
    const flush = async () => {
      extraDataDebounceTimers.delete(backendId)
      markSyncing()
      try {
        await nodesStore.update(backendId, { extraData })
        markSaved()
      } catch (e) {
        console.error('[sync] extraData update failed:', e)
        markError()
      }
    }
    extraDataDebounceTimers.set(backendId, {
      timer: setTimeout(flush, 500),
      flush
    })
  }

  /**
   * 结构性变化：父节点/同级排序
   * 需要对比「完整的新树」和「完整的旧树」才能得到父节点和排序信息，
   * 所以通过当前渲染树快照重建关系图进行比较。
   */
  async function handleStructuralChanges(diff: DiffItem) {
    const inst = getMindMapInstance()
    const renderRoot = inst?.renderer?.root
    if (!renderRoot) return

    const uid = diff.data.data.uid
    // 理论上 handleUpdate 已经 await 过，但保险起见再次等待（父节点可能刚创建）
    const backendId = await getBackendIdOrWait(uid)
    if (!backendId) return

    // 取当前整棵渲染树的 data 快照，构造关系图
    const currentSnapshot = inst.getData() as any
    if (!currentSnapshot) return
    const { parentOf, sortOrderOf } = buildRelationalMapsFromRaw(currentSnapshot)

    const newParentUid = parentOf.get(uid) ?? null
    const newSortOrder = sortOrderOf.get(uid) ?? 0
    const newParentBackendId = newParentUid ? await getBackendIdOrWait(newParentUid) ?? null : null

    const backendNode = nodesStore.findNode(backendId)
    if (!backendNode) return

    const parentChanged = newParentBackendId !== (backendNode.parentId ?? null)
    const orderChanged = newSortOrder !== backendNode.sortOrder

    const isNewParentRoot = newParentBackendId === nodesStore.rootNode?.id
    let newDirection: 0 | 1 | undefined = undefined
    if (isNewParentRoot) {
      const { x: mouseCanvasX } = inst.toPos(lastMouseClientX, lastMouseClientY)
      const { scaleX = 1, translateX = 0 } = inst.draw.transform()
      const rootCanvasCenterX = (renderRoot.left + (renderRoot.width || 0) / 2) * scaleX + translateX
      const targetDir = mouseCanvasX < rootCanvasCenterX ? 'left' : 'right'
      newDirection = targetDir === 'left' ? 0 : 1
    }

    if (parentChanged) {
      // 移动节点并同步更新 direction
      await nodesStore.move(backendId, {
        parentId: newParentBackendId,
        sortOrder: newSortOrder,
        direction: newDirection
      })
    } else if (orderChanged) {
      // 仅排序变化：检查兄弟节点是否也都变了 → 如果是，批量 reorder
      const siblingUids = collectSiblingUidsFromSnapshot(parentOf, sortOrderOf, newParentUid, uid)
      if (siblingUids.length >= 2) {
        const items: NodeBatchItem[] = []
        for (const sibUid of siblingUids) {
          const sibBackendId = await getBackendIdOrWait(sibUid)
          if (!sibBackendId) continue
          const sibBackend = nodesStore.findNode(sibBackendId)
          const so = sortOrderOf.get(sibUid) ?? 0
          if (!sibBackend || sibBackend.sortOrder !== so) {
            items.push({ id: sibBackendId, sortOrder: so })
          }
        }
        if (items.length > 0) {
          await nodesStore.batchUpdate(items)
        }
      } else {
        await nodesStore.update(backendId, { sortOrder: newSortOrder })
      }
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

  /** 收集兄弟节点 uid（按排序） */
  function collectSiblingUidsFromSnapshot(
    parentOf: Map<string, string | null>,
    sortOrderOf: Map<string, number>,
    parentUid: string | null,
    selfUid: string
  ): string[] {
    const siblings: string[] = []
    parentOf.forEach((p, uid) => {
      if (p === parentUid) siblings.push(uid)
    })
    if (!siblings.includes(selfUid)) siblings.push(selfUid)
    siblings.sort((a, b) => (sortOrderOf.get(a) ?? 0) - (sortOrderOf.get(b) ?? 0))
    return siblings
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

    // update 中纯文本/折叠变化是 debounced 的，不需要排队；结构性变化会自己 enqueue
    // 注意：handleUpdate 是 async（内部 await getBackendIdOrWait），不 await 在这里
    //       因为它的结果不影响后续批次；所有结构性副作用最终都流进 opQueue
    for (const u of updates) {
      handleUpdate(u).catch((e) => console.error('[sync] handleUpdate failed:', e))
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

    // 优先使用画布绝对坐标系计算落点方向
    const { x: mouseCanvasX } = inst.toPos(lastMouseClientX, lastMouseClientY)
    const { scaleX = 1, translateX = 0 } = inst.draw.transform()
    const rootCanvasCenterX = (root.left + (root.width || 0) / 2) * scaleX + translateX
    const targetDir: 'left' | 'right' = mouseCanvasX < rootCanvasCenterX ? 'left' : 'right'

    const rootUid = root.getData?.('uid')

    const willBeRootChild =
      info.overlapNodeUid === rootUid ||
      (info.prevNodeUid && findNodeParentUid(info.prevNodeUid) === rootUid) ||
      (info.nextNodeUid && findNodeParentUid(info.nextNodeUid) === rootUid)

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
    handleDragEnd,
    normalizeRootChildDirections,
    bindIncrementalSyncHandlers,
    flushPendingUpdates,
    waitForPendingOps,
    // 暴露给外部调试
    _debugIdMap: uidToBackendId
  }
}
