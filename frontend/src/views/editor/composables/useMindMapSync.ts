import { ref, type ComputedRef } from 'vue'
import type MindMap from 'simple-mind-map'
import type { NodeDto, NodeUpdatePayload } from '@/api/nodes'
import { useNodesStore } from '@/stores/nodes'

type NodesStore = ReturnType<typeof useNodesStore>

/** 后端 NodeShape 数字 → simple-mind-map 形状字符串 */
const shapeMap: Record<number, string> = {
  0: 'rectangle',
  1: 'roundedRectangle',
  2: 'circle',
  3: 'ellipse',
  4: 'diamond',
  5: 'parallelogram'
  // 6=Underline: simple-mind-map 无对应形状，默认 rectangle
}

/** 后端 EdgeStyle 数字 → simple-mind-map lineDasharray */
const edgeStyleMap: Record<number, string> = {
  0: 'none',
  1: '6,4',
  2: '2,2',
  3: 'none' // Curve 通过布局控制，虚线同实线
}

interface TreeDataNode {
  id?: string
  data?: { text?: string; expand?: boolean }
  children?: TreeDataNode[]
}

interface FlatNode {
  id?: string
  parentId: string | null
  text: string
  isCollapsed: boolean
  sortOrder: number
}

/**
 * 思维导图数据同步：负责 simple-mind-map 数据树与后端节点树的相互转换与同步
 */
export function useMindMapSync(opts: {
  getMindMapInstance: () => MindMap | null
  nodesStore: NodesStore
  readonly: ComputedRef<boolean>
}) {
  const { getMindMapInstance, nodesStore, readonly } = opts

  /** 防止 setData 触发 data_change 循环 */
  const isSettingData = ref(false)

  /** 记录最新鼠标屏幕坐标，供 beforeDragEnd 判定方向用 */
  let lastMouseClientX = 0
  let lastMouseClientY = 0

  /** 标记刚发生过拖拽，需要在 render_end 后兜底同步 */
  let pendingDragSync = false

  /** 全局鼠标位置监听（供 handleDragEnd 判定方向用） */
  function bindGlobalMouseTracker() {
    document.addEventListener('mousemove', (e) => {
      lastMouseClientX = e.clientX
      lastMouseClientY = e.clientY
    })
  }

  /** 转换后端节点树为 simple-mind-map 格式 */
  function convertToMindMapData(nodes: NodeDto[]): unknown {
    if (nodes.length === 0) return null

    const nodeMap = new Map<string, unknown>()
    const roots: unknown[] = []

    // 1. 第一遍循环：创建节点，统一将 id 转为 String 类型存入 Map
    for (const n of nodes) {
      const data: Record<string, unknown> = {
        text: n.icon ? `${n.icon} ${n.title}` : n.title,
        expand: !n.isCollapsed
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
      if (n.direction === 0) data.dir = 'left'
      else if (n.direction === 1) data.dir = 'right'

      const nodeData = {
        id: n.id,
        data,
        children: [] as unknown[]
      }

      // 强制转换为 String 作为 Key
      nodeMap.set(String(n.id), nodeData)
    }

    // 2. 第二遍循环：构建父子关系
    for (const n of nodes) {
      const nodeData = nodeMap.get(String(n.id)) as { children: unknown[] }

      // 安全地处理 parentId，排除 null/undefined，并强制转为 String
      const parentIdStr = n.parentId != null ? String(n.parentId) : null

      if (parentIdStr && nodeMap.has(parentIdStr)) {
        const parentData = nodeMap.get(parentIdStr) as { children: unknown[] }
        parentData.children.push(nodeData)
      } else {
        roots.push(nodeData)
      }
    }

    if (roots.length === 0) return null

    // 根节点直接子节点默认全部朝右
    const root = roots[0] as { children: { data: Record<string, unknown> }[] }
    for (const child of root.children) {
      if (!child.data.dir) child.data.dir = 'right'
    }

    return roots[0]
  }

  /** 重新加载画布数据（不重新拉取后端） */
  function reloadMindMap() {
    const inst = getMindMapInstance()
    if (!inst) return
    isSettingData.value = true
    const mindMapData = convertToMindMapData(nodesStore.nodes)
    if (mindMapData) {
      inst.setData(mindMapData)
    }
    setTimeout(() => { isSettingData.value = false }, 100)
  }

  /** 从 simple-mind-map 数据树提取所有节点信息 */
  function flattenTree(node: TreeDataNode, parentId: string | null, sortOrder: number, result: FlatNode[]) {
    const text = node.data?.text || ''
    const isCollapsed = node.data?.expand === false
    result.push({ id: node.id, parentId, text, isCollapsed, sortOrder })
    if (node.children) {
      for (let i = 0; i < node.children.length; i++) {
        flattenTree(node.children[i], node.id || null, i, result)
      }
    }
  }

  /** 同步 simple-mind-map 的数据变更到后端 */
  async function syncToBackend() {
    const inst = getMindMapInstance()
    if (!inst) return
    const rawData = inst.getData() as TreeDataNode
    if (!rawData) return

    const treeNodes: FlatNode[] = []
    flattenTree(rawData, null, 0, treeNodes)

    // 获取后端现有节点列表
    const backendNodes = nodesStore.nodes
    const backendIds = new Set(backendNodes.map(n => n.id))
    const treeIds = new Set(treeNodes.filter(n => n.id).map(n => n.id!))

    try {
      // 1. 创建新节点（tree 中有但 backend 中没有的）
      for (const tn of treeNodes) {
        if (!tn.id || !backendIds.has(tn.id)) {
          const parentId = tn.parentId && backendIds.has(tn.parentId) ? tn.parentId : null
          const created = await nodesStore.create({
            parentId,
            title: tn.text || '新节点',
            sortOrder: tn.sortOrder,
            isCollapsed: tn.isCollapsed
          })
          backendIds.add(created.id)
        }
      }

      // 2. 更新已有节点（文字、折叠状态、父节点变更）
      for (const tn of treeNodes) {
        if (!tn.id || !backendIds.has(tn.id)) continue
        const backendNode = backendNodes.find(n => n.id === tn.id)
        if (!backendNode) continue

        const updates: NodeUpdatePayload = {}
        // 比较时考虑 icon 前缀：text = icon + ' ' + title
        const expectedText = (backendNode.icon ? backendNode.icon + ' ' : '') + backendNode.title
        if (expectedText !== tn.text) {
          // 从 text 中去掉 icon 前缀提取 title
          let newTitle = tn.text
          if (backendNode.icon && tn.text.startsWith(backendNode.icon + ' ')) {
            newTitle = tn.text.substring(backendNode.icon.length + 1)
          }
          if (backendNode.title !== newTitle) updates.title = newTitle
        }
        if (backendNode.isCollapsed !== tn.isCollapsed) updates.isCollapsed = tn.isCollapsed
        if (backendNode.sortOrder !== tn.sortOrder) updates.sortOrder = tn.sortOrder

        // 父节点变更 → 调用 move API
        if (tn.parentId !== backendNode.parentId) {
          const newParentId = tn.parentId && backendIds.has(tn.parentId) ? tn.parentId : null
          await nodesStore.move(tn.id, { parentId: newParentId, sortOrder: tn.sortOrder })
        } else if (Object.keys(updates).length > 0) {
          await nodesStore.update(tn.id, updates)
        }
      }

      // 3. 删除不在 tree 中的节点
      for (const bn of backendNodes) {
        if (!treeIds.has(bn.id) && bn.parentId !== null) {
          await nodesStore.remove(bn.id)
        }
      }
    } catch (e) {
      console.error('[syncToBackend] error:', e)
    }
  }

  /** 递归查找某个 uid 对应节点的父节点 uid */
  function findNodeParentUid(uid: string): unknown {
    const inst = getMindMapInstance()
    const root = inst?.renderer.root
    if (!root) return null
    let result: unknown = null
    const walk = (node: typeof root) => {
      if (result) return
      if (node.getData('uid') === uid) {
        result = node.parent?.getData('uid') ?? null
        return
      }
      node.children?.forEach(walk)
    }
    walk(root)
    return result
  }

  /**
   * simple-mind-map 拖拽即将结束回调（execCommand 之前调用）
   * 根据鼠标最终屏幕位置相对根节点中心的方位，直接修改被拖拽节点的 dir 属性
   * 这样后续的 layout 就会按正确方向渲染，而不是按旧方向画好后再检测
   */
  function handleDragEnd(info: {
    overlapNodeUid?: string
    prevNodeUid?: string
    nextNodeUid?: string
    beingDragNodeList: Array<{
      parent?: { getData: (k?: string) => unknown; isRoot?: boolean }
      setData: (data: Record<string, unknown>) => void
      getData: (k?: string) => unknown
    }>
  }) {
    const inst = getMindMapInstance()
    if (!inst || readonly.value) return
    pendingDragSync = true

    const root = inst.renderer.root
    if (!root) return

    const svgEl = inst.draw.node as SVGSVGElement
    if (!svgEl) return

    const svgRect = svgEl.getBoundingClientRect()
    const rootCenterClientX = svgRect.left + root.left + (root.width || 0) / 2
    const targetDir = lastMouseClientX < rootCenterClientX ? 'left' : 'right'

    // 找出根节点 uid，用来判断 overlapNodeUid / prevNodeUid / nextNodeUid 是否属于根节点层
    const rootUid = root.getData('uid')

    // 判断被拖拽节点是否即将成为根节点的直接子节点
    // 情况：overlap 是 root（拖到根节点上），或 prev/next 的 parent 是 root（拖到根节点子节点之间）
    const willBeRootChild =
      info.overlapNodeUid === rootUid ||
      info.prevNodeUid && findNodeParentUid(info.prevNodeUid) === rootUid ||
      info.nextNodeUid && findNodeParentUid(info.nextNodeUid) === rootUid

    if (willBeRootChild) {
      for (const node of info.beingDragNodeList) {
        node.setData({ dir: targetDir })
      }
    }
  }

  /**
   * 每次 layout 完成后扫描根节点直接子节点，统一修正方向：
   * - 节点自身 data.dir 没设置（undefined）→ 用实际位置判断，同时写回 data.dir 和后端
   * - data.dir 有值但与实际位置不符（拖拽后 freeDrag 到了另一边）→ 更新 data.dir 和后端
   * 目的：彻底消除 simple-mind-map 默认的 index % 2 奇偶交替方向分配
   */
  async function normalizeRootChildDirections() {
    const inst = getMindMapInstance()
    if (!inst || readonly.value) return
    const root = inst.renderer.root
    if (!root) return

    const rootCenterX = root.left + (root.width || 0) / 2
    const updates: Array<{
      node: { setData: (d: Record<string, unknown>) => void; getData: (k?: string) => unknown }
      id: string
      targetDir: 'left' | 'right'
      backendDir: 0 | 1
    }> = []

    const scan = (node: typeof root) => {
      if (node.isRoot) {
        node.children?.forEach(scan)
        return
      }
      if (!node.parent || !node.parent.isRoot) return
      const id = node.getData('id') as string | undefined
      if (!id) return
      const centerX = node.left + (node.width || 0) / 2
      const targetDir: 'left' | 'right' = centerX < rootCenterX ? 'left' : 'right'
      const backendDir: 0 | 1 = targetDir === 'left' ? 0 : 1
      const currentBackend = nodesStore.findNode(id)?.direction
      const currentDataDir = node.getData('dir')
      // data.dir 不对（undefined 或 方向错）或后端 direction 不对 → 修正
      if (currentDataDir !== targetDir || currentBackend !== backendDir) {
        updates.push({ node, id, targetDir, backendDir })
      }
    }
    scan(root)

    if (updates.length === 0) return

    // 1. 先修正前端节点 data.dir（可能需要再 render 一次）
    for (const u of updates) {
      u.node.setData({ dir: u.targetDir })
    }

    // 2. 写回后端
    for (const u of updates) {
      try {
        await nodesStore.update(u.id, { direction: u.backendDir })
      } catch {
        // 单个失败忽略
      }
    }
  }

  return {
    isSettingData,
    bindGlobalMouseTracker,
    convertToMindMapData,
    reloadMindMap,
    syncToBackend,
    handleDragEnd,
    normalizeRootChildDirections
  }
}
