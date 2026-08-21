<script setup lang="ts">
import { ref, reactive, watch } from 'vue'
import {
  useMessage,
  NDrawer, NDrawerContent, NButton, NModal, NCard, NEmpty, NSpin, NTag,
  NPopconfirm, NCheckbox, NDatePicker, NInputNumber, NInput, NSpace
} from 'naive-ui'
import * as sharesApi from '@/api/shares'
import type { ShareDto, ShareCreatePayload } from '@/api/shares'

const props = defineProps<{
  show: boolean
  mindMapId: string
  isPublicDefault?: boolean
}>()

const emit = defineEmits<{
  'update:show': [boolean]
  'public-change': [boolean]
}>()

const message = useMessage()

const shareList = ref<ShareDto[]>([])
const shareLoading = ref(false)
const creatingShare = ref(false)
const createShareVisible = ref(false)
const newShare = reactive<ShareCreatePayload>({
  setPublic: false,
  password: '',
  expiresAt: null,
  maxAccessCount: null,
  allowCopy: true
})
const shareUrlCopied = ref<string | null>(null)

/** 抽屉打开时自动加载分享列表 */
watch(() => props.show, async (v) => {
  if (!v) return
  shareLoading.value = true
  try {
    shareList.value = await sharesApi.fetchShares(props.mindMapId)
  } catch (e) {
    message.error((e as Error).message || '获取分享列表失败')
  } finally {
    shareLoading.value = false
  }
})

function openCreateShare() {
  newShare.setPublic = props.isPublicDefault ?? false
  newShare.password = ''
  newShare.expiresAt = null
  newShare.maxAccessCount = null
  newShare.allowCopy = true
  createShareVisible.value = true
}

async function submitCreateShare() {
  creatingShare.value = true
  try {
    const payload: ShareCreatePayload = {
      setPublic: newShare.setPublic,
      allowCopy: newShare.allowCopy
    }
    if (newShare.password) payload.password = newShare.password
    if (newShare.expiresAt) payload.expiresAt = newShare.expiresAt
    if (newShare.maxAccessCount) payload.maxAccessCount = newShare.maxAccessCount
    const created = await sharesApi.createShare(props.mindMapId, payload)
    shareList.value.unshift(created)
    createShareVisible.value = false
    message.success('分享链接已创建')
    if (newShare.setPublic) {
      emit('public-change', true)
    }
    // 自动复制分享链接
    await copyShareUrl(created)
  } catch (e) {
    message.error((e as Error).message || '创建失败')
  } finally {
    creatingShare.value = false
  }
}

function buildShareUrl(share: ShareDto): string {
  return `${location.origin}/#/share/${share.shareToken}`
}

async function copyShareUrl(share: ShareDto) {
  const url = buildShareUrl(share)

  // 1. 优先尝试现代 Clipboard API（仅支持 HTTPS 或 localhost/127.0.0.1）
  if (navigator.clipboard && window.isSecureContext) {
    try {
      await navigator.clipboard.writeText(url)
      shareUrlCopied.value = share.id
      message.success('分享链接已复制到剪贴板')
      setTimeout(() => { shareUrlCopied.value = null }, 2000)
      return // 复制成功，提前退出
    } catch (err) {
      console.warn('Clipboard API 复制失败，正在尝试使用降级方案...', err)
    }
  }

  // 2. 降级方案：适用于 HTTP 协议、异步接口回调后手势失效等场景
  const textArea = document.createElement('textarea')
  textArea.value = url

  // 隐藏文本域，防止页面滚动或抖动
  textArea.style.position = 'fixed'
  textArea.style.top = '-9999px'
  textArea.style.left = '-9999px'

  document.body.appendChild(textArea)
  textArea.focus()
  textArea.select()

  try {
    const successful = document.execCommand('copy')
    textArea.remove()
    if (successful) {
      shareUrlCopied.value = share.id
      message.success('分享链接已复制到剪贴板')
      setTimeout(() => { shareUrlCopied.value = null }, 2000)
    } else {
      message.error('复制失败，请手动复制')
    }
  } catch (err) {
    console.error('降级复制方案失败:', err)
    textArea.remove()
    message.error('复制失败，请手动复制')
  }
}

async function handleDeleteShare(share: ShareDto) {
  try {
    await sharesApi.deleteShare(share.id)
    shareList.value = shareList.value.filter(s => s.id !== share.id)
    message.success('分享链接已删除')
  } catch (e) {
    message.error((e as Error).message || '删除失败')
  }
}

function formatShareTime(s: string | null | undefined): string {
  if (!s) return ''
  const d = new Date(s)
  return d.toLocaleDateString('zh-CN', { timeZone: 'Asia/Shanghai' }) + ' ' + d.toLocaleTimeString('zh-CN', { hour12: false, timeZone: 'Asia/Shanghai' }).slice(0, 5)
}

/** 暴露给父组件：在外部按钮直接打开"新建分享"弹窗 */
defineExpose({ openCreateShare })
</script>

<template>
  <!-- 分享抽屉 -->
  <NDrawer :show="show" @update:show="emit('update:show', $event)" placement="right" :width="420" auto-focus>
    <NDrawerContent closable>
      <template #header>
        <div class="share-drawer-header">
          <span>🔗 分享此思维导图</span>
          <NButton size="small" type="primary" @click="openCreateShare">
            + 新建分享
          </NButton>
        </div>
      </template>
      <div v-if="shareLoading" class="drawer-loading">
        <NSpin />
      </div>
      <template v-else-if="shareList.length === 0">
        <div class="drawer-empty">
          <NEmpty description="暂无分享链接，点击右上角「+ 新建分享」创建">
            <NButton type="primary" @click="openCreateShare">新建分享链接</NButton>
          </NEmpty>
        </div>
      </template>
      <div v-else class="share-list">
        <NCard v-for="share in shareList" :key="share.id" class="share-card" size="small">
          <div class="share-card-head">
            <div class="share-token-title">
              <NTag type="success" size="small" round>
                分享链接
              </NTag>
              <span class="share-token-code">{{ share.shareToken }}</span>
            </div>
            <div v-if="share.hasPassword" class="share-tags">
              <NTag size="small">🔐 密码</NTag>
            </div>
          </div>

          <div class="share-url-row">
            <code class="share-url">{{ buildShareUrl(share) }}</code>
            <NButton size="tiny" type="primary" quaternary @click="copyShareUrl(share)">
              {{ shareUrlCopied === share.id ? '✓ 已复制' : '复制' }}
            </NButton>
          </div>

          <div class="share-meta">
            <div class="meta-item">
              <span>📊 访问次数</span>
              <NTag size="small" type="info">
                {{ share.accessCount }}
                <template v-if="share.maxAccessCount">/{{ share.maxAccessCount }}</template>
              </NTag>
            </div>
            <div class="meta-item">
              <span>📝 另存为</span>
              <NTag size="small" :type="share.allowCopy ? 'success' : 'warning'">
                {{ share.allowCopy ? '允许' : '仅查看' }}
              </NTag>
            </div>
            <div v-if="share.expiresAt" class="meta-item">
              <span>⏰ 过期时间</span>
              <span class="meta-val">{{ formatShareTime(share.expiresAt) }}</span>
            </div>
            <div class="meta-item">
              <span>🕒 创建</span>
              <span class="meta-val">{{ formatShareTime(share.createdAt) }}</span>
            </div>
            <div v-if="share.lastAccessedAt" class="meta-item">
              <span>👀 最近访问</span>
              <span class="meta-val">{{ formatShareTime(share.lastAccessedAt) }}</span>
            </div>
          </div>

          <div class="share-card-actions">
            <NPopconfirm @positive-click="handleDeleteShare(share)">
              <template #trigger>
                <NButton size="small" type="error" quaternary>删除</NButton>
              </template>
              确认删除此分享链接？访问该链接的所有人将无法再查看导图。
            </NPopconfirm>
          </div>
        </NCard>
      </div>
    </NDrawerContent>
  </NDrawer>

  <!-- 新建分享弹窗 -->
  <NModal v-model:show="createShareVisible" preset="card" title="新建分享链接" style="width: 480px; max-width: 92vw">
    <div class="create-share-body">
      <div class="field-group">
        <label class="field-label">
          <span class="label-title">同时设为公开</span>
          <NCheckbox v-model:checked="newShare.setPublic">
            所有人可在首页「公开导图」列表中浏览
          </NCheckbox>
        </label>
      </div>
      <div class="field-group">
        <label class="field-label">访问密码（可选）</label>
        <NInput v-model:value="newShare.password" placeholder="留空则无需密码即可访问" maxlength="32"
          show-password-on="mousedown" type="password" />
      </div>
      <div class="field-group">
        <label class="field-label">过期时间（可选）</label>
        <NDatePicker v-model:value="(newShare.expiresAt as unknown) as number | null" type="datetime"
          placeholder="留空表示永不过期" style="width: 100%" :disabled-date="(t: number) => t < Date.now() - 86400000"
          value-format="yyyy-MM-dd HH:mm:ss" :allow-input="false" />
      </div>
      <div class="field-group">
        <label class="field-label">最大访问次数（可选）</label>
        <NInputNumber v-model:value="newShare.maxAccessCount" placeholder="留空表示不限次" :min="1" style="width: 100%" />
      </div>
      <div class="field-group">
        <label class="field-label">
          <span class="label-title">访问者权限</span>
          <NCheckbox v-model:checked="newShare.allowCopy">
            允许访问者另存为副本
          </NCheckbox>
        </label>
      </div>
    </div>
    <template #footer>
      <NSpace justify="end">
        <NButton @click="createShareVisible = false">取消</NButton>
        <NButton type="primary" :loading="creatingShare" @click="submitCreateShare">
          {{ creatingShare ? '创建中...' : '创建' }}
        </NButton>
      </NSpace>
    </template>
  </NModal>
</template>

<style scoped lang="scss">
.share-drawer-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  gap: 10px;
}

.drawer-loading,
.drawer-empty {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 40px 0;
}

.share-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.share-card-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
}

.share-token-title {
  display: flex;
  align-items: center;
  gap: 6px;
}

.share-token-code {
  font-size: 12px;
  color: var(--app-text-secondary, #666);
  font-family: monospace;
}

.share-tags {
  display: flex;
  gap: 4px;
}

.share-url-row {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 8px;
  background: var(--app-bg, #f5f7fa);
  border-radius: 6px;
  margin-bottom: 8px;
}

.share-url {
  flex: 1;
  font-size: 12px;
  color: var(--app-primary, #18a058);
  word-break: break-all;
  font-family: monospace;
  padding: 2px 4px;
  margin: 0;
}

.share-meta {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 12px;
  color: var(--app-text-secondary, #666);
  margin-bottom: 10px;
}

.share-meta .meta-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.meta-val {
  color: var(--app-text-primary, #333);
}

.share-card-actions {
  display: flex;
  justify-content: flex-end;
}

.create-share-body {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.field-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.field-label {
  font-size: 13px;
  font-weight: 600;
  color: var(--app-text-secondary, #666);
}

.label-title {
  display: block;
  margin-bottom: 6px;
  font-size: 13px;
  font-weight: 600;
  color: var(--app-text-secondary, #666);
}
</style>
