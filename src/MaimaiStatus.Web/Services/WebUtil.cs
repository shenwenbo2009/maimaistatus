using System.Globalization;

namespace MaimaiStatus.Web.Services;

/// <summary>前端展示工具(与旧 useApi.ts / 页面内工具函数逐条对应)。</summary>
public static class WebUtil
{
    /// <summary>上游封面是相对路径,拼成绝对地址。</summary>
    public static string CoverUrl(string? cover)
    {
        if (string.IsNullOrEmpty(cover))
        {
            return "";
        }

        return cover.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? cover : "https://mai.chongxi.us" + cover;
    }

    public static string FmtNum(long n) => n.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>"3 秒前 / 5 分钟前 / 3 小时前 / 2 天前"(旧 timeAgo)。</summary>
    public static string TimeAgo(long ts)
    {
        var s = Math.Max(1, (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ts) / 1000);
        if (s < 60)
        {
            return $"{s} 秒前";
        }

        if (s < 3600)
        {
            return $"{s / 60} 分钟前";
        }

        if (s < 86400)
        {
            return $"{s / 3600} 小时前";
        }

        return $"{s / 86400} 天前";
    }

    /// <summary>"11/8 14:32"(故障事件 / 表格时间)。</summary>
    public static string FmtDT(long ts) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ts).ToLocalTime().ToString("M/d HH:mm", CultureInfo.InvariantCulture);

    public static string FmtDur(long sec)
    {
        if (sec < 60)
        {
            return $"{sec} 秒";
        }

        var m = sec / 60;
        if (m < 60)
        {
            return $"{m} 分钟";
        }

        return $"{m / 60} 小时 {m % 60} 分";
    }

    /// <summary>ID 最高位:百位/千位(&lt;10000)→ STD;1 开头万位数(10000–19999)→ DX。</summary>
    public static bool IsDx(int id) => id >= 10000 && id < 20000;

    /// <summary>版本/总计徽章:百分比两位小数(旧 pct)。</summary>
    public static string Pct(long part, long total) =>
        total > 0 ? ((double)part / total * 100).ToString("0.00", CultureInfo.InvariantCulture) : "0.00";
}
