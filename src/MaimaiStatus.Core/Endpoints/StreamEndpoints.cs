using System.Text;
using MaimaiStatus.Core.Services;
using Microsoft.AspNetCore.Routing;

namespace MaimaiStatus.Core.Endpoints;

/// <summary>
/// SSE 实时推送:text/event-stream,连上先回放最新状态,之后每次采集广播一条,
/// 心跳 20s(: ping)。与旧 Go /api/stream 协议逐字节一致。
/// </summary>
public static class StreamEndpoints
{
    public static IEndpointRouteBuilder MapStreamEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/stream", async (HttpContext ctx, UpstreamGateway gw, SseHub hub, CancellationToken ct) =>
        {
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers["X-Data-Source"] = ApiResponse.Attribution;

            async Task SendAsync(byte[] body)
            {
                var json = Encoding.UTF8.GetString(body);
                await ctx.Response.WriteAsync($"event: status\ndata: {json.Trim()}\n\n", ct);
                await ctx.Response.BodyWriter.FlushAsync(ct);
            }

            // 连上先回放缓存里的最新状态(旧 Go 同行为)
            if (gw.TryGetCached("status", out var cached))
            {
                await SendAsync(cached.Body);
            }

            var ch = hub.Add();
            try
            {
                using var heartbeat = new PeriodicTimer(TimeSpan.FromSeconds(20));
                var readTask = ch.Reader.ReadAsync(ct).AsTask();
                var beatTask = heartbeat.WaitForNextTickAsync(ct).AsTask();
                while (!ct.IsCancellationRequested)
                {
                    var done = await Task.WhenAny(readTask, beatTask);
                    if (done == readTask)
                    {
                        var payload = await readTask;
                        await SendAsync(payload);
                        readTask = ch.Reader.ReadAsync(ct).AsTask();
                    }
                    else
                    {
                        await beatTask;
                        await ctx.Response.WriteAsync(": ping\n\n", ct);
                        await ctx.Response.BodyWriter.FlushAsync(ct);
                        beatTask = heartbeat.WaitForNextTickAsync(ct).AsTask();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 客户端断开
            }
            finally
            {
                hub.Remove(ch);
            }
        });

        return app;
    }
}
