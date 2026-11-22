using MaimaiStatus.Core.Services;
using Microsoft.AspNetCore.Routing;

namespace MaimaiStatus.Core.Endpoints;

/// <summary>落雪公开曲目列表代理(1h 缓存;原始透传,无归属头——与旧 Go 一致)。</summary>
public static class LxnsEndpoints
{
    public static IEndpointRouteBuilder MapLxnsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/lxns/songs", async (HttpContext ctx, LxnsProxy lxns, CancellationToken ct) =>
        {
            var body = await lxns.GetSongListAsync(ct);
            if (body is null)
            {
                await ApiResponse.WriteJsonAsync(ctx, 502, new { error = "lxns song list unreachable" });
                return;
            }

            await ApiResponse.WriteRawAsync(ctx, body);
        });

        return app;
    }
}
