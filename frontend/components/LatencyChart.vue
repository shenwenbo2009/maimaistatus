<!-- 延迟曲线(ECharts 多线 + 维护灰段):监控页图表概览-延迟与历史页延迟曲线共用,同构渲染 -->
<script setup lang="ts">
import { init as echartsInit, use as echartsUse } from 'echarts/core'
import { LineChart } from 'echarts/charts'
import { GridComponent, TooltipComponent, LegendComponent, DataZoomComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import type { ECharts } from 'echarts/core'

echartsUse([LineChart, GridComponent, TooltipComponent, LegendComponent, DataZoomComponent, CanvasRenderer])

export interface LatencyService { key: string; name: string; series: [number, number][] }

const props = withDefaults(defineProps<{
  services: LatencyService[]
  spans?: [number, number][]
  height?: number
}>(), { spans: () => [], height: 280 })

const SVC_COLORS = ['#22e6ff', '#ffd84d', '#ff9d3e', '#ff3ec8', '#9d7bff', '#2dffa8', '#7affd4', '#ff5470']
const svcColor = (i: number) => SVC_COLORS[i % SVC_COLORS.length]
const MAINT_GRAY = '#9aa3c7'

const colorMode = useColorMode()
const isDark = computed(() => colorMode.value === 'dark')
const axisColor = computed(() => (isDark.value ? '#9aa3c7' : '#64748b'))
const splitColor = computed(() => (isDark.value ? 'rgba(255,255,255,0.08)' : 'rgba(15,23,42,0.08)'))
const tipBg = computed(() => (isDark.value ? '#0b1130' : '#ffffff'))
const tipText = computed(() => (isDark.value ? '#eef1ff' : '#0f172a'))

const chartEl = useTemplateRef<HTMLDivElement>('chart')
let chart: ECharts | null = null
// 绘制错误直接上屏(红色),下次成功自动清除:生产排障用,截图即证据
const errMsg = ref('')

function fmtAxisTime(ts: number): string {
  return new Date(ts).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' })
}

// 脏数据只会画残,非法数据会整包抛错导致全白:先过滤
function cleanPts(pts: any[]): [number, number][] {
  if (!Array.isArray(pts)) return []
  return pts.filter(p =>
    Array.isArray(p) && typeof p[0] === 'number' && typeof p[1] === 'number' &&
    Number.isFinite(p[0]) && Number.isFinite(p[1])
  ) as [number, number][]
}

function ensureChart() {
  if (!chartEl.value) return false
  if (!chart || chart.isDisposed()) chart = echartsInit(chartEl.value)
  return true
}

function render() {
  if (!ensureChart()) return
  try {
    const hasData = props.services.length > 0
    const spans = (props.spans || []).filter(s =>
      Array.isArray(s) && typeof s[0] === 'number' && typeof s[1] === 'number' &&
      Number.isFinite(s[0]) && Number.isFinite(s[1])
    ) as [number, number][]
    chart!.setOption({
    grid: { left: 46, right: 16, top: 42, bottom: 62 },
    legend: { top: 0, textStyle: { color: axisColor.value, fontSize: 12 } },
    tooltip: {
      trigger: 'axis',
      backgroundColor: tipBg.value,
      borderWidth: 0,
      textStyle: { color: tipText.value, fontSize: 12 },
      axisPointer: { lineStyle: { color: splitColor.value } },
      valueFormatter: (v: any) => `${v}ms`
    },
    xAxis: {
      type: 'time',
      axisLabel: { color: axisColor.value, fontSize: 10, hideOverlap: true, formatter: (v: number) => fmtAxisTime(v) },
      axisLine: { lineStyle: { color: splitColor.value } },
      axisTick: { show: false }
    },
    yAxis: {
      type: 'value',
      axisLabel: { color: axisColor.value, fontSize: 10, formatter: '{value}ms' },
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
      ...props.services.map((s, i) => ({
        name: s.name,
        type: 'line',
        smooth: true,
        symbol: 'none',
        color: svcColor(i), // 图例取顶层 color,不写会和 lineStyle 脱钩
        lineStyle: { width: 2, color: svcColor(i) },
        emphasis: { focus: 'series' },
        data: cleanPts(s.series)
      })),
      // 维护段:灰底 + 压 0 灰线(同名 series legend 自动合并为一个"维护")
      ...spans.map(([s, e]) => ({
        name: '维护',
        type: 'line',
        symbol: 'none',
        color: MAINT_GRAY,
        lineStyle: { width: 2, color: MAINT_GRAY },
        data: [[s, 0], [e, 0]],
        markArea: {
          silent: true,
          itemStyle: { color: 'rgba(154,163,199,0.12)' },
          data: [[{ xAxis: s }, { xAxis: e }]]
        }
      }))
    ]
  }, true)
    if (errMsg.value) errMsg.value = '' // 成功即清除旧错
  } catch (e) {
    console.error('[LatencyChart]', e)
    errMsg.value = '绘制失败:' + String((e as Error)?.message || e).slice(0, 160)
    try { chart?.dispose() } catch {}
    chart = null // 废弃坏实例,下次重建,不抱僵尸
  }
}

function refresh() {
  render()
  chart?.resize()
}

defineExpose({ refresh })

function resize() { chart?.resize() }

watch([() => props.services, () => props.spans, isDark], () => nextTick(render))
onMounted(() => {
  nextTick(render)
  window.addEventListener('resize', resize)
})
onUnmounted(() => {
  window.removeEventListener('resize', resize)
  chart?.dispose()
  chart = null
})
</script>

<template>
  <div class="relative w-full" :style="{ height: height + 'px' }">
    <div ref="chart" class="absolute inset-0"></div>
    <div v-if="errMsg" class="absolute inset-0 flex items-center justify-center pointer-events-none px-4">
      <span class="text-xs text-center" style="color:#ef4444">{{ errMsg }}</span>
    </div>
  </div>
</template>
