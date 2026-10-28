namespace DmxPro.Engine.Tests;

/// <summary>
/// Shared steady-state allocation probe for the "AllocatesNothing" tests. It first runs the measured cycle itself as
/// warm-up until the JIT stops compiling (tier-0 code, tiering and one-time lazy initialisation are not measured), then
/// measures up to <c>attempts</c> rounds on this thread and returns the smallest result, stopping at the first clean
/// round. Under a parallel full-solution run tiering or runtime bookkeeping can land inside one round and be counted
/// on this thread; a real per-call allocation shows up in every round (each runs hundreds to thousands of calls), so
/// the assertion is not weakened.
/// </summary>
internal static class AllocationProbe
{
    public static long Measure(Action<int> cycle, int iterations = 1000, int attempts = 3, int warmupPerRound = 100)
    {
        var i = 0;
        for (var round = 0; round < 20; round++)
        {
            var compiled = System.Runtime.JitInfo.GetCompiledMethodCount();
            for (var k = 0; k < warmupPerRound; k++)
            {
                cycle(i++);
            }

            if (round > 0 && System.Runtime.JitInfo.GetCompiledMethodCount() == compiled)
            {
                break;
            }
        }

        var allocated = long.MaxValue;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var k = 0; k < iterations; k++)
            {
                cycle(i++);
            }

            allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
            if (allocated == 0)
            {
                break;
            }
        }

        return allocated;
    }
}
