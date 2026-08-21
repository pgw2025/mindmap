<template>
  <Transition name="panel-slide">
    <div v-if="visible" class="outer-frame-style-panel" @click.stop>
      <div class="panel-header">
        <span class="panel-title">外框样式</span>
        <button class="close-btn" @click="emit('close')" title="关闭">×</button>
      </div>

      <div class="panel-body">
        <!-- 边框样式组 -->
        <div class="style-group">
          <div class="group-label">边框</div>
          <div class="style-row">
            <label>颜色</label>
            <input type="color" :value="config.strokeColor" @input="update('strokeColor', ($event.target as HTMLInputElement).value)" />
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

        <!-- 填充 -->
        <div class="style-group">
          <div class="group-label">填充</div>
          <div class="style-row">
            <label>背景色</label>
            <input type="color" :value="rgbaToHex(config.fill)" @input="update('fill', hexToRgba(($event.target as HTMLInputElement).value))" />
          </div>
        </div>

        <!-- 文字标签样式 -->
        <div class="style-group">
          <div class="group-label">文字标签</div>
          <div class="style-row">
            <label>字号</label>
            <input type="range" min="10" max="24" :value="config.textFontSize"
              @input="update('textFontSize', Number(($event.target as HTMLInputElement).value))" />
            <span class="value-text">{{ config.textFontSize }}px</span>
          </div>
          <div class="style-row">
            <label>文字色</label>
            <input type="color" :value="config.textColor" @input="update('textColor', ($event.target as HTMLInputElement).value)" />
          </div>
          <div class="style-row">
            <label>文字背景</label>
            <input type="color" :value="rgbaToHex(config.textBgColor)" @input="update('textBgColor', hexToRgba(($event.target as HTMLInputElement).value))" />
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
/** 外框样式浮层：选中外框激活时显示，支持边框/填充/文字标签样式全配置 */

interface OuterFrameConfig {
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

const props = defineProps<{
  visible: boolean
  config: OuterFrameConfig
}>()

const emit = defineEmits<{
  (e: 'update', payload: Partial<OuterFrameConfig>): void
  (e: 'remove'): void
  (e: 'close'): void
}>()

/** 更新单个样式字段 */
function update<K extends keyof OuterFrameConfig>(key: K, value: OuterFrameConfig[K]) {
  emit('update', { [key]: value } as Partial<OuterFrameConfig>)
}

/** rgba(r,g,b,a) → #rrggbb（hex 不支持透明度，这里取 rgb 部分） */
function rgbaToHex(rgba: string): string {
  const m = rgba.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)/)
  if (!m) return '#ffffff'
  const toHex = (n: number) => n.toString(16).padStart(2, '0')
  return `#${toHex(Number(m[1]))}${toHex(Number(m[2]))}${toHex(Number(m[3]))}`
}

/** #rrggbb → rgba(r,g,b,0.05)（外框填充默认低透明度） */
function hexToRgba(hex: string): string {
  const r = parseInt(hex.slice(1, 3), 16)
  const g = parseInt(hex.slice(3, 5), 16)
  const b = parseInt(hex.slice(5, 7), 16)
  return `rgba(${r}, ${g}, ${b}, 0.05)`
}
</script>

<style scoped>
.outer-frame-style-panel {
  position: absolute;
  top: 60px;
  right: 16px;
  width: 260px;
  background: #fff;
  border-radius: 8px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.15);
  z-index: 1000;
  font-size: 12px;
  max-height: calc(100vh - 120px);
  overflow-y: auto;
}

.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 14px;
  border-bottom: 1px solid #eee;
}

.panel-title {
  font-weight: 600;
  color: #333;
}

.close-btn {
  background: none;
  border: none;
  font-size: 18px;
  color: #999;
  cursor: pointer;
  padding: 0 4px;
  line-height: 1;
}

.close-btn:hover {
  color: #333;
}

.panel-body {
  padding: 8px 14px 14px;
}

.style-group {
  padding: 8px 0;
  border-bottom: 1px solid #f0f0f0;
}

.style-group:last-of-type {
  border-bottom: none;
}

.group-label {
  font-weight: 600;
  color: #666;
  margin-bottom: 6px;
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.style-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 6px;
}

.style-row label {
  width: 60px;
  color: #666;
  flex-shrink: 0;
}

.style-row input[type="color"] {
  width: 32px;
  height: 24px;
  border: 1px solid #ddd;
  border-radius: 4px;
  cursor: pointer;
  padding: 0;
  background: none;
}

.style-row input[type="range"] {
  flex: 1;
  height: 4px;
  cursor: pointer;
  accent-color: #0984e3;
}

.value-text {
  width: 36px;
  text-align: right;
  color: #999;
  font-size: 11px;
  flex-shrink: 0;
}

.toggle-group {
  display: flex;
  gap: 4px;
}

.toggle-group button {
  padding: 3px 10px;
  border: 1px solid #ddd;
  border-radius: 4px;
  background: #fff;
  cursor: pointer;
  font-size: 11px;
  color: #666;
}

.toggle-group button.active {
  background: #0984e3;
  color: #fff;
  border-color: #0984e3;
}

.action-row {
  padding-top: 8px;
  display: flex;
  justify-content: flex-end;
}

.action-btn {
  padding: 6px 14px;
  border: 1px solid #ddd;
  border-radius: 4px;
  background: #fff;
  cursor: pointer;
  font-size: 12px;
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
    border-radius: 12px 12px 0 0;
    max-height: 60vh;
  }
}
</style>
