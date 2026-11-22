using System.Text.Encodings.Web;
using System.Text.Json;

namespace MaimaiStatus.Shared;

/// <summary>全站统一 JSON 序列化设置:camelCase + 中文原样写出(与旧 Go 网关字节级兼容)。</summary>
public static class MaimaiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
