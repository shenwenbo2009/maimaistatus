using System.Text;
using System.Text.Json;
using MaimaiStatus.Shared;

namespace MaimaiStatus.Core.Services;

/// <summary>
/// API 响应写出助手,复刻旧 Go 的 writeJSON / writeUpstream:
/// JSON 响应带 X-Data-Source 归属头,上游透传带 X-Cache HIT/MISS。
/// </summary>
public static class ApiResponse
{
    public static readonly string Attribution = UpstreamGateway.Attribution;

    /// <summary>JSON 响应(默认带归属头;status 为 HTTP 状态码)。</summary>
    public static async Task WriteJsonAsync(HttpContext ctx, int status, object value, bool attribution = true)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        if (attribution)
        {
            ctx.Response.Headers["X-Data-Source"] = Attribution;
        }

        await ctx.Response.WriteAsync(JsonSerializer.Serialize(value, MaimaiJson.Options), ctx.RequestAborted);
    }

    /// <summary>上游原始字节透传(带 X-Data-Source + X-Cache)。</summary>
    public static async Task WriteUpstreamAsync(HttpContext ctx, byte[] body, int status, bool hit)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.Headers["X-Data-Source"] = Attribution;
        ctx.Response.Headers["X-Cache"] = hit ? "HIT" : "MISS";
        await ctx.Response.Body.WriteAsync(body, ctx.RequestAborted);
    }

    /// <summary>原始字节直出(OpenAPI / 落雪曲库:无归属头无缓存头,与旧版一致)。</summary>
    public static async Task WriteRawAsync(HttpContext ctx, byte[] body, int status = 200, string contentType = "application/json; charset=utf-8")
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = contentType;
        await ctx.Response.Body.WriteAsync(body, ctx.RequestAborted);
    }

    /// <summary>上游不可达 → 502 {error, detail}(旧 Go 格式)。</summary>
    public static Task WriteUpstreamErrorAsync(HttpContext ctx, string? detail) =>
        WriteJsonAsync(ctx, 502, new Dictionary<string, string?>
        {
            ["error"] = "upstream unreachable",
            ["detail"] = detail,
        });
}
