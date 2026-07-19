using System.Diagnostics;

namespace MorseRunner.Engine;

internal sealed class AutomaticBlockClock(
    int sampleRate,
    int blockSize,
    int maximumCatchUpBlocks = 2)
{
    private long _startedTimestamp;
    private long _startedBlock;

    public void Start(long simulationBlock)
    {
        _startedBlock = simulationBlock;
        _startedTimestamp = Stopwatch.GetTimestamp();
    }

    public int GetDueBlockCount(long simulationBlock)
    {
        TimeSpan elapsed = Stopwatch.GetElapsedTime(_startedTimestamp);
        return GetDueBlockCount(elapsed, simulationBlock - _startedBlock);
    }

    internal int GetDueBlockCount(
        TimeSpan elapsed,
        long renderedBlocks)
    {
        long expectedBlocks = (long)Math.Floor(
            (elapsed.TotalSeconds * sampleRate / blockSize) + 0.00001d);
        long due = expectedBlocks - renderedBlocks;
        return (int)Math.Clamp(due, 0, maximumCatchUpBlocks);
    }
}
