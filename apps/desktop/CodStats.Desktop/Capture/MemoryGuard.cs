namespace CodStats.Desktop.Capture;

/// <summary>
/// Keeps the recognition's memory small. Reading crops of 4K frames allocates large arrays, and on a PC with lots
/// of RAM .NET lets those pile up for gigabytes before it collects them. Above the limit the client collects at
/// once; with only a few MB live that takes milliseconds.
/// </summary>
public static class MemoryGuard
{
    public const long Limit = 256L * 1024 * 1024;

    /// <summary>Call after each analysed frame. True when it collected.</summary>
    public static bool AfterFrame()
    {
        if (GC.GetTotalMemory(false) < Limit)
        {
            return false;
        }
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        return true;
    }
}
