<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue'

interface Props {
  /** 左滑后露出的操作按钮宽度总和，单位 px，默认 160 */
  actionWidth?: number
  /** 是否禁用滑动 */
  disabled?: boolean
  /** 滑动阈值（超过此距离自动展开/收起），默认 40 */
  threshold?: number
}

const props = withDefaults(defineProps<Props>(), {
  actionWidth: 160,
  disabled: false,
  threshold: 40
})

const emit = defineEmits<{
  (e: 'open'): void
  (e: 'close'): void
}>()

const translateX = ref(0)
const startX = ref(0)
const startY = ref(0)
const isDragging = ref(false)
const isOpen = ref(false)
const directionLocked = ref<'h' | 'v' | null>(null) // 方向锁定，避免垂直滚动时也触发横向滑动

const contentStyle = computed(() => ({
  transform: `translateX(${-translateX.value}px)`,
  transition: isDragging.value ? 'none' : 'transform 0.25s ease-out'
}))

let touchStartTime = 0

function onTouchStart(e: TouchEvent) {
  if (props.disabled) return
  const touch = e.touches[0]
  startX.value = touch.clientX
  startY.value = touch.clientY
  isDragging.value = true
  directionLocked.value = null
  touchStartTime = Date.now()

  // 如果已经展开，从展开位置开始计算
  if (isOpen.value) {
    startX.value += props.actionWidth
  }
}

function onTouchMove(e: TouchEvent) {
  if (!isDragging.value || props.disabled) return
  const touch = e.touches[0]
  const deltaX = startX.value - touch.clientX
  const deltaY = Math.abs(startY.value - touch.clientY)

  // 方向锁定：垂直滑动量大于水平滑动量时，认为是滚动，不拦截
  if (directionLocked.value === null) {
    if (deltaY > Math.abs(deltaX) && deltaY > 5) {
      directionLocked.value = 'v'
      isDragging.value = false
      return
    }
    if (Math.abs(deltaX) > 5) {
      directionLocked.value = 'h'
    }
  }

  if (directionLocked.value !== 'h') return

  // 阻止默认滚动行为
  if (e.cancelable) {
    e.preventDefault()
  }

  let newTranslate = deltaX
  // 限制范围：不能向右滑（收起方向）超过 0，向左滑（展开方向）不超过 actionWidth * 1.2（有阻尼）
  if (newTranslate < 0) {
    newTranslate = 0
  } else if (newTranslate > props.actionWidth) {
    // 超过部分有阻尼效果
    const over = newTranslate - props.actionWidth
    newTranslate = props.actionWidth + over * 0.3
  }

  translateX.value = newTranslate
}

function onTouchEnd() {
  if (!isDragging.value || directionLocked.value !== 'h') {
    isDragging.value = false
    return
  }
  isDragging.value = false

  const deltaTime = Date.now() - touchStartTime
  const velocity = translateX.value / deltaTime // px / ms

  // 判断是否展开：滑动距离超过阈值 或 快速滑动
  const shouldOpen =
    translateX.value > props.threshold || (velocity > 0.3 && translateX.value > 20)

  if (shouldOpen) {
    translateX.value = props.actionWidth
    isOpen.value = true
    emit('open')
  } else {
    translateX.value = 0
    isOpen.value = false
    emit('close')
  }
}

/** 外部调用：关闭左滑 */
function close() {
  translateX.value = 0
  isOpen.value = false
}

/** 外部调用：打开左滑 */
function open() {
  translateX.value = props.actionWidth
  isOpen.value = true
}

defineExpose({ close, open, isOpen })

// 点击其他地方时关闭（可选）
function handleContentClick() {
  if (isOpen.value) {
    close()
  }
}

// 防止组件卸载时状态残留
onBeforeUnmount(() => {
  isDragging.value = false
})
</script>

<template>
  <div class="swipe-card-wrapper">
    <!-- 操作按钮层（在内容下方） -->
    <div class="swipe-actions" :style="{ width: actionWidth + 'px' }">
      <slot name="actions" />
    </div>
    <!-- 内容层 -->
    <div
      class="swipe-content"
      :style="contentStyle"
      @touchstart.passive="onTouchStart"
      @touchmove.passive="onTouchMove"
      @touchend="onTouchEnd"
      @click="handleContentClick"
    >
      <slot />
    </div>
  </div>
</template>

<style scoped lang="scss">
.swipe-card-wrapper {
  position: relative;
  overflow: hidden;
  width: 100%;
}

.swipe-actions {
  position: absolute;
  top: 0;
  right: 0;
  bottom: 0;
  display: flex;
  align-items: stretch;
  z-index: 0;
}

.swipe-content {
  position: relative;
  z-index: 1;
  background: var(--app-card-bg);
  touch-action: pan-y; // 允许垂直滚动
  will-change: transform;
  -webkit-user-select: none;
  user-select: none;
}
</style>
