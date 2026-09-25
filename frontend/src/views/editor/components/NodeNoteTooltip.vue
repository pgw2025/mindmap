<script setup lang="ts">
import { ref, onBeforeUnmount, onMounted } from 'vue'

const visible = ref(false)
const content = ref('')
const left = ref(0)
const top = ref(0)
const tooltipRef = ref<HTMLDivElement | null>(null)

/** 关闭延迟定时器 */
let hideTimer: ReturnType<typeof setTimeout> | null = null
/** 关闭延迟时间（毫秒），给用户足够时间将鼠标移入气泡 */
const HIDE_DELAY = 300

/** 是否为触摸设备 */
const isTouchDevice = ref(false)
/** 触摸模式下用户主动点击显示的标记，用于忽略合成 mouseout 导致的 hide */
let touchModeActive = false
/** 记录上一次显示的备注内容，用于判断是否点击同一个图标（toggle） */
let lastNoteContent = ''
/** 记录上一次显示的位置，辅助判断是否同一个图标 */
let lastNotePos = { x: 0, y: 0 }

/** 清除待执行的关闭定时器 */
function clearHideTimer() {
  if (hideTimer) {
    clearTimeout(hideTimer)
    hideTimer = null
  }
}

/**
 * 判断两次 show 调用是否指向同一个备注图标
 * 通过内容和位置综合判断
 */
function isSameNote(note: string, x: number, y: number): boolean {
  return note === lastNoteContent && Math.abs(x - lastNotePos.x) < 1 && Math.abs(y - lastNotePos.y) < 1
}

/** 直接显示（内部方法） */
function doShow(note: string, x: number, y: number) {
  clearHideTimer()
  content.value = note || ''
  left.value = x + 8
  top.value = y + 8
  visible.value = content.value.length > 0
  lastNoteContent = note
  lastNotePos = { x, y }
}

/** 直接隐藏（内部方法） */
function doHide() {
  clearHideTimer()
  visible.value = false
  touchModeActive = false
}

/** 展示备注 tooltip（simple-mind-map customNoteContentShow.show 回调） */
function show(note: string, x: number, y: number) {
  // 触摸设备：点击切换模式
  if (isTouchDevice.value) {
    // 如果已经显示，且点击的是同一个图标 → toggle 关闭
    if (visible.value && isSameNote(note, x, y)) {
      doHide()
      return
    }
    // 否则显示，并标记为触摸模式激活，忽略后续的合成 hide
    doShow(note, x, y)
    touchModeActive = true
    return
  }

  // 桌面端：正常显示
  doShow(note, x, y)
}

/** 隐藏备注 tooltip（simple-mind-map customNoteContentShow.hide 回调） */
function hide() {
  // 触摸模式下：忽略合成 mouseout 导致的 hide
  if (isTouchDevice.value && touchModeActive) {
    return
  }

  // 桌面端：延迟关闭，给用户足够时间将鼠标移入气泡
  clearHideTimer()
  hideTimer = setTimeout(() => {
    visible.value = false
  }, HIDE_DELAY)
}

/** 鼠标进入气泡时，取消关闭，保持显示 */
function handleMouseEnter() {
  clearHideTimer()
}

/** 鼠标离开气泡时，启动延迟关闭 */
function handleMouseLeave() {
  hide()
}

/**
 * 阻止触摸事件冒泡，防止画布拦截滚动
 * 手机端手指在气泡内滑动时，应该滚动气泡内容，而不是拖拽画布
 */
function stopTouchPropagation(e: TouchEvent) {
  e.stopPropagation()
}

/**
 * 点击气泡外部时关闭备注（移动端）
 */
function handleClickOutside(e: TouchEvent) {
  if (!visible.value || !tooltipRef.value) return
  // 点击目标不在气泡内，则关闭
  if (!tooltipRef.value.contains(e.target as Node)) {
    // 延迟关闭：点按其他备注图标时，紧跟的合成 mouseover 会切换内容并取消本定时器，避免闪烁
    clearHideTimer()
    hideTimer = setTimeout(() => {
      doHide()
    }, 50)
  }
}

onMounted(() => {
  // 检测是否为触摸设备
  isTouchDevice.value = 'ontouchstart' in window || navigator.maxTouchPoints > 0
  if (isTouchDevice.value) {
    // 移动端：只监听 touchstart，点击气泡外关闭备注。
    // 不能监听 mousedown：点按图标后浏览器会合成 mouseover → mousedown，
    // mousedown 会把 mouseover 刚打开的气泡误判为"点击外部"立即关闭（闪现消失）
    document.addEventListener('touchstart', handleClickOutside, true)
  }
})

onBeforeUnmount(() => {
  clearHideTimer()
  document.removeEventListener('touchstart', handleClickOutside, true)
})

defineExpose({ show, hide })
</script>

<template>
  <Teleport to="body">
    <div
      v-if="visible"
      ref="tooltipRef"
      class="node-note-tooltip"
      :class="{ 'is-touch': isTouchDevice }"
      :style="{ left: left + 'px', top: top + 'px' }"
      @mouseenter="handleMouseEnter"
      @mouseleave="handleMouseLeave"
      @touchstart="stopTouchPropagation"
      @touchmove="stopTouchPropagation"
      @touchend="stopTouchPropagation"
      v-html="content"
    ></div>
  </Teleport>
</template>

<style scoped lang="scss">
.node-note-tooltip {
  position: fixed;
  z-index: 3000;
  max-width: 320px;
  max-height: 240px;
  overflow: auto;
  padding: 10px 12px;
  background: #fff;
  border: 1px solid rgba(0, 0, 0, 0.12);
  border-radius: 6px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.15);
  font-size: 14px;
  line-height: 1.6;
  color: #333;
  word-break: break-word;
  /* iOS 惯性滚动 */
  -webkit-overflow-scrolling: touch;

  :deep(p) {
    margin: 0 0 8px;
  }

  :deep(p:last-child) {
    margin-bottom: 0;
  }

  :deep(ul),
  :deep(ol) {
    padding-left: 24px;
    margin: 0 0 8px;
  }

  :deep(blockquote) {
    padding-left: 12px;
    border-left: 3px solid #d0d0d6;
    color: #666;
    margin: 0 0 8px;
  }

  :deep(a) {
    color: #18a058;
    text-decoration: underline;
  }

  :deep(.note-empty) {
    color: #aaa;
    font-style: italic;
  }

  /* 触摸设备下的样式调整 */
  &.is-touch {
    /* 更大的内边距，方便手指触摸滚动 */
    padding: 12px 14px;
    font-size: 15px;
    max-width: 85vw;
    max-height: 50vh;
  }
}
</style>
