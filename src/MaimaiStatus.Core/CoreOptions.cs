using Microsoft.Extensions.Configuration;

namespace MaimaiStatus.Core;

/// <summary>
/// 网关配置。键名兼容旧 Go 网关的裸环境变量(UPSTREAM_BASE / DATA_DIR / MAINT_WINDOWS ...),
/// 也支持 Maimai:* 分节(appsettings.json)。环境变量优先级更高,与旧部署的 .env 零改动兼容。
/// </summary>
public sealed class CoreOptions
{
    /// <summary>旧 Go 网关裸环境变量名 → 本对象属性的映射。</summary>
    private static readonly (string Env, string Prop)[] Keys =
    [
        ("UPSTREAM_BASE", nameof(UpstreamBase)),
        ("REPORT_UPSTREAM", nameof(ReportUpstream)),
        ("LXNS_BASE", nameof(LxnsBase)),
        ("POLL_INTERVAL_SEC", nameof(PollIntervalSec)),
        ("HISTORY_MAX", nameof(HistoryMax)),
        ("DATA_DIR", nameof(DataDir)),
        ("CORS_ALLOW", nameof(CorsAllow)),
        ("MAINT_WINDOWS", nameof(MaintWindows)),
        ("POLL_ENABLED", nameof(PollEnabled)),
        ("REPORT_CLIENT_ID", nameof(ReportClientId)),
        ("REPORT_PRIVATE_KEY", nameof(ReportPrivateKey)),
    ];

    public string UpstreamBase { get; private set; } = "https://mai.chongxi.us";
    public string ReportUpstream { get; private set; } = "https://maiapi.chongxi.us";
    public string LxnsBase { get; private set; } = "https://maimai.lxns.net";
    public int PollIntervalSec { get; private set; } = 30;
    public int HistoryMax { get; private set; } = 20000;
    public string DataDir { get; private set; } = "./data";
    public string CorsAllow { get; private set; } = "*";
    public string MaintWindows { get; private set; } = "04:00-07:00";
    public bool PollEnabled { get; private set; } = true;
    public string ReportClientId { get; private set; } = "";
    public string ReportPrivateKey { get; private set; } = "";

    public TimeSpan PollInterval => TimeSpan.FromSeconds(PollIntervalSec <= 0 ? 30 : PollIntervalSec);

    public static CoreOptions FromConfiguration(IConfiguration config)
    {
        var o = new CoreOptions();
        foreach (var (env, prop) in Keys)
        {
            // 裸环境变量(旧部署)优先,其次 Maimai:分节,最后保持代码默认
            var raw = config[env] ?? config[$"Maimai:{prop}"];
            if (raw is null)
            {
                continue;
            }

            switch (prop)
            {
                case nameof(PollIntervalSec) when int.TryParse(raw, out var secs) && secs > 0:
                    o.PollIntervalSec = secs;
                    break;
                case nameof(PollIntervalSec):
                    o.PollIntervalSec = 30;
                    break;
                case nameof(HistoryMax) when int.TryParse(raw, out var max) && max > 0:
                    o.HistoryMax = max;
                    break;
                case nameof(HistoryMax):
                    o.HistoryMax = 20000;
                    break;
                case nameof(PollEnabled):
                    o.PollEnabled = raw is not ("0" or "false" or "False");
                    break;
                case nameof(UpstreamBase):
                    o.UpstreamBase = raw.TrimEnd('/');
                    break;
                case nameof(ReportUpstream):
                    o.ReportUpstream = raw.TrimEnd('/');
                    break;
                case nameof(LxnsBase):
                    o.LxnsBase = raw.TrimEnd('/');
                    break;
                case nameof(DataDir):
                    o.DataDir = raw;
                    break;
                case nameof(CorsAllow):
                    o.CorsAllow = raw;
                    break;
                case nameof(MaintWindows):
                    o.MaintWindows = raw;
                    break;
                case nameof(ReportClientId):
                    o.ReportClientId = raw;
                    break;
                case nameof(ReportPrivateKey):
                    o.ReportPrivateKey = raw;
                    break;
            }
        }

        // 前端旧环境变量名兜底(Blazor 迁移期密钥可继续用同一份 .env)
        o.ReportPrivateKey = config["NUXT_PUBLIC_REPORT_PRIVATE_KEY"] ?? o.ReportPrivateKey;
        return o;
    }
}
