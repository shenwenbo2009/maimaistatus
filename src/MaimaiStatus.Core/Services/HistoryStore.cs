using System.Text.Json;
using MaimaiStatus.Shared;
using MaimaiStatus.Shared.Models;

namespace MaimaiStatus.Core.Services;

/// <summary>
/// JSONL 历史存储(data/history.jsonl)。格式与旧 Go 网关逐字节兼容,可直接沿用生产数据。
/// 单进程内用互斥锁串行化读写(与 Go 的 histMu 同策略)。
/// </summary>
public sealed class HistoryStore
{
    private readonly object _gate = new();
    private readonly CoreOptions _opts;
    private readonly MaintenanceWindow _maint;

    public HistoryStore(CoreOptions opts, MaintenanceWindow maint)
    {
        _opts = opts;
        _maint = maint;
    }

    public string FilePath => Path.Combine(_opts.DataDir, "history.jsonl");

    /// <summary>解析上游 /api/bot 快照并追加一行(解析失败静默丢弃,与 Go 一致)。</summary>
    public void AppendFromUpstream(string rawJson)
    {
        BotStatus? s;
        try
        {
            s = JsonSerializer.Deserialize<BotStatus>(rawJson, MaimaiJson.Options);
        }
        catch (JsonException)
        {
            return;
        }

        if (s is null)
        {
            return;
        }

        var ok = 0;
        var down = 0;
        var svcs = new List<HistoryServiceItem>(s.Services.Count);
        foreach (var sv in s.Services)
        {
            if (sv.State == "ok")
            {
                ok++;
            }
            else if (sv.State == "down")
            {
                down++;
            }

            svcs.Add(new HistoryServiceItem
            {
                Key = sv.Key,
                State = sv.State,
                // 上游 latency 可能是小数,历史点位保持整型(格式兼容),四舍五入取整
                LatencyMs = sv.Latency is { } lv ? (int)Math.Round(lv) : null,
            });
        }

        var p = new HistoryPoint
        {
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Verdict = s.Verdict,
            VerdictText = s.VerdictText,
            LatencyMs = s.Latency.CurrentMs is { } cm ? (int)Math.Round(cm) : null,
            Anomaly = s.Reports.AnomalyCount,
            Normal = s.Reports.NormalCount,
            OkCount = ok,
            DownCount = down,
            Services = svcs.Count > 0 ? svcs : null,
        };
        p.Maint = _maint.IsMaintPoint(p);

        var line = JsonSerializer.Serialize(p, MaimaiJson.Options);

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_opts.DataDir);
                File.AppendAllText(FilePath, line + "\n");
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"append history: {ex.Message}");
                return;
            }

            TrimLocked(_opts.HistoryMax);
        }
    }

    private void TrimLocked(int max)
    {
        if (max <= 0)
        {
            return;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(FilePath);
        }
        catch (IOException)
        {
            return;
        }

        if (lines.Length <= max)
        {
            return;
        }

        var kept = lines[^max..];
        var tmp = FilePath + ".tmp";
        try
        {
            File.WriteAllLines(tmp, kept);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (IOException)
        {
            // 修剪失败不致命,下次再试
        }
    }

    /// <summary>按时间正序返回(limit&gt;0 取末尾 limit 条),供统计类接口使用。</summary>
    public List<HistoryPoint> ReadAsc(int limit = 0)
    {
        lock (_gate)
        {
            var all = new List<HistoryPoint>();
            if (!File.Exists(FilePath))
            {
                return all;
            }

            foreach (var line in File.ReadLines(FilePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                HistoryPoint? p;
                try
                {
                    p = JsonSerializer.Deserialize<HistoryPoint>(line, MaimaiJson.Options);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (p is null)
                {
                    continue;
                }

                p.Maint = _maint.IsMaintPoint(p); // 老数据补算
                all.Add(p);
            }

            if (limit > 0 && all.Count > limit)
            {
                all = all.GetRange(all.Count - limit, limit);
            }

            return all;
        }
    }

    /// <summary>最新在前返回(limit&gt;0 取末尾 limit 条再倒序),前端列表渲染用。</summary>
    public List<HistoryPoint> ReadDesc(int limit = 0)
    {
        var pts = ReadAsc(limit);
        pts.Reverse();
        return pts;
    }
}
