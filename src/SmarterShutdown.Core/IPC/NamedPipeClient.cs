using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace SmarterShutdown.Core.IPC;

[SupportedOSPlatform("windows")]
public sealed class NamedPipeClient : IPipeClient
{
    public const string DefaultPipeName = NamedPipeServer.DefaultPipeName;
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    private readonly string _serverName;
    private readonly string _pipeName;
    private readonly ILogger<NamedPipeClient> _logger;
    private readonly Channel<PipeMessage> _incoming = Channel.CreateUnbounded<PipeMessage>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile StreamWriter? _writer;
    private volatile NamedPipeClientStream? _pipe;

    public NamedPipeClient(ILogger<NamedPipeClient> logger, string pipeName = DefaultPipeName, string serverName = ".")
    {
        _logger = logger;
        _pipeName = pipeName;
        _serverName = serverName;
    }

    public ChannelReader<PipeMessage> Incoming => _incoming.Reader;

    public Task StartAsync(CancellationToken ct)
    {
        if (_loop is not null) return Task.CompletedTask;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = Task.Run(() => RunAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public async Task SendAsync(PipeMessage message, CancellationToken ct)
    {
        var writer = _writer;
        var pipe = _pipe;
        if (writer is null || pipe is null || !pipe.IsConnected) return;

        await _writeLock.WaitAsync(ct);
        try
        {
            await writer.WriteLineAsync(message.Serialize().AsMemory(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Pipe send failed; will reconnect on next loop iteration");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_loop is not null)
        {
            try { await _loop; } catch { /* shutdown */ }
        }
        _cts?.Dispose();
        _writeLock.Dispose();
        _incoming.Writer.TryComplete();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeClientStream? pipe = null;
            try
            {
                pipe = new NamedPipeClientStream(_serverName, _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(ct);

                using var reader = new StreamReader(pipe);
                var writer = new StreamWriter(pipe) { AutoFlush = true, NewLine = "\n" };
                _pipe = pipe;
                _writer = writer;

                while (!ct.IsCancellationRequested && pipe.IsConnected)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line is null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        await _incoming.Writer.WriteAsync(PipeMessage.Deserialize(line), ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Discarded malformed pipe message: {Line}", line);
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Pipe client connection ended; reconnecting");
            }
            finally
            {
                _writer = null;
                _pipe = null;
                pipe?.Dispose();
            }

            if (ct.IsCancellationRequested) break;
            try { await Task.Delay(ReconnectDelay, ct); }
            catch (OperationCanceledException) { break; }
        }

        _incoming.Writer.TryComplete();
    }
}
