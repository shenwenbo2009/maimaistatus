using MaimaiStatus.Core.Services;
using MaimaiStatus.Shared.Models;

namespace MaimaiStatus.Core.Tests;

/// <summary>维护窗口测试(移植自旧 backend/maint_test.go,断言逐条一致)。</summary>
public class MaintenanceWindowTests
{
    private static readonly DateTime Day = new(2026, 10, 11);

    /// <summary>北京时间某日某时 → 毫秒时间戳。</summary>
    private static long BeijingMs(DateTime day, int h, int m) =>
        new DateTimeOffset(day.Year, day.Month, day.Day, h, m, 0, TimeSpan.FromHours(8))
            .ToUnixTimeMilliseconds();

    [Theory]
    [InlineData(3, 59, false)] // 窗口前
    [InlineData(4, 0, true)]   // 左闭
    [InlineData(5, 30, true)]  // 窗口内
    [InlineData(6, 59, true)]  // 右开前
    [InlineData(7, 0, false)]  // 右开
    [InlineData(12, 0, false)] // 白天
    [InlineData(23, 59, false)] // 深夜
    public void InRoutineMaint_DefaultWindow_0400_0700(int h, int m, bool want)
    {
        var w = new MaintenanceWindow("04:00-07:00");
        Assert.Equal(want, w.InRoutineMaint(BeijingMs(Day, h, m)));
    }

    [Fact]
    public void IsMaintPoint_FollowsVerdictAndWindow()
    {
        var w = new MaintenanceWindow("04:00-07:00");
        // 上游报 maintenance:任何时段都算维护
        Assert.True(w.IsMaintPoint(new HistoryPoint { Ts = BeijingMs(Day, 12, 0), Verdict = "maintenance" }));
        // 窗口内普通点位也算维护
        Assert.True(w.IsMaintPoint(new HistoryPoint { Ts = BeijingMs(Day, 5, 0), Verdict = "normal" }));
        // 窗口外正常点位不算
        Assert.False(w.IsMaintPoint(new HistoryPoint { Ts = BeijingMs(Day, 12, 0), Verdict = "normal" }));
        // 窗口外宕机不算维护(保持原判定)
        Assert.False(w.IsMaintPoint(new HistoryPoint { Ts = BeijingMs(Day, 12, 0), Verdict = "outage" }));
    }

    [Fact]
    public void MaintSpans_MergeWithin10Min_BreakAfter()
    {
        var w = new MaintenanceWindow("04:00-07:00");
        HistoryPoint Mk(int h, int m) => new() { Ts = BeijingMs(Day, h, m), Verdict = "normal" };

        var pts = new[] { Mk(3, 50), Mk(4, 5), Mk(4, 10), Mk(4, 15), Mk(8, 0) };
        var spans = w.MaintSpans(pts);
        Assert.Single(spans);
        Assert.Equal(BeijingMs(Day, 4, 5), spans[0][0]);
        Assert.Equal(BeijingMs(Day, 4, 15), spans[0][1]);

        // 间隔超 10 分钟即断开
        var pts2 = new[] { Mk(4, 5), Mk(4, 40) };
        Assert.Equal(2, w.MaintSpans(pts2).Count);
    }

    [Fact]
    public void Parse_SupportsCrossDayAndSkipsInvalid()
    {
        var windows = MaintenanceWindow.Parse("04:00-07:00, 23:30-00:30, garbage, 12:00");
        Assert.Equal(2, windows.Length);
        Assert.Equal((4 * 60, 7 * 60), windows[0]);
        Assert.Equal((23 * 60 + 30, 30), windows[1]);

        // 跨日窗口:23:59 与 00:10 都在内
        var w = new MaintenanceWindow("23:30-00:30");
        Assert.True(w.InRoutineMaint(BeijingMs(Day, 23, 59)));
        Assert.True(w.InRoutineMaint(BeijingMs(Day, 0, 10)));
        Assert.False(w.InRoutineMaint(BeijingMs(Day, 12, 0)));
    }
}
