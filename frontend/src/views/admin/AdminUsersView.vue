<script setup lang="ts">
import { computed, onMounted, ref, watch, h } from 'vue'
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
import { useAuthStore } from '@/stores/auth'
import * as adminApi from '@/api/admin'

const adminStore = useAdminStore()
const authStore = useAuthStore()
const message = useMessage()

const keyword = ref('')
const scope = ref<'all' | 'active' | 'disabled' | 'admin'>('all')
const page = ref(1)
const pageSize = ref(20)
const loading = ref(false)

// 删除用户确认弹窗
const userDeleteModalVisible = ref(false)
const userDeleteTarget = ref<adminApi.AdminUserListItem | null>(null)
const userDeleteSubmitting = ref(false)

const currentUserId = computed(() => authStore.user?.id ?? '')

const scopeOptions = [
  { label: '全部用户', value: 'all' },
  { label: '正常', value: 'active' },
  { label: '已禁用', value: 'disabled' },
  { label: '管理员', value: 'admin' }
]

async function load(): Promise<void> {
  loading.value = true
  try {
    await adminStore.loadUsers({
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

async function toggleDisabled(row: adminApi.AdminUserListItem): Promise<void> {
  const target = row.status === 0 ? 1 : 0
  try {
    await adminApi.updateAdminUser(row.id, { status: target })
    message.success(target === 1 ? '已禁用账号' : '已启用账号')
    await load()
  } catch (e) {
    message.error((e as Error).message)
  }
}

async function toggleAdmin(row: adminApi.AdminUserListItem): Promise<void> {
  const target = !row.isAdmin
  try {
    await adminApi.updateAdminUser(row.id, { isAdmin: target })
    message.success(target ? '已设为管理员' : '已撤销管理员')
    await load()
  } catch (e) {
    message.error((e as Error).message)
  }
}

function confirmDelete(row: adminApi.AdminUserListItem): void {
  userDeleteTarget.value = row
  userDeleteModalVisible.value = true
}

async function submitUserDelete(): Promise<boolean> {
  if (!userDeleteTarget.value) return true
  userDeleteSubmitting.value = true
  try {
    await adminApi.deleteAdminUser(userDeleteTarget.value.id)
    message.success('用户已删除')
    userDeleteModalVisible.value = false
    await load()
    return true
  } catch (e) {
    message.error((e as Error).message)
    return false
  } finally {
    userDeleteSubmitting.value = false
  }
}

const columns = computed<DataTableColumns<adminApi.AdminUserListItem>>(() => [
  {
    title: '用户名',
    key: 'username',
    minWidth: 120,
    ellipsis: { tooltip: true }
  },
  {
    title: '邮箱',
    key: 'email',
    minWidth: 180,
    ellipsis: { tooltip: true }
  },
  {
    title: '角色',
    key: 'isAdmin',
    width: 100,
    render: (row) =>
      row.isAdmin
        ? h(NTag, { type: 'success', size: 'small' }, () => '管理员')
        : h(NTag, { type: 'default', size: 'small' }, () => '普通')
  },
  {
    title: '状态',
    key: 'status',
    width: 100,
    render: (row) =>
      row.status === 0
        ? h(NTag, { type: 'info', size: 'small' }, () => '正常')
        : h(NTag, { type: 'error', size: 'small' }, () => '禁用')
  },
  {
    title: '导图数',
    key: 'mindMapCount',
    width: 80,
    align: 'center'
  },
  {
    title: '最近登录',
    key: 'lastLoginAt',
    minWidth: 160,
    render: (row) => row.lastLoginAt ? formatDate(row.lastLoginAt) : '—'
  },
  {
    title: '注册时间',
    key: 'createdAt',
    minWidth: 160,
    render: (row) => formatDate(row.createdAt)
  },
  {
    title: '操作',
    key: 'actions',
    width: 240,
    fixed: 'right',
    render: (row) => {
      const isSelf = row.id === currentUserId.value
      const buttons = [
        h(
          NButton,
          {
            size: 'tiny',
            quaternary: true,
            type: row.status === 0 ? 'error' : 'success',
            disabled: isSelf,
            onClick: () => toggleDisabled(row)
          },
          () => row.status === 0 ? '禁用' : '启用'
        ),
        h(
          NButton,
          {
            size: 'tiny',
            quaternary: true,
            type: 'warning',
            disabled: isSelf,
            onClick: () => toggleAdmin(row)
          },
          () => row.isAdmin ? '撤销管理' : '设为管理'
        ),
        h(
          NButton,
          {
            size: 'tiny',
            quaternary: true,
            type: 'error',
            disabled: isSelf,
            onClick: () => confirmDelete(row)
          },
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
  <div class="admin-users">
    <div class="page-title">
      <h2>用户管理</h2>
      <span class="hint">共 {{ adminStore.usersTotal }} 个用户</span>
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
        placeholder="搜索用户名/邮箱"
        style="width: 240px"
        @keyup.enter="applySearch"
      />
      <NButton size="small" type="primary" @click="applySearch">搜索</NButton>
    </NSpace>

    <NDataTable
      :columns="columns"
      :data="adminStore.users"
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
        :item-count="adminStore.usersTotal"
        :page-sizes="[10, 20, 50]"
        show-size-picker
        show-quick-jumper
        @update:page="load"
        @update:page-size="(s) => { pageSize = s; page = 1; load() }"
      />
    </div>

    <!-- 删除用户确认弹窗 -->
    <NModal
      v-model:show="userDeleteModalVisible"
      preset="card"
      title="删除用户"
      style="max-width: 460px"
      :bordered="false"
      size="medium"
    >
      <p style="margin: 0; color: #334155; line-height: 1.6;">
        确认删除用户「<b>{{ userDeleteTarget?.username }}</b>」？
        该操作将级联删除其所有导图、节点、版本、分享与举报记录，且不可恢复。
      </p>
      <template #footer>
        <div style="display: flex; justify-content: flex-end; gap: 10px;">
          <NButton size="small" @click="userDeleteModalVisible = false">
            取消
          </NButton>
          <NButton type="error" size="small" :loading="userDeleteSubmitting" @click="submitUserDelete">
            确认删除
          </NButton>
        </div>
      </template>
    </NModal>
  </div>
</template>

<style scoped lang="scss">
.admin-users {
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
</style>
