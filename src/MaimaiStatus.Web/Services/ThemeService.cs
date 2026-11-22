using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
// Fluent 实现类有 SetThemeAsync(ThemeMode)(接口 IThemeService 未暴露),用别名注入具体类
using FluentThemeSvc = Microsoft.FluentUI.AspNetCore.Components.ThemeService;

namespace MaimaiStatus.Web.Services;

/// <summary>maimaiTheme.bootstrap 的返回值。</summary>
public sealed record ThemeState(string Pref, bool Dark);

/// <summary>
/// 三态主题(跟随系统 / 浅色 / 深色):localStorage 持久化,system 挡实时跟随 prefers-color-scheme。
/// 与旧 Nuxt useColorMode 的 preference 语义一致;IsDark 喂给 html.dark 与 ECharts 配色。
/// Fluent 组件 token 双通道同步:初始值靠 body[data-theme](App.razor 预置),运行时切换调 Fluent SetThemeAsync。
/// </summary>
public sealed class ThemeService : IAsyncDisposable
{
    public const string StorageKey = "maimai-theme";

    private readonly IJSRuntime _js;
    private readonly IThemeService _fluentTheme;
    private DotNetObjectReference<ThemeService>? _self;
    private bool _ready;

    public ThemeService(IJSRuntime js, IThemeService fluentTheme)
    {
        _js = js;
        _fluentTheme = fluentTheme;
    }

    /// <summary>当前挡位:system / light / dark。</summary>
    public string Preference { get; private set; } = "system";

    /// <summary>解析后的实际深浅(html.dark 类与 ECharts 配色共用)。</summary>
    public bool IsDark { get; private set; }

    public event Action? Changed;

    /// <summary>仅可在 OnAfterRenderAsync(firstRender) 调用(预渲染阶段没有 JS)。</summary>
    public async Task InitializeAsync()
    {
        if (_ready)
        {
            return;
        }

        _ready = true;
        _self = DotNetObjectReference.Create(this);
        var state = await _js.InvokeAsync<ThemeState>("maimaiTheme.bootstrap", StorageKey, _self);
        Preference = state.Pref;
        IsDark = state.Dark;
        await SyncFluentAsync();
        Changed?.Invoke();
    }

    public async Task SetPreferenceAsync(string pref)
    {
        Preference = pref;
        IsDark = await _js.InvokeAsync<bool>("maimaiTheme.apply", pref);
        await _js.InvokeVoidAsync("maimaiTheme.save", StorageKey, pref);
        await SyncFluentAsync();
        Changed?.Invoke();
    }

    /// <summary>把当前挡位同步到 Fluent 设计 token(失败不连累自家主题)。</summary>
    private async Task SyncFluentAsync()
    {
        try
        {
            var mode = Preference switch
            {
                "dark" => ThemeMode.Dark,
                "light" => ThemeMode.Light,
                _ => ThemeMode.System,
            };
            // SetThemeAsync(ThemeMode) 只在实现类上,DI 注册的是接口 → 向下转型
            if (_fluentTheme is FluentThemeSvc impl)
            {
                await impl.SetThemeAsync(mode);
            }
        }
        catch (JSException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>system 挡下系统深浅变化的回调(JSInvokable)。</summary>
    [JSInvokable]
    public async Task OnSystemThemeChanged(bool dark)
    {
        if (Preference == "system" && IsDark != dark)
        {
            IsDark = dark;
            await SyncFluentAsync();
            Changed?.Invoke();
        }
    }

    public ValueTask DisposeAsync()
    {
        _self?.Dispose();
        _self = null;
        return ValueTask.CompletedTask;
    }
}
