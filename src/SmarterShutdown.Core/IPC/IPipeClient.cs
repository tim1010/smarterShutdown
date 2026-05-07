using System.Threading.Channels;

namespace SmarterShutdown.Core.IPC;

public interface IPipeClient : IAsyncDisposable
{
    /// <summary>
    /// Starts the connect-and-read loop. Reconnects automatically when the server is unavailable
    /// or the connection drops, until the cancellation token fires.
    /// </summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>
    /// Sends a message to the server. Silently drops the message if no connection is currently
    /// established — caller should treat send as best-effort.
    /// </summary>
    Task SendAsync(PipeMessage message, CancellationToken ct);

    /// <summary>Inbound messages dispatched from the server.</summary>
    ChannelReader<PipeMessage> Incoming { get; }
}
