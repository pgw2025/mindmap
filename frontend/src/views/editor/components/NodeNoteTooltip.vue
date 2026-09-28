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

/** 是否为触摸设备（仅用于样式判断，不影响交互逻辑） */
const isTouchDevice = ref(false)

/**
 * 当前输入方式：'mouse' | 'touch'
 * 动态检测用户当前正在使用的输入设备，而不是根据设备能力静态判断
 * 解决触摸屏电脑上用鼠标操作时备注不消失的问题
 */
type InputType = 'mouse' | 'touch'
let currentInputType: InputType = 'mouse'

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
}

/**
 * 切换为鼠标输入模式
 * 监听到 mousedown 事件时调用
 */
function switchToMouseInput() {
  currentInputType = 'mouse'
}

/**
 * 切换为触摸输入模式
 * 监听到 touchstart 事件时调用
 */
function switchToTouchInput() {
  currentInputType = 'touch'
}

/** 展示备注 tooltip（simple-mind-map customNoteContentShow.show 回调） */
function show(note: string, x: number, y: number) {
  // 触摸模式：点击切换
  if (currentInputType === 'touch') {
    // 如果已经显示，且点击的是同一个图标 → toggle 关闭
    if (visible.value && isSameNote(note, x, y)) {
      doHide()
      return
    }
    // 否则显示
    doShow(note, x, y)
    return
  }

  // 鼠标模式：正常显示
  doShow(note, x, y)
}

/** 隐藏备注 tooltip（simple-mind-map customNoteContentShow.hide 回调） */
function hide() {
  // 触摸模式：忽略合成 mouseout 导致的 hide
  // （触摸后浏览器会合成 mouseover → mouseout，我们不希望气泡自动消失）
  if (currentInputType === 'touch') {
    return
  }

  // 鼠标模式：延迟关闭，给用户足够时间将鼠标移入气泡
  clearHideTimer()
  hideTimer = setTimeout(() => {
    visible.value = false
  }, HIDE_DELAY)
}

/** 鼠标进入气泡时，取消关闭，保持显示 */
function handleMouseEnter() {
  // 只有鼠标模式下才需要处理
  if (currentInputType === 'mouse') {
    clearHideTimer()
  }
}

/** 鼠标离开气泡时，启动延迟关闭 */
function handleMouseLeave() {
  // 只有鼠标模式下才需要处理
  if (currentInputType === 'mouse') {
    hide()
  }
}

/**
 * 阻止触摸事件冒泡，防止画布拦截滚动
 * 手机端手指在气泡内滑动时，应该滚动气泡内容，而不是拖拽画布
 */
function stopTouchPropagation(e: TouchEvent) {
  e.stopPropagation()
}

/**
 * 点击气泡外部时关闭备注（触摸模式下使用）
 * 鼠标模式下由 mouseout 自动处理，这里作为兜底也可以关闭
 */
function handleClickOutside(e: Event) {
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
  // 检测是否为触摸设备（仅用于样式判断）
  isTouchDevice.value = 'ontouchstart' in window || navigator.maxTouchPoints > 0

  // 动态检测当前输入方式
  // 触摸时切换为触摸模式
  document.addEventListener('touchstart', switchToTouchInput, { passive: true })
  // 鼠标按下时切换为鼠标模式
  document.addEventListener('mousedown', switchToMouseInput, { passive: true })

  // 点击外部关闭（触摸模式下的主要关闭方式，鼠标模式下作为兜底）
  // 使用捕获阶段，确保在事件冒泡到画布之前就能检测到
  document.addEventListener('touchstart', handleClickOutside, true)
  document.addEventListener('mousedown', handleClickOutside, true)
})

onBeforeUnmount(() => {
  clearHideTimer()
  document.removeEventListener('touchstart', switchToTouchInput)
  document.removeEventListener('mousedown', switchToMouseInput)
  document.removeEventListener('touchstart', handleClickOutside, true)
  document.removeEventListener('mousedown', handleClickOutside, true)
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
