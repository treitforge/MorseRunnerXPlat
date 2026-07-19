namespace MorseRunner.Dsp;

internal sealed class LegacyModulator
{
    private readonly float[] _sine;
    private readonly float[] _cosine;
    private int _sampleIndex;

    public LegacyModulator(
        int sampleRate,
        float requestedCarrierHz,
        float gain = 1f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedCarrierHz);
        int periodSamples = Math.Max(
            2,
            (int)MathF.Round(sampleRate / requestedCarrierHz));
        EffectiveCarrierHz = (float)sampleRate / periodSamples;
        float phaseIncrement = (float)(2d * Math.PI / periodSamples);
        _sine = new float[periodSamples];
        _cosine = new float[periodSamples];
        _sine[0] = 0f;
        _cosine[0] = 1f;
        _sine[1] = (float)Math.Sin(phaseIncrement);
        _cosine[1] = (float)Math.Cos(phaseIncrement);
        for (int index = 2; index < periodSamples; index++)
        {
            _cosine[index] =
                (_cosine[1] * _cosine[index - 1])
                - (_sine[1] * _sine[index - 1]);
            _sine[index] =
                (_cosine[1] * _sine[index - 1])
                + (_sine[1] * _cosine[index - 1]);
        }

        for (int index = 0; index < periodSamples; index++)
        {
            _sine[index] *= gain;
            _cosine[index] *= gain;
        }
    }

    public float EffectiveCarrierHz { get; }

    public void Process(
        ReadOnlySpan<float> real,
        ReadOnlySpan<float> imaginary,
        Span<float> output)
    {
        if (real.Length != imaginary.Length || output.Length != real.Length)
        {
            throw new ArgumentException(
                "Modulator input and output lengths must match.");
        }

        for (int index = 0; index < output.Length; index++)
        {
            output[index] =
                (real[index] * _sine[_sampleIndex])
                - (imaginary[index] * _cosine[_sampleIndex]);
            _sampleIndex++;
            if (_sampleIndex == _sine.Length)
            {
                _sampleIndex = 0;
            }
        }
    }
}
