using SmarterShutdown.Core.Models;
using SmarterShutdown.Core.Scheduler;

namespace SmarterShutdown.Tests.Scheduler;

public class SchedulerEngineTests
{
    private static PolicySettings BasicEnabled(
        DayFlags activeDays = DayFlags.All,
        int hour = 22,
        int minute = 0)
        => new()
        {
            Enabled = true,
            ActiveDays = activeDays,
            ShutdownTime = new TimeOnly(hour, minute),
        };

    // 2026-05-04 is a Monday — used as a fixed anchor through these tests.
    private static DateTime On(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Local);

    [Fact]
    public void GetNextAction_WhenDisabled_ReturnsNull()
    {
        var policy = BasicEnabled() with { Enabled = false };
        var result = new SchedulerEngine().GetNextAction(policy, On(2026, 5, 4, 10, 0));
        Assert.Null(result);
    }

    [Fact]
    public void GetNextAction_WhenNoActiveDays_ReturnsNull()
    {
        var policy = BasicEnabled(activeDays: DayFlags.None);
        var result = new SchedulerEngine().GetNextAction(policy, On(2026, 5, 4, 10, 0));
        Assert.Null(result);
    }

    [Fact]
    public void GetNextAction_WhenTodayActiveAndBeforeTime_ReturnsToday()
    {
        // Mon 10:00, ActiveDays=Weekdays, ShutdownTime=22:00 → Mon 22:00
        var policy = BasicEnabled(DayFlags.Weekdays);
        var result = new SchedulerEngine().GetNextAction(policy, On(2026, 5, 4, 10, 0));
        Assert.Equal(On(2026, 5, 4, 22, 0), result);
    }

    [Fact]
    public void GetNextAction_WhenTodayActiveButAfterTime_ReturnsTomorrow()
    {
        // Mon 23:00 → Tue 22:00
        var policy = BasicEnabled(DayFlags.Weekdays);
        var result = new SchedulerEngine().GetNextAction(policy, On(2026, 5, 4, 23, 0));
        Assert.Equal(On(2026, 5, 5, 22, 0), result);
    }

    [Fact]
    public void GetNextAction_AtExactlyScheduledTime_ReturnsNextActiveDay()
    {
        // Mon 22:00:00 exactly → equality is "past", schedule next active day
        var policy = BasicEnabled(DayFlags.Weekdays);
        var result = new SchedulerEngine().GetNextAction(policy, On(2026, 5, 4, 22, 0));
        Assert.Equal(On(2026, 5, 5, 22, 0), result);
    }

    [Fact]
    public void GetNextAction_OneSecondBeforeScheduledTime_ReturnsToday()
    {
        var policy = BasicEnabled(DayFlags.Weekdays);
        var now = new DateTime(2026, 5, 4, 21, 59, 59, DateTimeKind.Local);
        var result = new SchedulerEngine().GetNextAction(policy, now);
        Assert.Equal(On(2026, 5, 4, 22, 0), result);
    }

    [Fact]
    public void GetNextAction_TodayNotActive_FindsNextActiveDay()
    {
        // Sat 12:00, ActiveDays=Weekdays → Mon 22:00
        var policy = BasicEnabled(DayFlags.Weekdays);
        var result = new SchedulerEngine().GetNextAction(policy, On(2026, 5, 9, 12, 0));
        Assert.Equal(On(2026, 5, 11, 22, 0), result);
    }

    [Fact]
    public void GetNextAction_FridayLate_SkipsWeekendForWeekdaysOnly()
    {
        // Fri 23:00 → Mon 22:00 (skip Sat & Sun)
        var policy = BasicEnabled(DayFlags.Weekdays);
        var result = new SchedulerEngine().GetNextAction(policy, On(2026, 5, 8, 23, 0));
        Assert.Equal(On(2026, 5, 11, 22, 0), result);
    }

    [Fact]
    public void GetNextAction_SingleDayActive_AfterToday_ReturnsNextWeek()
    {
        // ActiveDays=Sunday only, now=Sun 22:00 (exact) → next Sunday
        var policy = BasicEnabled(DayFlags.Sunday);
        var result = new SchedulerEngine().GetNextAction(policy, On(2026, 5, 3, 22, 0));
        Assert.Equal(On(2026, 5, 10, 22, 0), result);
    }

    [Fact]
    public void GetNextAction_SingleDayActive_TodayBeforeTime()
    {
        // ActiveDays=Sunday only, now=Sun 21:00 → today 22:00
        var policy = BasicEnabled(DayFlags.Sunday);
        var result = new SchedulerEngine().GetNextAction(policy, On(2026, 5, 3, 21, 0));
        Assert.Equal(On(2026, 5, 3, 22, 0), result);
    }

    [Fact]
    public void GetNextAction_PreservesDateTimeKindFromNow()
    {
        var policy = BasicEnabled(DayFlags.Weekdays);
        var nowUtc = new DateTime(2026, 5, 4, 10, 0, 0, DateTimeKind.Utc);
        var result = new SchedulerEngine().GetNextAction(policy, nowUtc);
        Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);
    }

    [Theory]
    [InlineData(DayOfWeek.Sunday, DayFlags.Sunday)]
    [InlineData(DayOfWeek.Monday, DayFlags.Monday)]
    [InlineData(DayOfWeek.Tuesday, DayFlags.Tuesday)]
    [InlineData(DayOfWeek.Wednesday, DayFlags.Wednesday)]
    [InlineData(DayOfWeek.Thursday, DayFlags.Thursday)]
    [InlineData(DayOfWeek.Friday, DayFlags.Friday)]
    [InlineData(DayOfWeek.Saturday, DayFlags.Saturday)]
    public void DayFlagsBitmask_MatchesDotNetDayOfWeek(DayOfWeek dow, DayFlags expectedFlag)
    {
        // Confirms the Sun=1..Sat=64 mapping in DayFlags lines up with System.DayOfWeek (Sun=0..Sat=6).
        var actualFlag = (DayFlags)(1 << (int)dow);
        Assert.Equal(expectedFlag, actualFlag);
    }
}
