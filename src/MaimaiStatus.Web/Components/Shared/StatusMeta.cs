using Microsoft.FluentUI.AspNetCore.Components;
using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons;

namespace MaimaiStatus.Web.Components.Shared;

/// <summary>
/// 状态视觉元数据(verdict / 服务状态 → Fluent 图标 + 指定色),
/// 色表与旧站逐条一致(以 history.jsonl 实测取值为准);图标优先 Color 版,缺失时 Filled 兜底。
/// </summary>
public static class StatusMeta
{
    /// <summary>总判定 → 图标 + 色(维护在深色下提亮,旧站规则)。</summary>
    public static (Icon Icon, string Color) Verdict(string verdict, bool dark) => verdict switch
    {
        "normal" => (new Icons.Color.Size24.CheckmarkCircle(), "#29C245"),
        "recovering" => (new Icons.Filled.Size24.ChevronUp(), "#79B329"),
        "degraded" => (new Icons.Filled.Size24.ChevronDown(), "#D9B61A"),
        "outage" => (new Icons.Color.Size24.ErrorCircle(), "#E68620"),
        "maintenance" => (new Icons.Filled.Size24.CloudSync(), dark ? "#E2E8F0" : "#000000"),
        "nodata" => (new Icons.Color.Size24.DismissCircle(), "#D42222"),
        _ => (new Icons.Color.Size24.ErrorCircle(), "#9AA3C7"),
    };

    /// <summary>部分服务宕机时:含 NET 宕机 → #E01212,不含则保持原色(verdict=outage)。</summary>
    public static string VerdictColor(StatusSnapshotDto? st, bool dark)
    {
        if (st is null)
        {
            return "#9AA3C7";
        }

        var (_, color) = Verdict(st.Verdict, dark);
        if (st.Verdict == "outage" && st.Services.Any(s => s.Key == "net" && s.State == "down"))
        {
            return "#E01212";
        }

        return color;
    }

    /// <summary>探针图标(按 key)。</summary>
    public static Icon ServiceIcon(string key) => key switch
    {
        "net" => new Icons.Color.Size24.Globe(),
        "aime" => new Icons.Color.Size24.Person(),
        "title" => new Icons.Filled.Size24.TextT(),
        "member" => new Icons.Color.Size24.Chat(),
        "qr" => new Icons.Filled.Size24.QrCode(),
        "bl" => new Icons.Color.Size24.Receipt(),
        _ => new Icons.Color.Size24.Database(),
    };

    /// <summary>探针状态图标(取代文字 pill)。</summary>
    public static Icon StateIcon(string state) => state switch
    {
        "ok" => new Icons.Color.Size24.CheckmarkCircle(),
        "down" => new Icons.Filled.Size24.CloudOff(),
        "degraded" => new Icons.Filled.Size24.ArrowTrendingDown(),
        "recovering" => new Icons.Filled.Size24.ArrowTrending(),
        "maintenance" => new Icons.Filled.Size24.CloudSync(),
        _ => new Icons.Color.Size24.Settings(),
    };

    /// <summary>探针状态色(服务行背景/图标;与旧站 STATE_COLORS 一致,维护深色提亮)。</summary>
    public static string StateColor(string state, bool dark) => state switch
    {
        "ok" => "#29C245",
        "down" => "#E01212",
        "degraded" => "#F04E23",
        "recovering" => "#E68620",
        "maintenance" => dark ? "#E2E8F0" : "#000000",
        _ => "#8A93A6", // nodata / 未知
    };

    /// <summary>可用率 → 十六进制色档(≥99.5 绿 / ≥90 黄 / 其余红;图表/数字同源取色)。</summary>
    public static string AvailColor(string pct) =>
        double.TryParse(pct, out var v) ? AvailHex(v) : AvailHex(0);

    public static string AvailColor(double pct) => AvailHex(pct);

    /// <summary>可用率 → 十六进制(图表/数字同源取色)。</summary>
    public static string AvailHex(string pct) => AvailHex(pct is null ? 0 : double.TryParse(pct, out var v) ? v : 0);

    public static string AvailHex(double pct) => pct >= 99.5 ? "#22c55e" : pct >= 90 ? "#f59e0b" : "#ef4444";
}
