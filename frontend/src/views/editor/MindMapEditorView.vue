<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed, nextTick, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useMessage, useDialog, NInput } from 'naive-ui'
import MindMap from 'simple-mind-map'
import Search from 'simple-mind-map/src/plugins/Search.js'

// 注册搜索插件
MindMap.usePlugin(Search)
import type { NodeDto, NodeCreatePayload, NodeUpdatePayload } from '@/api/nodes'
import type { MindMapDetail } from '@/api/mindmaps'
import { fetchMindMap } from '@/api/mindmaps'
import { useNodesStore } from '@/stores/nodes'
import { useMindMapsStore } from '@/stores/mindmaps'
import NodeToolbar from './NodeToolbar.vue'

const route = useRoute()
const router = useRouter()
const message = useMessage()
const dialog = useDialog()
const nodesStore = useNodesStore()
const mapsStore = useMindMapsStore()

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
function convertToMindMapData(nodes: NodeDto[]): unknown {
  if (nodes.length === 0) return null

  const nodeMap = new Map<string, unknown>()
  const roots: unknown[] = []

  for (const n of nodes) {
    const nodeData = {
      id: n.id,
      data: {
        text: n.title,
        title: n.title,
        content: n.content ?? '',
        note: n.note ?? ''
      },
      children: [] as unknown[],
      _backend: n
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
      if (backendNode.title !== tn.text) updates.title = tn.text
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
    dialog.warning({
      title: '无法删除',
      content: '根节点不能删除。你可以清空内容但不能删除中心主题。'
    })
    return
  }

  dialog.warning({
    title: '确认删除',
    content: `删除「${node.title}」及其所有子节点？`,
    positiveText: '删除',
    negativeText: '取消',
    onPositiveClick: async () => {
      try {
        await nodesStore.remove(selectedNodeId.value!)
        selectedNodeId.value = null
        showToolbar.value = false
        reloadMindMap()
        message.success('已删除')
      } catch (e) {
        message.error((e as Error).message)
      }
    }
  })
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

  // simple-mind-map 内部样式覆盖
  :deep(.mind-map) {
    width: 100%;
    height: 100%;
  }
}

@media (max-width: 767px) {
  .editor-header {
    padding: 8px 12px;
    gap: 8px;
  }

  .btn-back .text {
    display: none;
  }

  .editor-title {
    max-width: 200px;
  }

  .title-input {
    font-size: 14px;
    padding: 6px 8px;
  }

  .btn-tool {
    width: 32px;
    height: 32px;
    font-size: 14px;
  }
}
</style>
