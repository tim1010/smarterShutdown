using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmarterShutdown.Core.IPC;

public sealed record PipeMessage
{
    public MessageType Type { get; init; }
    public DateTime? ScheduledAt { get; init; }
    public bool? TeamsCallActive { get; init; }
    public int? PostponesRemaining { get; init; }
    public int? MaxPostpones { get; init; }
    public bool? PostponeAllowed { get; init; }
    public int? IdleMinutes { get; init; }
    public string? UserName { get; init; }
    public string? Message { get; init; }
    public string? Reason { get; init; }

    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    public static PipeMessage Deserialize(string json) =>
        JsonSerializer.Deserialize<PipeMessage>(json, JsonOptions)
            ?? throw new InvalidOperationException("Pipe message JSON deserialized to null.");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
