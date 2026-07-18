namespace MorseRunner.Audio;

internal sealed class AudioBlockQueue
{
    private readonly float[][] _blocks;
    private readonly int[] _lengths;
    private long _writeSequence;
    private long _readSequence;
    private int _readOffset;

    public AudioBlockQueue(int capacity, int blockSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);

        _blocks = new float[capacity][];
        _lengths = new int[capacity];
        for (int index = 0; index < capacity; index++)
        {
            _blocks[index] = new float[blockSize];
        }
    }

    public int Capacity => _blocks.Length;

    public int Count
    {
        get
        {
            long writeSequence = Volatile.Read(ref _writeSequence);
            long readSequence = Volatile.Read(ref _readSequence);
            return (int)Math.Clamp(
                writeSequence - readSequence,
                0,
                Capacity);
        }
    }

    public bool TryWrite(ReadOnlySpan<float> samples)
    {
        if (samples.Length > _blocks[0].Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(samples),
                "The audio block exceeds the configured block size.");
        }

        long writeSequence = _writeSequence;
        long readSequence = Volatile.Read(ref _readSequence);
        if (writeSequence - readSequence >= Capacity)
        {
            return false;
        }

        int slot = (int)(writeSequence % Capacity);
        samples.CopyTo(_blocks[slot]);
        _lengths[slot] = samples.Length;
        Volatile.Write(ref _writeSequence, writeSequence + 1);
        return true;
    }

    public bool TryReadSample(out float sample)
    {
        long readSequence = _readSequence;
        if (readSequence >= Volatile.Read(ref _writeSequence))
        {
            sample = 0f;
            return false;
        }

        int slot = (int)(readSequence % Capacity);
        sample = _blocks[slot][_readOffset];
        _readOffset++;
        if (_readOffset == _lengths[slot])
        {
            _readOffset = 0;
            Volatile.Write(ref _readSequence, readSequence + 1);
        }

        return true;
    }
}
