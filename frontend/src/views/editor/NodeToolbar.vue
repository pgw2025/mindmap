<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import type { NodeDto, NodeUpdatePayload, NodeShape, EdgeStyle } from '@/api/nodes'

const props = defineProps<{
  node: NodeDto | undefined
  /** 当前选中节点数量，>1 表示多选状态 */
  activeNodeCount?: number
}>()

const emit = defineEmits<{
  (e: 'add-child'): void
  (e: 'add-sibling'): void
  (e: 'delete'): void
  (e: 'update', payload: NodeUpdatePayload): void
  (e: 'copy'): void
  (e: 'paste'): void
  (e: 'open-note'): void
  (e: 'create-line'): void
  (e: 'add-generalization'): void
}>()

const localNode = ref<Partial<NodeDto>>({})

/** 移动端：工具栏折叠到只显示操作按钮，点击「更多」展开样式 */
const mobileExpanded = ref(false)

watch(
  () => props.node,
  (val) => {
    if (val) {
      localNode.value = { ...val }
      // 切换节点时自动折叠移动端扩展面板
      mobileExpanded.value = false
    }
  },
  { immediate: true, deep: true }
)

const isRoot = computed(() => props.node?.parentId == null)

/** 是否多选状态（用于切换「摘要」→「多节点摘要」） */
const isMultiSelect = computed(() => (props.activeNodeCount ?? 1) > 1)

/** 预设颜色 */
const presetColors = [
  '#18a058',
  '#2080f0',
  '#f0a020',
  '#d03050',
  '#7048e8',
  '#00b894',
  '#6c5ce7',
  '#fd79a8',
  '#2d3436',
  '#ffffff'
]

const presetBgColors = [
  '#18a058',
  '#2080f0',
  '#f0a020',
  '#d03050',
  '#7048e8',
  '#00b894',
  '#6c5ce7',
  '#fd79a8',
  '#2d3436',
  '#ffffff'
]

const presetFontSizes = [12, 14, 16, 18, 20, 24, 28, 32]

const shapeOptions: { label: string; value: NodeShape }[] = [
  { label: '矩形', value: 0 },
  { label: '圆角矩形', value: 1 },
  { label: '圆形', value: 2 },
  { label: '椭圆', value: 3 },
  { label: '菱形', value: 4 },
  { label: '平行四边形', value: 5 },
  { label: '下划线', value: 6 }
]

const edgeStyleOptions: { label: string; value: EdgeStyle }[] = [
  { label: '实线', value: 0 },
  { label: '虚线', value: 1 },
  { label: '点线', value: 2 },
  { label: '贝塞尔曲线', value: 3 }
]

const iconOptions = ['📝', '💡', '🎯', '⭐', '🚀', '📌', '📋', '✅', '⚠️', '❌', '🔥', '💎', '🏆', '🎨', '📊', '🔑', '💻', '📱', '🌐', '❤️']

function applyUpdate(field: keyof NodeUpdatePayload, value: unknown) {
  const payload: NodeUpdatePayload = { [field]: value }
  emit('update', payload)
}

function toggleCollapse() {
  const current = localNode.value.isCollapsed ?? false
  applyUpdate('isCollapsed', !current)
}

function selectColor(color: string) {
  applyUpdate('color', color)
}

function selectBgColor(color: string) {
  applyUpdate('backgroundColor', color)
}

function selectFontSize(size: number) {
  applyUpdate('fontSize', size)
}

function selectShape(shape: NodeShape) {
  applyUpdate('shape', shape)
}

function selectIcon(icon: string) {
  const current = localNode.value.icon === icon ? null : icon
  applyUpdate('icon', current)
}

function selectEdgeStyle(style: EdgeStyle) {
  applyUpdate('edgeStyle', style)
}
</script>

<template>
  <div class="node-toolbar">
    <!-- 操作区 -->
    <div class="toolbar-section actions">
      <button class="tool-btn" @click="emit('add-child')" title="添加子节点">
        <span class="icon">＋</span>
        <span class="label">子节点</span>
      </button>
      <button v-if="!isRoot" class="tool-btn" @click="emit('add-sibling')" title="添加同级节点">
        <span class="icon">∥</span>
        <span class="label">同级</span>
      </button>
      <button v-if="!isRoot" class="tool-btn danger" @click="emit('delete')" title="删除节点">
        <span class="icon">🗑</span>
        <span class="label">删除</span>
      </button>
      <button class="tool-btn" @click="emit('copy')" title="复制 (Ctrl+C)">
        <span class="icon">⧉</span>
        <span class="label">复制</span>
      </button>
      <button class="tool-btn" @click="emit('paste')" title="粘贴 (Ctrl+V)">
        <span class="icon">📋</span>
        <span class="label">粘贴</span>
      </button>
      <button class="tool-btn" @click="toggleCollapse" title="折叠/展开">
        <span class="icon">{{ localNode.isCollapsed ? '▶' : '▼' }}</span>
        <span class="label">{{ localNode.isCollapsed ? '展开' : '折叠' }}</span>
      </button>
      <button class="tool-btn" :class="{ 'has-note': !!localNode.note }" @click="emit('open-note')" title="编辑备注">
        <span class="icon">📝</span>
        <span class="label">备注</span>
      </button>
      <button class="tool-btn" @click="emit('create-line')" title="连接到其他节点">
        <span class="icon">🔗</span>
        <span class="label">连线</span>
      </button>
      <button class="tool-btn" :class="{ 'multi-select': isMultiSelect }" @click="emit('add-generalization')"
        :title="isMultiSelect ? '为选中的多个节点添加区间摘要' : '添加摘要'">
        <span class="icon">📌</span>
        <span class="label">{{ isMultiSelect ? '多节点摘要' : '摘要' }}</span>
      </button>
    </div>

    <div class="divider mobile-only"></div>

    <!-- 移动端折叠切换 -->
    <button class="mobile-toggle mobile-only" @click="mobileExpanded = !mobileExpanded">
      {{ mobileExpanded ? '▲ 收起样式' : '▼ 更多样式（颜色/字号/形状/图标/连线）' }}
    </button>

    <!-- 以下样式区域在移动端可折叠 -->
    <div class="style-panel" :class="{ 'mobile-collapsed': !mobileExpanded }">
      <!-- 文字颜色 -->
      <div class="toolbar-section">
        <div class="section-title">文字颜色</div>
        <div class="color-grid">
          <button v-for="color in presetColors" :key="color" class="color-swatch" :style="{ background: color }"
            :class="{ active: localNode.color === color }" @click="selectColor(color)"></button>
          <button class="color-swatch transparent" :class="{ active: !localNode.color }" @click="selectColor('')"
            title="清除颜色">
            <span>✕</span>
          </button>
        </div>
      </div>

      <!-- 背景颜色 -->
      <div class="toolbar-section">
        <div class="section-title">背景颜色</div>
        <div class="color-grid">
          <button v-for="color in presetBgColors" :key="color" class="color-swatch" :style="{ background: color }"
            :class="{ active: localNode.backgroundColor === color }" @click="selectBgColor(color)"></button>
          <button class="color-swatch transparent" :class="{ active: !localNode.backgroundColor }"
            @click="selectBgColor('')" title="清除背景">
            <span>✕</span>
          </button>
        </div>
      </div>

      <div class="divider"></div>

      <!-- 字号 -->
      <div class="toolbar-section">
        <div class="section-title">字号</div>
        <div class="size-grid">
          <button v-for="size in presetFontSizes" :key="size" class="size-btn"
            :class="{ active: localNode.fontSize === size }" @click="selectFontSize(size)">
            {{ size }}
          </button>
        </div>
      </div>

      <!-- 形状 -->
      <div class="toolbar-section">
        <div class="section-title">形状</div>
        <div class="shape-grid">
          <button v-for="shape in shapeOptions" :key="shape.value" class="shape-btn"
            :class="{ active: localNode.shape === shape.value }" @click="selectShape(shape.value)">
            {{ shape.label }}
          </button>
        </div>
      </div>

      <div class="divider"></div>

      <!-- 图标 -->
      <div class="toolbar-section">
        <div class="section-title">图标</div>
        <div class="icon-grid">
          <button v-for="icon in iconOptions" :key="icon" class="icon-btn" :class="{ active: localNode.icon === icon }"
            @click="selectIcon(icon)">
            {{ icon }}
          </button>
        </div>
      </div>

      <div class="divider"></div>

      <!-- 连线样式 -->
      <div class="toolbar-section">
        <div class="section-title">连线样式</div>
        <div class="edge-grid">
          <button v-for="style in edgeStyleOptions" :key="style.value" class="edge-btn"
            :class="{ active: localNode.edgeStyle === style.value }" @click="selectEdgeStyle(style.value)">
            {{ style.label }}
          </button>
        </div>
      </div>
    </div><!-- /.style-panel -->
  </div>
</template>

<style scoped lang="scss">
.node-toolbar {
  position: absolute;
  top: 16px;
  right: 16px;
  width: 290px;
  box-sizing: border-box;
  max-height: calc(100% - 32px);
  overflow-y: auto;
  overflow-x: hidden;
  -webkit-overflow-scrolling: touch;
  overscroll-behavior: contain;
  background: var(--app-card-bg, #fff);
  border-radius: 12px;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.12);
  padding: 16px;
  z-index: 80;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.toolbar-section {
  display: flex;
  flex-direction: column;
  gap: 6px;
  width: 100%;
  box-sizing: border-box;
}

.section-title {
  font-size: 11px;
  font-weight: 600;
  color: var(--app-text-secondary, #666);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.actions {
  flex-direction: row;
  flex-wrap: wrap;
  gap: 6px;
}

.tool-btn {
  flex: 1 1 calc(33.333% - 6px);
  min-width: 60px;
  box-sizing: border-box;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
  padding: 8px 6px;
  background: var(--app-bg, #f5f7fa);
  border: 1px solid var(--app-border, #e0e0e6);
  border-radius: 8px;
  color: var(--app-text-primary, #333);
  cursor: pointer;
  transition: all 0.2s;
  font-size: 12px;

  &:hover {
    background: var(--app-hover-bg, #e8eaed);
    border-color: var(--app-primary, #18a058);
  }

  &.danger {
    &:hover {
      background: #fff0f0;
      border-color: #d03050;
      color: #d03050;
    }
  }

  &.has-note {
    background: rgba(24, 160, 88, 0.1);
    border-color: var(--app-primary, #18a058);
    color: var(--app-primary, #18a058);
  }

  &.multi-select {
    background: rgba(32, 128, 240, 0.12);
    border-color: #2080f0;
    color: #2080f0;
  }

  .icon {
    font-size: 16px;
    font-weight: bold;
  }

  .label {
    font-size: 10px;
  }
}

.divider {
  height: 1px;
  background: var(--app-border, #e0e0e6);
  margin: 2px 0;
}

.color-grid {
  display: grid;
  grid-template-columns: repeat(6, 1fr);
  gap: 6px;
  justify-items: center;
}

.color-swatch {
  width: 32px;
  height: 32px;
  box-sizing: border-box;
  border-radius: 6px;
  border: 2px solid transparent;
  cursor: pointer;
  transition: all 0.2s;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #fff;
  font-size: 10px;

  &:hover {
    transform: scale(1.08);
  }

  &.active {
    border-color: var(--app-primary, #18a058);
    box-shadow: 0 0 0 2px rgba(24, 160, 88, 0.3);
  }

  &.transparent {
    background: var(--app-bg, #f5f7fa) !important;
    border: 1px dashed var(--app-border, #ccc);
    color: var(--app-text-tertiary, #999);

    span {
      opacity: 0.6;
    }
  }
}

.size-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 6px;
}

.size-btn {
  padding: 6px 4px;
  box-sizing: border-box;
  background: var(--app-bg, #f5f7fa);
  border: 1px solid var(--app-border, #e0e0e6);
  border-radius: 6px;
  color: var(--app-text-primary, #333);
  cursor: pointer;
  font-size: 12px;
  transition: all 0.2s;

  &:hover {
    background: var(--app-hover-bg, #e8eaed);
  }

  &.active {
    background: var(--app-primary, #18a058);
    color: #fff;
    border-color: var(--app-primary, #18a058);
  }
}

.shape-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 6px;
}

.shape-btn {
  padding: 6px 4px;
  box-sizing: border-box;
  background: var(--app-bg, #f5f7fa);
  border: 1px solid var(--app-border, #e0e0e6);
  border-radius: 6px;
  color: var(--app-text-primary, #333);
  cursor: pointer;
  font-size: 11px;
  transition: all 0.2s;

  &:hover {
    background: var(--app-hover-bg, #e8eaed);
  }

  &.active {
    background: var(--app-primary, #18a058);
    color: #fff;
    border-color: var(--app-primary, #18a058);
  }
}

.icon-grid {
  display: grid;
  grid-template-columns: repeat(5, 1fr);
  gap: 6px;
}

.icon-btn {
  height: 32px;
  box-sizing: border-box;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 0;
  background: var(--app-bg, #f5f7fa);
  border: 1px solid transparent;
  border-radius: 6px;
  cursor: pointer;
  font-size: 16px;
  line-height: 1;
  transition: all 0.15s ease;

  &:hover {
    background: var(--app-hover-bg, #e8eaed);
    transform: scale(1.1);
  }

  &.active {
    background: rgba(24, 160, 88, 0.15);
    border-color: var(--app-primary, #18a058);
  }
}

.edge-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 6px;
}

.edge-btn {
  padding: 6px 4px;
  background: var(--app-bg, #f5f7fa);
  border: 1px solid var(--app-border, #e0e0e6);
  border-radius: 6px;
  color: var(--app-text-primary, #333);
  cursor: pointer;
  font-size: 11px;
  transition: all 0.2s;

  &:hover {
    background: var(--app-hover-bg, #e8eaed);
  }

  &.active {
    background: var(--app-primary, #18a058);
    color: #fff;
    border-color: var(--app-primary, #18a058);
  }
}

@media (max-width: 767px) {
  .node-toolbar {
    position: fixed;
    top: auto;
    bottom: 0;
    left: 0;
    right: 0;
    width: 100%;
    max-height: 70vh;
    overflow-y: auto;
    overflow-x: hidden;
    -webkit-overflow-scrolling: touch;
    overscroll-behavior: contain;
    touch-action: pan-y;
    border-radius: 14px 14px 0 0;
    box-shadow: 0 -4px 20px rgba(0, 0, 0, 0.12);
    padding: 8px 10px calc(8px + env(safe-area-inset-bottom, 0px));
    gap: 6px;
    z-index: 1000;
  }

  .actions {
    justify-content: flex-start;
    flex-wrap: nowrap;
    overflow-x: auto;
    overflow-y: hidden;
    -webkit-overflow-scrolling: touch;
    overscroll-behavior: contain;
    touch-action: pan-x;
    gap: 5px;
    padding-bottom: 2px;

    .tool-btn {
      flex: 0 0 auto;
      min-width: 46px;
      padding: 4px 6px;
      border-radius: 6px;
      gap: 1px;

      .icon {
        font-size: 13px;
        line-height: 1.2;
      }

      .label {
        font-size: 9.5px;
        line-height: 1.1;
      }
    }
  }

  /* 移动端折叠样式面板 */
  .style-panel {
    display: flex;
    flex-direction: column;
    gap: 8px;
    width: 100%;
  }

  .style-panel.mobile-collapsed {
    display: none;
  }

  .mobile-toggle {
    width: 100%;
    text-align: center;
    padding: 5px 8px;
    background: var(--app-bg, #f5f7fa);
    border: 1px solid var(--app-border, #e0e0e6);
    border-radius: 6px;
    font-size: 11px;
    color: var(--app-text-secondary, #666);
    cursor: pointer;
    margin-bottom: 2px;
  }

  /* 桌面端隐藏移动端专用元素 */
  .mobile-only {
    display: block;
  }

  .color-grid {
    grid-template-columns: repeat(6, 1fr);
  }

  .color-swatch {
    width: 28px;
    height: 28px;
  }

  .icon-grid {
    grid-template-columns: repeat(6, 1fr);
  }
}

/* 桌面端：隐藏 mobile-only 元素 */
@media (min-width: 768px) {
  .mobile-only {
    display: none !important;
  }
}
</style>
