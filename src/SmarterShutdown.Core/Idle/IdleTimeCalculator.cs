namespace SmarterShutdown.Core.Idle;

public static class IdleTimeCalculator
{
    public static TimeSpan Compute(uint currentTick, uint lastInputTick)
    {
        // Unsigned subtraction handles the ~49.7-day GetTickCount wraparound naturally.
        uint deltaMs = currentTick - lastInputTick;
        return TimeSpan.FromMilliseconds(deltaMs);
    }
}
