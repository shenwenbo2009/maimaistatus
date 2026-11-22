using System.Text.Json.Serialization;

namespace MaimaiStatus.Shared.Models;

/// <summary>
/// GET /api/status(上游 /api/bot 原样透传)的完整前端模型。
/// 供 Blazor 页面与 SSE 推送共用;字段与上游 JSON 逐一对齐(snake_case)。
/// </summary>
public sealed class StatusSnapshotDto
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("status_text")]
    public string StatusText { get; set; } = "";

    [JsonPropertyName("incident_level")]
    public string IncidentLevel { get; set; } = "";

    [JsonPropertyName("verdict")]
    public string Verdict { get; set; } = "";

    [JsonPropertyName("verdict_text")]
    public string VerdictText { get; set; } = "";

    [JsonPropertyName("services")]
    public List<StatusServiceDto> Services { get; set; } = new();

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("latency")]
    public StatusLatencyDto Latency { get; set; } = new();

    [JsonPropertyName("reports")]
    public StatusReportsDto Reports { get; set; } = new();

    [JsonPropertyName("broadcast")]
    public StatusBroadcastDto? Broadcast { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
}

/// <summary>六个探针项(延迟字段名是 latency,历史点位里才是 latency_ms)。</summary>
public sealed class StatusServiceDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("state_text")]
    public string StateText { get; set; } = "";

    [JsonPropertyName("latency")]
    public double? Latency { get; set; } // 上游可能给小数

    [JsonPropertyName("duration_text")]
    public string DurationText { get; set; } = "";
}

public sealed class StatusLatencyDto
{
    [JsonPropertyName("current_ms")]
    public double? CurrentMs { get; set; } // 上游可能给小数或 null(维护窗口)

    [JsonPropertyName("load")]
    public string Load { get; set; } = "";

    [JsonPropertyName("load_text")]
    public string LoadText { get; set; } = "";

    [JsonPropertyName("volatility")]
    public string Volatility { get; set; } = "";

    [JsonPropertyName("volatility_text")]
    public string VolatilityText { get; set; } = "";
}

public sealed class StatusReportsDto
{
    [JsonPropertyName("anomaly_count")]
    public int AnomalyCount { get; set; }

    [JsonPropertyName("normal_count")]
    public int NormalCount { get; set; }

    [JsonPropertyName("last_report_ago_minutes")]
    public int LastReportAgoMinutes { get; set; }

    [JsonPropertyName("data_empty")]
    public bool DataEmpty { get; set; }
}

public sealed class StatusBroadcastDto
{
    [JsonPropertyName("msg")]
    public string Msg { get; set; } = "";
}
