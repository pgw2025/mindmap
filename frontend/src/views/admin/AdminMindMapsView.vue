<script setup lang="ts">
import { computed, h, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import {
  NButton,
  NDataTable,
  NInput,
  NSelect,
  NSpace,
  NTag,
  NModal,
  NPagination,
  useMessage,
  type DataTableColumns
} from 'naive-ui'
import { useAdminStore } from '@/stores/admin'
import * as adminApi from '@/api/admin'
import AdminDetailSheet from '@/components/admin/AdminDetailSheet.vue'

const adminStore = useAdminStore()
const message = useMessage()
const router = useRouter()

const keyword = ref('')
const scope = ref<'all' | 'public' | 'takenDown'>('all')
const page = ref(1)
const pageSize = ref(20)
const loading = ref(false)

// 移动端筛选展开状态
const filterExpanded = ref(false)

// 删除导图确认弹窗
const mapDeleteModalVisible = ref(false)
const mapDeleteTarget = ref<adminApi.AdminMindMapListItem | null>(null)
const mapDeleteSubmitting = ref(false)

const scopeOptions = [
  { label: '全部导图', value: 'all' },
  { label: '公开', value: 'public' },
  { label: '已下架', value: 'takenDown' }
]

// 下架弹窗
const takeDownModalVisible = ref(false)
const takeDownTarget = ref<adminApi.AdminMindMapListItem | null>(null)
const takeDownReason = ref('')
const takeDownSubmitting = ref(false)

// 详情抽屉
const detailVisible = ref(false)
const detailMap = ref<adminApi.AdminMindMapListItem | null>(null)

// 移动端分页信息
const paginationInfo = computed(() => {
  const total = adminStore.mindMapsTotal
  const start = total === 0 ? 0 : (page.value - 1) * pageSize.value + 1
  const end = Math.min(page.value * pageSize.value, total)
  return { start, end, total }
})

const canPrevPage = computed(() => page.value > 1)
const canNextPage = computed(() => page.value * pageSize.value < adminStore.mindMapsTotal)

async function load(): Promise<void> {
  loading.value = true
  try {
    await adminStore.loadMindMaps({
      scope: scope.value,
      keyword: keyword.value.trim() || undefined,
      page: page.value,
      pageSize: pageSize.value
    })
  } catch (e) {
    message.error((e as Error).message)
  } finally {
    loading.value = false
  }
}

function applySearch(): void {
  page.value = 1
  load()
  filterExpanded.value = false
}

watch(scope, () => {
  page.value = 1
  load()
})

function openTakeDown(row: adminApi.AdminMindMapListItem): void {
  takeDownTarget.value = row
  takeDownReason.value = ''
  takeDownModalVisible.value = true
}

async function submitTakeDown(): Promise<void> {
  if (!takeDownTarget.value) return
  takeDownSubmitting.value = true
  try {
    await adminApi.takeDownMindMap(takeDownTarget.value.id, takeDownReason.value.trim() || undefined)
    message.success('导图已下架')
    takeDownModalVisible.value = false
    await load()
  } catch (e) {
    message.error((e as Error).message)
  } finally {
    takeDownSubmitting.value = false
  }
}

async function restore(row: adminApi.AdminMindMapListItem): Promise<void> {
  try {
    await adminApi.restoreMindMap(row.id)
    message.success('已恢复上架')
    await load()
  } catch (e) {
    message.error((e as Error).message)
  }
}

function confirmDelete(row: adminApi.AdminMindMapListItem): void {
  mapDeleteTarget.value = row
  mapDeleteModalVisible.value = true
}

async function submitMapDelete(): Promise<boolean> {
  if (!mapDeleteTarget.value) return true
  mapDeleteSubmitting.value = true
  try {
    await adminApi.deleteAdminMindMap(mapDeleteTarget.value.id)
    message.success('导图已删除')
    mapDeleteModalVisible.value = false
    await load()
    return true
  } catch (e) {
    message.error((e as Error).message)
    return false
  } finally {
    mapDeleteSubmitting.value = false
  }
}

function viewMindMap(row: adminApi.AdminMindMapListItem): void {
  router.push({ name: 'mindmap-edit', params: { id: row.id } })
}

function openDetail(map: adminApi.AdminMindMapListItem): void {
  detailMap.value = map
  detailVisible.value = true
}

function prevPage(): void {
  if (canPrevPage.value) {
    page.value--
    load()
  }
}

function nextPage(): void {
  if (canNextPage.value) {
    page.value++
    load()
  }
}

const columns = computed<DataTableColumns<adminApi.AdminMindMapListItem>>(() => [
  {
    title: '标题',
    key: 'title',
    minWidth: 200,
    ellipsis: { tooltip: true },
    render: (row) =>
      h('span', { style: row.isTakenDown ? 'text-decoration: line-through; opacity: 0.6' : '' }, row.title)
  },
  {
    title: '所有者',
    key: 'ownerName',
    minWidth: 120,
    ellipsis: { tooltip: true }
  },
  {
    title: '公开',
    key: 'isPublic',
    width: 80,
    render: (row) =>
      row.isPublic
        ? h(NTag, { type: 'success', size: 'small' }, () => '公开')
        : h(NTag, { type: 'default', size: 'small' }, () => '私有')
  },
  {
    title: '状态',
    key: 'isTakenDown',
    width: 100,
    render: (row) =>
      row.isTakenDown
        ? h(NTag, { type: 'error', size: 'small' }, () => '已下架')
        : h(NTag, { type: 'info', size: 'small' }, () => '正常')
  },
  {
    title: '节点数',
    key: 'nodeCount',
    width: 80,
    align: 'center'
  },
  {
    title: '下架原因',
    key: 'takenDownReason',
    minWidth: 160,
    ellipsis: { tooltip: true },
    render: (row) => row.takenDownReason ?? '—'
  },
  {
    title: '最后编辑',
    key: 'lastEditedAt',
    minWidth: 150,
    render: (row) => formatDate(row.lastEditedAt)
  },
  {
    title: '操作',
    key: 'actions',
    width: 240,
    fixed: 'right',
    render: (row) => {
      const buttons = [
        row.isTakenDown
          ? h(
              NButton,
              { size: 'tiny', quaternary: true, type: 'success', onClick: () => restore(row) },
              () => '恢复'
            )
          : h(
              NButton,
              { size: 'tiny', quaternary: true, type: 'warning', onClick: () => openTakeDown(row) },
              () => '下架'
            ),
        h(
          NButton,
          { size: 'tiny', quaternary: true, type: 'error', onClick: () => confirmDelete(row) },
          () => '删除'
        )
      ]
      return h(NSpace, { size: 4 }, () => buttons)
    }
  }
])

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString('zh-CN', { hour12: false, timeZone: 'Asia/Shanghai' })
}

function formatShortDate(iso: string): string {
  const d = new Date(iso)
  const now = new Date()
  const sameYear = d.getFullYear() === now.getFullYear()
  if (sameYear) {
    return `${d.getMonth() + 1}/${d.getDate()} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
  }
  return `${d.getFullYear()}/${d.getMonth() + 1}/${d.getDate()}`
}

onMounted(load)
</script>

<template>
  <div class="admin-mindmaps">
    <div class="page-title">
      <h2>导图管理</h2>
      <span class="hint">共 {{ adminStore.mindMapsTotal }} 个导图</span>
    </div>

    <!-- 桌面端筛选栏 -->
    <NSpace class="filter-bar desktop-only" align="center" :wrap="true" :size="8">
      <NSelect
        v-model:value="scope"
        :options="scopeOptions"
        size="small"
        style="width: 140px"
      />
      <NInput
        v-model:value="keyword"
        size="small"
        clearable
        placeholder="搜索标题/描述"
        style="width: 240px"
        @keyup.enter="applySearch"
      />
      <NButton size="small" type="primary" @click="applySearch">搜索</NButton>
    </NSpace>

    <!-- 移动端筛选栏 -->
    <div class="mobile-filter mobile-only">
      <div class="mobile-filter-main">
        <NInput
          v-model:value="keyword"
          size="medium"
          clearable
          placeholder="搜索标题/描述"
          @keyup.enter="applySearch"
        />
        <NButton size="medium" @click="filterExpanded = !filterExpanded">
          筛选
        </NButton>
      </div>
      <div v-if="filterExpanded" class="mobile-filter-extra">
        <NSelect
          v-model:value="scope"
          :options="scopeOptions"
          size="medium"
        />
        <NButton size="medium" type="primary" @click="applySearch">搜索</NButton>
      </div>
    </div>

    <!-- 桌面端表格 -->
    <div class="desktop-only">
      <NDataTable
        :columns="columns"
        :data="adminStore.mindMaps"
        :loading="loading"
        :bordered="false"
        :single-line="false"
        size="small"
        :scroll-x="1100"
      />
    </div>

    <!-- 桌面端分页 -->
    <div class="pagination-wrap desktop-only">
      <NPagination
        v-model:page="page"
        :page-size="pageSize"
        :item-count="adminStore.mindMapsTotal"
        :page-sizes="[10, 20, 50]"
        show-size-picker
        show-quick-jumper
        @update:page="load"
        @update:page-size="(s) => { pageSize = s; page = 1; load() }"
      />
    </div>

    <!-- 移动端卡片列表 -->
    <div class="mobile-card-list mobile-only">
      <div v-if="loading" class="mobile-loading">加载中...</div>
      <template v-else>
        <div v-if="adminStore.mindMaps.length === 0" class="mobile-empty">暂无数据</div>
        <div
          v-for="item in adminStore.mindMaps"
          :key="item.id"
          class="mindmap-card"
          @click="openDetail(item)"
        >
          <!-- 顶部：标题 + 状态标签 -->
          <div class="card-header">
            <span class="card-title" :class="{ 'is-taken-down': item.isTakenDown }">
              {{ item.title }}
            </span>
            <NTag
              :type="item.isTakenDown ? 'error' : 'info'"
              size="small"
              round
            >
              {{ item.isTakenDown ? '已下架' : '正常' }}
            </NTag>
          </div>

          <!-- 第二行：描述 -->
          <div v-if="item.description" class="card-desc">
            {{ item.description }}
          </div>
          <div v-else class="card-desc card-desc-empty">
            暂无描述
          </div>

          <!-- 第三行：所有者 + 节点数 + 最后编辑时间 -->
          <div class="card-meta">
            <span class="meta-item" title="所有者">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/>
                <circle cx="12" cy="7" r="4"/>
              </svg>
              {{ item.ownerName }}
            </span>
            <span class="meta-item" title="节点数">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="3" y="3" width="7" height="7" rx="1"/>
                <rect x="14" y="3" width="7" height="7" rx="1"/>
                <rect x="3" y="14" width="7" height="7" rx="1"/>
                <rect x="14" y="14" width="7" height="7" rx="1"/>
              </svg>
              {{ item.nodeCount }}
            </span>
            <span class="meta-item" title="最后编辑">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10"/>
                <polyline points="12 6 12 12 16 14"/>
              </svg>
              {{ formatShortDate(item.lastEditedAt) }}
            </span>
          </div>

          <!-- 底部操作区 -->
          <div class="card-actions">
            <NButton
              size="medium"
              type="primary"
              block
              @click.stop="viewMindMap(item)"
            >
              查看
            </NButton>
            <NButton
              v-if="!item.isTakenDown"
              size="medium"
              type="warning"
              block
              @click.stop="openTakeDown(item)"
            >
              下架
            </NButton>
            <NButton
              v-else
              size="medium"
              type="success"
              block
              @click.stop="restore(item)"
            >
              恢复
            </NButton>
          </div>
        </div>
      </template>
    </div>

    <!-- 移动端分页 -->
    <div class="mobile-pagination mobile-only">
      <NButton
        size="medium"
        :disabled="!canPrevPage"
        @click="prevPage"
      >
        上一页
      </NButton>
      <span class="mobile-pagination-info">
        第 {{ paginationInfo.start }}-{{ paginationInfo.end }} 条 / 共 {{ paginationInfo.total }} 条
      </span>
      <NButton
        size="medium"
        type="primary"
        :disabled="!canNextPage"
        @click="nextPage"
      >
        下一页
      </NButton>
    </div>

    <!-- 详情抽屉 -->
    <AdminDetailSheet
      v-model:show="detailVisible"
      title="导图详情"
    >
      <div v-if="detailMap">
        <!-- 标题 -->
        <div class="detail-title" :class="{ 'is-taken-down': detailMap.isTakenDown }">
          {{ detailMap.title }}
        </div>

        <!-- 基本信息 -->
        <div class="detail-section">
          <div class="detail-section-title">基本信息</div>
          <div class="detail-info-grid">
            <div class="detail-info-item">
              <span class="detail-info-label">所有者</span>
              <span class="detail-info-value">{{ detailMap.ownerName }}</span>
            </div>
            <div class="detail-info-item">
              <span class="detail-info-label">公开状态</span>
              <NTag :type="detailMap.isPublic ? 'success' : 'default'" size="small">
                {{ detailMap.isPublic ? '公开' : '私有' }}
              </NTag>
            </div>
            <div class="detail-info-item">
              <span class="detail-info-label">状态</span>
              <NTag :type="detailMap.isTakenDown ? 'error' : 'info'" size="small">
                {{ detailMap.isTakenDown ? '已下架' : '正常' }}
              </NTag>
            </div>
            <div class="detail-info-item">
              <span class="detail-info-label">节点数</span>
              <span class="detail-info-value">{{ detailMap.nodeCount }}</span>
            </div>
          </div>
        </div>

        <!-- 描述信息 -->
        <div class="detail-section">
          <div class="detail-section-title">描述</div>
          <div class="detail-desc">
            {{ detailMap.description || '暂无描述' }}
          </div>
        </div>

        <!-- 时间信息 -->
        <div class="detail-section">
          <div class="detail-section-title">时间信息</div>
          <div class="detail-info-grid">
            <div class="detail-info-item">
              <span class="detail-info-label">创建时间</span>
              <span class="detail-info-value">{{ formatDate(detailMap.createdAt) }}</span>
            </div>
            <div class="detail-info-item">
              <span class="detail-info-label">最后编辑</span>
              <span class="detail-info-value">{{ formatDate(detailMap.lastEditedAt) }}</span>
            </div>
          </div>
        </div>

        <!-- 下架原因 -->
        <div v-if="detailMap.isTakenDown" class="detail-section">
          <div class="detail-section-title">下架原因</div>
          <div class="detail-taken-down-reason">
            {{ detailMap.takenDownReason || '未填写' }}
          </div>
        </div>
      </div>

      <template #footer>
        <template v-if="detailMap">
          <NButton
            type="primary"
            block
            @click="viewMindMap(detailMap); detailVisible = false"
          >
            查看导图
          </NButton>
          <NButton
            v-if="!detailMap.isTakenDown"
            type="warning"
            block
            @click="openTakeDown(detailMap); detailVisible = false"
          >
            下架
          </NButton>
          <NButton
            v-else
            type="success"
            block
            @click="restore(detailMap); detailVisible = false"
          >
            恢复
          </NButton>
          <NButton
            type="error"
            block
            @click="confirmDelete(detailMap); detailVisible = false"
          >
            删除
          </NButton>
        </template>
      </template>
    </AdminDetailSheet>

    <!-- 下架弹窗 -->
    <NModal
      v-model:show="takeDownModalVisible"
      preset="card"
      title="下架导图"
      display-directive="if"
      style="max-width: 420px"
    >
      <p v-if="takeDownTarget" class="take-down-tip">
        导图「{{ takeDownTarget.title }}」下架后将从公开广场移除，所有分享链接将失效。
      </p>
      <NInput
        v-model:value="takeDownReason"
        type="textarea"
        placeholder="请输入下架原因（可选）"
        :rows="3"
        maxlength="256"
      />
      <template #footer>
        <NSpace justify="end">
          <NButton @click="takeDownModalVisible = false">取消</NButton>
          <NButton
            type="warning"
            :loading="takeDownSubmitting"
            @click="submitTakeDown"
          >
            确认下架
          </NButton>
        </NSpace>
      </template>
    </NModal>

    <!-- 删除导图确认弹窗 -->
    <NModal
      v-model:show="mapDeleteModalVisible"
      preset="card"
      title="删除导图"
      style="max-width: 460px"
      :bordered="false"
      size="medium"
    >
      <p style="margin: 0; color: #334155; line-height: 1.6;">
        确认删除导图「<b>{{ mapDeleteTarget?.title }}</b>」？
        该操作将级联删除其所有节点、版本、分享与举报记录，且不可恢复。
      </p>
      <template #footer>
        <div style="display: flex; justify-content: flex-end; gap: 10px;">
          <NButton size="small" @click="mapDeleteModalVisible = false">
            取消
          </NButton>
          <NButton type="error" size="small" :loading="mapDeleteSubmitting" @click="submitMapDelete">
            确认删除
          </NButton>
        </div>
      </template>
    </NModal>
  </div>
</template>

<style scoped lang="scss">
.admin-mindmaps {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.page-title {
  display: flex;
  align-items: baseline;
  gap: 12px;

  h2 {
    margin: 0;
    font-size: 20px;
    font-weight: 600;
  }

  .hint {
    font-size: 12px;
    color: var(--app-text-secondary);
    margin-left: auto;
  }
}

.filter-bar {
  flex-wrap: wrap;
}

.pagination-wrap {
  display: flex;
  justify-content: flex-end;
  margin-top: 8px;
}

.take-down-tip {
  margin: 0 0 8px;
  font-size: 13px;
  color: var(--app-text-secondary);
}

/* ========== 响应式显示控制 ========== */
.desktop-only {
  display: block;
}

.mobile-only {
  display: none;
}

@media screen and (max-width: 767px) {
  .desktop-only {
    display: none !important;
  }

  .mobile-only {
    display: block;
  }

  .admin-mindmaps {
    gap: 10px;
  }

  .page-title {
    h2 {
      font-size: 18px;
    }

    .hint {
      font-size: 11px;
    }
  }

  /* 移动端筛选栏 */
  .mobile-filter {
    display: flex;
    flex-direction: column;
    gap: 8px;

    .mobile-filter-main {
      display: flex;
      gap: 8px;
      align-items: center;

      :deep(.n-input) {
        flex: 1;
      }
    }

    .mobile-filter-extra {
      display: flex;
      gap: 8px;
      align-items: center;
      padding: 8px 12px;
      background: var(--app-bg);
      border-radius: 8px;
      animation: slideDown 0.2s ease-out;

      :deep(.n-select) {
        flex: 1;
      }
    }
  }

  @keyframes slideDown {
    from {
      opacity: 0;
      transform: translateY(-4px);
    }
    to {
      opacity: 1;
      transform: translateY(0);
    }
  }

  /* 移动端卡片列表 */
  .mobile-card-list {
    display: flex;
    flex-direction: column;
    gap: 10px;
  }

  .mobile-loading,
  .mobile-empty {
    text-align: center;
    padding: 40px 0;
    color: var(--app-text-secondary);
    font-size: 14px;
  }

  .mindmap-card {
    background: var(--app-card-bg);
    border-radius: 10px;
    padding: 14px;
    box-shadow: 0 1px 3px rgba(0, 0, 0, 0.06);
    border: 1px solid var(--app-border);
    display: flex;
    flex-direction: column;
    gap: 10px;
  }

  .card-header {
    display: flex;
    align-items: flex-start;
    justify-content: space-between;
    gap: 8px;
  }

  .card-title {
    font-size: 16px;
    font-weight: 600;
    color: var(--app-text-primary);
    line-height: 1.4;
    flex: 1;
    word-break: break-all;
    display: -webkit-box;
    -webkit-line-clamp: 2;
    -webkit-box-orient: vertical;
    overflow: hidden;

    &.is-taken-down {
      text-decoration: line-through;
      opacity: 0.6;
    }
  }

  .card-desc {
    font-size: 13px;
    color: var(--app-text-secondary, #94a3b8);
    line-height: 1.5;
    display: -webkit-box;
    -webkit-line-clamp: 2;
    -webkit-box-orient: vertical;
    overflow: hidden;

    &.card-desc-empty {
      color: var(--app-text-secondary);
      font-style: italic;
    }
  }

  .card-meta {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 12px;
    font-size: 12px;
    color: var(--app-text-secondary, #64748b);
  }

  .meta-item {
    display: inline-flex;
    align-items: center;
    gap: 4px;

    svg {
      width: 14px;
      height: 14px;
      flex-shrink: 0;
    }
  }

  .card-actions {
    display: flex;
    gap: 8px;
    padding-top: 4px;
    border-top: 1px solid var(--app-border);

    > * {
      flex: 1;
    }
  }

  /* 移动端分页 */
  .mobile-pagination {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 10px;
    padding: 8px 0;
  }

  .mobile-pagination-info {
    font-size: 12px;
    color: var(--app-text-secondary, #64748b);
    white-space: nowrap;
    flex-shrink: 0;
  }

  /* 详情抽屉样式 */
  .detail-title {
    font-size: 18px;
    font-weight: 600;
    color: var(--app-text-primary);
    line-height: 1.4;
    word-break: break-all;
    margin-bottom: 16px;

    &.is-taken-down {
      text-decoration: line-through;
      opacity: 0.6;
    }
  }

  .detail-section {
    margin-bottom: 16px;
  }

  .detail-section-title {
    font-size: 13px;
    font-weight: 600;
    color: var(--app-text-secondary, #64748b);
    margin-bottom: 8px;
  }

  .detail-info-grid {
    display: flex;
    flex-direction: column;
    gap: 8px;
    background: var(--app-bg);
    border-radius: 8px;
    padding: 10px 12px;
  }

  .detail-info-item {
    display: flex;
    align-items: center;
    justify-content: space-between;
    font-size: 14px;
  }

  .detail-info-label {
    color: var(--app-text-secondary, #64748b);
  }

  .detail-info-value {
    color: var(--app-text-primary);
    font-weight: 500;
  }

  .detail-desc {
    font-size: 14px;
    color: var(--app-text-primary);
    line-height: 1.6;
    background: var(--app-bg);
    border-radius: 8px;
    padding: 10px 12px;
    white-space: pre-wrap;
    word-break: break-all;
  }

  .detail-taken-down-reason {
    font-size: 14px;
    color: var(--app-text-primary);
    line-height: 1.6;
    background: rgba(239, 68, 68, 0.08);
    border-radius: 8px;
    padding: 10px 12px;
    white-space: pre-wrap;
    word-break: break-all;
  }
}
</style>
