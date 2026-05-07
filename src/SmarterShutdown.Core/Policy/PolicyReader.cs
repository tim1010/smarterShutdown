using System.Globalization;
using SmarterShutdown.Core.Models;

namespace SmarterShutdown.Core.Policy;

public sealed class PolicyReader
{
    public const string DefaultKeyPath = @"SOFTWARE\Policies\SmarterShutdown";

    private readonly IRegistryReader _registry;
    private readonly string _keyPath;

    public PolicyReader(IRegistryReader registry, string keyPath = DefaultKeyPath)
    {
        _registry = registry;
        _keyPath = keyPath;
    }

    public PolicySettings Read()
    {
        if (!_registry.KeyExists(_keyPath))
        {
            return new PolicySettings();
        }

        var defaults = new PolicySettings();

        return new PolicySettings
        {
            Enabled = ReadBool("Enabled", defaults.Enabled),
            ShutdownTime = ReadTime("ShutdownTime", defaults.ShutdownTime),
            ActiveDays = ReadActiveDays(),
            Action = ReadAction(defaults.Action),
            ForceShutdown = ReadBool("ForceShutdown", defaults.ForceShutdown),
            SkipIfOnBattery = ReadBool("SkipIfOnBattery", defaults.SkipIfOnBattery),
            WarningMinutes = ReadInt("WarningMinutes", defaults.WarningMinutes),
            WarningMessage = _registry.ReadString(_keyPath, "WarningMessage") ?? defaults.WarningMessage,
            AllowPostpone = ReadBool("AllowPostpone", defaults.AllowPostpone),
            PostponeMinutes = ReadInt("PostponeMinutes", defaults.PostponeMinutes),
            MaxPostpones = ReadInt("MaxPostpones", defaults.MaxPostpones),
            IdleThresholdMinutes = ReadInt("IdleThresholdMinutes", defaults.IdleThresholdMinutes),
            IdleWarningMinutes = ReadInt("IdleWarningMinutes", defaults.IdleWarningMinutes),
            TeamsDefer = ReadBool("TeamsDefer", defaults.TeamsDefer),
            TeamsDeferMaxMinutes = ReadInt("TeamsDeferMaxMinutes", defaults.TeamsDeferMaxMinutes),
            EventLogLevel = ReadEventLogLevel(defaults.EventLogLevel),
        };
    }

    private bool ReadBool(string valueName, bool fallback)
    {
        var v = _registry.ReadDword(_keyPath, valueName);
        return v.HasValue ? v.Value == 1 : fallback;
    }

    private int ReadInt(string valueName, int fallback)
        => _registry.ReadDword(_keyPath, valueName) ?? fallback;

    private TimeOnly ReadTime(string valueName, TimeOnly fallback)
    {
        var raw = _registry.ReadString(_keyPath, valueName);
        if (raw is null) return fallback;
        return TimeOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t
            : fallback;
    }

    private DayFlags ReadActiveDays()
    {
        var raw = _registry.ReadDword(_keyPath, "ActiveDays") ?? 0;
        // Mask to defined day bits (Sun..Sat = 0x7F) so unknown high bits never leak into the enum.
        return (DayFlags)(raw & (int)DayFlags.All);
    }

    private ShutdownAction ReadAction(ShutdownAction fallback)
    {
        var v = _registry.ReadDword(_keyPath, "ShutdownAction");
        return v switch
        {
            (int)ShutdownAction.Shutdown => ShutdownAction.Shutdown,
            (int)ShutdownAction.Hibernate => ShutdownAction.Hibernate,
            (int)ShutdownAction.Sleep => ShutdownAction.Sleep,
            _ => fallback,
        };
    }

    private EventLogLevel ReadEventLogLevel(EventLogLevel fallback)
    {
        var v = _registry.ReadDword(_keyPath, "EventLogLevel");
        return v switch
        {
            (int)EventLogLevel.Off => EventLogLevel.Off,
            (int)EventLogLevel.Errors => EventLogLevel.Errors,
            (int)EventLogLevel.Warnings => EventLogLevel.Warnings,
            (int)EventLogLevel.Info => EventLogLevel.Info,
            _ => fallback,
        };
    }
}
