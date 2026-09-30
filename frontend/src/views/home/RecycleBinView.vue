<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import {
  NButton,
  NEmpty,
  NIcon,
  NInput,
  NModal,
  NPagination,
  NPopconfirm,
  NSelect,
  NSpace,
  NSpin,
  NTag,
  useMessage
} from 'naive-ui'
import {
  RefreshOutline,
  SearchOutline,
  SettingsOutline,
  TrashOutline
} from '@vicons/ionicons5'
import * as recycleApi from '@/api/recyclebin'
import type { RecycleBinItem, RecycleBinSettings } from '@/api/recyclebin'

const router = useRouter()
const message = useMessage()

// ====================== 列表 ======================
const items = ref<RecycleBinItem[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = 20
const loading = ref(false)
const keyword = ref('')
const settings = ref<RecycleBinSettings | null>(null)

const totalPages = computed(() => Math.max(1, Math.ceil(total.value / pageSize)))

async function load() {
  loading.value = true
  try {
    const res = await recycleApi.fetchRecycleBin({
      page: page.value,
      pageSize,
      keyword: keyword.value.trim() || undefined
    })
    items.value = res.items
    total.value = res.total
    page.value = res.page
  } catch (e) {
    message.error((e as Error).message)
  } finally {
    loading.value = false
  }
}

async function search() {
  page.value = 1
  await load()
}

async function gotoPage(p: number) {
  page.value = Math.min(Math.max(1, p), totalPages.value)
  await load()
}

function formatDateTime(iso: string): string {
  const d = new Date(iso)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
}

// ====================== 恢复 / 彻底删除 ======================
const restoringId = ref('')
const deletingId = ref('')

async function restore(item: RecycleBinItem) {
  restoringId.value = item.id
  try {
    await recycleApi.restoreFromRecycleBin(item.id)
    message.success(`已恢复「${item.title}」`)
    await load()
  } catch (e) {
    message.error((e as Error).message)
  } finally {
    restoringId.value = ''
  }
}

async function deleteForever(item: RecycleBinItem) {
  deletingId.value = item.id
  try {
    await recycleApi.deleteFromRecycleBin(item.id)
    message.success(`已彻底删除「${item.title}」`)
    if (items.value.length === 1 && page.value > 1) page.value -= 1
    await load()
  } catch (e) {
    message.error((e as Error).message)
  } finally {
    deletingId.value = ''
  }
}

// ====================== 保留天数设置 ======================
const settingsModalVisible = ref(false)
const settingsSaving = ref(false)
/** -1 表示「跟随默认」，其余为自定义天数（1–365） */
const FOLLOW_DEFAULT = -1
const retentionInput = ref<number>(FOLLOW_DEFAULT)

const retentionPresets = [7, 15, 30, 60, 90, 180, 365]
const retentionOptions = computed(() => {
  const opts: { label: string; value: number }[] = [
    { label: `跟随默认（${settings.value?.effectiveRetentionDays ?? 30} 天）`, value: FOLLOW_DEFAULT }
  ]
  for (const d of retentionPresets) {
    opts.push({ label: `${d} 天`, value: d })
  }
  return opts
})

function openSettings() {
  retentionInput.value = settings.value?.customRetentionDays ?? FOLLOW_DEFAULT
  settingsModalVisible.value = true
}

async function saveSettings() {
  const custom = retentionInput.value === FOLLOW_DEFAULT ? null : retentionInput.value
  if (custom !== null && (custom < 1 || custom > 365)) {
    message.warning('保留天数须在 1–365 之间')
    return
  }
  settingsSaving.value = true
  try {
    settings.value = await recycleApi.updateRecycleBinSettings(custom)
    message.success(
      custom === null
        ? `已恢复默认（${settings.value.effectiveRetentionDays} 天）`
        : `已设置为 ${custom} 天`
    )
    settingsModalVisible.value = false
    await load()
  } catch (e) {
    message.error((e as Error).message)
  } finally {
    settingsSaving.value = false
  }
}

function goHome() {
  router.push({ name: 'home' })
}

onMounted(async () => {
  await Promise.all([
    load(),
    recycleApi
      .fetchRecycleBinSettings()
      .then((s) => {
        settings.value = s
      })
      .catch((e) => message.error((e as Error).message))
  ])
})
</script>

<template>
  <div class="recycle-bin">
    <div class="page-header">
      <h1 class="page-title">回收站</h1>
      <div class="header-actions">
        <NButton size="small" @click="openSettings">
          <template #icon>
            <NIcon><SettingsOutline /></NIcon>
          </template>
          保留天数
        </NButton>
      </div>
    </div>

    <p class="page-desc">
      删除的导图将在此保留
      {{ settings ? `${settings.effectiveRetentionDays} 天` : '一段时间' }}，到期后自动彻底删除；恢复后回到原列表位置。
    </p>

    <div class="toolbar">
      <NInput
        v-model:value="keyword"
        placeholder="搜索回收站中的导图"
        clearable
        size="small"
        class="search-input"
        @keyup.enter="search"
        @clear="search"
      >
        <template #prefix>
          <NIcon><SearchOutline /></NIcon>
        </template>
      </NInput>
      <NButton size="small" type="primary" secondary @click="search">搜索</NButton>
    </div>

    <NSpin :show="loading">
      <div v-if="items.length > 0" class="item-list">
        <div v-for="item in items" :key="item.id" class="item-card">
          <div class="item-main">
            <div class="item-title-row">
              <span class="item-title">{{ item.title }}</span>
              <NTag
                size="small"
                :type="item.daysLeft <= 3 ? 'error' : item.daysLeft <= 7 ? 'warning' : 'default'"
                :bordered="false"
              >
                {{ item.daysLeft <= 0 ? '今天到期' : `剩 ${item.daysLeft} 天` }}
              </NTag>
            </div>
            <div v-if="item.description" class="item-desc">{{ item.description }}</div>
            <div class="item-meta">
              <span>{{ item.nodeCount }} 个节点</span>
              <span v-if="item.folderName">文件夹：{{ item.folderName }}</span>
              <span>删除于 {{ formatDateTime(item.deletedAt) }}</span>
            </div>
          </div>
          <div class="item-actions">
            <NButton
              size="small"
              type="primary"
              secondary
              :loading="restoringId === item.id"
              @click="restore(item)"
            >
              <template #icon>
                <NIcon><RefreshOutline /></NIcon>
              </template>
              恢复
            </NButton>
            <NPopconfirm @positive-click="deleteForever(item)">
              <template #trigger>
                <NButton
                  size="small"
                  type="error"
                  secondary
                  :loading="deletingId === item.id"
                >
                  <template #icon>
                    <NIcon><TrashOutline /></NIcon>
                  </template>
                  彻底删除
                </NButton>
              </template>
              彻底删除「{{ item.title }}」？此操作不可恢复，节点与版本将全部清除。
            </NPopconfirm>
          </div>
        </div>
      </div>
      <NEmpty
        v-else-if="!loading"
        description="回收站是空的"
        size="large"
        class="empty-state"
      >
        <template #extra>
          <NButton size="small" @click="goHome">返回导图列表</NButton>
        </template>
      </NEmpty>
      <div v-else class="empty-state"></div>
    </NSpin>

    <div v-if="total > pageSize" class="pagination-row">
      <NPagination
        :page="page"
        :page-count="totalPages"
        @update:page="gotoPage"
      />
    </div>

    <!-- 保留天数设置 Modal -->
    <NModal
      v-model:show="settingsModalVisible"
      preset="card"
      title="回收站保留天数"
      display-directive="if"
      style="max-width: 400px"
    >
      <div class="settings-body">
        <NSelect
          v-model:value="retentionInput"
          :options="retentionOptions"
          placeholder="选择保留天数"
        />
        <p class="settings-hint">
          缩短保留天数后，已超过新期限的内容会在下一次自动清理时被彻底删除。
        </p>
      </div>
      <template #footer>
        <NSpace justify="end">
          <NButton @click="settingsModalVisible = false">取消</NButton>
          <NButton type="primary" :loading="settingsSaving" @click="saveSettings">保存</NButton>
        </NSpace>
      </template>
    </NModal>
  </div>
</template>

<style scoped lang="scss">
.recycle-bin {
  padding: 20px;
  max-width: 960px;
  margin: 0 auto;
}

.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
}

.page-title {
  font-size: 18px;
  font-weight: 600;
  margin: 0;
}

.page-desc {
  font-size: 13px;
  color: var(--app-text-secondary, #666);
  margin: 8px 0 16px;
}

.toolbar {
  display: flex;
  gap: 8px;
  margin-bottom: 16px;

  .search-input {
    max-width: 320px;
  }
}

.item-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.item-card {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
  padding: 14px 16px;
  border-radius: 10px;
  background: var(--app-card-bg, #fff);
  border: 1px solid var(--app-border, rgba(0, 0, 0, 0.08));
}

.item-main {
  flex: 1;
  min-width: 220px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.item-title-row {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.item-title {
  font-size: 15px;
  font-weight: 500;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 100%;
}

.item-desc {
  font-size: 12px;
  color: var(--app-text-tertiary, #999);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.item-meta {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
  font-size: 12px;
  color: var(--app-text-secondary, #666);
}

.item-actions {
  display: flex;
  gap: 8px;
  flex-shrink: 0;
}

.empty-state {
  padding: 60px 0;
}

.pagination-row {
  display: flex;
  justify-content: center;
  margin-top: 20px;
}

.settings-body {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.settings-hint {
  font-size: 12px;
  color: var(--app-text-tertiary, #999);
  margin: 0;
}

@media (max-width: 767px) {
  .recycle-bin {
    padding: 12px;
  }

  .item-card {
    flex-direction: column;
    align-items: stretch;
  }

  .item-actions {
    justify-content: flex-end;
  }

  .toolbar .search-input {
    max-width: none;
    flex: 1;
  }
}
</style>
