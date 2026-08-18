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

const adminStore = useAdminStore()
const message = useMessage()

const keyword = ref('')
const scope = ref<'pending' | 'resolved' | 'all'>('pending')
const page = ref(1)
const pageSize = ref(20)
const loading = ref(false)

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
  load()
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
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

onMounted(load)
</script>

<template>
  <div class="admin-reports">
    <div class="page-title">
      <h2>举报审核</h2>
      <span class="hint">共 {{ adminStore.reportsTotal }} 条举报</span>
    </div>

    <NSpace class="filter-bar" align="center" :wrap="true" :size="8">
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

    <NDataTable
      :columns="columns"
      :data="adminStore.reports"
      :loading="loading"
      :bordered="false"
      :single-line="false"
      size="small"
      :scroll-x="1200"
    />

    <div class="pagination-wrap">
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

.filter-bar {
  flex-wrap: wrap;
}

.pagination-wrap {
  display: flex;
  justify-content: flex-end;
  margin-top: 8px;
}

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
</style>
