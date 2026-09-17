<script setup lang="ts">
import { computed, h, onMounted, ref, watch } from 'vue'
import {
  NButton,
  NDataTable,
  NInput,
  NModal,
  NSelect,
  NSpace,
  NTag,
  NPagination,
  useMessage,
  type DataTableColumns
} from 'naive-ui'
import { useAdminStore } from '@/stores/admin'
import * as adminApi from '@/api/admin'
import AdminDetailSheet from '@/components/admin/AdminDetailSheet.vue'
import SwipeCard from '@/components/admin/SwipeCard.vue'
import PullRefresh from '@/components/admin/PullRefresh.vue'

const adminStore = useAdminStore()
const message = useMessage()

const keyword = ref('')
const scope = ref<'pending' | 'resolved' | 'all'>('pending')
const page = ref(1)
const pageSize = ref(20)
const loading = ref(false)
const mobileFilterOpen = ref(false)

const scopeOptions = [
  { label: '待处理', value: 'pending' },
  { label: '已处理', value: 'resolved' },
  { label: '全部', value: 'all' }
]

// 处理弹窗
const resolveModalVisible = ref(false)
const resolveTarget = ref<adminApi.AdminReportListItem | null>(null)
const resolveTakeDown = ref(false)
const resolveNote = ref('')
const resolveSubmitting = ref(false)

// 详情抽屉
const detailVisible = ref(false)
const detailReport = ref<adminApi.AdminReportListItem | null>(null)

function openDetail(report: adminApi.AdminReportListItem): void {
  detailReport.value = report
  detailVisible.value = true
}

async function load(): Promise<void> {
  loading.value = true
  try {
    await adminStore.loadReports({
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
  mobileFilterOpen.value = false
  load()
}

async function onPullRefresh(): Promise<void> {
  page.value = 1
  await load()
}

watch(scope, () => {
  page.value = 1
  load()
})

function openResolve(row: adminApi.AdminReportListItem, takeDown: boolean): void {
  resolveTarget.value = row
  resolveTakeDown.value = takeDown
  resolveNote.value = ''
  resolveModalVisible.value = true
}

async function submitResolve(): Promise<void> {
  if (!resolveTarget.value) return
  resolveSubmitting.value = true
  try {
    await adminApi.resolveAdminReport(resolveTarget.value.id, {
      takeDown: resolveTakeDown.value,
      note: resolveNote.value.trim() || undefined
    })
    message.success(resolveTakeDown.value ? '已下架导图并处理举报' : '已驳回举报')
    resolveModalVisible.value = false
    detailVisible.value = false
    await load()
  } catch (e) {
    message.error((e as Error).message)
  } finally {
    resolveSubmitting.value = false
  }
}

function statusTag(status: number) {
  if (status === 0) return h(NTag, { type: 'warning', size: 'small' }, () => '待处理')
  if (status === 2) return h(NTag, { type: 'error', size: 'small' }, () => '已下架')
  return h(NTag, { type: 'default', size: 'small' }, () => '已驳回')
}

function getStatusText(status: number): string {
  if (status === 0) return '待处理'
  if (status === 2) return '已下架'
  return '已驳回'
}

function getStatusClass(status: number): string {
  if (status === 0) return 'status-pending'
  if (status === 2) return 'status-taken-down'
  return 'status-rejected'
}

const columns = computed<DataTableColumns<adminApi.AdminReportListItem>>(() => [
  {
    title: '导图标题',
    key: 'mindMapTitle',
    minWidth: 160,
    ellipsis: { tooltip: true }
  },
  {
    title: '导图所有者',
    key: 'mindMapOwnerName',
    minWidth: 120,
    ellipsis: { tooltip: true }
  },
  {
    title: '举报人',
    key: 'reporterName',
    minWidth: 120,
    render: (row) => row.reporterName ?? '—'
  },
  {
    title: '举报理由',
    key: 'reason',
    minWidth: 200,
    ellipsis: { tooltip: true }
  },
  {
    title: '状态',
    key: 'status',
    width: 100,
    render: (row) => statusTag(row.status)
  },
  {
    title: '处理备注',
    key: 'resolutionNote',
    minWidth: 160,
    ellipsis: { tooltip: true },
    render: (row) => row.resolutionNote ?? '—'
  },
  {
    title: '举报时间',
    key: 'createdAt',
    minWidth: 150,
    render: (row) => formatDate(row.createdAt)
  },
  {
    title: '操作',
    key: 'actions',
    width: 200,
    fixed: 'right',
    render: (row) => {
      if (row.status !== 0) {
        return h('span', { style: 'color: var(--app-text-secondary); font-size: 12px' }, '已处理')
      }
      return h(NSpace, { size: 4 }, () => [
        h(
          NButton,
          { size: 'tiny', quaternary: true, type: 'error', onClick: () => openResolve(row, true) },
          () => '下架处理'
        ),
        h(
          NButton,
          { size: 'tiny', quaternary: true, type: 'default', onClick: () => openResolve(row, false) },
          () => '驳回'
        )
      ])
    }
  }
])

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString('zh-CN', { hour12: false, timeZone: 'Asia/Shanghai' })
}

// 手机端分页计算
const mobilePageInfo = computed(() => {
  const total = adminStore.reportsTotal
  const currentPage = page.value
  const size = pageSize.value
  const start = total === 0 ? 0 : (currentPage - 1) * size + 1
  const end = Math.min(currentPage * size, total)
  return { start, end, total }
})

const canPrevPage = computed(() => page.value > 1)
const canNextPage = computed(() => page.value * pageSize.value < adminStore.reportsTotal)

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

function toggleMobileFilter(): void {
  mobileFilterOpen.value = !mobileFilterOpen.value
}

onMounted(load)
</script>

<template>
  <div class="admin-reports">
    <div class="page-title">
      <h2>举报审核</h2>
      <span class="hint">共 {{ adminStore.reportsTotal }} 条举报</span>
    </div>

    <!-- 桌面端筛选栏 -->
    <NSpace class="filter-bar filter-bar-desktop" align="center" :wrap="true" :size="8">
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
        placeholder="搜索理由/导图标题"
        style="width: 240px"
        @keyup.enter="applySearch"
      />
      <NButton size="small" type="primary" @click="applySearch">搜索</NButton>
    </NSpace>

    <!-- 手机端筛选栏 -->
    <div class="filter-bar-mobile">
      <div class="filter-mobile-top">
        <NInput
          v-model:value="keyword"
          size="medium"
          clearable
          placeholder="搜索理由/导图标题"
          @keyup.enter="applySearch"
        />
        <NButton size="medium" @click="toggleMobileFilter">筛选</NButton>
      </div>
      <div v-if="mobileFilterOpen" class="filter-mobile-expand">
        <NSelect
          v-model:value="scope"
          :options="scopeOptions"
          size="medium"
          style="width: 100%"
        />
        <NButton type="primary" block @click="applySearch">搜索</NButton>
      </div>
    </div>

    <!-- 桌面端表格 -->
    <NDataTable
      class="desktop-table"
      :columns="columns"
      :data="adminStore.reports"
      :loading="loading"
      :bordered="false"
      :single-line="false"
      size="small"
      :scroll-x="1200"
    />

    <!-- 手机端卡片列表 -->
    <PullRefresh @refresh="onPullRefresh" class="mobile-pull-refresh">
      <div class="mobile-card-list">
        <div v-if="loading && adminStore.reports.length === 0" class="mobile-loading">加载中...</div>
        <template v-else>
        <SwipeCard
          v-for="item in adminStore.reports"
          :key="item.id"
          :action-width="item.status === 0 ? 180 : 0"
          :disabled="item.status !== 0"
          class="swipe-report-card"
        >
          <div
            class="report-card"
            @click="openDetail(item)"
          >
            <div class="card-header">
              <span class="card-title">{{ item.mindMapTitle }}</span>
              <span :class="['card-status', getStatusClass(item.status)]">
                {{ getStatusText(item.status) }}
              </span>
            </div>
            <div class="card-reason">{{ item.reason }}</div>
            <div class="card-meta">
              <span class="meta-item">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="meta-icon">
                  <circle cx="12" cy="8" r="4"/>
                  <path d="M4 21v-2a8 8 0 0 1 16 0v2"/>
                </svg>
                {{ item.reporterName ?? '—' }}
              </span>
              <span class="meta-item">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="meta-icon">
                  <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/>
                  <circle cx="12" cy="7" r="4"/>
                </svg>
                {{ item.mindMapOwnerName }}
              </span>
              <span class="meta-item">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="meta-icon">
                  <circle cx="12" cy="12" r="10"/>
                  <polyline points="12 6 12 12 16 14"/>
                </svg>
                {{ formatDate(item.createdAt) }}
              </span>
            </div>
            <div v-if="item.status !== 0" class="card-resolved">
              已处理
            </div>
          </div>
          <template #actions>
            <button
              v-if="item.status === 0"
              class="swipe-btn swipe-btn-danger"
              @click.stop="openResolve(item, true)"
            >
              下架
            </button>
            <button
              v-if="item.status === 0"
              class="swipe-btn swipe-btn-default"
              @click.stop="openResolve(item, false)"
            >
              驳回
            </button>
          </template>
        </SwipeCard>
        <div v-if="adminStore.reports.length === 0" class="mobile-empty">暂无数据</div>
        </template>
      </div>

      <!-- 手机端分页 -->
      <div class="pagination-mobile">
        <div class="mobile-page-info">
          第 {{ mobilePageInfo.start }}-{{ mobilePageInfo.end }} 条 / 共 {{ mobilePageInfo.total }} 条
        </div>
        <div class="mobile-page-buttons">
          <NButton
            size="large"
            :disabled="!canPrevPage"
            @click="prevPage"
          >
            上一页
          </NButton>
          <NButton
            size="large"
            type="primary"
            :disabled="!canNextPage"
            @click="nextPage"
          >
            下一页
          </NButton>
        </div>
      </div>
    </PullRefresh>

    <!-- 桌面端分页 -->
    <div class="pagination-wrap pagination-desktop">
      <NPagination
        v-model:page="page"
        :page-size="pageSize"
        :item-count="adminStore.reportsTotal"
        :page-sizes="[10, 20, 50]"
        show-size-picker
        show-quick-jumper
        @update:page="load"
        @update:page-size="(s) => { pageSize = s; page = 1; load() }"
      />
    </div>

    <AdminDetailSheet
      v-model:show="detailVisible"
      :title="detailReport?.mindMapTitle"
      :height-ratio="0.8"
    >
      <div v-if="detailReport" class="detail-content">
        <!-- 举报信息 -->
        <div class="detail-section">
          <div class="detail-section-title">举报信息</div>
          <div class="detail-row">
            <span class="detail-label">举报理由</span>
            <span class="detail-value detail-reason-full">{{ detailReport.reason }}</span>
          </div>
          <div class="detail-row">
            <span class="detail-label">举报人</span>
            <span class="detail-value">{{ detailReport.reporterName ?? '—' }}</span>
          </div>
          <div class="detail-row">
            <span class="detail-label">举报时间</span>
            <span class="detail-value">{{ formatDate(detailReport.createdAt) }}</span>
          </div>
        </div>

        <!-- 导图信息 -->
        <div class="detail-section">
          <div class="detail-section-title">导图信息</div>
          <div class="detail-row">
            <span class="detail-label">导图所有者</span>
            <span class="detail-value">{{ detailReport.mindMapOwnerName }}</span>
          </div>
          <div class="detail-row">
            <span class="detail-label">导图状态</span>
            <span :class="['detail-value', 'detail-status', getStatusClass(detailReport.status)]">
              {{ getStatusText(detailReport.status) }}
            </span>
          </div>
        </div>

        <!-- 处理信息（已处理时显示） -->
        <div v-if="detailReport.status !== 0" class="detail-section">
          <div class="detail-section-title">处理信息</div>
          <div class="detail-row">
            <span class="detail-label">处理状态</span>
            <span :class="['detail-value', 'detail-status', getStatusClass(detailReport.status)]">
              {{ getStatusText(detailReport.status) }}
            </span>
          </div>
          <div class="detail-row">
            <span class="detail-label">处理备注</span>
            <span class="detail-value">{{ detailReport.resolutionNote ?? '—' }}</span>
          </div>
          <div class="detail-row">
            <span class="detail-label">处理时间</span>
            <span class="detail-value">{{ detailReport.resolvedAt ? formatDate(detailReport.resolvedAt) : '—' }}</span>
          </div>
        </div>
      </div>

      <template #footer>
        <template v-if="detailReport && detailReport.status === 0">
          <NButton type="error" size="large" block @click="openResolve(detailReport, true)">
            下架处理
          </NButton>
          <NButton size="large" block @click="openResolve(detailReport, false)">
            驳回
          </NButton>
        </template>
        <div v-else class="detail-resolved-tip">
          已处理
        </div>
      </template>
    </AdminDetailSheet>

    <!-- 处理举报弹窗 -->
    <NModal
      v-model:show="resolveModalVisible"
      preset="card"
      :title="resolveTakeDown ? '下架处理' : '驳回举报'"
      display-directive="if"
      style="max-width: 460px"
    >
      <div v-if="resolveTarget" class="resolve-body">
        <div class="resolve-row">
          <span class="label">导图</span>
          <span>{{ resolveTarget.mindMapTitle }}</span>
        </div>
        <div class="resolve-row">
          <span class="label">所有者</span>
          <span>{{ resolveTarget.mindMapOwnerName }}</span>
        </div>
        <div class="resolve-row">
          <span class="label">举报理由</span>
          <span>{{ resolveTarget.reason }}</span>
        </div>
        <div v-if="resolveTakeDown" class="resolve-warning">
          下架后该导图将从公开广场移除，所有分享链接失效。
        </div>
        <NInput
          v-model:value="resolveNote"
          type="textarea"
          placeholder="处理备注（可选）"
          :rows="3"
          maxlength="512"
        />
      </div>
      <template #footer>
        <NSpace justify="end">
          <NButton @click="resolveModalVisible = false">取消</NButton>
          <NButton
            :type="resolveTakeDown ? 'error' : 'default'"
            :loading="resolveSubmitting"
            @click="submitResolve"
          >
            {{ resolveTakeDown ? '确认下架' : '确认驳回' }}
          </NButton>
        </NSpace>
      </template>
    </NModal>
  </div>
</template>

<style scoped lang="scss">
.admin-reports {
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

/* 筛选栏 */
.filter-bar {
  flex-wrap: wrap;
}

.filter-bar-mobile {
  display: none;
}

/* 桌面端表格 */
.desktop-table {
  display: block;
}

/* 手机端卡片列表 */
.mobile-card-list {
  display: none;
}

/* 分页 */
.pagination-wrap {
  display: flex;
  justify-content: flex-end;
  margin-top: 8px;
}

.pagination-mobile {
  display: none;
}

/* 弹窗样式 */
.resolve-body {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.resolve-row {
  display: flex;
  gap: 12px;
  font-size: 13px;

  .label {
    width: 70px;
    flex-shrink: 0;
    color: var(--app-text-secondary);
  text-align: right;
  }
}

.resolve-warning {
  padding: 8px 12px;
  border-radius: 4px;
  background: rgba(208, 48, 80, 0.1);
  color: #d03050;
  font-size: 12px;
}

/* 详情抽屉内容 */
.detail-content {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.detail-section {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.detail-section-title {
  font-size: 15px;
  font-weight: 600;
  color: var(--app-text-primary);
  padding-bottom: 6px;
  border-bottom: 1px solid var(--app-border);
}

.detail-row {
  display: flex;
  gap: 12px;
  font-size: 14px;
  line-height: 1.5;
}

.detail-label {
  width: 80px;
  flex-shrink: 0;
  color: var(--app-text-secondary);
}

.detail-value {
  flex: 1;
  color: var(--app-text-primary);
  word-break: break-all;
}

.detail-reason-full {
  white-space: pre-wrap;
}

.detail-status {
  display: inline-block;
  padding: 2px 10px;
  border-radius: 4px;
  font-size: 13px;
  font-weight: 500;
}

.detail-resolved-tip {
  flex: 1;
  text-align: center;
  padding: 10px 0;
  color: var(--app-text-secondary, #999);
  font-size: 14px;
}

/* ========== 手机端适配 ========== */
@media (max-width: 767px) {
  .admin-reports {
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

  /* 隐藏桌面端筛选和表格 */
  .filter-bar-desktop,
  .desktop-table,
  .pagination-desktop {
    display: none !important;
  }

  /* 显示手机端筛选 */
  .filter-bar-mobile {
    display: flex;
    flex-direction: column;
    gap: 8px;

    .filter-mobile-top {
      display: flex;
      gap: 8px;
      align-items: center;

      :deep(.n-input) {
        flex: 1;
      }
    }

    .filter-mobile-expand {
      display: flex;
      flex-direction: column;
      gap: 8px;
      animation: slideDown 0.2s ease-out;
    }
  }

  @keyframes slideDown {
    from {
      opacity: 0;
      transform: translateY(-8px);
    }
    to {
      opacity: 1;
      transform: translateY(0);
    }
  }

  /* 显示手机端卡片列表 */
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

  .report-card {
    background: var(--app-card-bg, #fff);
    border-radius: 8px;
    padding: 12px;
    box-shadow: 0 1px 3px rgba(0, 0, 0, 0.08);
    display: flex;
    flex-direction: column;
    gap: 8px;
    cursor: pointer;
    transition: background 0.15s ease;

    &:active {
      background: var(--app-border, #f0f0f0);
    }
  }

  .card-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
  }

  .card-title {
    font-size: 16px;
    font-weight: 600;
    color: var(--app-text-primary);
    flex: 1;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .card-status {
    flex-shrink: 0;
    font-size: 12px;
    padding: 2px 8px;
    border-radius: 4px;
    font-weight: 500;
  }

  .status-pending {
    background: rgba(208, 48, 80, 0.1);
    color: #d03050;
  }

  .status-taken-down {
    background: rgba(208, 48, 80, 0.1);
    color: #d03050;
  }

  .status-rejected {
    background: rgba(128, 128, 128, 0.15);
    color: #888;
  }

  .card-reason {
    font-size: 13px;
    color: var(--app-text-secondary, #888);
    line-height: 1.5;
    display: -webkit-box;
    -webkit-line-clamp: 2;
    -webkit-box-orient: vertical;
    overflow: hidden;
  }

  .card-meta {
    display: flex;
    flex-wrap: wrap;
    gap: 6px 12px;
    font-size: 12px;
    color: var(--app-text-secondary, #888);
  }

  .meta-item {
    display: flex;
    align-items: center;
    gap: 4px;
  }

  .meta-icon {
    width: 14px;
    height: 14px;
    flex-shrink: 0;
  }

  .card-actions {
    display: flex;
    gap: 8px;
    margin-top: 4px;

    .n-button {
      flex: 1;
    }
  }

  .card-resolved {
    text-align: center;
    padding: 8px 0;
    color: var(--app-text-secondary, #999);
    font-size: 13px;
    border-top: 1px solid var(--app-border);
    margin-top: 4px;
  }

  /* 左滑操作按钮 */
  .swipe-report-card {
    margin-bottom: 10px;
    border-radius: 10px;
  }

  .swipe-btn {
    flex: 1;
    border: none;
    color: white;
    font-size: 14px;
    font-weight: 500;
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    transition: opacity 0.15s ease;

    &:active {
      opacity: 0.8;
    }
  }

  .swipe-btn-danger {
    background: #ef4444;
  }

  .swipe-btn-default {
    background: #6b7280;
  }

  /* 手机端分页 */
  .pagination-mobile {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 8px;
    padding: 8px 0;
  }

  .mobile-page-info {
    font-size: 13px;
    color: var(--app-text-secondary);
  }

  .mobile-page-buttons {
    display: flex;
    gap: 12px;
    width: 100%;

    .n-button {
      flex: 1;
    }
  }
}
</style>
