using SmarterShutdown.Core.Idle;

namespace SmarterShutdown.Tests.Idle;

public class IdleTimeCalculatorTests
{
    [Fact]
    public void Compute_WhenSameTick_ReturnsZero()
    {
        Assert.Equal(TimeSpan.Zero, IdleTimeCalculator.Compute(1000, 1000));
    }

    [Fact]
    public void Compute_NormalCase_ReturnsDelta()
    {
        // 10s elapsed
        Assert.Equal(TimeSpan.FromMilliseconds(10000), IdleTimeCalculator.Compute(15000, 5000));
    }

    [Fact]
    public void Compute_AcrossTickWrap_HandlesUnsignedRollover()
    {
        // Tick counts are uint and wrap every ~49.7 days.
        // If lastInput happened just before wrap and 'now' is just after, unsigned subtraction
        // must still yield the small positive delta — not a near-49-day idle reading.
        const uint nearMax = uint.MaxValue - 100; // 100ms before wrap
        const uint afterWrap = 50;                // 50ms after wrap
        // Real elapsed = 100 + 1 + 50 = 151ms (the +1 accounts for the wrap point itself).
        var elapsed = IdleTimeCalculator.Compute(afterWrap, nearMax);
        Assert.Equal(TimeSpan.FromMilliseconds(151), elapsed);
    }

    [Fact]
    public void Compute_ConvertsCleanlyToMinutes()
    {
        var idle = IdleTimeCalculator.Compute(currentTick: 120_000, lastInputTick: 0);
        Assert.Equal(2.0, idle.TotalMinutes);
    }
}
