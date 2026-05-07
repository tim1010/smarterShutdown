using SmarterShutdown.Core.IPC;

namespace SmarterShutdown.Tests.IPC;

public class PipeMessageTests
{
    [Fact]
    public void RoundTrip_ShutdownPending_PreservesAllFields()
    {
        var original = new PipeMessage
        {
            Type = MessageType.ShutdownPending,
            ScheduledAt = new DateTime(2026, 5, 4, 22, 0, 0, DateTimeKind.Local),
            PostponesRemaining = 2,
            MaxPostpones = 3,
        };

        var json = original.Serialize();
        var restored = PipeMessage.Deserialize(json);

        Assert.Equal(original, restored);
    }

    [Fact]
    public void RoundTrip_PostponeAck_PreservesNewScheduledTime()
    {
        var original = new PipeMessage
        {
            Type = MessageType.PostponeAck,
            ScheduledAt = new DateTime(2026, 5, 4, 22, 15, 0, DateTimeKind.Local),
        };

        var restored = PipeMessage.Deserialize(original.Serialize());

        Assert.Equal(MessageType.PostponeAck, restored.Type);
        Assert.Equal(original.ScheduledAt, restored.ScheduledAt);
    }

    [Fact]
    public void RoundTrip_TeamsCallStatus_PreservesBoolPayload()
    {
        var original = new PipeMessage
        {
            Type = MessageType.TeamsCallStatus,
            TeamsCallActive = true,
        };

        var restored = PipeMessage.Deserialize(original.Serialize());

        Assert.Equal(MessageType.TeamsCallStatus, restored.Type);
        Assert.True(restored.TeamsCallActive);
    }

    [Fact]
    public void RoundTrip_NoPayloadMessage()
    {
        var original = new PipeMessage { Type = MessageType.PolicyRefreshed };
        var restored = PipeMessage.Deserialize(original.Serialize());
        Assert.Equal(MessageType.PolicyRefreshed, restored.Type);
        Assert.Null(restored.ScheduledAt);
        Assert.Null(restored.TeamsCallActive);
    }

    [Fact]
    public void Serialize_OmitsNullFields()
    {
        var msg = new PipeMessage { Type = MessageType.PolicyRefreshed };
        var json = msg.Serialize();
        // Forward-compat: keep wire size small and tolerate added fields.
        Assert.DoesNotContain("scheduledAt", json);
        Assert.DoesNotContain("teamsCallActive", json);
        Assert.DoesNotContain("postponesRemaining", json);
        Assert.DoesNotContain("reason", json);
    }

    [Fact]
    public void Deserialize_IgnoresUnknownFields()
    {
        // A future version of the protocol adds new fields — older readers must not crash.
        var json = """
            {
              "type": 9,
              "futureField": "someValue",
              "anotherFuture": 42
            }
            """;
        var msg = PipeMessage.Deserialize(json);
        Assert.Equal(MessageType.PolicyRefreshed, msg.Type);
    }

    [Fact]
    public void Deserialize_RejectsNullJson()
    {
        Assert.Throws<InvalidOperationException>(() => PipeMessage.Deserialize("null"));
    }

    [Theory]
    [InlineData(MessageType.ShutdownPending)]
    [InlineData(MessageType.ShutdownPendingIdle)]
    [InlineData(MessageType.PostponeRequest)]
    [InlineData(MessageType.PostponeAck)]
    [InlineData(MessageType.SuspendRequest)]
    [InlineData(MessageType.SuspendAck)]
    [InlineData(MessageType.DeferredTeams)]
    [InlineData(MessageType.TeamsCallStatus)]
    [InlineData(MessageType.PolicyRefreshed)]
    public void RoundTrip_AllMessageTypes_PreserveType(MessageType type)
    {
        var msg = new PipeMessage { Type = type };
        var restored = PipeMessage.Deserialize(msg.Serialize());
        Assert.Equal(type, restored.Type);
    }
}
