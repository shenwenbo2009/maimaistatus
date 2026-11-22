using System.Net;
using Microsoft.Extensions.Caching.Memory;

namespace MaimaiStatus.Core.Services;

/// <summary>上游抓取结果。Ok=false 时 Error 为原因(对应旧 Go 的 502 响应体)。</summary>
public sealed record UpstreamResult(bool Ok, byte[]? Body, int Status, bool Hit, string? Error);

/// <summary>
/// isMaiDown 上游代理:带 TTL 的响应缓存(状态 20s / 榜单 1h)+ 8MB 读取上限 + 12s 超时。
/// X-Cache HIT/MISS 语义与旧 Go 网关一致(每次请求如实标注)。
/// </summary>
public sealed class UpstreamGateway
{
    // 注意:HTTP 响应头只允许 ASCII(Kestrel 校验),中文归属文案放在页面与文档里
    public const string Attribution = "isMaiDown by Chongxi (https://mai.chongxi.us)";

    private readonly IHttpClientFactory _http;
    private readonly IMemoryCache _cache;

    public UpstreamGateway(IHttpClientFactory http, IMemoryCache cache)
    {
        _http = http;
        _cache = cache;
    }

    /// <summary>缓存命中直接返回;未命中抓上游并写缓存(失败不缓存)。</summary>
    public async Task<UpstreamResult> GetCachedAsync(
        string key, string url, TimeSpan ttl, CancellationToken ct)
    {
        if (TryGetCached(key, out var entry))
        {
            return new UpstreamResult(true, entry.Body, entry.Status, Hit: true, null);
        }

        var fetched = await FetchAsync(url, ct);
        if (!fetched.Ok)
        {
            return fetched;
        }

        SetCached(key, fetched.Body!, fetched.Status, ttl);
        return new UpstreamResult(true, fetched.Body, fetched.Status, Hit: false, null);
    }

    public bool TryGetCached(string key, out (byte[] Body, int Status) entry)
    {
        if (_cache.TryGetValue(key, out var cached) && cached is CachedEntry e)
        {
            entry = (e.Body, e.Status);
            return true;
        }

        entry = default;
        return false;
    }

    public void SetCached(string key, byte[] body, int status, TimeSpan ttl)
    {
        _cache.Set(key, new CachedEntry(body, status), ttl);
    }

    /// <summary>抓上游 GET:12s 超时、8MB 上限、带 UA(对应旧 Go fetchUpstream)。</summary>
    public async Task<UpstreamResult> FetchAsync(string url, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "maimai-gateway/1.0 (+https://mai.chongxi.us)");
            using var resp = await _http.CreateClient("upstream").SendAsync(
                req, HttpCompletionOption.ResponseHeadersRead, ct);
            var body = await ReadCappedAsync(resp.Content, 8 << 20, ct);
            return new UpstreamResult(true, body, (int)resp.StatusCode, Hit: false, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return new UpstreamResult(false, null, 0, Hit: false, ex.Message);
        }
    }

    /// <summary>读取响应体,上限 maxBytes(超出截断,与 Go 的 io.LimitReader 一致)。</summary>
    internal static async Task<byte[]> ReadCappedAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        return await ReadCappedAsync(stream, maxBytes, ct);
    }

    /// <summary>从任意流读取,上限 maxBytes(请求体同款)。</summary>
    internal static async Task<byte[]> ReadCappedAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buf = new byte[16 * 1024];
        var total = 0;
        while (total < maxBytes)
        {
            var read = await stream.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, maxBytes - total)), ct);
            if (read <= 0)
            {
                break;
            }

            ms.Write(buf, 0, read);
            total += read;
        }

        return ms.ToArray();
    }

    private sealed record CachedEntry(byte[] Body, int Status);
}
