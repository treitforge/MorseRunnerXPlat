namespace MorseRunner.Dsp;

public sealed class LegacyRandom
{
    private const int StateLength = 624;
    private const int Period = 397;
    private readonly uint[] _state = new uint[StateLength];
    private int _index = StateLength;

    public LegacyRandom(int seed)
    {
        _state[0] = unchecked((uint)seed);
        for (int index = 1; index < StateLength; index++)
        {
            uint previous = _state[index - 1];
            _state[index] = unchecked(
                (1_812_433_253U * (previous ^ (previous >> 30))) + (uint)index);
        }
    }

    public float NextSingle()
    {
        return (float)NextDouble();
    }

    public double NextDouble()
    {
        return NextUInt32() * (1.0 / 4_294_967_296.0);
    }

    public int Next(int exclusiveMaximum)
    {
        if (exclusiveMaximum < 0)
        {
            exclusiveMaximum++;
        }

        return (int)(NextUInt32() * (long)exclusiveMaximum >> 32);
    }

    public long NextInt64(long exclusiveMaximum)
    {
        long value = NextUInt32();
        value |= (long)(((ulong)NextUInt32() << 32) & long.MaxValue);
        return exclusiveMaximum == 0 ? 0 : value % exclusiveMaximum;
    }

    private uint NextUInt32()
    {
        if (_index >= StateLength)
        {
            Twist();
        }

        uint value = _state[_index++];
        value ^= value >> 11;
        value ^= (value << 7) & 0x9D2C5680U;
        value ^= (value << 15) & 0xEFC60000U;
        value ^= value >> 18;
        return value;
    }

    private void Twist()
    {
        for (int index = 0; index < StateLength; index++)
        {
            uint bits = (_state[index] & 0x80000000U)
                | (_state[(index + 1) % StateLength] & 0x7FFFFFFFU);
            uint next = _state[(index + Period) % StateLength] ^ (bits >> 1);
            if ((bits & 1U) != 0)
            {
                next ^= 0x9908B0DFU;
            }

            _state[index] = next;
        }

        _index = 0;
    }
}
