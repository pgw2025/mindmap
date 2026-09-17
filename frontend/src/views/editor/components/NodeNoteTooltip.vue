<script setup lang="ts">
import { ref } from 'vue'

const visible = ref(false)
const content = ref('')
const left = ref(0)
const top = ref(0)

/** 展示备注 tooltip（simple-mind-map customNoteContentShow.show 回调） */
function show(note: string, x: number, y: number) {
  content.value = note || ''
  // 在备注图标右下偏移，避免遮挡图标
  left.value = x + 8
  top.value = y + 8
  visible.value = content.value.length > 0
}

/** 隐藏备注 tooltip（simple-mind-map customNoteContentShow.hide 回调） */
function hide() {
  visible.value = false
}

defineExpose({ show, hide })
</script>

<template>
  <Teleport to="body">
    <div v-if="visible" class="node-note-tooltip" :style="{ left: left + 'px', top: top + 'px' }"
      v-html="content"></div>
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