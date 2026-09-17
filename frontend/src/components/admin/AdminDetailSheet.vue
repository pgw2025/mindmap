<script setup lang="ts">
import { computed } from 'vue'
import { NDrawer, NDrawerContent, NIcon } from 'naive-ui'
import { CloseOutline } from '@vicons/ionicons5'

interface Props {
  show: boolean
  title?: string
  /** 抽屉高度比例 0-1，默认 0.75 */
  heightRatio?: number
  /** 是否显示关闭按钮，默认 true */
  closable?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  title: '',
  heightRatio: 0.75,
  closable: true
})

const emit = defineEmits<{
  (e: 'update:show', v: boolean): void
  (e: 'close'): void
}>()

const drawerStyle = computed(() => ({
  height: `${props.heightRatio * 100}vh`
}))

function handleClose() {
  emit('update:show', false)
  emit('close')
}
</script>

<template>
  <NDrawer
    :show="show"
    :style="drawerStyle"
    placement="bottom"
    class="bottom-sheet-drawer"
    @update:show="(v: boolean) => emit('update:show', v)"
  >
    <NDrawerContent>
      <div class="bottom-sheet">
        <!-- 顶部拖拽指示条 -->
        <div class="sheet-handle">
          <span class="handle-bar"></span>
        </div>

        <!-- 标题栏 -->
        <div v-if="title || closable" class="sheet-header">
          <div v-if="title" class="sheet-title">{{ title }}</div>
          <div v-else></div>
          <button v-if="closable" class="sheet-close-btn" @click="handleClose">
            <NIcon size="20">
              <CloseOutline />
            </NIcon>
          </button>
        </div>

        <!-- 内容区 -->
        <div class="sheet-body">
          <slot />
        </div>

        <!-- 底部操作区 -->
        <div v-if="$slots.footer" class="sheet-footer">
          <slot name="footer" />
        </div>
      </div>
    </NDrawerContent>
  </NDrawer>
</template>

<style scoped lang="scss">
.bottom-sheet-drawer {
  :deep(.n-drawer) {
    border-radius: 16px 16px 0 0;
    background: var(--app-bg);
    box-shadow: 0 -4px 24px rgba(0, 0, 0, 0.12);
  }

  :deep(.n-drawer-content-wrapper) {
    padding: 0;
  }

  :deep(.n-drawer .n-drawer-content) {
    padding: 0;
  }
}

.bottom-sheet {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}

.sheet-handle {
  display: flex;
  justify-content: center;
  padding: 10px 0 6px;
  flex-shrink: 0;

  .handle-bar {
    width: 36px;
    height: 4px;
    border-radius: 2px;
    background: var(--app-border);
  }
}

.sheet-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 4px 16px 10px;
  flex-shrink: 0;
  border-bottom: 1px solid var(--app-border);
}

.sheet-title {
  font-size: 17px;
  font-weight: 600;
  color: var(--app-text-primary);
}

.sheet-close-btn {
  width: 32px;
  height: 32px;
  border: none;
  background: var(--app-card-bg);
  border-radius: 50%;
  color: var(--app-text-secondary);
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.15s ease;

  &:active {
    transform: scale(0.9);
    background: var(--app-border);
  }
}

.sheet-body {
  flex: 1;
  overflow-y: auto;
  padding: 16px;
  -webkit-overflow-scrolling: touch;
}

.sheet-footer {
  flex-shrink: 0;
  padding: 12px 16px;
  padding-bottom: calc(12px + env(safe-area-inset-bottom));
  border-top: 1px solid var(--app-border);
  background: var(--app-card-bg);
  display: flex;
  gap: 10px;
  align-items: center;
}
</style>
