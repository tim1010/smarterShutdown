namespace SmarterShutdown.Core.Power;

public interface IBatteryStatus
{
    /// <summary>True when the device is running on battery (not plugged in).</summary>
    bool IsOnBattery();
}
