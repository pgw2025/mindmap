<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue'

interface Props {
  /** 触发刷新的阈值距离，单位 px，默认 60 */
  threshold?: number
  /** 最大下拉距离，单位 px，默认 100 */
  maxDistance?: number
  /** 是否禁用 */
  disabled?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  threshold: 60,
  maxDistance: 100,
  disabled: false
})

const emit = defineEmits<{
  (e: 'refresh'): Promise<void> | void
}>()

const pullDistance = ref(0)
const isRefreshing = ref(false)
const startY = ref(0)
const isPulling = ref(false)

const containerStyle = computed(() => ({
  transform: `translateY(${pullDistance.value}px)`,
  transition: isPulling.value ? 'none' : 'transform 0.3s ease-out'
}))

const indicatorStyle = computed(() => {
  const progress = Math.min(pullDistance.value / props.threshold, 1)
  return {
    opacity: progress,
    transform: `scale(${0.6 + progress * 0.4}) rotate(${pullDistance.value * 3}deg)`
  }
})

const indicatorText = computed(() => {
  if (isRefreshing.value) return '刷新中...'
  if (pullDistance.value >= props.threshold) return '释放刷新'
  return '下拉刷新'
})

let scrollTop = 0

function onTouchStart(e: TouchEvent) {
  if (props.disabled || isRefreshing.value) return
  // 只有在滚动到顶部时才允许下拉刷新
  scrollTop = window.scrollY || document.documentElement.scrollTop
  if (scrollTop > 0) return

  startY.value = e.touches[0].clientY
  isPulling.value = true
}

function onTouchMove(e: TouchEvent) {
  if (!isPulling.value || props.disabled || isRefreshing.value) return

  const deltaY = e.touches[0].clientY - startY.value
  if (deltaY <= 0) {
    pullDistance.value = 0
    return
  }

  // 阻尼效果
  let distance = deltaY
  if (distance > props.maxDistance) {
    const over = distance - props.maxDistance
    distance = props.maxDistance + over * 0.3
  }

  pullDistance.value = distance
}

async function onTouchEnd() {
  if (!isPulling.value) return
  isPulling.value = false

  if (pullDistance.value >= props.threshold && !isRefreshing.value) {
    // 触发刷新
    isRefreshing.value = true
    pullDistance.value = 50 // 保持在刷新指示器高度

    try {
      await emit('refresh')
    } finally {
      // 延迟一点再收起，让用户看到完成状态
      setTimeout(() => {
        isRefreshing.value = false
        pullDistance.value = 0
      }, 300)
    }
  } else {
    pullDistance.value = 0
  }
}

onBeforeUnmount(() => {
  isPulling.value = false
  isRefreshing.value = false
})
</script>

<template>
  <div class="pull-refresh-wrapper">
    <!-- 下拉刷新指示器 -->
    <div class="pull-refresh-indicator" :style="{ top: `${-50 + pullDistance * 0.5}px` }">
      <div class="indicator-content" :style="indicatorStyle">
        <svg v-if="!isRefreshing" class="indicator-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <polyline points="23 4 23 10 17 10"/>
          <path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"/>
        </svg>
        <svg v-else class="indicator-icon spinning" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <path d="M21 12a9 9 0 1 1-6.219-8.56"/>
        </svg>
        <span class="indicator-text">{{ indicatorText }}</span>
      </div>
    </div>
    <!-- 内容区 -->
    <div
      class="pull-refresh-content"
      :style="containerStyle"
      @touchstart.passive="onTouchStart"
      @touchmove.passive="onTouchMove"
      @touchend="onTouchEnd"
    >
      <slot />
    </div>
  </div>
</template>

<style scoped lang="scss">
.pull-refresh-wrapper {
  position: relative;
  width: 100%;
  overflow: hidden;
}

.pull-refresh-indicator {
  position: absolute;
  left: 0;
  right: 0;
  height: 50px;
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 0;
  pointer-events: none;
  transition: top 0.3s ease-out;
}

.indicator-content {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
  color: var(--app-text-secondary);
  transition: all 0.2s ease;
}

.indicator-icon {
  width: 20px;
  height: 20px;

  &.spinning {
    animation: spin 1s linear infinite;
  }
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

.indicator-text {
  font-size: 12px;
}

.pull-refresh-content {
  position: relative;
  z-index: 1;
  background: transparent;
  will-change: transform;
}
</style>
