using System.Text.Json;
using MaimaiStatus.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MaimaiStatus.Core.Tests;

/// <summary>端点冒烟测试:TestHost 内联宿主,验证路由接线与响应形状(不访问上游)。</summary>
public class EndpointsTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private string _dir = null!;

    public Task InitializeAsync()
    {
        _dir = Path.Combine(Path.GetTempPath(), "maimai-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DATA_DIR"] = _dir,
            ["POLL_ENABLED"] = "false", // 冒烟测试不访问上游
        });
        builder.Services.AddMaimaiCore(builder.Configuration);

        _app = builder.Build();
        _app.MapMaimaiEndpoints();
        return StartAsync();
    }

    private async Task StartAsync()
    {
        await _app.StartAsync();
        var server = (TestServer)_app.Services.GetRequiredService<IServer>();
        _client = server.CreateClient();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Healthz_ReturnsOkWithTimestamp()
    {
        foreach (var path in new[] { "/healthz", "/api/healthz" })
        {
            var resp = await _client.GetAsync(path);
            Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(doc.RootElement.GetProperty("time").GetString()!.Contains('T'));
            Assert.Contains("isMaiDown", resp.Headers.GetValues("X-Data-Source").First());
        }
    }

    [Fact]
    public async Task Openapi_ServedEmbedded()
    {
        var resp = await _client.GetAsync("/api/openapi.json");
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        var text = await resp.Content.ReadAsStringAsync();
        Assert.Contains("/api/stream", text);
        Assert.Contains("/api/services/availability", text);
    }

    [Fact]
    public async Task History_EmptyStore_ReturnsEmptyPoints()
    {
        var resp = await _client.GetAsync("/api/history");
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("points").ValueKind);
    }

    [Fact]
    public async Task HistorySummary_EmptyStore_ShapeMatches()
    {
        var resp = await _client.GetAsync("/api/history/summary");
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(0, root.GetProperty("total").GetInt32());
        Assert.Equal("0.00", root.GetProperty("uptime_pct").GetString());
        Assert.Equal(12, root.GetProperty("window_hours").GetInt32());
        Assert.Equal(JsonValueKind.Object, root.GetProperty("latest").ValueKind); // 无数据 → {}
    }

    [Fact]
    public async Task RatingList_InvalidVersion_400()
    {
        var resp = await _client.GetAsync("/api/ratinglist?version=dx2020");
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("invalid version", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Availability_EmptyStore_48Buckets()
    {
        var resp = await _client.GetAsync("/api/services/availability");
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(48, root.GetProperty("buckets").GetArrayLength());
        Assert.Equal("0.00", root.GetProperty("overall_pct").GetString());
    }

    [Fact]
    public async Task LatencyStats_EmptyStore_OnlyHoursAndCount()
    {
        var resp = await _client.GetAsync("/api/latency/stats");
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(24, root.GetProperty("hours").GetInt32());
        Assert.Equal(0, root.GetProperty("count").GetInt32());
        Assert.False(root.TryGetProperty("p50", out _)); // count=0 时省略分位
    }

    [Fact]
    public async Task ReportsTrend_And_Incidents_EmptyShapes()
    {
        using var trend = JsonDocument.Parse(
            await (await _client.GetAsync("/api/reports/trend")).Content.ReadAsStringAsync());
        Assert.Equal(0, trend.RootElement.GetProperty("trend").GetArrayLength());

        using var inc = JsonDocument.Parse(
            await (await _client.GetAsync("/api/incidents")).Content.ReadAsStringAsync());
        Assert.Equal(0, inc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Report_WithoutSignatureHeaders_400()
    {
        var resp = await _client.PostAsync("/api/report", new StringContent("[]"));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Contains("X-Signature", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Captcha_IssuesEightDigitCode()
    {
        var resp = await _client.GetAsync("/api/report/captcha");
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var code = doc.RootElement.GetProperty("code").GetString()!;
        Assert.Equal(8, code.Length);
        Assert.All(code, c => Assert.InRange(c, '0', '9'));
        Assert.Equal(300, doc.RootElement.GetProperty("expires_in").GetInt32());
    }
}
