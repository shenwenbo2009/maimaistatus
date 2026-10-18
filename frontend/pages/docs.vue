<template>
  <DesignSystem
    title="开发者文档"
    description="网关 API 全参考 · 数据来源于 isMaiDown by Chongxi,使用时必须标注来源。"
  >
    <!-- 基址与通用说明 -->
    <UCard title="基址与通用说明">
      <div class="grid grid-cols-1 md:grid-cols-2 gap-3 text-sm">
        <div class="rounded-xl bg-elevated/60 p-3">
          <p class="dim">生产基址(Base URL)</p>
          <code class="inline">https://maimai.imikufans.cn/api</code>
        </div>
        <div class="rounded-xl bg-elevated/60 p-3">
          <p class="dim">本地网关(开发联调)</p>
          <code class="inline">http://localhost:9560</code>
          <span class="dim">(+ <code class="inline">/healthz</code> 在域名根下,不带 /api)</span>
        </div>
      </div>
      <ul class="text-sm mt-3 space-y-1 list-disc pl-5">
        <li>所有响应均带 <code class="inline">X-Data-Source: 数据来源于 isMaiDown by Chongxi</code>。</li>
        <li>代理缓存类接口命中时带 <code class="inline">X-Cache: HIT</code>,否则 <code class="inline">MISS</code>。</li>
        <li>上游不可达返回 <code class="inline">502 {"error":"upstream unreachable"}</code>;参数错误 <code class="inline">400</code>;验证码无效 <code class="inline">403</code>;上报鉴权失败(上游) <code class="inline">401</code>;限流 <code class="inline">429</code>。</li>
        <li>封面等相对路径请拼上游前缀 <code class="inline">https://mai.chongxi.us</code>。</li>
      </ul>
      <div class="text-sm mt-3 rounded-xl bg-elevated/60 p-3">
        <p class="font-bold mb-1">各语言依赖说明</p>
        <p>Go: 标准库即可(<code class="inline">net/http</code> 等)。Rust: <code class="inline">reqwest + tokio</code>(async main)。JavaScript: Node 18+ / 浏览器原生 <code class="inline">fetch</code>。Python: <code class="inline">pip install requests</code>。</p>
      </div>
    </UCard>

    <!-- 语言切换(全局) -->
    <div class="flex items-center gap-3 mt-4 flex-wrap">
      <span class="dim">使用例语言:</span>
      <UTabs v-model="lang" :content="false" :items="langTabs" color="primary" variant="pill" size="sm" />
    </div>

    <!-- 分组锚点 -->
    <div class="flex gap-2 flex-wrap mt-3">
      <UButton
        v-for="g in groups"
        :key="g.id"
        :to="'#sec-' + g.id"
        color="neutral"
        variant="ghost"
        size="xs"
        class="cursor-pointer"
      >
        {{ g.title }}
      </UButton>
    </div>

    <!-- 接口卡片 -->
    <div v-for="g in groups" :key="g.id" :id="'sec-' + g.id" class="scroll-mt-24 mt-6">
      <h3 class="text-lg font-black mb-3">{{ g.title }}</h3>
      <div class="space-y-4">
        <UCard v-for="ep in g.items" :key="ep.id" :id="'ep-' + ep.id" class="scroll-mt-24">
          <template #header>
            <div class="flex items-center gap-2 flex-wrap">
              <UBadge :color="ep.method === 'GET' ? 'success' : 'warning'" variant="soft">{{ ep.method }}</UBadge>
              <code class="inline">{{ ep.path }}</code>
              <span class="dim">{{ ep.summary }}</span>
            </div>
          </template>

          <p class="font-bold text-sm mt-1 mb-1">用途</p>
          <p class="text-sm">{{ ep.purpose }}</p>

          <p class="font-bold text-sm mt-4 mb-2">请求参数</p>
          <UTable v-if="ep.params.length" :data="ep.params" :columns="paramCols" class="w-full" />
          <p v-else class="dim text-sm">无。</p>

          <p class="font-bold text-sm mt-4 mb-2">使用例({{ langLabel }})</p>
          <pre class="code">{{ ep.codes[lang] }}</pre>

          <p class="font-bold text-sm mt-4 mb-2">响应字段</p>
          <UTable :data="ep.fields" :columns="fieldCols" class="w-full" />

          <p class="font-bold text-sm mt-4 mb-2">正确响应例</p>
          <pre class="code">{{ ep.resp }}</pre>
        </UCard>
      </div>
    </div>
  </DesignSystem>
</template>

<script setup lang="ts">
import type { TableColumn, TabsItem } from '@nuxt/ui'

type Lang = 'curl' | 'go' | 'rust' | 'js' | 'py'
const lang = ref<Lang>('curl')
const langTabs: TabsItem[] = [
  { label: 'cURL', value: 'curl' },
  { label: 'Go', value: 'go' },
  { label: 'Rust', value: 'rust' },
  { label: 'JavaScript', value: 'js' },
  { label: 'Python', value: 'py' }
]
const langLabel = computed(() => langTabs.find(t => t.value === lang.value)?.label || 'cURL')

interface Param { name: string; pos: string; required: string; type: string; desc: string }
interface Field { name: string; type: string; desc: string }
interface Ep {
  id: string; method: string; path: string; summary: string; purpose: string;
  params: Param[]; codes: Record<Lang, string>; fields: Field[]; resp: string
}
interface Group { id: string; title: string; items: Ep[] }

const paramCols: TableColumn<Param>[] = [
  { accessorKey: 'name', header: '名称' },
  { accessorKey: 'pos', header: '位置' },
  { accessorKey: 'required', header: '必填' },
  { accessorKey: 'type', header: '类型' },
  { accessorKey: 'desc', header: '说明' }
]
const fieldCols: TableColumn<Field>[] = [
  { accessorKey: 'name', header: '字段' },
  { accessorKey: 'type', header: '类型' },
  { accessorKey: 'desc', header: '说明' }
]

const B = 'https://maimai.imikufans.cn/api'
const noParams: Param[] = []

function getCodes(path: string, extra = ''): Record<Lang, string> {
  const url = `${B}${path}${extra}`
  return {
    curl: `curl "${url}"`,
    go: `package main\n\nimport (\n\t"fmt"\n\t"io"\n\t"log"\n\t"net/http"\n)\n\nfunc main() {\n\tresp, err := http.Get("${url}")\n\tif err != nil {\n\t\tlog.Fatal(err)\n\t}\n\tdefer resp.Body.Close()\n\tbody, _ := io.ReadAll(resp.Body)\n\tfmt.Println(string(body))\n}`,
    rust: `#[tokio::main]\nasync fn main() -> Result<(), Box<dyn std::error::Error>> {\n\tlet body = reqwest::get("${url}").await?.text().await?;\n\tprintln!("{body}");\n\tOk(())\n}`,
    js: `const r = await fetch('${url}');\nconst d = await r.json();\nconsole.log(d);`,
    py: `import requests\n\nd = requests.get('${url}', timeout=10).json()\nprint(d)`
  }
}

const groups: Group[] = [
  {
    id: 'status', title: '实时状态', items: [
      {
        id: 'status', method: 'GET', path: '/api/status', summary: '实时状态(代理上游 /api/bot,20s 缓存)',
        purpose: '查询舞萌DX 服务器实时状态与延迟,是监控页的核心数据源。与上游 /api/bot 同结构,命中缓存带 X-Cache: HIT。',
        params: noParams, codes: getCodes('/status'),
        fields: [
          { name: 'version', type: 'string', desc: '上游数据版本' },
          { name: 'timestamp', type: 'string', desc: '上游数据时间' },
          { name: 'status / status_text', type: 'string', desc: '兼容旧字段:normal/anomaly/empty/maintenance' },
          { name: 'incident_level', type: 'string', desc: '兼容旧字段:normal/degraded/outage/maintenance' },
          { name: 'verdict', type: 'string', desc: '总判定:normal/recovering/degraded/outage/maintenance/nodata' },
          { name: 'verdict_text', type: 'string', desc: '总判定文案' },
          { name: 'services[]', type: 'array', desc: '六个探针:{key,state,state_text,latency,duration_text},key=net/aime/title/member/qr/bl' },
          { name: 'summary', type: 'string', desc: '摘要,如"服务器运行正常,5条异常,17条正常上报"' },
          { name: 'latency', type: 'object', desc: '{current_ms,load_text,volatility_text}' },
          { name: 'reports', type: 'object', desc: '{anomaly_count,normal_count}:近1小时异常/正常上报数' },
          { name: 'broadcast', type: 'object|null', desc: '{msg}:公告,为空忽略' }
        ],
        resp: `{
  "version": "26.10",
  "status": "normal",
  "verdict": "recovering",
  "verdict_text": "部分服务性能恢复",
  "services": [
    { "key": "net", "name": "NET", "state": "ok", "state_text": "正常", "latency": 98, "duration_text": "" },
    { "key": "member", "name": "公众号(NET)", "state": "recovering", "state_text": "性能恢复", "latency": 318, "duration_text": "2 分钟前恢复" }
  ],
  "summary": "服务器运行正常，5条异常，17条正常上报",
  "latency": { "current_ms": 25, "load_text": "流畅", "volatility_text": "波动大" },
  "reports": { "anomaly_count": 5, "normal_count": 17 },
  "broadcast": null
}`
      },
      {
        id: 'bot', method: 'GET', path: '/api/bot', summary: '兼容原路径,同 /api/status',
        purpose: '为兼容直连上游的老客户端保留的别名,与 /api/status 同缓存同结构,新接入请用 /api/status。',
        params: noParams, codes: getCodes('/bot'),
        fields: [{ name: '(全部)', type: '-', desc: '同 /api/status' }],
        resp: `同 /api/status(结构完全一致)。`
      }
    ]
  },
  {
    id: 'snapshot', title: '静态快照(1h 缓存)', items: [
      {
        id: 'top100', method: 'GET', path: '/api/top100', summary: '全服游玩 Top100',
        purpose: '按全服游玩次数排序的乐曲榜,供全服乐曲页渲染。封面为相对路径,需拼 https://mai.chongxi.us 前缀。',
        params: noParams, codes: getCodes('/top100'),
        fields: [
          { name: 'totalPlays', type: 'int', desc: '全服总游玩次数' },
          { name: 'songs[]', type: 'array', desc: '按名次排列:{id,name,plays,cover}' }
        ],
        resp: `{
  "totalPlays": 481002,
  "songs": [
    { "id": 731, "name": "妄想感傷代償連盟", "plays": 318703, "cover": "/top100-covers/731.webp" },
    { "id": 11558, "name": "神っぽいな", "plays": 294880, "cover": "/top100-covers/11558.webp" }
  ]
}`
      },
      {
        id: 'chart145', method: 'GET', path: '/api/chart145', summary: '定数 14.5–15.0 谱面排行',
        purpose: '越级排行数据源。注意上游 level 低标了一档(3≈真 MAS,4≈真 ReM),本站已用落雪 level_value 校准,见越级页 TIER_FIX。',
        params: noParams, codes: getCodes('/chart145'),
        fields: [
          { name: 'totalPlayers', type: 'int', desc: '合计游玩人次' },
          { name: 'songs[]', type: 'array', desc: '{rank,players,musicId,name,level,rate(定数),avg,med,mode,cover}' }
        ],
        resp: `{
  "totalPlayers": 4760955,
  "songs": [
    { "rank": 1, "players": 92360, "musicId": 834, "name": "PANDORA PARADOXXX", "level": 4, "rate": 15, "avg": 77.1, "med": 83.3, "mode": 101, "cover": "/chart145-covers/834.webp" },
    { "rank": 2, "players": 66315, "musicId": 363, "name": "Oshama Scramble!", "level": 3, "rate": 14.5, "avg": 93.4, "med": 98, "mode": 100.6, "cover": "/chart145-covers/363.webp" }
  ]
}`
      },
      {
        id: 'ratinglist', method: 'GET', path: '/api/ratinglist', summary: 'Rating 分布',
        purpose: '全服 Rating 百分一段位分布。不带 version 返回全版本,带则返回单版本(version 仅允许 dx2023/2024/2025/2026,非法返回 400)。',
        params: [{ name: 'version', pos: 'query', required: '否', type: 'string', desc: 'dx2023/dx2024/dx2025/dx2026,缺省返回全版本' }],
        codes: getCodes('/ratinglist', '?version=dx2026'),
        fields: [
          { name: 'versions[]', type: 'array', desc: '版本清单:{key,label,total}' },
          { name: 'data[]', type: 'array', desc: '各版本:{key,label,total,data[{bucket,count}]}(每 100 分一段)' }
        ],
        resp: `{
  "versions": [{ "key": "dx2026", "label": "DX2026", "total": 283092 }],
  "data": [{
    "key": "dx2026", "label": "DX2026", "total": 283092,
    "data": [{ "bucket": 100, "count": 10255 }, { "bucket": 200, "count": 8989 }]
  }]
}`
      }
    ]
  },
  {
    id: 'history', title: '本地历史(网关采集)', items: [
      {
        id: 'history', method: 'GET', path: '/api/history', summary: '点位列表,最新在前',
        purpose: '网关每 30 秒采集上游落盘的本地历史,历史页与图表的数据来源。网关未启动时无数据(无法兜底上游)。',
        params: [{ name: 'limit', pos: 'query', required: '否', type: 'int', desc: '默认 288,最大 2000' }],
        codes: getCodes('/history', '?limit=2'),
        fields: [
          { name: 'points[]', type: 'array', desc: '最新在前:{ts(毫秒),verdict,verdict_text,latency_ms,anomaly,normal,ok_count,down_count,maint,services[{key,state,latency_ms}]}' },
          { name: 'maint', type: 'bool', desc: '维护点:上游报 maintenance 或落在常规窗口(默认北京时间每日 04:00-07:00)' }
        ],
        resp: `{
  "points": [{
    "ts": 1791718786836, "verdict": "recovering", "verdict_text": "部分服务性能恢复",
    "latency_ms": 25, "anomaly": 5, "normal": 17,
    "ok_count": 5, "down_count": 0, "maint": false,
    "services": [{ "key": "net", "state": "ok", "latency_ms": 98 }]
  }]
}`
      },
      {
        id: 'history-summary', method: 'GET', path: '/api/history/summary', summary: '近 12h 可用率与平均延迟',
        purpose: '历史页仪表盘数字。uptime_pct 与 availability 全服务检查数口径同源(维护点位剔除)。',
        params: noParams, codes: getCodes('/history/summary'),
        fields: [
          { name: 'total', type: 'int', desc: '非维护采集点数' },
          { name: 'uptime_pct', type: 'string', desc: '可用率百分比(两位小数)' },
          { name: 'avg_latency_ms', type: 'int', desc: '平均延迟' },
          { name: 'latest', type: 'object', desc: '{ts,verdict,verdict_text}' },
          { name: 'maint_points', type: 'int', desc: '维护点数' },
          { name: 'window_hours', type: 'int', desc: '固定 12' }
        ],
        resp: `{
  "avg_latency_ms": 40,
  "latest": { "ts": 1791718786836, "verdict": "recovering", "verdict_text": "部分服务性能恢复" },
  "maint_points": 0, "total": 451, "uptime_pct": "82.67", "window_hours": 12
}`
      },
      {
        id: 'history-range', method: 'GET', path: '/api/history/range', summary: '按毫秒时间戳范围查历史',
        purpose: '按时间窗口过滤历史点位,供自定义范围查询。',
        params: [
          { name: 'from', pos: 'query', required: '否', type: 'int', desc: '起始毫秒时间戳' },
          { name: 'to', pos: 'query', required: '否', type: 'int', desc: '结束毫秒时间戳' },
          { name: 'limit', pos: 'query', required: '否', type: 'int', desc: '默认 2000,最大 5000' }
        ],
        codes: getCodes('/history/range', '?from=1791718000000&limit=1'),
        fields: [{ name: 'points[]', type: 'array', desc: '同 /api/history 点位结构' }],
        resp: `{
  "points": [{
    "ts": 1791718786836, "verdict": "recovering", "verdict_text": "部分服务性能恢复",
    "latency_ms": 25, "anomaly": 5, "normal": 17, "maint": false
  }]
}`
      }
    ]
  },
  {
    id: 'stats', title: '统计聚合(本地计算)', items: [
      {
        id: 'incidents', method: 'GET', path: '/api/incidents', summary: '故障事件列表',
        purpose: 'verdict 非 normal 的连续点位自动提取为一段事件,最新在前。',
        params: noParams, codes: getCodes('/incidents'),
        fields: [
          { name: 'incidents[]', type: 'array', desc: '{start_ts,end_ts,duration_sec(秒),worst_verdict,verdict_text,ongoing}' },
          { name: 'count', type: 'int', desc: '事件数' }
        ],
        resp: `{
  "count": 3,
  "incidents": [{
    "duration_sec": 94, "end_ts": 1791718786836, "ongoing": true,
    "start_ts": 1791718697190, "verdict_text": "部分服务性能恢复", "worst_verdict": "degraded"
  }]
}`
      },
      {
        id: 'latency-stats', method: 'GET', path: '/api/latency/stats', summary: '延迟分位统计',
        purpose: '指定窗口内的延迟分布,维护点位剔除。',
        params: [{ name: 'hours', pos: 'query', required: '否', type: 'int', desc: '默认 24,最大 168' }],
        codes: getCodes('/latency/stats', '?hours=24'),
        fields: [
          { name: 'hours / count', type: 'int', desc: '窗口小时数 / 样本数' },
          { name: 'min / p50 / p90 / p99 / max / avg / current', type: 'int', desc: '毫秒' }
        ],
        resp: `{ "avg": 40, "count": 451, "current": 25, "hours": 24, "max": 1955, "min": 25, "p50": 28, "p90": 31, "p99": 291 }`
      },
      {
        id: 'reports-trend', method: 'GET', path: '/api/reports/trend', summary: '上报趋势(正序)',
        purpose: '异常/正常上报数随时间序列,正序返回可直接画线,也是图表概览上报档的数据源。',
        params: [{ name: 'limit', pos: 'query', required: '否', type: 'int', desc: '默认 288,最大 2000' }],
        codes: getCodes('/reports/trend', '?limit=2'),
        fields: [{ name: 'trend[]', type: 'array', desc: '{ts,anomaly,normal}' }],
        resp: `{ "trend": [{ "anomaly": 5, "normal": 17, "ts": 1791718756898 }] }`
      },
      {
        id: 'services-availability', method: 'GET', path: '/api/services/availability', summary: '各服务可用率',
        purpose: '服务可用性柱状图 + 六宫格的数据源。maint 柱前端画满格灰;overall_pct 与 history/summary 的 uptime_pct 同值。',
        params: [{ name: 'hours', pos: 'query', required: '否', type: 'int', desc: '默认 12,最大 168' }],
        codes: getCodes('/services/availability', '?hours=12'),
        fields: [
          { name: 'hours', type: 'int', desc: '窗口小时数' },
          { name: 'buckets[]', type: 'array', desc: '48 柱:{ts,ratio,maint}(maint 柱置灰)' },
          { name: 'services[]', type: 'array', desc: '{key,name,availability_pct,avg_latency_ms,checks}' },
          { name: 'overall_pct', type: 'string', desc: '全服务总可用率(与 summary 同源)' }
        ],
        resp: `{
  "hours": 12, "overall_pct": "82.67",
  "buckets": [{ "maint": false, "ratio": 1, "ts": 1791675599081 }],
  "services": [{ "availability_pct": "93.8", "avg_latency_ms": 114, "checks": 451, "key": "net", "name": "NET" }]
}`
      },
      {
        id: 'services-latency', method: 'GET', path: '/api/services/latency', summary: '各服务延迟序列',
        purpose: '图表概览延迟档的多线数据源,自动降采样。maint_spans 为维护区间,前端压 0 灰线。',
        params: [
          { name: 'hours', pos: 'query', required: '否', type: 'int', desc: '默认 24,最大 168' },
          { name: 'maxpoints', pos: 'query', required: '否', type: 'int', desc: '默认 240,最大 1000' }
        ],
        codes: getCodes('/services/latency', '?hours=24&maxpoints=3'),
        fields: [
          { name: 'hours', type: 'int', desc: '窗口小时数' },
          { name: 'services[]', type: 'array', desc: '{key,name,series[[ts,ms]]}' },
          { name: 'maint_spans', type: 'array|null', desc: '维护区间 [[start,end],...](毫秒,无则 null)' }
        ],
        resp: `{
  "hours": 24, "maint_spans": null,
  "services": [{ "key": "net", "name": "NET", "series": [[1791705194636, 108]] }]
}`
      },
      {
        id: 'overview', method: 'GET', path: '/api/overview', summary: '聚合看板',
        purpose: '一次请求拿到状态原字段 + history_summary + latency_stats + ongoing_incidents,首页可用它一次渲染(本站目前为分开请求,效果等价)。',
        params: noParams, codes: getCodes('/overview'),
        fields: [
          { name: '(状态原字段)', type: '-', desc: '同 /api/status' },
          { name: 'history_summary', type: 'object', desc: '同 /api/history/summary' },
          { name: 'latency_stats', type: 'object', desc: '同 /api/latency/stats(24h)' },
          { name: 'ongoing_incidents', type: 'array', desc: '进行中的事件(同 incidents 项结构)' }
        ],
        resp: `{
  "verdict": "recovering", "verdict_text": "部分服务性能恢复",
  "history_summary": { "uptime_pct": "82.67", "avg_latency_ms": 40 },
  "latency_stats": { "p50": 28, "p99": 291 },
  "ongoing_incidents": [{ "ongoing": true, "worst_verdict": "degraded" }]
}`
      }
    ]
  },
  {
    id: 'stream', title: '实时推送', items: [
      {
        id: 'stream', method: 'GET', path: '/api/stream', summary: 'SSE 实时推送',
        purpose: '订阅服务器状态实时流。连接后先吐一次当前状态,之后每次采集(约 30s)广播 event: status,心跳 : ping 每 20s。断线重连即可,注意反代须 proxy_buffering off。',
        params: noParams,
        codes: {
          curl: `curl -N "${B}/stream"`,
          go: `package main\n\nimport (\n\t"bufio"\n\t"fmt"\n\t"log"\n\t"net/http"\n)\n\nfunc main() {\n\tresp, err := http.Get("${B}/stream")\n\tif err != nil {\n\t\tlog.Fatal(err)\n\t}\n\tdefer resp.Body.Close()\n\tsc := bufio.NewScanner(resp.Body)\n\tfor sc.Scan() {\n\t\tfmt.Println(sc.Text())\n\t}\n}`,
          rust: `use futures_util::StreamExt;\n\n#[tokio::main]\nasync fn main() -> Result<(), Box<dyn std::error::Error>> {\n\tlet mut s = reqwest::get("${B}/stream").await?.bytes_stream();\n\twhile let Some(chunk) = s.next().await {\n\t\tprint!("{}", String::from_utf8_lossy(&chunk?));\n\t}\n\tOk(())\n}`,
          js: `const es = new EventSource('${B}/stream');\nes.addEventListener('status', e => console.log(JSON.parse(e.data)));\nes.onerror = () => console.warn('stream disconnected');`,
          py: `import requests\n\nwith requests.get('${B}/stream', stream=True, timeout=65) as r:\n    for line in r.iter_lines(decode_unicode=True):\n        if line.startswith('data:'):\n            print(line[5:].strip())`
        },
        fields: [
          { name: 'event: status', type: 'SSE', desc: 'data 为 JSON(同 /api/status 结构)' },
          { name: ': ping', type: 'SSE', desc: '每 20s 心跳' }
        ],
        resp: `event: status\ndata: {"verdict":"recovering","verdict_text":"部分服务性能恢复", ...}\n\n: ping`
      }
    ]
  },
  {
    id: 'report', title: '手动上报', items: [
      {
        id: 'report-captcha', method: 'GET', path: '/api/report/captcha', summary: '领取上报验证码',
        purpose: '上报前先领取 8 位数字验证码(后端随机生成,5 分钟有效,一次有效),上报时原样带回 X-Captcha-ID / X-Captcha-Code。',
        params: noParams, codes: getCodes('/report/captcha'),
        fields: [
          { name: 'captcha_id', type: 'string', desc: '验证码 ID' },
          { name: 'code', type: 'string', desc: '8 位数字验证码(展示给用户照打)' },
          { name: 'expires_in', type: 'int', desc: '有效秒数:300' }
        ],
        resp: `{ "captcha_id": "103d1d0fff6bcb7b72b07d29131af067", "code": "09584263", "expires_in": 300 }`
      },
      {
        id: 'report', method: 'POST', path: '/api/report', summary: '上报透传到上游 bot-post',
        purpose: '机台异常手动上报。Body 为上报对象数组,签名算法见上游文档(先 SHA-256 取 bodyHash,再 HMAC-SHA256 对 "POST:/bot-post:{clientId}:{ts}:{bodyHash}" 签名,十六进制)。网关验本地验证码后透传上游,成功 200(透传上游原样),401 为上游鉴权失败,429 限流。建议 30s 聚合一次。类型代码 t:101 机台断网 / 102 无法登录 / 103 NET 打不开 / 104 二维码 / 105 成绩上传失败 / 202 小黑屋 / 300 排队时长(v 为秒) / 501 恢复正常。',
        params: [
          { name: 'X-Client-ID', pos: 'header', required: '是', type: 'string', desc: '自定义 ID(英文字母+数字),私钥发邮件至 qwq@chongxi.us 获取' },
          { name: 'X-Timestamp', pos: 'header', required: '是', type: 'string', desc: '毫秒时间戳字符串' },
          { name: 'X-Signature', pos: 'header', required: '是', type: 'string', desc: 'HMAC-SHA256 十六进制摘要' },
          { name: 'X-Captcha-ID', pos: 'header', required: '是', type: 'string', desc: '验证码 ID(先调 captcha 接口)' },
          { name: 'X-Captcha-Code', pos: 'header', required: '是', type: 'string', desc: '用户照打的 8 位数字' },
          { name: 'body', pos: 'body', required: '是', type: 'array', desc: '[{t,v,r,bot_display_name?}],r 建议 "WEB",缺验证码返回 400,错/过期返回 403' }
        ],
        codes: {
          curl: `# 1. 领码(记下 captcha_id 与 code,code 给用户照打)\ncurl "${B}/report/captcha"\n\n# 2. 上报(先本地算好 bodyHash 与签名,见 Go 例)\nBODY='[{"t":103,"v":1,"r":"WEB","bot_display_name":"路人"}]'\ncurl -X POST "${B}/report" \\\n  -H 'Content-Type: application/json' \\\n  -H 'X-Client-ID: <your-key>' \\\n  -H "X-Timestamp: $(date +%s%3N)" \\\n  -H 'X-Signature: <hmac-sha256-hex>' \\\n  -H 'X-Captcha-ID: <captcha_id>' \\\n  -H 'X-Captcha-Code: <用户照打的8位>' \\\n  -d "$BODY"`,
          go: `package main\n\nimport (\n\t"crypto/hmac"\n\t"crypto/sha256"\n\t"encoding/hex"\n\t"fmt"\n\t"io"\n\t"log"\n\t"net/http"\n\t"strings"\n\t"time"\n)\n\nfunc main() {\n\tclientID, privKey := "<your-key>", "<your-key>"\n\tbody := \`[{"t":103,"v":1,"r":"WEB"}]\`\n\tts := fmt.Sprint(time.Now().UnixMilli())\n\tbh := sha256.Sum256([]byte(body))\n\tmac := hmac.New(sha256.New, []byte(privKey))\n\tmac.Write([]byte("POST:/bot-post:" + clientID + ":" + ts + ":" + hex.EncodeToString(bh[:])))\n\treq, _ := http.NewRequest("POST", "${B}/report", strings.NewReader(body))\n\treq.Header.Set("Content-Type", "application/json")\n\treq.Header.Set("X-Client-ID", clientID)\n\treq.Header.Set("X-Timestamp", ts)\n\treq.Header.Set("X-Signature", hex.EncodeToString(mac.Sum(nil)))\n\treq.Header.Set("X-Captcha-ID", "<captcha_id>")\n\treq.Header.Set("X-Captcha-Code", "<用户照打的8位>")\n\tresp, err := http.DefaultClient.Do(req)\n\tif err != nil {\n\t\tlog.Fatal(err)\n\t}\n\tdefer resp.Body.Close()\n\trb, _ := io.ReadAll(resp.Body)\n\tfmt.Println(resp.StatusCode, string(rb))\n}`,
          rust: `use hmac::{Hmac, Mac};\nuse sha2::Sha256;\n\n#[tokio::main]\nasync fn main() -> Result<(), Box<dyn std::error::Error>> {\n\tlet (client_id, priv_key) = ("<your-key>", "<your-key>");\n\tlet body = r#"[{"t":103,"v":1,"r":"WEB"}]"#;\n\tlet ts = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH)?.as_millis().to_string();\n\tlet body_hash = hex::encode(sha2::Sha256::digest(body.as_bytes()));\n\tlet mut mac = Hmac::<Sha256>::new_from_slice(priv_key.as_bytes())?;\n\tmac.update(format!("POST:/bot-post:{client_id}:{ts}:{body_hash}").as_bytes());\n\tlet sig = hex::encode(mac.finalize().into_bytes());\n\tlet res = reqwest::Client::new().post("${B}/report")\n\t\t.header("Content-Type", "application/json")\n\t\t.header("X-Client-ID", client_id).header("X-Timestamp", ts).header("X-Signature", sig)\n\t\t.header("X-Captcha-ID", "<captcha_id>").header("X-Captcha-Code", "<用户照打的8位>")\n\t\t.body(body.to_string()).send().await?;\n\tprintln!("{} {}", res.status(), res.text().await?);\n\tOk(())\n}`,
          js: `const clientId = '<your-key>', privKey = '<your-key>';\nconst body = JSON.stringify([{ t: 103, v: 1, r: 'WEB' }]);\nconst ts = String(Date.now());\nconst buf = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(body));\nconst bodyHash = [...new Uint8Array(buf)].map(b => b.toString(16).padStart(2, '0')).join('');\nconst key = await crypto.subtle.importKey('raw', new TextEncoder().encode(privKey), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);\nconst sigBuf = await crypto.subtle.sign('HMAC', key, new TextEncoder().encode(\`POST:/bot-post:\${clientId}:\${ts}:\${bodyHash}\`));\nconst sig = [...new Uint8Array(sigBuf)].map(b => b.toString(16).padStart(2, '0')).join('');\nconst r = await fetch('${B}/report', {\n  method: 'POST',\n  headers: { 'Content-Type': 'application/json', 'X-Client-ID': clientId, 'X-Timestamp': ts, 'X-Signature': sig, 'X-Captcha-ID': '<captcha_id>', 'X-Captcha-Code': '<用户照打的8位>' },\n  body\n});\nconsole.log(r.status, await r.text());`,
          py: `import hashlib, hmac, json, time\nimport requests\n\nclient_id, priv_key = '<your-key>', '<your-key>'\nbody = json.dumps([{'t': 103, 'v': 1, 'r': 'WEB'}], separators=(',', ':'))\nts = str(int(time.time() * 1000))\nbody_hash = hashlib.sha256(body.encode()).hexdigest()\nsig = hmac.new(priv_key.encode(), f'POST:/bot-post:{client_id}:{ts}:{body_hash}'.encode(), hashlib.sha256).hexdigest()\nr = requests.post('${B}/report', data=body, headers={\n    'Content-Type': 'application/json', 'X-Client-ID': client_id,\n    'X-Timestamp': ts, 'X-Signature': sig,\n    'X-Captcha-ID': '<captcha_id>', 'X-Captcha-Code': '<用户照打的8位>',\n}, timeout=10)\nprint(r.status_code, r.text)`
        },
        fields: [
          { name: '200', type: 'status', desc: '已受理(透传上游原样返回)' },
          { name: '400', type: 'status', desc: '缺头或 Body 非数组,如 {"error":"missing X-Captcha-ID / X-Captcha-Code"}' },
          { name: '403', type: 'status', desc: '验证码无效/过期:{"error":"invalid or expired captcha"}' },
          { name: '401', type: 'status', desc: '上游鉴权失败(检查 Client ID / Private Key)' },
          { name: '429', type: 'status', desc: '被限流,30 秒后再试' }
        ],
        resp: `HTTP 200(透传上游原样)\n\n验证码错误时:\n{"error":"invalid or expired captcha"}`
      }
    ]
  },
  {
    id: 'lxns', title: '落雪曲目', items: [
      {
        id: 'lxns-songs', method: 'GET', path: '/api/lxns/songs', summary: '公开曲目列表(1h 缓存)',
        purpose: '落雪曲库全量(含各难度定数 level_value),本站用它校准越级页难度档,无需登录。',
        params: noParams, codes: getCodes('/lxns/songs'),
        fields: [
          { name: 'songs[]', type: 'array', desc: '{id,title,artist,genre,bpm,version,difficulties{standard[],dx[]}}' },
          { name: 'difficulties[]', type: 'array', desc: '{type,difficulty(0-4=BAS/ADV/EXP/MAS/ReM),level,level_value(定数),note_designer,version}' },
          { name: 'genres / versions', type: 'array', desc: '分类与版本表' }
        ],
        resp: `{
  "songs": [{
    "id": 8, "title": "True Love Song", "bpm": 150, "version": 10000,
    "difficulties": { "standard": [
      { "type": "standard", "difficulty": 3, "level": "12", "level_value": 12.4 }
    ], "dx": [] }
  }]
}`
      }
    ]
  },
  {
    id: 'ops', title: '运维', items: [
      {
        id: 'healthz', method: 'GET', path: '/api/healthz', summary: '健康检查(同域反代口径)',
        purpose: '网关存活探针,部署验证用。网关原生在域名根下 /healthz,为适配同域反代只放行 /api/* 的场景,加了 /api/healthz 别名,两者等价。',
        params: noParams,
        codes: {
          curl: `curl "https://maimai.imikufans.cn/api/healthz"`,
          go: `package main\n\nimport (\n\t"fmt"\n\t"io"\n\t"log"\n\t"net/http"\n)\n\nfunc main() {\n\tresp, err := http.Get("https://maimai.imikufans.cn/api/healthz")\n\tif err != nil {\n\t\tlog.Fatal(err)\n\t}\n\tdefer resp.Body.Close()\n\tbody, _ := io.ReadAll(resp.Body)\n\tfmt.Println(string(body))\n}`,
          rust: `#[tokio::main]\nasync fn main() -> Result<(), Box<dyn std::error::Error>> {\n\tlet body = reqwest::get("https://maimai.imikufans.cn/api/healthz").await?.text().await?;\n\tprintln!("{body}");\n\tOk(())\n}`,
          js: `const r = await fetch('https://maimai.imikufans.cn/api/healthz');\nconsole.log(await r.json());`,
          py: `import requests\n\nprint(requests.get('https://maimai.imikufans.cn/api/healthz', timeout=10).json())`
        },
        fields: [
          { name: 'ok', type: 'bool', desc: '存活' },
          { name: 'time', type: 'string', desc: '网关时间(RFC3339)' }
        ],
        resp: `{ "ok": true, "time": "2026-10-11T19:39:51+08:00" }`
      },
      {
        id: 'openapi', method: 'GET', path: '/api/openapi.json', summary: 'OpenAPI 3.0 规格',
        purpose: '机器可读的接口规格(本页即其人工版),可导入 Postman / 代码生成。',
        params: noParams, codes: getCodes('/openapi.json'),
        fields: [
          { name: 'openapi', type: 'string', desc: '固定 3.0.3' },
          { name: 'info / paths', type: 'object', desc: '标题版本与路径表' }
        ],
        resp: `{
  "openapi": "3.0.3",
  "info": { "title": "Maimai Status Gateway", "version": "1.1.0" },
  "paths": { "/healthz": { ... }, "/api/status": { ... } }
}`
      }
    ]
  }
]
</script>
