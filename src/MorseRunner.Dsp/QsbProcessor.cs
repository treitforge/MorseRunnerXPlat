namespace MorseRunner.Dsp;

public sealed class QsbProcessor
{
    private const int SampleRate = 11_025;
    private const int BlockSize = 512;
    private const int GainBlockSize = BlockSize / 4;
    private readonly LegacyRandomEffects _effects;
    private readonly QuickAverage _filter = new();
    private float _gain;
    private float _bandwidth;

    public QsbProcessor(LegacyRandomEffects effects)
    {
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _filter.Passes = 3;
        Level = 1f;
        Bandwidth = 0.1f;
    }

    public float Level { get; set; }

    public float Bandwidth
    {
        get => _bandwidth;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _bandwidth = value;
            _filter.Points = (int)Math.Ceiling(
                0.37d * SampleRate / (GainBlockSize * value));
            for (int index = 0; index <= _filter.Points * 3; index++)
            {
                _gain = NewGain();
            }
        }
    }

    public void Apply(Span<float> samples)
    {
        int blockCount = samples.Length / GainBlockSize;
        for (int block = 0; block < blockCount; block++)
        {
            float gainDelta = (NewGain() - _gain) / GainBlockSize;
            int start = block * GainBlockSize;
            for (int index = 0; index < GainBlockSize; index++)
            {
                samples[start + index] *= _gain;
                _gain += gainDelta;
            }
        }
    }

    private float NewGain()
    {
        ComplexSample filtered = _filter.Filter(
            new ComplexSample(_effects.Uniform(), _effects.Uniform()));
        float gain = MathF.Sqrt(
            ((filtered.Real * filtered.Real)
                + (filtered.Imaginary * filtered.Imaginary))
            * 3f
            * _filter.Points);
        return (gain * Level) + (1f - Level);
    }
}
