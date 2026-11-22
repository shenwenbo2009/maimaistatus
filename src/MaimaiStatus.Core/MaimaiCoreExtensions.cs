using MaimaiStatus.Core.Endpoints;
using MaimaiStatus.Core.Services;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MaimaiStatus.Core;

/// <summary>
/// Core 接入点:宿主(MaimaiStatus.Web)调用 AddMaimaiCore 注册服务、
/// MapMaimaiEndpoints 挂载全部 21 条 API 路由——单进程统一架构的接缝。
/// </summary>
public static class MaimaiCoreExtensions
{
    public static IServiceCollection AddMaimaiCore(this IServiceCollection services, IConfiguration config)
    {
        var opts = CoreOptions.FromConfiguration(config);
        services.AddSingleton(opts);
        services.AddSingleton(new MaintenanceWindow(opts.MaintWindows));
        services.AddSingleton<HistoryStore>();
        services.AddSingleton<HistoryAnalytics>();
        services.AddSingleton<SseHub>();
        services.AddSingleton<CaptchaService>();
        services.AddSingleton<LxnsProxy>();
        services.AddSingleton<UpstreamGateway>();
        services.AddMemoryCache();

        services.AddHttpClient("upstream", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(12); // 与旧 Go http.Client 一致
        });
        services.AddHttpClient("report", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(12);
        });

        services.AddHostedService<PollingService>();

        // CORS:兼容旧 Go withCORS(预检放行,上报相关头全开)
        services.AddCors(cors => cors.AddDefaultPolicy(policy =>
        {
            var origin = opts.CorsAllow == "*" ? policy.AllowAnyOrigin() : policy.WithOrigins(opts.CorsAllow);
            origin
                .WithMethods("GET", "POST")
                .WithHeaders("Content-Type", "X-Client-ID", "X-Timestamp", "X-Signature",
                    "X-Session", "X-Captcha-ID", "X-Captcha-Code");
        }));

        return services;
    }

    /// <summary>挂载全部 API 路由(21 条,与 openapi.json 一一对应)。</summary>
    public static IEndpointRouteBuilder MapMaimaiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapOpsEndpoints();
        app.MapStatusEndpoints();
        app.MapLeaderboardEndpoints();
        app.MapHistoryEndpoints();
        app.MapStreamEndpoints();
        app.MapReportEndpoints();
        app.MapLxnsEndpoints();
        return app;
    }
}
