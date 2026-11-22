using System.Text.Json.Serialization;

namespace MaimaiStatus.Shared.Models;

/// <summary>历史点位(JSONL 每行一个,与旧 Go 网关格式完全兼容)。</summary>
public sealed class HistoryPoint
{
    [JsonPropertyName("ts")]
    public long Ts { get; set; }

    [JsonPropertyName("verdict")]
    public string Verdict { get; set; } = "";

    [JsonPropertyName("verdict_text")]
    public string VerdictText { get; set; } = "";

    [JsonPropertyName("latency_ms")]
    public int? LatencyMs { get; set; }

    [JsonPropertyName("anomaly")]
    public int Anomaly { get; set; }

    [JsonPropertyName("normal")]
    public int Normal { get; set; }

    [JsonPropertyName("ok_count")]
    public int OkCount { get; set; }

    [JsonPropertyName("down_count")]
    public int DownCount { get; set; }

    [JsonPropertyName("maint")]
    public bool Maint { get; set; }

    /// <summary>与 Go 的 omitempty 一致:空不写出。</summary>
    [JsonPropertyName("services")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<HistoryServiceItem>? Services { get; set; }
}

/// <summary>历史点位里的服务快照(latency_ms 可空)。</summary>
public sealed class HistoryServiceItem
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("latency_ms")]
    public int? LatencyMs { get; set; }
}

/// <summary>上游 /api/bot 响应(采集时解析用,原样透传不受此模型影响)。</summary>
public sealed class BotStatus
{
    [JsonPropertyName("verdict")]
    public string Verdict { get; set; } = "";

    [JsonPropertyName("verdict_text")]
    public string VerdictText { get; set; } = "";

    [JsonPropertyName("latency")]
    public BotLatency Latency { get; set; } = new();

    [JsonPropertyName("reports")]
    public BotReports Reports { get; set; } = new();

    [JsonPropertyName("services")]
    public List<BotService> Services { get; set; } = new();
}

public sealed class BotLatency
{
    [JsonPropertyName("current_ms")]
    public double? CurrentMs { get; set; } // 上游可能给小数
}

public sealed class BotReports
{
    [JsonPropertyName("anomaly_count")]
    public int AnomalyCount { get; set; }

    [JsonPropertyName("normal_count")]
    public int NormalCount { get; set; }
}

/// <summary>上游服务项:延迟字段名是 latency(历史点位里才是 latency_ms)。</summary>
public sealed class BotService
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("latency")]
    public double? Latency { get; set; } // 上游可能给小数
}
