<script setup lang="ts">
import { ref, onBeforeUnmount, onMounted } from 'vue'

const visible = ref(false)
const url = ref('')
const title = ref('')
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
 * 依据 Pointer Events 的 pointerType 判定真实输入源（与备注 tooltip 同一套约定）。
 */
type InputType = 'mouse' | 'touch'
let currentInputType: InputType = 'mouse'

/** 记录上一次显示的图片 url，用于判断是否点击同一个缩略图（toggle） */
let lastImageUrl = ''
let lastImagePos = { x: 0, y: 0 }

/** 清除待执行的关闭定时器 */
function clearHideTimer() {
  if (hideTimer) {
    clearTimeout(hideTimer)
    hideTimer = null
  }
}

/** 判断两次 show 调用是否指向同一个图片缩略图 */
function isSameImage(imgUrl: string, x: number, y: number): boolean {
  return imgUrl === lastImageUrl && Math.abs(x - lastImagePos.x) < 1 && Math.abs(y - lastImagePos.y) < 1
}

/** 直接显示（内部方法） */
function doShow(imgUrl: string, imgTitle: string, x: number, y: number) {
  clearHideTimer()
  url.value = imgUrl || ''
  title.value = imgTitle || ''
  left.value = x + 8
  top.value = y + 8
  visible.value = url.value.length > 0
  lastImageUrl = imgUrl
  lastImagePos = { x, y }
}

/** 直接隐藏（内部方法） */
function doHide() {
  clearHideTimer()
  visible.value = false
}

function handlePointerInput(e: PointerEvent) {
  if (e.pointerType === 'mouse') {
    currentInputType = 'mouse'
  } else if (e.type === 'pointerdown') {
    currentInputType = 'touch'
  }
}

/** 展示图片 tooltip（simple-mind-map node_img_mouseenter 回调） */
function show(imgUrl: string, imgTitle: string, x: number, y: number) {
  if (currentInputType === 'touch') {
    if (visible.value && isSameImage(imgUrl, x, y)) {
      doHide()
      return
    }
    doShow(imgUrl, imgTitle, x, y)
    return
  }
  doShow(imgUrl, imgTitle, x, y)
}

/** 隐藏图片 tooltip（simple-mind-map node_img_mouseleave 回调） */
function hide() {
  if (currentInputType === 'touch') {
    return
  }
  clearHideTimer()
  hideTimer = setTimeout(() => {
    visible.value = false
  }, HIDE_DELAY)
}

function handleMouseEnter() {
  if (currentInputType === 'mouse') {
    clearHideTimer()
  }
}

function handleMouseLeave() {
  if (currentInputType === 'mouse') {
    hide()
  }
}

function stopTouchPropagation(e: TouchEvent) {
  e.stopPropagation()
}

function handleClickOutside(e: PointerEvent) {
  if (!visible.value || !tooltipRef.value) return
  if (!tooltipRef.value.contains(e.target as Node)) {
    clearHideTimer()
    hideTimer = setTimeout(() => {
      doHide()
    }, 50)
  }
}

onMounted(() => {
  isTouchDevice.value = 'ontouchstart' in window || navigator.maxTouchPoints > 0
  document.addEventListener('pointerdown', handlePointerInput)
  document.addEventListener('pointermove', handlePointerInput)
  document.addEventListener('pointerdown', handleClickOutside, true)
})

onBeforeUnmount(() => {
  clearHideTimer()
  document.removeEventListener('pointerdown', handlePointerInput)
  document.removeEventListener('pointermove', handlePointerInput)
  document.removeEventListener('pointerdown', handleClickOutside, true)
})

defineExpose({ show, hide })
</script>

<template>
  <Teleport to="body">
    <div
      v-if="visible"
      ref="tooltipRef"
      class="node-image-tooltip"
      :class="{ 'is-touch': isTouchDevice }"
      :style="{ left: left + 'px', top: top + 'px' }"
      @mouseenter="handleMouseEnter"
      @mouseleave="handleMouseLeave"
      @touchstart="stopTouchPropagation"
      @touchmove="stopTouchPropagation"
      @touchend="stopTouchPropagation"
    >
      <img :src="url" :alt="title || '节点图片'" class="preview-img" />
      <div v-if="title" class="preview-title">{{ title }}</div>
    </div>
  </Teleport>
</template>

<style scoped lang="scss">
.node-image-tooltip {
  position: fixed;
  z-index: 3000;
  max-width: 320px;
  max-height: 280px;
  overflow: hidden;
  background: #fff;
  border: 1px solid rgba(0, 0, 0, 0.12);
  border-radius: 8px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.15);
  padding: 6px;

  .preview-img {
    display: block;
    max-width: 100%;
    max-height: 220px;
    object-fit: contain;
    border-radius: 4px;
  }

  .preview-title {
    margin-top: 6px;
    padding: 0 4px;
    font-size: 12px;
    line-height: 1.4;
    color: #666;
    word-break: break-word;
  }

  &.is-touch {
    max-width: 85vw;
    max-height: 60vh;

    .preview-img {
      max-height: 45vh;
    }
  }
}
</style>
