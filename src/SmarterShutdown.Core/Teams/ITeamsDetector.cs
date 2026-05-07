namespace SmarterShutdown.Core.Teams;

public interface ITeamsDetector
{
    /// <summary>True when an active Teams call is in progress in the current user session.</summary>
    bool IsInCall();
}
