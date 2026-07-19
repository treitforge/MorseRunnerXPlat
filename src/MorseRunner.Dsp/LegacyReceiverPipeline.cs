namespace MorseRunner.Dsp;

public sealed class LegacyReceiverPipeline
{
    private const float Pcm16Scale = 32_768f;
    private readonly float[] _standbyReal;
    private readonly float[] _standbyImaginary;
    private readonly float[] _filteredReal;
    private readonly float[] _filteredImaginary;
    private readonly float[] _modulated;
    private readonly float[] _agcOutput;
    private LegacyMovingAverageFilter _activeFilter;
    private LegacyMovingAverageFilter _standbyFilter;
    private readonly LegacyModulator _modulator;
    private readonly LegacyAutomaticGainControl _agc = new();
    private int _blockNumber;

    public LegacyReceiverPipeline(
        int sampleRate,
        int blockSize,
        int bandwidthHz,
        int requestedCarrierHz)
    {
        int points = (int)MathF.Round(
            0.7f * sampleRate / bandwidthHz);
        float gainDb = 10f * MathF.Log10(500f / bandwidthHz);
        _activeFilter = new(blockSize, points, passes: 3, gainDb);
        _standbyFilter = new(blockSize, points, passes: 3, gainDb);
        _modulator = new(sampleRate, requestedCarrierHz);
        _standbyReal = new float[blockSize];
        _standbyImaginary = new float[blockSize];
        _filteredReal = new float[blockSize];
        _filteredImaginary = new float[blockSize];
        _modulated = new float[blockSize];
        _agcOutput = new float[blockSize];
    }

    public float EffectiveCarrierHz => _modulator.EffectiveCarrierHz;

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
        _blockNumber++;
        if ((_blockNumber % 10) == 0)
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
