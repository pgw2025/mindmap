<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed, nextTick, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useMessage, NInput, NModal, NDropdown } from 'naive-ui'
import MindMap from 'simple-mind-map'
import Search from 'simple-mind-map/src/plugins/Search.js'
import Export from 'simple-mind-map/src/plugins/Export.js'
import ExportPDF from 'simple-mind-map/src/plugins/ExportPDF.js'
import ExportXMind from 'simple-mind-map/src/plugins/ExportXMind.js'
import Drag from 'simple-mind-map/src/plugins/Drag.js'
import Select from 'simple-mind-map/src/plugins/Select.js'
import TouchEvent from 'simple-mind-map/src/plugins/TouchEvent.js'

// 1. 增强 Drag 插件：修复松手后画布漂移 bug，并在松手时即时同步执行重叠检测，防止 300ms 节流导致落位判定失效
if (Drag && (Drag as any).prototype) {
  const dragProto = (Drag as any).prototype
  const origDragOnMouseup = dragProto.onMouseup

  dragProto.onMouseup = async function (this: any, e: MouseEvent) {
    // 强制清除边缘移动定时器，杜绝任何情况下画布无休止平移
    if (this.autoMove) {
      this.autoMove.clearAutoMoveTimer()
    }

    // 若处于拖拽中，在松手瞬间强制同步触发一次非节流的 checkOverlapNode，精准判定目标父节点或兄弟节点
    if (this.isMousedown && this.isDragging && Drag.prototype.checkOverlapNode) {
      try {
        Drag.prototype.checkOverlapNode.call(this)
      } catch (err) {
        console.warn('[Drag] immediate checkOverlapNode call error:', err)
      }
    }

    // 执行原有 onMouseup 逻辑
    const result = await origDragOnMouseup.call(this, e)

    // 再次无条件确保定时器被清除
    if (this.autoMove) {
      this.autoMove.clearAutoMoveTimer()
    }

    return result
  }
}

// 2. 增强 TouchEvent 插件：处理 touchmove 阻止浏览器手势干扰，完善 touchcancel，并精确派发 mouseup 坐标
if (TouchEvent && (TouchEvent as any).prototype) {
  const proto = (TouchEvent as any).prototype

  // 单指移动时调用 preventDefault，防止浏览器手势拦截/触发 touchcancel
  proto.onTouchmove = function (this: any, e: globalThis.TouchEvent) {
    const len = e.touches.length
    if (len === 1) {
      const touch = e.touches[0]
      if (e.cancelable) {
        e.preventDefault()
      }
      this.dispatchMouseEvent('mousemove', touch.target, touch)
    } else if (len === 2) {
      const { disableTouchZoom, minTouchZoomScale, maxTouchZoomScale } = this.mindMap.opt
      if (disableTouchZoom) return
      const minScale = minTouchZoomScale === -1 ? -Infinity : minTouchZoomScale / 100
      const maxScale = maxTouchZoomScale === -1 ? Infinity : maxTouchZoomScale / 100
      const touch1 = e.touches[0]
      const touch2 = e.touches[1]
      const ox = touch1.clientX - touch2.clientX
      const oy = touch1.clientY - touch2.clientY
      const distance = Math.sqrt(Math.pow(ox, 2) + Math.pow(oy, 2))
      const { x: touch1ClientX, y: touch1ClientY } = this.mindMap.toPos(touch1.clientX, touch1.clientY)
      const { x: touch2ClientX, y: touch2ClientY } = this.mindMap.toPos(touch2.clientX, touch2.clientY)
      const cx = (touch1ClientX + touch2ClientX) / 2
      const cy = (touch1ClientY + touch2ClientY) / 2
      const view = this.mindMap.view
      if (!this.touchStartScaleView) {
        this.touchStartScaleView = {
          distance,
          scale: view.scale,
          x: view.x,
          y: view.y,
          cx,
          cy
        }
        return
      }
      const viewBefore = this.touchStartScaleView
      let scale = viewBefore.scale * (distance / viewBefore.distance)
      if (Math.abs(distance - viewBefore.distance) <= 10) {
        scale = viewBefore.scale
      }
      scale = scale < minScale ? minScale : scale > maxScale ? maxScale : scale
      const ratio = 1 - scale / viewBefore.scale
      view.scale = scale
      view.x = viewBefore.x + (cx - viewBefore.x) * ratio + (cx - viewBefore.cx) * scale
      view.y = viewBefore.y + (cy - viewBefore.y) * ratio + (cy - viewBefore.cy) * scale
      view.transform()
      this.mindMap.emit('scale', scale)
    }
  }

  // 触摸取消时派发 mouseup 安全收尾
  proto.onTouchcancel = function (this: any, e: globalThis.TouchEvent) {
    const touch = (e.changedTouches && e.changedTouches[0]) || (e.touches && e.touches[0]) || this.singleTouchstartEvent || null
    const target = touch?.target || e.target || document.body
    if (touch) {
      this.dispatchMouseEvent('mouseup', target, touch)
    } else {
      this.dispatchMouseEvent('mouseup', target)
    }
    this.touchesNum = 0
    this.singleTouchstartEvent = null
    this.touchStartScaleView = null
  }

  // 触摸结束时精准传递落点坐标
  proto.onTouchend = function (this: any, e: globalThis.TouchEvent) {
    const touch = (e.changedTouches && e.changedTouches[0]) || (e.touches && e.touches[0]) || this.singleTouchstartEvent || null
    const target = touch?.target || e.target || document.body
    if (touch) {
      this.dispatchMouseEvent('mouseup', target, touch)
    } else {
      this.dispatchMouseEvent('mouseup', target)
    }
    if (this.touchesNum === 1) {
      this.clickNum++
      setTimeout(() => {
        this.clickNum = 0
        this.lastTouchStartPosition = null
        this.lastTouchStartDistance = 0
      }, 300)
      const ev = this.singleTouchstartEvent
      if (this.clickNum > 1 && this.lastTouchStartDistance <= 5 && ev) {
        this.clickNum = 0
        this.dispatchMouseEvent('dblclick', ev.target, ev)
      }
    }
    this.touchesNum = 0
    this.singleTouchstartEvent = null
    this.touchStartScaleView = null
  }

  // 发送带精确坐标与状态的合成鼠标事件
  proto.dispatchMouseEvent = function (this: any, eventName: string, target: EventTarget, e?: any) {
    let opt: any = {
      which: 1,
      button: 0,
      buttons: eventName === 'mouseup' ? 0 : 1
    }
    if (e) {
      opt = {
        ...opt,
        screenX: e.screenX ?? 0,
        screenY: e.screenY ?? 0,
        clientX: e.clientX ?? 0,
        clientY: e.clientY ?? 0
      }
    }
    const event = new MouseEvent(eventName, {
      view: document.defaultView,
      bubbles: true,
      cancelable: true,
      ...opt
    })
    target.dispatchEvent(event)
  }
}

// 注册插件
MindMap.usePlugin(Search)
MindMap.usePlugin(Export)
MindMap.usePlugin(ExportPDF)
MindMap.usePlugin(ExportXMind)
MindMap.usePlugin(Drag)
MindMap.usePlugin(Select)
MindMap.usePlugin(TouchEvent)

import type { NodeDto, NodeCreatePayload, NodeUpdatePayload } from '@/api/nodes'
import type { MindMapDetail } from '@/api/mindmaps'
import { fetchMindMap, updateMindMap } from '@/api/mindmaps'
import { useNodesStore } from '@/stores/nodes'
import { useMindMapsStore } from '@/stores/mindmaps'
import { useAuthStore } from '@/stores/auth'
import { useTemplatesStore } from '@/stores/templates'
import { fetchTemplate } from '@/api/templates'
import NodeToolbar from './NodeToolbar.vue'
import ShareDrawer from './components/ShareDrawer.vue'
import VersionDrawer from './components/VersionDrawer.vue'
import NodeContentModal from './components/NodeContentModal.vue'
import { useMindMapSync } from './composables/useMindMapSync'
import { THEMES, getThemeConfig, getThemeIdOrDefault, type MindMapThemeConfig } from '@/themes/presets'

const route = useRoute()
const router = useRouter()
const message = useMessage()
const nodesStore = useNodesStore()
const mapsStore = useMindMapsStore()
const templatesStore = useTemplatesStore()

// 后端约定 Guid.Empty 表示清除引用（JSON null 不会触发 Guid? 更新）
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

// 根节点不能删除提示弹窗
const rootDeleteTipVisible = ref(false)
// 删除节点确认弹窗
const nodeDeleteConfirmVisible = ref(false)
const nodeDeleteTargetTitle = ref('')
const nodeDeleteSubmitting = ref(false)

const mindMapId = computed(() => route.params.id as string)
const readonly = computed(() => route.name === 'mindmap-preview')
const mapDetail = ref<MindMapDetail | null>(null)
const loading = ref(true)
const mindMapRef = ref<HTMLDivElement | null>(null)
let mindMapInstance: MindMap | null = null

/** 选中的节点样式 */
const selectedNodeId = ref<string | null>(null)
const showToolbar = ref(false)

/** 复制/粘贴剪贴板 */
const clipboardNode = ref<NodeDto | null>(null)

/** 导图内搜索 */
const searchKeyword = ref('')
const searchMatchCount = ref(0)
const searchCurrentIndex = ref(0)

// —— 组合式函数：数据转换/同步/方向归一化/拖拽预判 ——
const {
  isSettingData,
  bindGlobalMouseTracker,
  convertToMindMapData,
  reloadMindMap,
  handleDragEnd,
  normalizeRootChildDirections,
  bindIncrementalSyncHandlers
} = useMindMapSync({
  getMindMapInstance: () => mindMapInstance,
  nodesStore,
  readonly
})

// —— 子组件引用 ——
const versionDrawerRef = ref<InstanceType<typeof VersionDrawer> | null>(null)

// —— 弹窗/抽屉可见状态 ——
const shareDrawerVisible = ref(false)
const versionsDrawerVisible = ref(false)
const contentModalVisible = ref(false)

/** 当前选中节点（供 NodeContentModal 使用） */
const selectedNodeForContent = computed<NodeDto | null>(() => {
  if (!selectedNodeId.value) return null
  return nodesStore.findNode(selectedNodeId.value) ?? null
})

/** 初始化 simple-mind-map */
function initMindMap() {
  if (!mindMapRef.value) {
    console.error('[MindMap] 容器元素不存在，mindMapRef =', mindMapRef.value)
    return
  }

  // 1. 开启保护开关，防止初始化和首次 setData 触发 syncToBackend 误删数据库节点
  isSettingData.value = true

  const themeId = getThemeIdOrDefault(mapDetail.value?.theme)

  mindMapInstance = new MindMap({
    el: mindMapRef.value,
    data: {
      data: { text: '中心主题' },
      children: []
    },
    theme: 'classic',
    layout: 'mindMap',
    draggable: !readonly.value,
    contextMenu: !readonly.value,
    toolBar: !readonly.value,
    nodeLineDash: false,
    enableFreeDrag: true,
    scrollbarStyle: 'thin',
    minScale: 0.2,
    maxScale: 2,
    beforeDragEnd: handleDragEnd
  })

  // 全局鼠标位置记录（供 beforeDragEnd 判定方向用）
  bindGlobalMouseTracker()

  // 加载数据
  const mindMapData = convertToMindMapData(nodesStore.nodes)
  if (mindMapData) {
    mindMapInstance.setData(mindMapData)
    // setData 触发的 render 是 setTimeout 异步的，view.fit() 同步调用拿不到
    // 正确的节点位置，导致根节点不在画布中心。等首次渲染完成后再居中。
    // simple-mind-map 没有 once API，用 on + off 手动实现
    const onFirstRender = () => {
      mindMapInstance?.off('node_tree_render_end', onFirstRender)
      // 样式应用优先级：模板 > 主题
      // 模板：异步获取 configJson 后应用（完整自定义配置）
      // 主题：同步获取内置预设应用
      const templateId = mapDetail.value?.templateId
      if (templateId) {
        fetchTemplate(templateId)
          .then((tpl) => {
            try {
              const cfg = JSON.parse(tpl.configJson) as MindMapThemeConfig
              mindMapInstance?.setThemeConfig(cfg, false)
            } catch {
              mindMapInstance?.setThemeConfig(getThemeConfig(themeId), false)
            }
            const r = mindMapInstance?.renderer?.root
            if (r) {
              ;(mindMapInstance?.renderer as any)?.moveNodeToCenter(r)
            }
          })
          .catch(() => {
            mindMapInstance?.setThemeConfig(getThemeConfig(themeId), false)
          })
      } else {
        // 主题在 setData 之后应用，否则会被 setData 的异步渲染覆盖
        // setThemeConfig 第二个参数 notRender=false 表示立即触发重绘
        mindMapInstance?.setThemeConfig(getThemeConfig(themeId), false)
        const root = mindMapInstance?.renderer?.root
        if (root) {
          // moveNodeToCenter 在 Render 实例上，不在 MindMap 实例上
          ;(mindMapInstance?.renderer as any)?.moveNodeToCenter(root)
        }
      }
    }
    mindMapInstance.on('node_tree_render_end', onFirstRender)
  } else {
    // 没有数据时直接应用样式（模板优先，否则主题）
    const templateId = mapDetail.value?.templateId
    if (templateId) {
      fetchTemplate(templateId)
        .then((tpl) => {
          try {
            const cfg = JSON.parse(tpl.configJson) as MindMapThemeConfig
            mindMapInstance?.setThemeConfig(cfg, false)
          } catch {
            mindMapInstance?.setThemeConfig(getThemeConfig(themeId), false)
          }
        })
        .catch(() => {
          mindMapInstance?.setThemeConfig(getThemeConfig(themeId), false)
        })
    } else {
      mindMapInstance.setThemeConfig(getThemeConfig(themeId), false)
    }
  }

  // 3. 延迟关闭保护开关，确保初始渲染引发的 data_change 被安全跳过
  nextTick(() => {
    setTimeout(() => {
      isSettingData.value = false
    }, 500) // 延迟 500ms 避开初始化渲染期
  })

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

  // —— 增量同步事件绑定：替代旧的 data_change → syncToBackend 整树 diff 方案
  //    1. data_change_detail：simple-mind-map 内置 diff，传出 create/update/delete 明细
  //    2. node_text_edit_change：编辑中实时 debounce 文本更新
  bindIncrementalSyncHandlers()

  // 监听搜索匹配结果
  mindMapInstance.on('search_match_node_list_change', (...args: unknown[]) => {
    const list = args[0]
    searchMatchCount.value = Array.isArray(list) ? list.length : 0
    searchCurrentIndex.value = searchMatchCount.value > 0 ? 1 : 0
  })

  // 兜底：每次 layout 完成后，扫描根节点直接子节点
  mindMapInstance.on('node_tree_render_end', () => {
    if (readonly.value) return
    setTimeout(() => normalizeRootChildDirections(), 0)
  })
}

function getNextSortOrder(parentId: string | null): number {
  const children = nodesStore.getChildren(parentId)
  if (children.length === 0) return 0
  return Math.max(...children.map((c) => c.sortOrder)) + 1
}

/** 工具栏操作 */
async function handleAddChild() {
  if (!selectedNodeId.value) return
  const node = nodesStore.findNode(selectedNodeId.value)
  const isRootChild = node?.id === nodesStore.rootNode?.id
  const payload: NodeCreatePayload = {
    parentId: node?.id ?? null,
    title: '新子节点',
    sortOrder: getNextSortOrder(node?.id ?? null),
    direction: isRootChild ? 1 : undefined
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
  const isRootChild = node.parentId === nodesStore.rootNode?.id
  const payload: NodeCreatePayload = {
    parentId: node.parentId,
    title: '新节点',
    sortOrder: getNextSortOrder(node.parentId),
    direction: isRootChild ? 1 : undefined
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

/** 节点内容保存回调（来自 NodeContentModal） */
async function handleContentSave(payload: NodeUpdatePayload) {
  if (!selectedNodeId.value) return
  try {
    await nodesStore.update(selectedNodeId.value, payload)
    reloadMindMap()
  } catch (e) {
    message.error((e as Error).message || '保存失败')
  }
}

/** 版本回滚后刷新画布 + 节点 + 详情（来自 VersionDrawer） */
async function handleVersionRollback() {
  const mapId = mindMapId.value
  nodesStore.clearAll?.()
  await nodesStore.load(mapId)
  mapDetail.value = await fetchMindMap(mapId)
  reloadMindMap()
}

function openContentEditor() {
  if (!selectedNodeId.value) return
  contentModalVisible.value = true
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

const currentThemeId = computed(() => getThemeIdOrDefault(mapDetail.value?.theme))
const currentTemplateId = computed(() => mapDetail.value?.templateId ?? null)

const themeDropdownOptions = computed(() =>
  THEMES.map((t) => ({
    key: t.id,
    label: t.name,
    meta: t.description
  }))
)

const templateDropdownOptions = computed(() => [
  { key: '__none__', label: '不使用模板（用主题）' },
  ...templatesStore.enabledList.map((t) => ({ key: t.id, label: t.name }))
])

async function handleThemeSelect(key: string) {
  if (!mindMapInstance || !mapDetail.value) return
  if (key === currentThemeId.value && !currentTemplateId.value) return
  mindMapInstance.setThemeConfig(getThemeConfig(key))
  if (!readonly.value) {
    try {
      // 切换主题时清除模板（模板优先级高于主题，切换主题=放弃模板）
      // 后端约定 Guid.Empty 表示清除；JSON null 不会触发更新
      await updateMindMap(mindMapId.value, { theme: key, templateId: EMPTY_GUID })
      mapDetail.value.theme = key
      mapDetail.value.templateId = null
    } catch (e) {
      message.error('主题保存失败：' + (e as Error).message)
    }
  }
}

async function handleTemplateSelect(key: string) {
  if (!mindMapInstance || !mapDetail.value) return
  if (key === '__none__') {
    // 清除模板，回退到当前主题
    const themeId = currentThemeId.value
    mindMapInstance.setThemeConfig(getThemeConfig(themeId))
    if (!readonly.value) {
      try {
        await updateMindMap(mindMapId.value, { templateId: EMPTY_GUID })
        mapDetail.value.templateId = null
      } catch (e) {
        message.error('模板清除失败：' + (e as Error).message)
      }
    }
    return
  }
  if (key === currentTemplateId.value) return
  // 套用模板：拉取详情 → 应用 configJson → 保存 templateId
  try {
    const tpl = await fetchTemplate(key)
    let cfg: MindMapThemeConfig
    try {
      cfg = JSON.parse(tpl.configJson) as MindMapThemeConfig
    } catch {
      message.error('模板样式解析失败')
      return
    }
    mindMapInstance.setThemeConfig(cfg)
    if (!readonly.value) {
      await updateMindMap(mindMapId.value, { templateId: key })
      mapDetail.value.templateId = key
    }
  } catch (e) {
    message.error('模板切换失败：' + (e as Error).message)
  }
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

async function handleDescriptionBlur() {
  if (mapDetail.value) {
    try {
      const desc = (mapDetail.value.description ?? '').trim()
      mapDetail.value.description = desc || null
      await mapsStore.update(mindMapId.value, { description: desc })
      message.success('描述已更新')
    } catch (e) {
      message.error((e as Error).message)
    }
  }
}

function handleRootDeleteTipClose() {
  rootDeleteTipVisible.value = false
}

/** 分享设为公开后同步父组件状态 */
function handleSharePublicChange(isPublic: boolean) {
  if (mapDetail.value) {
    mapDetail.value.isPublic = mapDetail.value.isPublic || isPublic
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

    // 懒加载启用的模板列表（供工具栏模板下拉使用）
    templatesStore.loadEnabled().catch(() => { /* ignore */ })
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
  if (readonly.value) return
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
        <NInput v-if="!readonly" v-model:value="mapDetail.title" class="title-input" @blur="handleTitleBlur" />
        <span v-else class="title-text">{{ mapDetail.title }}</span>
        <div v-if="!readonly" class="desc-row">
          <NInput
            v-model:value="mapDetail.description"
            class="desc-input"
            placeholder="添加导图描述（可选）"
            clearable
            @blur="handleDescriptionBlur"
          />
        </div>
        <p v-else-if="mapDetail.description" class="desc-text">{{ mapDetail.description }}</p>
      </div>

      <div class="editor-actions">
        <template v-if="!readonly">
          <button class="btn-tool" :disabled="!nodesStore.canUndo" @click="handleUndo" title="撤销 (Ctrl+Z)">
            ↶
          </button>
          <button class="btn-tool" :disabled="!nodesStore.canRedo" @click="handleRedo" title="重做 (Ctrl+Y)">
            ↷
          </button>
          <button class="btn-tool" :disabled="!selectedNodeId" @click="handleCopy" title="复制 (Ctrl+C)">
            ⧉
          </button>
          <button class="btn-tool" :disabled="!clipboardNode" @click="handlePaste" title="粘贴 (Ctrl+V)">
            📋
          </button>
          <button class="btn-tool" :disabled="!selectedNodeId" @click="openContentEditor" title="编辑节点内容">
            📝
          </button>
          <span class="action-divider"></span>
          <button class="btn-action-save" @click="versionDrawerRef?.openCreateVersion()" title="保存为版本快照">
            <span class="btn-icon">💾</span><span class="btn-label">保存版本</span>
          </button>
        </template>
        <button class="btn-action-history" @click="versionsDrawerVisible = true" title="查看版本历史">
          <span class="btn-icon">🕘</span><span class="btn-label">历史</span>
        </button>
        <button class="btn-action-share" @click="shareDrawerVisible = true" title="分享此导图">
          <span class="btn-icon">🔗</span><span class="btn-label">分享</span>
        </button>
        <NDropdown trigger="click" :options="exportOptions" @select="handleExport">
          <button class="btn-action-export" :class="{ 'is-loading': exporting }" title="导出导图" :disabled="exporting">
            <span class="btn-icon">{{ exporting ? '⏳' : '📤' }}</span><span class="btn-label">{{ exporting ? '导出中...' :
              '导出'
            }}</span>
          </button>
        </NDropdown>
        <NDropdown trigger="click" :options="templateDropdownOptions" :value="currentTemplateId ?? '__none__'" @select="handleTemplateSelect">
          <button class="btn-action-template" :class="{ 'is-active': !!currentTemplateId }" title="切换模板">
            <span class="btn-icon">📋</span>
            <span class="btn-label">{{
              currentTemplateId
                ? (templatesStore.enabledList.find(t => t.id === currentTemplateId)?.name ?? '模板')
                : '模板'
            }}</span>
          </button>
        </NDropdown>
        <NDropdown trigger="click" :options="themeDropdownOptions" :value="currentThemeId" @select="handleThemeSelect">
          <button class="btn-action-theme" :class="{ 'is-dimmed': !!currentTemplateId }" title="切换主题">
            <span class="theme-swatch"
              :style="{ background: THEMES.find(t => t.id === currentThemeId)?.swatch.rootFill }"></span>
            <span class="btn-icon">🎨</span>
            <span class="btn-label">{{ THEMES.find(t => t.id === currentThemeId)?.name ?? '主题' }}</span>
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
        <input v-model="searchKeyword" class="search-input" type="text" placeholder="搜索节点..."
          @keyup.enter="handleSearch" />
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
    <NodeToolbar v-if="showToolbar && selectedNodeId" :node="nodesStore.findNode(selectedNodeId)"
      @add-child="handleAddChild" @add-sibling="handleAddSibling" @delete="handleDelete" @update="handleUpdateStyle"
      @copy="handleCopy" @paste="handlePaste" />

    <!-- 版本历史抽屉（含新建版本弹窗） -->
    <VersionDrawer ref="versionDrawerRef" v-model:show="versionsDrawerVisible" :mind-map-id="mindMapId"
      :node-count="nodesStore.nodes.length" @rollback="handleVersionRollback" />

    <!-- 分享抽屉（含新建分享弹窗） -->
    <ShareDrawer v-model:show="shareDrawerVisible" :mind-map-id="mindMapId"
      :is-public-default="mapDetail?.isPublic ?? false" @public-change="handleSharePublicChange" />

    <!-- 富文本节点内容编辑弹窗 -->
    <NodeContentModal v-model:show="contentModalVisible" :node="selectedNodeForContent" @save="handleContentSave" />

    <!-- 根节点不能删除提示 -->
    <NModal v-model:show="rootDeleteTipVisible" preset="dialog" type="warning" title="无法删除" positive-text="我知道了"
      display-directive="if" style="max-width: 420px" @positive-click="handleRootDeleteTipClose">
      根节点不能删除。你可以清空内容但不能删除中心主题。
    </NModal>

    <!-- 删除节点确认 -->
    <NModal v-model:show="nodeDeleteConfirmVisible" preset="dialog" type="warning" title="确认删除" positive-text="删除"
      negative-text="取消" :positive-button-props="{ type: 'error', loading: nodeDeleteSubmitting }"
      display-directive="if" style="max-width: 420px" @positive-click="submitNodeDelete">
      删除「{{ nodeDeleteTargetTitle }}」及其所有子节点？
    </NModal>
  </div>
</template>

<style scoped lang="scss" src="./MindMapEditorView.scss"></style>
