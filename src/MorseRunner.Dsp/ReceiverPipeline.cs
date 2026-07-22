namespace MorseRunner.Dsp;

public sealed class ReceiverPipeline
{
    private const float Pcm16Scale = 32_768f;
    private readonly int _sampleRate;
    private readonly int _blockSize;
    private readonly float[] _standbyReal;
    private readonly float[] _standbyImaginary;
    private readonly float[] _filteredReal;
    private readonly float[] _filteredImaginary;
    private readonly float[] _modulated;
    private readonly float[] _agcOutput;
    private MovingAverageFilter _activeFilter;
    private MovingAverageFilter _standbyFilter;
    private readonly ReceiverModulator _modulator;
    private readonly AutomaticGainControl _agc = new();
    private int _absoluteRequestCount;

    public ReceiverPipeline(
        int sampleRate,
        int blockSize,
        int bandwidthHz,
        int requestedCarrierHz,
        int initialAbsoluteRequestCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(
            initialAbsoluteRequestCount);

        _sampleRate = sampleRate;
        _blockSize = blockSize;
        _activeFilter = CreateFilter(bandwidthHz);
        _standbyFilter = CreateFilter(bandwidthHz);
        _modulator = new(sampleRate, requestedCarrierHz);
        _standbyReal = new float[blockSize];
        _standbyImaginary = new float[blockSize];
        _filteredReal = new float[blockSize];
        _filteredImaginary = new float[blockSize];
        _modulated = new float[blockSize];
        _agcOutput = new float[blockSize];
        _absoluteRequestCount = initialAbsoluteRequestCount;
    }

    public float EffectiveCarrierHz => _modulator.EffectiveCarrierHz;

    public void SetBandwidth(int bandwidthHz)
    {
        _activeFilter = CreateFilter(bandwidthHz);
        _standbyFilter = CreateFilter(bandwidthHz);
    }

    public void Process(
        ReadOnlySpan<float> realInput,
        ReadOnlySpan<float> imaginaryInput,
        Span<float> output)
    {
        ProcessCore(realInput, imaginaryInput);
        for (int index = 0; index < output.Length; index++)
        {
            output[index] = Math.Clamp(
                _agcOutput[index] / Pcm16Scale,
                -1f,
                1f);
        }
    }

    internal void ProcessPcm16(
        ReadOnlySpan<float> realInput,
        ReadOnlySpan<float> imaginaryInput,
        Span<float> output)
    {
        ProcessCore(realInput, imaginaryInput);
        _agcOutput.AsSpan().CopyTo(output);
    }

    private MovingAverageFilter CreateFilter(int bandwidthHz)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bandwidthHz);
        int points = checked((int)Math.Round(
            0.7d * _sampleRate / bandwidthHz,
            MidpointRounding.ToEven));
        float gainDb = (float)(
            10d * Math.Log10(500d / bandwidthHz));
        return new(_blockSize, points, passes: 3, gainDb);
    }

    private void ProcessCore(
        ReadOnlySpan<float> realInput,
        ReadOnlySpan<float> imaginaryInput)
    {
        _standbyFilter.Process(
            realInput,
            imaginaryInput,
            _standbyReal,
            _standbyImaginary);
        _activeFilter.Process(
            realInput,
            imaginaryInput,
            _filteredReal,
            _filteredImaginary);
        _absoluteRequestCount++;
        if ((_absoluteRequestCount % 10) == 0)
        {
            (_activeFilter, _standbyFilter) =
                (_standbyFilter, _activeFilter);
            _standbyFilter.Reset();
        }

        _modulator.Process(
            _filteredReal,
            _filteredImaginary,
            _modulated);
        _agc.Process(_modulated, _agcOutput);
    }
}
