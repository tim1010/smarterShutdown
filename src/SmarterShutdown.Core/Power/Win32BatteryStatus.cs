using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SmarterShutdown.Core.Power;

// Boundary adapter — no unit tests. Returns true when ACLineStatus reports "Offline" (1 = AC connected, 0 = on battery).
// Devices without a battery report ACLineStatus=1 and BatteryFlag=128 ("No system battery") so they are never "on battery".
[SupportedOSPlatform("windows")]
public sealed class Win32BatteryStatus : IBatteryStatus
{
    public bool IsOnBattery()
    {
        if (!GetSystemPowerStatus(out var status))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetSystemPowerStatus failed");
        }
        // ACLineStatus: 0 = Offline (battery), 1 = Online (AC), 255 = Unknown.
        // Treat unknown as "not on battery" — safer to act than to indefinitely skip.
        return status.ACLineStatus == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);
}
