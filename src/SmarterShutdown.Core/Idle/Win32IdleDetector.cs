using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SmarterShutdown.Core.Idle;

// Thin P/Invoke adapter — boundary code, no unit tests. Logic lives in IdleTimeCalculator.
[SupportedOSPlatform("windows")]
public sealed class Win32IdleDetector : IIdleDetector
{
    public TimeSpan GetIdleTime()
    {
        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref lii))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        return IdleTimeCalculator.Compute(GetTickCount(), lii.dwTime);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("kernel32.dll")]
    private static extern uint GetTickCount();
}
