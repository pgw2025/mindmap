<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch, h } from 'vue'
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
import AdminDetailSheet from '@/components/admin/AdminDetailSheet.vue'

const adminStore = useAdminStore()
const authStore = useAuthStore()
const message = useMessage()

const keyword = ref('')
const scope = ref<'all' | 'active' | 'disabled' | 'admin'>('all')
const page = ref(1)
const pageSize = ref(20)
const loading = ref(false)

// 响应式判断
const isMobile = ref(false)
const MOBILE_BREAKPOINT = 767

function checkMobile(): void {
  isMobile.value = window.innerWidth <= MOBILE_BREAKPOINT
}

let resizeTimer: ReturnType<typeof setTimeout> | null = null
function handleResize(): void {
  if (resizeTimer) clearTimeout(resizeTimer)
  resizeTimer = setTimeout(checkMobile, 100)
}

// 手机端筛选展开状态
const filterExpanded = ref(false)

function toggleFilter(): void {
  filterExpanded.value = !filterExpanded.value
}

// 删除用户确认弹窗
const userDeleteModalVisible = ref(false)
const userDeleteTarget = ref<adminApi.AdminUserListItem | null>(null)
const userDeleteSubmitting = ref(false)

// 详情抽屉
const detailVisible = ref(false)
const detailUser = ref<adminApi.AdminUserListItem | null>(null)

function openDetail(user: adminApi.AdminUserListItem): void {
  detailUser.value = user
  detailVisible.value = true
}

const currentUserId = computed(() => authStore.user?.id ?? '')

const scopeOptions = [
  { label: '全部用户', value: 'all' },
  { label: '正常', value: 'active' },
  { label: '已禁用', value: 'disabled' },
  { label: '管理员', value: 'admin' }
]

// 分页计算
const totalPages = computed(() => Math.max(1, Math.ceil(adminStore.usersTotal / pageSize.value)))
const startItem = computed(() => (page.value - 1) * pageSize.value + 1)
const endItem = computed(() => Math.min(page.value * pageSize.value, adminStore.usersTotal))

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
  filterExpanded.value = false
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

// 手机端分页
function prevPage(): void {
  if (page.value > 1) {
    page.value--
    load()
  }
}

function nextPage(): void {
  if (page.value < totalPages.value) {
    page.value++
    load()
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
  return new Date(iso).toLocaleString('zh-CN', { hour12: false, timeZone: 'Asia/Shanghai' })
}

function formatShortDate(iso: string): string {
  return new Date(iso).toLocaleDateString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    timeZone: 'Asia/Shanghai'
  })
}

onMounted(() => {
  checkMobile()
  window.addEventListener('resize', handleResize)
  load()
})

onUnmounted(() => {
  window.removeEventListener('resize', handleResize)
  if (resizeTimer) clearTimeout(resizeTimer)
})
</script>

<template>
  <div class="admin-users">
    <div class="page-title">
      <h2>用户管理</h2>
      <span class="hint">共 {{ adminStore.usersTotal }} 个用户</span>
    </div>

    <!-- 桌面端筛选栏 -->
    <NSpace v-if="!isMobile" class="filter-bar" align="center" :wrap="true" :size="8">
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

    <!-- 手机端筛选栏 -->
    <div v-else class="mobile-filter-bar">
      <div class="mobile-filter-main">
        <NInput
          v-model:value="keyword"
          size="medium"
          clearable
          placeholder="搜索用户名/邮箱"
          class="mobile-search-input"
          @keyup.enter="applySearch"
        />
        <NButton size="medium" type="default" @click="toggleFilter" class="filter-toggle-btn">
          筛选
        </NButton>
      </div>
      <div v-show="filterExpanded" class="mobile-filter-extra">
        <NSelect
          v-model:value="scope"
          :options="scopeOptions"
          size="medium"
          class="mobile-scope-select"
        />
        <NButton size="medium" type="primary" @click="applySearch" class="mobile-search-btn">
          搜索
        </NButton>
      </div>
    </div>

    <!-- 桌面端表格 -->
    <NDataTable
      v-if="!isMobile"
      :columns="columns"
      :data="adminStore.users"
      :loading="loading"
      :bordered="false"
      :single-line="false"
      size="small"
      :scroll-x="1100"
    />

    <!-- 手机端卡片列表 -->
    <div v-else class="mobile-card-list">
      <div v-if="loading" class="mobile-loading">加载中...</div>
      <template v-else>
        <div v-if="adminStore.users.length === 0" class="mobile-empty">暂无数据</div>
        <div
          v-for="user in adminStore.users"
          :key="user.id"
          class="user-card"
          @click="openDetail(user)"
        >
          <!-- 顶部：用户名 + 状态标签 -->
          <div class="card-header">
            <span class="card-username">{{ user.username }}</span>
            <NTag :type="user.status === 0 ? 'info' : 'error'" size="small" round>
              {{ user.status === 0 ? '正常' : '禁用' }}
            </NTag>
          </div>
          <!-- 第二行：邮箱 -->
          <div class="card-email">{{ user.email }}</div>
          <!-- 第三行：角色 + 导图数 + 注册时间 -->
          <div class="card-meta">
            <NTag :type="user.isAdmin ? 'success' : 'default'" size="small">
              {{ user.isAdmin ? '管理员' : '普通' }}
            </NTag>
            <span class="meta-item">
              <svg class="meta-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                <polyline points="14 2 14 8 20 8"></polyline>
              </svg>
              {{ user.mindMapCount }}
            </span>
            <span class="meta-item">
              <svg class="meta-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="3" y="4" width="18" height="18" rx="2" ry="2"></rect>
                <line x1="16" y1="2" x2="16" y2="6"></line>
                <line x1="8" y1="2" x2="8" y2="6"></line>
                <line x1="3" y1="10" x2="21" y2="10"></line>
              </svg>
              {{ formatShortDate(user.createdAt) }}
            </span>
          </div>
          <!-- 底部操作区 -->
          <div class="card-actions" @click.stop>
            <NButton
              size="medium"
              type="default"
              dashed
              class="card-action-btn"
              @click.stop="toggleAdmin(user)"
              :disabled="user.id === currentUserId"
            >
              {{ user.isAdmin ? '撤销管理' : '设为管理' }}
            </NButton>
            <NButton
              size="medium"
              :type="user.status === 0 ? 'error' : 'success'"
              class="card-action-btn"
              @click.stop="toggleDisabled(user)"
              :disabled="user.id === currentUserId"
            >
              {{ user.status === 0 ? '禁用' : '启用' }}
            </NButton>
          </div>
        </div>
      </template>
    </div>

    <!-- 桌面端分页 -->
    <div v-if="!isMobile" class="pagination-wrap">
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

    <!-- 手机端分页 -->
    <div v-else class="mobile-pagination">
      <NButton
        size="large"
        type="default"
        class="mobile-page-btn"
        :disabled="page <= 1 || loading"
        @click="prevPage"
      >
        上一页
      </NButton>
      <span class="mobile-page-info">
        第 {{ startItem }}-{{ endItem }} 条 / 共 {{ adminStore.usersTotal }} 条
      </span>
      <NButton
        size="large"
        type="default"
        class="mobile-page-btn"
        :disabled="page >= totalPages || loading"
        @click="nextPage"
      >
        下一页
      </NButton>
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

    <!-- 用户详情抽屉 -->
    <AdminDetailSheet
      v-model:show="detailVisible"
      :title="detailUser?.username ?? ''"
    >
      <template v-if="detailUser">
        <!-- 用户基本信息 -->
        <div class="detail-section">
          <div class="detail-section-title">基本信息</div>
          <div class="detail-info-list">
            <div class="detail-info-item">
              <span class="detail-info-label">用户名</span>
              <span class="detail-info-value">{{ detailUser.username }}</span>
            </div>
            <div class="detail-info-item">
              <span class="detail-info-label">邮箱</span>
              <span class="detail-info-value">{{ detailUser.email }}</span>
            </div>
            <div class="detail-info-item">
              <span class="detail-info-label">角色</span>
              <NTag :type="detailUser.isAdmin ? 'success' : 'default'" size="small">
                {{ detailUser.isAdmin ? '管理员' : '普通' }}
              </NTag>
            </div>
            <div class="detail-info-item">
              <span class="detail-info-label">状态</span>
              <NTag :type="detailUser.status === 0 ? 'info' : 'error'" size="small">
                {{ detailUser.status === 0 ? '正常' : '禁用' }}
              </NTag>
            </div>
          </div>
        </div>

        <!-- 数据统计 -->
        <div class="detail-section">
          <div class="detail-section-title">数据统计</div>
          <div class="detail-info-list">
            <div class="detail-info-item">
              <span class="detail-info-label">导图数量</span>
              <span class="detail-info-value">{{ detailUser.mindMapCount }}</span>
            </div>
            <div class="detail-info-item">
              <span class="detail-info-label">注册时间</span>
              <span class="detail-info-value">{{ formatDate(detailUser.createdAt) }}</span>
            </div>
            <div class="detail-info-item">
              <span class="detail-info-label">最近登录</span>
              <span class="detail-info-value">
                {{ detailUser.lastLoginAt ? formatDate(detailUser.lastLoginAt) : '—' }}
              </span>
            </div>
          </div>
        </div>
      </template>

      <template #footer>
        <template v-if="detailUser">
          <NButton
            size="large"
            type="warning"
            class="detail-footer-btn"
            :disabled="detailUser.id === currentUserId"
            @click="toggleAdmin(detailUser)"
          >
            {{ detailUser.isAdmin ? '撤销管理' : '设为管理' }}
          </NButton>
          <NButton
            size="large"
            :type="detailUser.status === 0 ? 'error' : 'success'"
            class="detail-footer-btn"
            :disabled="detailUser.id === currentUserId"
            @click="toggleDisabled(detailUser)"
          >
            {{ detailUser.status === 0 ? '禁用' : '启用' }}
          </NButton>
          <NButton
            size="large"
            type="error"
            dashed
            class="detail-footer-btn"
            :disabled="detailUser.id === currentUserId"
            @click="confirmDelete(detailUser); detailVisible = false"
          >
            删除用户
          </NButton>
        </template>
      </template>
    </AdminDetailSheet>
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

/* ========== 手机端样式 ========== */

// 手机端筛选栏
.mobile-filter-bar {
  display: flex;
  flex-direction: column;
  gap: 10px;

  .mobile-filter-main {
    display: flex;
    align-items: center;
    gap: 8px;

    .mobile-search-input {
      flex: 1;
      min-width: 0;
    }

    .filter-toggle-btn {
      flex-shrink: 0;
      min-width: 64px;
    }
  }

  .mobile-filter-extra {
    display: flex;
    align-items: center;
    gap: 8px;
    animation: slideDown 0.2s ease;

    .mobile-scope-select {
      flex: 1;
      min-width: 0;
    }

    .mobile-search-btn {
      flex-shrink: 0;
      min-width: 80px;
    }
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

// 手机端卡片列表
.mobile-card-list {
  display: flex;
  flex-direction: column;
  gap: 12px;

  .mobile-loading,
  .mobile-empty {
    text-align: center;
    padding: 40px 0;
    color: var(--app-text-secondary);
    font-size: 14px;
  }
}

.user-card {
  background: var(--app-card-bg);
  border-radius: 12px;
  padding: 14px 16px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.06);
  display: flex;
  flex-direction: column;
  gap: 10px;
  cursor: pointer;
  transition: background 0.15s ease;

  &:active {
    background: var(--app-bg);
  }

  .card-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;

    .card-username {
      font-size: 17px;
      font-weight: 600;
      color: var(--app-text-primary, #1e293b);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
  }

  .card-email {
    font-size: 13px;
    color: var(--app-text-secondary, #64748b);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .card-meta {
    display: flex;
    align-items: center;
    gap: 12px;
    flex-wrap: wrap;

    .meta-item {
      display: flex;
      align-items: center;
      gap: 4px;
      font-size: 12px;
      color: var(--app-text-secondary, #64748b);

      .meta-icon {
        width: 14px;
        height: 14px;
        flex-shrink: 0;
      }
    }
  }

  .card-actions {
    display: flex;
    gap: 10px;
    padding-top: 4px;
    border-top: 1px solid var(--app-border);

    .card-action-btn {
      flex: 1;
      height: 40px;
      font-size: 14px;
    }
  }
}

// 手机端分页
.mobile-pagination {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 8px 0;

  .mobile-page-btn {
    flex: 1;
    max-width: 120px;
    height: 44px;
    font-size: 15px;
  }

  .mobile-page-info {
    flex: 1;
    text-align: center;
    font-size: 13px;
    color: var(--app-text-secondary, #64748b);
    white-space: nowrap;
  }
}

// 详情抽屉样式
.detail-section {
  margin-bottom: 20px;

  &:last-of-type {
    margin-bottom: 0;
  }

  .detail-section-title {
    font-size: 14px;
    font-weight: 600;
    color: var(--app-text-primary);
    margin-bottom: 12px;
    padding-bottom: 8px;
    border-bottom: 1px solid var(--app-border);
  }
}

.detail-info-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.detail-info-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;

  .detail-info-label {
    font-size: 14px;
    color: var(--app-text-secondary);
    flex-shrink: 0;
  }

  .detail-info-value {
    font-size: 14px;
    color: var(--app-text-primary);
    text-align: right;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
}

.detail-footer-btn {
  flex: 1;
  height: 44px;
  font-size: 14px;
}

// 小屏适配（安全冗余，配合 JS 判断）
@media (max-width: 767px) {
  .page-title {
    h2 {
      font-size: 18px;
    }

    .hint {
      font-size: 11px;
    }
  }
}
</style>
