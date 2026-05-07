using SmarterShutdown.Core.Models;

namespace SmarterShutdown.Core.Scheduler;

public sealed class SchedulerEngine
{
    public DateTime? GetNextAction(PolicySettings policy, DateTime now)
    {
        if (!policy.Enabled) return null;
        if (policy.ActiveDays == DayFlags.None) return null;

        for (int offset = 0; offset < 8; offset++)
        {
            var date = now.Date.AddDays(offset);
            var candidate = new DateTime(
                date.Year, date.Month, date.Day,
                policy.ShutdownTime.Hour, policy.ShutdownTime.Minute, 0,
                now.Kind);

            if (IsActive(policy.ActiveDays, candidate.DayOfWeek) && candidate > now)
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsActive(DayFlags flags, DayOfWeek dow) =>
        (flags & (DayFlags)(1 << (int)dow)) != 0;
}
