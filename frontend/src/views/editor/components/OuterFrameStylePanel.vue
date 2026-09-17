<template>
  <Transition name="panel-slide">
    <div v-if="visible" class="outer-frame-style-panel" @click.stop>
      <div class="panel-header">
        <span class="panel-title">外框样式</span>
        <button class="close-btn" @click="emit('close')" title="关闭">×</button>
      </div>

      <div class="panel-body">
        <!-- 标题组 -->
        <div class="style-group">
          <div class="group-header" @click="toggleGroup('title')">
            <span class="group-arrow" :class="{ expanded: expandedGroups.title }">▶</span>
            <span class="group-label">标题</span>
          </div>
          <div class="group-content" v-show="expandedGroups.title">
            <div class="style-row">
              <label>标题文字</label>
              <input type="text" class="title-input" :value="config.text"
                @input="update('text', ($event.target as HTMLInputElement).value)" placeholder="输入外框标题..." />
            </div>
            <div class="style-row">
              <label>字号</label>
              <input type="range" min="10" max="28" :value="config.textFontSize"
                @input="update('textFontSize', Number(($event.target as HTMLInputElement).value))" />
              <span class="value-text">{{ config.textFontSize }}px</span>
            </div>
            <div class="style-row">
              <label>文字色</label>
              <input type="color" :value="normalizeHex(config.textColor)"
                @input="update('textColor', ($event.target as HTMLInputElement).value)" />
            </div>
            <div class="style-row">
              <label>文字背景</label>
              <input type="color" :value="rgbaToHex(config.textBgColor)"
                @input="update('textBgColor', hexToRgba(($event.target as HTMLInputElement).value, 0.15))" />
            </div>
          </div>
        </div>

        <!-- 边框组 -->
        <div class="style-group">
          <div class="group-header" @click="toggleGroup('border')">
            <span class="group-arrow" :class="{ expanded: expandedGroups.border }">▶</span>
            <span class="group-label">边框</span>
          </div>
          <div class="group-content" v-show="expandedGroups.border">
            <div class="style-row">
              <label>颜色</label>
              <input type="color" :value="normalizeHex(config.strokeColor)"
                @input="update('strokeColor', ($event.target as HTMLInputElement).value)" />
            </div>
            <div class="style-row">
              <label>线宽</label>
              <input type="range" min="1" max="8" :value="config.strokeWidth"
                @input="update('strokeWidth', Number(($event.target as HTMLInputElement).value))" />
              <span class="value-text">{{ config.strokeWidth }}px</span>
            </div>
            <div class="style-row">
              <label>线型</label>
              <div class="toggle-group">
                <button :class="{ active: config.strokeDasharray === 'none' }"
                  @click="update('strokeDasharray', 'none')">实线</button>
                <button :class="{ active: config.strokeDasharray !== 'none' }"
                  @click="update('strokeDasharray', '5,5')">虚线</button>
              </div>
            </div>
            <div class="style-row">
              <label>圆角</label>
              <input type="range" min="0" max="30" :value="config.radius"
                @input="update('radius', Number(($event.target as HTMLInputElement).value))" />
              <span class="value-text">{{ config.radius }}px</span>
            </div>
          </div>
        </div>

        <!-- 背景组 -->
        <div class="style-group">
          <div class="group-header" @click="toggleGroup('background')">
            <span class="group-arrow" :class="{ expanded: expandedGroups.background }">▶</span>
            <span class="group-label">背景</span>
          </div>
          <div class="group-content" v-show="expandedGroups.background">
            <div class="style-row">
              <label>颜色</label>
              <input type="color" :value="rgbaToHex(config.fill)"
                @input="onFillColorChange(($event.target as HTMLInputElement).value)" />
            </div>
            <div class="style-row">
              <label>透明度</label>
              <input type="range" min="0" max="100" :value="fillOpacity" @input="onFillOpacityChange" />
              <span class="value-text">{{ fillOpacity }}%</span>
            </div>
          </div>
        </div>

        <!-- 预设组 -->
        <div class="style-group">
          <div class="group-header" @click="toggleGroup('presets')">
            <span class="group-arrow" :class="{ expanded: expandedGroups.presets }">▶</span>
            <span class="group-label">预设样式</span>
          </div>
          <div class="group-content" v-show="expandedGroups.presets">
            <div class="preset-grid">
              <button v-for="preset in presets" :key="preset.name" class="preset-btn"
                :style="getPresetPreviewStyle(preset)" @click="applyPreset(preset)" :title="preset.name">
                <span class="preset-name">{{ preset.name }}</span>
              </button>
            </div>
          </div>
        </div>

        <!-- 操作按钮 -->
        <div class="action-row">
          <button class="action-btn danger" @click="emit('remove')" title="删除外框">删除外框</button>
        </div>
      </div>
    </div>
  </Transition>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'

/** 外框样式浮层：选中外框激活时显示，支持分组折叠、标题编辑、背景透明度、样式预设 */

export interface OuterFrameConfig {
  strokeColor: string
  strokeWidth: number
  strokeDasharray: string
  radius: number
  fill: string
  text: string
  textFontSize: number
  textColor: string
  textBgColor: string
}

interface Preset {
  name: string
  strokeColor: string
  strokeWidth: number
  strokeDasharray: string
  radius: number
  fillColor: string
  fillOpacity: number
  textColor: string
  textBgColor: string
}

const props = defineProps<{
  visible: boolean
  config: OuterFrameConfig
}>()

const emit = defineEmits<{
  (e: 'update', payload: Partial<OuterFrameConfig>): void
  (e: 'remove'): void
  (e: 'close'): void
}>()

/** 各分组展开状态 */
const expandedGroups = ref({
  title: true,
  border: true,
  background: true,
  presets: false
})

function toggleGroup(key: keyof typeof expandedGroups.value) {
  expandedGroups.value[key] = !expandedGroups.value[key]
}

/** 更新单个样式字段 */
function update<K extends keyof OuterFrameConfig>(key: K, value: OuterFrameConfig[K]) {
  emit('update', { [key]: value } as Partial<OuterFrameConfig>)
}

/** 从 rgba 字符串中提取透明度百分比 */
const fillOpacity = computed(() => {
  const m = props.config.fill.match(/rgba?\([^)]+,\s*([\d.]+)\)/)
  if (!m) return 5
  return Math.round(Number(m[1]) * 100)
})

/** rgba(r,g,b,a) → #rrggbb（取 rgb 部分） */
function rgbaToHex(rgba: string): string {
  const m = rgba.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)/)
  if (!m) return '#ffffff'
  const toHex = (n: number) => n.toString(16).padStart(2, '0')
  return `#${toHex(Number(m[1]))}${toHex(Number(m[2]))}${toHex(Number(m[3]))}`
}

/** 将任意颜色值规范化为 #rrggbb 格式（3位hex、命名颜色 → 6位hex） */
function normalizeHex(color: string): string {
  if (!color) return '#000000'
  // 已经是 6 位 hex，直接返回
  if (/^#[0-9a-fA-F]{6}$/.test(color)) return color.toLowerCase()
  // 3 位 hex → 6 位
  if (/^#[0-9a-fA-F]{3}$/.test(color)) {
    return '#' + color.slice(1).split('').map(c => c + c).join('').toLowerCase()
  }
  // rgba/rgb → 转换
  if (color.startsWith('rgb')) {
    return rgbaToHex(color)
  }
  // 其他情况（命名颜色等）用临时元素转换
  try {
    const temp = document.createElement('div')
    temp.style.color = color
    document.body.appendChild(temp)
    const rgb = getComputedStyle(temp).color
    document.body.removeChild(temp)
    return rgbaToHex(rgb)
  } catch {
    return '#000000'
  }
}

/** #rrggbb + 透明度 → rgba(r,g,b,a) */
function hexToRgba(hex: string, alpha = 0.05): string {
  const h = normalizeHex(hex)
  const r = parseInt(h.slice(1, 3), 16)
  const g = parseInt(h.slice(3, 5), 16)
  const b = parseInt(h.slice(5, 7), 16)
  return `rgba(${r}, ${g}, ${b}, ${alpha})`
}

/** 背景色变化时，保留当前透明度 */
function onFillColorChange(hex: string) {
  const alpha = fillOpacity.value / 100
  update('fill', hexToRgba(hex, alpha))
}

/** 透明度变化时，保留当前颜色 */
function onFillOpacityChange(e: Event) {
  const opacity = Number((e.target as HTMLInputElement).value)
  const hex = rgbaToHex(props.config.fill)
  update('fill', hexToRgba(hex, opacity / 100))
}

/** 预设样式列表 */
const presets: Preset[] = [
  {
    name: '蓝色实线',
    strokeColor: '#2080f0',
    strokeWidth: 2,
    strokeDasharray: 'none',
    radius: 6,
    fillColor: '#2080f0',
    fillOpacity: 5,
    textColor: '#2080f0',
    textBgColor: 'rgba(32,128,240,0.1)'
  },
  {
    name: '绿色填充',
    strokeColor: '#18a058',
    strokeWidth: 2,
    strokeDasharray: 'none',
    radius: 8,
    fillColor: '#18a058',
    fillOpacity: 12,
    textColor: '#18a058',
    textBgColor: 'rgba(24,160,88,0.15)'
  },
  {
    name: '红色警示',
    strokeColor: '#d03050',
    strokeWidth: 2,
    strokeDasharray: 'none',
    radius: 4,
    fillColor: '#d03050',
    fillOpacity: 8,
    textColor: '#d03050',
    textBgColor: 'rgba(208,48,80,0.1)'
  },
  {
    name: '橙色重点',
    strokeColor: '#f0a020',
    strokeWidth: 3,
    strokeDasharray: 'none',
    radius: 6,
    fillColor: '#f0a020',
    fillOpacity: 10,
    textColor: '#f0a020',
    textBgColor: 'rgba(240,160,32,0.15)'
  },
  {
    name: '灰色虚线',
    strokeColor: '#999',
    strokeWidth: 1,
    strokeDasharray: '5,5',
    radius: 4,
    fillColor: '#999',
    fillOpacity: 3,
    textColor: '#666',
    textBgColor: 'rgba(153,153,153,0.1)'
  },
  {
    name: '紫色圆角',
    strokeColor: '#7048e8',
    strokeWidth: 2,
    strokeDasharray: 'none',
    radius: 20,
    fillColor: '#7048e8',
    fillOpacity: 8,
    textColor: '#7048e8',
    textBgColor: 'rgba(112,72,232,0.12)'
  }
]

/** 生成预设按钮的预览样式 */
function getPresetPreviewStyle(preset: Preset) {
  return {
    borderColor: preset.strokeColor,
    borderWidth: `${preset.strokeWidth}px`,
    borderStyle: preset.strokeDasharray === 'none' ? 'solid' : 'dashed',
    borderRadius: `${preset.radius}px`,
    background: hexToRgba('#' + preset.fillColor.slice(1), preset.fillOpacity / 100),
    color: preset.textColor
  }
}

/** 应用预设样式 */
function applyPreset(preset: Preset) {
  emit('update', {
    strokeColor: preset.strokeColor,
    strokeWidth: preset.strokeWidth,
    strokeDasharray: preset.strokeDasharray,
    radius: preset.radius,
    fill: hexToRgba(preset.fillColor, preset.fillOpacity / 100),
    textColor: preset.textColor,
    textBgColor: preset.textBgColor
  })
}
</script>

<style scoped>
.outer-frame-style-panel {
  position: absolute;
  top: 60px;
  right: 16px;
  width: 280px;
  background: var(--app-card-bg, #fff);
  border-radius: 12px;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.15);
  z-index: 1000;
  font-size: 12px;
  max-height: calc(100vh - 120px);
  overflow-y: auto;
  -webkit-overflow-scrolling: touch;
  overscroll-behavior: contain;
}

.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px;
  border-bottom: 1px solid var(--app-border, #eee);
  position: sticky;
  top: 0;
  background: var(--app-card-bg, #fff);
  z-index: 1;
}

.panel-title {
  font-weight: 600;
  color: var(--app-text-primary, #333);
  font-size: 14px;
}

.close-btn {
  background: none;
  border: none;
  font-size: 20px;
  color: var(--app-text-tertiary, #999);
  cursor: pointer;
  padding: 0 4px;
  line-height: 1;
  width: 24px;
  height: 24px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 4px;
  transition: background 0.2s;
}

.close-btn:hover {
  color: var(--app-text-primary, #333);
  background: var(--app-hover-bg, #f0f0f0);
}

.panel-body {
  padding: 8px 16px 16px;
}

.style-group {
  padding: 4px 0;
  border-bottom: 1px solid var(--app-divider, #f0f0f0);
}

.style-group:last-of-type {
  border-bottom: none;
}

.group-header {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 10px 0;
  cursor: pointer;
  user-select: none;
}

.group-arrow {
  font-size: 10px;
  color: var(--app-text-tertiary, #999);
  transition: transform 0.2s;
  display: inline-block;
}

.group-arrow.expanded {
  transform: rotate(90deg);
}

.group-label {
  font-weight: 600;
  color: var(--app-text-secondary, #666);
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.group-content {
  padding-bottom: 8px;
  overflow: hidden;
}

.style-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
}

.style-row:last-child {
  margin-bottom: 0;
}

.style-row label {
  width: 68px;
  color: var(--app-text-secondary, #666);
  flex-shrink: 0;
  font-size: 12px;
}

.title-input {
  flex: 1;
  padding: 5px 8px;
  border: 1px solid var(--app-border, #ddd);
  border-radius: 6px;
  font-size: 12px;
  color: var(--app-text-primary, #333);
  background: var(--app-bg, #fff);
  outline: none;
  transition: border-color 0.2s;
}

.title-input:focus {
  border-color: var(--app-primary, #18a058);
}

.style-row input[type="color"] {
  width: 32px;
  height: 26px;
  border: 1px solid var(--app-border, #ddd);
  border-radius: 6px;
  cursor: pointer;
  padding: 2px;
  background: var(--app-card-bg, #fff);
}

.style-row input[type="range"] {
  flex: 1;
  height: 4px;
  cursor: pointer;
  accent-color: var(--app-primary, #18a058);
}

.value-text {
  width: 40px;
  text-align: right;
  color: var(--app-text-tertiary, #999);
  font-size: 11px;
  flex-shrink: 0;
}

.toggle-group {
  display: flex;
  gap: 4px;
}

.toggle-group button {
  padding: 4px 12px;
  border: 1px solid var(--app-border, #ddd);
  border-radius: 6px;
  background: var(--app-card-bg, #fff);
  cursor: pointer;
  font-size: 11px;
  color: var(--app-text-secondary, #666);
  transition: all 0.2s;
}

.toggle-group button.active {
  background: var(--app-primary, #18a058);
  color: #fff;
  border-color: var(--app-primary, #18a058);
}

/* 预设样式网格 */
.preset-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 8px;
}

.preset-btn {
  padding: 12px 4px;
  border: 2px solid #ddd;
  border-radius: 8px;
  background: #fff;
  cursor: pointer;
  font-size: 11px;
  color: #333;
  transition: all 0.2s;
  display: flex;
  align-items: center;
  justify-content: center;
  text-align: center;
  min-height: 44px;
}

.preset-btn:hover {
  transform: translateY(-1px);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
}

.preset-name {
  font-weight: 500;
}

.action-row {
  padding-top: 12px;
  display: flex;
  justify-content: flex-end;
}

.action-btn {
  padding: 7px 16px;
  border: 1px solid var(--app-border, #ddd);
  border-radius: 6px;
  background: var(--app-card-bg, #fff);
  cursor: pointer;
  font-size: 12px;
  transition: all 0.2s;
}

.action-btn.danger {
  color: #d03050;
  border-color: #d03050;
}

.action-btn.danger:hover {
  background: #d03050;
  color: #fff;
}

/* 浮层滑入动画 */
.panel-slide-enter-active,
.panel-slide-leave-active {
  transition: transform 0.2s ease, opacity 0.2s ease;
}

.panel-slide-enter-from,
.panel-slide-leave-to {
  transform: translateX(20px);
  opacity: 0;
}

/* 移动端适配 */
@media (max-width: 768px) {
  .outer-frame-style-panel {
    top: auto;
    bottom: 0;
    right: 0;
    left: 0;
    width: 100%;
    border-radius: 14px 14px 0 0;
    max-height: 75vh;
  }

  .panel-header {
    padding: 14px 16px;
  }

  .panel-body {
    padding: 4px 16px calc(16px + env(safe-area-inset-bottom, 0px));
  }

  .preset-grid {
    grid-template-columns: repeat(3, 1fr);
  }
}
</style>
