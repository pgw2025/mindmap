<script setup lang="ts">
import { ref, watch, computed, nextTick } from 'vue'
import { NButton, NSpace } from 'naive-ui'
import type { NodeDto } from '@/api/nodes'
import RichTextEditor from '../RichTextEditor.vue'

const props = defineProps<{
  show: boolean
  node: NodeDto | null
  readonly?: boolean
}>()

const emit = defineEmits<{
  'update:show': [boolean]
  'change': [note: string]
}>()

const noteContent = ref('')
/** 防止初始化载入 note 时触发 change 事件 */
let isLoading = false

watch(() => props.show, (v) => {
  if (v && props.node) {
    isLoading = true
    noteContent.value = props.node.note || ''
    nextTick(() => {
      // 等待 RichTextEditor 内部 setContent 完成
      setTimeout(() => { isLoading = false }, 50)
    })
  }
})

const nodeTitle = computed(() => props.node?.title || '节点')
const hasNote = computed(() => {
  const txt = noteContent.value.replace(/<[^>]+>/g, '').trim()
  return txt.length > 0
})

function onInput(html: string) {
  noteContent.value = html
  if (isLoading) return
  emit('change', html)
}

function clearNote() {
  noteContent.value = ''
  emit('change', '')
}

function close() {
  emit('update:show', false)
}
</script>

<template>
  <Teleport to="body">
    <transition name="note-mask">
      <div v-if="show" class="note-mask" @click="close"></div>
    </transition>
    <transition name="note-panel">
      <div v-if="show" class="note-panel">
        <header class="note-header">
          <div class="note-title-zone">
            <span class="note-icon">📝</span>
            <div class="note-title-text">
              <div class="note-label">节点备注</div>
              <div class="note-node-title" :title="nodeTitle">{{ nodeTitle }}</div>
            </div>
          </div>
          <button class="note-close-btn" @click="close" title="关闭" aria-label="关闭">
            ✕
          </button>
        </header>

        <div class="note-body">
          <RichTextEditor v-if="!readonly" :model-value="noteContent" placeholder="输入节点备注（支持富文本）..."
            @update:model-value="onInput" />
          <div v-else class="note-readonly" v-html="noteContent || '<p class=\'note-empty\'>暂无备注</p>'"></div>
        </div>

        <footer class="note-footer">
          <span class="note-hint" v-if="!readonly">编辑后自动保存</span>
          <NSpace :size="8">
            <NButton v-if="!readonly && hasNote" size="small" quaternary @click="clearNote">
              清空备注
            </NButton>
            <NButton size="small" type="primary" @click="close">
              完成
            </NButton>
          </NSpace>
        </footer>
      </div>
    </transition>
  </Teleport>
</template>

<style scoped lang="scss">
.note-mask {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.35);
  z-index: 1999;
}

.note-panel {
  position: fixed;
  z-index: 2000;
  background: var(--app-card-bg, #fff);
  box-shadow: -4px 0 24px rgba(0, 0, 0, 0.15);
  display: flex;
  flex-direction: column;
  box-sizing: border-box;
  overflow: hidden;

  /* PC 端：右侧抽屉 */
  top: 0;
  right: 0;
  bottom: 0;
  width: 420px;
  max-width: 92vw;
}

.note-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14px 16px;
  border-bottom: 1px solid var(--app-border, #e0e0e6);
  flex-shrink: 0;
}

.note-title-zone {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
  flex: 1;
}

.note-icon {
  font-size: 18px;
  flex-shrink: 0;
}

.note-title-text {
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.note-label {
  font-size: 12px;
  font-weight: 600;
  color: var(--app-text-secondary, #666);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.note-node-title {
  font-size: 14px;
  font-weight: 500;
  color: var(--app-text-primary, #333);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.note-close-btn {
  flex-shrink: 0;
  width: 28px;
  height: 28px;
  border: none;
  background: transparent;
  color: var(--app-text-secondary, #999);
  font-size: 16px;
  cursor: pointer;
  border-radius: 6px;
  transition: all 0.2s;

  &:hover {
    background: var(--app-hover-bg, #f0f0f0);
    color: var(--app-text-primary, #333);
  }
}

.note-body {
  flex: 1;
  overflow-y: auto;
  overflow-x: hidden;
  -webkit-overflow-scrolling: touch;
  overscroll-behavior: contain;
  padding: 12px 16px;

  :deep(.rich-text-editor) {
    border: none;
    border-radius: 0;
  }

  :deep(.editor-content) {
    min-height: 200px;
    max-height: none;
  }
}

.note-readonly {
  font-size: 14px;
  line-height: 1.7;
  color: var(--app-text-primary, #333);
  word-break: break-word;

  :deep(p) {
    margin: 0 0 8px;
  }

  .note-empty,
  :deep(.note-empty) {
    color: var(--app-text-tertiary, #aaa);
    font-style: italic;
  }
}

.note-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 16px;
  border-top: 1px solid var(--app-border, #e0e0e6);
  flex-shrink: 0;
  gap: 8px;
}

.note-hint {
  font-size: 12px;
  color: var(--app-text-tertiary, #999);
}

/* PC 端过渡动画：从右滑入 */
.note-mask-enter-active,
.note-mask-leave-active {
  transition: opacity 0.25s ease;
}

.note-mask-enter-from,
.note-mask-leave-to {
  opacity: 0;
}

.note-panel-enter-active,
.note-panel-leave-active {
  transition: transform 0.3s cubic-bezier(0.4, 0, 0.2, 1);
}

.note-panel-enter-from,
.note-panel-leave-to {
  transform: translateX(100%);
}

/* 移动端：底部抽屉 */
@media (max-width: 767px) {
  .note-panel {
    top: auto;
    left: 0;
    right: 0;
    bottom: 0;
    width: 100%;
    max-width: 100%;
    height: 75vh;
    border-radius: 14px 14px 0 0;
    box-shadow: 0 -4px 24px rgba(0, 0, 0, 0.18);
    padding-bottom: env(safe-area-inset-bottom, 0px);
  }

  .note-panel-enter-from,
  .note-panel-leave-to {
    transform: translateY(100%);
  }

  .note-header {
    padding: 12px 14px;
  }

  .note-body {
    padding: 10px 14px;
  }

  .note-footer {
    padding: 10px 14px calc(10px + env(safe-area-inset-bottom, 0px));
  }
}
</style>
