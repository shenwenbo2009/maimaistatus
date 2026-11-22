using System.Text;
using Microsoft.Extensions.Hosting;

namespace MaimaiStatus.Core.Services;

/// <summary>
/// 定时采集(替代 Go ticker):启动即采一次,之后每 POLL_INTERVAL_SEC 抓 /api/bot,
/// 命中 200 则写缓存 → 落盘历史 → 广播 SSE。单次失败只记日志,不影响下一轮。
/// </summary>
public sealed class PollingService : BackgroundService
{
    private readonly CoreOptions _opts;
    private readonly UpstreamGateway _gw;
    private readonly HistoryStore _store;
    private readonly SseHub _hub;
    private readonly ILogger<PollingService> _log;

    public PollingService(
        CoreOptions opts, UpstreamGateway gw, HistoryStore store, SseHub hub, ILogger<PollingService> log)
    {
        _opts = opts;
        _gw = gw;
        _store = store;
        _hub = hub;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opts.PollEnabled)
        {
            _log.LogInformation("polling disabled (POLL_ENABLED=0)");
            return;
        }

        _log.LogInformation(
            "polling started: upstream={Upstream} interval={Interval}s dataDir={DataDir}",
            _opts.UpstreamBase, _opts.PollIntervalSec, _opts.DataDir);

        await CollectAsync(stoppingToken);
        using var timer = new PeriodicTimer(_opts.PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CollectAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关停
        }
    }

    private async Task CollectAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        var result = await _gw.FetchAsync($"{_opts.UpstreamBase}/api/bot", cts.Token);
        if (!result.Ok)
        {
            _log.LogWarning("poll failed: {Error}", result.Error);
            return;
        }

        if (result.Status != 200)
        {
            _log.LogWarning("poll: upstream {Status}", result.Status);
            return;
        }

        var body = Encoding.UTF8.GetString(result.Body!);
        _gw.SetCached("status", result.Body!, 200, TimeSpan.FromSeconds(20));
        _store.AppendFromUpstream(body);
        _hub.Broadcast(result.Body!);
    }
}
