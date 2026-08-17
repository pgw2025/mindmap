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

/** 命令历史记录，用于 undo/redo */
interface HistoryCommand {
  type: 'create' | 'update' | 'delete' | 'move' | 'batch'
  nodeId?: string
  snapshot?: NodeDto | NodeDto[]
  prevData?: unknown
  nextData?: unknown
}

export const useNodesStore = defineStore('nodes', () => {
  const mindMapId = ref('')
  const nodes = ref<NodeDto[]>([])
  const tree = ref<NodeTreeNodeDto[]>([])
  const loading = ref(false)
  const selectedNodeId = ref<string | null>(null)

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

  function pushHistory(cmd: HistoryCommand) {
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
      const [flat, treeData] = await Promise.all([
        nodesApi.fetchNodes(mindMapIdParam),
        nodesApi.fetchNodeTree(mindMapIdParam)
      ])
      nodes.value = flat
      tree.value = treeData
    } finally {
      loading.value = false
    }
  }

  async function reloadTree() {
    if (!mindMapId.value) return
    const [flat, treeData] = await Promise.all([
      nodesApi.fetchNodes(mindMapId.value),
      nodesApi.fetchNodeTree(mindMapId.value)
    ])
    nodes.value = flat
    tree.value = treeData
  }

  async function create(payload: NodeCreatePayload): Promise<NodeDto> {
    const node = await nodesApi.createNode(mindMapId.value, payload)
    nodes.value.push(node)
    await reloadTree()
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
    await reloadTree()
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
    await reloadTree()
    pushHistory({ type: 'move', nodeId, prevData, nextData: moved })
    return moved
  }

  async function batchUpdate(items: NodeBatchItem[]): Promise<void> {
    const prevData = items
      .map((item) => nodes.value.find((n) => n.id === item.id))
      .filter(Boolean) as NodeDto[]
    await nodesApi.batchUpdateNodes(mindMapId.value, { nodes: items })
    await reloadTree()
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
    await reloadTree()
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

  async function applyUndo(cmd: HistoryCommand) {
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
            isCollapsed: n.isCollapsed
          }))
          await nodesApi.batchUpdateNodes(mindMapId.value, { nodes: items })
        }
        break
    }
    await reloadTree()
  }

  async function applyRedo(cmd: HistoryCommand) {
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
    }
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
    tree.value = []
    selectedNodeId.value = null
    undoStack.value = []
    redoStack.value = []
  }

  return {
    mindMapId,
    nodes,
    tree,
    loading,
    selectedNodeId,
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
    reset
  }
})
