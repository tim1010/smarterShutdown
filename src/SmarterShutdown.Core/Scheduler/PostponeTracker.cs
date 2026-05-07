namespace SmarterShutdown.Core.Scheduler;

public sealed class PostponeTracker
{
    public int Count { get; private set; }

    public bool TryPostpone(int maxPostpones)
    {
        // Spec: maxPostpones = 0 means unlimited.
        if (maxPostpones != 0 && Count >= maxPostpones)
        {
            return false;
        }
        Count++;
        return true;
    }

    public void Reset() => Count = 0;
}
