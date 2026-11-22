using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace MaimaiStatus.Core.Services;

/// <summary>
/// 上报验证码:8 位数字、5 分钟有效、一次有效(校验成功即销毁)。
/// 校验失败不销毁(允许用户重试),过期在下次生成时顺手清理——与旧 Go 行为一致。
/// </summary>
public sealed class CaptchaService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, Entry> _map = new();

    public CaptchaService(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <summary>生成 (captcha_id 32位hex, 8位数字码)。</summary>
    public (string Id, string Code) Create()
    {
        Span<byte> raw = stackalloc byte[24];
        RandomNumberGenerator.Fill(raw);

        var id = Convert.ToHexString(raw[..16]).ToLowerInvariant();
        var digits = new char[8];
        for (var i = 0; i < 8; i++)
        {
            digits[i] = (char)('0' + raw[16 + i] % 10);
        }

        var now = _time.GetUtcNow();
        foreach (var (k, e) in _map)
        {
            if (now >= e.ExpiresAt)
            {
                _map.TryRemove(k, out _); // 顺手清过期
            }
        }

        _map[id] = new Entry(new string(digits), now.Add(Ttl));
        return (id, new string(digits));
    }

    /// <summary>校验并消费(恒定时间比较;成功即销毁,过期/不存在返回 false)。</summary>
    public bool Verify(string id, string code)
    {
        if (string.IsNullOrEmpty(id) || !_map.TryGetValue(id, out var e))
        {
            return false;
        }

        if (_time.GetUtcNow() >= e.ExpiresAt)
        {
            _map.TryRemove(id, out _);
            return false;
        }

        var a = Encoding.ASCII.GetBytes(e.Code);
        var b = Encoding.ASCII.GetBytes(code ?? "");
        if (!CryptographicOperations.FixedTimeEquals(a, b))
        {
            return false; // 猜错不销毁,可重试(与 Go subtle.ConstantTimeCompare 一致)
        }

        _map.TryRemove(id, out _); // 一次有效
        return true;
    }

    private sealed record Entry(string Code, DateTimeOffset ExpiresAt);
}
