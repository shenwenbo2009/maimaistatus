<template>
  <!-- 全站页壳:标题由导航承担,监控页只留内容 -->
  <DesignSystem class="space-y-4">
    <UAlert
      v-if="broadcast"
      color="warning"
      variant="soft"
      icon="i-lucide-megaphone"
      :title="broadcast"
    />

    <div v-if="loading" class="grid grid-cols-1 xl:grid-cols-2 gap-4">
      <UCard><USkeleton class="h-40 w-full" /></UCard>
      <UCard><USkeleton class="h-40 w-full" /></UCard>
    </div>

    <UAlert
      v-else-if="error"
      color="error"
      variant="soft"
      icon="i-lucide-triangle-alert"
      title="获取状态失败"
      :description="error"
    >
      <template #actions>
        <UButton color="error" variant="solid" icon="i-lucide-rotate-cw" @click="load">
          重试
        </UButton>
      </template>
    </UAlert>

    <template v-else-if="st">
      <!-- Row 1:服务器状态 + 状态概览 -->
      <div class="grid grid-cols-1 xl:grid-cols-2 gap-4">
        <!-- 服务器状态:图标 + 单行结论,右上刷新 -->
        <UCard>
          <template #header>
            <div class="flex items-start justify-between gap-3">
              <div>
                <h3 class="text-highlighted font-semibold">服务器状态</h3>
                <p class="mt-1 text-muted text-sm">{{ updatedText }} · {{ countdown }}s 后自动刷新</p>
              </div>
              <UButton
                color="primary"
                variant="soft"
                size="xs"
                icon="i-lucide-rotate-cw"
                :loading="refreshing"
                class="cursor-pointer"
                @click="load(true); loadExtra()"
              >
                刷新
              </UButton>
            </div>
          </template>

          <div class="hero status-hero" :style="heroSyncStyle">
            <div class="status-line" :style="{ color: verdictColor }">
              <Icon :name="verdictIcon" class="status-icon" />
              <span class="status-text">{{ st.verdict_text }}</span>
            </div>
            <p class="sub">{{ st.summary }}</p>
          </div>

          <!-- 最近三次上报:网关采集的 reports 快照(上游只给聚合数,无单条接口) -->
          <template v-if="reportSnapshots !== null">
            <USeparator class="my-4" />
            <div class="flex items-center justify-between">
              <span class="text-sm font-bold">最近三次上报</span>
              <UButton to="/history" variant="link" size="xs" trailing-icon="i-lucide-arrow-right">
                全部历史
              </UButton>
            </div>
            <UTimeline
              v-if="reportSnapshots.length"
              :items="reportSnapshots"
              :default-value="0"
              color="neutral"
              size="xs"
              class="mt-3"
            />
            <UAlert
              v-else
              color="neutral"
              variant="soft"
              icon="i-lucide-inbox"
              title="暂无上报快照"
              description="网关采集一段时间后展示。"
              class="mt-3"
            />
          </template>
        </UCard>

        <!-- 状态概览:右上我要上报 -->
        <UCard>
          <template #header>
            <div class="flex items-start justify-between gap-3">
              <div>
                <h3 class="text-highlighted font-semibold">状态概览</h3>
                <p class="mt-1 text-muted text-sm">延迟 · 上报 · 各服务探针</p>
              </div>
              <UButton
                color="warning"
                variant="soft"
                size="xs"
                icon="i-lucide-megaphone"
                @click="reportOpen = true"
              >
                我要上报
              </UButton>
            </div>
          </template>
          <!-- 仪表条:图标领衔,不再是纯文字 H2 堆 -->
          <div class="grid grid-cols-1 sm:grid-cols-3 gap-3">
            <div class="inst">
              <span class="inst-ic text-primary"><Icon name="i-lucide-gauge" /></span>
              <span class="inst-tx">
                <b>{{ st.latency.current_ms }}<small>ms</small></b>
                <small>延迟 · 负载{{ st.latency.load_text }} · {{ st.latency.volatility_text }}</small>
              </span>
            </div>
            <div class="inst">
              <span class="inst-ic text-error"><Icon name="i-lucide-triangle-alert" /></span>
              <span class="inst-tx">
                <b>{{ latestReport?.anomaly ?? st.reports.anomaly_count }}</b>
                <small>异常上报 · 近1小时<span v-if="latestReport"> · {{ timeAgo(latestReport.ts) }}</span></small>
              </span>
            </div>
            <div class="inst">
              <span class="inst-ic text-success"><Icon name="i-lucide-circle-check" /></span>
              <span class="inst-tx">
                <b>{{ latestReport?.normal ?? st.reports.normal_count }}</b>
                <small>正常上报 · 近1小时<span v-if="latestReport"> · {{ timeAgo(latestReport.ts) }}</span></small>
              </span>
            </div>
          </div>

          <USeparator class="my-4" />

          <!-- 服务行:图标徽章 + 状态图标,文字只做辅助 -->
          <div class="flex flex-col gap-1">
            <div
              v-for="s in st.services"
              :key="s.key"
              class="svc-row"
              :class="'is-' + s.state"
              :style="{ '--svc': svcStateColor(s.state) }"
            >
              <span class="svc-ic"><Icon :name="svcIcon(s.key)" /></span>
              <span class="svc-meta">
                <b>{{ s.name }}</b>
                <small v-if="s.duration_text">{{ s.duration_text }}</small>
                <small v-else>{{ s.state_text }}</small>
              </span>
              <span class="svc-lat">{{ s.latency ?? '—' }}<small>ms</small></span>
              <Icon :name="svcStateIcon(s.state)" class="svc-state" :title="s.state_text" :aria-label="s.state_text" />
            </div>
          </div>
        </UCard>
      </div>

      <!-- 附属区首载骨架:先占位,落定后再判空/判错 -->
      <div v-if="extrasLoading" class="space-y-4">
        <UCard><USkeleton class="h-56 w-full" /></UCard>
        <UCard><USkeleton class="h-72 w-full" /></UCard>
      </div>

      <!-- 网关附属区全挂:明示原因 + 重试,不再静默消失 -->
      <UAlert
        v-if="extrasDown"
        color="warning"
        variant="soft"
        icon="i-lucide-plug-zap"
        title="网关未连接,图表区暂不可用"
        :description="`可用性 / 延迟曲线 / 宕机时间线依赖本地网关 ${apiBaseHint},当前仅显示上游实时状态。请先启动 Go 后端。`"
      >
        <template #actions>
          <UButton color="warning" variant="soft" icon="i-lucide-rotate-cw" @click="loadExtra">
            重试连接
          </UButton>
        </template>
      </UAlert>

      <!-- Row 2:服务可用性(左图表右数据) -->
      <UCard
        v-if="avail"
        :title="`服务可用性 · 近 ${avail.hours} 小时`"
        :description="`平均可用率 ${avail.overall_pct ?? '--'}% · 左侧为可用率柱状,右侧为各服务明细`"
      >
        <div class="grid grid-cols-1 lg:grid-cols-2 gap-6 items-start">
          <!-- 左:ECharts 可用率柱状 -->
          <div>
            <div class="relative w-full" style="height:220px">
              <div ref="availChart" class="absolute inset-0"></div>
              <div v-if="availErr" class="absolute inset-0 flex items-center justify-center pointer-events-none px-4">
                <span class="text-xs text-center" style="color:#ef4444">{{ availErr }}</span>
              </div>
            </div>
            <p class="dim mt-2">绿色 ≥99.5% · 黄色 ≥90% · 红色 &lt;90%</p>
          </div>
          <!-- 右:六个监控项,两行 × 三个 -->
          <div>
            <div v-if="avail.services.length" class="grid grid-cols-2 sm:grid-cols-3 gap-3">
              <div
                v-for="s in avail.services"
                :key="s.key"
                class="rounded-xl border border-default bg-elevated/50 p-3 text-center"
              >
                <div class="flex items-center justify-center gap-1.5 font-bold text-sm">
                  <Icon :name="svcIcon(s.key)" :style="{ color: availHex(s.availability_pct) }" />
                  {{ s.name }}
                </div>
                <div class="mt-1 text-xl font-black tabular-nums" :style="{ color: availHex(s.availability_pct) }">
                  {{ s.availability_pct }}%
                </div>
                <div class="dim mt-0.5">{{ s.avg_latency_ms }}ms · {{ s.checks }}次</div>
                <UProgress v-model="s._v" :color="availColor(s.availability_pct)" size="xs" class="mt-2" />
              </div>
            </div>
            <div v-else class="dim">暂无数据,网关采集一段时间后展示。</div>
          </div>
        </div>
      </UCard>

      <!-- Row 3:图表概览(ECharts:延迟多线 / 上报双柱) -->
      <UCard v-if="!extrasLoading && !extrasDown">
        <template #header>
          <div class="flex items-start justify-between gap-3 flex-wrap">
            <div>
              <h3 class="text-highlighted font-semibold">图表概览</h3>
              <p class="mt-1 text-muted text-sm">各服务延迟 / 上报趋势 · 滚轮缩放</p>
            </div>
            <UTabs
              v-model="chartMode"
              :content="false"
              :items="chartTabs"
              color="primary"
              variant="pill"
              size="sm"
            />
          </div>
        </template>
        <!-- v-show 常驻:实例不销毁,切 tab 只重排,不复活僵尸实例 -->
        <LatencyChart
          v-show="chartMode === 'lat'"
          ref="latChartRef"
          :services="latSeries"
          :spans="maintSpans"
          :height="280"
        />
        <div v-show="chartMode === 'rep'" class="relative w-full" style="height:280px">
          <div ref="repChartEl" class="absolute inset-0"></div>
          <div v-if="repErr" class="absolute inset-0 flex items-center justify-center pointer-events-none px-4">
            <span class="text-xs text-center" style="color:#ef4444">{{ repErr }}</span>
          </div>
        </div>
      </UCard>

      <ReportModal v-model:open="reportOpen" />
    </template>
  </DesignSystem>
</template>

<script setup lang="ts">
import { apiGet, timeAgo, type BotStatus } from '~/composables/useApi'
import type { TimelineItem } from '@nuxt/ui'

const st = ref<BotStatus | null>(null)
const loading = ref(true) // 首屏骨架:仅无数据时展示
const refreshing = ref(false) // 后台静默刷新:只换数值图表,不掀页面
const error = ref('')
const updatedAt = ref(0)
const countdown = ref(30)
const reportOpen = ref(false)
let timer: ReturnType<typeof setInterval> | null = null
// 横幅里展示当前网关地址,方便排查连的是哪
const apiBaseHint = (useRuntimeConfig().public.apiBase as string) || ''

const broadcast = computed(() => st.value?.broadcast?.msg || '')
const updatedText = computed(() =>
  updatedAt.value ? `更新于 ${new Date(updatedAt.value).toLocaleTimeString('zh-CN')}` : ''
)

/* verdict → line-md 图标 + 指定色(以 history.jsonl 实测取值为准) */
const colorMode = useColorMode()
const VERDICT_META: Record<string, { icon: string; color: string }> = {
  normal: { icon: 'line-md:confirm-circle', color: '#29C245' },
  recovering: { icon: 'line-md:chevron-double-up', color: '#79B329' },
  degraded: { icon: 'line-md:chevron-double-down', color: '#D9B61A' },
  outage: { icon: 'line-md:alert', color: '#E68620' },
  maintenance: { icon: 'line-md:cloud-alt-tags-loop', color: '#000000' },
  nodata: { icon: 'line-md:close-circle', color: '#D42222' }
}
const verdictMeta = computed(
  () => VERDICT_META[st.value?.verdict || ''] || { icon: 'line-md:alert', color: '#9aa3c7' }
)
const verdictIcon = computed(() => verdictMeta.value.icon)
// 部分服务宕机时:含 NET 宕机 → #E01212,不含则保持原色;维护黑深色下提亮
const isNetDown = computed(() => st.value?.services?.some(s => s.key === 'net' && s.state === 'down') ?? false)
const verdictColor = computed(() => {
  if (st.value?.verdict === 'maintenance' && colorMode.value === 'dark') return '#e2e8f0'
  if (st.value?.verdict === 'outage' && isNetDown.value) return '#E01212'
  return verdictMeta.value.color
})

/* 结果容器与结果字体颜色同步:边框 + 底 tint + 光晕全跟 verdictColor 走 */
const heroSyncStyle = computed(() => ({
  borderColor: `color-mix(in srgb, ${verdictColor.value} 45%, transparent)`,
  background: `linear-gradient(135deg, color-mix(in srgb, ${verdictColor.value} 12%, transparent), color-mix(in srgb, ${verdictColor.value} 4%, transparent))`,
  boxShadow: `0 0 60px color-mix(in srgb, ${verdictColor.value} 12%, transparent) inset, 0 0 40px color-mix(in srgb, ${verdictColor.value} 8%, transparent)`
}))

/* 服务图标(按 key)与状态图标(取代文字 pill),与 verdict 取色同系 */
const SVC_ICONS: Record<string, string> = {
  net: 'i-lucide-globe',
  aime: 'i-lucide-user-round',
  title: 'i-lucide-type',
  member: 'i-lucide-message-circle',
  qr: 'i-lucide-qr-code',
  bl: 'i-lucide-receipt'
}
const svcIcon = (k: string) => SVC_ICONS[k] || 'i-lucide-server'
const STATE_ICONS: Record<string, string> = {
  ok: 'i-lucide-circle-check',
  down: 'i-lucide-cloud-off',
  degraded: 'i-lucide-chevrons-down',
  recovering: 'i-lucide-chevrons-up',
  maintenance: 'i-lucide-cloud-cog',
  nodata: 'i-lucide-circle-dot'
}
const STATE_COLORS: Record<string, string> = {
  ok: '#29C245',
  down: '#E01212',
  degraded: '#F04E23',
  recovering: '#E68620',
  maintenance: '#000000',
  nodata: '#8a93a6'
}
const svcStateIcon = (s: string) => STATE_ICONS[s] || 'i-lucide-circle-dot'
const svcStateColor = (s: string) => {
  // 维护黑深色下提亮,浅色严格按指定色
  if (s === 'maintenance' && colorMode.value === 'dark') return '#e2e8f0'
  return STATE_COLORS[s] || '#8a93a6'
}

function availColor(p: string | number): 'success' | 'warning' | 'error' {
  const v = Number(p)
  return v >= 99.5 ? 'success' : v >= 90 ? 'warning' : 'error'
}

async function load(silent = false) {
  // 有数据后的刷新走静默通道:旧页面原地保留,新数据直接替换响应式源
  if (silent && st.value) refreshing.value = true
  else loading.value = true
  error.value = ''
  try {
    st.value = await apiGet<BotStatus>('/api/status')
    updatedAt.value = Date.now()
  } catch (e: any) {
    // 后台刷新失败不断页:留旧数据仅打告警;首屏失败才进 error 全页
    if (!st.value) error.value = e?.message || '网络错误'
    else console.warn('[status] background refresh failed:', e?.message || e)
  } finally {
    loading.value = false
    refreshing.value = false
    countdown.value = 30 // finally 里归零,避免失败时倒计时穿底每秒轰炸
  }
}

onMounted(() => {
  load()
  loadExtra()
  timer = setInterval(() => {
    countdown.value--
    if (countdown.value <= 0) {
      load(true)
      loadExtra() // 附属图表本地静默重算,无 loading 门,不闪
    }
  }, 1000)
})
onUnmounted(() => { if (timer) clearInterval(timer) })

/* ---- ECharts(按需引入,保持包体苗条) ---- */
import { init as echartsInit, use as echartsUse } from 'echarts/core'
import { BarChart } from 'echarts/charts'
import { GridComponent, TooltipComponent, LegendComponent, DataZoomComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import type { ECharts } from 'echarts/core'

echartsUse([BarChart, GridComponent, TooltipComponent, LegendComponent, DataZoomComponent, CanvasRenderer])

/* ---- 服务可用性 + 图表概览(依赖网关本地采集,失败则隐藏) ---- */
const avail = ref<{ hours: number; overall_pct?: string; buckets: { ts: number; ratio: number; maint?: boolean; empty?: boolean }[]; services: any[] } | null>(null)
const latSeries = ref<{ key: string; name: string; series: [number, number][] }[]>([])
// 维护段(后端合并好的 [start,end] 毫秒区间,空为 null):柱满格灰 / 线压 0 灰
const maintSpans = ref<[number, number][]>([])
const MAINT_GRAY = '#9aa3c7'
// 网关上报趋势(取最新点做状态概览计数,带采集新鲜度;网关挂了回退上游快照)
const reportTrend = ref<{ ts: number; anomaly: number; normal: number }[]>([])
const latestReport = computed(() => reportTrend.value.length ? reportTrend.value[reportTrend.value.length - 1] : null)
// 网关附属区连通旗:全挂则 Row2/Row3 改显警告横幅,不再静默消失
const extrasDown = ref(false)
// 附属区首载旗:骨架占位,loadExtra 落定后才判空/判错,避免开屏闪空
const extrasLoading = ref(true)

async function loadExtra() {
  // 各端点独立结算:一个挂了不连累其他卡片
  const [ra, rl, rt] = await Promise.allSettled([
    apiGet<any>('/api/services/availability', { hours: '12' }),
    apiGet<any>('/api/services/latency', { hours: '24' }),
    apiGet<{ trend: { ts: number; anomaly: number; normal: number }[] }>('/api/reports/trend', { limit: '60' })
  ])
  if (ra.status === 'fulfilled') {
    const a = ra.value
    // 给 UProgress 补 v-model 需要的数值字段
    if (a?.services) {
      for (const s of a.services) s._v = Number(s.availability_pct)
    }
    avail.value = a
  }
  if (rl.status === 'fulfilled') {
    latSeries.value = rl.value.services || []
    maintSpans.value = rl.value.maint_spans || []
  }
  if (rt.status === 'fulfilled') reportTrend.value = rt.value.trend || []
  extrasDown.value = [ra, rl, rt].every(r => r.status === 'rejected')
  extrasLoading.value = false
}

/* ---- 最近三次上报 Timeline(网关 reports 快照,后台 fetch;上游无单条接口) ---- */
const reportSnapshots = computed<TimelineItem[] | null>(() => {
  const t = reportTrend.value
  if (!t.length && !extrasDown.value) return null // 还没回来就先不渲染
  const out: TimelineItem[] = []
  for (let k = Math.max(0, t.length - 3); k < t.length; k++) {
    const p = t[k]
    const prev = k > 0 ? t[k - 1] : null
    const dA = prev ? p.anomaly - prev.anomaly : 0
    const dN = prev ? p.normal - prev.normal : 0
    const delta = prev ? ` · 较上次 ${dA >= 0 ? '+' : ''}${dA}/${dN >= 0 ? '+' : ''}${dN}` : ''
    out.push({
      date: new Date(p.ts).toLocaleString('zh-CN', { month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit' }),
      title: `异常 ${p.anomaly} · 正常 ${p.normal}`,
      description: `上报快照${delta}`,
      icon: p.anomaly > 0 ? 'i-lucide-triangle-alert' : 'i-lucide-check-circle',
      value: `snap-${k}`
    })
  }
  return out.reverse() // 最新在前
})

// 延迟 / 上报两档(上报口径见 isMaiDown /api/bot reports:近1小时异常/正常上报数)
const chartMode = ref<'lat' | 'rep'>('lat')
const chartTabs = [
  { label: '延迟', value: 'lat', icon: 'i-lucide-activity' },
  { label: '上报', value: 'rep', icon: 'i-lucide-bar-chart-3' }
]

/* ---- ECharts 渲染(主题跟随深浅色) ---- */
const isDark = computed(() => colorMode.value === 'dark')
const axisColor = computed(() => (isDark.value ? '#9aa3c7' : '#64748b'))
const splitColor = computed(() => (isDark.value ? 'rgba(255,255,255,0.08)' : 'rgba(15,23,42,0.08)'))
const tipBg = computed(() => (isDark.value ? '#0b1130' : '#ffffff'))
const tipText = computed(() => (isDark.value ? '#eef1ff' : '#0f172a'))

const AVAIL_HEX = { ok: '#22c55e', warn: '#f59e0b', bad: '#ef4444' }
function availHex(p: string | number): string {
  const v = Number(p)
  return v >= 99.5 ? AVAIL_HEX.ok : v >= 90 ? AVAIL_HEX.warn : AVAIL_HEX.bad
}

const availChartEl = useTemplateRef<HTMLDivElement>('availChart')
const repChartEl = useTemplateRef<HTMLDivElement>('repChartEl')
let availChart: ECharts | null = null
let repChart: ECharts | null = null
// 绘制错误上屏(截图即证据),成功自动清除
const availErr = ref('')
const repErr = ref('')

function fmtAxisTime(ts: number): string {
  return new Date(ts).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' })
}
function fmtTipTime(ts: number): string {
  const d = new Date(ts)
  return `${d.getMonth() + 1}/${d.getDate()} ${d.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' })}`
}

function renderAvail() {
  if (!availChartEl.value || !avail.value) return
  if (!availChart || availChart.isDisposed()) availChart = echartsInit(availChartEl.value)
  try {
    const ratios = avail.value.buckets.map(b => b.ratio * 100)
    const min = ratios.length ? Math.min(...ratios) : 100
    availChart.setOption({
    grid: { left: 42, right: 12, top: 16, bottom: 24 },
    tooltip: {
      trigger: 'axis',
      backgroundColor: tipBg.value,
      borderWidth: 0,
      textStyle: { color: tipText.value, fontSize: 12 },
      formatter: (ps: any) => {
        const p = Array.isArray(ps) ? ps[0] : ps
        const b = avail.value!.buckets[p.dataIndex]
        if (!b || b.empty || p.value[1] == null) return `${fmtTipTime(p.value[0])}<br/>暂无采集`
        if (b.maint) return `${fmtTipTime(p.value[0])}<br/>常规维护中(04:00-07:00 GMT+8)`
        return `${fmtTipTime(p.value[0])}<br/>可用率 <b>${Number(p.value[1]).toFixed(1)}%</b>`
      }
    },
    xAxis: {
      type: 'time',
      axisLabel: { color: axisColor.value, fontSize: 10, hideOverlap: true, formatter: (v: number) => fmtAxisTime(v) },
      axisLine: { lineStyle: { color: splitColor.value } },
      axisTick: { show: false }
    },
    yAxis: {
      type: 'value',
      min: Math.max(0, Math.floor(min - 1)),
      max: 100,
      axisLabel: { color: axisColor.value, fontSize: 10, formatter: '{value}%' },
      splitLine: { lineStyle: { color: splitColor.value } }
    },
    series: [{
      type: 'bar',
      data: avail.value.buckets.map(b => [b.ts, b.empty ? null : (b.maint ? 100 : +(b.ratio * 100).toFixed(1))]),
      barWidth: '60%',
      itemStyle: {
        borderRadius: [3, 3, 0, 0],
        color: (p: any) => {
          const b = avail.value!.buckets[p.dataIndex]
          if (!b || b.empty || p.value[1] == null) return 'transparent'
          return b.maint ? MAINT_GRAY : availHex(p.value[1])
        }
      }
    }]
    }, true)
    if (availErr.value) availErr.value = ''
  } catch (e) {
    console.error('[AvailChart]', e)
    availErr.value = '绘制失败:' + String((e as Error)?.message || e).slice(0, 160)
    try { availChart?.dispose() } catch {}
    availChart = null
  }
}

function renderChart() {
  if (chartMode.value !== 'rep' || !repChartEl.value) return
  // 复活守卫:实例被 dispose 后变量置空,下次重建而非复用僵尸
  if (!repChart || repChart.isDisposed()) repChart = echartsInit(repChartEl.value)
  return renderRep()
}

function renderRep() {
  const hasData = reportTrend.value.length > 0
  try {
    repChart!.setOption({
    grid: { left: 46, right: 16, top: 42, bottom: 62 },
    legend: { top: 0, textStyle: { color: axisColor.value, fontSize: 12 } },
    tooltip: {
      trigger: 'axis',
      backgroundColor: tipBg.value,
      borderWidth: 0,
      textStyle: { color: tipText.value, fontSize: 12 },
      axisPointer: { lineStyle: { color: splitColor.value } },
      valueFormatter: (v: any) => `${v} 条`
    },
    xAxis: {
      type: 'time',
      axisLabel: { color: axisColor.value, fontSize: 10, hideOverlap: true, formatter: (v: number) => fmtAxisTime(v) },
      axisLine: { lineStyle: { color: splitColor.value } },
      axisTick: { show: false }
    },
    yAxis: {
      type: 'value',
      axisLabel: { color: axisColor.value, fontSize: 10, formatter: '{value}' },
      splitLine: { lineStyle: { color: splitColor.value } }
    },
    dataZoom: [
      { type: 'inside' },
      { type: 'slider', height: 22, bottom: 6, borderColor: splitColor.value, textStyle: { color: axisColor.value, fontSize: 10 } }
    ],
    // 无数据也画框:中央空态字,不留白
    graphic: hasData ? [] : [{
      type: 'text',
      left: 'center',
      top: 'middle',
      style: { text: '数据采集中…', fill: axisColor.value, fontSize: 13 }
    }],
    series: [
      {
        name: '异常上报',
        type: 'bar',
        barWidth: '38%',
        itemStyle: { borderRadius: [3, 3, 0, 0], color: '#ff5470' },
        data: reportTrend.value.map(p => [p.ts, p.anomaly])
      },
      {
        name: '正常上报',
        type: 'bar',
        barWidth: '38%',
        itemStyle: { borderRadius: [3, 3, 0, 0], color: '#2dffa8' },
        data: reportTrend.value.map(p => [p.ts, p.normal])
      }
    ]
    }, true)
    if (repErr.value) repErr.value = ''
  } catch (e) {
    console.error('[RepChart]', e)
    repErr.value = '绘制失败:' + String((e as Error)?.message || e).slice(0, 160)
    try { repChart?.dispose() } catch {}
    repChart = null
  }
}

function resizeCharts() {
  availChart?.resize()
  repChart?.resize()
}

watch([avail, isDark], () => nextTick(renderAvail))
watch([reportTrend, isDark], () => nextTick(renderChart))
// 切 tab 时显式重排:隐藏容器尺寸为 0,切回来必须 resize 否则白板
const latChartRef = useTemplateRef<{ refresh: () => void }>('latChartRef')
watch(chartMode, () => {
  nextTick(() => {
    if (chartMode.value === 'rep') {
      renderChart()
      repChart?.resize()
    } else {
      latChartRef.value?.refresh()
    }
  })
})

onMounted(() => {
  nextTick(() => { renderAvail(); renderChart() })
  window.addEventListener('resize', resizeCharts)
})
onUnmounted(() => {
  window.removeEventListener('resize', resizeCharts)
  availChart?.dispose()
  repChart?.dispose()
})
</script>
