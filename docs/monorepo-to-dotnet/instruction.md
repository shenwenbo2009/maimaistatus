# 从 Go + Vue 分体架构迁移到 .NET Blazor + ASP.NET Core 统一架构

> 项目：华立服务器死了吗？— 舞萌DX 状态监控（仿 isMaiDown）
> 目标：将原有的 Go 网关 + Nuxt 4 前端分体架构，重构为 .NET Blazor + ASP.NET Core 统一架构
> 文档位置：`docs/monorepo-to-dotnet/迁移指南.md`

---

## ⏱ 迁移进度（2026-11-08 更新）

### 已完成

- **Phase 1 基础设施 ✅**：`MaimaiStatus.slnx` + 5 项目（Core/Web/Shared + 2 测试），`Directory.Build.props` / `Directory.Packages.props` 集中管理，.NET SDK 10.0.114。
- **Phase 2 后端 API ✅**：21 条路由全部移植并通过验收——
  - 架构定案：**Core 改为类库**（`FrameworkReference Microsoft.AspNetCore.App`），由 **Web 作为唯一宿主**承载（Blazor + Minimal API 单进程），宿主调用 `AddMaimaiCore()` + `MapMaimaiEndpoints()` 接线；
  - 服务层：`UpstreamGateway`(TTL 缓存) / `HistoryStore`(JSONL) / `MaintenanceWindow` / `PollingService`(BackgroundService) / `SseHub` / `LxnsProxy` / `CaptchaService` / `ReportSigner` / `HistoryAnalytics`(全部统计聚合)；
  - 配置兼容旧部署：裸环境变量 `UPSTREAM_BASE` / `DATA_DIR` / `MAINT_WINDOWS` ... 优先，其次 `Maimai:*` 分节；
  - 验收：`dotnet build` 0 警告 0 错误；`dotnet test` **26/26 通过**；与旧 Go 网关同数据对拍 **14 端点全部一致**（仅毫秒级采样漂移）；SSE 首帧回放 + 采集广播 + 20s 心跳实测通过；上报 400/上游 401 透传、CORS 预检实测通过。

### 本轮关键决策

| 决策 | 结论 |
|------|------|
| 托管形态 | **单进程单端口 9561**（appsettings `Urls`，可用 `ASPNETCORE_URLS` 覆盖；nginx 后续只需把 `/api/` 上游也指到 9561） |
| UI 框架 | **MudBlazor**（Phase 3 引入） |
| 图表 | **JS Interop + ECharts**（复用现有配置：dataZoom/维护灰段/图例联动） |
| 上报验证码 | **暂不启用**：`POST /api/report` 只验签名头并透传；`GET /api/report/captcha` 保留（维持 21 路由/OpenAPI 不变），后续要接再补 6 行校验 |
| X-Data-Source | 响应头改 ASCII：`isMaiDown by Chongxi (https://mai.chongxi.us)`（Kestrel 禁止非 ASCII 头，中文归属文案放页面/文档） |
| 私钥位置 | 报名签名私钥只留服务端（`ReportSigner`），不再下发浏览器 |

### 待办

- [x] Phase 3：Blazor 前端（MudBlazor 重构 8 页面 + ECharts Interop 图表 + 深浅色）
- [ ] Phase 4：bUnit / WebApplicationFactory 集成测试、并行运行对比
- [ ] Phase 5：Docker 镜像改 .NET 发布、nginx 单端口分流、systemd、下线 Go+Nuxt
- [ ] `container/` 编排改为 `dotnet publish` 单阶段

---


## 一、现有架构概览

### 1.1 当前技术栈

| 层级 | 技术 | 说明 |
|------|------|------|
| 后端 | Go 1.23+ | 标准库零第三方依赖，网关代理 + 缓存 + 定时采集 + SSE 推送 |
| 前端 | Nuxt 4 + Nuxt UI + ECharts + Tailwind | SSR/SPA，浅色/深色/跟随系统，8 个页面 |
| 部署 | Docker (Alpine) + nginx + systemd | 单容器 All-in-One，后端 `:9560` / 前端 `:9561` |
| 数据 | 本地 JSONL (`data/history.jsonl`) | 每 30s 采集落盘，最多 20000 条 |

### 1.2 当前目录结构

```
wahlap-status/
├── backend/                 # Go 网关
│   ├── main.go              # 网关主程序：代理、缓存、采集、SSE、上报验证
│   ├── lxns.go              # 落雪曲库代理 (1h 缓存)
│   ├── maint_test.go        # 维护窗口单元测试
│   ├── openapi.json         # OpenAPI 3.0.3 规格 (20 个路由)
│   └── docs/API.md          # 接口速查文档
├── frontend/                # Nuxt 4 前端
│   ├── app.vue              # 顶栏 + 导航 + 页脚
│   ├── pages/               # 8 个页面 (index, history, top100, ratinglist, chart145, report, about, docs)
│   ├── components/          # DesignSystem, ColorModeButton, ReportWizard, ReportModal, LatencyChart
│   ├── composables/useApi.ts # API 访问：网关优先，上游兜底
│   ├── assets/css/main.css  # 系统字体栈
│   ├── nuxt.config.ts       # Nuxt 配置 (端口 9561)
│   └── package.json
├── container/               # Docker 编排
│   ├── Dockerfile
│   ├── docker-compose.yml
│   ├── entrypoint.sh
│   └── README.md
├── deploy/                  # 生产部署
│   ├── DEPLOY.md
│   ├── maimai-gateway.service
│   └── nginx.conf
└── docs/
    └── monorepo-to-dotnet/  # 本迁移文档目录
```

### 1.3 核心功能模块

| 模块 | 接口 | 说明 |
|------|------|------|
| 实时状态 | `GET /api/status` | 代理上游 `/api/bot`，20s 缓存 |
| 全服 Top100 | `GET /api/top100` | 1h 缓存 |
| 越级排行 | `GET /api/chart145` | 1h 缓存 |
| Rating 分布 | `GET /api/ratinglist?version=` | dx2023-2026，1h 缓存 |
| 本地历史 | `GET /api/history` | 点位列表，最新在前 |
| 历史摘要 | `GET /api/history/summary` | 近 12h 可用率 + 平均延迟 |
| 故障事件 | `GET /api/incidents` | 异常连续段自动提取 |
| 延迟统计 | `GET /api/latency/stats` | 分位统计 (p50/p90/p99/max) |
| 上报趋势 | `GET /api/reports/trend` | 异常/正常上报趋势 |
| 服务可用率 | `GET /api/services/availability` | 48 柱 + 各服务百分比 |
| 服务延迟 | `GET /api/services/latency` | 各服务延迟序列 + maint_spans |
| 聚合看板 | `GET /api/overview` | 状态 + 摘要 + 延迟 + 进行中事件 |
| SSE 推送 | `GET /api/stream` | `text/event-stream`，心跳 20s |
| 验证码 | `GET /api/report/captcha` | 8 位数字，5 分钟有效，一次有效 |
| 手动上报 | `POST /api/report` | HMAC-SHA256 签名 + 验证码，透传上游 |
| 落雪曲库 | `GET /api/lxns/songs` | 公开曲目列表，1h 缓存 |
| 健康检查 | `GET /healthz` | `{ok: true, time}` |
| OpenAPI | `GET /api/openapi.json` | 机器可读规格 |

### 1.4 核心机制

- **维护窗口**：默认北京时间 04:00–07:00，维护点不计入可用率，可用性柱满格灰，延迟线压 0 灰
- **难度校准**：上游 level 系统性低标一档，已用落雪 `level_value` 逐首核验
- **上报链路**：8 位验证码 → 本地 HMAC-SHA256 签名 → 透传上游 `bot-post`
- **数据来源**：isMaiDown by Chongxi (https://mai.chongxi.us)，必须标注来源

---

## 二、目标架构设计

### 2.1 新技术栈

| 层级 | 技术 | 版本 | 说明 |
|------|------|------|------|
| 后端 | ASP.NET Core Minimal APIs | .NET 9/10 | 统一 Web 应用，替代 Go 网关 |
| 前端 | Blazor Web App | .NET 9/10 | Razor 组件，替代 Nuxt 4 + Vue |
| UI 框架 | MudBlazor / AntDesign Blazor | - | 替代 Nuxt UI + Tailwind |
| 图表 | Blazor ECharts 包装 / SVG | - | 替代 ECharts |
| 部署 | Docker + nginx + systemd | - | 单容器，Kestrel 直接对外 |
| 数据 | 本地 JSONL / SQLite | - | 保持 JSONL 兼容，可选 SQLite 升级 |

### 2.2 目标目录结构（单仓库 Monorepo）

```
wahlap-status/
├── src/
│   ├── MaimaiStatus.Core/         # 后端 ASP.NET Core Minimal APIs
│   │   ├── Program.cs
│   │   ├── Endpoints/             # 按功能分组的端点定义
│   │   │   ├── StatusEndpoints.cs
│   │   │   ├── HistoryEndpoints.cs
│   │   │   ├── ReportEndpoints.cs
│   │   │   ├── LxnsEndpoints.cs
│   │   │   └── ...
│   │   ├── Services/              # 业务服务
│   │   │   ├── UpstreamProxyService.cs
│   │   │   ├── PollingService.cs    # 定时采集 (替代 Go ticker)
│   │   │   ├── HistoryStore.cs      # JSONL 读写
│   │   │   ├── MaintenanceWindow.cs # 维护窗口判定
│   │   │   ├── CaptchaService.cs    # 验证码生成/校验
│   │   │   └── HmacSigner.cs        # HMAC-SHA256 签名
│   │   ├── Models/                # DTO 和领域模型
│   │   ├── Data/                  # history.jsonl 存储目录
│   │   ├── appsettings.json
│   │   └── appsettings.Development.json
│   ├── MaimaiStatus.Web/          # 前端 Blazor Web App
│   │   ├── App.razor
│   │   ├── Routes.razor
│   │   ├── Components/
│   │   │   ├── Layout/MainLayout.razor
│   │   │   ├── Layout/NavMenu.razor
│   │   │   ├── StatusHero.razor
│   │   │   ├── LatencyChart.razor
│   │   │   ├── ReportWizard.razor
│   │   │   └── ...
│   │   ├── Pages/
│   │   │   ├── Index.razor        # 监控
│   │   │   ├── History.razor      # 历史
│   │   │   ├── Top100.razor       # 全服乐曲
│   │   │   ├── RatingList.razor   # 全服排行
│   │   │   ├── Chart145.razor     # 越级排行
│   │   │   ├── Report.razor       # 手动上报
│   │   │   ├── About.razor        # 关于
│   │   │   └── Docs.razor         # 开发者文档
│   │   ├── Services/              # 前端服务 (HttpClient 包装)
│   │   │   ├── ApiClient.cs
│   │   │   └── ReportClient.cs
│   │   ├── wwwroot/
│   │   │   ├── css/
│   │   │   └── js/
│   │   ├── appsettings.json
│   │   └── Program.cs
│   └── MaimaiStatus.Shared/       # 共享模型 (DTO, 枚举)
│       ├── Models/
│       │   ├── BotStatus.cs
│       │   ├── HistoryPoint.cs
│       │   ├── Incident.cs
│       │   └── ...
│       └── Enums/
│           └── Verdict.cs
├── tests/
│   ├── MaimaiStatus.Core.Tests/
│   │   ├── MaintenanceWindowTests.cs  # 替代 maint_test.go
│   │   ├── EndpointTests/
│   │   └── ...
│   └── MaimaiStatus.Web.Tests/
├── container/
│   ├── Dockerfile
│   ├── docker-compose.yml
│   └── entrypoint.sh
├── deploy/
│   ├── DEPLOY.md
│   ├── maimai-status.service
│   └── nginx.conf
├── docs/
│   ├── API.md
│   └── monorepo-to-dotnet/
│       └── 迁移指南.md
├── README.md
├── Directory.Build.props
├── Directory.Packages.props
├── MaimaiStatus.sln
└── .github/
```

### 2.3 渲染模式选择

根据 Microsoft Learn 文档，Blazor 支持多种渲染模式。对于本项目，推荐：

| 页面 | 推荐渲染模式 | 理由 |
|------|-------------|------|
| 监控 (Index) | InteractiveServer | 需要实时 SSE 推送，服务器端状态同步 |
| 历史 (History) | InteractiveServer | 数据查询密集，服务器端渲染更快 |
| 全服 Top100 | InteractiveServer | 表格数据，服务器端分页 |
| 全服排行 (RatingList) | InteractiveServer | 图表 + 表格，服务器端计算 |
| 越级排行 (Chart145) | InteractiveServer | 搜索 + 筛选，服务器端处理 |
| 手动上报 (Report) | InteractiveServer | 表单验证，服务器端签名校验 |
| 关于/文档 (About/Docs) | StaticServer | 纯展示内容，无需交互 |

**整体模式**：Blazor Web App (Auto)，每个组件可独立设置 `@rendermode InteractiveServer` 或 `@rendermode StaticServer`。

---

## 三、迁移过程中的考验与挑战

### 3.1 技术挑战

#### 3.1.1 SSE (Server-Sent Events) 迁移

**现状**：Go 后端使用 `net/http` 的 `Flush()` + `text/event-stream` 实现 SSE，前端使用 `EventSource` 订阅。

**挑战**：
- ASP.NET Core Minimal API 原生支持 SSE，但需要手动管理 `HttpResponse.BodyWriter` 和 `CancellationToken`
- Blazor Server 本身已使用 SignalR WebSocket，与 SSE 共存需谨慎配置
- 需要实现订阅者管理（Hub 模式），广播状态给所有连接的客户端

**解决方案**：
```csharp
// 示例：ASP.NET Core SSE 端点
app.MapGet("/api/stream", async (HttpContext context, CancellationToken ct) =>
{
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.Append("Cache-Control", "no-cache");
    context.Response.Headers.Append("Connection", "keep-alive");

    var channel = sseHub.Add();
    try
    {
        await foreach (var message in channel.Reader.ReadAllAsync(ct))
        {
            await context.Response.WriteAsync($"data: {message}\n\n", ct);
            await context.Response.BodyWriter.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException) { }
    finally { sseHub.Remove(channel); }
});
```

#### 3.1.2 定时采集 (Polling Service)

**现状**：Go 使用 `time.Ticker` 每 30s 采集上游状态并落盘 JSONL。

**挑战**：
- .NET 需要使用 `IHostedService` / `BackgroundService` 实现后台定时任务
- 需要确保采集任务与 Web 请求不阻塞
- JSONL 文件并发读写需要锁保护

**解决方案**：
```csharp
public class PollingService : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HistoryStore _historyStore;
    private readonly ILogger<PollingService> _logger;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = TimeSpan.FromSeconds(30);
        using var timer = new PeriodicTimer(period);
        
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CollectStatusAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "采集失败");
            }
        }
    }
}
```

#### 3.1.3 HMAC-SHA256 签名与验证码

**现状**：Go 后端本地生成 8 位验证码，前端 HMAC-SHA256 签名后透传上游。

**挑战**：
- 密钥管理：前端环境变量 `NUXT_PUBLIC_REPORT_PRIVATE_KEY` 需迁移到 Blazor 的 `appsettings.json` 或环境变量
- Blazor WebAssembly 中私钥暴露风险，建议将签名逻辑移至服务器端 API
- 验证码存储：需要内存缓存或分布式缓存 (Redis)

**解决方案**：将验证码生成和签名验证全部移至 ASP.NET Core 后端，前端仅调用 API。

#### 3.1.4 图表组件迁移

**现状**：前端使用 Vue 3 + ECharts 渲染延迟曲线、可用率柱状图。

**挑战**：
- Blazor 生态中成熟的 ECharts 包装较少
- 需要 JavaScript 互操作 (JS Interop) 来调用 ECharts
- 或者替换为 SVG/Canvas 原生实现，或使用 Blazor 兼容的图表库

**解决方案**：
- 选项 A：使用 `Blazor.ECharts` 或自定义 JS Interop 包装 ECharts
- 选项 B：使用 MudBlazor 的图表组件 (功能较简单)
- 选项 C：纯 SVG 实现延迟曲线 (无 JS 依赖，性能可控)

#### 3.1.5 缓存机制

**现状**：Go 使用内存 map + mutex 实现 TTL 缓存 (20s/1h)。

**挑战**：
- .NET 需要使用 `IMemoryCache` 或 `Microsoft.Extensions.Caching.Memory`
- 需要为每个端点配置不同的过期时间
- 多实例部署时缓存不共享 (可考虑 Redis)

#### 3.1.6 CORS 与反代

**现状**：nginx 反代 `/api/` 到 Go 后端，SSE 需关闭缓冲。

**挑战**：
- Kestrel 直接对外时需配置 CORS 策略
- SSE 端点需要禁用响应缓冲
- 如果保留 nginx 反代，需同样配置 `proxy_buffering off`

---

### 3.2 数据迁移挑战

| 数据类型 | 现状 | 迁移方案 |
|---------|------|---------|
| history.jsonl | Go 写入，每行一个 JSON | .NET 继续读写，保持格式兼容 |
| 缓存数据 | Go 内存 map | .NET IMemoryCache，重启后丢失 (可接受) |
| 配置 | 环境变量 + .env | appsettings.json + 环境变量 |
| 维护窗口 | `MAINT_WINDOWS` 环境变量 | appsettings.json 配置段 |

---

### 3.3 团队与流程挑战

1. **技能栈切换**：团队需从 Go + Vue/TypeScript 转向 C# + Razor/Blazor
2. **构建流程**：从 `go build` + `npm run build` 变为 `dotnet build` + `dotnet publish`
3. **测试框架**：从 Go `testing` + 手动前端测试 变为 xUnit + bUnit (Blazor 组件测试)
4. **CI/CD**：需重新配置 GitHub Actions / Azure DevOps 流水线
5. **监控与日志**：从 Go 标准库 log 变为 ASP.NET Core 内置日志 (ILogger + Serilog)

---

## 四、迁移后的网站会怎么样

### 4.1 功能保持一致

迁移后，网站将保留所有现有功能：

| 功能 | 现状 | 迁移后 |
|------|------|--------|
| 实时服务器状态 | ✅ | ✅ (Blazor + SSE) |
| 延迟曲线图表 | ✅ | ✅ (Blazor 图表组件) |
| 全服 Top100 榜单 | ✅ | ✅ |
| Rating 分布排行 | ✅ | ✅ |
| 越级排行 (14.5+) | ✅ | ✅ |
| 故障事件时间线 | ✅ | ✅ |
| 手动上报 (验证码+签名) | ✅ | ✅ |
| 历史记录查询 | ✅ | ✅ |
| 浅色/深色模式 | ✅ | ✅ (Blazor 组件样式) |
| SSE 实时推送 | ✅ | ✅ |
| OpenAPI 规格 | ✅ | ✅ (Swagger/OpenAPI 中间件) |
| 健康检查 | ✅ | ✅ |
| 落雪曲库代理 | ✅ | ✅ |

### 4.2 架构优势

| 维度 | Go + Vue 分体 | .NET Blazor 统一 | 改进 |
|------|-------------|----------------|------|
| 编程语言 | Go + TypeScript | C# (全栈) | 语言统一，代码可共享 |
| 项目结构 | 两个独立仓库/目录 | 单解决方案 (sln) | 构建、部署、版本管理简化 |
| 类型安全 | Go 强类型 + TS 类型 | C# 强类型，DTO 共享 | 前后端模型共享，减少不一致 |
| 构建产物 | Go 二进制 + Vue 静态文件 | 单个 .NET 发布包 | 部署更简单 |
| 实时推送 | SSE (Go 手动实现) | SSE + SignalR 可选 | 更多实时通信选择 |
| 组件复用 | Vue 组件 | Razor 组件 | 组件可跨页面复用，支持库丰富 |
| 开发工具 | GoLand/VS Code + WebStorm | Visual Studio / VS Code + C# Dev Kit | 调试、IntelliSense 更成熟 |
| 生态系统 | Go 标准库 + npm | .NET 库 + NuGet | 库的选择更丰富 |

### 4.3 架构劣势与风险

| 风险 | 说明 | 缓解措施 |
|------|------|---------|
| Blazor 首屏加载 | InteractiveServer 需要 SignalR 连接，首屏可能有延迟 | 关键页面使用 StaticServer 预渲染 |
| 服务器资源 | Blazor Server 每个连接占用服务器内存 (线路) | 使用 Azure SignalR Service 扩展，或限制并发连接 |
| 离线能力 | 无法像 PWA 那样离线运行 | 如需离线，改用 Blazor WebAssembly |
| 学习曲线 | 团队需学习 C# + Blazor | 提供培训、代码审查、结对编程 |
| 图表库选择 | Blazor 图表组件不如 Vue/ECharts 成熟 | 使用 JS Interop 调用 ECharts，或自研 SVG 组件 |

### 4.4 迁移后的部署架构

```
┌─────────────────────────────────────────────────────────┐
│                    用户浏览器                              │
└─────────────────────────────────────────────────────────┘
                          │ HTTPS
                          ▼
┌─────────────────────────────────────────────────────────┐
│                    nginx (反代 + SSL)                     │
│  / → Blazor Web App (Kestrel :5000)                      │
│  /api/ → ASP.NET Core Minimal APIs (Kestrel :5000)      │
│  /api/stream → SSE (proxy_buffering off)                │
└─────────────────────────────────────────────────────────┘
                          │
┌─────────────────────────────────────────────────────────┐
│              .NET 9/10 单进程 (Kestrel)                   │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │   Blazor UI  │  │  Minimal API │  │ Background   │  │
│  │  (Razor)     │  │  Endpoints   │  │ Polling      │  │
│  └──────────────┘  └──────────────┘  │ Service      │  │
│                                     └──────────────┘  │
│  ┌──────────────┐  ┌──────────────┐                    │
│  │ IMemoryCache │  │ HistoryStore │                    │
│  │  (TTL 缓存)   │  │  (JSONL)     │                    │
│  └──────────────┘  └──────────────┘                    │
└─────────────────────────────────────────────────────────┘
```

---

## 五、迁移路线图 (建议)

### Phase 1: 准备与基础设施 (第 1-2 周)

- [x] 安装 .NET 9/10 SDK（实际：10.0.114）
- [x] 创建解决方案 `MaimaiStatus.slnx`（实际用新式 .slnx 格式）
- [x] 创建三个项目：Core, Web, Shared（外加两个测试项目）
- [x] 配置 `Directory.Build.props` 和 `Directory.Packages.props`
- [ ] 搭建 Docker 容器化构建（移到 Phase 5 一并处理）
- [x] 编写迁移检查清单（见文档顶部「迁移进度」）

### Phase 2: 后端 API 迁移 (第 3-5 周)

- [x] 实现接线（Core 为类库，Web 宿主 `AddMaimaiCore` + `MapMaimaiEndpoints`；单进程单端口 9561）
- [x] 实现 `UpstreamGateway` (代理 isMaiDown 接口, TTL 缓存)
- [x] 实现 `HistoryStore` (JSONL 读写, 兼容现有格式)
- [x] 实现 `PollingService` (BackgroundService, 30s 采集)
- [x] 实现 `MaintenanceWindow` (维护窗口判定, 替代 Go 测试)
- [x] 实现 `CaptchaService` + `ReportSigner` (验证码暂不启用, 签名留服务端)
- [x] 实现所有 21 个 Minimal API 端点
- [x] 实现 SSE 端点 `/api/stream`（回放 + 广播 + 20s 心跳, 实测通过）
- [x] 实现 OpenAPI（内嵌 openapi.json 原样输出）
- [x] 编写 xUnit 单元测试 (替代 `maint_test.go`, 26/26 通过)

### Phase 3: 前端 Blazor 迁移 (第 6-9 周)

- [x] 实现 `App.razor` + `Routes.razor` + `MainLayout.razor`
- [x] 实现 `NavMenu.razor` (顶栏导航) — 合入 `MainLayout.razor`(MudAppBar 横向导航 + <960px 汉堡 Temporary Drawer)
- [x] 实现 `Index.razor` (监控页, 含 SSE 客户端)
- [x] 实现 `History.razor` (历史页)
- [x] 实现 `Top100.razor` (全服乐曲)
- [x] 实现 `RatingList.razor` (全服排行)
- [x] 实现 `Chart145.razor` (越级排行)
- [x] 实现 `Report.razor` (手动上报向导)
- [x] 实现 `About.razor` + `Docs.razor` (关于/文档)
- [x] 实现图表组件 (LatencyChart, 可用率柱状图)
- [x] 实现 `ApiClient` 服务 (HttpClient 包装)
- [x] 实现深色/浅色模式切换

**Phase 3 实测记录 (2026-11-08)**:

- 视觉方向:用户选定**纯 MudBlazor Material 风**(不移植旧站霓虹 CSS);移动端导航选定**顶栏汉堡抽屉**。
- MudBlazor **9.12.0**(net10.0),`AddMudServices` 在 `MudBlazor.Services` 命名空间。
- 8 页全部编译通过(0 警告 0 错误),冒烟 8 路由全 200;`dotnet test` 26/26 无回归。
- ECharts 配置全在 `wwwroot/js/charts.js`(延迟多线/可用率柱/上报双柱,从旧 Vue 平移:维护灰段、空态、HIDDEN 守卫、报错上屏);`charts.js`/`site.js`(三态主题 + SSE 客户端)已就位。
- 上报向导 3 步复刻,**签名走服务端 `ReportSigner`(私钥不出服务器)**;验证码暂未启用,前端保留 8 位照打校验(后端接回时前端零改动)。
- Docs 页 501 行内容移植为 `DocsCatalog` 静态数据(8 分组 16 端点,五语言示例模板生成),锚点 + 语言切换齐全。
- 预渲染与 Dispose 的 JS interop 冲突已修:预渲染实例 Dispose 跳过 JS(守卫 `_jsReady` / `_self is null`)。
- MudBlazor 9 缺失的工具类(`font-weight-bold/text-center/h-100` 等)已在 `app.css` 补齐;响应式收列用自定义 `.hide-md/.hide-sm`。
- 冒烟命令:`cd src/MaimaiStatus.Web/bin/Debug/net10.0 && DATA_DIR=... POLL_ENABLED=false ./MaimaiStatus.Web`(端口 9561)。

### Phase 4: 测试与优化 (第 10-11 周)

- [ ] 编写 bUnit 组件测试
- [ ] 编写 API 集成测试 (WebApplicationFactory)
- [ ] 性能测试 (并发连接, SSE 广播)
- [ ] 安全审查 (HMAC 签名, CORS 策略)
- [ ] 与现有 Go 版本并行运行，对比响应

### Phase 5: 部署与上线 (第 12 周)

- [ ] 配置 nginx 反代 (SSE 缓冲关闭)
- [ ] 配置 systemd 服务
- [ ] 配置 certbot SSL 证书
- [ ] 数据迁移 (history.jsonl 兼容，无需转换)
- [ ] 生产环境验证
- [ ] 下线 Go 后端和 Nuxt 前端

---

## 六、关键代码对照表

| Go 现有实现 | .NET 目标实现 | 文件位置 |
|------------|--------------|---------|
| `main.go` 网关主程序 | `Program.cs` + Minimal APIs | `src/MaimaiStatus.Core/` |
| `time.Ticker` 30s 采集 | `BackgroundService` + `PeriodicTimer` | `src/MaimaiStatus.Core/Services/PollingService.cs` |
| 内存 map 缓存 | `IMemoryCache` | `src/MaimaiStatus.Core/Services/` |
| `hub` SSE 广播 | `Channel<T>` + `BackgroundService` | `src/MaimaiStatus.Core/Services/SseHub.cs` |
| `isMaintPoint` 维护判定 | `MaintenanceWindow` 类 | `src/MaimaiStatus.Core/Services/MaintenanceWindow.cs` |
| `maint_test.go` | xUnit 测试类 | `tests/MaimaiStatus.Core.Tests/` |
| `lxns.go` 曲库代理 | `LxnsEndpoints.cs` | `src/MaimaiStatus.Core/Endpoints/` |
| `useApi.ts` API 调用 | `ApiClient.cs` (HttpClient) | `src/MaimaiStatus.Web/Services/` |
| Nuxt 页面 | Razor 组件 (`.razor`) | `src/MaimaiStatus.Web/Pages/` |
| Nuxt UI 组件 | MudBlazor / 自定义 Razor 组件 | `src/MaimaiStatus.Web/Components/` |
| ECharts 图表 | JS Interop + ECharts / SVG | `src/MaimaiStatus.Web/Components/` |
| `nuxt.config.ts` | `appsettings.json` + `Program.cs` | `src/MaimaiStatus.Web/` |
| `docker-compose.yml` | 更新为 .NET 镜像 | `container/` |
| `nginx.conf` | 保留，增加 SSE 缓冲配置 | `deploy/` |

---

## 七、附录：参考资料

1. [ASP.NET Core Blazor 概述](https://learn.microsoft.com/zh-cn/aspnet/core/blazor/)
2. [ASP.NET Core Blazor 托管模型](https://learn.microsoft.com/zh-cn/aspnet/core/blazor/hosting-models)
3. [ASP.NET Core 最小 API 快速参考](https://learn.microsoft.com/zh-cn/aspnet/core/fundamentals/minimal-apis/)
4. [isMaiDown 上游文档](https://mai.chongxi.us/docs)
5. [OpenAPI 3.0.3 规格](backend/openapi.json)
6. [现有接口文档](backend/docs/API.md)

---

> 本文档由迁移规划阶段生成，将在实际重构过程中持续更新。迁移完成后，本文档可作为新架构的参考基线。