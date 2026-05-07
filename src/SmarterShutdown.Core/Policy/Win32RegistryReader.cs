using System.Runtime.Versioning;
using Microsoft.Win32;

namespace SmarterShutdown.Core.Policy;

// Thin adapter over HKLM. Not unit-tested — boundary code with no logic of its own;
// covered by integration testing once the Service host is in place.
[SupportedOSPlatform("windows")]
public sealed class Win32RegistryReader : IRegistryReader
{
    public bool KeyExists(string keyPath)
    {
        using var key = Registry.LocalMachine.OpenSubKey(keyPath);
        return key is not null;
    }

    public int? ReadDword(string keyPath, string valueName)
    {
        using var key = Registry.LocalMachine.OpenSubKey(keyPath);
        return key?.GetValue(valueName) is int i ? i : null;
    }

    public string? ReadString(string keyPath, string valueName)
    {
        using var key = Registry.LocalMachine.OpenSubKey(keyPath);
        return key?.GetValue(valueName) as string;
    }
}
