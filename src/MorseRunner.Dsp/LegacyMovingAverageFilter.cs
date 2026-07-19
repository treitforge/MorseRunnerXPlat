namespace MorseRunner.Dsp;

internal sealed class LegacyMovingAverageFilter
{
    private readonly int _blockSize;
    private readonly int _points;
    private readonly int _passes;
    private readonly float[][] _realBuffers;
    private readonly float[][] _imaginaryBuffers;
    private readonly float _normalization;

    public LegacyMovingAverageFilter(
        int blockSize,
        int points,
        int passes,
        float gainDb)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(points);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(passes);
        _blockSize = blockSize;
        _points = points;
        _passes = passes;
        _normalization = (float)(
            Math.Pow(10d, 0.05d * gainDb)
            * Math.Pow(points, -passes));
        _realBuffers = CreateBuffers();
        _imaginaryBuffers = CreateBuffers();
    }

    public void Process(
        ReadOnlySpan<float> realInput,
        ReadOnlySpan<float> imaginaryInput,
        Span<float> realOutput,
        Span<float> imaginaryOutput)
    {
        ValidateLength(realInput.Length);
        ValidateLength(imaginaryInput.Length);
        ValidateLength(realOutput.Length);
        ValidateLength(imaginaryOutput.Length);
        ProcessComponent(realInput, realOutput, _realBuffers);
        ProcessComponent(imaginaryInput, imaginaryOutput, _imaginaryBuffers);
    }

    public void Reset()
    {
        Clear(_realBuffers);
        Clear(_imaginaryBuffers);
    }

    private float[][] CreateBuffers()
    {
        var buffers = new float[_passes + 1][];
        for (int index = 0; index < buffers.Length; index++)
        {
            buffers[index] = new float[_blockSize + _points];
        }

        return buffers;
    }

    private void ProcessComponent(
        ReadOnlySpan<float> input,
        Span<float> output,
        float[][] buffers)
    {
        float[] inputBuffer = buffers[0];
        Array.Copy(
            inputBuffer,
            _blockSize,
            inputBuffer,
            0,
            _points);
        input.CopyTo(inputBuffer.AsSpan(_points, _blockSize));

        for (int pass = 1; pass <= _passes; pass++)
        {
            float[] source = buffers[pass - 1];
            float[] destination = buffers[pass];
            Array.Copy(
                destination,
                _blockSize,
                destination,
                0,
                _points);
            for (int index = _points;
                 index < destination.Length;
                 index++)
            {
                destination[index] = destination[index - 1]
                    - source[index - _points]
                    + source[index];
            }
        }

        float[] result = buffers[_passes];
        for (int index = 0; index < _blockSize; index++)
        {
            output[index] = result[_points + index] * _normalization;
        }
    }

    private void ValidateLength(int length)
    {
        if (length != _blockSize)
        {
            throw new ArgumentException(
                $"The filter requires {_blockSize} samples.");
        }
    }

    private static void Clear(float[][] buffers)
    {
        foreach (float[] buffer in buffers)
        {
            Array.Clear(buffer);
        }
    }
}
