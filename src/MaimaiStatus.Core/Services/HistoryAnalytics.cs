using System.Globalization;
using MaimaiStatus.Shared.Models;

namespace MaimaiStatus.Core.Services;

/// <summary>
/// 历史统计聚合(替代旧 Go 的 historySummary / incidents / latencyStats /
/// services/availability / services/latency 内联函数)。全部基于本地 JSONL,
/// 维护点位剔除口径与旧版逐条对齐。
/// </summary>
public sealed class HistoryAnalytics
{
    private static readonly Dictionary<string, int> VerdictRank = new()
    {
        ["normal"] = 0,
        ["recovering"] = 1,
        ["degraded"] = 2,
        ["outage"] = 3,
        ["maintenance"] = 4,
        ["nodata"] = 5,
    };

    private readonly HistoryStore _store;
    private readonly MaintenanceWindow _maint;

    public HistoryAnalytics(HistoryStore store, MaintenanceWindow maint)
    {
        _store = store;
        _maint = maint;
    }

    /// <summary>近 hours 小时全服务总可用率(与 availability 聚合同源,维护点位剔除)。
    /// 返回:百分比,总检查数,维护点数。</summary>
    public (double Pct, int Total, int Maint) OverallAvailability(int hours)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-hours).ToUnixTimeMilliseconds();
        var ok = 0;
        var tot = 0;
        var maint = 0;
        foreach (var p in _store.ReadAsc())
        {
            if (p.Ts < cutoff || p.Services is null || p.Services.Count == 0)
            {
                continue;
            }

            if (p.Maint)
            {
                maint++;
                continue;
            }

            foreach (var s in p.Services)
            {
                tot++;
                if (s.State == "ok")
                {
                    ok++;
                }
            }
        }

        return tot == 0 ? (0, 0, maint) : ((double)ok / tot * 100, tot, maint);
    }

    /// <summary>近 12h 可用率与平均延迟;uptime_pct 与服务可用性接口同源。</summary>
    public HistorySummaryDto Summary()
    {
        var pts = _store.ReadAsc(1440);
        var (uptime, _, maint) = OverallAvailability(12);
        var total = 0;
        var latSum = 0;
        var latN = 0;
        LatestDto? latest = null;
        foreach (var p in pts)
        {
            if (p.Maint)
            {
                continue;
            }

            total++;
            if (p.LatencyMs is { } v)
            {
                latSum += v;
                latN++;
            }

            latest = new LatestDto { Ts = p.Ts, Verdict = p.Verdict, VerdictText = p.VerdictText };
        }

        return new HistorySummaryDto
        {
            Total = total,
            UptimePct = uptime.ToString("F2", CultureInfo.InvariantCulture),
            AvgLatencyMs = latN > 0 ? latSum / latN : 0,
            Latest = latest ?? new LatestDto(),
            MaintPoints = maint,
            WindowHours = 12,
        };
    }

    /// <summary>从历史提取异常连续段:verdict 非 normal 即视为事件,最新在前。</summary>
    public List<IncidentDto> Incidents()
    {
        var pts = _store.ReadAsc();
        var outList = new List<IncidentDto>();
        IncidentDto? cur = null;
        var curWorst = "";

        void Flush(HistoryPoint end)
        {
            if (cur is null)
            {
                return;
            }

            cur.EndTs = end.Ts;
            cur.DurationSec = (end.Ts - cur.StartTs) / 1000;
            cur.WorstVerdict = curWorst;
            outList.Add(cur);
            cur = null;
        }

        foreach (var p in pts)
        {
            if (p.Verdict is "" or "normal")
            {
                if (cur is not null)
                {
                    Flush(p);
                }

                continue;
            }

            if (cur is null)
            {
                curWorst = p.Verdict;
                cur = new IncidentDto { StartTs = p.Ts, VerdictText = p.VerdictText, Ongoing = false };
            }
            else if (VerdictRank.GetValueOrDefault(p.Verdict) > VerdictRank.GetValueOrDefault(curWorst))
            {
                curWorst = p.Verdict;
            }

            cur.EndTs = p.Ts;
            cur.VerdictText = p.VerdictText;
        }

        if (cur is not null) // 最后一段仍在持续
        {
            cur.DurationSec = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - cur.StartTs) / 1000;
            cur.WorstVerdict = curWorst;
            cur.Ongoing = true;
            outList.Add(cur);
        }

        outList.Reverse();
        return outList;
    }

    /// <summary>指定窗口内的延迟分位统计,维护点位剔除不计(hours 0&lt;x≤168,否则 24)。</summary>
    public LatencyStatsDto LatencyStats(int hours)
    {
        if (hours <= 0 || hours > 168)
        {
            hours = 24;
        }

        var cutoff = DateTimeOffset.UtcNow.AddHours(-hours).ToUnixTimeMilliseconds();
        var lat = new List<int>();
        var current = 0;
        foreach (var p in _store.ReadAsc())
        {
            if (p.Ts < cutoff || p.LatencyMs is not { } v || p.Maint)
            {
                continue;
            }

            lat.Add(v);
            current = v;
        }

        if (lat.Count == 0)
        {
            return new LatencyStatsDto { Hours = hours, Count = 0 };
        }

        lat.Sort();
        int Pct(double q) => lat[(int)(q * (lat.Count - 1))];
        var sum = 0;
        foreach (var v in lat)
        {
            sum += v;
        }

        return new LatencyStatsDto
        {
            Hours = hours,
            Count = lat.Count,
            Min = lat[0],
            P50 = Pct(0.5),
            P90 = Pct(0.9),
            P99 = Pct(0.99),
            Max = lat[^1],
            Avg = sum / lat.Count,
            Current = current,
        };
    }

    /// <summary>各服务可用率:48 柱窗口 + 各服务百分比 + 全局 overall_pct。</summary>
    public AvailabilityResponseDto Availability(int hours)
    {
        if (hours <= 0 || hours > 168)
        {
            hours = 12;
        }

        const int nBuckets = 48;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var start = now - (long)hours * 3600 * 1000;
        var span = (double)(now - start);
        var okB = new int[nBuckets];
        var totB = new int[nBuckets];
        var maintB = new int[nBuckets];
        var bySvc = new Dictionary<string, (int Ok, int Tot, int LatSum, int LatN)>();
        var order = new List<string>();

        foreach (var p in _store.ReadAsc())
        {
            if (p.Ts < start || p.Services is null || p.Services.Count == 0)
            {
                continue;
            }

            var b = (int)((p.Ts - start) / span * nBuckets);
            if (b >= nBuckets)
            {
                b = nBuckets - 1;
            }

            if (p.Maint)
            {
                maintB[b]++;
                continue; // 维护点不计入可用率
            }

            foreach (var s in p.Services)
            {
                if (!bySvc.TryGetValue(s.Key, out var a))
                {
                    a = default;
                    order.Add(s.Key);
                }

                a.Tot++;
                totB[b]++;
                if (s.State == "ok")
                {
                    a.Ok++;
                    okB[b]++;
                }

                if (s.LatencyMs is { } v)
                {
                    a.LatSum += v;
                    a.LatN++;
                }

                bySvc[s.Key] = a;
            }
        }

        var buckets = new List<AvailabilityBucketDto>(nBuckets);
        for (var i = 0; i < nBuckets; i++)
        {
            var ratio = totB[i] > 0 ? (double)okB[i] / totB[i] : 1.0;
            // 维护柱:过半为维护点即整柱置灰;空桶标 empty,前端留白不断造 100%
            var maint = maintB[i] > 0 && maintB[i] * 2 >= maintB[i] + totB[i];
            buckets.Add(new AvailabilityBucketDto
            {
                Ts = start + (long)(i * span / nBuckets),
                Ratio = ratio,
                Maint = maint,
                Empty = totB[i] == 0 && maintB[i] == 0,
            });
        }

        var svcs = new List<ServiceAvailabilityDto>(order.Count);
        var overallOk = 0;
        var overallTot = 0;
        foreach (var k in order)
        {
            var a = bySvc[k];
            var avg = a.LatN > 0 ? a.LatSum / a.LatN : 0;
            var pct = a.Tot > 0 ? (double)a.Ok / a.Tot * 100 : 100.0;
            overallOk += a.Ok;
            overallTot += a.Tot;
            svcs.Add(new ServiceAvailabilityDto
            {
                Key = k,
                Name = ServiceCatalog.Resolve(k),
                AvailabilityPct = pct.ToString("F1", CultureInfo.InvariantCulture),
                AvgLatencyMs = avg,
                Checks = a.Tot,
            });
        }

        var overall = overallTot > 0 ? (double)overallOk / overallTot * 100 : 0.0;
        return new AvailabilityResponseDto
        {
            Hours = hours,
            Buckets = buckets,
            Services = svcs,
            OverallPct = overall.ToString("F2", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>各服务延迟序列(自动降采样到 maxPoints)+ 维护灰段。</summary>
    public LatencySeriesResponseDto LatencySeries(int hours, int maxPoints)
    {
        if (hours <= 0 || hours > 168)
        {
            hours = 24;
        }

        if (maxPoints <= 0 || maxPoints > 1000)
        {
            maxPoints = 240;
        }

        var cutoff = DateTimeOffset.UtcNow.AddHours(-hours).ToUnixTimeMilliseconds();
        var pts = new List<HistoryPoint>();
        foreach (var p in _store.ReadAsc())
        {
            if (p.Ts >= cutoff && p.Services is { Count: > 0 })
            {
                pts.Add(p);
            }
        }

        var step = 1;
        if (pts.Count > maxPoints)
        {
            step = (pts.Count + maxPoints - 1) / maxPoints;
        }

        var series = new Dictionary<string, List<long[]>>();
        var order = new List<string>();
        for (var i = 0; i < pts.Count; i += step)
        {
            foreach (var s in pts[i].Services!)
            {
                if (s.LatencyMs is not { } v)
                {
                    continue;
                }

                if (!series.ContainsKey(s.Key))
                {
                    order.Add(s.Key);
                    series[s.Key] = [];
                }

                series[s.Key].Add([pts[i].Ts, v]);
            }
        }

        var outList = new List<ServiceLatencySeriesDto>(order.Count);
        foreach (var k in order)
        {
            outList.Add(new ServiceLatencySeriesDto
            {
                Key = k,
                Name = ServiceCatalog.Resolve(k),
                Series = series[k],
            });
        }

        var spans = _maint.MaintSpans(pts);
        return new LatencySeriesResponseDto
        {
            Hours = hours,
            Services = outList,
            MaintSpans = spans.Count > 0 ? spans : null, // Go:nil → null
        };
    }
}
