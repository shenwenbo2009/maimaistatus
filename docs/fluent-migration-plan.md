# MudBlazor → Fluent UI Blazor 5.0.1 迁移方案(Phase 4 设计探究)

> 探究日期:2026-11-08 · 全部结论经**真实编译探针验证**(`/tmp/opencode/fluentprobe`,0 错误),
> 图标清单经**反射核对** 47/47 条,非纸上谈兵。

## 0. 结论摘要(TL;DR)

| 项 | 结论 |
|---|---|
| 是否迁移 | **建议迁移**。MudBlazor Material 风与 maimai 视觉基调不符;Fluent 5.0.1 组件面完整、主题三态原生支持、图标体系(含 Color)官方维护 |
| 目标版本 | `Microsoft.FluentUI.AspNetCore.Components` **5.0.1**(net10.0 TFM,依赖 10.0.12)+ `Microsoft.FluentUI.AspNetCore.Components.Icons` **5.0.1** |
| 初始化 | `AddFluentUIComponents()` + `_Imports` 两行 + App.razor 引 `lib.module.js` + Providers 落 MainLayout(见 §2) |
| 图标 | **47/47 映射完成**:24 条 Color + 21 条 Filled + 2 条换名(§4) |
| 工作量 | 16 个 razor / 2309 行 / ~380 处 Mud 组件 / 47 处图标;估 2~3 个工作日(§7 分四步) |
| 最大风险 | ① 5.0.1 相对 4.x 是**大改版**,网上多数教程已过时(§1.2);② app.css 依赖 Mud 工具类与 `--mud-palette-*`(§5) |

## 1. 调研依据

### 1.1 包与版本(已核实 NuGet)

- `Microsoft.FluentUI.AspNetCore.Components` 5.0.1:lib 含 **net10.0**/net11.0/net8.0/net9.0,依赖 `Microsoft.AspNetCore.Components.Web 10.0.12` ✅ 与本项目 net10.0 匹配。
- `Microsoft.FluentUI.AspNetCore.Components.Icons` 5.0.1:lib 仅 net8.0(net8.0 资产可被 net10.0 引用 ✅),含 **Regular / Filled / Light / Color** 四套图标 DLL。
- 图标规模(反射):Regular 10141 导出类型、Filled 10267、Light 219、**Color 896(199 个唯一名,精选集)**。
- 官方 README:`dotnet new fluentblazor` 模板初始化;库为 best-effort 支持(非 ASP.NET Core 官方组件)。
- 官方模板 `_Imports.razor` 标准写法(已下载 nupkg 核对):
  ```razor
  @using Microsoft.FluentUI.AspNetCore.Components
  @using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons
  ```
  ⚠️ **不能**写 `@using ...Icons.Regular.Size24`(Size24 是类不是命名空间,CS0138);
  也**不能**只靠 `@using ...Components` 后直接写 `Icons.X`(子命名空间不会被导入,CS0246)——必须用别名。

### 1.2 ⚠️ 关键发现:5.0.1 是大改版,4.x 教程不可照抄

探针实测(iconprobe 曾误用 Icons 包传递依赖的 **4.14.5**,已纠正),**5.0.1 中已移除/改名**:

| 4.x(过时) | 5.0.1 现行 | 说明 |
|---|---|---|
| `FluentTextField` | **`FluentTextInput`**(+ `FluentField` 包 Label/校验) | `@bind-Value` 仍可用(继承 `FluentInputBase<string>`) |
| `FluentNumberField` | **`FluentNumberInput<T>`** | Min/Max/Step/StepButtons |
| `FluentNavMenu` / `FluentNavLink` | **`FluentNav` / `FluentNavItem`** | `Href`/`Match`(NavLinkMatch)/`IconRest`/`IconActive`/`Active` |
| `FluentDesignTheme` + `DesignThemeModes` | **`IThemeService`** + `ThemeMode`(**Light/Dark/System**) | `SwitchThemeAsync`/`IsDarkModeAsync`/`ThemeSettings{Mode,Color,…}` |
| `IToastService` | **`INotificationService`** | `ShowSuccessToastAsync`/`ShowSuccessBarAsync`… |
| `FluentDialogService` | **`IDialogService`** | `ShowDialogAsync`/`ShowConfirmationAsync`/`ShowMessageBoxAsync`/`ShowDrawerAsync` |
| `MessageType.*` | **`MessageBarIntent.*`**(Success/Warning/Error/Info/Custom) | `FluentMessageBar` 属性:Intent/Icon/Title/Visible/AllowDismiss |
| `Appearance.Accent`(按钮) | **`ButtonAppearance`** = Default/Outline/Primary/Subtle/Transparent | Badge 另有 `BadgeAppearance` = Filled/Ghost/Outline/Tint |
| `MudSnackbar` 概念 | `INotificationService.Show*ToastAsync` | — |

**5.0.1 中确认存在**(反射+编译双验证):
FluentAppBar/FluentAppBarItem、**FluentLayout + FluentLayoutHamburger(移动端汉堡抽屉)**、FluentLayoutItem、
FluentNav/FluentNavItem、FluentDataGrid<T>/PropertyColumn/TemplateColumn/FluentPaginator(PaginationState)、
FluentMessageBar(+Provider)、FluentDialog(+IDialogService)、FluentToast(+Provider)、FluentWizard/FluentWizardStep、
FluentTabs/FluentTab(`ActiveTabId` 字符串绑定)、FluentCard、FluentButton、FluentBadge、FluentSkeleton、
FluentProgress、FluentDivider、FluentStack、FluentGrid/FluentGridItem(xs/sm/md/lg 断点)、FluentMenu/FluentMenuItem、
FluentTooltip、FluentOverlay、FluentSwitch、FluentCheckbox、FluentSelect、FluentAvatar、FluentAccordion、
FluentProviders(主题容器)、FluentIcon<T>。

**5.0.1 中不存在**(勿用):FluentBreadcrumb、FluentList、FluentIconButton、FluentDesignTheme、FluentNavMenu。

### 1.3 编译探针验证过的用法(节选,0 错误)

```razor
@* 图标:Color 优先 *@
<FluentIcon Value="@(new Icons.Color.Size24.CheckmarkCircle())" Color="Color.Success" Width="24px" />
<FluentIcon Value="@(new Icons.Filled.Size24.ArrowRight())" />

@* 消息条替 MudAlert *@
<FluentMessageBar Intent="MessageBarIntent.Warning" Icon="@(new Icons.Color.Size24.MegaphoneLoud())" Title="公告">…</FluentMessageBar>

@* 按钮 *@
<FluentButton Appearance="ButtonAppearance.Primary" IconStart="@(new Icons.Regular.Size24.ArrowSync())" OnClick="…">刷新</FluentButton>

@* 表格(列必须包在 <ChildContent> 里,RZ9996) *@
<FluentDataGrid Items="@Rows" TItem="Row" ShowHoverRow="true">
    <ChildContent>
        <PropertyColumn Property="@(r => r.Name)" Title="曲名" />
        <TemplateColumn Title="操作" Style="text-align:right">…</TemplateColumn>
    </ChildContent>
    <EmptyContent>没有匹配的乐曲</EmptyContent>
</FluentDataGrid>

@* 输入 *@
<FluentTextInput @bind-Value="_q" Placeholder="搜索…" Appearance="TextInputAppearance.Outline" />
<FluentField Label="上传次数" Required="true"><FluentNumberInput T="int" @bind-Value="_n" Min="1" /></FluentField>

@* 分页(私有 setter,用异步方法) *@
<PaginationState _pagerState = new() { ItemsPerPage = 20 };
// 初始化:await _pagerState.SetTotalItemCountAsync(total); await _pagerState.SetCurrentPageIndexAsync(page);
<FluentPaginator State="@_pagerState" />

@* 步骤向导(可替 ReportWizard 手写步骤条) *@
<FluentWizard Value="@_step" ValueChanged="@((int v) => _step = v)">
    <Steps><FluentWizardStep Label="步骤1">…</FluentWizardStep></Steps>
</FluentWizard>

@* 导航(桌面顶栏与移动抽屉通用) *@
<FluentNav>
    <FluentNavItem Href="/" Match="NavLinkMatch.All" IconRest="@(new Icons.Regular.Size24.Home())">监控</FluentNavItem>
</FluentNav>

@* 移动端抽屉(替代 MudDrawer Temporary) *@
<FluentLayoutHamburger PanelPosition="DialogAlignment.Start" Visible="@_drawer">…</FluentLayoutHamburger>
```

已知坑(探针踩过):
- `ButtonAppearance` 没有 `Lightweight`(那是 4.x `Appearance` 的值),用 `Subtle`/`Outline`。
- `PaginationState.TotalItemCount/CurrentPageIndex` setter 是 **private**,须用 `SetTotalItemCountAsync/SetCurrentPageIndexAsync`。
- `FluentLayoutHamburger` 直接放子内容,不要再包 `<ChildContent>`(RZ10012)。
- FluentNavItem 的 `Match` 类型是 `Microsoft.AspNetCore.Components.Routing.NavLinkMatch`(_Imports 需加该 using)。

## 2. 初始化改造清单

| 文件 | 改动 |
|---|---|
| `Directory.Packages.props` | `MudBlazor 9.12.0` → 删;新增 `Microsoft.FluentUI.AspNetCore.Components 5.0.1`、`...Icons 5.0.1` |
| `Program.cs` | `AddMudServices()` → `AddFluentUIComponents()`(Blazor Server 场景官方要求同时 `AddHttpClient()`,已隐含则不动) |
| `_Imports.razor` | 加 `@using Microsoft.AspNetCore.Components.Routing`、`@using Microsoft.FluentUI.AspNetCore.Components`、`@using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons`;删 MudBlazor 相关 |
| `App.razor` | 删 `_content/MudBlazor/MudBlazor.min.css` 与 `MudBlazor.min.js`;head 加 `<script src="_content/Microsoft.FluentUI.AspNetCore.Components/Microsoft.FluentUI.AspNetCore.Components.lib.module.js" type="module" async></script>`(脚本含在包内,SSR 也生效);可选引 reboot.css |
| `MainLayout.razor` | Providers:删 `MudThemeProvider/MudPopoverProvider/MudDialogProvider/MudSnackbarProvider` → MainLayout 末尾放 `FluentMessageBarProvider/FluentDialogProvider/FluentToastProvider/FluentTooltipProvider/FluentMenuProvider`;顶栏/抽屉按 §3 改造 |
| `Program.cs` 之外的 DI | `IDialogService`、`INotificationService`、`IThemeService` 由 `AddFluentUIComponents()` 注册,页面 `@inject` 直接换接口名 |

## 3. 组件映射表(按实际使用次数排序)

| MudBlazor(次数) | Fluent 5.0.1 | 备注 |
|---|---|---|
| MudText ×84 | `<p>/<span>/<h*>` + `FluentText` | 大部分是排版,直接用语义标签+app.css 类;强调场景用 `FluentText`(Size/Weight/Color) |
| MudTh/Td ×25/25 | `PropertyColumn`/`TemplateColumn` 单元格 | DataGrid 列定义(§1.3),响应式隐藏列用 CSS 类 `hide-md`/`hide-sm` 挂在列 Style/Class |
| MudItem ×18 / MudGrid ×9 | `FluentGridItem`/`FluentGrid`(Spacing) | `xs/sm/md/lg` 断点语义一致 |
| MudButton ×18 | `FluentButton` | Variant→`ButtonAppearance`:Filled→Primary、Outlined→Outline、Text→Subtle/Transparent;Color→`Color.*` |
| MudIcon ×12(+StatusMeta 动态) | `FluentIcon` | §4 图标映射;`Icon` 参数(字符串)→ `Value`(Icon 对象) |
| MudCard×12 / Header×12 / Content×12 | `FluentCard`(无 Header 子组件) | 卡头自写 `<div class="card-head">`(app.css 已有等价样式) |
| MudAlert ×11 | `FluentMessageBar`(Intent/Icon/Title) | Severity.Error→Error、Warning→Warning、Info→Info |
| MudPaper ×9 | `FluentCard` 或普通 `<div>` | Elevation=1 卡片 → FluentCard;纯容器 → div+现有类 |
| MudSkeleton ×6 | `FluentSkeleton`(Height/Shimmer) | PageShell/Index 骨架直接换 |
| MudChip ×6 | `FluentBadge`(Appearance=Filled/Ghost) | T="string" 泛型参数消失 |
| MudTable ×5 | `FluentDataGrid<T>` + `TemplateColumn` | **难点**:见 §6;分页 MudPagination×2 → `FluentPaginator`+`PaginationState` |
| MudTextField ×4 | `FluentTextInput`(+`FluentField` 包 Label) | Adornment 图标 → `StartTemplate` 内放 `FluentIcon`;Debounce 自管(现逻辑保留) |
| MudListItem×4 / MudList×1 | `FluentNav`/`FluentNavItem` 或普通列表 | Docs 页说明列表建议直接 ul/li+app.css(更轻) |
| MudNavLink ×3 | `FluentNavItem` | 顶栏与抽屉共用一个 `<FluentNav>` 片段 |
| MudMenuItem×3 / MudMenu×1 | `FluentMenu`/`FluentMenuItem` | 主题切换菜单 |
| MudProgressLinear ×2 | `FluentProgress`(Min/Max/Value) | RatingList/可用率条 |
| MudTimeline ×1/Item ×1 | **无对应** → 自写(现有 wizard-steps 思路)或改竖排列表 | History 页最近事件;量小,自写 CSS 更快 |
| MudNumericField ×1 | `FluentNumberInput<T>` | ReportWizard 上传次数 |
| MudDialog×1 + IDialogService | `IDialogService.ShowDialogAsync` + `FluentDialog` 内容组件 | ReportDialog 改造,DialogParameters(Title/Modal/Width) |
| MudDrawer/MudAppBar/MudSpacer/MudMainContent/MudContainer | `FluentLayoutHamburger`(移动抽屉)+ 自写顶栏 / FluentGrid 容器 | 顶栏品牌+导航+主题菜单建议**保留自写 div 结构**(maimai-brand 样式不动),只换图标与菜单组件——改动面最小 |
| MudThemeProvider ×1 | `FluentProviders` + `IThemeService`(ThemeMode) | 三态映射 §5 |
| MudSnackbarProvider | `FluentToastProvider` + `INotificationService` | 当前未见实际 Snackbar 调用,直接换 Provider |
| MudAvatar ×1 | `FluentAvatar` | Index 上报人头像 |

**无 Mud 对应、需保留自写**:PageShell、PillTabs(→ 可换 `FluentTabs`,Id 绑定)、LatencyChart(ECharts 互操作不变)、
verdict-panel/inst/svc-row 等全部页面自有类、wizard-steps 手写步骤条(→ 可选升级 `FluentWizard`)。

## 4. 图标映射表(Material → Fluent,**47/47 全覆盖**)

用法:`<FluentIcon Value="@(new Icons.Color.Size24.CheckmarkCircle())" Width="1em" />`
(推荐 `Width="1em"` 继承字号,可让现有 `.verdict-icon{font-size:…}` 等 CSS 继续生效,替代 Material Icon 的字体方案。)

**Color 优先(24 条)**——`Icons.Color.Size24.*`:

| Material | Fluent Color | 用途 |
|---|---|---|
| CheckCircle | **CheckmarkCircle** | 状态正常 ×7、StatusMeta |
| Warning | **Warning** | 警告 ×6 |
| Refresh | **ArrowSync** | 刷新 ×6 |
| Campaign | **MegaphoneLoud** | 上报/公告 ×4 |
| Speed | **Gauge** | 延迟仪表 |
| SettingsBrightness | **Lightbulb** | 跟随系统主题 |
| MenuBook | **BookOpen** | 关于/文档导航 |
| Error | **ErrorCircle** | 宕机判定、StatusMeta |
| Chat | **Chat** | 会员/公众号 |
| Wifi | **Wifi** | 机台绿网(仅 Color 有 Wifi,Regular/Filled 只有 Wifi1/2/3/4) |
| Storage | **Database** | 存储/Title |
| Send | **Send** | 提交上报 |
| ReceiptLong | **Receipt** | BL |
| Public | **Globe** | NET |
| Person | **Person** | AIME |
| Leaderboard / EmojiEvents | **Trophy** | 全服排行导航 ×2 |
| Inbox | **Mail** | 空态 |
| History | **History** | 历史导航 |
| Groups | **People** | 人数量 |
| Dns | **Database** | 服务探针兜底 |
| Dashboard | **Apps** | 监控导航 |
| Cancel | **DismissCircle** | 无数据 |
| Adjust | **Settings** | 兜底/设置 |

**Filled 兜底(21 条)**——`Icons.Filled.Size24.*`:

| Material | Fluent Filled | 备注 |
|---|---|---|
| TrendingUp | ArrowTrending | Fluent 无 TrendingUp,箭头语义等价 |
| QrCode | QrCode | |
| ArrowForward | ArrowRight | 向导"下一步" |
| Search | Search | 搜索框 |
| LightMode | WeatherSunny | 浅色主题 |
| DarkMode | WeatherMoon | 深色主题 |
| CloudSync | CloudSync | 维护中 |
| CloudOff | CloudOff | 宕机 |
| WifiOff | WifiOff | 机台断网 |
| Upload | ArrowUpload | 成绩上传失败 |
| TrendingDown | ArrowTrendingDown | 降级 |
| **TextFields** | **TextT** | ⚠️ Fluent 无 Size24 的 `Text`,用 `TextT`(Size24 存在) |
| Power | Power | |
| PersonOff | PersonDelete | 登录变游客 |
| MusicNote | MusicNote1 | 全服乐曲导航 |
| Menu | Navigation | 汉堡按钮 |
| **Login** | **ArrowEnter** | ⚠️ `ArrowEnter` 仅 Size16/20——用 `Icons.Filled.Size20.ArrowEnter` + `Width="24px"`,或直接用 `ArrowRight` |
| KeyboardDoubleArrowUp | ChevronUp | 恢复中 |
| KeyboardDoubleArrowDown | ChevronDown | 降级中 |
| Check | Checkmark | 步骤完成 |
| Block | Prohibited | 小黑屋 |
| ArrowBack | ArrowLeft | 向导"上一步" |

**StatusMeta.cs 专项**(图标色表,旧栈语义必须保留):
```csharp
// 旧:("normal", "#29C245") → Icons.Material.Filled.CheckCircle
// 新:("normal", "#29C245") → new Icons.Color.Size24.CheckmarkCircle()
// 返回类型从 (string Icon, string Color) 改为 (Icon Icon, string Color)
// 宕机 #E01212 / 恢复 #79B329 / 降级 #D9B61A / 维护 #E2E8F0|000 / 无数据 #D42222 色值全部不变
```

## 5. 主题方案(三态:system/light/dark)

现状:`ThemeService`(localStorage 键 `maimai-theme`)+ 首帧防闪脚本 + `html.dark` 类 + `MudThemeProvider IsDarkMode`。
app.css 自定义层与 ECharts 颜色都读 `.dark` / `data-theme`。

**方案(低风险,推荐)**:
1. `ThemeService` **保留为唯一真源**(键名、防闪脚本、`html.dark`、`data-theme` 全不动)——ECharts/现有 CSS 零改动。
2. `MudThemeProvider` → 换 `FluentProviders` 容器;主题档位变化时调 `IThemeService.SwitchThemeAsync(ThemeMode.Light|Dark|System)` 同步 Fluent 组件外观(在 `ThemeService.SetPreferenceAsync`/`OnSystemThemeChanged` 回调里补一行)。
3. ⚠️ 实施时验证一点:5.0.1 的 `FluentProviders` 是否自己写 localStorage(键冲突/双写)。若冲突,用其 `ThemeSettings` API显式指定存储键或改为每帧 `SetThemeToElementAsync` 覆盖 `documentElement`。
4. app.css 的 `var(--mud-palette-*)`(14 处)→ 改为自维护语义变量(见 §6),不依赖 Fluent token 命名,防止库升级再断。

## 6. CSS 兼容层(工作量集中在 app.css)

1. **Mud 工具类**(razor 中约 130 处依赖 MudBlazor.min.css):
   `d-flex/align-center/align-start/justify-space-between/justify-center/flex-wrap/flex-column/gap-1..4/pa-0..6/mt/mb/ml-2…`
   → **方案 A(推荐)**:app.css 增补一份 ~40 行微工具类(语义同名,断点工具 `only-md-*` 已自维护,照此办理),razor 零改动。
   → 方案 B:全部改 `FluentStack/FluentGrid`——改动面大,收益低,不推荐。
2. **`mud-text-secondary` ×37**:app.css 已有等价 `.text-secondary`,批量替换类名即可。
3. **`var(--mud-palette-*)` ×14**:改为 `--maimai-primary/--maimai-success/--maimai-error/--maimai-line/--maimai-text-2`,在 `:root` 与 `.dark` 各定义一份(现有 Material 色值直接搬)。
4. **Fluent 自带 reboot.css**:先**不引**,避免与现有 reset/字体冲突;仅当需要 Fluent 设计 token 时再评估。
5. 页面自有类(verdict-panel/wizard-steps/svc-row/maimai-table-wrap 等 50+)**不动**。

## 7. 分步执行计划

| 步骤 | 内容 | 验收 |
|---|---|---|
| **S1 基础设施** | 换包、Program/_Imports/App.razor、Providers、CSS 兼容层(§6.1~6.3)、ThemeService 挂钩(§5) | `dotnet build` 0 错;首页能渲染(组件未换前 Mud 页面会炸,故 S1 与 S2 同批提交) |
| **S2 布局+共享** | MainLayout(顶栏/抽屉/主题菜单)、PageShell、StatusMeta(Icon 类型化)、PillTabs、ReportDialog、ReportWizard | 8 路由可达,抽屉/主题切换可用 |
| **S3 页面×8** | Index(最复杂:SSE+双图+概览)→ History(表格+Timeline 自写)→ Top100/RatingList/Chart145(DataGrid+分页)→ Report/About/Docs | 逐页冒烟,图表与空态/报错上屏行为与 Phase 3 一致 |
| **S4 图标+收尾** | 47 处图标按 §4 落位(StatusMeta 先行)、`Icons.Material` 全清、rebuild/test/冒烟 | `grep -r "Icons.Material\|Mud[A-Z]" src` 归零;26 测试全绿;8 路由 200 |

依赖顺序:S1→S2→S3(页内可并行)→S4 可与 S3 交织(图标随页换)。

## 8. 风险清单

| 风险 | 等级 | 对策 |
|---|---|---|
| 5.0.1 文档稀缺(官网 JS 渲染抓不到,社区文章多为 4.x) | 中 | 已用反射+编译探针锁定 API(§1.2/1.3);实施遇阻继续探针先行 |
| DataGrid 替 MudTable:分页、响应式隐藏列、hover 行为差异 | 中 | 保留自写 `maimai-table-wrap` 包裹;隐藏列用现有 `hide-md/hide-sm` CSS;PaginationState 异步 API(§1.3) |
| 主题双写 localStorage / Fluent 深色与 `html.dark` 不同步 | 中 | §5 方案:ThemeService 为真源,Fluent 只做单向同步;实施时专项验证 |
| 工具类缺失导致布局塌陷 | 低 | §6.1 微工具类兜底,冒烟逐页看 |
| ReportDialog 改 `IDialogService` 的参数模型差异 | 低 | 5.0.1 `ShowDialogAsync` + DialogParameters(Title/Modal/Width)探针已列;ShowConfirmationAsync 可直接做确认弹窗 |
| 预渲染 Dispose JS interop 异常(Phase 3 已踩过) | 低 | 沿用 `_jsReady`/`_self is null` 守卫,Fluent 组件同理 |
| Icons 包仅 net8.0 TFM | 低 | net8.0 资产兼容 net10.0,探针已验证编译+反射 |

## 9. 不受影响的既有资产

ApiClient、LiveStatusStore、ChartsInterop(ECharts 互操作/dataZoom/灰段)、ReportClient/ReportSigner、
WebUtil、charts.js/site.js、ThemeService 主逻辑、21 条 API 路由、26 个测试、`history.jsonl` 格式、
三态主题语义、维护窗口灰柱、状态色表色值、来源标注文案。

## 10. 探针资产(可复用)

- `/tmp/opencode/fluentprobe/` — 5.0.1 编译验证工程(0 错误的完整用法样例 `Test.razor`)
- `/tmp/opencode/v5probe/` — 5.0.1 反射 API 核对
- `/tmp/opencode/iconprobe/` — 图标反射 + 47 条映射核对程序
- `/tmp/opencode/fluent5/`、`/tmp/opencode/fluenticons/` — 已解包 nupkg(XML 文档可查)
