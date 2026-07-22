namespace MorseRunner.Dsp;

public sealed class StationMixer
{
    private const double TwoPi = 2d * Math.PI;

    private readonly int _sampleRate;
    private float _bfoPhase;
    private float _bfoPhaseIncrement;

    public StationMixer(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        _sampleRate = sampleRate;
    }

    public float BfoPhase => _bfoPhase;

    public float BfoPhaseIncrement => _bfoPhaseIncrement;

    public void BeginTransmission(int pitchOffsetHz)
    {
        _bfoPhase = 0f;
        _bfoPhaseIncrement = (float)(
            TwoPi * pitchOffsetHz / _sampleRate);
    }

    public void MixBlock(
        ReadOnlySpan<float> envelope,
        Span<float> real,
        Span<float> imaginary,
        int ritHz,
        float ritPhase)
    {
        if (envelope.Length != real.Length
            || envelope.Length != imaginary.Length)
        {
            throw new ArgumentException(
                "Envelope and complex receiver spans must have the "
                + "same length.");
        }

        for (int index = 0; index < envelope.Length; index++)
        {
            float stationBfo = TakeNextBfo();
            float mixedPhase = (float)(
                stationBfo
                - ritPhase
                - ((double)index * TwoPi * ritHz / _sampleRate));
            real[index] = (float)(
                real[index]
                + (envelope[index] * Math.Cos(mixedPhase)));
            imaginary[index] = (float)(
                imaginary[index]
                - (envelope[index] * Math.Sin(mixedPhase)));
        }
    }

    public static float AdvanceRitPhase(
        float ritPhase,
        int blockSize,
        int ritHz,
        int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        ritPhase = (float)(
            ritPhase
            + ((double)blockSize * TwoPi * ritHz / sampleRate));
        while (ritPhase > TwoPi)
        {
            ritPhase = (float)(ritPhase - TwoPi);
        }

        while (ritPhase < -TwoPi)
        {
            ritPhase = (float)(ritPhase + TwoPi);
        }

        return ritPhase;
    }

    private float TakeNextBfo()
    {
        float result = _bfoPhase;
        _bfoPhase += _bfoPhaseIncrement;
        if (_bfoPhase > TwoPi)
        {
            _bfoPhase = (float)(_bfoPhase - TwoPi);
        }

        return result;
    }
}
