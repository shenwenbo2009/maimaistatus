namespace MaimaiStatus.Shared.Models;

/// <summary>服务 key → 展示名(历史点位只存 key,展示时还原;与旧 Go 网关一致)。</summary>
public static class ServiceCatalog
{
    private static readonly Dictionary<string, string> Names = new()
    {
        ["net"] = "NET",
        ["aime"] = "会员",
        ["title"] = "标题",
        ["member"] = "公众号(NET)",
        ["qr"] = "公众号(二维码)",
        ["bl"] = "计费",
    };

    public static string Resolve(string key) =>
        Names.TryGetValue(key, out var name) ? name : key;
}
