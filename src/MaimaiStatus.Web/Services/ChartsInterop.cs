using System.Text.Json;
using Microsoft.JSInterop;
using MaimaiStatus.Shared;

namespace MaimaiStatus.Web.Services;

/// <summary>
/// ECharts Interop 封装:配置全在 wwwroot/js/charts.js(从旧 Vue 平移),C# 只管数据与状态。
/// render 返回:'' 成功 / 'HIDDEN' 容器隐藏(等可见重画)/ 其余为错误文案(报错上屏)。
/// </summary>
public sealed class ChartsInterop(IJSRuntime js)
{
    private readonly IJSRuntime _js = js;

    private static string Payload(object value) =>
        JsonSerializer.Serialize(value, MaimaiJson.Options);

    /// <summary>延迟多线(维护灰段 + dataZoom)。</summary>
    public Task<string> RenderLatencyAsync(string id, ElementReference el, object payload, bool dark, CancellationToken ct = default) =>
        InvokeAsync("latency", id, el, payload, dark, ct);

    /// <summary>服务可用率柱(48 桶,maint 灰 / empty 白)。</summary>
    public Task<string> RenderAvailabilityAsync(string id, ElementReference el, object payload, bool dark, CancellationToken ct = default) =>
        InvokeAsync("availability", id, el, payload, dark, ct);

    /// <summary>上报双柱(异常/正常,带 dataZoom)。</summary>
    public Task<string> RenderReportsAsync(string id, ElementReference el, object payload, bool dark, CancellationToken ct = default) =>
        InvokeAsync("reports", id, el, payload, dark, ct);

    public ValueTask DisposeAsync(string id) => _js.InvokeVoidAsync("maimaiCharts.dispose", id);

    private Task<string> InvokeAsync(string kind, string id, ElementReference el, object payload, bool dark, CancellationToken ct) =>
        _js.InvokeAsync<string>("maimaiCharts.render", ct, kind, id, el, Payload(payload), dark).AsTask();
}
