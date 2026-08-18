<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed, nextTick, watch, reactive } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useMessage, NInput, NDrawer, NDrawerContent, NButton, NPopconfirm, NSpace, NModal, NCard, NEmpty, NSpin, NTag, NDropdown, NCheckbox, NDatePicker, NInputNumber } from 'naive-ui'
import MindMap from 'simple-mind-map'
import Search from 'simple-mind-map/src/plugins/Search.js'
import Export from 'simple-mind-map/src/plugins/Export.js'
import ExportPDF from 'simple-mind-map/src/plugins/ExportPDF.js'
import ExportXMind from 'simple-mind-map/src/plugins/ExportXMind.js'

// 注册插件
MindMap.usePlugin(Search)
MindMap.usePlugin(Export)
MindMap.usePlugin(ExportPDF)
MindMap.usePlugin(ExportXMind)
import type { NodeDto, NodeCreatePayload, NodeUpdatePayload } from '@/api/nodes'
import type { MindMapDetail } from '@/api/mindmaps'
import * as sharesApi from '@/api/shares'
import type { ShareDto, ShareCreatePayload } from '@/api/shares'
import { fetchMindMap } from '@/api/mindmaps'
import { useNodesStore } from '@/stores/nodes'
import { useMindMapsStore } from '@/stores/mindmaps'
import { useVersionsStore } from '@/stores/versions'
import { useAuthStore } from '@/stores/auth'
import NodeToolbar from './NodeToolbar.vue'
import RichTextEditor from './RichTextEditor.vue'

const route = useRoute()
const router = useRouter()
const message = useMessage()
const nodesStore = useNodesStore()
const mapsStore = useMindMapsStore()
const versionsStore = useVersionsStore()

// 根节点不能删除提示弹窗
const rootDeleteTipVisible = ref(false)
// 删除节点确认弹窗
const nodeDeleteConfirmVisible = ref(false)
const nodeDeleteTargetTitle = ref('')
const nodeDeleteSubmitting = ref(false)

const mindMapId = computed(() => route.params.id as string)
const mapDetail = ref<MindMapDetail | null>(null)
const loading = ref(true)
const mindMapRef = ref<HTMLDivElement | null>(null)
let mindMapInstance: MindMap | null = null

/** 防止 setData 触发 data_change 循环 */
let isSettingData = false

/** 选中的节点样式 */
const selectedNodeId = ref<string | null>(null)
const showToolbar = ref(false)

/** 复制/粘贴剪贴板 */
const clipboardNode = ref<NodeDto | null>(null)

/** 导图内搜索 */
const searchKeyword = ref('')
const searchMatchCount = ref(0)
const searchCurrentIndex = ref(0)

/** 转换后端节点树为 simple-mind-map 格式 */
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

function convertToMindMapData(nodes: NodeDto[]): unknown {
  if (nodes.length === 0) return null

  const nodeMap = new Map<string, unknown>()
  const roots: unknown[] = []

  for (const n of nodes) {
    const data: Record<string, unknown> = {
      // 图标 emoji 拼接到标题前显示（simple-mind-map 的 icon 数组系统需预定义 iconList，不适用 emoji）
      text: n.icon ? `${n.icon} ${n.title}` : n.title,
      expand: !n.isCollapsed
    }
    // 样式属性（simple-mind-map 直接从 data 对象读取）
    if (n.color) data.color = n.color
    if (n.fontSize) data.fontSize = n.fontSize
    if (n.fontFamily) data.fontFamily = n.fontFamily
    // 背景色：simple-mind-map 用 fillColor（节点形状填充色），非 backgroundColor（容器CSS背景色）
    if (n.backgroundColor) data.fillColor = n.backgroundColor
    if (n.borderColor) data.borderColor = n.borderColor
    if (n.shape != null && n.shape in shapeMap) data.shape = shapeMap[n.shape]
    if (n.edgeColor) data.lineColor = n.edgeColor
    if (n.edgeStyle != null && n.edgeStyle in edgeStyleMap) data.lineDasharray = edgeStyleMap[n.edgeStyle]
    if (n.note) data.note = n.note

    const nodeData = {
      id: n.id,
      data,
      children: [] as unknown[]
    }
    nodeMap.set(n.id, nodeData)
  }

  for (const n of nodes) {
    const nodeData = nodeMap.get(n.id) as { children: unknown[] }
    if (n.parentId && nodeMap.has(n.parentId)) {
      const parentData = nodeMap.get(n.parentId) as { children: unknown[] }
      parentData.children.push(nodeData)
    } else {
      roots.push(nodeData)
    }
  }

  if (roots.length === 0) return null
  return roots[0]
}

/** 触摸事件 → 鼠标事件桥接 + 双指捏合缩放（simple-mind-map 默认不绑定 touch 事件） */
let touchBridgeBound = false
function bindTouchBridge() {
  const el = mindMapRef.value
  if (!el || touchBridgeBound) return
  touchBridgeBound = true

  /** 派发合成鼠标事件到指定 target */
  function dispatchMouse(type: string, touch: Touch, target: Element) {
    const evt = new MouseEvent(type, {
      bubbles: true,
      cancelable: true,
      view: window,
      detail: 1,
      clientX: touch.clientX,
      clientY: touch.clientY,
      button: 0,
      buttons: type === 'mouseup' ? 0 : 1
    })
    ;(evt as any)._fromTouch = true
    target.dispatchEvent(evt)
  }

  /** 元素 + 指定坐标下的实际目标 */
  function elemAtPoint(el: Element, x: number, y: number): Element {
    const hit = document.elementFromPoint(x, y)
    if (hit && (el === hit || el.contains(hit))) return hit
    return el
  }

  /** 两指距离 */
  function distance(t1: Touch, t2: Touch) {
    const dx = t1.clientX - t2.clientX
    const dy = t1.clientY - t2.clientY
    return Math.hypot(dx, dy)
  }

  /** 两指中心点（相对于容器） */
  function pinchCenter(t1: Touch, t2: Touch) {
    const rect = el!.getBoundingClientRect()
    return {
      cx: (t1.clientX + t2.clientX) / 2 - rect.left,
      cy: (t1.clientY + t2.clientY) / 2 - rect.top
    }
  }

  // —— 单指拖拽状态 ——
  let downTarget: Element | null = null
  let lastTouch: Touch | null = null
  let moved = false

  // —— 双指捏合状态 ——
  let pinching = false
  let pinchStartDist = 0
  let pinchStartScale = 1
  let pinchCenterPoint = { cx: 0, cy: 0 }

  el.addEventListener('touchstart', (e: TouchEvent) => {
    if (e.touches.length >= 2) {
      // 进入捏合模式，重置单指状态
      pinching = true
      downTarget = null
      lastTouch = null
      moved = false
      const t1 = e.touches[0]
      const t2 = e.touches[1]
      pinchStartDist = distance(t1, t2)
      pinchStartScale = (mindMapInstance?.view as any)?.scale ?? 1
      pinchCenterPoint = pinchCenter(t1, t2)
      e.preventDefault()
      return
    }
    if (pinching) {
      // 之前是捏合，现在只剩一指：退出捏合模式
      pinching = false
    }
    const t = e.touches[0]
    lastTouch = t
    moved = false
    downTarget = elemAtPoint(el, t.clientX, t.clientY)
    dispatchMouse('mousedown', t, downTarget)
  }, { passive: false, capture: false })

  el.addEventListener('touchmove', (e: TouchEvent) => {
    if (pinching && e.touches.length >= 2) {
      // 双指捏合缩放
      const t1 = e.touches[0]
      const t2 = e.touches[1]
      const curDist = distance(t1, t2)
      const view: any = mindMapInstance?.view
      if (pinchStartDist > 0 && view) {
        const ratio = curDist / pinchStartDist
        const targetScale = Math.max(0.2, Math.min(4, pinchStartScale * ratio))
        view.setScale(targetScale, pinchCenterPoint.cx, pinchCenterPoint.cy)
      }
      e.preventDefault()
      return
    }
    if (e.touches.length > 1 || !lastTouch || !downTarget) return
    const t = e.touches[0]
    if (moved || Math.abs(t.clientX - lastTouch.clientX) > 4 || Math.abs(t.clientY - lastTouch.clientY) > 4) {
      moved = true
      e.preventDefault()
    }
    lastTouch = t
    dispatchMouse('mousemove', t, downTarget)
  }, { passive: false, capture: false })

  el.addEventListener('touchend', (e: TouchEvent) => {
    if (e.touches.length >= 2) {
      // 还在捏合，保持状态
      return
    }
    if (pinching) {
      // 捏合结束，可能还有一指残留
      pinching = false
      if (e.touches.length === 1) {
        // 只剩一指：作为新的单指起点
        const t = e.touches[0]
        lastTouch = t
        moved = false
        downTarget = elemAtPoint(el, t.clientX, t.clientY)
        dispatchMouse('mousedown', t, downTarget)
      }
      return
    }
    if (!lastTouch || !downTarget) {
      downTarget = null
      lastTouch = null
      return
    }
    dispatchMouse('mouseup', lastTouch, downTarget)
    downTarget = null
    lastTouch = null
    moved = false
  }, { passive: true, capture: false })

  el.addEventListener('touchcancel', () => {
    if (lastTouch && downTarget && !pinching) {
      dispatchMouse('mouseup', lastTouch, downTarget)
    }
    downTarget = null
    lastTouch = null
    moved = false
    pinching = false
  }, { passive: true })
}

/** 初始化 simple-mind-map */
function initMindMap() {
  if (!mindMapRef.value) {
    console.error('[MindMap] 容器元素不存在，mindMapRef =', mindMapRef.value)
    return
  }

  console.log('[MindMap] 初始化，容器 =', mindMapRef.value)

  mindMapInstance = new MindMap({
    el: mindMapRef.value,
    data: {
      data: { text: '中心主题' },
      children: []
    },
    theme: 'classic',
    layout: MindMap.TREE,
    draggable: true,
    contextMenu: true,
    toolBar: true,
    nodeLineDash: false,
    enableFreeDrag: true,
    scrollbarStyle: 'thin',
    minScale: 0.2,
    maxScale: 2
  })

  // 加载数据
  const mindMapData = convertToMindMapData(nodesStore.nodes)
  if (mindMapData) {
    mindMapInstance.setData(mindMapData)
  }

  // 移动端触摸事件桥接到鼠标事件（simple-mind-map 默认只绑定 mouse 事件）
  bindTouchBridge()

  // 监听选中节点：node_active 回调参数为 (node, activeNodeList)
  mindMapInstance.on('node_active', (...args: unknown[]) => {
    const activeNodeList = args[1] as Array<{ nodeData?: { id?: string } }> | undefined
    if (activeNodeList && activeNodeList.length > 0) {
      const id = activeNodeList[0]?.nodeData?.id
      if (id) {
        selectedNodeId.value = id
        showToolbar.value = true
      }
    } else {
      selectedNodeId.value = null
      showToolbar.value = false
    }
  })

  // 监听数据变化（键盘快捷键 Tab/Enter/Delete 等触发）
  let dataChangeTimer: ReturnType<typeof setTimeout> | null = null
  mindMapInstance.on('data_change', () => {
    if (isSettingData) return // setData 触发的，跳过同步
    // 防抖：避免频繁操作时多次调用
    if (dataChangeTimer) clearTimeout(dataChangeTimer)
    dataChangeTimer = setTimeout(() => {
      syncToBackend()
    }, 500)
  })

  // 监听搜索匹配结果
  mindMapInstance.on('search_match_node_list_change', (...args: unknown[]) => {
    const list = args[0]
    searchMatchCount.value = Array.isArray(list) ? list.length : 0
    searchCurrentIndex.value = searchMatchCount.value > 0 ? 1 : 0
  })
}

function getNextSortOrder(parentId: string | null): number {
  const children = nodesStore.getChildren(parentId)
  if (children.length === 0) return 0
  return Math.max(...children.map((c) => c.sortOrder)) + 1
}

function reloadMindMap() {
  if (!mindMapInstance) return
  isSettingData = true
  const mindMapData = convertToMindMapData(nodesStore.nodes)
  if (mindMapData) {
    mindMapInstance.setData(mindMapData)
  }
  setTimeout(() => { isSettingData = false }, 100)
}

/** 从 simple-mind-map 数据树提取所有节点信息 */
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
  if (!mindMapInstance) return
  const rawData = mindMapInstance.getData() as TreeDataNode
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

/** 工具栏操作 */
async function handleAddChild() {
  if (!selectedNodeId.value) return
  const node = nodesStore.findNode(selectedNodeId.value)
  const payload: NodeCreatePayload = {
    parentId: node?.id ?? null,
    title: '新子节点',
    sortOrder: getNextSortOrder(node?.id ?? null)
  }
  try {
    await nodesStore.create(payload)
    reloadMindMap()
  } catch (e) {
    message.error((e as Error).message)
  }
}

async function handleAddSibling() {
  if (!selectedNodeId.value) return
  const node = nodesStore.findNode(selectedNodeId.value)
  if (!node?.parentId) {
    message.warning('根节点没有同级')
    return
  }
  const payload: NodeCreatePayload = {
    parentId: node.parentId,
    title: '新节点',
    sortOrder: getNextSortOrder(node.parentId)
  }
  try {
    await nodesStore.create(payload)
    reloadMindMap()
  } catch (e) {
    message.error((e as Error).message)
  }
}

async function handleDelete() {
  if (!selectedNodeId.value) return
  const node = nodesStore.findNode(selectedNodeId.value)
  if (!node) return

  if (node.parentId == null) {
    rootDeleteTipVisible.value = true
    return
  }

  nodeDeleteTargetTitle.value = node.title
  nodeDeleteConfirmVisible.value = true
}

async function submitNodeDelete(): Promise<void> {
  if (!selectedNodeId.value) return
  nodeDeleteSubmitting.value = true
  try {
    await nodesStore.remove(selectedNodeId.value)
    selectedNodeId.value = null
    showToolbar.value = false
    reloadMindMap()
    message.success('已删除')
    nodeDeleteConfirmVisible.value = false
  } catch (e) {
    message.error((e as Error).message)
  } finally {
    nodeDeleteSubmitting.value = false
  }
}

async function handleUpdateStyle(payload: NodeUpdatePayload) {
  if (!selectedNodeId.value) return
  try {
    await nodesStore.update(selectedNodeId.value, payload)
    reloadMindMap()
  } catch (e) {
    message.error((e as Error).message)
  }
}

function handleZoomIn() {
  mindMapInstance?.view?.enlarge()
}

function handleZoomOut() {
  mindMapInstance?.view?.narrow()
}

function handleReset() {
  mindMapInstance?.view?.reset()
}

function handleBack() {
  router.push({ name: 'home' })
}

/** 导图内搜索 */
function handleSearch() {
  if (!mindMapInstance?.search) return
  const text = searchKeyword.value.trim()
  if (!text) {
    mindMapInstance.search.endSearch()
    searchMatchCount.value = 0
    searchCurrentIndex.value = 0
    return
  }
  mindMapInstance.search.search(text)
}

function handleSearchNext() {
  if (!mindMapInstance?.search) return
  mindMapInstance.search.searchNext()
  if (searchMatchCount.value > 0) {
    searchCurrentIndex.value = Math.min(searchCurrentIndex.value + 1, searchMatchCount.value)
  }
}

function handleSearchPrev() {
  if (!mindMapInstance?.search) return
  mindMapInstance.search.searchPrev()
  if (searchCurrentIndex.value > 1) {
    searchCurrentIndex.value--
  }
}

function handleSearchClear() {
  searchKeyword.value = ''
  mindMapInstance?.search?.endSearch()
  searchMatchCount.value = 0
  searchCurrentIndex.value = 0
}

/** 版本历史 */
const versionsDrawerVisible = ref(false)
const createVersionModalVisible = ref(false)
const createVersionRemark = ref('')
const creatingVersion = ref(false)
const rollingBackId = ref<string | null>(null)

async function openVersions() {
  try {
    await versionsStore.load(mindMapId.value, true)
  } catch (e) {
    message.error('加载版本历史失败')
  }
  versionsDrawerVisible.value = true
}

async function openCreateVersion() {
  createVersionRemark.value = ''
  createVersionModalVisible.value = true
}

async function submitCreateVersion() {
  creatingVersion.value = true
  try {
    await versionsStore.create(mindMapId.value, {
      remark: createVersionRemark.value.trim() || undefined
    })
    message.success('版本已保存')
    createVersionModalVisible.value = false
    // 如果抽屉已打开，刷新列表
    if (versionsDrawerVisible.value) {
      await versionsStore.load(mindMapId.value, true)
    }
  } catch (e) {
    message.error((e as Error).message || '保存失败')
  } finally {
    creatingVersion.value = false
  }
}

async function handleRollback(versionId: string, versionNumber: number) {
  rollingBackId.value = versionId
  try {
    await versionsStore.rollback(mindMapId.value, versionId)
    message.success(`已回滚到 V${versionNumber}`)
    // 重新加载节点数据和画布
    const mapId = mindMapId.value
    // 清空 store，强制重新加载
    nodesStore.clearAll?.()
    await nodesStore.load(mapId)
    // 重新加载导图详情
    mapDetail.value = await fetchMindMap(mapId)
    reloadMindMap()
    versionsDrawerVisible.value = false
  } catch (e) {
    message.error((e as Error).message || '回滚失败')
  } finally {
    rollingBackId.value = null
  }
}

async function handleDeleteVersion(versionId: string) {
  try {
    await versionsStore.remove(mindMapId.value, versionId)
    message.success('已删除')
  } catch (e) {
    message.error((e as Error).message || '删除失败')
  }
}

function formatVersionTime(iso: string): string {
  const d = new Date(iso)
  const now = new Date()
  const diffMs = now.getTime() - d.getTime()
  const diffMins = Math.floor(diffMs / 60000)
  if (diffMins < 1) return '刚刚'
  if (diffMins < 60) return `${diffMins} 分钟前`
  const diffHours = Math.floor(diffMins / 60)
  if (diffHours < 24) return `${diffHours} 小时前`
  const diffDays = Math.floor(diffHours / 24)
  if (diffDays < 30) return `${diffDays} 天前`
  return d.toLocaleDateString() + ' ' + d.toLocaleTimeString().slice(0, 5)
}

/** 导出功能 */
const exporting = ref(false)

const exportOptions = [
  { label: 'PNG 图片', key: 'png' },
  { label: 'SVG 矢量图', key: 'svg' },
  { label: 'PDF 文档', key: 'pdf' },
  { label: 'JSON 数据', key: 'json' },
  { label: 'Markdown', key: 'md' },
  { label: 'XMind', key: 'xmind' },
  { label: 'FreeMind (.mm)', key: 'freemind' }
]

async function handleExport(format: string) {
  if (exporting.value) return
  exporting.value = true
  const fileName = mapDetail.value?.title || '思维导图'
  try {
    if (format === 'freemind') {
      // 后端导出 FreeMind
      const url = `/api/mindmaps/${mindMapId.value}/export/freemind`
      const token = useAuthStore().accessToken
      const resp = await fetch(url, {
        headers: token ? { Authorization: `Bearer ${token}` } : {}
      })
      if (!resp.ok) throw new Error(`导出失败: ${resp.status}`)
      const blob = await resp.blob()
      downloadBlob(blob, `${fileName}.mm`)
      message.success('FreeMind 导出成功')
    } else {
      // simple-mind-map Export 插件导出（instanceName='doExport'）
      if (!mindMapInstance?.doExport) {
        throw new Error('导出插件未加载')
      }
      await mindMapInstance.doExport.export(format, true, fileName)
      message.success(`${format.toUpperCase()} 导出成功`)
    }
  } catch (e) {
    const err = e as Error
    message.error(err.message || '导出失败')
  } finally {
    exporting.value = false
  }
}

function downloadBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  URL.revokeObjectURL(url)
}

/** 撤销/重做后刷新画布 */
async function handleUndo() {
  await nodesStore.undo()
  reloadMindMap()
}

async function handleRedo() {
  await nodesStore.redo()
  reloadMindMap()
}

/** 复制选中节点到剪贴板 */
function handleCopy() {
  if (!selectedNodeId.value) return
  const node = nodesStore.findNode(selectedNodeId.value)
  if (node) {
    clipboardNode.value = { ...node }
    message.success('已复制节点')
  }
}

/** 粘贴节点（创建同级副本） */
async function handlePaste() {
  if (!clipboardNode.value || !selectedNodeId.value) return
  const sourceNode = clipboardNode.value
  const targetNode = nodesStore.findNode(selectedNodeId.value)
  if (!targetNode) return

  // 根节点不能粘贴为同级，改为粘贴为子节点
  const parentId = targetNode.parentId ?? targetNode.id
  const payload: NodeCreatePayload = {
    parentId,
    title: `${sourceNode.title} (副本)`,
    content: sourceNode.content ?? undefined,
    note: sourceNode.note ?? undefined,
    sortOrder: getNextSortOrder(parentId),
    color: sourceNode.color ?? undefined,
    fontSize: sourceNode.fontSize ?? undefined,
    shape: sourceNode.shape ?? undefined,
    icon: sourceNode.icon ?? undefined,
    backgroundColor: sourceNode.backgroundColor ?? undefined,
    borderColor: sourceNode.borderColor ?? undefined,
    edgeColor: sourceNode.edgeColor ?? undefined,
    edgeStyle: sourceNode.edgeStyle ?? undefined
  }
  try {
    await nodesStore.create(payload)
    reloadMindMap()
    message.success('已粘贴节点')
  } catch (e) {
    message.error((e as Error).message)
  }
}

async function handleTitleBlur() {
  if (mapDetail.value) {
    try {
      await mapsStore.update(mindMapId.value, { title: mapDetail.value.title })
      message.success('标题已更新')
    } catch (e) {
      message.error((e as Error).message)
    }
  }
}

/** 富文本节点内容编辑面板 */
const contentModalVisible = ref(false)
const editingNodeTitle = ref('')
const editingNodeContent = ref('')
const editingNodeNote = ref('')
const savingContent = ref(false)

function openContentEditor() {
  if (!selectedNodeId.value) return
  const node = nodesStore.findNode(selectedNodeId.value)
  if (!node) return
  editingNodeTitle.value = node.title
  editingNodeContent.value = node.content || ''
  editingNodeNote.value = node.note || ''
  contentModalVisible.value = true
}

async function saveNodeContent() {
  if (!selectedNodeId.value) return
  savingContent.value = true
  try {
    const payload: NodeUpdatePayload = {
      title: editingNodeTitle.value,
      content: editingNodeContent.value || undefined,
      note: editingNodeNote.value || undefined
    }
    await nodesStore.update(selectedNodeId.value, payload)
    reloadMindMap()
    contentModalVisible.value = false
    message.success('内容已保存')
  } catch (e) {
    message.error((e as Error).message || '保存失败')
  } finally {
    savingContent.value = false
  }
}

/** 分享功能 */
const shareDrawerVisible = ref(false)
const shareList = ref<ShareDto[]>([])
const shareLoading = ref(false)
const creatingShare = ref(false)
const createShareVisible = ref(false)
const newShare = reactive<ShareCreatePayload>({
  setPublic: false,
  password: '',
  expiresAt: null,
  maxAccessCount: null,
  allowCopy: true
})
const shareUrlCopied = ref<string | null>(null)

async function openShareDrawer() {
  shareDrawerVisible.value = true
  shareLoading.value = true
  try {
    shareList.value = await sharesApi.fetchShares(mindMapId.value)
  } catch (e) {
    message.error((e as Error).message || '获取分享列表失败')
  } finally {
    shareLoading.value = false
  }
}

function openCreateShare() {
  newShare.setPublic = mapDetail.value?.isPublic ?? false
  newShare.password = ''
  newShare.expiresAt = null
  newShare.maxAccessCount = null
  newShare.allowCopy = true
  createShareVisible.value = true
}

async function submitCreateShare() {
  creatingShare.value = true
  try {
    const payload: ShareCreatePayload = {
      setPublic: newShare.setPublic,
      allowCopy: newShare.allowCopy
    }
    if (newShare.password) payload.password = newShare.password
    if (newShare.expiresAt) payload.expiresAt = newShare.expiresAt
    if (newShare.maxAccessCount) payload.maxAccessCount = newShare.maxAccessCount
    const created = await sharesApi.createShare(mindMapId.value, payload)
    shareList.value.unshift(created)
    createShareVisible.value = false
    message.success('分享链接已创建')
    if (mapDetail.value) mapDetail.value.isPublic = mapDetail.value.isPublic || !!newShare.setPublic
    // 自动复制分享链接
    await copyShareUrl(created)
  } catch (e) {
    message.error((e as Error).message || '创建失败')
  } finally {
    creatingShare.value = false
  }
}

function buildShareUrl(share: ShareDto): string {
  return `${location.origin}/#/share/${share.shareToken}`
}

async function copyShareUrl(share: ShareDto) {
  const url = buildShareUrl(share)
  
  // 1. 优先尝试现代 Clipboard API（仅支持 HTTPS 或 localhost/127.0.0.1）
  if (navigator.clipboard && window.isSecureContext) {
    try {
      await navigator.clipboard.writeText(url)
      shareUrlCopied.value = share.id
      message.success('分享链接已复制到剪贴板')
      setTimeout(() => { shareUrlCopied.value = null }, 2000)
      return // 复制成功，提前退出
    } catch (err) {
      console.warn('Clipboard API 复制失败，正在尝试使用降级方案...', err)
    }
  }

  // 2. 降级方案：适用于 HTTP 协议、异步接口回调后手势失效等场景
  const textArea = document.createElement('textarea')
  textArea.value = url
  
  // 隐藏文本域，防止页面滚动或抖动
  textArea.style.position = 'fixed'
  textArea.style.top = '-9999px'
  textArea.style.left = '-9999px'
  
  document.body.appendChild(textArea)
  textArea.focus()
  textArea.select()
  
  try {
    const successful = document.execCommand('copy')
    textArea.remove()
    if (successful) {
      shareUrlCopied.value = share.id
      message.success('分享链接已复制到剪贴板')
      setTimeout(() => { shareUrlCopied.value = null }, 2000)
    } else {
      message.error('复制失败，请手动复制')
    }
  } catch (err) {
    console.error('降级复制方案失败:', err)
    textArea.remove()
    message.error('复制失败，请手动复制')
  }
}

async function handleDeleteShare(share: ShareDto) {
  try {
    await sharesApi.deleteShare(share.id)
    shareList.value = shareList.value.filter(s => s.id !== share.id)
    message.success('分享链接已删除')
  } catch (e) {
    message.error((e as Error).message || '删除失败')
  }
}

function formatShareTime(s: string | null | undefined): string {
  if (!s) return ''
  const d = new Date(s)
  return d.toLocaleDateString() + ' ' + d.toLocaleTimeString().slice(0, 5)
}

/** 初始化空导图 */
async function initEmptyMindMap() {
  const payload: NodeCreatePayload = {
    title: '中心主题',
    sortOrder: 0,
    color: '#fff',
    backgroundColor: '#18a058',
    shape: 1
  }
  await nodesStore.create(payload)
  await nextTick()
  reloadMindMap()
}

onMounted(async () => {
  try {
    // 加载导图详情
    mapDetail.value = await fetchMindMap(mindMapId.value)

    // 加载节点
    await nodesStore.load(mindMapId.value)

    // 自动创建根节点（如果为空）
    if (nodesStore.nodes.length === 0) {
      await initEmptyMindMap()
    }
  } catch (e) {
    message.error((e as Error).message || '加载失败')
    router.push({ name: 'home' })
    return
  } finally {
    loading.value = false
  }

  // 等待两帧确保 DOM 完全渲染后再初始化 simple-mind-map
  requestAnimationFrame(() => {
    requestAnimationFrame(() => {
      initMindMap()
    })
  })
})

onUnmounted(() => {
  mindMapInstance?.destroy()
  mindMapInstance = null
  nodesStore.reset()
})

/** 全局键盘事件 */
function handleKeydown(e: KeyboardEvent) {
  // 不要在输入框中触发快捷键
  const target = e.target as HTMLElement
  if (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.isContentEditable) return

  if (e.key === 'Escape') {
    selectedNodeId.value = null
    showToolbar.value = false
  }
  // Ctrl+Z 撤销
  if ((e.ctrlKey || e.metaKey) && e.key === 'z' && !e.shiftKey) {
    e.preventDefault()
    handleUndo()
  }
  // Ctrl+Y / Ctrl+Shift+Z 重做
  if ((e.ctrlKey || e.metaKey) && (e.key === 'y' || (e.key === 'z' && e.shiftKey))) {
    e.preventDefault()
    handleRedo()
  }
  // Ctrl+C 复制
  if ((e.ctrlKey || e.metaKey) && e.key === 'c' && selectedNodeId.value) {
    e.preventDefault()
    handleCopy()
  }
  // Ctrl+V 粘贴
  if ((e.ctrlKey || e.metaKey) && e.key === 'v' && clipboardNode.value) {
    e.preventDefault()
    handlePaste()
  }
}

onMounted(() => {
  window.addEventListener('keydown', handleKeydown)
})

onUnmounted(() => {
  window.removeEventListener('keydown', handleKeydown)
})

watch(() => route.params.id, () => {
  nodesStore.reset()
})
</script>

<template>
  <div class="editor-container">
    <!-- 顶部工具栏 -->
    <header class="editor-header">
      <button class="btn-back" @click="handleBack">
        <span class="icon">←</span>
        <span class="text">返回</span>
      </button>

      <div class="editor-title" v-if="mapDetail">
        <NInput
          v-model:value="mapDetail.title"
          class="title-input"
          @blur="handleTitleBlur"
        />
      </div>

      <div class="editor-actions">
        <button
          class="btn-tool"
          :disabled="!nodesStore.canUndo"
          @click="handleUndo"
          title="撤销 (Ctrl+Z)"
        >
          ↶
        </button>
        <button
          class="btn-tool"
          :disabled="!nodesStore.canRedo"
          @click="handleRedo"
          title="重做 (Ctrl+Y)"
        >
          ↷
        </button>
        <button
          class="btn-tool"
          :disabled="!selectedNodeId"
          @click="handleCopy"
          title="复制 (Ctrl+C)"
        >
          ⧉
        </button>
        <button
          class="btn-tool"
          :disabled="!clipboardNode"
          @click="handlePaste"
          title="粘贴 (Ctrl+V)"
        >
          📋
        </button>
        <button
          class="btn-tool"
          :disabled="!selectedNodeId"
          @click="openContentEditor"
          title="编辑节点内容"
        >
          📝
        </button>
        <span class="action-divider"></span>
        <button class="btn-action-save" @click="openCreateVersion" title="保存为版本快照">
          <span class="btn-icon">💾</span><span class="btn-label">保存版本</span>
        </button>
        <button class="btn-action-history" @click="openVersions" title="查看版本历史">
          <span class="btn-icon">🕘</span><span class="btn-label">历史</span>
        </button>
        <button class="btn-action-share" @click="openShareDrawer" title="分享此导图">
          <span class="btn-icon">🔗</span><span class="btn-label">分享</span>
        </button>
        <NDropdown
          trigger="click"
          :options="exportOptions"
          @select="handleExport"
        >
          <button class="btn-action-export" :class="{ 'is-loading': exporting }" title="导出导图" :disabled="exporting">
            <span class="btn-icon">{{ exporting ? '⏳' : '📤' }}</span><span class="btn-label">{{ exporting ? '导出中...' : '导出' }}</span>
          </button>
        </NDropdown>
        <span class="action-divider"></span>
        <button class="btn-tool" @click="handleZoomIn" title="放大">+</button>
        <button class="btn-tool" @click="handleZoomOut" title="缩小">−</button>
        <button class="btn-tool" @click="handleReset" title="重置视图">⟲</button>
      </div>
    </header>

    <!-- 画布区域 -->
    <main class="editor-main">
      <!-- 导图内搜索栏 -->
      <div class="search-bar">
        <input
          v-model="searchKeyword"
          class="search-input"
          type="text"
          placeholder="搜索节点..."
          @keyup.enter="handleSearch"
        />
        <button v-if="searchMatchCount > 0" class="search-nav-btn" @click="handleSearchPrev" title="上一个">▲</button>
        <button v-if="searchMatchCount > 0" class="search-nav-btn" @click="handleSearchNext" title="下一个">▼</button>
        <button v-if="searchKeyword" class="search-nav-btn" @click="handleSearchClear" title="清除">✕</button>
        <span v-if="searchMatchCount > 0" class="search-count">
          {{ searchCurrentIndex }}/{{ searchMatchCount }}
        </span>
      </div>
      <div ref="mindMapRef" class="mindmap-canvas"></div>
      <div v-if="loading" class="loading-wrap">
        <div class="spinner"></div>
        <p>加载中...</p>
      </div>
    </main>

    <!-- 浮动工具栏 -->
    <NodeToolbar
      v-if="showToolbar && selectedNodeId"
      :node="nodesStore.findNode(selectedNodeId)"
      @add-child="handleAddChild"
      @add-sibling="handleAddSibling"
      @delete="handleDelete"
      @update="handleUpdateStyle"
      @copy="handleCopy"
      @paste="handlePaste"
    />

    <!-- 创建版本弹窗 -->
    <NModal
      v-model:show="createVersionModalVisible"
      preset="card"
      title="保存版本快照"
      style="width: 420px"
      :mask-closable="false"
    >
      <div class="create-version-body">
        <p class="tip">当前节点数：<strong>{{ nodesStore.nodes.length }}</strong>，保存后可随时回滚到此状态。</p>
        <NInput
          v-model:value="createVersionRemark"
          type="textarea"
          placeholder="输入版本备注（可选），例如：完成需求分析阶段"
          :autosize="{ minRows: 3, maxRows: 5 }"
          maxlength="200"
          show-count
        />
      </div>
      <template #footer>
        <NSpace justify="end">
          <NButton @click="createVersionModalVisible = false">取消</NButton>
          <NButton
            type="primary"
            :loading="creatingVersion"
            @click="submitCreateVersion"
          >
            {{ creatingVersion ? '保存中...' : '确认保存' }}
          </NButton>
        </NSpace>
      </template>
    </NModal>

    <!-- 版本历史抽屉 -->
    <NDrawer
      v-model:show="versionsDrawerVisible"
      :width="420"
      placement="right"
      display-directive="if"
      title="版本历史"
    >
      <NDrawerContent>
        <template #header>
          <div class="drawer-header">
            <span>🕘 版本历史</span>
            <NButton size="small" type="primary" @click="openCreateVersion">+ 新建版本</NButton>
          </div>
        </template>
        <div class="versions-list-wrap">
          <NSpin v-if="versionsStore.loading" :show="true">
            <div style="height: 200px" />
          </NSpin>
          <template v-else>
            <NEmpty
              v-if="versionsStore.items.length === 0"
              description="暂无版本快照，点击右上角「新建版本」保存第一个快照"
            />
            <div v-else class="versions-list">
              <NCard
                v-for="v in versionsStore.items"
                :key="v.id"
                class="version-card"
                :bordered="true"
              >
                <div class="version-card-header">
                  <div class="version-title">
                    <NTag type="info" round>V{{ v.versionNumber }}</NTag>
                    <span class="version-note" v-if="v.remark">{{ v.remark }}</span>
                    <span class="version-note no-remark" v-else>（无备注）</span>
                  </div>
                  <div class="version-meta">
                    <span class="meta-item">📊 {{ v.nodeCount }} 节点</span>
                    <span class="meta-item">👤 {{ v.createdByName }}</span>
                    <span class="meta-item">⏰ {{ formatVersionTime(v.createdAt) }}</span>
                  </div>
                </div>
                <div class="version-actions">
                  <NPopconfirm
                    @positive-click="handleRollback(v.id, v.versionNumber)"
                    positive-text="确认回滚"
                    negative-text="取消"
                  >
                    <template #trigger>
                      <NButton
                        size="small"
                        type="warning"
                        :loading="rollingBackId === v.id"
                      >
                        {{ rollingBackId === v.id ? '回滚中...' : '回滚到此版本' }}
                      </NButton>
                    </template>
                    确认回滚到 V{{ v.versionNumber }}？当前所有未保存的修改将丢失，且操作不可撤销。
                  </NPopconfirm>
                  <NPopconfirm
                    @positive-click="handleDeleteVersion(v.id)"
                    positive-text="删除"
                    negative-text="取消"
                  >
                    <template #trigger>
                      <NButton size="small" type="error" quaternary>删除</NButton>
                    </template>
                    确认删除此版本快照？此操作不可撤销。
                  </NPopconfirm>
                </div>
              </NCard>
            </div>
          </template>
        </div>
      </NDrawerContent>
    </NDrawer>

    <!-- 分享抽屉 -->
    <NDrawer
      v-model:show="shareDrawerVisible"
      placement="right"
      :width="420"
      auto-focus
    >
      <NDrawerContent closable>
        <template #header>
          <div style="display:flex;align-items:center;justify-content:space-between;width:100%;gap:10px">
            <span>🔗 分享此思维导图</span>
            <NButton size="small" type="primary" @click="openCreateShare">
              + 新建分享
            </NButton>
          </div>
        </template>
        <div v-if="shareLoading" class="drawer-loading">
          <NSpin />
        </div>
        <template v-else-if="shareList.length === 0">
          <div class="drawer-empty">
            <NEmpty description="暂无分享链接，点击右上角「+ 新建分享」创建">
              <NButton type="primary" @click="openCreateShare">新建分享链接</NButton>
            </NEmpty>
          </div>
        </template>
        <div v-else class="share-list">
          <NCard
            v-for="share in shareList"
            :key="share.id"
            class="share-card"
            size="small"
          >
            <div class="share-card-head">
              <div class="share-token-title">
                <NTag type="success" size="small" round>
                  分享链接
                </NTag>
                <span class="share-token-code">{{ share.shareToken }}</span>
              </div>
              <div v-if="share.hasPassword" class="share-tags">
                <NTag size="small">🔐 密码</NTag>
              </div>
            </div>

            <div class="share-url-row">
              <code class="share-url">{{ buildShareUrl(share) }}</code>
              <NButton
                size="tiny"
                type="primary"
                quaternary
                @click="copyShareUrl(share)"
              >
                {{ shareUrlCopied === share.id ? '✓ 已复制' : '复制' }}
              </NButton>
            </div>

            <div class="share-meta">
              <div class="meta-item">
                <span>📊 访问次数</span>
                <NTag size="small" type="info">
                  {{ share.accessCount }}
                  <template v-if="share.maxAccessCount">/{{ share.maxAccessCount }}</template>
                </NTag>
              </div>
              <div class="meta-item">
                <span>📝 另存为</span>
                <NTag size="small" :type="share.allowCopy ? 'success' : 'warning'">
                  {{ share.allowCopy ? '允许' : '仅查看' }}
                </NTag>
              </div>
              <div v-if="share.expiresAt" class="meta-item">
                <span>⏰ 过期时间</span>
                <span class="meta-val">{{ formatShareTime(share.expiresAt) }}</span>
              </div>
              <div class="meta-item">
                <span>🕒 创建</span>
                <span class="meta-val">{{ formatShareTime(share.createdAt) }}</span>
              </div>
              <div v-if="share.lastAccessedAt" class="meta-item">
                <span>👀 最近访问</span>
                <span class="meta-val">{{ formatShareTime(share.lastAccessedAt) }}</span>
              </div>
            </div>

            <div class="share-card-actions">
              <NPopconfirm
                @positive-click="handleDeleteShare(share)"
              >
                <template #trigger>
                  <NButton size="small" type="error" quaternary>删除</NButton>
                </template>
                确认删除此分享链接？访问该链接的所有人将无法再查看导图。
              </NPopconfirm>
            </div>
          </NCard>
        </div>
      </NDrawerContent>
    </NDrawer>

    <!-- 新建分享弹窗 -->
    <NModal
      v-model:show="createShareVisible"
      preset="card"
      title="新建分享链接"
      style="width: 480px; max-width: 92vw"
    >
      <div class="create-share-body">
        <div class="field-group">
          <label class="field-label">
            <span class="label-title">同时设为公开</span>
            <NCheckbox v-model:checked="newShare.setPublic">
              所有人可在首页「公开导图」列表中浏览
            </NCheckbox>
          </label>
        </div>
        <div class="field-group">
          <label class="field-label">访问密码（可选）</label>
          <NInput
            v-model:value="newShare.password"
            placeholder="留空则无需密码即可访问"
            maxlength="32"
            show-password-on="mousedown"
            type="password"
          />
        </div>
        <div class="field-group">
          <label class="field-label">过期时间（可选）</label>
          <NDatePicker
            v-model:value="(newShare.expiresAt as unknown) as number | null"
            type="datetime"
            placeholder="留空表示永不过期"
            style="width: 100%"
            :disabled-date="(t: number) => t < Date.now() - 86400000"
            value-format="yyyy-MM-dd HH:mm:ss"
            :allow-input="false"
          />
        </div>
        <div class="field-group">
          <label class="field-label">最大访问次数（可选）</label>
          <NInputNumber
            v-model:value="newShare.maxAccessCount"
            placeholder="留空表示不限次"
            :min="1"
            style="width: 100%"
          />
        </div>
        <div class="field-group">
          <label class="field-label">
            <span class="label-title">访问者权限</span>
            <NCheckbox v-model:checked="newShare.allowCopy">
              允许访问者另存为副本
            </NCheckbox>
          </label>
        </div>
      </div>
      <template #footer>
        <NSpace justify="end">
          <NButton @click="createShareVisible = false">取消</NButton>
          <NButton
            type="primary"
            :loading="creatingShare"
            @click="submitCreateShare"
          >
            {{ creatingShare ? '创建中...' : '创建' }}
          </NButton>
        </NSpace>
      </template>
    </NModal>

    <!-- 富文本节点内容编辑弹窗 -->
    <NModal
      v-model:show="contentModalVisible"
      preset="card"
      title="📝 编辑节点内容"
      style="width: 600px; max-width: 92vw"
      :mask-closable="false"
    >
      <div class="content-edit-body">
        <div class="field-group">
          <label class="field-label">标题</label>
          <NInput
            v-model:value="editingNodeTitle"
            placeholder="节点标题"
            maxlength="200"
          />
        </div>
        <div class="field-group">
          <label class="field-label">正文内容</label>
          <RichTextEditor v-model="editingNodeContent" />
        </div>
        <div class="field-group">
          <label class="field-label">备注</label>
          <NInput
            v-model:value="editingNodeNote"
            type="textarea"
            placeholder="节点备注（可选）"
            :autosize="{ minRows: 2, maxRows: 4 }"
            maxlength="2000"
          />
        </div>
      </div>
      <template #footer>
        <NSpace justify="end">
          <NButton @click="contentModalVisible = false">取消</NButton>
          <NButton
            type="primary"
            :loading="savingContent"
            @click="saveNodeContent"
          >
            {{ savingContent ? '保存中...' : '保存' }}
          </NButton>
        </NSpace>
      </template>
    </NModal>

    <!-- 根节点不能删除提示 -->
    <NModal
      v-model:show="rootDeleteTipVisible"
      preset="dialog"
      type="warning"
      title="无法删除"
      positive-text="我知道了"
      :negative-button-props="{ style: { display: 'none' } }"
      display-directive="if"
      style="max-width: 420px"
    >
      根节点不能删除。你可以清空内容但不能删除中心主题。
    </NModal>

    <!-- 删除节点确认 -->
    <NModal
      v-model:show="nodeDeleteConfirmVisible"
      preset="dialog"
      type="warning"
      title="确认删除"
      positive-text="删除"
      negative-text="取消"
      :positive-button-props="{ type: 'error', loading: nodeDeleteSubmitting }"
      display-directive="if"
      style="max-width: 420px"
      @positive-click="submitNodeDelete"
    >
      删除「{{ nodeDeleteTargetTitle }}」及其所有子节点？
    </NModal>
  </div>
</template>

<style scoped lang="scss">
.editor-container {
  display: flex;
  flex-direction: column;
  height: 100vh;
  width: 100vw;
  background: var(--app-bg, #f5f7fa);
  overflow: hidden;
}

.editor-header {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 12px 16px;
  padding-top: calc(12px + var(--safe-top, 0px));
  background: var(--app-card-bg, #fff);
  border-bottom: 1px solid var(--app-border, #e0e0e6);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.04);
  z-index: 10;
}

.btn-back {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 8px 12px;
  background: transparent;
  border: 1px solid var(--app-border, #e0e0e6);
  border-radius: 6px;
  color: var(--app-text-primary, #333);
  cursor: pointer;
  font-size: 14px;
  transition: all 0.2s;

  &:hover {
    background: var(--app-hover-bg, #f0f0f0);
    border-color: var(--app-primary, #18a058);
  }

  .icon {
    font-size: 18px;
    font-weight: bold;
  }
}

.editor-title {
  flex: 1;
  max-width: 400px;
}

.title-input {
  width: 100%;
  padding: 8px 12px;
  font-size: 16px;
  font-weight: 500;
  border: 1px solid transparent;
  border-radius: 6px;
  background: transparent;
  color: var(--app-text-primary, #333);
  outline: none;
  text-align: center;
  transition: all 0.2s;

  &:hover,
  &:focus {
    border-color: var(--app-border, #e0e0e6);
    background: var(--app-bg, #f5f7fa);
  }
}

.editor-actions {
  display: flex;
  gap: 4px;
  align-items: center;
}

.action-divider {
  width: 1px;
  height: 24px;
  background: var(--app-border, #e0e0e6);
  margin: 0 4px;
}

.btn-tool {
  width: 36px;
  height: 36px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: transparent;
  border: 1px solid var(--app-border, #e0e0e6);
  border-radius: 6px;
  color: var(--app-text-primary, #333);
  cursor: pointer;
  font-size: 16px;
  font-weight: bold;
  transition: all 0.2s;

  &:hover:not(:disabled) {
    background: var(--app-hover-bg, #f0f0f0);
    border-color: var(--app-primary, #18a058);
  }

  &:disabled {
    opacity: 0.4;
    cursor: not-allowed;
  }
}

.editor-main {
  flex: 1;
  position: relative;
  overflow: hidden;
}

.search-bar {
  position: absolute;
  top: 12px;
  left: 12px;
  z-index: 50;
  display: flex;
  align-items: center;
  gap: 4px;
  background: var(--app-card-bg, #fff);
  border-radius: 8px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
  padding: 4px 8px;
}

.search-input {
  width: 180px;
  padding: 6px 10px;
  border: 1px solid var(--app-border, #e0e0e6);
  border-radius: 6px;
  font-size: 13px;
  background: transparent;
  color: var(--app-text-primary, #333);
  outline: none;
  transition: border-color 0.2s;

  &:focus {
    border-color: var(--app-primary, #18a058);
  }
}

.search-nav-btn {
  width: 28px;
  height: 28px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: transparent;
  border: 1px solid var(--app-border, #e0e0e6);
  border-radius: 6px;
  color: var(--app-text-primary, #333);
  cursor: pointer;
  font-size: 12px;
  transition: all 0.2s;

  &:hover {
    background: var(--app-hover-bg, #f0f0f0);
    border-color: var(--app-primary, #18a058);
  }
}

.search-count {
  font-size: 12px;
  color: var(--app-text-secondary, #666);
  white-space: nowrap;
  padding: 0 4px;
}

.loading-wrap {
  position: absolute;
  inset: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 16px;
  color: var(--app-text-secondary, #666);
}

.spinner {
  width: 40px;
  height: 40px;
  border: 3px solid var(--app-border, #e0e0e6);
  border-top-color: var(--app-primary, #18a058);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.mindmap-canvas {
  width: 100%;
  height: 100%;
  background: var(--app-bg, #f5f7fa);
  /* 阻止浏览器默认手势（移动端滚动/双指缩放）抢占节点拖拽 */
  touch-action: pan-x pan-y;
  -ms-touch-action: pan-x pan-y;

  // simple-mind-map 内部样式覆盖
  :deep(.mind-map) {
    width: 100%;
    height: 100%;
    /* SVG 内部允许触摸操作以派发事件 */
    touch-action: none;
  }
}

.btn-action-save,
.btn-action-history,
.btn-action-share,
.btn-action-export {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 6px 12px;
  border: 1px solid var(--app-border, #e0e0e6);
  border-radius: 6px;
  cursor: pointer;
  font-size: 13px;
  font-weight: 500;
  transition: all 0.2s;
  white-space: nowrap;
}

.btn-icon {
  font-size: 15px;
  line-height: 1;
}

.btn-label {
  line-height: 1;
}

.btn-action-save {
  background: var(--app-primary, #18a058);
  color: #fff;
  border-color: var(--app-primary, #18a058);

  &:hover {
    filter: brightness(1.05);
  }
}

.btn-action-history,
.btn-action-share,
.btn-action-export {
  background: #fff;
  color: var(--app-text-primary, #333);

  &:hover {
    background: var(--app-hover-bg, #f0f0f0);
    border-color: var(--app-primary, #18a058);
    color: var(--app-primary, #18a058);
  }
}

.btn-action-export {
  &:disabled {
    opacity: 0.6;
    cursor: not-allowed;
  }

  &.is-loading {
    border-color: var(--app-primary, #18a058);
    color: var(--app-primary, #18a058);
  }
}

.create-version-body {
  .tip {
    margin: 0 0 16px;
    font-size: 14px;
    color: var(--app-text-secondary, #666);

    strong {
      color: var(--app-primary, #18a058);
      font-size: 15px;
    }
  }
}

.content-edit-body {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.field-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.field-label {
  font-size: 13px;
  font-weight: 600;
  color: var(--app-text-secondary, #666);
}

.drawer-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  padding-right: 12px;
  font-size: 16px;
  font-weight: 600;
  color: var(--app-text-primary, #333);
}

.versions-list-wrap {
  padding: 0 4px 16px;
}

.versions-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.version-card {
  transition: all 0.2s;

  &:hover {
    box-shadow: 0 2px 12px rgba(0, 0, 0, 0.08);
  }

  :deep(.n-card__content) {
    padding: 16px !important;
  }
}

.version-card-header {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-bottom: 12px;
}

.version-title {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 15px;
  font-weight: 600;
  color: var(--app-text-primary, #333);
}

.version-note {
  color: var(--app-text-primary, #333);
  font-weight: 500;

  &.no-remark {
    color: var(--app-text-secondary, #999);
    font-weight: 400;
    font-style: italic;
  }
}

.version-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  font-size: 12px;
  color: var(--app-text-secondary, #666);
}

.meta-item {
  display: flex;
  align-items: center;
  gap: 2px;
}

.version-actions {
  display: flex;
  gap: 8px;
  justify-content: flex-end;
}

.share-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.share-card-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
}

.share-token-title {
  display: flex;
  align-items: center;
  gap: 6px;
}

.share-token-code {
  font-size: 12px;
  color: var(--app-text-secondary, #666);
  font-family: monospace;
}

.share-tags {
  display: flex;
  gap: 4px;
}

.share-url-row {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 8px;
  background: var(--app-bg, #f5f7fa);
  border-radius: 6px;
  margin-bottom: 8px;
}

.share-url {
  flex: 1;
  font-size: 12px;
  color: var(--app-primary, #18a058);
  word-break: break-all;
  font-family: monospace;
  padding: 2px 4px;
  margin: 0;
}

.share-meta {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 12px;
  color: var(--app-text-secondary, #666);
  margin-bottom: 10px;
}

.share-meta .meta-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.meta-val {
  color: var(--app-text-primary, #333);
}

.share-card-actions {
  display: flex;
  justify-content: flex-end;
}

.create-share-body {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.label-title {
  display: block;
  margin-bottom: 6px;
  font-size: 13px;
  font-weight: 600;
  color: var(--app-text-secondary, #666);
}

@media (max-width: 767px) {
  .editor-header {
    padding: 10px 10px;
    gap: 8px;
    flex-wrap: wrap;
    overflow: visible;
  }

  .btn-back {
    flex: 0 0 auto;
    padding: 8px 10px;
    font-size: 15px;
    .text {
      display: none;
    }
  }

  .editor-title {
    flex: 1 1 auto;
    max-width: none;
    min-width: 0;
  }

  .title-input {
    font-size: 14px;
    padding: 8px 8px;
    text-align: left;
  }

  .editor-actions {
    flex: 1 0 100%;
    flex-wrap: wrap;
    gap: 6px;
    overflow: visible;
  }

  .btn-tool {
    width: 38px;
    height: 38px;
    font-size: 16px;
    flex: 0 0 auto;
  }

  .btn-action-save .btn-label,
  .btn-action-history .btn-label,
  .btn-action-share .btn-label,
  .btn-action-export .btn-label {
    display: none;
  }

  .btn-action-save,
  .btn-action-history,
  .btn-action-share,
  .btn-action-export {
    padding: 8px 12px;
    font-size: 18px;
    min-width: 38px;
    min-height: 38px;
    flex: 0 0 auto;
  }

  .action-divider {
    display: none;
  }

  /* 移动端搜索栏更紧凑 */
  .search-bar {
    top: 8px;
    left: 8px;
    padding: 4px 6px;
    gap: 2px;
  }

  .search-input {
    width: 120px;
    font-size: 12px;
    padding: 4px 8px;
  }

  .search-nav-btn {
    width: 24px;
    height: 24px;
    font-size: 10px;
  }

  /* 移动端 loading 不遮挡工具栏 */
  .loading-wrap {
    background: rgba(255, 255, 255, 0.8);
  backdrop-filter: blur(2px);
  }
}
</style>
