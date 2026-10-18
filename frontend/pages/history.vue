<template>
  <DesignSystem
    title="历史记录"
    description="Go 网关每 30 秒采集一次上游状态并落盘,此处展示本地历史曲线。"
    :loading="loading"
    :error="error"
    @retry="load"
  >
    <!-- 统计仪表条(可用率已下线,只留延迟与点数) -->
    <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
      <div class="inst">
        <span class="inst-ic text-primary"><Icon name="i-lucide-gauge" /></span>
        <span class="inst-tx">
          <b>{{ summary.avg_latency_ms }}<small>ms</small></b>
          <small>平均延迟</small>
        </span>
      </div>
      <div class="inst">
        <span class="inst-ic text-warning"><Icon name="i-lucide-database" /></span>
        <span class="inst-tx">
          <b>{{ summary.total }}</b>
          <small>采集点数</small>
        </span>
      </div>
    </div>

    <!-- 延迟曲线(与监控页图表概览-延迟同构,同一组件) -->
    <UCard class="mt-4" title="延迟曲线" description="各服务延迟趋势 · 与监控页一致">
      <LatencyChart :services="latServices" :spans="latSpans" :height="240" />
    </UCard>

    <!-- 故障事件时间线 -->
    <UCard class="mt-4" title="故障事件" description="异常连续段自动提取,最新在前">
      <UTimeline
        v-if="incidentItems.length"
        :items="incidentItems"
        :default-value="0"
        :color="hasOngoing ? 'error' : 'success'"
        size="sm"
      />
      <UAlert
        v-else
        color="success"
        variant="soft"
        icon="i-lucide-check-circle"
        title="暂无异常事件"
        description="一切正常。"
      />
    </UCard>

    <!-- 最近记录表格 -->
    <UCard class="mt-4" title="最近记录" description="最新 60 条采集点位">
      <UTable :data="tableRows" :columns="columns" class="w-full" />
    </UCard>
  </DesignSystem>
</template>

<script setup lang="ts">
import { h, resolveComponent } from 'vue'
import type { TableColumn, TimelineItem } from '@nuxt/ui'
import { apiGet, timeAgo } from '~/composables/useApi'

const UBadge = resolveComponent('UBadge')

interface Point { ts: number; verdict: string; verdict_text: string; latency_ms: number | null; anomaly: number; normal: number; ok_count: number; down_count: number; maint?: boolean }

const points = ref<Point[]>([]) // 时间正序
const summary = ref<any>({})
const incidents = ref<any[]>([])
const loading = ref(true)
const error = ref('')

function fmtDur(sec: number): string {
  if (sec < 60) return `${sec} 秒`
  const m = Math.floor(sec / 60)
  if (m < 60) return `${m} 分钟`
  return `${Math.floor(m / 60)} 小时 ${m % 60} 分`
}
function fmtDT(ts: number): string {
  return new Date(ts).toLocaleString('zh-CN', { month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit' })
}

/* ---- 故障事件 Timeline(后端已按最新在前排好) ---- */
const hasOngoing = computed(() => incidents.value.some(i => i.ongoing))
const incidentItems = computed<TimelineItem[]>(() =>
  incidents.value.map((it, i) => ({
    date: fmtDT(it.start_ts),
    title: it.verdict_text || it.worst_verdict,
    description: `持续 ${fmtDur(it.duration_sec)} · ${it.worst_verdict} · ${it.ongoing ? '进行中' : '已恢复'}`,
    icon: it.ongoing ? 'i-lucide-triangle-alert' : 'i-lucide-check-circle',
    value: `inc-${i}`
  }))
)

/* ---- 最近记录 Table ---- */
const tableRows = computed(() => [...points.value].reverse().slice(0, 60))
const columns: TableColumn<Point>[] = [
  {
    accessorKey: 'ts',
    header: '时间',
    cell: ({ row }) => timeAgo(row.getValue('ts') as number)
  },
  {
    accessorKey: 'verdict',
    header: '判定',
    cell: ({ row }) => {
      const o = row.original as Point
      return h(UBadge, {
        variant: 'soft',
        size: 'sm',
        color: o.verdict === 'normal' ? 'success' : 'error'
      }, () => o.verdict_text || o.verdict)
    }
  },
  {
    accessorKey: 'latency_ms',
    header: '延迟',
    cell: ({ row }) => {
      const v = row.getValue('latency_ms') as number | null
      return v == null ? '—' : `${v} ms`
    }
  },
  {
    id: 'svc',
    header: '服务正常',
    cell: ({ row }) => {
      const o = row.original as Point
      return `${o.ok_count}/${o.ok_count + o.down_count}`
    }
  },
  {
    id: 'rep',
    header: '上报(异常/正常)',
    cell: ({ row }) => {
      const o = row.original as Point
      return `${o.anomaly}/${o.normal}`
    }
  }
]

/* ---- 延迟曲线数据(与监控页同源:/api/services/latency) ---- */
const latServices = ref<{ key: string; name: string; series: [number, number][] }[]>([])
const latSpans = ref<[number, number][]>([])

async function load() {
  loading.value = true
  error.value = ''
  try {
    const [h, s, inc, l] = await Promise.all([
      apiGet<{ points: Point[] }>('/api/history', { limit: '288' }),
      apiGet<any>('/api/history/summary'),
      apiGet<{ incidents: any[] }>('/api/incidents').catch(() => ({ incidents: [] })),
      apiGet<{ services: { key: string; name: string; series: [number, number][] }[]; maint_spans: [number, number][] | null }>('/api/services/latency', { hours: '24' })
    ])
    points.value = [...h.points].reverse() // 表格最新在前,此处转正序备用
    summary.value = s
    incidents.value = inc.incidents
    latServices.value = l.services || []
    latSpans.value = l.maint_spans || []
  } catch (e: any) {
    // 网关没开时给空态(历史依赖本地采集,无法兜底上游)
    error.value = '网关未连接:请先启动 Go 后端 (历史数据依赖本地采集)'
  } finally {
    loading.value = false
  }
}
onMounted(load)
</script>
