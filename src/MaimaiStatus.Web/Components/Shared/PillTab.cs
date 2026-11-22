namespace MaimaiStatus.Web.Components.Shared;

/// <summary>分段胶囊标签项(与旧 TabsItem 对应:key/label/徽章)。</summary>
public sealed record PillTab(string Key, string Label, string? Badge = null);
