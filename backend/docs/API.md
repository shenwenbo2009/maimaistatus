# 网关 API 参考

基址默认 `http://localhost:9560`。所有响应均带 `X-Data-Source: 数据来源于 isMaiDown by Chongxi`。
完整机器可读规格见 `GET /api/openapi.json`(本目录 `openapi.json` 即内嵌来源)。

上游公开接口文档:https://mai.chongxi.us/docs

## 1. 透传类(代理 + 缓存上游)

| 方法 | 路径 | 缓存 | 说明 |
| --- | --- | --- | --- |
| GET | `/api/status`(`/api/bot` 兼容) | 20s | 服务器状态与延迟,结构同上游 `/api/bot` |
| GET | `/api/top100` | 1h | 全服游玩 Top100:`{totalPlays, songs[{id,name,plays,cover}]}` |
| GET | `/api/chart145` | 1h | 越级排行:`{totalPlayers, songs[{rank,players,musicId,name,level,rate,avg,med,mode,cover}]}` |
| GET | `/api/ratinglist[?version=]` | 1h | Rating 分布;`version` 仅允许 `dx2023/2024/2025/2026` |
| POST | `/api/report` | — | 手动上报透传到 `https://maiapi.chongxi.us/bot-post`,需带 `X-Client-ID` / `X-Timestamp` / `X-Signature`(HMAC-SHA256,见上游文档) **+ 本地验证码 `X-Captcha-ID` / `X-Captcha-Code`(先 `GET /api/report/captcha` 领取 8 位数字,5 分钟有效,一次有效)**,Body 为 JSON 数组,上游要求约 30s 聚合发送一次 |
| GET | `/api/report/captcha` | — | 领取上报验证码:`{captcha_id,code,expires_in:300}`,上报时原样带回 |

命中缓存时响应带 `X-Cache: HIT`,否则 `MISS`。

## 2. 本地历史(网关每 POLL_INTERVAL_SEC 秒采集上游落盘)

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/history?limit=288` | 点位列表,最新在前。点位:`{ts,verdict,verdict_text,latency_ms,anomaly,normal,ok_count,down_count,maint}`(`maint` 为维护点:上游报 maintenance 或落在 `MAINT_WINDOWS` 常规窗口,默认北京时间每日 04:00-07:00) |
| GET | `/api/history/summary` | 近 12h:`{total,uptime_pct,avg_latency_ms,latest,maint_points,window_hours}`。`uptime_pct` 与 `availability` 全服务检查数口径同源(维护点位剔除不计) |
| GET | `/api/history/range?from=&to=&limit=` | `from/to` 为毫秒时间戳,按范围过滤 |

## 3. 统计与聚合(由本地历史计算)

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/incidents` | 故障事件:`{incidents[{start_ts,end_ts,duration_sec,worst_verdict,verdict_text,ongoing}],count}`。verdict 非 `normal` 的连续点位即记为一段事件 |
| GET | `/api/latency/stats?hours=24` | 延迟分位:`{min,p50,p90,p99,max,avg,current,count}`,`hours` 最大 168 |
| GET | `/api/reports/trend?limit=288` | 上报趋势:`{trend[{ts,anomaly,normal}]}`,按时间正序,方便直接画线 |
| GET | `/api/services/availability?hours=12` | 各服务可用率:`{hours,buckets[{ts,ratio,maint,empty}],services[...],overall_pct}`(`maint` 柱前端画满格灰,`empty` 桶前端留白;`overall_pct` 为全服务总可用率,与 `history/summary` 的 `uptime_pct` 同值,维护点位不计入) |
| GET | `/api/services/latency?hours=24` | 各服务延迟序列(自动降采样):`{services[{key,name,series[[ts,ms]]}],maint_spans[[start,end]]}`,维护段前端压 0 灰线 |
| GET | `/api/overview` | 聚合看板:状态原字段 + `history_summary` + `latency_stats` + `ongoing_incidents`,首页一次请求即可渲染 |

## 4. 实时推送

`GET /api/stream` — SSE(`text/event-stream`)。连接后先吐一次当前状态,之后每次采集完成广播:

```
event: status
data: {"verdict":"normal",...}
```

心跳 `: ping` 每 20s。前端可用 `EventSource` 订阅,断线重连即可。

```js
const es = new EventSource('http://localhost:9560/api/stream')
es.addEventListener('status', e => render(JSON.parse(e.data)))
```

## 5. 运维

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/healthz` | `{ok:true,time}` |
| GET | `/api/openapi.json` | OpenAPI 3.0 规格 |

## 6. 落雪公开曲目(查分 OAuth 已下线,仅保留曲目列表)

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/lxns/songs` | 公开曲目列表(1h 缓存,无需登录) |

## 错误格式

上游不可达时返回 `502 {"error":"upstream unreachable","detail":"..."}`;参数错误返回 `400 {"error":"..."}`。
