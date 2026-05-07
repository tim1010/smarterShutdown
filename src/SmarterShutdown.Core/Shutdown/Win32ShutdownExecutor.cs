using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SmarterShutdown.Core.Models;

namespace SmarterShutdown.Core.Shutdown;

// Boundary adapter — no unit tests. The Worker mocks IShutdownExecutor.
//
// LocalSystem holds SE_SHUTDOWN_NAME but it is *disabled* in the token by default;
// AdjustTokenPrivileges enables it before any shutdown / suspend API call.
[SupportedOSPlatform("windows")]
public sealed class Win32ShutdownExecutor : IShutdownExecutor
{
    public void Execute(ShutdownAction action, bool force)
    {
        EnableShutdownPrivilege();

        switch (action)
        {
            case ShutdownAction.Shutdown:
                if (!InitiateSystemShutdownExW(
                        lpMachineName: null,
                        lpMessage: null,
                        dwTimeout: 0,
                        bForceAppsClosed: force,
                        bRebootAfterShutdown: false,
                        dwReason: ShutdownReasonMajorApplication | ShutdownReasonMinorMaintenance | ShutdownReasonFlagPlanned))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "InitiateSystemShutdownEx failed");
                }
                break;

            case ShutdownAction.Hibernate:
                if (!SetSuspendState(hibernate: true, forceCritical: false, disableWakeEvent: false))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetSuspendState (hibernate) failed");
                }
                break;

            case ShutdownAction.Sleep:
                if (!SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetSuspendState (sleep) failed");
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown shutdown action");
        }
    }

    // ---- privilege enablement ----

    private static void EnableShutdownPrivilege()
    {
        var hProcess = GetCurrentProcess();
        if (!OpenProcessToken(hProcess, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var hToken))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken failed");
        }

        try
        {
            if (!LookupPrivilegeValue(null, SE_SHUTDOWN_NAME, out var luid))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "LookupPrivilegeValue(SeShutdownPrivilege) failed");
            }

            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = SE_PRIVILEGE_ENABLED,
            };

            if (!AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "AdjustTokenPrivileges failed");
            }

            // AdjustTokenPrivileges returns true even when not all privileges were assigned;
            // ERROR_NOT_ALL_ASSIGNED (1300) means the calling token doesn't hold the privilege at all.
            int err = Marshal.GetLastWin32Error();
            if (err == ERROR_NOT_ALL_ASSIGNED)
            {
                throw new Win32Exception(err, "Calling process does not hold SeShutdownPrivilege");
            }
        }
        finally
        {
            CloseHandle(hToken);
        }
    }

    // ---- P/Invoke ----

    private const string SE_SHUTDOWN_NAME = "SeShutdownPrivilege";
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const int ERROR_NOT_ALL_ASSIGNED = 1300;

    // Reason codes (winnt.h).
    private const uint ShutdownReasonMajorApplication = 0x00040000;
    private const uint ShutdownReasonMinorMaintenance = 0x00000001;
    private const uint ShutdownReasonFlagPlanned = 0x80000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr TokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState,
        uint BufferLength,
        IntPtr PreviousState,
        IntPtr ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "InitiateSystemShutdownExW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitiateSystemShutdownExW(
        string? lpMachineName,
        string? lpMessage,
        uint dwTimeout,
        [MarshalAs(UnmanagedType.Bool)] bool bForceAppsClosed,
        [MarshalAs(UnmanagedType.Bool)] bool bRebootAfterShutdown,
        uint dwReason);

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSuspendState(
        [MarshalAs(UnmanagedType.Bool)] bool hibernate,
        [MarshalAs(UnmanagedType.Bool)] bool forceCritical,
        [MarshalAs(UnmanagedType.Bool)] bool disableWakeEvent);
}
