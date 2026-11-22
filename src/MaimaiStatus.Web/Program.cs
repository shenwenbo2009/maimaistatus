using MaimaiStatus.Core;
using Microsoft.FluentUI.AspNetCore.Components;
using MaimaiStatus.Web.Components;
using MaimaiStatus.Web.Services;
// 自家 ThemeService 与 Fluent 5.0.1 同名类冲突(CS0104),固定别名
using ThemeService = MaimaiStatus.Web.Services.ThemeService;

var builder = WebApplication.CreateBuilder(args);

// Blazor 组件(交互式服务端渲染)+ Fluent UI Blazor 5.0.1 组件库(主题/对话框/通知服务一并注册)
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddFluentUIComponents();

// 前端服务:API 客户端 / 三态主题 / 顶栏实时状态 / ECharts Interop / 上报签名
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<LiveStatusStore>();
builder.Services.AddScoped<ChartsInterop>();
builder.Services.AddScoped<ReportClient>();

// ASP.NET Core 网关:21 条 API 路由 + 30s 采集 + SSE(与 Blazor 同进程同端口)
builder.Services.AddMaimaiCore(builder.Configuration);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
// TLS 由 nginx 终结,进程只监听 HTTP;端口见 appsettings.json "Urls"(可被 ASPNETCORE_URLS 覆盖)
app.UseCors();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// API 路由(与 Blazor 页面同域同端口,/api/* 由网关处理)
app.MapMaimaiEndpoints();

app.Run();

// 供 WebApplicationFactory 集成测试使用
public partial class Program;
