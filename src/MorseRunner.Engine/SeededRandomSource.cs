namespace MorseRunner.Engine;

internal sealed class SeededRandomSource
{
    private ulong _state;

    public SeededRandomSource(int seed)
    {
        _state = unchecked((uint)seed) + 0x9E3779B97F4A7C15UL;
        NextUInt32();
    }

    public int Next(int exclusiveMaximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMaximum);
        return (int)(NextUInt32() % (uint)exclusiveMaximum);
    }

    private uint NextUInt32()
    {
        ulong value = _state;
        value ^= value >> 12;
        value ^= value << 25;
        value ^= value >> 27;
        _state = value;
        return (uint)((value * 2685821657736338717UL) >> 32);
    }
}
