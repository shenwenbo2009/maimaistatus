using System.Globalization;
using MaimaiStatus.Core.Services;
using MaimaiStatus.Shared.Models;
using Microsoft.AspNetCore.Routing;

namespace MaimaiStatus.Core.Endpoints;

/// <summary>运维端点:健康检查(根路径与 /api 双别名)+ 内嵌 OpenAPI 规格。</summary>
public static class OpsEndpoints
{
    private static readonly Lazy<byte[]> OpenApiSpec = new(LoadSpec);

    public static IEndpointRouteBuilder MapOpsEndpoints(this IEndpointRouteBuilder app)
    {
        async Task Health(HttpContext ctx)
        {
            await ApiResponse.WriteJsonAsync(ctx, 200, new HealthDto
            {
                Ok = true,
                Time = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
            });
        }

        app.MapGet("/healthz", Health);
        app.MapGet("/api/healthz", Health); // 同域部署时反代只放行 /api/*,加别名让健康检查同口径

        // OpenAPI 规格(内嵌 openapi.json,原样输出不加归属头)
        app.MapGet("/api/openapi.json", (HttpContext ctx) =>
            ApiResponse.WriteRawAsync(ctx, OpenApiSpec.Value));

        return app;
    }

    private static byte[] LoadSpec()
    {
        var asm = typeof(OpsEndpoints).Assembly;
        using var stream = asm.GetManifestResourceStream("MaimaiStatus.Core.openapi.json")
            ?? throw new InvalidOperationException("embedded openapi.json missing");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
