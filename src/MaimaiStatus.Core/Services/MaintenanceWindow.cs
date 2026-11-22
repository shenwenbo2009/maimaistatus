using MaimaiStatus.Shared.Models;

namespace MaimaiStatus.Core.Services;

/// <summary>
/// 华立常规维护窗口判定(北京时间每日,中国无夏令时固定 UTC+8)。
/// 默认 04:00–07:00,可用 MAINT_WINDOWS 覆盖,支持逗号分隔与跨日窗口。
/// 维护点位不计入可用率;图表里置灰(柱满格灰 / 线压 0 灰)。
/// </summary>
public sealed class MaintenanceWindow
{
    private readonly (int From, int To)[] _windows;

    public MaintenanceWindow(string windowsSpec)
    {
        _windows = Parse(windowsSpec);
    }

    /// <summary>解析 "04:00-07:00,23:30-00:30";非法段静默跳过(与旧 Go 一致)。</summary>
    public static (int From, int To)[] Parse(string spec)
    {
        var list = new List<(int, int)>();
        foreach (var w in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = w.Split('-', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            if (TryToMinute(parts[0].Trim(), out var from) && TryToMinute(parts[1].Trim(), out var to))
            {
                list.Add((from, to));
            }
        }

        return list.ToArray();
    }

    private static bool TryToMinute(string t, out int minutes)
    {
        minutes = 0;
        var parts = t.Split(':');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m) ||
            h is < 0 or > 23 || m is < 0 or > 59)
        {
            return false;
        }

        minutes = h * 60 + m;
        return true;
    }

    /// <summary>毫秒时间戳是否落在每日维护窗口(北京时间)。</summary>
    public bool InRoutineMaint(long tsMillis)
    {
        var mins = (int)((tsMillis / 60000 + 8 * 60) % 1440);
        foreach (var (from, to) in _windows)
        {
            if (from <= to)
            {
                if (mins >= from && mins < to)
                {
                    return true;
                }
            }
            else if (mins >= from || mins < to) // 跨日窗口
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>维护点位:上游 verdict 报 maintenance,或落在常规窗口。</summary>
    public bool IsMaintPoint(HistoryPoint p) =>
        p.Verdict == "maintenance" || InRoutineMaint(p.Ts);

    /// <summary>把维护点合并为 [start,end] 毫秒区间(间隔超 10 分钟即断开)。</summary>
    public List<long[]> MaintSpans(IEnumerable<HistoryPoint> pts)
    {
        var spans = new List<long[]>();
        foreach (var p in pts)
        {
            if (!IsMaintPoint(p))
            {
                continue;
            }

            if (spans.Count > 0 && p.Ts - spans[^1][1] <= 10 * 60 * 1000)
            {
                spans[^1][1] = p.Ts;
            }
            else
            {
                spans.Add([p.Ts, p.Ts]);
            }
        }

        return spans;
    }
}
