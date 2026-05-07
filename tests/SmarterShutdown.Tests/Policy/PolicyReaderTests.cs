using Moq;
using SmarterShutdown.Core.Models;
using SmarterShutdown.Core.Policy;

namespace SmarterShutdown.Tests.Policy;

public class PolicyReaderTests
{
    private const string KeyPath = @"SOFTWARE\Policies\SmarterShutdown";

    private static Mock<IRegistryReader> KeyPresent()
    {
        var m = new Mock<IRegistryReader>();
        m.Setup(r => r.KeyExists(KeyPath)).Returns(true);
        return m;
    }

    private static PolicySettings ReadWith(Action<Mock<IRegistryReader>> stub)
    {
        var m = KeyPresent();
        stub(m);
        return new PolicyReader(m.Object).Read();
    }

    // ---- key missing ----

    [Fact]
    public void Read_WhenRootKeyMissing_ReturnsDisabledDefaults()
    {
        var registry = new Mock<IRegistryReader>();
        registry.Setup(r => r.KeyExists(KeyPath)).Returns(false);

        var settings = new PolicyReader(registry.Object).Read();

        Assert.False(settings.Enabled);
    }

    // ---- Enabled ----

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void Read_EnabledMapsFromDword(int raw, bool expected)
    {
        var settings = ReadWith(m => m.Setup(r => r.ReadDword(KeyPath, "Enabled")).Returns(raw));
        Assert.Equal(expected, settings.Enabled);
    }

    // ---- ShutdownTime ----

    [Fact]
    public void Read_ShutdownTime_ParsedFromString()
    {
        var settings = ReadWith(m => m.Setup(r => r.ReadString(KeyPath, "ShutdownTime")).Returns("07:30"));
        Assert.Equal(new TimeOnly(7, 30), settings.ShutdownTime);
    }

    [Fact]
    public void Read_ShutdownTime_DefaultsTo2200_WhenMissing()
    {
        var settings = ReadWith(_ => { });
        Assert.Equal(new TimeOnly(22, 0), settings.ShutdownTime);
    }

    [Fact]
    public void Read_ShutdownTime_DefaultsTo2200_WhenUnparseable()
    {
        var settings = ReadWith(m => m.Setup(r => r.ReadString(KeyPath, "ShutdownTime")).Returns("not-a-time"));
        Assert.Equal(new TimeOnly(22, 0), settings.ShutdownTime);
    }

    // ---- ActiveDays ----

    [Fact]
    public void Read_ActiveDays_MapsBitmaskToFlags()
    {
        // Mon|Tue|Wed|Thu|Fri = 2+4+8+16+32 = 62
        var settings = ReadWith(m => m.Setup(r => r.ReadDword(KeyPath, "ActiveDays")).Returns(62));
        Assert.Equal(
            DayFlags.Monday | DayFlags.Tuesday | DayFlags.Wednesday | DayFlags.Thursday | DayFlags.Friday,
            settings.ActiveDays);
    }

    [Fact]
    public void Read_ActiveDays_FullWeek()
    {
        var settings = ReadWith(m => m.Setup(r => r.ReadDword(KeyPath, "ActiveDays")).Returns(127));
        Assert.Equal(
            DayFlags.Sunday | DayFlags.Monday | DayFlags.Tuesday | DayFlags.Wednesday |
            DayFlags.Thursday | DayFlags.Friday | DayFlags.Saturday,
            settings.ActiveDays);
    }

    [Fact]
    public void Read_ActiveDays_DefaultsToNone()
    {
        var settings = ReadWith(_ => { });
        Assert.Equal(DayFlags.None, settings.ActiveDays);
    }

    // ---- ShutdownAction ----

    [Theory]
    [InlineData(0, ShutdownAction.Shutdown)]
    [InlineData(1, ShutdownAction.Hibernate)]
    [InlineData(2, ShutdownAction.Sleep)]
    public void Read_ShutdownAction_MapsFromDword(int raw, ShutdownAction expected)
    {
        var settings = ReadWith(m => m.Setup(r => r.ReadDword(KeyPath, "ShutdownAction")).Returns(raw));
        Assert.Equal(expected, settings.Action);
    }

    [Fact]
    public void Read_ShutdownAction_DefaultsToShutdown()
    {
        var settings = ReadWith(_ => { });
        Assert.Equal(ShutdownAction.Shutdown, settings.Action);
    }

    // ---- ForceShutdown ----

    [Fact]
    public void Read_ForceShutdown_FromDword()
    {
        var on = ReadWith(m => m.Setup(r => r.ReadDword(KeyPath, "ForceShutdown")).Returns(1));
        Assert.True(on.ForceShutdown);

        var missing = ReadWith(_ => { });
        Assert.False(missing.ForceShutdown);
    }

    // ---- SkipIfOnBattery ----

    [Fact]
    public void Read_SkipIfOnBattery_FromDword()
    {
        var on = ReadWith(m => m.Setup(r => r.ReadDword(KeyPath, "SkipIfOnBattery")).Returns(1));
        Assert.True(on.SkipIfOnBattery);

        var missing = ReadWith(_ => { });
        Assert.False(missing.SkipIfOnBattery);
    }

    // ---- WarningMinutes ----

    [Fact]
    public void Read_WarningMinutes_FromDword_AndDefault()
    {
        var explicitVal = ReadWith(m => m.Setup(r => r.ReadDword(KeyPath, "WarningMinutes")).Returns(30));
        Assert.Equal(30, explicitVal.WarningMinutes);

        var missing = ReadWith(_ => { });
        Assert.Equal(15, missing.WarningMinutes);
    }

    // ---- WarningMessage ----

    [Fact]
    public void Read_WarningMessage_FromString_AndDefault()
    {
        var explicitVal = ReadWith(m => m.Setup(r => r.ReadString(KeyPath, "WarningMessage")).Returns("Save your work"));
        Assert.Equal("Save your work", explicitVal.WarningMessage);

        var missing = ReadWith(_ => { });
        Assert.Equal("", missing.WarningMessage);
    }

    // ---- AllowPostpone ----

    [Fact]
    public void Read_AllowPostpone_FromDword_AndDefault()
    {
        var off = ReadWith(m => m.Setup(r => r.ReadDword(KeyPath, "AllowPostpone")).Returns(0));
        Assert.False(off.AllowPostpone);

        var on = ReadWith(m => m.Setup(r => r.ReadDword(KeyPath, "AllowPostpone")).Returns(1));
        Assert.True(on.AllowPostpone);

        var missing = ReadWith(_ => { });
        Assert.True(missing.AllowPostpone);
    }

    // ---- PostponeMinutes ----

    [Fact]
    public void Read_PostponeMinutes_FromDword_AndDefault()
    {
        var explicitVal = ReadWith(m => m.Setup(r => r.ReadDword(KeyPath, "PostponeMinutes")).Returns(10));
        Assert.Equal(10, explicitVal.PostponeMinutes);

        var missing = ReadWith(_ => { });
        Assert.Equal(15, missing.PostponeMinutes);
    }

    // ---- MaxPostpones ----

    [Fact]
    public void Read_MaxPostpones_FromDword_AndDefault()
    {
        var explicitVal = ReadWith(m => m.Setup(r => r.ReadDword(KeyPath, "MaxPostpones")).Returns(5));
        Assert.Equal(5, explicitVal.MaxPostpones);

        var missing = ReadWith(_ => { });
        Assert.Equal(3, missing.MaxPostpones);
    }

    // ---- IdleThresholdMinutes (spec default 30) ----

    [Fact]
    public void Read_IdleThresholdMinutes_FromDword_AndDefault30()
    {
        var explicitVal = ReadWith(m => m.Setup(r => r.ReadDword(KeyPath, "IdleThresholdMinutes")).Returns(60));
        Assert.Equal(60, explicitVal.IdleThresholdMinutes);

        var missing = ReadWith(_ => { });
        Assert.Equal(30, missing.IdleThresholdMinutes);
    }

    // ---- IdleWarningMinutes (spec default 2) ----

    [Fact]
    public void Read_IdleWarningMinutes_FromDword_AndDefault2()
    {
        var explicitVal = ReadWith(m => m.Setup(r => r.ReadDword(KeyPath, "IdleWarningMinutes")).Returns(5));
        Assert.Equal(5, explicitVal.IdleWarningMinutes);

        var missing = ReadWith(_ => { });
        Assert.Equal(2, missing.IdleWarningMinutes);
    }

    // ---- TeamsDefer ----

    [Fact]
    public void Read_TeamsDefer_FromDword_AndDefaultTrue()
    {
        var off = ReadWith(m => m.Setup(r => r.ReadDword(KeyPath, "TeamsDefer")).Returns(0));
        Assert.False(off.TeamsDefer);

        var missing = ReadWith(_ => { });
        Assert.True(missing.TeamsDefer);
    }

    // ---- TeamsDeferMaxMinutes (spec default 60) ----

    [Fact]
    public void Read_TeamsDeferMaxMinutes_FromDword_AndDefault60()
    {
        var explicitVal = ReadWith(m => m.Setup(r => r.ReadDword(KeyPath, "TeamsDeferMaxMinutes")).Returns(90));
        Assert.Equal(90, explicitVal.TeamsDeferMaxMinutes);

        var missing = ReadWith(_ => { });
        Assert.Equal(60, missing.TeamsDeferMaxMinutes);
    }

    // ---- EventLogLevel (spec default Info=3) ----

    [Theory]
    [InlineData(0, EventLogLevel.Off)]
    [InlineData(1, EventLogLevel.Errors)]
    [InlineData(2, EventLogLevel.Warnings)]
    [InlineData(3, EventLogLevel.Info)]
    public void Read_EventLogLevel_MapsFromDword(int raw, EventLogLevel expected)
    {
        var settings = ReadWith(m => m.Setup(r => r.ReadDword(KeyPath, "EventLogLevel")).Returns(raw));
        Assert.Equal(expected, settings.EventLogLevel);
    }

    [Fact]
    public void Read_EventLogLevel_DefaultsToInfo()
    {
        var settings = ReadWith(_ => { });
        Assert.Equal(EventLogLevel.Info, settings.EventLogLevel);
    }
}
