<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed, nextTick, watch, h } from 'vue'
import { useRoute, useRouter, onBeforeRouteLeave } from 'vue-router'
import { useMessage, NModal, NDropdown, type DropdownOption } from 'naive-ui'
import MindMap from 'simple-mind-map'
import Search from 'simple-mind-map/src/plugins/Search.js'
import Export from 'simple-mind-map/src/plugins/Export.js'
import ExportPDF from 'simple-mind-map/src/plugins/ExportPDF.js'
import ExportXMind from 'simple-mind-map/src/plugins/ExportXMind.js'
import Drag from 'simple-mind-map/src/plugins/Drag.js'
import Select from 'simple-mind-map/src/plugins/Select.js'
import TouchEvent from 'simple-mind-map/src/plugins/TouchEvent.js'
import AssociativeLine from 'simple-mind-map/src/plugins/AssociativeLine.js'
import OuterFrame from 'simple-mind-map/src/plugins/OuterFrame.js'
import { throttle } from 'simple-mind-map/src/utils'

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

  // —— P0-1 & P2-2：调整拖拽热区比例 ——
  // 桌面端：兄弟节点热区从 1/4 扩大到 1/3，子节点区域相应从中间 1/2 缩小到 1/3
  // 移动端：进一步扩大到 3/8，子节点区域约 1/4（触屏精度低，默认倾向同级插入）
  // 思路：通过包装检测方法，临时放大 getNodeRect 返回的尺寸，
  // 使得方法内部计算的 oneFourthHeight/oneFourthWidth 按比例扩大
  const SIBLING_ZONE_SCALE_DESKTOP = 4 / 3  // 桌面端：H * 4/3 / 4 = H/3
  const SIBLING_ZONE_SCALE_MOBILE = 1.5     // 移动端：H * 1.5 / 4 = 3H/8

  const isMobileDrag = () => {
    if (typeof window === 'undefined') return false
    return window.innerWidth < 768 || ('ontouchstart' in window)
  }

  const wrapDragCheck = (origFn: any, sizeKey: 'originHeight' | 'originWidth') => {
    return function (this: any, ...args: any[]) {
      const origGetNodeRect = this.getNodeRect
      const scale = isMobileDrag() ? SIBLING_ZONE_SCALE_MOBILE : SIBLING_ZONE_SCALE_DESKTOP
      this.getNodeRect = function (node: any) {
        const rect = origGetNodeRect.call(this, node)
        rect[sizeKey] = rect[sizeKey] * scale
        return rect
      }
      const result = origFn.apply(this, args)
      this.getNodeRect = origGetNodeRect
      return result
    }
  }

  dragProto.handleVerticalCheck = wrapDragCheck(dragProto.handleVerticalCheck, 'originHeight')
  dragProto.handleHorizontalCheck = wrapDragCheck(dragProto.handleHorizontalCheck, 'originWidth')

  // —— P0-2：降低重叠检测节流时间（300ms → 120ms），让拖拽更跟手 ——
  // 重写 bindEvent，在原始绑定完成后用更短的节流时间重新包装 checkOverlapNode
  const origBindEvent = dragProto.bindEvent
  dragProto.bindEvent = function (this: any) {
    origBindEvent.call(this)
    this.checkOverlapNode = throttle(Drag.prototype.checkOverlapNode, 120, this)
  }

  // —— P1-1：子节点模式视觉提示（目标节点高亮 + 标签） + 兄弟节点模式标签 ——
  // 当拖拽进入「成为子节点」模式时，目标节点高亮虚线框 + 「添加为子节点」标签
  // 当拖拽进入「同级插入」模式时，显示「插入为同级」标签
  const origHandleOverlapNode = dragProto.handleOverlapNode
  dragProto.handleOverlapNode = function (this: any) {
    origHandleOverlapNode.call(this)
    if (!this.overlapNode || !this.mindMap?.otherDraw) return

    const node = this.overlapNode
    const { left, top, width, height } = node
    const padding = 6

    // 高亮虚线框
    if (!this._overlapHighlight) {
      this._overlapHighlight = this.mindMap.otherDraw
        .rect()
        .radius(8)
        .stroke({ color: '#18a058', width: 2, dasharray: '6,4' })
        .fill({ color: 'rgba(24, 160, 88, 0.1)' })
        .css('pointer-events', 'none')
        .css('z-index', 9998)
    }
    this._overlapHighlight
      .size(width + padding * 2, height + padding * 2)
      .move(left - padding, top - padding)
      .show()

    // 文字标签
    if (!this._overlapLabel) {
      this._overlapLabel = this.mindMap.otherDraw.group().css('pointer-events', 'none')
      this._overlapLabel._bg = this._overlapLabel.rect().radius(4).fill({ color: '#18a058' })
      this._overlapLabel._text = this._overlapLabel
        .text('添加为子节点')
        .fill({ color: '#fff' })
        .font({ size: 12, family: 'system-ui, -apple-system, sans-serif' })
    }
    const labelText = this._overlapLabel._text
    const labelBg = this._overlapLabel._bg
    const textBBox = labelText.bbox()
    const padX = 10
    const padY = 5
    labelBg.size(textBBox.width + padX * 2, textBBox.height + padY * 2).move(0, 0)
    labelText.move(padX, padY)
    const labelW = textBBox.width + padX * 2
    const labelX = left + width / 2 - labelW / 2
    const labelY = top - 32
    this._overlapLabel.move(labelX, labelY).show()

    // 清理兄弟节点标签（避免两种模式同时显示）
    if (this._siblingLabel) {
      this._siblingLabel.hide()
    }
  }

  // 包装 setPlaceholderRect：在兄弟节点模式下显示「插入为同级」标签
  const origSetPlaceholderRect = dragProto.setPlaceholderRect
  dragProto.setPlaceholderRect = function (this: any, opts: any) {
    origSetPlaceholderRect.call(this, opts)
    if (!this.mindMap?.otherDraw || this.overlapNode) return

    const isSiblingMode = this.prevNode || this.nextNode
    if (!isSiblingMode) return

    const { x, y } = opts

    if (!this._siblingLabel) {
      this._siblingLabel = this.mindMap.otherDraw.group().css('pointer-events', 'none')
      this._siblingLabel._bg = this._siblingLabel.rect().radius(4).fill({ color: '#3b82f6' })
      this._siblingLabel._text = this._siblingLabel
        .text('插入为同级')
        .fill({ color: '#fff' })
        .font({ size: 12, family: 'system-ui, -apple-system, sans-serif' })
    }
    const labelText = this._siblingLabel._text
    const labelBg = this._siblingLabel._bg
    const textBBox = labelText.bbox()
    const padX = 10
    const padY = 5
    labelBg.size(textBBox.width + padX * 2, textBBox.height + padY * 2).move(0, 0)
    labelText.move(padX, padY)
    const labelW = textBBox.width + padX * 2
    const labelX = x + this.placeholderWidth / 2 - labelW / 2
    const labelY = y - 28
    this._siblingLabel.move(labelX, labelY).show()

    // 清理子节点高亮
    if (this._overlapHighlight) this._overlapHighlight.hide()
    if (this._overlapLabel) this._overlapLabel.hide()
  }

  // 清理高亮和标签
  const origRemoveCloneNode = dragProto.removeCloneNode
  dragProto.removeCloneNode = function (this: any) {
    origRemoveCloneNode.call(this)
    if (this._overlapHighlight) {
      this._overlapHighlight.remove()
      this._overlapHighlight = null
    }
    if (this._overlapLabel) {
      this._overlapLabel.remove()
      this._overlapLabel = null
    }
    if (this._siblingLabel) {
      this._siblingLabel.remove()
      this._siblingLabel = null
    }
  }
}

// 2. 增强 TouchEvent 插件：处理 touchmove 阻止浏览器手势干扰，完善 touchcancel，并精确派发 mouseup 坐标
if (TouchEvent && (TouchEvent as any).prototype) {
  const proto = (TouchEvent as any).prototype

  // 单指移动时判断是否落在工具栏或输入控件内，如果在内部则允许原生滚动，不拦截；在画布上才 preventDefault
  proto.onTouchstart = function (this: any, e: globalThis.TouchEvent) {
    const target = e.target as HTMLElement | null
    if (target && target.closest('.node-toolbar, .mobile-zen-pill, .btn-mobile-zen-toggle, .n-modal, .n-drawer, .search-bar, .editor-header, input, textarea, button, select')) {
      // 允许工具栏、悬浮胶囊和输入控件原生交互与点击，不转化为画布 mousedown
      this.touchesNum = 0
      this.singleTouchstartEvent = null
      return
    }
    this.touchesNum = e.touches.length
    this.touchStartScaleView = null
    if (this.touchesNum === 1) {
      const touch = e.touches[0]
      this.singleTouchstartEvent = touch
      this.dispatchMouseEvent('mousedown', touch.target, touch)
    }
  }

  // 单指移动时：如果触摸目标在工具栏/弹窗内，不阻止默认滚动；否则调用 preventDefault 驱动画布拖动
  proto.onTouchmove = function (this: any, e: globalThis.TouchEvent) {
    const target = e.target as HTMLElement | null
    if (target && target.closest('.node-toolbar, .mobile-zen-pill, .btn-mobile-zen-toggle, .n-modal, .n-drawer, .search-bar, .editor-header, input, textarea, button, select')) {
      return
    }

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

// 3. 增强 MindMap 核心：容错容器尺寸计算，杜绝 "容器元素el的宽高不能为0" 崩溃
if (MindMap && (MindMap as any).prototype) {
  const mapProto = (MindMap as any).prototype
  mapProto.getElRectInfo = function (this: any) {
    if (!this.el) return
    this.elRect = this.el.getBoundingClientRect()
    this.width = this.elRect.width || this.el.clientWidth || window.innerWidth || 800
    this.height = this.elRect.height || this.el.clientHeight || (window.innerHeight - 60) || 600
    if (this.width <= 0) this.width = 800
    if (this.height <= 0) this.height = 600
  }
}

// 4. 增强 AssociativeLine 插件：修正连线端点方向为"后方"
//    连线端点固定指向节点远离根节点的一侧（后方），不指向节点的前方/上方/下方
//    左侧布局节点（centerX < rootCenterX）→ 后方=left；右侧布局节点 → 后方=right
if (AssociativeLine && (AssociativeLine as any).prototype) {
  const alProto = (AssociativeLine as any).prototype
  const originalUpdateAllLinesPos = alProto.updateAllLinesPos

  alProto.updateAllLinesPos = function (this: any, node: any, toNode: any, associativeLinePoint: any) {
    const [startPoint, endPoint] = originalUpdateAllLinesPos.call(this, node, toNode, associativeLinePoint)
    const root = this.mindMap?.renderer?.root
    if (!root || !node || !toNode) return [startPoint, endPoint]
    const rootCenterX = root.left + root.width / 2
    let sp = startPoint
    let ep = endPoint
    // 修正起点方向为"后方"（远离根节点的一侧）
    if (!node.isRoot) {
      const dir = (node.left + node.width / 2) < rootCenterX ? 'left' : 'right'
      const range = startPoint.range || 0
      const { left, top, width, height } = node
      sp = dir === 'left'
        ? { x: left, y: top + height / 2 - range, dir, range }
        : { x: left + width, y: top + height / 2 - range, dir, range }
    }
    // 修正终点方向为"后方"
    if (!toNode.isRoot) {
      const dir = (toNode.left + toNode.width / 2) < rootCenterX ? 'left' : 'right'
      const range = endPoint.range || 0
      const { left, top, width, height } = toNode
      ep = dir === 'left'
        ? { x: left, y: top + height / 2 - range, dir, range }
        : { x: left + width, y: top + height / 2 - range, dir, range }
    }
    return [sp, ep]
  }

  // 增强 drawLine：根据端点方向设置朝外的控制点偏移，使弧度朝向节点外侧
  // 左侧布局（dir=left）→ 控制点向左偏移，弧度朝左（朝外）
  // 右侧布局（dir=right）→ 控制点向右偏移，弧度朝右（朝外）
  const originalDrawLine = alProto.drawLine
  alProto.drawLine = function (this: any, startPoint: any, endPoint: any, node: any, toNode: any) {
    const nodeData = node?.nodeData?.data
    const savedOffsets = nodeData?.associativeLineTargetControlOffsets
    if (nodeData) {
      const targets = nodeData.associativeLineTargets || []
      const toUid = toNode?.getData?.('uid') || toNode?.nodeData?.data?.uid
      const targetIndex = targets.findIndex((t: string) => t === toUid)
      const spDir = startPoint.dir
      const epDir = endPoint.dir
      const yDiff = endPoint.y - startPoint.y
      // 朝外的 x 偏移量，保证曲线明显朝外
      const xOut = Math.max(50, Math.abs(yDiff) / 2 + 50)
      const tempOffsets = Array.isArray(savedOffsets) ? [...savedOffsets] : []
      if (targetIndex >= 0) {
        tempOffsets[targetIndex] = [
          {
            x: spDir === 'left' ? -xOut : (spDir === 'right' ? xOut : 0),
            y: yDiff / 2
          },
          {
            x: epDir === 'left' ? -xOut : (epDir === 'right' ? xOut : 0),
            y: -yDiff / 2
          }
        ]
        nodeData.associativeLineTargetControlOffsets = tempOffsets
      } else {
        nodeData.associativeLineTargetControlOffsets = null
      }
    }
    const result = originalDrawLine.call(this, startPoint, endPoint, node, toNode)
    if (nodeData) {
      nodeData.associativeLineTargetControlOffsets = savedOffsets
    }
    return result
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
MindMap.usePlugin(AssociativeLine)
MindMap.usePlugin(OuterFrame)

import type { NodeDto, NodeCreatePayload, NodeUpdatePayload, NodeTreeNodeDto } from '@/api/nodes'
import type { MindMapDetail } from '@/api/mindmaps'
import { fetchMindMap, updateMindMap } from '@/api/mindmaps'
import * as offlineDb from '@/offline/db'
import { withFallback } from '@/offline/fallback'
import { getOfflineStatus, offlineState, formatSyncTime } from '@/offline/sync'
import { useNodesStore } from '@/stores/nodes'
import { useMindMapsStore } from '@/stores/mindmaps'
import { useAuthStore } from '@/stores/auth'
import { useTemplatesStore } from '@/stores/templates'
import { useThemeStore } from '@/stores/theme'
import { fetchTemplate } from '@/api/templates'
import NodeToolbar from './NodeToolbar.vue'
import ShareDrawer from './components/ShareDrawer.vue'
import VersionDrawer from './components/VersionDrawer.vue'
import NodeContentModal from './components/NodeContentModal.vue'
import NotePanel from './components/NotePanel.vue'
import NodeNoteTooltip from './components/NodeNoteTooltip.vue'
import OuterFrameStylePanel from './components/OuterFrameStylePanel.vue'
import { useMindMapSync } from './composables/useMindMapSync'
import {
  THEMES,
  resolveThemeConfig,
  getThemeIdOrDefault,
  getThemePreset,
  getThemeSwatch,
  type MindMapThemeConfig
} from '@/themes/presets'

const route = useRoute()
const router = useRouter()
const message = useMessage()
const nodesStore = useNodesStore()
const mapsStore = useMindMapsStore()
const templatesStore = useTemplatesStore()
const themeStore = useThemeStore()

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
const noteTooltipRef = ref<InstanceType<typeof NodeNoteTooltip> | null>(null)
let mindMapInstance: MindMap | null = null

// —— 离线快照状态 ——
/** 导图详情是否来自离线快照 */
const isOfflineDetail = ref(false)
/** 详情或节点任一来自离线快照 → 顶栏标注「离线数据」并禁用强依赖网络的按钮 */
const isOfflineData = computed(() => isOfflineDetail.value || nodesStore.isOfflineSnapshot)
const offlineLastSyncText = computed(() => formatSyncTime(offlineState.lastSync))

// 选中的节点样式
const selectedNodeId = ref<string | null>(null)
/** 当前激活（选中）的节点数量，多选时 >1，用于切换「摘要」按钮为「多节点摘要」 */
const activeNodeCount = ref(0)
const showToolbar = ref(false)

// 移动端顶部标题/描述/导航栏沉浸折叠状态（Zen 模式，默认开启沉浸式）
const isMobileHeaderCollapsed = ref(true)
let containerResizeObserver: ResizeObserver | null = null

function toggleMobileHeader() {
  isMobileHeaderCollapsed.value = !isMobileHeaderCollapsed.value
  nextTick(() => {
    mindMapInstance?.resize()
  })
}

/** 复制/粘贴剪贴板 */
const clipboardNode = ref<NodeDto | null>(null)

/** 导图内搜索 */
const searchKeyword = ref('')
const searchMatchCount = ref(0)
const searchCurrentIndex = ref(0)

// —— 组合式函数：数据转换/同步/方向归一化/拖拽预判 ——
const {
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
  hasPendingWriteOps
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
const notePanelVisible = ref(false)

/** 外框样式面板状态：选中外框激活时显示，取消激活时隐藏 */
const outerFramePanelVisible = ref(false)
/** 当前激活外框的样式配置，传给 OuterFrameStylePanel */
const outerFrameConfig = ref({
  strokeColor: '#0984e3',
  strokeWidth: 2,
  strokeDasharray: '5,5',
  radius: 5,
  fill: 'rgba(9,132,227,0.05)',
  text: '',
  textFontSize: 14,
  textColor: '#333',
  textBgColor: 'rgba(9,132,227,0.05)'
})

/** 外框标题浮动编辑器状态 */
const outerFrameTitleEditorVisible = ref(false)
const outerFrameTitleEditorValue = ref('')
const outerFrameTitleEditorStyle = ref({ left: '0px', top: '0px', fontSize: '14px', color: '#333' })
const outerFrameTitleEditorInputRef = ref<HTMLInputElement | null>(null)

/** 当前选中节点（供 NodeContentModal 使用） */
const selectedNodeForContent = computed<NodeDto | null>(() => {
  if (!selectedNodeId.value) return null
  return nodesStore.findNode(selectedNodeId.value) ?? null
})

/** 已解析的模板配置（模板优先级高于主题；明暗切换时复用它，避免重复请求） */
const templateConfigCache = ref<MindMapThemeConfig | null>(null)

/**
 * 把当前「色相 × 明暗」的解析结果应用到画布 —— 全组件唯一的配色出口。
 * 初始化、切换主题、套用/清除模板、切浅色深色都必须走这里，
 * 否则会出现两套解析逻辑，深色下必然漏掉某条路径。
 * @param themeIdOverride 用于「先预览后落库」的场景（切换主题时后端还没返回）
 */
function applyResolvedTheme(notRender = false, themeIdOverride?: string) {
  if (!mindMapInstance) return
  const cfg = resolveThemeConfig(themeIdOverride ?? mapDetail.value?.theme, themeStore.isDark, {
    templateConfig: templateConfigCache.value
  })
  // 第二个参数 notRender=false 表示立即触发重绘
  mindMapInstance.setThemeConfig(cfg, notRender)
}

/**
 * 拉取并缓存模板配置后应用配色；无模板或模板获取/解析失败时回退到主题预设。
 */
async function applyResolvedThemeWithTemplate() {
  const templateId = mapDetail.value?.templateId
  if (!templateId) {
    templateConfigCache.value = null
    applyResolvedTheme()
    return
  }
  try {
    const tpl = await fetchTemplate(templateId)
    templateConfigCache.value = JSON.parse(tpl.configJson) as MindMapThemeConfig
  } catch {
    // 拉取失败或 configJson 不是合法 JSON：清掉缓存，回退到主题预设
    templateConfigCache.value = null
  }
  applyResolvedTheme()
}

// 切换浅色/深色时重新解析画布配色。
// 只换配色：不重新居中、不动缩放与位移（setThemeConfig 内部不会重置视口）。
watch(
  () => themeStore.isDark,
  () => applyResolvedTheme()
)

/** 初始化 simple-mind-map */
function initMindMap() {
  if (!mindMapRef.value) {
    console.error('[MindMap] 容器元素不存在，mindMapRef =', mindMapRef.value)
    return
  }

  // 1. 开启保护开关，防止初始化和首次 setData 触发 syncToBackend 误删数据库节点
  isSettingData.value = true

  mindMapInstance = new MindMap({
    el: mindMapRef.value,
    data: {
      data: { text: '中心主题' },
      children: []
    },
    theme: 'classic',
    layout: 'mindMap',
    draggable: true,
    readonly: readonly.value,
    contextMenu: !readonly.value,
    toolBar: !readonly.value,
    nodeLineDash: false,
    enableFreeDrag: !readonly.value,
    scrollbarStyle: 'thin',
    minScale: 0.2,
    maxScale: 2,
    customNoteContentShow: {
      show: (note: string, left: number, top: number) =>
        noteTooltipRef.value?.show(note, left, top),
      hide: () => noteTooltipRef.value?.hide()
    },
    beforeDragEnd: handleDragEnd,
    // —— P1-2：拖拽透明度优化 ——
    // 克隆节点更透明（看清下方落点），原节点更淡（视觉区分更明显）
    dragOpacityConfig: {
      cloneNodeOpacity: 0.55,
      beingDragNodeOpacity: 0.25
    }
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
      // 配色必须在 setData 之后应用，否则会被 setData 的异步渲染覆盖。
      // 样式优先级：模板 > 主题；浅色/深色由 applyResolvedTheme 统一派生。
      applyResolvedThemeWithTemplate().finally(() => {
        const root = mindMapInstance?.renderer?.root
        if (root) {
          // moveNodeToCenter 在 Render 实例上，不在 MindMap 实例上
          ; (mindMapInstance?.renderer as any)?.moveNodeToCenter(root)
        }
      })
    }
    mindMapInstance.on('node_tree_render_end', onFirstRender)
  } else {
    // 没有数据时直接应用样式（模板优先，否则主题）
    void applyResolvedThemeWithTemplate()
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
    const count = activeNodeList?.length ?? 0
    activeNodeCount.value = count
    if (count > 0) {
      const id = activeNodeList![0]?.nodeData?.id
      if (id) {
        selectedNodeId.value = id
        showToolbar.value = true
      }
    } else {
      selectedNodeId.value = null
      showToolbar.value = false
    }
  })

  // 监听外框激活：从 activeOuterFrame 读取当前样式填充到面板
  mindMapInstance.on('outer_frame_active', (...args: unknown[]) => {
    // 事件参数为 (el, node, range) 三个独立参数
    const el = args[0] as any
    const node = args[1] as any
    const range = args[2] as [number, number] | undefined
    const inst = mindMapInstance
    if (!inst?.outerFrame || !node) return

    const of = inst.outerFrame as any

    // 从第一个节点读取完整的外框样式配置
    let styleConfig: any = {}
    try {
      const firstNode = of.getNodeRangeFirstNode
        ? of.getNodeRangeFirstNode(node, range)
        : node
      if (firstNode && typeof firstNode.getData === 'function') {
        styleConfig = of.getStyle ? of.getStyle(firstNode) : {}
      }
    } catch {
      // getStyle 调用失败，忽略
    }

    // 从 el.cacheStyle 兜底读取
    if (!Object.keys(styleConfig).length && el?.cacheStyle) {
      styleConfig = { ...styleConfig, ...el.cacheStyle }
    }

    // 读取实际的外框数据（用户设置的值）
    let nodeData: any = {}
    try {
      if (node && typeof node.getData === 'function') {
        nodeData = node.getData('outerFrame') || {}
      }
    } catch {
      // 忽略
    }

    // 映射字段名：插件字段 → 我们面板使用的字段
    outerFrameConfig.value = {
      strokeColor: nodeData.strokeColor ?? styleConfig.strokeColor ?? '#0984e3',
      strokeWidth: nodeData.strokeWidth ?? styleConfig.strokeWidth ?? 2,
      strokeDasharray: nodeData.strokeDasharray ?? styleConfig.strokeDasharray ?? '5,5',
      radius: nodeData.radius ?? styleConfig.radius ?? 5,
      fill: nodeData.fill ?? styleConfig.fill ?? 'rgba(9,132,227,0.05)',
      text: nodeData.text ?? styleConfig.text ?? '',
      // 插件字段名：fontSize / color / textFill
      textFontSize: nodeData.fontSize ?? nodeData.textFontSize ?? styleConfig.fontSize ?? 14,
      textColor: nodeData.color ?? nodeData.textColor ?? styleConfig.color ?? '#333',
      textBgColor: nodeData.textFill ?? nodeData.textBgColor ?? styleConfig.textFill ?? 'rgba(9,132,227,0.1)'
    }
    outerFramePanelVisible.value = true
  })

  // 监听外框取消激活：隐藏样式面板
  mindMapInstance.on('outer_frame_deactivate', () => {
    outerFramePanelVisible.value = false
  })

  // 监听外框删除：隐藏样式面板
  mindMapInstance.on('outer_frame_delete', () => {
    outerFramePanelVisible.value = false
  })

  // 绑定双击外框标题编辑事件
  mindMapRef.value?.addEventListener('dblclick', handleOuterFrameTitleDblClick)

  // —— 增量同步事件绑定：替代旧的 data_change → syncToBackend 整树 diff 方案
  //    1. data_change_detail：simple-mind-map 内置 diff，传出 create/update/delete 明细
  //    2. node_text_edit_change：编辑中实时 debounce 文本更新
  bindIncrementalSyncHandlers()

  // —— P2-1：拖拽完成后显示可撤销提示 ——
  let lastDragMsg: any = null
  ;(mindMapInstance as any).on('node_dragend', () => {
    if (readonly.value) return
    if (lastDragMsg) {
      lastDragMsg.destroy()
      lastDragMsg = null
    }
    lastDragMsg = message.success('节点已移动', {
      duration: 3000,
      action: () => h('span', {
        style: {
          color: '#18a058',
          cursor: 'pointer',
          marginLeft: '8px',
          fontWeight: 500
        },
        onClick: (e: Event) => {
          e.stopPropagation()
          handleUndo()
          if (lastDragMsg) {
            lastDragMsg.destroy()
            lastDragMsg = null
          }
        }
      }, '撤销')
    } as any)
    setTimeout(() => { lastDragMsg = null }, 3000)
  })

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
    // 折叠态父节点上新增子节点时自动展开父节点（与画布 Tab/右键行为一致），
    // 避免新节点在 reload 后被折叠隐藏
    if (node?.isCollapsed) {
      await nodesStore.update(node.id, { isCollapsed: false })
    }
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

async function submitNodeDelete(): Promise<boolean> {
  if (!selectedNodeId.value) return true
  nodeDeleteSubmitting.value = true
  try {
    await nodesStore.remove(selectedNodeId.value)
    selectedNodeId.value = null
    showToolbar.value = false
    reloadMindMap()
    message.success('已删除')
    nodeDeleteConfirmVisible.value = false
    return true
  } catch (e) {
    message.error((e as Error).message)
    return false
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

/** 打开节点备注面板 */
function openNotePanel() {
  if (!selectedNodeId.value) return
  notePanelVisible.value = true
}

/** 从当前选中节点开始创建关联线：进入连线模式后点击目标节点即可 */
function createAssociativeLine() {
  const inst = mindMapInstance
  if (!inst || !inst.associativeLine) return
  ;(inst.associativeLine as any).createLineFromActiveNode()
}

/** 给当前选中节点添加摘要：调用核心库 ADD_GENERALIZATION 命令，会自动进入摘要文字编辑状态 */
function addGeneralization() {
  const inst = mindMapInstance
  if (!inst) return
  inst.execCommand('ADD_GENERALIZATION')
}

/** 给当前选中节点添加外框：调用 OuterFrame 插件 addOuterFrame，使用默认样式 */
function addOuterFrame() {
  const inst = mindMapInstance
  if (!inst || !inst.outerFrame) return
  ;(inst.outerFrame as any).addOuterFrame()
}

/** 外框样式面板更新回调：调用 updateActiveOuterFrame 写入配置到节点 data */
function updateOuterFrameStyle(payload: Partial<typeof outerFrameConfig.value>) {
  const inst = mindMapInstance
  if (!inst || !inst.outerFrame) return
  Object.assign(outerFrameConfig.value, payload)

  // 将我们的字段名映射为插件识别的字段名
  const pluginPayload: any = {}
  ;(['strokeColor', 'strokeWidth', 'strokeDasharray', 'radius', 'fill', 'text'] as const).forEach(key => {
    if (payload[key] !== undefined) {
      pluginPayload[key] = payload[key]
    }
  })
  // 文字相关字段名映射
  if (payload.textFontSize !== undefined) pluginPayload.fontSize = payload.textFontSize
  if (payload.textColor !== undefined) pluginPayload.color = payload.textColor
  if (payload.textBgColor !== undefined) pluginPayload.textFill = payload.textBgColor

  ;(inst.outerFrame as any).updateActiveOuterFrame(pluginPayload)

  // 修复：激活状态下插件的 updateOuterFrameStyle 会强制把 dasharray 设为 none（选中高亮）
  // 这里手动把用户设置的线型应用回去，并同步更新 cacheStyle 确保失活后也正确
  const of = inst.outerFrame as any
  const active = of.activeOuterFrame
  if (active && active.el && pluginPayload.strokeDasharray !== undefined) {
    const dasharray = pluginPayload.strokeDasharray
    active.el.stroke({ dasharray })
    if (active.el.cacheStyle) {
      active.el.cacheStyle.dasharray = dasharray
    }
  }
}

/** 删除当前激活的外框 */
function removeOuterFrame() {
  const inst = mindMapInstance
  if (!inst || !inst.outerFrame) return
  ;(inst.outerFrame as any).removeActiveOuterFrame()
  outerFramePanelVisible.value = false
}

/** 双击外框标题：显示浮动输入框进行编辑 */
function handleOuterFrameTitleDblClick(e: MouseEvent) {
  if (readonly.value) return
  const target = e.target as HTMLElement
  // 判断是否点击了外框文字元素（simple-mind-map 外框标题的 class 包含 outer-frame-text 或类似）
  const textEl = target.closest('[class*="outer-frame-text"], [class*="outer_frame_text"], text') as SVGTextElement | null
  if (!textEl) return

  // 确认外框处于激活状态（样式面板已打开即表示激活）
  const inst = mindMapInstance
  if (!inst?.outerFrame || !outerFramePanelVisible.value) return

  // 获取文字元素在画布容器中的位置
  const canvasRect = mindMapRef.value?.getBoundingClientRect()
  const textRect = textEl.getBoundingClientRect()
  if (!canvasRect) return

  const left = textRect.left - canvasRect.left
  const top = textRect.top - canvasRect.top
  const width = textRect.width
  const height = textRect.height

  // 直接从当前面板配置读取（激活时已同步）
  const config = outerFrameConfig.value
  const fontSize = config.textFontSize ?? 14
  const textColor = config.textColor ?? '#333'
  const currentText = config.text ?? ''

  // 设置编辑器样式和位置
  outerFrameTitleEditorStyle.value = {
    left: `${left}px`,
    top: `${top}px`,
    fontSize: `${fontSize}px`,
    color: textColor
  }
  outerFrameTitleEditorValue.value = currentText
  outerFrameTitleEditorVisible.value = true

  // 聚焦并选中全部文字
  nextTick(() => {
    const input = outerFrameTitleEditorInputRef.value
    if (input) {
      input.focus()
      input.select()
      // 设置输入框宽度略大于文字宽度
      input.style.width = `${Math.max(width + 20, 80)}px`
      input.style.height = `${height + 4}px`
    }
  })
}

/** 确认外框标题编辑 */
function confirmOuterFrameTitleEdit() {
  const newText = outerFrameTitleEditorValue.value.trim()
  const inst = mindMapInstance
  if (inst?.outerFrame) {
    ;(inst.outerFrame as any).updateActiveOuterFrame({ text: newText })
    // 同步更新本地配置
    outerFrameConfig.value.text = newText
  }
  outerFrameTitleEditorVisible.value = false
}

/** 取消外框标题编辑 */
function cancelOuterFrameTitleEdit() {
  outerFrameTitleEditorVisible.value = false
}

/** 备注面板内容变化：写入 simple-mind-map 节点 data.note，触发 data_change_detail 增量同步到后端 */
function handleNoteChange(note: string) {
  if (!mindMapInstance || !selectedNodeId.value) return
  const root = mindMapInstance.renderer?.root
  if (!root) return
  const uid = selectedNodeId.value
  const walk = (node: any): boolean => {
    if (node.getData?.('uid') === uid) {
      // 写入 data.note；空串转为 undefined 以清空备注
      node.setData({ note: note || undefined })
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

  // 同步更新 nodesStore 中的 note（供 NodeToolbar has-note 标记和 NotePanel 重新打开时回显）
  const storeNode = nodesStore.findNode(uid)
  if (storeNode) {
    storeNode.note = note || null
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

/** ============ 层级菜单：全部展开 / 全部折叠 / 收起到第 N 级 ============ */
const levelMenuBusy = ref(false)

/** 「收起到第 N 级」的菜单项按当前导图实际深度生成（根节点为第 1 级），
 *  最深到 7 级截断，避免菜单过长；深度不足时不显示无效层级 */
const levelDropdownOptions = computed<DropdownOption[]>(() => {
  const opts: DropdownOption[] = [
    { label: '全部展开', key: 'expand-all' },
    { type: 'divider', key: 'divider-1' },
    { label: '全部折叠', key: 'collapse-all' },
    { type: 'divider', key: 'divider-2' }
  ]
  let maxDepth = 1
  const walk = (list: NodeTreeNodeDto[], depth: number) => {
    for (const n of list) {
      if (depth > maxDepth) maxDepth = depth
      if (n.children?.length) walk(n.children, depth + 1)
    }
  }
  if (nodesStore.tree?.length) walk(nodesStore.tree, 1)
  const maxLevel = Math.min(maxDepth, 7)
  for (let lv = 2; lv <= maxLevel; lv++) {
    opts.push({ label: `收起到第 ${lv} 级`, key: String(lv) })
  }
  return opts
})

async function handleLevelSelect(key: string | number) {
  if (isOfflineData.value || levelMenuBusy.value) return
  levelMenuBusy.value = true
  try {
    if (key === 'expand-all') {
      await applyExpandCollapse('expand-all')
    } else if (key === 'collapse-all') {
      await applyExpandCollapse('collapse-all')
    } else {
      // 菜单的「第 N 级」以根节点为第 1 级；库内 UNEXPAND_TO_LEVEL 的
      // layerIndex 从根节点 0 起算，因此传 N - 1
      await applyExpandCollapse(Number(key) - 1)
    }
  } catch (e) {
    message.error((e as Error).message || '操作失败')
  } finally {
    levelMenuBusy.value = false
  }
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
  // 切换主题 = 放弃模板（模板优先级高于主题）：先清模板缓存，再按新色相 + 当前明暗应用
  templateConfigCache.value = null
  applyResolvedTheme(false, key)
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
    templateConfigCache.value = null
    applyResolvedTheme()
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
  // 套用模板：拉取详情 → 缓存 configJson → 应用（明暗由唯一出口派生）
  try {
    const tpl = await fetchTemplate(key)
    let cfg: MindMapThemeConfig
    try {
      cfg = JSON.parse(tpl.configJson) as MindMapThemeConfig
    } catch {
      message.error('模板样式解析失败')
      return
    }
    templateConfigCache.value = cfg
    applyResolvedTheme()
    if (!readonly.value) {
      await updateMindMap(mindMapId.value, { templateId: key })
      mapDetail.value.templateId = key
    }
  } catch (e) {
    message.error('模板切换失败：' + (e as Error).message)
  }
}

async function handleBack() {
  // 返回前 flush 所有 pending 的防抖修改并等待 API 完成
  try {
    await waitForPendingOps()
  } catch { /* ignore */ }
  router.push({ name: 'home' })
}

// —— 同步状态 UI 计算 ——
const syncStatusText = computed(() => {
  switch (syncStatus.value) {
    case 'syncing': return pendingCount.value > 0 ? `同步中(${pendingCount.value})` : '同步中…'
    case 'saved': return '已保存'
    case 'error': return errorCount.value > 0 ? `同步失败(${errorCount.value})` : '同步失败'
    default: return '已保存'
  }
})

const syncStatusTooltip = computed(() => {
  switch (syncStatus.value) {
    case 'syncing': return `正在同步 ${pendingCount.value} 个操作到服务器…`
    case 'saved': return lastSavedAt.value ? `最后保存：${formatTime(lastSavedAt.value)}` : '更改已保存'
    case 'error': return `同步失败${errorCount.value > 0 ? `（${errorCount.value} 个错误）` : ''}，点击重试`
    default: return lastSavedAt.value ? `最后保存：${formatTime(lastSavedAt.value)}` : '所有更改已保存'
  }
})

const lastSavedAtFormatted = computed(() => {
  if (!lastSavedAt.value) return ''
  const now = new Date()
  const diff = now.getTime() - lastSavedAt.value.getTime()
  if (diff < 60000) return '刚刚'
  if (diff < 3600000) return `${Math.floor(diff / 60000)}分钟前`
  return lastSavedAt.value.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', timeZone: 'Asia/Shanghai' })
})

function formatTime(d: Date): string {
  return d.toLocaleString('zh-CN', { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit', timeZone: 'Asia/Shanghai' })
}

/** 手动保存：先重放失败操作，再等待所有 pending 完成 */
async function handleManualSave() {
  clearError()
  await retryFailedOps()
  await waitForPendingOps()
  if (errorCount.value > 0) {
    message.error('仍有操作同步失败，请检查网络后重试')
  } else {
    message.success('已保存')
  }
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
  return true
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
  // 补齐全局离线状态（lastSync），供顶栏「离线数据 · 最后同步时间」展示
  getOfflineStatus()

  try {
    // 加载导图详情（网络优先，失败时读 IndexedDB 离线快照）
    mapDetail.value = await withFallback(
      () => fetchMindMap(mindMapId.value),
      async () => (await offlineDb.getMap(mindMapId.value))?.detail ?? null,
      {
        onSuccess: (detail) => {
          isOfflineDetail.value = false
          // 增量回写：在线打开单图也保持快照新鲜
          offlineDb.updateMapDetail(mindMapId.value, detail).catch(() => { /* ignore */ })
        },
        onFallback: () => {
          isOfflineDetail.value = true
        }
      }
    )

    // 加载节点（nodesStore.load 内部同样带离线兜底）
    await nodesStore.load(mindMapId.value)

    // 自动创建根节点（如果为空）。离线快照下跳过：写操作会失败，保持只读展示
    if (nodesStore.nodes.length === 0 && !isOfflineData.value) {
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

      // 监听容器尺寸变动（窗口缩放、横竖屏切换、工具栏折叠等）自动自适应
      if (mindMapRef.value && typeof ResizeObserver !== 'undefined') {
        containerResizeObserver?.disconnect()
        containerResizeObserver = new ResizeObserver(() => {
          mindMapInstance?.resize()
        })
        containerResizeObserver.observe(mindMapRef.value)
      }
    })
  })
})

onUnmounted(() => {
  // 组件卸载前 flush 所有 pending 防抖修改（不等待完成，尽最大努力保存）
  flushPendingUpdates()
  containerResizeObserver?.disconnect()
  containerResizeObserver = null
  mindMapInstance?.destroy()
  mindMapInstance = null
  nodesStore.reset()
})

/** 路由离开前 flush + 等待所有 pending 操作完成，防止数据丢失 */
onBeforeRouteLeave(async (_to, _from, next) => {
  if (readonly.value) {
    next()
    return
  }
  try {
    await waitForPendingOps()
  } catch { /* ignore */ }
  next()
})

/** 页面刷新/关闭前 flush 所有 pending 防抖修改；
 *  若仍有未同步完成的写入，弹出浏览器原生确认，避免误刷新导致数据丢失 */
function handleBeforeUnload(e: BeforeUnloadEvent) {
  flushPendingUpdates()
  if (hasPendingWriteOps()) {
    e.preventDefault()
    e.returnValue = ''
  }
}

onMounted(() => {
  window.addEventListener('beforeunload', handleBeforeUnload)
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
  window.removeEventListener('beforeunload', handleBeforeUnload)
})

watch(() => route.params.id, () => {
  nodesStore.reset()
})
</script>

<template>
  <div class="editor-container" :class="{ 'is-zen-mode': isMobileHeaderCollapsed }">
    <!-- 顶部工具栏 -->
    <header class="editor-header" :class="{ 'is-collapsed': isMobileHeaderCollapsed }">
      <!-- 左侧：返回与文档元信息内联编辑区 -->
      <div class="header-left-zone">
        <button class="btn-back" @click="handleBack" title="返回列表">
          <span class="icon">←</span>
          <span class="text">返回</span>
        </button>

        <span class="header-v-divider"></span>

        <div class="doc-meta-box" v-if="mapDetail">
          <div class="doc-title-row">
            <input v-if="!readonly" v-model="mapDetail.title" class="inline-title-input" placeholder="未命名思维导图"
              @blur="handleTitleBlur" />
            <span v-else class="inline-title-text">{{ mapDetail.title }}</span>
            <span
              v-if="!readonly"
              class="save-status-badge"
              :class="'status-' + syncStatus"
              :title="syncStatusTooltip"
              @click="syncStatus === 'error' ? handleManualSave() : undefined"
            >
              <span class="status-dot" :class="'dot-' + syncStatus"></span>
              <span class="status-text">{{ syncStatusText }}</span>
              <span v-if="lastSavedAtFormatted" class="status-time">{{ lastSavedAtFormatted }}</span>
            </span>
          </div>
          <div class="doc-desc-row">
            <input v-if="!readonly" v-model="mapDetail.description" class="inline-desc-input" placeholder="添加描述或备注..."
              @blur="handleDescriptionBlur" />
            <span v-else-if="mapDetail.description" class="inline-desc-text">{{ mapDetail.description }}</span>
          </div>
          <div v-if="isOfflineData" class="offline-data-badge" title="网络不可用，正在展示本地快照（只读）">
            <span class="offline-badge-icon">⚡</span>
            离线数据<span v-if="offlineLastSyncText"> · 最后同步 {{ offlineLastSyncText }}</span>
          </div>
        </div>
      </div>

      <!-- 移动端收起/展开快捷按钮 -->
      <button class="btn-mobile-zen-toggle" @click="toggleMobileHeader"
        :title="isMobileHeaderCollapsed ? '展开顶栏' : '收起顶栏 (沉浸模式)'">
        <span class="zen-icon">{{ isMobileHeaderCollapsed ? '▾' : '▴' }}</span>
        <span class="zen-text">{{ isMobileHeaderCollapsed ? '展开' : '沉浸' }}</span>
      </button>

      <!-- 中间与右侧：现代工具胶囊组与动作按钮 -->
      <div class="editor-actions">
        <!-- 历史与编辑胶囊组 (桌面端) -->
        <template v-if="!readonly">
          <div class="btn-group-pill">
            <button class="btn-tool-pill" :disabled="!nodesStore.canUndo" @click="handleUndo" title="撤销 (Ctrl+Z)">
              <span class="pill-icon">↶</span>
            </button>
            <button class="btn-tool-pill" :disabled="!nodesStore.canRedo" @click="handleRedo" title="重做 (Ctrl+Y)">
              <span class="pill-icon">↷</span>
            </button>
            <span class="group-divider"></span>
            <button class="btn-tool-pill" :disabled="!selectedNodeId" @click="handleCopy" title="复制选中节点 (Ctrl+C)">
              <span class="pill-icon">⧉</span>
            </button>
            <button class="btn-tool-pill" :disabled="!clipboardNode" @click="handlePaste" title="粘贴节点 (Ctrl+V)">
              <span class="pill-icon">📋</span>
            </button>
            <button class="btn-tool-pill" :disabled="!selectedNodeId" @click="openContentEditor" title="编辑节点富文本内容">
              <span class="pill-icon">📝</span>
            </button>
          </div>
        </template>

        <!-- 手动保存按钮 (仅编辑模式) -->
        <button
          v-if="!readonly"
          class="btn-tool-pill btn-save-sync"
          :class="{ 'is-syncing': syncStatus === 'syncing' }"
          :disabled="syncStatus === 'syncing' && pendingCount === 0"
          @click="handleManualSave"
          :title="syncStatus === 'syncing' ? '同步中…' : '手动保存 (立即同步到服务器)'"
        >
          <span class="pill-icon">{{ syncStatus === 'syncing' ? '⏳' : '💾' }}</span>
        </button>

        <!-- 视图缩放胶囊组 -->
        <div class="btn-group-pill zoom-group">
          <button class="btn-tool-pill" @click="handleZoomIn" title="放大画布 (Ctrl + +)">
            <span class="pill-icon">+</span>
          </button>
          <button class="btn-tool-pill" @click="handleZoomOut" title="缩小画布 (Ctrl + -)">
            <span class="pill-icon">−</span>
          </button>
          <button class="btn-tool-pill" @click="handleReset" title="自适应居中视图">
            <span class="pill-icon">⟲</span>
          </button>
        </div>

        <!-- 层级胶囊组：全部展开 / 全部折叠 / 收起到第 N 级 -->
        <div class="btn-group-pill">
          <NDropdown trigger="click" :options="levelDropdownOptions" @select="handleLevelSelect">
            <button class="btn-tool-pill" :disabled="isOfflineData || levelMenuBusy"
              title="层级：全部展开 / 全部折叠 / 收起到指定层级">
              <span class="pill-icon">≡</span>
            </button>
          </NDropdown>
        </div>

        <span class="header-v-divider"></span>

        <!-- 主题与模板定制 -->
        <div class="action-btn-group">
          <NDropdown trigger="click" :options="templateDropdownOptions" :value="currentTemplateId ?? '__none__'"
            @select="handleTemplateSelect">
            <button class="btn-action-ghost" :class="{ 'is-active': !!currentTemplateId }" title="切换模版结构">
              <span class="btn-icon">📐</span>
              <span class="btn-label">{{
                currentTemplateId
                  ? (templatesStore.enabledList.find(t => t.id === currentTemplateId)?.name ?? '模板')
                  : '模板'
              }}</span>
            </button>
          </NDropdown>
          <NDropdown trigger="click" :options="themeDropdownOptions" :value="currentThemeId"
            @select="handleThemeSelect">
            <button class="btn-action-ghost" :class="{ 'is-dimmed': !!currentTemplateId }" title="切换配色主题">
              <span class="theme-swatch"
                :style="{ background: getThemeSwatch(currentThemeId, themeStore.isDark).rootFill }"></span>
              <span class="btn-label">{{getThemePreset(currentThemeId).name}}</span>
            </button>
          </NDropdown>
        </div>

        <!-- 协同、历史与保存 -->
        <div class="action-btn-group">
          <button class="btn-action-ghost" :disabled="isOfflineData" @click="versionsDrawerVisible = true"
            :title="isOfflineData ? '离线模式暂不可用' : '查看版本历史'">
            <span class="btn-icon">🕘</span><span class="btn-label">历史</span>
          </button>
          <button class="btn-action-ghost" v-if="!readonly" :disabled="isOfflineData"
            @click="versionDrawerRef?.openCreateVersion()" :title="isOfflineData ? '离线模式暂不可用' : '保存当前快照'">
            <span class="btn-icon">💾</span><span class="btn-label">快照</span>
          </button>
          <button class="btn-action-ghost" :disabled="isOfflineData" @click="shareDrawerVisible = true"
            :title="isOfflineData ? '离线模式暂不可用' : '分享与协作'">
            <span class="btn-icon">🔗</span><span class="btn-label">分享</span>
          </button>
        </div>

        <!-- 核心导出主操作 -->
        <NDropdown trigger="click" :options="exportOptions" @select="handleExport">
          <button class="btn-action-primary" :class="{ 'is-loading': exporting }" title="导出导图文件或图片"
            :disabled="exporting">
            <span class="btn-icon">{{ exporting ? '⏳' : '📤' }}</span>
            <span class="btn-label">{{ exporting ? '导出中...' : '导出' }}</span>
            <span class="dropdown-arrow">▾</span>
          </button>
        </NDropdown>
      </div>
    </header>

    <!-- 画布区域 -->
    <main class="editor-main">
      <!-- 移动端沉浸模式顶部控制栏（收起胶囊 + 搜索框同排无遮挡布局） -->
      <div class="mobile-zen-topbar" :class="{ 'is-collapsed': !isMobileHeaderCollapsed }">
        <transition name="zen-fade">
          <div v-if="isMobileHeaderCollapsed" class="mobile-zen-pill" @click="toggleMobileHeader" title="点击展开导航与操作栏">
            <button class="zen-pill-back" @click.stop="handleBack" title="返回" aria-label="返回上一页">
              ←
            </button>
            <span class="zen-pill-title">{{ mapDetail?.title || '思维导图' }}</span>
            <span
              v-if="!readonly"
              class="zen-pill-sync"
              :class="'sync-' + syncStatus"
              :title="syncStatusText + (lastSavedAtFormatted ? ' · ' + lastSavedAtFormatted : '')"
            ></span>
            <button class="zen-pill-expand" @click.stop="toggleMobileHeader" title="展开顶栏" aria-label="展开顶栏">
              ▾
            </button>
          </div>
        </transition>

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
      </div>
      <div ref="mindMapRef" class="mindmap-canvas"></div>
      <NodeNoteTooltip ref="noteTooltipRef" />
      <div v-if="loading" class="loading-wrap">
        <div class="spinner"></div>
        <p>加载中...</p>
      </div>

      <!-- 浮动工具栏（挂载在画布主容器内，随主容器定位） -->
      <NodeToolbar v-if="showToolbar && selectedNodeId" :node="nodesStore.findNode(selectedNodeId)"
        :active-node-count="activeNodeCount"
        @add-child="handleAddChild" @add-sibling="handleAddSibling" @delete="handleDelete" @update="handleUpdateStyle"
        @copy="handleCopy" @paste="handlePaste" @open-note="openNotePanel" @create-line="createAssociativeLine"
        @add-generalization="addGeneralization" @add-outer-frame="addOuterFrame" />

      <!-- 外框样式面板：选中外框激活时显示 -->
      <OuterFrameStylePanel :visible="outerFramePanelVisible" :config="outerFrameConfig"
        @update="updateOuterFrameStyle" @remove="removeOuterFrame" @close="outerFramePanelVisible = false" />

      <!-- 外框标题浮动编辑器：双击标题时出现 -->
      <input v-if="outerFrameTitleEditorVisible" ref="outerFrameTitleEditorInputRef"
        v-model="outerFrameTitleEditorValue"
        class="outer-frame-title-editor"
        :style="outerFrameTitleEditorStyle"
        @blur="confirmOuterFrameTitleEdit"
        @keydown.enter="confirmOuterFrameTitleEdit"
        @keydown.esc="cancelOuterFrameTitleEdit"
        @click.stop
        @dblclick.stop />
    </main>

    <!-- 版本历史抽屉（含新建版本弹窗） -->
    <VersionDrawer ref="versionDrawerRef" v-model:show="versionsDrawerVisible" :mind-map-id="mindMapId"
      :node-count="nodesStore.nodes.length" @rollback="handleVersionRollback" />

    <!-- 分享抽屉（含新建分享弹窗） -->
    <ShareDrawer v-model:show="shareDrawerVisible" :mind-map-id="mindMapId"
      :is-public-default="mapDetail?.isPublic ?? false" @public-change="handleSharePublicChange" />

    <!-- 富文本节点内容编辑弹窗 -->
    <NodeContentModal v-model:show="contentModalVisible" :node="selectedNodeForContent" @save="handleContentSave" />

    <!-- 节点备注面板（富文本抽屉） -->
    <NotePanel v-model:show="notePanelVisible" :node="selectedNodeForContent" :readonly="readonly"
      @change="handleNoteChange" />

    <!-- 根节点不能删除提示 -->
    <NModal v-model:show="rootDeleteTipVisible" preset="card" title="无法删除" style="max-width: 420px" :bordered="false"
      size="medium">
      <div style="font-size: 14px; color: #475569; line-height: 1.6;">
        根节点不能删除。你可以清空内容但不能删除中心主题。
      </div>
      <template #footer>
        <div style="display: flex; justify-content: flex-end;">
          <NButton type="primary" size="small" @click="handleRootDeleteTipClose">
            我知道了
          </NButton>
        </div>
      </template>
    </NModal>

    <!-- 删除节点确认 -->
    <NModal v-model:show="nodeDeleteConfirmVisible" preset="card" title="确认删除" style="max-width: 420px"
      :bordered="false" size="medium">
      <div style="font-size: 14px; color: #334155; line-height: 1.6;">
        删除「<b>{{ nodeDeleteTargetTitle }}</b>」及其所有子节点？
      </div>
      <template #footer>
        <div style="display: flex; justify-content: flex-end; gap: 10px;">
          <NButton size="small" @click="nodeDeleteConfirmVisible = false">
            取消
          </NButton>
          <NButton type="error" size="small" :loading="nodeDeleteSubmitting" @click="submitNodeDelete">
            删除
          </NButton>
        </div>
      </template>
    </NModal>
  </div>
</template>

<style scoped lang="scss" src="./MindMapEditorView.scss"></style>
