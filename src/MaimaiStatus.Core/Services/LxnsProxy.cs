namespace MaimaiStatus.Core.Services;

/// <summary>
/// 落雪公开曲目列表代理(无需登录,1h 缓存;查分 OAuth 已下线,仅保留曲目信息)。
/// 与旧 Go lxns.go 行为一致:缓存期内直接回放,过期重抓,失败回 502。
/// </summary>
public sealed class LxnsProxy
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    private readonly CoreOptions _opts;
    private readonly UpstreamGateway _gw;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private byte[]? _body;
    private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;

    public LxnsProxy(CoreOptions opts, UpstreamGateway gw)
    {
        _opts = opts;
        _gw = gw;
    }

    public async Task<byte[]?> GetSongListAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_body is not null && DateTimeOffset.UtcNow - _fetchedAt < Ttl)
            {
                return _body;
            }
        }
        finally
        {
            _gate.Release();
        }

        var result = await _gw.FetchAsync($"{_opts.LxnsBase}/api/v0/maimai/song/list", ct);
        if (!result.Ok || result.Status != 200)
        {
            return null;
        }

        await _gate.WaitAsync(ct);
        try
        {
            _body = result.Body;
            _fetchedAt = DateTimeOffset.UtcNow;
            return _body;
        }
        finally
        {
            _gate.Release();
        }
    }
}
