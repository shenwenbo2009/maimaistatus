using System.Text.Json.Serialization;

namespace MaimaiStatus.Shared.Models;

/// <summary>GET /healthz、/api/healthz</summary>
public sealed class HealthDto
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; } = true;

    [JsonPropertyName("time")]
    public string Time { get; set; } = "";
}

/// <summary>历史摘要里的 latest(无数据时序列化为 {})</summary>
public sealed class LatestDto
{
    [JsonPropertyName("ts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Ts { get; set; }

    [JsonPropertyName("verdict")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Verdict { get; set; }

    [JsonPropertyName("verdict_text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VerdictText { get; set; }
}

/// <summary>GET /api/history/summary</summary>
public sealed class HistorySummaryDto
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    /// <summary>百分比字符串("%.2f"),与监控页服务可用性同源。</summary>
    [JsonPropertyName("uptime_pct")]
    public string UptimePct { get; set; } = "0.00";

    [JsonPropertyName("avg_latency_ms")]
    public int AvgLatencyMs { get; set; }

    [JsonPropertyName("latest")]
    public LatestDto Latest { get; set; } = new();

    [JsonPropertyName("maint_points")]
    public int MaintPoints { get; set; }

    [JsonPropertyName("window_hours")]
    public int WindowHours { get; set; } = 12;
}

/// <summary>GET /api/history、/api/history/range</summary>
public sealed class HistoryResponseDto
{
    [JsonPropertyName("points")]
    public List<HistoryPoint> Points { get; set; } = new();
}

/// <summary>故障事件(异常连续段)</summary>
public sealed class IncidentDto
{
    [JsonPropertyName("start_ts")]
    public long StartTs { get; set; }

    [JsonPropertyName("end_ts")]
    public long EndTs { get; set; }

    [JsonPropertyName("duration_sec")]
    public long DurationSec { get; set; }

    [JsonPropertyName("worst_verdict")]
    public string WorstVerdict { get; set; } = "";

    [JsonPropertyName("verdict_text")]
    public string VerdictText { get; set; } = "";

    [JsonPropertyName("ongoing")]
    public bool Ongoing { get; set; }
}

/// <summary>GET /api/incidents</summary>
public sealed class IncidentsResponseDto
{
    [JsonPropertyName("incidents")]
    public List<IncidentDto> Incidents { get; set; } = new();

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

/// <summary>GET /api/latency/stats(count=0 时 min/p50/... 全部省略,与 Go 一致)</summary>
public sealed class LatencyStatsDto
{
    [JsonPropertyName("hours")]
    public int Hours { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("min")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Min { get; set; }

    [JsonPropertyName("p50")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? P50 { get; set; }

    [JsonPropertyName("p90")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? P90 { get; set; }

    [JsonPropertyName("p99")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? P99 { get; set; }

    [JsonPropertyName("max")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Max { get; set; }

    [JsonPropertyName("avg")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Avg { get; set; }

    [JsonPropertyName("current")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Current { get; set; }
}

/// <summary>GET /api/reports/trend 单点</summary>
public sealed class ReportTrendPointDto
{
    [JsonPropertyName("ts")]
    public long Ts { get; set; }

    [JsonPropertyName("anomaly")]
    public int Anomaly { get; set; }

    [JsonPropertyName("normal")]
    public int Normal { get; set; }
}

/// <summary>GET /api/reports/trend</summary>
public sealed class ReportsTrendResponseDto
{
    [JsonPropertyName("trend")]
    public List<ReportTrendPointDto> Trend { get; set; } = new();
}

/// <summary>GET /api/services/availability 单柱</summary>
public sealed class AvailabilityBucketDto
{
    [JsonPropertyName("ts")]
    public long Ts { get; set; }

    [JsonPropertyName("ratio")]
    public double Ratio { get; set; }

    /// <summary>过半为维护点 → 整柱置灰。</summary>
    [JsonPropertyName("maint")]
    public bool Maint { get; set; }

    /// <summary>无采集,前端留白不造 100%。</summary>
    [JsonPropertyName("empty")]
    public bool Empty { get; set; }
}

/// <summary>GET /api/services/availability 单服务行</summary>
public sealed class ServiceAvailabilityDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("availability_pct")]
    public string AvailabilityPct { get; set; } = "100.0";

    [JsonPropertyName("avg_latency_ms")]
    public int AvgLatencyMs { get; set; }

    [JsonPropertyName("checks")]
    public int Checks { get; set; }
}

/// <summary>GET /api/services/availability</summary>
public sealed class AvailabilityResponseDto
{
    [JsonPropertyName("hours")]
    public int Hours { get; set; }

    [JsonPropertyName("buckets")]
    public List<AvailabilityBucketDto> Buckets { get; set; } = new();

    [JsonPropertyName("services")]
    public List<ServiceAvailabilityDto> Services { get; set; } = new();

    [JsonPropertyName("overall_pct")]
    public string OverallPct { get; set; } = "0.00";
}

/// <summary>GET /api/services/latency 单条服务延迟线</summary>
public sealed class ServiceLatencySeriesDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>[[ts, ms], ...] 与 ECharts 数据格式同形。</summary>
    [JsonPropertyName("series")]
    public List<long[]> Series { get; set; } = new();
}

/// <summary>GET /api/services/latency</summary>
public sealed class LatencySeriesResponseDto
{
    [JsonPropertyName("hours")]
    public int Hours { get; set; }

    [JsonPropertyName("services")]
    public List<ServiceLatencySeriesDto> Services { get; set; } = new();

    /// <summary>[[start, end], ...];无维护时为 null(Go 原样)。</summary>
    [JsonPropertyName("maint_spans")]
    public List<long[]>? MaintSpans { get; set; }
}

/// <summary>GET /api/report/captcha</summary>
public sealed class CaptchaResponseDto
{
    [JsonPropertyName("captcha_id")]
    public string CaptchaId { get; set; } = "";

    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; } = 300;
}
