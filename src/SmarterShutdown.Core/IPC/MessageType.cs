namespace SmarterShutdown.Core.IPC;

public enum MessageType
{
    None = 0,
    ShutdownPending = 1,
    ShutdownPendingIdle = 2,
    PostponeRequest = 3,
    PostponeAck = 4,
    SuspendRequest = 5,
    SuspendAck = 6,
    DeferredTeams = 7,
    TeamsCallStatus = 8,
    PolicyRefreshed = 9,
}
