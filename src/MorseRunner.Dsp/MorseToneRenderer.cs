namespace MorseRunner.Dsp;

public sealed class MorseToneRenderer
{
    private readonly MorseKeyer _keyer;
    private readonly int _sampleRate;
    private readonly float _carrierFrequency;
    private readonly float _gain;
    private float[] _envelope = [];
    private int _envelopePosition;
    private double _phase;

    public MorseToneRenderer(
        int sampleRate,
        int blockSize,
        int wordsPerMinute = 30,
        float carrierFrequency = 600f,
        float gain = 0.2f)
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
        _carrierFrequency = carrierFrequency;
        _gain = gain;
        _keyer = new(sampleRate, blockSize);
        _keyer.SetWordsPerMinute(wordsPerMinute);
    }

    public bool HasPendingAudio => _envelopePosition < _envelope.Length;

    public void LoadMessage(string text)
    {
        string encoded = MorseKeyer.Encode(text);
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
}
