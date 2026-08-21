<script setup lang="ts">
import { ref, watch } from 'vue'
import {
  useMessage,
  NDrawer, NDrawerContent, NButton, NModal, NCard, NEmpty, NSpin, NTag,
  NPopconfirm, NInput, NSpace
} from 'naive-ui'
import { useVersionsStore } from '@/stores/versions'

const props = defineProps<{
  show: boolean
  mindMapId: string
  nodeCount: number
}>()

const emit = defineEmits<{
  'update:show': [boolean]
  'rollback': [versionId: string, versionNumber: number]
}>()

const message = useMessage()
const versionsStore = useVersionsStore()

const createVersionModalVisible = ref(false)
const createVersionRemark = ref('')
const creatingVersion = ref(false)
const rollingBackId = ref<string | null>(null)

/** 抽屉打开时自动加载版本列表 */
watch(() => props.show, async (v) => {
  if (!v) return
  try {
    await versionsStore.load(props.mindMapId, true)
  } catch (e) {
    message.error('加载版本历史失败')
  }
})

function openCreateVersion() {
  createVersionRemark.value = ''
  createVersionModalVisible.value = true
}

async function submitCreateVersion() {
  creatingVersion.value = true
  try {
    await versionsStore.create(props.mindMapId, {
      remark: createVersionRemark.value.trim() || undefined
    })
    message.success('版本已保存')
    createVersionModalVisible.value = false
    // 如果抽屉已打开，刷新列表
    if (props.show) {
      await versionsStore.load(props.mindMapId, true)
    }
  } catch (e) {
    message.error((e as Error).message || '保存失败')
  } finally {
    creatingVersion.value = false
  }
}

async function handleRollback(versionId: string, versionNumber: number) {
  rollingBackId.value = versionId
  try {
    await versionsStore.rollback(props.mindMapId, versionId)
    message.success(`已回滚到 V${versionNumber}`)
    emit('rollback', versionId, versionNumber)
    emit('update:show', false)
  } catch (e) {
    message.error((e as Error).message || '回滚失败')
  } finally {
    rollingBackId.value = null
  }
}

async function handleDeleteVersion(versionId: string) {
  try {
    await versionsStore.remove(props.mindMapId, versionId)
    message.success('已删除')
  } catch (e) {
    message.error((e as Error).message || '删除失败')
  }
}

function formatVersionTime(iso: string): string {
  const d = new Date(iso)
  const now = new Date()
  const diffMs = now.getTime() - d.getTime()
  const diffMins = Math.floor(diffMs / 60000)
  if (diffMins < 1) return '刚刚'
  if (diffMins < 60) return `${diffMins} 分钟前`
  const diffHours = Math.floor(diffMins / 60)
  if (diffHours < 24) return `${diffHours} 小时前`
  const diffDays = Math.floor(diffHours / 24)
  if (diffDays < 30) return `${diffDays} 天前`
  return d.toLocaleDateString('zh-CN', { timeZone: 'Asia/Shanghai' }) + ' ' + d.toLocaleTimeString('zh-CN', { hour12: false, timeZone: 'Asia/Shanghai' }).slice(0, 5)
}

/** 暴露给父组件：外部按钮直接打开"保存版本快照"弹窗 */
defineExpose({ openCreateVersion })
</script>

<template>
  <!-- 创建版本弹窗 -->
  <NModal v-model:show="createVersionModalVisible" preset="card" title="保存版本快照" style="width: 420px"
    :mask-closable="false">
    <div class="create-version-body">
      <p class="tip">当前节点数：<strong>{{ nodeCount }}</strong>，保存后可随时回滚到此状态。</p>
      <NInput v-model:value="createVersionRemark" type="textarea" placeholder="输入版本备注（可选），例如：完成需求分析阶段"
        :autosize="{ minRows: 3, maxRows: 5 }" maxlength="200" show-count />
    </div>
    <template #footer>
      <NSpace justify="end">
        <NButton @click="createVersionModalVisible = false">取消</NButton>
        <NButton type="primary" :loading="creatingVersion" @click="submitCreateVersion">
          {{ creatingVersion ? '保存中...' : '确认保存' }}
        </NButton>
      </NSpace>
    </template>
  </NModal>

  <!-- 版本历史抽屉 -->
  <NDrawer :show="show" @update:show="emit('update:show', $event)" :width="420" placement="right"
    display-directive="if" title="版本历史">
    <NDrawerContent>
      <template #header>
        <div class="version-drawer-header">
          <span>🕘 版本历史</span>
          <NButton size="small" type="primary" @click="openCreateVersion">+ 新建版本</NButton>
        </div>
      </template>
      <div class="versions-list-wrap">
        <NSpin v-if="versionsStore.loading" :show="true">
          <div style="height: 200px" />
        </NSpin>
        <template v-else>
          <NEmpty v-if="versionsStore.items.length === 0" description="暂无版本快照，点击右上角「新建版本」保存第一个快照" />
          <div v-else class="versions-list">
            <NCard v-for="v in versionsStore.items" :key="v.id" class="version-card" :bordered="true">
              <div class="version-card-header">
                <div class="version-title">
                  <NTag type="info" round>V{{ v.versionNumber }}</NTag>
                  <span class="version-note" v-if="v.remark">{{ v.remark }}</span>
                  <span class="version-note no-remark" v-else>（无备注）</span>
                </div>
                <div class="version-meta">
                  <span class="meta-item">📊 {{ v.nodeCount }} 节点</span>
                  <span class="meta-item">👤 {{ v.createdByName }}</span>
                  <span class="meta-item">⏰ {{ formatVersionTime(v.createdAt) }}</span>
                </div>
              </div>
              <div class="version-actions">
                <NPopconfirm @positive-click="handleRollback(v.id, v.versionNumber)" positive-text="确认回滚"
                  negative-text="取消">
                  <template #trigger>
                    <NButton size="small" type="warning" :loading="rollingBackId === v.id">
                      {{ rollingBackId === v.id ? '回滚中...' : '回滚到此版本' }}
                    </NButton>
                  </template>
                  确认回滚到 V{{ v.versionNumber }}？当前所有未保存的修改将丢失，且操作不可撤销。
                </NPopconfirm>
                <NPopconfirm @positive-click="handleDeleteVersion(v.id)" positive-text="删除" negative-text="取消">
                  <template #trigger>
                    <NButton size="small" type="error" quaternary>删除</NButton>
                  </template>
                  确认删除此版本快照？此操作不可撤销。
                </NPopconfirm>
              </div>
            </NCard>
          </div>
        </template>
      </div>
    </NDrawerContent>
  </NDrawer>
</template>

<style scoped lang="scss">
.version-drawer-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  padding-right: 12px;
  font-size: 16px;
  font-weight: 600;
  color: var(--app-text-primary, #333);
}

.versions-list-wrap {
  padding: 0 4px 16px;
}

.versions-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.version-card {
  transition: all 0.2s;

  &:hover {
    box-shadow: 0 2px 12px rgba(0, 0, 0, 0.08);
  }

  :deep(.n-card__content) {
    padding: 16px !important;
  }
}

.version-card-header {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-bottom: 12px;
}

.version-title {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 15px;
  font-weight: 600;
  color: var(--app-text-primary, #333);
}

.version-note {
  color: var(--app-text-primary, #333);
  font-weight: 500;

  &.no-remark {
    color: var(--app-text-secondary, #999);
    font-weight: 400;
    font-style: italic;
  }
}

.version-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  font-size: 12px;
  color: var(--app-text-secondary, #666);
}

.meta-item {
  display: flex;
  align-items: center;
  gap: 2px;
}

.version-actions {
  display: flex;
  gap: 8px;
  justify-content: flex-end;
}

.create-version-body {
  .tip {
    margin: 0 0 16px;
    font-size: 14px;
    color: var(--app-text-secondary, #666);

    strong {
      color: var(--app-primary, #18a058);
      font-size: 15px;
    }
  }
}
</style>
