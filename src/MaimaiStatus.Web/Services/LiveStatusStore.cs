namespace MaimaiStatus.Web.Services;

/// <summary>
/// 顶栏状态圆点的数据源(每个电路一份):监控页 SSE/轮询推送进来,顶栏订阅刷新。
/// 顶栏自身也低频兜底拉取,离开监控页后圆点仍会更新。
/// </summary>
public sealed class LiveStatusStore
{
    public StatusSnapshotDto? Current { get; private set; }

    public long UpdatedAt { get; private set; }

    public event Action? Changed;

    public void Publish(StatusSnapshotDto status)
    {
        Current = status;
        UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Changed?.Invoke();
    }
}
