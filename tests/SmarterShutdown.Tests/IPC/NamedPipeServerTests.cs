using System.IO.Pipes;
using Microsoft.Extensions.Logging.Abstractions;
using SmarterShutdown.Core.IPC;

namespace SmarterShutdown.Tests.IPC;

public class NamedPipeServerTests
{
    // Each test uses a unique pipe name to avoid clashes when run in parallel.
    private static string PipeName() => $"SmarterShutdown.Test.{Guid.NewGuid():N}";

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ClientToServer_MessageRoundTripsToIncoming()
    {
        var pipeName = PipeName();
        await using var server = new NamedPipeServer(NullLogger<NamedPipeServer>.Instance, pipeName);
        using var cts = new CancellationTokenSource(TestTimeout);
        server.Start(cts.Token);

        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(cts.Token);

        var sent = new PipeMessage
        {
            Type = MessageType.PostponeRequest,
        };

        await using (var writer = new StreamWriter(client) { AutoFlush = true, NewLine = "\n" })
        {
            await writer.WriteLineAsync(sent.Serialize());

            var received = await server.Incoming.ReadAsync(cts.Token);
            Assert.Equal(MessageType.PostponeRequest, received.Type);
        }
    }

    [Fact]
    public async Task ServerToClient_BroadcastReachesConnectedClient()
    {
        var pipeName = PipeName();
        await using var server = new NamedPipeServer(NullLogger<NamedPipeServer>.Instance, pipeName);
        using var cts = new CancellationTokenSource(TestTimeout);
        server.Start(cts.Token);

        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(cts.Token);
        using var reader = new StreamReader(client);

        // Give the server's read loop a moment to register the connection before broadcasting.
        // Without this, the broadcast can race the connection registration and silently drop.
        await WaitForConnectionRegistration(server, cts.Token);

        var sent = new PipeMessage
        {
            Type = MessageType.ShutdownPending,
            ScheduledAt = new DateTime(2026, 5, 4, 22, 0, 0, DateTimeKind.Local),
            PostponesRemaining = 2,
        };
        await server.BroadcastAsync(sent, cts.Token);

        var line = await reader.ReadLineAsync(cts.Token);
        Assert.NotNull(line);
        var received = PipeMessage.Deserialize(line!);
        Assert.Equal(MessageType.ShutdownPending, received.Type);
        Assert.Equal(sent.ScheduledAt, received.ScheduledAt);
        Assert.Equal(2, received.PostponesRemaining);
    }

    [Fact]
    public async Task MultipleClients_EachReceiveBroadcast()
    {
        var pipeName = PipeName();
        await using var server = new NamedPipeServer(NullLogger<NamedPipeServer>.Instance, pipeName);
        using var cts = new CancellationTokenSource(TestTimeout);
        server.Start(cts.Token);

        await using var c1 = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await using var c2 = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await c1.ConnectAsync(cts.Token);
        await c2.ConnectAsync(cts.Token);

        using var r1 = new StreamReader(c1);
        using var r2 = new StreamReader(c2);

        await WaitForConnectionRegistration(server, cts.Token, expected: 2);

        await server.BroadcastAsync(new PipeMessage { Type = MessageType.PolicyRefreshed }, cts.Token);

        var line1 = await r1.ReadLineAsync(cts.Token);
        var line2 = await r2.ReadLineAsync(cts.Token);

        Assert.Equal(MessageType.PolicyRefreshed, PipeMessage.Deserialize(line1!).Type);
        Assert.Equal(MessageType.PolicyRefreshed, PipeMessage.Deserialize(line2!).Type);
    }

    [Fact]
    public async Task MalformedJson_DoesNotKillTheServer_AndSubsequentMessageStillArrives()
    {
        var pipeName = PipeName();
        await using var server = new NamedPipeServer(NullLogger<NamedPipeServer>.Instance, pipeName);
        using var cts = new CancellationTokenSource(TestTimeout);
        server.Start(cts.Token);

        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(cts.Token);

        await using var writer = new StreamWriter(client) { AutoFlush = true, NewLine = "\n" };
        await writer.WriteLineAsync("{ this is : not :: valid json");

        var valid = new PipeMessage { Type = MessageType.SuspendRequest };
        await writer.WriteLineAsync(valid.Serialize());

        var received = await server.Incoming.ReadAsync(cts.Token);
        Assert.Equal(MessageType.SuspendRequest, received.Type);
    }

    // The server registers a connection a beat after WaitForConnectionAsync returns; tests can
    // race that with broadcasting and miss the message. A small delay closes the gap reliably
    // and the 5s test timeout still catches genuine hangs.
    private static Task WaitForConnectionRegistration(NamedPipeServer server, CancellationToken ct, int expected = 1)
        => Task.Delay(TimeSpan.FromMilliseconds(150 * expected), ct);
}
