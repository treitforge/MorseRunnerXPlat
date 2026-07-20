namespace MorseRunner.Dsp;

public sealed class MorseToneRenderer
{
    private readonly MorseKeyer _keyer;
    private readonly int _sampleRate;
    private readonly float _gain;
    private float _carrierFrequency;
    private float[] _envelope = [];
    private int _envelopePosition;
    private double _phase;

    public MorseToneRenderer(
        int sampleRate,
        int blockSize,
        int wordsPerMinute = 30,
        float carrierFrequency = 600f,
        float gain = 1f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wordsPerMinute);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(carrierFrequency);
        if (gain is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(gain));
        }

        _sampleRate = sampleRate;
        _carrierFrequency = QuantizeCarrier(carrierFrequency);
        _gain = gain;
        _keyer = new(sampleRate, blockSize);
        _keyer.SetWordsPerMinute(wordsPerMinute);
    }

    public bool HasPendingAudio => _envelopePosition < _envelope.Length;

    public int WordsPerMinute => _keyer.SendingWordsPerMinute;

    public float CarrierFrequency
    {
        get => _carrierFrequency;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _carrierFrequency = QuantizeCarrier(value);
        }
    }

    public void SetWordsPerMinute(int wordsPerMinute)
    {
        _keyer.SetWordsPerMinute(wordsPerMinute);
    }

    public void LoadMessage(string text)
    {
        string encoded = _keyer.EncodeText(text);
        _envelope = _keyer.CreateEnvelope(encoded);
        _envelopePosition = 0;
    }

    public void Render(Span<float> output)
    {
        double phaseStep = 2d * Math.PI * _carrierFrequency / _sampleRate;
        for (int index = 0; index < output.Length; index++)
        {
            float envelope = _envelopePosition < _envelope.Length
                ? _envelope[_envelopePosition]
                : 0f;
            output[index] = envelope * _gain * (float)Math.Sin(_phase);
            _envelopePosition++;
            _phase += phaseStep;
            if (_phase >= 2d * Math.PI)
            {
                _phase -= 2d * Math.PI;
            }
        }
    }

    public void RenderEnvelope(Span<float> output)
    {
        for (int index = 0; index < output.Length; index++)
        {
            float envelope = _envelopePosition < _envelope.Length
                ? _envelope[_envelopePosition]
                : 0f;
            output[index] = envelope * _gain;
            _envelopePosition++;
        }
    }

    private float QuantizeCarrier(float requestedFrequency)
    {
        int periodSamples = Math.Max(
            2,
            (int)MathF.Round(_sampleRate / requestedFrequency));
        return (float)_sampleRate / periodSamples;
    }
}
