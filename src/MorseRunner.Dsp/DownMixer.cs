namespace MorseRunner.Dsp;

public sealed class DownMixer
{
    private double _phase;
    private float _twoPiDeltaTime;
    private int _sampleRate = 5_512;

    public DownMixer()
    {
        UpdateDeltaTime();
    }

    public int SampleRate
    {
        get => _sampleRate;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _sampleRate = value;
            UpdateDeltaTime();
        }
    }

    public float Frequency { get; set; }

    public void Reset()
    {
        _phase = 0d;
    }

    public void Mix(
        ReadOnlySpan<float> input,
        Span<ComplexSample> output)
    {
        if (output.Length < input.Length)
        {
            throw new ArgumentException(
                "The output span is shorter than the input span.",
                nameof(output));
        }

        NormalizePhase();
        for (int index = 0; index < input.Length; index++)
        {
            output[index] = new(
                (float)(input[index] * Math.Cos(_phase)),
                (float)(-input[index] * Math.Sin(_phase)));
            _phase += Frequency * _twoPiDeltaTime;
        }
    }

    private void NormalizePhase()
    {
        if (_phase > 2d * Math.PI)
        {
            _phase -= 2d * Math.PI * Math.Truncate(_phase / (2d * Math.PI));
        }
    }

    private void UpdateDeltaTime()
    {
        _twoPiDeltaTime = (float)(2d * Math.PI / _sampleRate);
    }
}
