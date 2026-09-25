<script setup lang="ts">
import { ref, onBeforeUnmount } from 'vue'

const visible = ref(false)
const content = ref('')
const left = ref(0)
const top = ref(0)

/** 关闭延迟定时器 */
let hideTimer: ReturnType<typeof setTimeout> | null = null
/** 关闭延迟时间（毫秒），给用户足够时间将鼠标移入气泡 */
const HIDE_DELAY = 300

/** 清除待执行的关闭定时器 */
function clearHideTimer() {
  if (hideTimer) {
    clearTimeout(hideTimer)
    hideTimer = null
  }
}

/** 展示备注 tooltip（simple-mind-map customNoteContentShow.show 回调） */
function show(note: string, x: number, y: number) {
  clearHideTimer()
  content.value = note || ''
  // 在备注图标右下偏移，避免遮挡图标
  left.value = x + 8
  top.value = y + 8
  visible.value = content.value.length > 0
}

/** 隐藏备注 tooltip（simple-mind-map customNoteContentShow.hide 回调）
 *  添加延迟关闭，鼠标从图标移到气泡上时不会消失
 */
function hide() {
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

onBeforeUnmount(() => {
  clearHideTimer()
})

defineExpose({ show, hide })
</script>

<template>
  <Teleport to="body">
    <div
      v-if="visible"
      class="node-note-tooltip"
      :style="{ left: left + 'px', top: top + 'px' }"
      @mouseenter="handleMouseEnter"
      @mouseleave="handleMouseLeave"
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
}
</style>
