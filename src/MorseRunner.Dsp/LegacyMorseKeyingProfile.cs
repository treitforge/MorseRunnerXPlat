namespace MorseRunner.Dsp;

public enum LegacyMorseKeyingMode
{
    Standard = 0,
    SstFarnsworth = 1,
}

public sealed class LegacyMorseKeyingProfile
{
    public const int MinimumQrmWordsPerMinute = 30;
    public const int MaximumQrmWordsPerMinute = 49;
    public const float DefaultRiseTimeSeconds = 0.005f;

    public LegacyMorseKeyingProfile(
        int sampleRate,
        int blockSize,
        LegacyMorseKeyingMode mode,
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
        _rampOn = LegacyMorseRamp.CreateOn(rampLength);
        _rampOff = LegacyMorseRamp.CreateOff(_rampOn);
    }

    private readonly float[] _rampOn;
    private readonly float[] _rampOff;

    public int SampleRate { get; }

    public int BlockSize { get; }

    public LegacyMorseKeyingMode Mode { get; }

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
                $"CE QRM speed must be between "
                + $"{MinimumQrmWordsPerMinute} and "
                + $"{MaximumQrmWordsPerMinute} WPM.");
        }
    }
}
