using System.Security.Cryptography;
using System.Text;

namespace MaimaiStatus.Core.Services;

/// <summary>
/// 上报签名(与上游 bot-post 约定一致):
/// 先 SHA-256 取 bodyHash,再用私钥对 "POST:/bot-post:{clientId}:{ts}:{bodyHash}" 做 HMAC-SHA256,输出小写 hex。
/// 签名留在服务器端(Blazor 交互式服务端持有私钥),不再下发浏览器——比旧 Vue 方案更安全。
/// </summary>
public static class ReportSigner
{
    public static string Sign(string privateKey, string clientId, string timestampMs, ReadOnlySpan<byte> body)
    {
        var bodyHash = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        var text = $"POST:/bot-post:{clientId}:{timestampMs}:{bodyHash}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(privateKey));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    /// <summary>生成毫秒时间戳(X-Timestamp 用)。</summary>
    public static string NowMs() =>
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
}
