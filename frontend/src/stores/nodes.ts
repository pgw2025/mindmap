import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import * as nodesApi from '@/api/nodes'
import type {
  NodeDto,
  NodeTreeNodeDto,
  NodeCreatePayload,
  NodeUpdatePayload,
  NodeMovePayload,
  NodeBatchItem
} from '@/api/nodes'
import * as offlineDb from '@/offline/db'

/** 命令历史记录，用于 undo/redo */
interface HistoryCommand {
  type: 'create' | 'update' | 'delete' | 'move' | 'batch' | 'compound'
  nodeId?: string
  snapshot?: NodeDto | NodeDto[]
  prevData?: unknown
  nextData?: unknown
  /** type === 'compound' 时承载被合并的子命令（如一次拖拽产生的 update/move/batch） */
  subCommands?: HistoryCommand[]
}

export const useNodesStore = defineStore('nodes', () => {
  const mindMapId = ref('')
  const nodes = ref<NodeDto[]>([])
  // tree 由扁平 nodes 前端组装（逻辑与后端 BuildTree 一致），
  // 不再单独请求 GET /nodes/tree，写操作后自动跟随 nodes 变化。
  const tree = computed<NodeTreeNodeDto[]>(() => offlineDb.buildNodeTree(nodes.value))
  const loading = ref(false)
  const selectedNodeId = ref<string | null>(null)

  /** 当前节点数据是否来自离线快照（供编辑器顶栏显示「离线数据」） */
  const isOfflineSnapshot = ref(false)

  /** reloadTree 版本号：响应乱序时只采用最新一次请求结果 */
  let reloadVersion = 0

  /** 历史记录栈 */
  const undoStack = ref<HistoryCommand[]>([])
  const redoStack = ref<HistoryCommand[]>([])

  const rootNode = computed(() =>
    nodes.value.find((n) => n.parentId == null) || null
  )

  const canUndo = computed(() => undoStack.value.length > 0)
  const canRedo = computed(() => redoStack.value.length > 0)

  function findNode(id: string): NodeDto | undefined {
    return nodes.value.find((n) => n.id === id)
  }

  function getChildren(parentId: string | null): NodeDto[] {
    return nodes.value
      .filter((n) => n.parentId === parentId)
      .sort((a, b) => a.sortOrder - b.sortOrder)
  }

  /** 历史分组缓冲：beginHistoryGroup 期间所有 pushHistory 写入此处，
   *  endHistoryGroup 时合并为单条 compound 历史，使一次复合操作（如一次拖拽
   *  产生的 update/move + 兄弟重排 batch）只占一条 undo 记录，
   *  撤销时一次回退全部子命令，避免「第一次撤销观感无效」。
   *  buffer === null 表示当前不在分组期，pushHistory 直接入 undoStack。 */
  let historyGroupBuffer: HistoryCommand[] | null = null

  function beginHistoryGroup() {
    historyGroupBuffer = []
  }

  function endHistoryGroup() {
    const buf = historyGroupBuffer
    historyGroupBuffer = null
    if (!buf || buf.length === 0) return
    if (buf.length === 1) {
      // 单条无需包装，保持原有粒度
      pushHistory(buf[0])
      return
    }
    pushHistory({ type: 'compound', subCommands: buf })
  }

  function pushHistory(cmd: HistoryCommand) {
    // 分组期内：累积到 buffer，不直接入 undoStack
    if (historyGroupBuffer !== null) {
      historyGroupBuffer.push(cmd)
      return
    }
    undoStack.value.push(cmd)
    redoStack.value = []
    if (undoStack.value.length > 100) {
      undoStack.value.shift()
    }
  }

  async function load(mindMapIdParam: string) {
    mindMapId.value = mindMapIdParam
    loading.value = true
    try {
      const flat = await nodesApi.fetchNodes(mindMapIdParam)
      nodes.value = flat
      isOfflineSnapshot.value = false
      // 增量回写：在线打开单图也保持快照新鲜（tree 由 nodes 组装，不单独落库）
      offlineDb.updateMapNodes(mindMapIdParam, flat).catch(() => { /* ignore */ })
    } catch (err) {
      // 离线兜底：读单图快照，tree 由扁平 nodes 前端组装
      const snapshot = await offlineDb.getMap(mindMapIdParam)
      if (snapshot?.nodes) {
        nodes.value = snapshot.nodes
        isOfflineSnapshot.value = true
      } else {
        throw err
      }
    } finally {
      loading.value = false
    }
  }

  async function reloadTree() {
    if (!mindMapId.value) return
    const version = ++reloadVersion
    const flat = await nodesApi.fetchNodes(mindMapId.value)
    // 响应乱序时只采用最新一次 reloadTree 的结果，丢弃过期响应，避免旧数据覆盖新数据
    if (version !== reloadVersion) return
    nodes.value = flat
  }

  async function create(payload: NodeCreatePayload): Promise<NodeDto> {
    const node = await nodesApi.createNode(mindMapId.value, payload)
    nodes.value.push(node)
    pushHistory({ type: 'create', nodeId: node.id, nextData: node })
    return node
  }

  async function update(
    nodeId: string,
    payload: NodeUpdatePayload
  ): Promise<NodeDto> {
    const node = findNode(nodeId)
    const prevData = node ? { ...node } : null
    const updated = await nodesApi.updateNode(mindMapId.value, nodeId, payload)
    const idx = nodes.value.findIndex((n) => n.id === nodeId)
    if (idx >= 0) {
      nodes.value[idx] = updated
    }
    pushHistory({ type: 'update', nodeId, prevData, nextData: updated })
    return updated
  }

  async function move(
    nodeId: string,
    payload: NodeMovePayload
  ): Promise<NodeDto> {
    const node = findNode(nodeId)
    const prevData = node ? { ...node } : null
    const moved = await nodesApi.moveNode(mindMapId.value, nodeId, payload)
    const idx = nodes.value.findIndex((n) => n.id === nodeId)
    if (idx >= 0) {
      nodes.value[idx] = moved
    }
    pushHistory({ type: 'move', nodeId, prevData, nextData: moved })
    return moved
  }

  async function batchUpdate(items: NodeBatchItem[]): Promise<void> {
    const prevData = items
      .map((item) => nodes.value.find((n) => n.id === item.id))
      .filter(Boolean) as NodeDto[]
    await nodesApi.batchUpdateNodes(mindMapId.value, { nodes: items })
    // 就地回填：batchUpdate 返回 void，本地 nodes 靠 items 回填保持新鲜，
    // 避免下次 reloadMindMap（读 nodesStore.nodes）用过期值覆盖画布。
    for (const item of items) {
      const node = nodes.value.find((n) => n.id === item.id)
      if (!node) continue
      if (item.sortOrder !== undefined) node.sortOrder = item.sortOrder
      if (item.parentId !== undefined) node.parentId = item.parentId
      if (item.direction !== undefined) node.direction = item.direction
      if (item.isCollapsed !== undefined) node.isCollapsed = item.isCollapsed
      if (item.x !== undefined) node.x = item.x
      if (item.y !== undefined) node.y = item.y
      if (item.color !== undefined) node.color = item.color
      if (item.fontSize !== undefined) node.fontSize = item.fontSize
      if (item.fontFamily !== undefined) node.fontFamily = item.fontFamily
      if (item.shape !== undefined) node.shape = item.shape
      if (item.icon !== undefined) node.icon = item.icon
      if (item.borderColor !== undefined) node.borderColor = item.borderColor
      if (item.backgroundColor !== undefined) node.backgroundColor = item.backgroundColor
      if (item.edgeColor !== undefined) node.edgeColor = item.edgeColor
      if (item.edgeStyle !== undefined) node.edgeStyle = item.edgeStyle
    }
    pushHistory({ type: 'batch', snapshot: prevData, nextData: items })
  }

  async function remove(nodeId: string): Promise<void> {
    const descendants = collectDescendants(nodeId)
    const allToDelete = [nodeId, ...descendants]
    const snapshot = nodes.value.filter((n) => allToDelete.includes(n.id))
    await nodesApi.deleteNode(mindMapId.value, nodeId)
    nodes.value = nodes.value.filter((n) => !allToDelete.includes(n.id))
    if (selectedNodeId.value && allToDelete.includes(selectedNodeId.value)) {
      selectedNodeId.value = null
    }
    pushHistory({ type: 'delete', nodeId, snapshot })
  }

  function collectDescendants(parentId: string): string[] {
    const result: string[] = []
    const queue = [parentId]
    while (queue.length > 0) {
      const current = queue.shift()!
      const children = nodes.value.filter((n) => n.parentId === current)
      for (const child of children) {
        result.push(child.id)
        queue.push(child.id)
      }
    }
    return result
  }

  async function undo(): Promise<void> {
    if (!canUndo.value) return
    const cmd = undoStack.value.pop()!
    redoStack.value.push(cmd)
    await applyUndo(cmd)
  }

  async function redo(): Promise<void> {
    if (!canRedo.value) return
    const cmd = redoStack.value.pop()!
    undoStack.value.push(cmd)
    await applyRedo(cmd)
  }

  async function applyUndoCore(cmd: HistoryCommand) {
    switch (cmd.type) {
      case 'create':
        if (cmd.nodeId) {
          await nodesApi.deleteNode(mindMapId.value, cmd.nodeId)
          nodes.value = nodes.value.filter((n) => n.id !== cmd.nodeId)
        }
        break
      case 'update':
      case 'move':
        if (cmd.nodeId && cmd.prevData) {
          const prev = cmd.prevData as NodeUpdatePayload
          await nodesApi.updateNode(mindMapId.value, cmd.nodeId, prev)
        }
        break
      case 'delete':
        if (cmd.snapshot) {
          const snapshot = cmd.snapshot as NodeDto[]
          // 按层级顺序从父到子恢复
          const sorted = snapshot.sort((a, b) => {
            const aDepth = getDepth(a.id)
            const bDepth = getDepth(b.id)
            return aDepth - bDepth
          })
          for (const n of sorted) {
            await nodesApi.createNode(mindMapId.value, {
              parentId: n.parentId,
              title: n.title,
              content: n.content ?? undefined,
              note: n.note ?? undefined,
              sortOrder: n.sortOrder,
              color: n.color ?? undefined,
              fontSize: n.fontSize ?? undefined,
              shape: n.shape ?? undefined
            })
          }
        }
        break
      case 'batch':
        if (cmd.snapshot) {
          const prev = cmd.snapshot as NodeDto[]
          const items = prev.map((n) => ({
            id: n.id,
            sortOrder: n.sortOrder,
            parentId: n.parentId,
            x: n.x ?? undefined,
            y: n.y ?? undefined,
            isCollapsed: n.isCollapsed,
            color: n.color ?? undefined,
            fontSize: n.fontSize ?? undefined,
            fontFamily: n.fontFamily ?? undefined,
            shape: n.shape ?? undefined,
            icon: n.icon ?? undefined,
            borderColor: n.borderColor ?? undefined,
            backgroundColor: n.backgroundColor ?? undefined,
            edgeColor: n.edgeColor ?? undefined,
            edgeStyle: n.edgeStyle ?? undefined
          }))
          await nodesApi.batchUpdateNodes(mindMapId.value, { nodes: items })
        }
        break
      case 'compound':
        // 倒序回滚子命令（与入栈顺序相反），reloadTree 由 applyUndo 统一执行一次
        if (cmd.subCommands) {
          for (let i = cmd.subCommands.length - 1; i >= 0; i--) {
            await applyUndoCore(cmd.subCommands[i])
          }
        }
        break
    }
  }

  async function applyUndo(cmd: HistoryCommand) {
    await applyUndoCore(cmd)
    await reloadTree()
  }

  async function applyRedoCore(cmd: HistoryCommand) {
    switch (cmd.type) {
      case 'create':
        if (cmd.nextData) {
          const next = cmd.nextData as NodeCreatePayload
          await nodesApi.createNode(mindMapId.value, next)
        }
        break
      case 'update':
      case 'move':
        if (cmd.nodeId && cmd.nextData) {
          const next = cmd.nextData as NodeUpdatePayload
          await nodesApi.updateNode(mindMapId.value, cmd.nodeId, next)
        }
        break
      case 'delete':
        if (cmd.nodeId) {
          await nodesApi.deleteNode(mindMapId.value, cmd.nodeId)
        }
        break
      case 'batch':
        if (cmd.nextData) {
          const items = cmd.nextData as NodeBatchItem[]
          await nodesApi.batchUpdateNodes(mindMapId.value, { nodes: items })
        }
        break
      case 'compound':
        // 正序重做子命令，reloadTree 由 applyRedo 统一执行一次
        if (cmd.subCommands) {
          for (const sub of cmd.subCommands) {
            await applyRedoCore(sub)
          }
        }
        break
    }
  }

  async function applyRedo(cmd: HistoryCommand) {
    await applyRedoCore(cmd)
    await reloadTree()
  }

  function getDepth(nodeId: string): number {
    let depth = 0
    let current = findNode(nodeId)
    while (current && current.parentId) {
      depth++
      current = findNode(current.parentId)
    }
    return depth
  }

  function selectNode(id: string | null) {
    selectedNodeId.value = id
  }

  function reset() {
    mindMapId.value = ''
    nodes.value = []
    selectedNodeId.value = null
    isOfflineSnapshot.value = false
    undoStack.value = []
    redoStack.value = []
    historyGroupBuffer = null
  }

  function clearAll() {
    nodes.value = []
    selectedNodeId.value = null
    undoStack.value = []
    redoStack.value = []
    historyGroupBuffer = null
  }

  return {
    mindMapId,
    nodes,
    tree,
    loading,
    selectedNodeId,
    isOfflineSnapshot,
    rootNode,
    canUndo,
    canRedo,
    findNode,
    getChildren,
    load,
    reloadTree,
    create,
    update,
    move,
    batchUpdate,
    remove,
    undo,
    redo,
    selectNode,
    reset,
    clearAll,
    beginHistoryGroup,
    endHistoryGroup
  }
})
