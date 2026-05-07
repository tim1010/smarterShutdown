namespace SmarterShutdown.Core.Idle;

public interface IIdleDetector
{
    TimeSpan GetIdleTime();
}
