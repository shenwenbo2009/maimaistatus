# 华立服务器死了吗?— 舞萌DX 状态监控(仿 isMaiDown)

实时监测舞萌DX 服务器状态的前后端项目：Go 网关代理 + 缓存上游接口、定时采集落盘本地历史；Nuxt 4 前端做监控看板、历史曲线、全服榜单与手动上报。

- 线上：`https://maimai.imikufans.cn` · 后端 `:9560` / 前端 `:9561`
- 数据来源于 [isMaiDown by Chongxi](https://mai.chongxi.us)，上游文档：<https://mai.chongxi.us/docs>。本站数据免费公开，可自由使用 / 分发 / 商业使用，**使用时必须标注来源**。
- 本项目为非官方平台，与华立科技及世嘉官方无任何关联。

## 页面一览(`frontend/pages/`)

| 路由 | 页面 | 说明 |
| --- | --- | --- |
| `/` | 监控 | 服务器状态(verdict 图标 + 随色容器 + 最近三次上报 Timeline)+ 状态概览(图标服务行)+ 服务可用性(ECharts 柱状 + 六宫格)+ 图表概览(ECharts 延迟多线 / 上报双柱,右上可切)。30s 静默刷新，只换数值图表不闪整页 |
| `/history` | 历史 | 近 12h 可用率(维护剔除,已下线旧算法见 git 历史)/ 平均延迟 / 采集点数 + ECharts 延迟曲线(与监控页同构组件 `LatencyChart`)+ 故障事件 Timeline + 最近记录 `UTable` |
| `/top100` | 全服乐曲 | `UTable` 六列(#/封面/曲名/ID/游玩人数/占比)+ `UInput` 搜索 + STD(蓝,<10000)/DX(橙,1w+)徽章 + 20 首分页 |
| `/ratinglist` | 全服排行 | 默认 DX2026，`UTabs` 切版本(徽章带人数)+ `UProgress` 分布条(峰值标绿) |
| `/chart145` | 越级排行 | 搜索 + `UTable`(#/封面/曲名/难度/占比三连/人数)+ 难度 Tabs(ADV/EXP/MAS/ReM,KOP 叫法)+ STD/DX 徽章 + 难度徽章带定数(如 `ReM 15`) |
| `/report` | 我要上报 | 三步向导(选类型 → 填次数/显示名 → 8 位验证码复打),与监控页弹窗复用同一 `ReportWizard` 组件 |
| `/about` | 关于 | 数据来源 + 架构，右上按钮进开发者文档 |
| `/docs` | 开发者文档 | 20 个接口全参考：用途、请求参数、使用例(cURL/Go/Rust/JavaScript/Python)、响应字段、线上实抓响应例。Base URL `https://maimai.imikufans.cn/api` |

## 架构

- `backend/` — Go 网关，**标准库、零第三方依赖**：TTL 缓存代理上游接口；每 30s 采集落盘 `data/history.jsonl`；SSE 实时推送；手动上报验本地验证码后透传上游。20 个路由，规格见 `GET /api/openapi.json` 与 `backend/docs/API.md`。
- `frontend/` — Nuxt 4 + Nuxt UI + ECharts + Tailwind：浅色/深色/跟随系统(右上切换)，全页统一 `components/DesignSystem.vue` 页壳，顶栏 `UNavigationMenu` 在 `app.vue` 一处管理。

## 核心机制(实现位置备查)

- 维护窗口(`backend/main.go:isMaintPoint`,默认 `MAINT_WINDOWS=04:00-07:00` 北京时间)：维护点不计入可用率；可用性柱满格灰、延迟线压 0 灰(`maint_spans`)；`history/summary` 近 12h 与 `availability overall_pct` 同源(全服务检查数口径，线上实测一致)。
- 难度校准(`frontend/pages/chart145.vue:TIER_FIX`)：上游 level 系统性低标一档(3≈真 MAS,4≈真 ReM)，已用落雪 `level_value` 逐首核验，例外收表、默认走系统规则。
- 上报链路：`GET /api/report/captcha`(8 位码，5 分钟、一次有效)→ 本地 HMAC-SHA256 签名 → `POST /api/report` 验码后透传 `bot-post`。密钥只放前端环境变量(`NUXT_PUBLIC_REPORT_CLIENT_ID/PRIVATE_KEY`)，默认占位 `<your-key>`。
- 查分 OAuth 已下线，仅保留 `GET /api/lxns/songs` 公开曲库(1h 缓存)。

## 快速启动

```bash
# 终端 1:后端(需 Go ≥1.23,需联网访问上游)
cd backend
go build -o gateway .
PORT=9560 ./gateway  # 健康检查:curl http://127.0.0.1:9560/healthz

# 终端 2:前端(需 Node ≥20)
cd frontend
npm install
npm run dev  # http://localhost:9561,网关默认 http://localhost:9560
```

生产构建：`cd frontend && NUXT_PUBLIC_API_BASE=https://maimai.imikufans.cn/api npm run build && PORT=9561 node .output/server/index.mjs`，反代与证书见 `deploy/`。

## 环境变量

后端(`backend/.env.example`)：`PORT`(9560)、`UPSTREAM_BASE`、`REPORT_UPSTREAM`、`POLL_INTERVAL_SEC`(30)、`DATA_DIR`、`HISTORY_MAX`、`CORS_ALLOW`、`MAINT_WINDOWS`(04:00-07:00，逗号分隔，支持跨日)。

前端(`frontend/.env.example`)：`NUXT_PUBLIC_API_BASE`(网关地址)、`NUXT_PUBLIC_REPORT_CLIENT_ID` / `NUXT_PUBLIC_REPORT_PRIVATE_KEY`(上报签名密钥，Client ID 自定义、私钥发邮件至 `qwq@chongxi.us` 获取)。

## 目录

- `backend/` — `main.go`(网关)、`lxns.go`(落雪曲库代理)、`maint_test.go`、`openapi.json` 内嵌规格、`docs/API.md` 接口速查、`data/history.jsonl` 本地历史。
- `frontend/` — `app.vue`(顶栏+导航)、`components/`(DesignSystem/ColorModeButton/ReportWizard/ReportModal/LatencyChart)、`pages/`(见上表)、`composables/useApi.ts`(网关优先、上游兜底)。
- `deploy/` — `DEPLOY.md` 上线清单、`nginx.conf` 反代(含 SSE 关缓冲)、`maimai-gateway.service` systemd。
- `container/` — 单容器编排(Alpine + 国内源 + Go/Node)：`Dockerfile`、`docker-compose.yml`、`entrypoint.sh`，`cd container && docker compose up -d --build`，详见 `container/README.md`。
