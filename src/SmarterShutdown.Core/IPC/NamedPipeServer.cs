using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace SmarterShutdown.Core.IPC;

[SupportedOSPlatform("windows")]
public sealed class NamedPipeServer : IPipeServer
{
    public const string DefaultPipeName = "SmarterShutdown";

    private readonly string _pipeName;
    private readonly ILogger<NamedPipeServer> _logger;
    private readonly Channel<PipeMessage> _incoming = Channel.CreateUnbounded<PipeMessage>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly ConcurrentDictionary<Guid, Connection> _connections = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public NamedPipeServer(ILogger<NamedPipeServer> logger, string pipeName = DefaultPipeName)
    {
        _logger = logger;
        _pipeName = pipeName;
    }

    public ChannelReader<PipeMessage> Incoming => _incoming.Reader;

    public int ConnectedClients => _connections.Count;

    public void Start(CancellationToken ct)
    {
        if (_acceptLoop is not null) return;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
    }

    public async Task BroadcastAsync(PipeMessage message, CancellationToken ct)
    {
        var line = message.Serialize();
        var sends = _connections.Values
            .Select(c => c.SendAsync(line, ct))
            .ToArray();
        await Task.WhenAll(sends);
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; } catch { /* shutdown */ }
        }
        foreach (var conn in _connections.Values)
        {
            await conn.DisposeAsync();
        }
        _connections.Clear();
        _cts?.Dispose();
        _incoming.Writer.TryComplete();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = NamedPipeServerStreamAcl.Create(
                    pipeName: _pipeName,
                    direction: PipeDirection.InOut,
                    maxNumberOfServerInstances: NamedPipeServerStream.MaxAllowedServerInstances,
                    transmissionMode: PipeTransmissionMode.Byte,
                    options: PipeOptions.Asynchronous,
                    inBufferSize: 4096,
                    outBufferSize: 4096,
                    pipeSecurity: BuildSecurity());

                await server.WaitForConnectionAsync(ct);

                var conn = new Connection(server, this, _logger);
                _connections[conn.Id] = conn;
                _ = conn.ReadLoopAsync(ct);
                server = null; // ownership handed off to Connection
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pipe accept loop error");
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ContinueWith(_ => { });
            }
            finally
            {
                server?.Dispose();
            }
        }
    }

    private static PipeSecurity BuildSecurity()
    {
        var sec = new PipeSecurity();
        // Authenticated users in the local session may read/write the pipe.
        sec.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));
        // Block anything coming over the network — pipe is intra-machine only.
        sec.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Deny));
        return sec;
    }

    internal void RemoveConnection(Guid id) => _connections.TryRemove(id, out _);

    internal ValueTask EnqueueIncomingAsync(PipeMessage message, CancellationToken ct)
        => _incoming.Writer.WriteAsync(message, ct);

    private sealed class Connection : IAsyncDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();

        private readonly NamedPipeServerStream _pipe;
        private readonly NamedPipeServer _owner;
        private readonly ILogger _logger;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public Connection(NamedPipeServerStream pipe, NamedPipeServer owner, ILogger logger)
        {
            _pipe = pipe;
            _owner = owner;
            _logger = logger;
            _reader = new StreamReader(pipe);
            _writer = new StreamWriter(pipe) { AutoFlush = true, NewLine = "\n" };
        }

        public async Task ReadLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && _pipe.IsConnected)
                {
                    var line = await _reader.ReadLineAsync(ct);
                    if (line is null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var msg = PipeMessage.Deserialize(line);
                        await _owner.EnqueueIncomingAsync(msg, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Discarded malformed pipe message: {Line}", line);
                    }
                }
            }
            catch (OperationCanceledException) { /* shutdown */ }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Pipe connection {Id} read loop ended", Id);
            }
            finally
            {
                _owner.RemoveConnection(Id);
                await DisposeAsync();
            }
        }

        public async Task SendAsync(string line, CancellationToken ct)
        {
            if (!_pipe.IsConnected) return;
            await _writeLock.WaitAsync(ct);
            try
            {
                await _writer.WriteLineAsync(line.AsMemory(), ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to send on pipe {Id}; dropping connection", Id);
                _owner.RemoveConnection(Id);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            try { await _writer.DisposeAsync(); } catch { }
            try { _reader.Dispose(); } catch { }
            try { await _pipe.DisposeAsync(); } catch { }
            _writeLock.Dispose();
        }
    }
}
