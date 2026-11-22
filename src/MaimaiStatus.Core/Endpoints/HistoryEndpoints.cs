using System.Text.Json;
using System.Text.Json.Nodes;
using MaimaiStatus.Core.Services;
using MaimaiStatus.Shared;
using MaimaiStatus.Shared.Models;
using Microsoft.AspNetCore.Routing;

namespace MaimaiStatus.Core.Endpoints;

/// <summary>本地历史与统计聚合(全部读 JSONL,维护点位剔除口径与旧版一致)。</summary>
public static class HistoryEndpoints
{
    public static IEndpointRouteBuilder MapHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        // 点位列表,最新在前
        app.MapGet("/api/history", (HttpContext ctx, HistoryStore store) =>
        {
            var limit = ParseInt(ctx, "limit");
            if (limit <= 0 || limit > 2000)
            {
                limit = 288;
            }

            return ApiResponse.WriteJsonAsync(ctx, 200,
                new HistoryResponseDto { Points = store.ReadDesc(limit) });
        });

        // 近 12h 摘要(uptime 与服务可用性同源)
        app.MapGet("/api/history/summary", (HttpContext ctx, HistoryAnalytics analytics) =>
            ApiResponse.WriteJsonAsync(ctx, 200, analytics.Summary()));

        // 范围查询:from/to 为毫秒时间戳
        app.MapGet("/api/history/range", (HttpContext ctx, HistoryStore store) =>
        {
            var from = ParseLong(ctx, "from");
            var to = ParseLong(ctx, "to");
            var limit = ParseInt(ctx, "limit");
            if (limit <= 0 || limit > 5000)
            {
                limit = 2000;
            }

            var outPts = new List<HistoryPoint>();
            foreach (var p in store.ReadDesc(limit))
            {
                if (from > 0 && p.Ts < from)
                {
                    continue;
                }

                if (to > 0 && p.Ts > to)
                {
                    continue;
                }

                outPts.Add(p);
            }

            return ApiResponse.WriteJsonAsync(ctx, 200, new HistoryResponseDto { Points = outPts });
        });

        // 故障事件:由历史自动提取的异常连续段
        app.MapGet("/api/incidents", (HttpContext ctx, HistoryAnalytics analytics) =>
        {
            var list = analytics.Incidents();
            return ApiResponse.WriteJsonAsync(ctx, 200, new IncidentsResponseDto
            {
                Incidents = list,
                Count = list.Count,
            });
        });

        // 延迟分位:?hours=24(最大168)
        app.MapGet("/api/latency/stats", (HttpContext ctx, HistoryAnalytics analytics) =>
            ApiResponse.WriteJsonAsync(ctx, 200, analytics.LatencyStats(ParseInt(ctx, "hours"))));

        // 上报趋势:正序返回,方便画线
        app.MapGet("/api/reports/trend", (HttpContext ctx, HistoryStore store) =>
        {
            var limit = ParseInt(ctx, "limit");
            if (limit <= 0 || limit > 2000)
            {
                limit = 288;
            }

            var pts = store.ReadDesc(limit);
            var trend = new List<ReportTrendPointDto>(pts.Count);
            for (var i = pts.Count - 1; i >= 0; i--)
            {
                trend.Add(new ReportTrendPointDto
                {
                    Ts = pts[i].Ts,
                    Anomaly = pts[i].Anomaly,
                    Normal = pts[i].Normal,
                });
            }

            return ApiResponse.WriteJsonAsync(ctx, 200, new ReportsTrendResponseDto { Trend = trend });
        });

        // 各服务可用率:48 柱 + 各服务百分比 + overall_pct
        app.MapGet("/api/services/availability", (HttpContext ctx, HistoryAnalytics analytics) =>
            ApiResponse.WriteJsonAsync(ctx, 200, analytics.Availability(ParseInt(ctx, "hours"))));

        // 各服务延迟序列:多线图数据 + 维护灰段,自动降采样
        app.MapGet("/api/services/latency", (HttpContext ctx, HistoryAnalytics analytics) =>
            ApiResponse.WriteJsonAsync(ctx, 200,
                analytics.LatencySeries(ParseInt(ctx, "hours"), ParseInt(ctx, "maxpoints"))));

        // 聚合看板:状态 + 摘要 + 延迟分位 + 进行中事件,一次请求拿全
        app.MapGet("/api/overview", async (HttpContext ctx, CoreOptions opts, UpstreamGateway gw,
            HistoryAnalytics analytics, CancellationToken ct) =>
        {
            JsonObject root;
            if (gw.TryGetCached("status", out var cached))
            {
                root = ParseObject(cached.Body);
            }
            else
            {
                var r = await gw.FetchAsync($"{opts.UpstreamBase}/api/bot", ct);
                if (!r.Ok)
                {
                    await ApiResponse.WriteJsonAsync(ctx, 502, new { error = "upstream unreachable" });
                    return;
                }

                if (r.Status == 200)
                {
                    gw.SetCached("status", r.Body!, 200, TimeSpan.FromSeconds(20));
                }

                root = ParseObject(r.Body!);
            }

            root["history_summary"] = JsonSerializer.SerializeToNode(analytics.Summary(), MaimaiJson.Options)!;
            root["latency_stats"] = JsonSerializer.SerializeToNode(analytics.LatencyStats(24), MaimaiJson.Options)!;
            var ongoing = analytics.Incidents().Where(i => i.Ongoing).ToList();
            root["ongoing_incidents"] = JsonSerializer.SerializeToNode(ongoing, MaimaiJson.Options)!;
            await ApiResponse.WriteJsonAsync(ctx, 200, root);
        });

        return app;
    }

    private static JsonObject ParseObject(byte[] body)
    {
        try
        {
            return JsonNode.Parse(body) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private static int ParseInt(HttpContext ctx, string key) =>
        int.TryParse(ctx.Request.Query[key].ToString(), out var v) ? v : 0;

    private static long ParseLong(HttpContext ctx, string key) =>
        long.TryParse(ctx.Request.Query[key].ToString(), out var v) ? v : 0;
}
