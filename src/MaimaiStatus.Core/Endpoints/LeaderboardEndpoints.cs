using MaimaiStatus.Core.Services;
using Microsoft.AspNetCore.Routing;

namespace MaimaiStatus.Core.Endpoints;

/// <summary>静态榜单快照:top100 / chart145 / ratinglist,1h 缓存原样透传。</summary>
public static class LeaderboardEndpoints
{
    private static readonly string[] AllowedRatingVersions = ["dx2023", "dx2024", "dx2025", "dx2026"];

    public static IEndpointRouteBuilder MapLeaderboardEndpoints(this IEndpointRouteBuilder app)
    {
        var ttl = TimeSpan.FromHours(1);

        app.MapGet("/api/top100", async (HttpContext ctx, CoreOptions opts, UpstreamGateway gw, CancellationToken ct) =>
        {
            var r = await gw.GetCachedAsync("top100", $"{opts.UpstreamBase}/api/top100", ttl, ct);
            if (!r.Ok)
            {
                await ApiResponse.WriteUpstreamErrorAsync(ctx, r.Error);
                return;
            }

            await ApiResponse.WriteUpstreamAsync(ctx, r.Body!, r.Status, r.Hit);
        });

        app.MapGet("/api/chart145", async (HttpContext ctx, CoreOptions opts, UpstreamGateway gw, CancellationToken ct) =>
        {
            var r = await gw.GetCachedAsync("chart145", $"{opts.UpstreamBase}/api/chart145", ttl, ct);
            if (!r.Ok)
            {
                await ApiResponse.WriteUpstreamErrorAsync(ctx, r.Error);
                return;
            }

            await ApiResponse.WriteUpstreamAsync(ctx, r.Body!, r.Status, r.Hit);
        });

        app.MapGet("/api/ratinglist", async (HttpContext ctx, CoreOptions opts, UpstreamGateway gw, CancellationToken ct) =>
        {
            var version = ctx.Request.Query["version"].ToString();
            var key = "ratinglist:all";
            var url = $"{opts.UpstreamBase}/api/ratinglist";
            if (version != "")
            {
                // 简单白名单,防 SSRF 拼接
                if (!AllowedRatingVersions.Contains(version))
                {
                    await ApiResponse.WriteJsonAsync(ctx, 400, new { error = "invalid version" });
                    return;
                }

                key = "ratinglist:" + version;
                url += "?version=" + version;
            }

            var r = await gw.GetCachedAsync(key, url, ttl, ct);
            if (!r.Ok)
            {
                await ApiResponse.WriteUpstreamErrorAsync(ctx, r.Error);
                return;
            }

            await ApiResponse.WriteUpstreamAsync(ctx, r.Body!, r.Status, r.Hit);
        });

        return app;
    }
}
