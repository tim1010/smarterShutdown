namespace SmarterShutdown.Core.Models;

public sealed record PolicySettings
{
    public bool Enabled { get; init; }
    public TimeOnly ShutdownTime { get; init; } = new(22, 0);
    public DayFlags ActiveDays { get; init; } = DayFlags.None;
    public ShutdownAction Action { get; init; } = ShutdownAction.Shutdown;
    public bool ForceShutdown { get; init; }
    public bool SkipIfOnBattery { get; init; }
    public int WarningMinutes { get; init; } = 15;
    public string WarningMessage { get; init; } = "";
    public bool AllowPostpone { get; init; } = true;
    public int PostponeMinutes { get; init; } = 15;
    public int MaxPostpones { get; init; } = 3;
    public int IdleThresholdMinutes { get; init; } = 30;
    public int IdleWarningMinutes { get; init; } = 2;
    public bool TeamsDefer { get; init; } = true;
    public int TeamsDeferMaxMinutes { get; init; } = 60;
    public EventLogLevel EventLogLevel { get; init; } = EventLogLevel.Info;
}
