using SmarterShutdown.Core.Scheduler;

namespace SmarterShutdown.Tests.Scheduler;

public class TeamsDeferralPolicyTests
{
    private static readonly DateTime Base = new(2026, 5, 4, 22, 0, 0, DateTimeKind.Local);
    private static DateTime At(int minutesFromBase) => Base.AddMinutes(minutesFromBase);

    [Fact]
    public void TeamsNotActive_ReturnsProceed()
    {
        var p = new TeamsDeferralPolicy();
        Assert.Equal(TeamsDeferralDecision.Proceed, p.Evaluate(At(0), teamsActive: false, maxMinutes: 60));
    }

    [Fact]
    public void FirstActiveCall_ReturnsDeferStarting()
    {
        var p = new TeamsDeferralPolicy();
        Assert.Equal(TeamsDeferralDecision.DeferStarting, p.Evaluate(At(0), teamsActive: true, maxMinutes: 60));
    }

    [Fact]
    public void SubsequentActiveCalls_ReturnDefer_NotDeferStarting()
    {
        // Caller logs Event 1003 only on DeferStarting; subsequent ticks should suppress it.
        var p = new TeamsDeferralPolicy();
        Assert.Equal(TeamsDeferralDecision.DeferStarting, p.Evaluate(At(0), true, 60));
        Assert.Equal(TeamsDeferralDecision.Defer, p.Evaluate(At(2), true, 60));
        Assert.Equal(TeamsDeferralDecision.Defer, p.Evaluate(At(10), true, 60));
    }

    [Fact]
    public void AfterMaxMinutes_ReturnsMaxReached()
    {
        var p = new TeamsDeferralPolicy();
        Assert.Equal(TeamsDeferralDecision.DeferStarting, p.Evaluate(At(0), true, 60));
        Assert.Equal(TeamsDeferralDecision.Defer, p.Evaluate(At(30), true, 60));
        Assert.Equal(TeamsDeferralDecision.MaxReached, p.Evaluate(At(60), true, 60));
    }

    [Fact]
    public void TeamsCallEnding_ReturnsProceed_AndResetsDeferralState()
    {
        var p = new TeamsDeferralPolicy();
        p.Evaluate(At(0), true, 60);   // start
        p.Evaluate(At(10), true, 60);  // continue

        // Call ends — should proceed.
        Assert.Equal(TeamsDeferralDecision.Proceed, p.Evaluate(At(15), false, 60));

        // New call later — should DeferStarting again, not Defer (state reset).
        Assert.Equal(TeamsDeferralDecision.DeferStarting, p.Evaluate(At(20), true, 60));
    }

    [Fact]
    public void Reset_ClearsState_ForNextSlot()
    {
        var p = new TeamsDeferralPolicy();
        p.Evaluate(At(0), true, 60);
        p.Reset();
        Assert.Equal(TeamsDeferralDecision.DeferStarting, p.Evaluate(At(5), true, 60));
    }
}
