namespace SmarterShutdown.Core.Scheduler;

public enum TeamsDeferralDecision
{
    /// <summary>No active call (or call ended) — caller should fire the action.</summary>
    Proceed,

    /// <summary>First tick of a new deferral — caller logs Event 1003 then waits.</summary>
    DeferStarting,

    /// <summary>Continuing deferral — caller waits without re-logging.</summary>
    Defer,

    /// <summary>Hit the configured cap — caller logs Event 1005 then fires the action.</summary>
    MaxReached,
}

public sealed class TeamsDeferralPolicy
{
    private DateTime? _deferStartedAt;
    private bool _logged;

    public TeamsDeferralDecision Evaluate(DateTime now, bool teamsActive, int maxMinutes)
    {
        if (!teamsActive)
        {
            Reset();
            return TeamsDeferralDecision.Proceed;
        }

        _deferStartedAt ??= now;
        var elapsed = now - _deferStartedAt.Value;
        if (elapsed >= TimeSpan.FromMinutes(maxMinutes))
        {
            return TeamsDeferralDecision.MaxReached;
        }

        if (!_logged)
        {
            _logged = true;
            return TeamsDeferralDecision.DeferStarting;
        }
        return TeamsDeferralDecision.Defer;
    }

    public void Reset()
    {
        _deferStartedAt = null;
        _logged = false;
    }
}
