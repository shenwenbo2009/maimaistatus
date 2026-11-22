using System.Text.Json.Serialization;

namespace MaimaiStatus.Web.Services;

/// <summary>GET /api/top100(上游原样透传,字段为 camelCase)。</summary>
public sealed class Top100Dto
{
    [JsonPropertyName("totalPlays")]
    public long TotalPlays { get; set; }

    [JsonPropertyName("songs")]
    public List<LeaderboardSongDto> Songs { get; set; } = new();
}

public sealed class LeaderboardSongDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("plays")]
    public long Plays { get; set; }

    [JsonPropertyName("cover")]
    public string? Cover { get; set; }
}

/// <summary>GET /api/chart145(越级排行)。</summary>
public sealed class Chart145Dto
{
    [JsonPropertyName("totalPlayers")]
    public long TotalPlayers { get; set; }

    [JsonPropertyName("songs")]
    public List<Chart145SongDto> Songs { get; set; } = new();
}

public sealed class Chart145SongDto
{
    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    [JsonPropertyName("players")]
    public long Players { get; set; }

    [JsonPropertyName("musicId")]
    public int MusicId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>上游 level 低标一档(3≈真 MAS,4≈真 ReM),展示时按 TIER_FIX 校准。</summary>
    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("rate")]
    public double Rate { get; set; }

    [JsonPropertyName("avg")]
    public double Avg { get; set; }

    [JsonPropertyName("med")]
    public double Med { get; set; }

    [JsonPropertyName("mode")]
    public double Mode { get; set; }

    [JsonPropertyName("cover")]
    public string? Cover { get; set; }
}

/// <summary>GET /api/ratinglist(不带 version 返回全版本)。</summary>
public sealed class RatingListDto
{
    [JsonPropertyName("versions")]
    public List<RatingVersionBriefDto> Versions { get; set; } = new();

    [JsonPropertyName("data")]
    public List<RatingVersionDto> Data { get; set; } = new();
}

public sealed class RatingVersionBriefDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("total")]
    public long Total { get; set; }
}

public sealed class RatingVersionDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("total")]
    public long Total { get; set; }

    [JsonPropertyName("data")]
    public List<RatingBucketDto> Data { get; set; } = new();
}

public sealed class RatingBucketDto
{
    [JsonPropertyName("bucket")]
    public int Bucket { get; set; }

    [JsonPropertyName("count")]
    public long Count { get; set; }
}
