<script setup lang="ts">
import { computed, h, onMounted, ref, watch } from 'vue'
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
  useDialog,
  type DataTableColumns
} from 'naive-ui'
import { useAdminStore } from '@/stores/admin'
import * as adminApi from '@/api/admin'

const adminStore = useAdminStore()
const message = useMessage()
const dialog = useDialog()

const keyword = ref('')
const scope = ref<'all' | 'public' | 'takenDown'>('all')
const page = ref(1)
const pageSize = ref(20)
const loading = ref(false)

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
  dialog.warning({
    title: '删除导图',
    content: `确认删除导图「${row.title}」？该操作将级联删除其所有节点、版本、分享与举报记录，且不可恢复。`,
    positiveText: '确认删除',
    negativeText: '取消',
    onPositiveClick: async () => {
      try {
        await adminApi.deleteAdminMindMap(row.id)
        message.success('导图已删除')
        await load()
      } catch (e) {
        message.error((e as Error).message)
      }
    }
  })
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
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

onMounted(load)
</script>

<template>
  <div class="admin-mindmaps">
    <div class="page-title">
      <h2>导图管理</h2>
      <span class="hint">共 {{ adminStore.mindMapsTotal }} 个导图</span>
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
        placeholder="搜索标题/描述"
        style="width: 240px"
        @keyup.enter="applySearch"
      />
      <NButton size="small" type="primary" @click="applySearch">搜索</NButton>
    </NSpace>

    <NDataTable
      :columns="columns"
      :data="adminStore.mindMaps"
      :loading="loading"
      :bordered="false"
      :single-line="false"
      size="small"
      :scroll-x="1100"
    />

    <div class="pagination-wrap">
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
</style>
