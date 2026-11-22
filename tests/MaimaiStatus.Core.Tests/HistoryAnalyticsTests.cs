using System.Text.Json;
using MaimaiStatus.Core.Services;
using MaimaiStatus.Shared;
using MaimaiStatus.Shared.Models;
using Microsoft.Extensions.Configuration;

namespace MaimaiStatus.Core.Tests;

/// <summary>历史存储与聚合测试(移植自旧 backend/maint_test.go 的 TestOverallAvailability)。</summary>
public class HistoryAnalyticsTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "maimai-hist-" + Guid.NewGuid().ToString("N"));

    public HistoryAnalyticsTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败可忽略
        }
    }

    private static CoreOptions OptionsFor(string dir) => CoreOptions.FromConfiguration(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DATA_DIR"] = dir })
            .Build());

    /// <summary>空窗口规格:可用率口径只看 verdict(窗口逻辑由 MaintenanceWindowTests 单测)。</summary>
    private (HistoryStore Store, HistoryAnalytics Analytics) NewSut()
    {
        var maint = new MaintenanceWindow("");
        var store = new HistoryStore(OptionsFor(_dir), maint);
        return (store, new HistoryAnalytics(store, maint));
    }

    private static string Line(long ts, string verdict, params HistoryServiceItem[] svcs) =>
        JsonSerializer.Serialize(new HistoryPoint
        {
            Ts = ts,
            Verdict = verdict,
            Services = svcs.ToList(),
        }, MaimaiJson.Options) + "\n";

    [Fact]
    public void OverallAvailability_ExcludesMaint_MatchesSummaryUptime()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var ok = new HistoryServiceItem { Key = "net", State = "ok" };
        var down = new HistoryServiceItem { Key = "net", State = "down" };
        // Go 原测试数据:1h 前 2 ok / 30min 前 1ok+1down / 15min 前 maintenance 2down
        var lines = Line(now - 3600 * 1000, "normal", ok, ok) +
                    Line(now - 1800 * 1000, "outage", ok, down) +
                    Line(now - 900 * 1000, "maintenance", down, down);
        File.WriteAllText(Path.Combine(_dir, "history.jsonl"), lines);

        var (_, analytics) = NewSut();
        var (pct, tot, maint) = analytics.OverallAvailability(12);
        Assert.Equal(4, tot);
        Assert.Equal(1, maint);
        Assert.InRange(pct, 74.99, 75.01); // 3/4

        var s = analytics.Summary();
        Assert.Equal("75.00", s.UptimePct); // 与 availability 同源
        Assert.Equal(2, s.Total); // maintenance 点被剔除
        Assert.Equal(1, s.MaintPoints);
    }

    [Fact]
    public void AppendRead_RoundTrip_LimitsAndOrder()
    {
        // 空窗口:点位 maint 只由 verdict 决定,测试不依赖运行时刻
        var store = new HistoryStore(OptionsFor(_dir), new MaintenanceWindow(""));

        var bot = JsonSerializer.Serialize(new BotStatus
        {
            Verdict = "normal",
            VerdictText = "一切正常",
            Latency = new BotLatency { CurrentMs = 123 },
            Reports = new BotReports { AnomalyCount = 1, NormalCount = 2 },
            Services =
            [
                new BotService { Key = "net", State = "ok", Latency = 10 },
                new BotService { Key = "bl", State = "down", Latency = null },
            ],
        }, MaimaiJson.Options);
        store.AppendFromUpstream(bot);

        var desc = store.ReadDesc();
        Assert.Single(desc);
        var p = desc[0];
        Assert.Equal("normal", p.Verdict);
        Assert.Equal(123, p.LatencyMs);
        Assert.Equal(1, p.Anomaly);
        Assert.Equal(2, p.Normal);
        Assert.Equal(1, p.OkCount);
        Assert.Equal(1, p.DownCount);
        Assert.False(p.Maint); // 12:00 UTC ≈ 20:00 北京,窗口外
        Assert.NotNull(p.Services);
        Assert.Equal(2, p.Services!.Count);

        // 非法 JSON 静默丢弃
        store.AppendFromUpstream("{not json");
        Assert.Single(store.ReadDesc());
    }

    [Fact]
    public void LatencyStats_EmptyStore_ReturnsOnlyHoursAndCount()
    {
        var (_, analytics) = NewSut();
        var stats = analytics.LatencyStats(0); // 非法 hours → 24
        Assert.Equal(24, stats.Hours);
        Assert.Equal(0, stats.Count);
        Assert.Null(stats.P50); // count=0 时分位字段省略(Go 一致)
    }

    [Fact]
    public void Availability_EmptyStore_FortyEightEmptyBuckets()
    {
        var (_, analytics) = NewSut();
        var av = analytics.Availability(999); // 非法 hours → 12
        Assert.Equal(12, av.Hours);
        Assert.Equal(48, av.Buckets.Count);
        Assert.All(av.Buckets, b => Assert.True(b.Empty));
        Assert.Equal("0.00", av.OverallPct);
        Assert.Empty(av.Services);
    }

    [Fact]
    public void LatencySeries_EmptyStore_SpansNull()
    {
        var (_, analytics) = NewSut();
        var ls = analytics.LatencySeries(24, 0); // 非法 maxpoints → 240
        Assert.Equal(24, ls.Hours);
        Assert.Empty(ls.Services);
        Assert.Null(ls.MaintSpans); // Go:nil → null
    }

    [Fact]
    public void Incidents_EmptyStore_ZeroCount()
    {
        var (_, analytics) = NewSut();
        var inc = analytics.Incidents();
        Assert.Empty(inc);
    }
}
