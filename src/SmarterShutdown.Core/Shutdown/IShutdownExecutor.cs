using SmarterShutdown.Core.Models;

namespace SmarterShutdown.Core.Shutdown;

public interface IShutdownExecutor
{
    void Execute(ShutdownAction action, bool force);
}
