<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { NCard, NEmpty, NIcon, NSpin, NGrid, NGridItem, useMessage } from 'naive-ui'
import {
  PeopleOutline,
  MapOutline,
  ShareSocialOutline,
  FlagOutline
} from '@vicons/ionicons5'
import { useAdminStore } from '@/stores/admin'

const adminStore = useAdminStore()
const message = useMessage()
const loading = ref(true)

const stats = computed(() => adminStore.stats)

interface StatCard {
  label: string
  value: number
  sub?: string
  icon: typeof PeopleOutline
  color: string
}

const cards = computed<StatCard[]>(() => {
  const s = stats.value
  if (!s) return []
  return [
    {
      label: '用户总数',
      value: s.userCount,
      sub: `活跃 ${s.activeUserCount} · 禁用 ${s.disabledUserCount} · 管理员 ${s.adminCount}`,
      icon: PeopleOutline,
      color: '#2080f0'
    },
    {
      label: '导图总数',
      value: s.mindMapCount,
      sub: `公开 ${s.publicMindMapCount} · 下架 ${s.takenDownMindMapCount}`,
      icon: MapOutline,
      color: '#18a058'
    },
    {
      label: '分享链接',
      value: s.shareCount,
      sub: `活跃 ${s.activeShareCount}`,
      icon: ShareSocialOutline,
      color: '#f0a020'
    },
    {
      label: '待处理举报',
      value: s.pendingReportCount,
      sub: `累计举报 ${s.totalReportCount}`,
      icon: FlagOutline,
      color: '#d03050'
    }
  ]
})

// 近7日折线图数据：合并用户和导图数据
const chartData = computed(() => {
  const s = stats.value
  if (!s) return []
  const map = new Map<string, { date: string; users: number; maps: number }>()
  for (const d of s.newUsersLast7Days) {
    map.set(d.date, { date: d.date, users: d.count, maps: 0 })
  }
  for (const d of s.newMindMapsLast7Days) {
    const entry = map.get(d.date) ?? { date: d.date, users: 0, maps: 0 }
    entry.maps = d.count
    map.set(d.date, entry)
  }
  return Array.from(map.values())
})

const chartMax = computed(() => {
  const all = chartData.value.flatMap((d) => [d.users, d.maps])
  return all.length === 0 ? 1 : Math.max(...all, 1)
})

function formatDate(d: string): string {
  // yyyy-MM-dd → MM/dd
  const parts = d.split('-')
  if (parts.length !== 3) return d
  return `${parts[1]}/${parts[2]}`
}

async function refresh(): Promise<void> {
  loading.value = true
  try {
    await adminStore.loadStats()
  } catch (e) {
    message.error((e as Error).message)
  } finally {
    loading.value = false
  }
}

onMounted(refresh)
</script>

<template>
  <div class="admin-dashboard">
    <div class="page-title">
      <h2>管理看板</h2>
      <span class="hint">系统整体运行状态概览</span>
    </div>

    <NSpin :show="loading">
      <NGrid :cols="4" :x-gap="12" :y-gap="12" responsive="screen" item-responsive>
        <NGridItem
          v-for="card in cards"
          :key="card.label"
          span="4 m:2 l:1"
        >
          <NCard class="stat-card" :bordered="true">
            <div class="stat-row">
              <div class="stat-icon" :style="{ background: `${card.color}22`, color: card.color }">
                <NIcon size="28"><component :is="card.icon" /></NIcon>
              </div>
              <div class="stat-text">
                <div class="stat-value" :style="{ color: card.color }">{{ card.value }}</div>
                <div class="stat-label">{{ card.label }}</div>
                <div v-if="card.sub" class="stat-sub">{{ card.sub }}</div>
              </div>
            </div>
          </NCard>
        </NGridItem>
      </NGrid>

      <NCard class="chart-card" title="近 7 日新增趋势" :bordered="true">
        <div v-if="chartData.length === 0" class="chart-empty">
          <NEmpty description="暂无数据" />
        </div>
        <div v-else class="chart-wrap">
          <div class="chart-bars">
            <div v-for="d in chartData" :key="d.date" class="bar-group">
              <div class="bars">
                <div
                  class="bar bar-users"
                  :style="{ height: `${(d.users / chartMax) * 100}%` }"
                  :title="`新增用户 ${d.users}`"
                ></div>
                <div
                  class="bar bar-maps"
                  :style="{ height: `${(d.maps / chartMax) * 100}%` }"
                  :title="`新增导图 ${d.maps}`"
                ></div>
              </div>
              <div class="bar-label">{{ formatDate(d.date) }}</div>
            </div>
          </div>
          <div class="legend">
            <span><i class="dot dot-users"></i>新增用户</span>
            <span><i class="dot dot-maps"></i>新增导图</span>
          </div>
        </div>
      </NCard>
    </NSpin>
  </div>
</template>

<style scoped lang="scss">
.page-title {
  display: flex;
  align-items: baseline;
  gap: 12px;
  margin-bottom: 16px;

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

.stat-card {
  :deep(.n-card__content) {
    padding: 16px;
  }
}

.stat-row {
  display: flex;
  align-items: center;
  gap: 12px;
}

.stat-icon {
  width: 56px;
  height: 56px;
  border-radius: 12px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.stat-text {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.stat-value {
  font-size: 28px;
  font-weight: 700;
  line-height: 1.1;
}

.stat-label {
  font-size: 13px;
  color: var(--app-text-secondary);
}

.stat-sub {
  font-size: 11px;
  color: var(--app-text-secondary);
  opacity: 0.8;
}

.chart-card {
  margin-top: 12px;
}

.chart-wrap {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.chart-bars {
  display: flex;
  align-items: flex-end;
  gap: 8px;
  height: 220px;
  padding: 0 8px;
  border-bottom: 1px solid var(--app-border);
}

.bar-group {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  height: 100%;
}

.bars {
  display: flex;
  align-items: flex-end;
  gap: 4px;
  flex: 1;
  width: 100%;
  justify-content: center;
}

.bar {
  width: 16px;
  min-height: 2px;
  border-radius: 3px 3px 0 0;
  transition: height 0.3s ease;
}

.bar-users {
  background: #2080f0;
}

.bar-maps {
  background: #18a058;
}

.bar-label {
  font-size: 11px;
  color: var(--app-text-secondary);
}

.legend {
  display: flex;
  gap: 16px;
  font-size: 12px;
  color: var(--app-text-secondary);

  .dot {
    display: inline-block;
    width: 10px;
    height: 10px;
    border-radius: 2px;
    margin-right: 4px;
    vertical-align: middle;
  }

  .dot-users {
    background: #2080f0;
  }

  .dot-maps {
    background: #18a058;
  }
}

.chart-empty {
  padding: 32px;
}
</style>
