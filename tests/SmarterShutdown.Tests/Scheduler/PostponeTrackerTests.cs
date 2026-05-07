using SmarterShutdown.Core.Scheduler;

namespace SmarterShutdown.Tests.Scheduler;

public class PostponeTrackerTests
{
    [Fact]
    public void NewTracker_HasZeroPostpones()
    {
        var t = new PostponeTracker();
        Assert.Equal(0, t.Count);
    }

    [Fact]
    public void TryPostpone_UnderLimit_ReturnsTrueAndIncrements()
    {
        var t = new PostponeTracker();
        Assert.True(t.TryPostpone(maxPostpones: 3));
        Assert.Equal(1, t.Count);
    }

    [Fact]
    public void TryPostpone_AtLimit_ReturnsFalse()
    {
        var t = new PostponeTracker();
        Assert.True(t.TryPostpone(3));
        Assert.True(t.TryPostpone(3));
        Assert.True(t.TryPostpone(3));
        Assert.False(t.TryPostpone(3));
        Assert.Equal(3, t.Count);
    }

    [Fact]
    public void TryPostpone_MaxZeroMeansUnlimited()
    {
        // Spec: MaxPostpones = 0 means unlimited.
        var t = new PostponeTracker();
        for (int i = 0; i < 100; i++)
        {
            Assert.True(t.TryPostpone(maxPostpones: 0));
        }
        Assert.Equal(100, t.Count);
    }

    [Fact]
    public void Reset_ZeroesCount()
    {
        var t = new PostponeTracker();
        t.TryPostpone(3);
        t.TryPostpone(3);
        t.Reset();
        Assert.Equal(0, t.Count);
        Assert.True(t.TryPostpone(3));
    }
}
