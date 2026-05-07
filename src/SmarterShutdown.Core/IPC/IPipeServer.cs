using System.Threading.Channels;

namespace SmarterShutdown.Core.IPC;

public interface IPipeServer : IAsyncDisposable
{
    void Start(CancellationToken ct);
    Task BroadcastAsync(PipeMessage message, CancellationToken ct);
    ChannelReader<PipeMessage> Incoming { get; }

    /// <summary>Count of currently connected pipe clients (Notifier instances).</summary>
    int ConnectedClients { get; }
}
