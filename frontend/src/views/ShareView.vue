<script setup lang="ts">
import { ref, onMounted, computed, nextTick } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useMessage, NButton, NInput, NSpin, NResult, NCard, NTag, NModal } from 'naive-ui'
import MindMap from 'simple-mind-map'
import Search from 'simple-mind-map/src/plugins/Search.js'
import { verifyShare, fetchSharedMindMap } from '@/api/shares'
import * as mindmapsApi from '@/api/mindmaps'
import { useAuthStore } from '@/stores/auth'

/** 后端 NodeShape 数字 → simple-mind-map 形状字符串 */
const shapeMap: Record<number, string> = {
  0: 'rectangle',
  1: 'roundedRectangle',
  2: 'circle',
  3: 'ellipse',
  4: 'diamond',
  5: 'parallelogram'
}

/** 后端 EdgeStyle 数字 → simple-mind-map lineDasharray */
const edgeStyleMap: Record<number, string> = {
  0: 'none',
  1: '6,4',
  2: '2,2',
  3: 'none'
}

const route = useRoute()
const router = useRouter()
const message = useMessage()
const authStore = useAuthStore()

// 登录提示弹窗
const loginPromptVisible = ref(false)

const shareToken = computed(() => String(route.params.token || ''))

/** 验证通过后后端返回的分享信息 */
const allowCopy = ref(false)
const shareMeta = ref<{ mindMapId: string; title: string; ownerId: string; ownerName: string } | null>(null)
const nodesFlat = ref<any[]>([])
const sharedError = ref<string | null>(null)

const passwordNeeded = ref(false)
const passwordInput = ref('')
const verifying = ref(false)
const loading = ref(false)
const copyLoading = ref(false)

const shareVerified = ref(false)

const canvasEl = ref<HTMLDivElement | null>(null)
let mindMapInstance: MindMap | null = null

/** simple-mind-map 根节点布局方向（按后端 defaultLayout） */
const layoutConfig: Record<number, { direction: string }> = {
  0: { direction: 'left' },
  1: { direction: 'right' },
  2: { direction: 'top' },
  3: { direction: 'bottom' },
  4: { direction: 'radial' }
}

async function submitVerify(pwd?: string) {
  verifying.value = true
  sharedError.value = null
  try {
    const res = await verifyShare({ token: shareToken.value, password: pwd })
    if (!res.success) {
      if (res.needsPassword) {
        passwordNeeded.value = true
        return
      }
      sharedError.value = res.message || '分享链接无效'
      return
    }
    allowCopy.value = !!res.allowCopy
    shareMeta.value = res.mindMapId
      ? {
          mindMapId: res.mindMapId,
          title: res.title || '',
          ownerId: res.ownerId || '',
          ownerName: res.ownerName || ''
        }
      : null
    shareVerified.value = true
    passwordNeeded.value = false
    await loadMindMapData()
  } catch (e) {
    sharedError.value = (e as Error).message || '验证失败'
  } finally {
    verifying.value = false
  }
}

/** 后端节点树 → simple-mind-map 根对象 */
function convertNodeTreeToMindMap(nodes: any[]): any {
  if (!nodes || nodes.length === 0) return null
  const idMap = new Map<string, any>()
  let root: any = null
  for (const n of nodes) {
    const data: Record<string, any> = {
      text: n.icon ? `${n.icon} ${n.title}` : n.title,
      expand: !n.isCollapsed
    }
    if (n.color) data.color = n.color
    if (n.fontSize) data.fontSize = n.fontSize
    if (n.fontFamily) data.fontFamily = n.fontFamily
    if (n.backgroundColor) data.fillColor = n.backgroundColor
    if (n.borderColor) data.borderColor = n.borderColor
    if (n.shape != null && n.shape in shapeMap) data.shape = shapeMap[n.shape]
    if (n.edgeColor) data.lineColor = n.edgeColor
    if (n.edgeStyle != null && n.edgeStyle in edgeStyleMap) data.lineDasharray = edgeStyleMap[n.edgeStyle]
    if (n.note) data.note = n.note
    if (n.direction === 0) data.dir = 'left'
    else if (n.direction === 1) data.dir = 'right'
    const item = { id: n.id, data, children: [] as any[] }
    idMap.set(n.id, item)
    if (!n.parentId) root = item
  }
  for (const n of nodes) {
    if (n.parentId && idMap.has(n.parentId)) {
      idMap.get(n.parentId).children.push(idMap.get(n.id))
    }
  }
  return root
}

/** 递归展平节点（供搜索等功能使用） */
function flattenNodes(nodes: any[]): any[] {
  const list: any[] = []
  const walk = (arr: any[]) => {
    for (const n of arr) {
      list.push(n)
      if (n.children && n.children.length) walk(n.children)
    }
  }
  walk(nodes)
  return list
}

async function loadMindMapData() {
  loading.value = true
  try {
    const data = await fetchSharedMindMap(shareToken.value)
    const root = convertNodeTreeToMindMap(data.nodes)
    const flat = flattenNodes(data.nodes as any[])
    nodesFlat.value = flat
    document.title = `${shareMeta.value?.title || '分享的思维导图'} · 思维导图`

    await nextTick()
    const container = canvasEl.value
    if (!container) return

    await nextTick()
    await nextTick()

    const layout = layoutConfig[data.mindMap.defaultLayout] ?? layoutConfig[0]

    if (mindMapInstance) {
      mindMapInstance.destroy()
      mindMapInstance = null
    }

    mindMapInstance = new MindMap({
      el: container,
      layout: layout.direction,
      data: root || {
        data: { text: '（空思维导图）' },
        children: []
      }
    } as any)
    MindMap.usePlugin(Search)
    ;(mindMapInstance as any).on('node_active', () => {
      // 只读模式下不做处理
    })
  } catch (e) {
    sharedError.value = (e as Error).message || '加载导图失败'
  } finally {
    loading.value = false
  }
}

async function handleCopyToMine() {
  if (!shareMeta.value?.mindMapId) return
  if (!authStore.isAuthenticated) {
    loginPromptVisible.value = true
    return
  }
  copyLoading.value = true
  try {
    const created = await mindmapsApi.copyMindMap(shareMeta.value.mindMapId, `${shareMeta.value.title}（副本）`)
    message.success('已另存为副本')
    router.push(`/mindmaps/${created.id}/edit`)
  } catch (e) {
    message.error((e as Error).message || '另存失败')
  } finally {
    copyLoading.value = false
  }
}

function goLogin(): void {
  router.push({ name: 'login', query: { redirect: route.fullPath } })
}

function zoomIn() { mindMapInstance?.view?.enlarge() }
function zoomOut() { mindMapInstance?.view?.narrow() }
function zoomReset() { mindMapInstance?.view?.reset() }

onMounted(() => {
  submitVerify()
})
</script>

<template>
  <div class="share-page">
    <header class="share-header">
      <div class="share-brand">🧠 思维导图</div>
      <div v-if="shareMeta" class="share-info">
        <h1 class="share-title">{{ shareMeta.title }}</h1>
        <div class="share-owner">
          <NTag size="small" type="info">作者：{{ shareMeta.ownerName }}</NTag>
          <button
            v-if="allowCopy"
            class="copy-btn"
            :disabled="copyLoading"
            @click="handleCopyToMine"
          >
            {{ copyLoading ? '保存中...' : '另存为副本' }}
          </button>
        </div>
      </div>
    </header>

    <!-- 验证错误 -->
    <div v-if="sharedError" class="share-center">
      <NResult
        status="warning"
        title="无法访问"
        :description="sharedError"
      >
        <template #footer>
          <NButton type="primary" @click="router.push({ name: 'home' })">返回首页</NButton>
        </template>
      </NResult>
    </div>

    <!-- 输入密码 -->
    <div v-else-if="passwordNeeded" class="share-center">
      <NCard class="pwd-card" title="🔐 访问需要密码">
        <p style="margin:0 0 16px;font-size:13px;color:#666">分享链接设置了访问密码，请输入密码后查看。</p>
        <NInput
          v-model:value="passwordInput"
          type="password"
          show-password-on="click"
          placeholder="输入访问密码"
          @keyup.enter="submitVerify(passwordInput)"
        />
        <div style="margin-top:16px;display:flex;justify-content:flex-end">
          <NButton type="primary" :loading="verifying" @click="submitVerify(passwordInput)">
            验证并进入
          </NButton>
        </div>
      </NCard>
    </div>

    <!-- 加载中 -->
    <div v-else-if="loading || !shareVerified" class="share-center">
      <NSpin size="large" />
    </div>

    <!-- 画布 -->
    <section v-else class="share-canvas-section">
      <div class="share-canvas-toolbar">
        <button class="btn-canvas" @click="zoomOut" title="缩小">−</button>
        <button class="btn-canvas" @click="zoomReset" title="重置视图">◎</button>
        <button class="btn-canvas" @click="zoomIn" title="放大">+</button>
      </div>
      <div class="share-canvas" ref="canvasEl"></div>
    </section>

    <!-- 登录提示弹窗 -->
    <NModal
      v-model:show="loginPromptVisible"
      preset="dialog"
      type="warning"
      title="请先登录"
      positive-text="去登录"
      negative-text="取消"
      display-directive="if"
      style="max-width: 420px"
      @positive-click="goLogin"
    >
      需要登录才能另存为副本到你的账户。
    </NModal>
  </div>
</template>

<style scoped lang="scss">
.share-page {
  min-height: 100vh;
  background: var(--app-bg, #f5f7fa);
  display: flex;
  flex-direction: column;
}

.share-header {
  padding: 14px 24px;
  background: #fff;
  border-bottom: 1px solid var(--app-border, #e0e0e6);
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 10px;
}

.share-brand {
  font-size: 18px;
  font-weight: 700;
  color: var(--app-primary, #18a058);
}

.share-info {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 4px;
  max-width: 70%;
}

.share-title {
  margin: 0;
  font-size: 16px;
  color: var(--app-text-primary, #333);
  text-align: right;
}

.share-owner {
  display: flex;
  align-items: center;
  gap: 10px;
}

.copy-btn {
  padding: 5px 12px;
  border: 1px solid var(--app-primary, #18a058);
  background: #fff;
  color: var(--app-primary, #18a058);
  border-radius: 6px;
  font-size: 13px;
  cursor: pointer;

  &:hover:not(:disabled) {
    background: var(--app-primary, #18a058);
    color: #fff;
  }

  &:disabled {
    opacity: 0.6;
    cursor: not-allowed;
  }
}

.share-center {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 40px 20px;
}

.pwd-card {
  width: 400px;
  max-width: 92vw;
}

.share-canvas-section {
  flex: 1;
  position: relative;
  overflow: hidden;
}

.share-canvas-toolbar {
  position: absolute;
  top: 16px;
  right: 16px;
  z-index: 10;
  display: flex;
  flex-direction: column;
  gap: 4px;
  background: #fff;
  padding: 4px;
  border: 1px solid var(--app-border, #e0e0e6);
  border-radius: 6px;
  box-shadow: 0 2px 8px rgba(0,0,0,0.08);
}

.btn-canvas {
  width: 36px;
  height: 36px;
  border: none;
  background: transparent;
  font-size: 18px;
  cursor: pointer;
  border-radius: 4px;
  color: var(--app-text-primary, #333);

  &:hover {
    background: var(--app-hover-bg, #f0f0f0);
    color: var(--app-primary, #18a058);
  }
}

.share-canvas {
  width: 100%;
  height: calc(100vh - 78px);
  background: var(--app-bg, #f5f7fa);

  :deep(.mind-map) {
    width: 100%;
    height: 100%;
  }
}

@media (max-width: 767px) {
  .share-header {
    padding: 10px 14px;
  }

  .share-title {
    font-size: 14px;
    max-width: 200px;
  }

  .share-brand {
    font-size: 16px;
  }
}
</style>
