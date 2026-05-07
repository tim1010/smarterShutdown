using Microsoft.Extensions.Logging;

namespace SmarterShutdown.Service;

internal static class EventIds
{
    public static readonly EventId ServiceStarted = new(1000, nameof(ServiceStarted));
    public static readonly EventId ActionExecuted = new(1001, nameof(ActionExecuted));
    public static readonly EventId ActionPostponed = new(1002, nameof(ActionPostponed));
    public static readonly EventId ActionDeferredTeams = new(1003, nameof(ActionDeferredTeams));
    public static readonly EventId PolicyRefreshed = new(1004, nameof(PolicyRefreshed));
    public static readonly EventId TeamsDeferLimitReached = new(1005, nameof(TeamsDeferLimitReached));
    public static readonly EventId SkippedOnBattery = new(1006, nameof(SkippedOnBattery));
    public static readonly EventId NoActiveDays = new(1007, nameof(NoActiveDays));
    public static readonly EventId RegistryReadFailed = new(1008, nameof(RegistryReadFailed));
    public static readonly EventId ShutdownFailed = new(1009, nameof(ShutdownFailed));
    public static readonly EventId SuspendedByAdmin = new(1010, nameof(SuspendedByAdmin));
    public static readonly EventId WarningBroadcast = new(1011, nameof(WarningBroadcast));
}
