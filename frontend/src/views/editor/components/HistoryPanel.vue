<script setup lang="ts">
import { computed } from 'vue'
import { NDrawer, NDrawerContent, NEmpty } from 'naive-ui'
import { useNodesStore } from '@/stores/nodes'

const props = defineProps<{
  show: boolean
}>()

const emit = defineEmits<{
  'update:show': [boolean]
  /** 用户点击某条历史记录，请求跳转到对应状态 */
  'jump': [targetCursor: number]
}>()

const nodesStore = useNodesStore()

/** 当前状态操作数（即 historyCursor） */
const cursor = computed(() => nodesStore.historyCursor)

/** 类型 → 图标映射 */
const TYPE_ICON: Record<string, string> = {
  create: '＋',
  update: '✎',
  delete: '🗑',
  move: '⇄',
  batch: '≡',
  compound: '⇄'
}

function typeIcon(type: string): string {
  return TYPE_ICON[type] ?? '·'
}

/** 点击条目：跳转到该条完成后的状态。
 *  undo 侧（index < cursor）：点击 index=i → 回退到 cursor=i+1（保留该条效果）。
 *  redo 侧（index >= cursor）：点击 index=i → 前进到 cursor=i+1。 */
function handleClick(index: number) {
  if (index + 1 === cursor.value) return // 已是当前状态，无操作
  emit('jump', index + 1)
}

/** 条目是否处于「当前状态」（即指针正好落在该条之后） */
function isCurrent(index: number): boolean {
  return index + 1 === cursor.value
}
</script>

<template>
  <NDrawer :show="show" @update:show="emit('update:show', $event)" :width="360" placement="right"
    display-directive="if" title="历史记录">
    <NDrawerContent>
      <template #header>
        <div class="history-drawer-header">
          <span>⏱ 历史记录</span>
          <span class="history-hint">点击条目跳转</span>
        </div>
      </template>

      <div class="history-list-wrap">
        <NEmpty v-if="nodesStore.historyList.length === 0"
          description="暂无操作记录。开始编辑脑图后，每次操作都会出现在这里。" />
        <div v-else class="history-list">
          <button
            v-for="(entry, i) in nodesStore.historyList"
            :key="`${entry.side}-${i}`"
            class="history-item"
            :class="{
              'is-redo': entry.side === 'redo',
              'is-current': isCurrent(i)
            }"
            @click="handleClick(i)"
          >
            <span class="history-icon">{{ typeIcon(entry.cmd.type) }}</span>
            <span class="history-label">{{ entry.cmd.label || entry.cmd.type }}</span>
            <span class="history-side-tag">{{ entry.side === 'redo' ? '已撤销' : '已应用' }}</span>
          </button>
        </div>
      </div>
    </NDrawerContent>
  </NDrawer>
</template>

<style scoped lang="scss">
.history-drawer-header {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  width: 100%;
  padding-right: 12px;
  font-size: 16px;
  font-weight: 600;
  color: var(--app-text-primary, #333);

  .history-hint {
    font-size: 12px;
    font-weight: 400;
    color: var(--app-text-secondary, #999);
  }
}

.history-list-wrap {
  padding: 0 4px 16px;
}

.history-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.history-item {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  padding: 10px 12px;
  border: 1px solid transparent;
  border-radius: 8px;
  background: transparent;
  text-align: left;
  cursor: pointer;
  transition: background 0.15s, border-color 0.15s, opacity 0.15s;
  font-size: 14px;
  color: var(--app-text-primary, #333);

  &:hover {
    background: var(--app-hover-bg, rgba(0, 0, 0, 0.04));
  }

  &.is-redo {
    opacity: 0.45;

    &:hover {
      opacity: 0.7;
    }
  }

  &.is-current {
    border-color: var(--app-primary, #18a058);
    background: var(--app-primary-soft, rgba(24, 160, 88, 0.08));

    &:hover {
      background: var(--app-primary-soft, rgba(24, 160, 88, 0.12));
    }
  }
}

.history-icon {
  flex: none;
  width: 20px;
  text-align: center;
  font-size: 15px;
  color: var(--app-text-secondary, #666);
}

.history-label {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.history-side-tag {
  flex: none;
  font-size: 11px;
  color: var(--app-text-secondary, #999);
}
</style>
