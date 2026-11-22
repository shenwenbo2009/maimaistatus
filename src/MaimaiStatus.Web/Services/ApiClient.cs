using System.Net;
using System.Text;
using MaimaiStatus.Core;
using MaimaiStatus.Shared;

namespace MaimaiStatus.Web.Services;

/// <summary>API 调用失败(消息可直接上屏)。</summary>
public sealed class ApiException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// 统一 API 访问:先走本进程网关(/api/*),失败时对上游真实存在的 4 个接口直连兜底——
/// 与旧前端 useApi.ts 逻辑一致;网关专有接口(历史/统计类)不打扰上游,直接抛错。
/// </summary>
public sealed class ApiClient
{
    // 仅这些路径允许直连上游兜底(其余是网关专有接口)
    private static readonly Dictionary<string, string> UpstreamPaths = new()
    {
        ["/api/status"] = "/api/bot",
        ["/api/top100"] = "/api/top100",
        ["/api/chart145"] = "/api/chart145",
        ["/api/ratinglist"] = "/api/ratinglist",
    };

    private readonly IHttpClientFactory _factory;
    private readonly NavigationManager _nav;
    private readonly CoreOptions _opts;

    public ApiClient(IHttpClientFactory factory, NavigationManager nav, CoreOptions opts)
    {
        _factory = factory;
        _nav = nav;
        _opts = opts;
    }

    /// <summary>本进程网关绝对地址(/api/... → http://host/api/...)。</summary>
    public Uri GatewayUri(string path, string? query = null)
    {
        var url = _nav.ToAbsoluteUri(path).ToString();
        if (!string.IsNullOrEmpty(query))
        {
            url += (url.Contains('?') ? "&" : "?") + query;
        }

        return new Uri(url);
    }

    public async Task<T> GetAsync<T>(string path, string? query = null, CancellationToken ct = default)
    {
        try
        {
            return await GetOnceAsync<T>(GatewayUri(path, query), ct);
        }
        catch (Exception gatewayError) when (UpstreamPaths.TryGetValue(path, out _))
        {
            // 网关挂了才直连上游;仍失败则抛上游错误(与旧 $fetch 链路一致)
            var upstream = UpstreamPaths[path];
            var uri = new Uri(_opts.UpstreamBase + upstream + (query is null ? "" : "?" + query));
            try
            {
                return await GetOnceAsync<T>(uri, ct);
            }
            catch (Exception upstreamError)
            {
                throw new ApiException(Describe(upstreamError), upstreamError is ApiException ? gatewayError : upstreamError);
            }
        }
        catch (ApiException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new ApiException(Describe(e), e);
        }
    }

    /// <summary>POST JSON(上报等):返回状态码 + 响应原文,不抛业务错误。</summary>
    public async Task<(int Status, string Body)> PostJsonAsync(
        string path, string json, IReadOnlyDictionary<string, string>? headers = null, CancellationToken ct = default)
    {
        var client = _factory.CreateClient("upstream");
        using var req = new HttpRequestMessage(HttpMethod.Post, GatewayUri(path))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (headers is not null)
        {
            foreach (var (k, v) in headers)
            {
                req.Headers.TryAddWithoutValidation(k, v);
            }
        }

        try
        {
            using var resp = await client.SendAsync(req, ct);
            return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync(ct));
        }
        catch (Exception e)
        {
            throw new ApiException(Describe(e), e);
        }
    }

    private async Task<T> GetOnceAsync<T>(Uri uri, CancellationToken ct)
    {
        var client = _factory.CreateClient("upstream");
        try
        {
            using var resp = await client.GetAsync(uri, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (resp.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.NotModified))
            {
                throw new ApiException($"HTTP {(int)resp.StatusCode} {resp.StatusCode} · {Truncate(body)}");
            }

            var data = System.Text.Json.JsonSerializer.Deserialize<T>(body, MaimaiJson.Options);
            return data ?? throw new ApiException("响应为空");
        }
        catch (ApiException)
        {
            throw;
        }
        catch (System.Text.Json.JsonException e)
        {
            throw new ApiException("响应解析失败:" + e.Message, e);
        }
        catch (TaskCanceledException e)
        {
            throw new ApiException("请求超时(网关或上游无响应)", e);
        }
        catch (HttpRequestException e)
        {
            throw new ApiException(Describe(e), e);
        }
    }

    private static string Truncate(string s) => s.Length <= 160 ? s : s[..160];

    private static string Describe(Exception e) => e switch
    {
        ApiException => e.Message,
        HttpRequestException => "网关未连接:请确认后端服务已启动",
        TaskCanceledException => "请求超时:网关或上游无响应",
        _ => e.Message,
    };
}
