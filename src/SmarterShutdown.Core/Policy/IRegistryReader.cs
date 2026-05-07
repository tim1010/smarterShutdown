namespace SmarterShutdown.Core.Policy;

public interface IRegistryReader
{
    bool KeyExists(string keyPath);
    int? ReadDword(string keyPath, string valueName);
    string? ReadString(string keyPath, string valueName);
}
