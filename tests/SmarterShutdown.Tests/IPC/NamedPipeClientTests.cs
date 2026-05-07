using Microsoft.Extensions.Logging.Abstractions;
using SmarterShutdown.Core.IPC;

namespace SmarterShutdown.Tests.IPC;

public class NamedPipeClientTests
{
    private static string PipeName() => $"SmarterShutdown.Test.{Guid.NewGuid():N}";

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Client_RoundTripsBothDirections()
    {
        var pipeName = PipeName();
        using var cts = new CancellationTokenSource(TestTimeout);

        await using var server = new NamedPipeServer(NullLogger<NamedPipeServer>.Instance, pipeName);
        server.Start(cts.Token);

        await using var client = new NamedPipeClient(NullLogger<NamedPipeClient>.Instance, pipeName);
        await client.StartAsync(cts.Token);

        // Wait for client→server connection to settle.
        await Task.Delay(200, cts.Token);

        // Client → server
        await client.SendAsync(new PipeMessage { Type = MessageType.PostponeRequest }, cts.Token);
        var serverGot = await server.Incoming.ReadAsync(cts.Token);
        Assert.Equal(MessageType.PostponeRequest, serverGot.Type);

        // Server → client
        var scheduledAt = new DateTime(2026, 5, 4, 22, 0, 0, DateTimeKind.Local);
        await server.BroadcastAsync(
            new PipeMessage { Type = MessageType.PostponeAck, ScheduledAt = scheduledAt },
            cts.Token);

        var clientGot = await client.Incoming.ReadAsync(cts.Token);
        Assert.Equal(MessageType.PostponeAck, clientGot.Type);
        Assert.Equal(scheduledAt, clientGot.ScheduledAt);
    }

    [Fact]
    public async Task Client_ReconnectsAfterServerDropsConnection()
    {
        var pipeName = PipeName();
        using var cts = new CancellationTokenSource(TestTimeout);

        await using var client = new NamedPipeClient(NullLogger<NamedPipeClient>.Instance, pipeName);
        await client.StartAsync(cts.Token);

        // First server lifetime.
        await using (var server1 = new NamedPipeServer(NullLogger<NamedPipeServer>.Instance, pipeName))
        {
            server1.Start(cts.Token);
            await Task.Delay(200, cts.Token);
            await server1.BroadcastAsync(new PipeMessage { Type = MessageType.PolicyRefreshed }, cts.Token);
            var first = await client.Incoming.ReadAsync(cts.Token);
            Assert.Equal(MessageType.PolicyRefreshed, first.Type);
        }
        // Server disposed — client must reconnect on its own.

        // Second server lifetime, same pipe name.
        await using (var server2 = new NamedPipeServer(NullLogger<NamedPipeServer>.Instance, pipeName))
        {
            server2.Start(cts.Token);
            // Reconnect delay is 1s; give the client up to 3s to reconnect, then send.
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(100, cts.Token);
                await server2.BroadcastAsync(new PipeMessage { Type = MessageType.SuspendAck }, cts.Token);
                if (client.Incoming.TryRead(out var msg))
                {
                    Assert.Equal(MessageType.SuspendAck, msg.Type);
                    return;
                }
            }
            Assert.Fail("Client did not reconnect to second server within timeout");
        }
    }
}
