using MaimaiStatus.Core.Services;
using Microsoft.AspNetCore.Routing;

namespace MaimaiStatus.Core.Endpoints;

/// <summary>实时状态:/api/status 与 /api/bot 同源同缓存(20s),原样透传上游。</summary>
public static class StatusEndpoints
{
    public static IEndpointRouteBuilder MapStatusEndpoints(this IEndpointRouteBuilder app)
    {
        var ttl = TimeSpan.FromSeconds(20);

        async Task Handler(HttpContext ctx, CoreOptions opts, UpstreamGateway gw, CancellationToken ct)
        {
            var r = await gw.GetCachedAsync("status", $"{opts.UpstreamBase}/api/bot", ttl, ct);
            if (!r.Ok)
            {
                await ApiResponse.WriteUpstreamErrorAsync(ctx, r.Error);
                return;
            }

            await ApiResponse.WriteUpstreamAsync(ctx, r.Body!, r.Status, r.Hit);
        }

        app.MapGet("/api/status", Handler);
        app.MapGet("/api/bot", Handler); // 兼容原路径,前端可直接切到本网关
        return app;
    }
}
