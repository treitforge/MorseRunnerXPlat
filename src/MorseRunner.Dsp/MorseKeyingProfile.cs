namespace MorseRunner.Dsp;

public enum MorseKeyingMode
{
    Standard = 0,
    SstFarnsworth = 1,
}

public sealed class MorseKeyingProfile
{
    public const int MinimumQrmWordsPerMinute = 30;
    public const int MaximumQrmWordsPerMinute = 49;
    public const float DefaultRiseTimeSeconds = 0.005f;

    public MorseKeyingProfile(
        int sampleRate,
        int blockSize,
        MorseKeyingMode mode,
        float riseTimeSeconds = DefaultRiseTimeSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            riseTimeSeconds);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        SampleRate = sampleRate;
        BlockSize = blockSize;
        Mode = mode;
        RiseTimeSeconds = riseTimeSeconds;
        int rampLength = CalculateRampLength();
        _rampOn = MorseRamp.CreateOn(rampLength);
        _rampOff = MorseRamp.CreateOff(_rampOn);
    }

    private readonly float[] _rampOn;
    private readonly float[] _rampOff;

    public int SampleRate { get; }

    public int BlockSize { get; }

    public MorseKeyingMode Mode { get; }

    public float RiseTimeSeconds { get; }

    internal ReadOnlySpan<float> RampOn => _rampOn;

    internal ReadOnlySpan<float> RampOff => _rampOff;

    internal int RampLength => _rampOn.Length;

    internal int CalculateRampLength() =>
        (int)Math.Round(
            2.7d * (double)RiseTimeSeconds * SampleRate,
            MidpointRounding.ToEven);

    internal static void ValidateQrmWordsPerMinute(
        int wordsPerMinute)
    {
        if (wordsPerMinute is
            < MinimumQrmWordsPerMinute
            or > MaximumQrmWordsPerMinute)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wordsPerMinute),
                wordsPerMinute,
                $"QRM speed must be between "
                + $"{MinimumQrmWordsPerMinute} and "
                + $"{MaximumQrmWordsPerMinute} WPM.");
        }
    }
}
