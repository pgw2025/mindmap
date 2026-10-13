<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useMessage } from 'naive-ui'
import type { NodeDto } from '@/api/nodes'
import { uploadNodeImage, readImageSize, MAX_IMAGE_SIZE_BYTES } from '@/api/uploads'

/** 保存给画布的图片载荷；null 表示移除图片 */
export interface NodeImagePayload {
  url: string
  title?: string
  width?: number
  height?: number
}

/** 从节点 ExtraData 解析当前图片信息 */
export interface NodeImageInfo {
  url: string
  title: string
  width: number
  height: number
}

const props = defineProps<{
  show: boolean
  node: NodeDto | null
}>()

const emit = defineEmits<{
  (e: 'update:show', value: boolean): void
  (e: 'save', payload: NodeImagePayload | null): void
}>()

const message = useMessage()

/** 展示用最大宽度：超过则等比缩小（避免 custom 尺寸撑爆画布） */
const MAX_DISPLAY_WIDTH = 500

const url = ref('')
const title = ref('')
const width = ref(0)
const height = ref(0)
const lockRatio = ref(true)
const uploading = ref(false)

const hasImage = computed(() => !!url.value)

/** 当前宽高比（锁定比例时按它联动） */
function currentRatio(): number {
  return width.value > 0 && height.value > 0 ? width.value / height.value : 0
}

watch(
  () => props.show,
  (show) => {
    if (!show) return
    const info = parseNodeImage(props.node)
    url.value = info?.url ?? ''
    title.value = info?.title ?? ''
    width.value = info?.width ?? 0
    height.value = info?.height ?? 0
    lockRatio.value = true
  }
)

/** 解析节点 ExtraData 中的图片信息 */
function parseNodeImage(node: NodeDto | null): NodeImageInfo | null {
  if (!node?.extraData) return null
  try {
    const extra = JSON.parse(node.extraData)
    if (extra.image) {
      return {
        url: extra.image,
        title: extra.imageTitle ?? '',
        width: extra.imageSize?.width ?? 0,
        height: extra.imageSize?.height ?? 0
      }
    }
  } catch {
    // ExtraData 不是合法 JSON，忽略
  }
  return null
}

async function handleUpload(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  if (!file.type.startsWith('image/')) {
    message.warning('请选择图片文件')
    return
  }
  if (file.size > MAX_IMAGE_SIZE_BYTES) {
    message.warning('图片不能超过 5MB')
    return
  }
  uploading.value = true
  try {
    const res = await uploadNodeImage(file)
    // 后端探测失败（宽高为 0）时用浏览器读取兜底
    let w = res.width
    let h = res.height
    if (!w || !h) {
      const dims = await readImageSize(res.url)
      w = dims.width
      h = dims.height
    }
    // 超过展示上限等比缩小
    if (w > MAX_DISPLAY_WIDTH) {
      h = Math.round((h * MAX_DISPLAY_WIDTH) / w)
      w = MAX_DISPLAY_WIDTH
    }
    url.value = res.url
    width.value = w
    height.value = h
    lockRatio.value = true
    message.success('图片上传成功')
  } catch (err) {
    message.error((err as Error).message || '图片上传失败')
  } finally {
    uploading.value = false
  }
}

/** 宽度变化：锁定比例时联动高度 */
function onWidthChange() {
  if (lockRatio.value) {
    const ratio = currentRatio()
    if (ratio > 0 && width.value > 0) height.value = Math.round(width.value / ratio)
  }
}

/** 高度变化：锁定比例时联动宽度 */
function onHeightChange() {
  if (lockRatio.value) {
    const ratio = currentRatio()
    if (ratio > 0 && height.value > 0) width.value = Math.round(height.value * ratio)
  }
}

function handleSave() {
  if (!url.value) return
  emit('save', {
    url: url.value,
    title: title.value.trim(),
    width: width.value > 0 ? width.value : undefined,
    height: height.value > 0 ? height.value : undefined
  })
  emit('update:show', false)
}

function handleRemove() {
  emit('save', null)
  emit('update:show', false)
}
</script>

<template>
  <NModal :show="show" preset="card" title="节点图片" style="max-width: 460px" :bordered="false" size="medium"
    @update:show="(v: boolean) => emit('update:show', v)">
    <div class="img-modal">
      <!-- 上传区 -->
      <div v-if="!hasImage" class="upload-zone">
        <label class="upload-btn" :class="{ disabled: uploading }">
          <input type="file" accept="image/png,image/jpeg,image/gif,image/webp" @change="handleUpload" />
          <span v-if="uploading">上传中...</span>
          <span v-else>＋ 选择图片上传</span>
        </label>
        <p class="hint">支持 jpg / png / gif / webp，≤ 5MB</p>
      </div>

      <!-- 预览 + 编辑 -->
      <template v-else>
        <div class="preview">
          <img :src="url" :alt="title || '节点图片'" />
        </div>

        <div class="form-row">
          <span class="row-label">宽</span>
          <NInputNumber v-model:value="width" :min="1" :max="2000" size="small" style="width: 90px" @update:value="onWidthChange" />
          <span class="row-label">高</span>
          <NInputNumber v-model:value="height" :min="1" :max="2000" size="small" style="width: 90px" @update:value="onHeightChange" />
          <label class="ratio-lock">
            <NCheckbox v-model:checked="lockRatio" size="small" />
            <span>锁定比例</span>
          </label>
        </div>

        <div class="form-row">
          <span class="row-label">标题</span>
          <NInput v-model:value="title" size="small" placeholder="图片说明（鼠标悬停显示）" clearable />
        </div>
      </template>
    </div>

    <template #footer>
      <div class="img-modal-footer">
        <NButton v-if="hasImage" size="small" type="error" quaternary @click="handleRemove">移除图片</NButton>
        <div class="spacer"></div>
        <NButton size="small" @click="emit('update:show', false)">取消</NButton>
        <NButton size="small" type="primary" :disabled="!hasImage" @click="handleSave">保存</NButton>
      </div>
    </template>
  </NModal>
</template>

<style scoped lang="scss">
.img-modal {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 4px 0;
}

.upload-zone {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  padding: 24px 0;
}

.upload-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 8px 20px;
  border: 1px dashed var(--app-primary, #18a058);
  border-radius: 8px;
  color: var(--app-primary, #18a058);
  background: rgba(24, 160, 88, 0.06);
  font-size: 14px;
  cursor: pointer;
  transition: all 0.2s;

  &:hover {
    background: rgba(24, 160, 88, 0.12);
  }

  &.disabled {
    opacity: 0.6;
    cursor: not-allowed;
  }

  input {
    display: none;
  }
}

.hint {
  font-size: 12px;
  color: var(--app-text-tertiary, #999);
  margin: 0;
}

.preview {
  display: flex;
  justify-content: center;
  max-height: 220px;
  overflow: hidden;
  border-radius: 8px;
  border: 1px solid var(--app-border, #e0e0e6);
  background: var(--app-bg, #f5f7fa);

  img {
    max-width: 100%;
    max-height: 220px;
    object-fit: contain;
  }
}

.form-row {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  color: var(--app-text-secondary, #666);
}

.row-label {
  flex-shrink: 0;
  color: var(--app-text-secondary, #666);
}

.ratio-lock {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  margin-left: auto;
  cursor: pointer;
  color: var(--app-text-secondary, #666);
  font-size: 12px;
}

.img-modal-footer {
  display: flex;
  align-items: center;
  gap: 10px;

  .spacer {
    flex: 1;
  }
}

@media (max-width: 767px) {
  .form-row {
    flex-wrap: wrap;
  }

  .ratio-lock {
    margin-left: 0;
  }
}
</style>
