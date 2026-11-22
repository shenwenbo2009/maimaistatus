using System.Text.Json.Nodes;
using MaimaiStatus.Core.Services;
using MaimaiStatus.Shared.Models;
using Microsoft.AspNetCore.Routing;

namespace MaimaiStatus.Core.Endpoints;

/// <summary>
/// 手动上报:先发 8 位验证码(5 分钟一次有效),再透传签名头到上游 bot-post。
/// 签名头由调用方提供(Blazor 向导在服务端用 ReportSigner 计算,私钥不出服务器)。
/// </summary>
public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        // 领码:code 给用户照打(暂未接入校验,保留接口以维持 OpenAPI 21 路由)
        app.MapGet("/api/report/captcha", (HttpContext ctx, CaptchaService captcha) =>
        {
            var (id, code) = captcha.Create();
            return ApiResponse.WriteJsonAsync(ctx, 200, new CaptchaResponseDto
            {
                CaptchaId = id,
                Code = code,
                ExpiresIn = 300,
            });
        });

        // 上报转发:原字节透传上游,Body 限 1MB(验证码校验暂不启用)
        app.MapPost("/api/report", async (HttpContext ctx, CancellationToken ct) =>
        {
            var clientID = ctx.Request.Headers["X-Client-ID"].ToString();
            var ts = ctx.Request.Headers["X-Timestamp"].ToString();
            var sig = ctx.Request.Headers["X-Signature"].ToString();
            if (clientID == "" || ts == "" || sig == "")
            {
                await ApiResponse.WriteJsonAsync(ctx, 400,
                    new { error = "missing X-Client-ID / X-Timestamp / X-Signature" });
                return;
            }

            byte[] body;
            try
            {
                body = await UpstreamGateway.ReadCappedAsync(ctx.Request.Body, 1 << 20, ct);
            }
            catch (IOException)
            {
                await ApiResponse.WriteJsonAsync(ctx, 400, new { error = "read body failed" });
                return;
            }

            try
            {
                if (JsonNode.Parse(body) is not JsonArray)
                {
                    await ApiResponse.WriteJsonAsync(ctx, 400, new { error = "body must be a JSON array" });
                    return;
                }
            }
            catch (System.Text.Json.JsonException)
            {
                await ApiResponse.WriteJsonAsync(ctx, 400, new { error = "body must be a JSON array" });
                return;
            }

            var opts = ctx.RequestServices.GetRequiredService<CoreOptions>();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{opts.ReportUpstream}/bot-post")
                {
                    Content = new ByteArrayContent(body),
                };
                req.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
                req.Headers.TryAddWithoutValidation("X-Client-ID", clientID);
                req.Headers.TryAddWithoutValidation("X-Timestamp", ts);
                req.Headers.TryAddWithoutValidation("X-Signature", sig);

                using var resp = await ctx.RequestServices
                    .GetRequiredService<IHttpClientFactory>()
                    .CreateClient("report")
                    .SendAsync(req, ct);
                var rb = await UpstreamGateway.ReadCappedAsync(resp.Content, 1 << 20, ct);
                await ApiResponse.WriteRawAsync(ctx, rb, (int)resp.StatusCode);
            }
            catch (HttpRequestException ex)
            {
                await ApiResponse.WriteJsonAsync(ctx, 502,
                    new Dictionary<string, string?> { ["error"] = "report upstream unreachable", ["detail"] = ex.Message });
            }
        });

        return app;
    }
}
