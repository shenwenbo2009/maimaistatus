using System.Text;
using MaimaiStatus.Core;
using MaimaiStatus.Core.Services;
using MaimaiStatus.Shared.Models;

namespace MaimaiStatus.Web.Services;

/// <summary>上报提交结果(消息可直接上屏)。</summary>
public sealed record ReportSubmitResult(bool Ok, string Message);

/// <summary>
/// 手动上报客户端(Blazor 交互式服务端持有私钥,签名不出服务器):
/// 领验证码 → 本地 ReportSigner 签名 → POST 本进程 /api/report 透传上游。
/// </summary>
public sealed class ReportClient(ApiClient api, CoreOptions opts)
{
    private readonly ApiClient _api = api;
    private readonly CoreOptions _opts = opts;

    /// <summary>站长是否配置了上报密钥(未配置时向导只提示不放行,与旧站 keysReady 一致)。</summary>
    public bool KeysReady =>
        !string.IsNullOrWhiteSpace(_opts.ReportClientId) &&
        !string.IsNullOrWhiteSpace(_opts.ReportPrivateKey) &&
        !_opts.ReportClientId.Contains("<your-key>") &&
        !_opts.ReportPrivateKey.Contains("<your-key>");

    public Task<CaptchaResponseDto> GetCaptchaAsync(CancellationToken ct = default) =>
        _api.GetAsync<CaptchaResponseDto>("/api/report/captcha", null, ct);

    /// <summary>
    /// 提交一次上报:t=类型码, v=次数, displayName 空则用类型名, r 固定 "WEB"。
    /// </summary>
    public async Task<ReportSubmitResult> SubmitAsync(
        int t, int count, string typeLabel, string displayName,
        string captchaId, string captchaCode, CancellationToken ct = default)
    {
        if (!KeysReady)
        {
            return new ReportSubmitResult(false, "站长未配置上报密钥(REPORT_CLIENT_ID / REPORT_PRIVATE_KEY)。");
        }

        var name = string.IsNullOrWhiteSpace(displayName) ? typeLabel : displayName.Trim();
        var body = $"[{{\"t\":{t},\"v\":{Math.Max(1, count)},\"r\":\"WEB\",\"bot_display_name\":{JsonString(name)}}}]";
        var ts = ReportSigner.NowMs();
        var sig = ReportSigner.Sign(_opts.ReportPrivateKey, _opts.ReportClientId, ts, Encoding.UTF8.GetBytes(body));

        var headers = new Dictionary<string, string>
        {
            ["X-Client-ID"] = _opts.ReportClientId,
            ["X-Timestamp"] = ts,
            ["X-Signature"] = sig,
            ["X-Captcha-ID"] = captchaId,
            ["X-Captcha-Code"] = captchaCode,
        };

        try
        {
            var (status, respBody) = await _api.PostJsonAsync("/api/report", body, headers, ct);
            if (status == 200)
            {
                return new ReportSubmitResult(true, $"已上报「{name}」× {Math.Max(1, count)},感谢反馈!");
            }

            return new ReportSubmitResult(false, status switch
            {
                401 => "身份验证失败:检查上报密钥配置。",
                403 => "验证码已失效,请换一张重试。",
                429 => "触发限流:请 30 秒后再试。",
                _ => ExtractError(respBody) ?? $"上报失败(HTTP {status}),请稍后重试。",
            });
        }
        catch (ApiException e)
        {
            return new ReportSubmitResult(false, e.Message);
        }
    }

    private static string JsonString(string s) =>
        System.Text.Json.JsonSerializer.Serialize(s);

    private static string? ExtractError(string body)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("error", out var err))
            {
                return err.GetString();
            }
        }
        catch (System.Text.Json.JsonException)
        {
        }

        return null;
    }
}
