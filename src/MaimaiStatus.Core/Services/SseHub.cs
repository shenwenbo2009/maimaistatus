using System.Threading.Channels;

namespace MaimaiStatus.Core.Services;

/// <summary>
/// SSE 订阅者广播:每次采集完成向所有 /api/stream 连接推送原始状态 JSON。
/// 慢订阅者直接丢包(容量 4 满即丢),保证采集永不被阻塞——与旧 Go hub 同策略。
/// </summary>
public sealed class SseHub
{
    private readonly object _gate = new();
    private readonly List<Channel<byte[]>> _subs = [];

    public Channel<byte[]> Add()
    {
        var ch = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(4) { FullMode = BoundedChannelFullMode.DropWrite });
        lock (_gate)
        {
            _subs.Add(ch);
        }

        return ch;
    }

    public void Remove(Channel<byte[]> ch)
    {
        lock (_gate)
        {
            _subs.Remove(ch);
        }

        ch.Writer.TryComplete();
    }

    public void Broadcast(byte[] payload)
    {
        lock (_gate)
        {
            foreach (var ch in _subs)
            {
                ch.Writer.TryWrite(payload); // 满则丢弃,不等慢客户端
            }
        }
    }
}
