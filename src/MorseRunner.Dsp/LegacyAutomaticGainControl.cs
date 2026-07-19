namespace MorseRunner.Dsp;

internal sealed class LegacyAutomaticGainControl
{
    private readonly float[] _realBuffer;
    private readonly float[] _magnitudeBuffer;
    private readonly float[] _attackShape;
    private readonly float _beta;
    private readonly float _maxOutput;
    private int _bufferIndex;

    public LegacyAutomaticGainControl(
        int attackSamples = 155,
        int holdSamples = 155,
        float maxOutput = 20_000f,
        float noiseInputDb = 76f,
        float noiseOutputDb = 76f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attackSamples);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(holdSamples);
        _maxOutput = maxOutput;
        int length = 2 * (attackSamples + holdSamples) + 1;
        _realBuffer = new float[length];
        _magnitudeBuffer = new float[length];
        _attackShape = new float[length];
        for (int index = 0; index < attackSamples; index++)
        {
            float value = (float)Math.Log(
                0.5f
                - (0.5f * Math.Cos(
                    (index + 1) * Math.PI / (attackSamples + 1))));
            _attackShape[index] = value;
            _attackShape[length - 1 - index] = value;
        }

        float noiseInput = (float)Math.Pow(10d, 0.05d * noiseInputDb);
        float noiseOutput = MathF.Min(
            0.25f * maxOutput,
            (float)Math.Pow(10d, 0.05d * noiseOutputDb));
        _beta = (float)(
            noiseInput
            / Math.Log(
                (double)maxOutput / (maxOutput - noiseOutput)));
    }

    public void Process(ReadOnlySpan<float> input, Span<float> output)
    {
        if (output.Length != input.Length)
        {
            throw new ArgumentException(
                "AGC input and output lengths must match.");
        }

        for (int index = 0; index < input.Length; index++)
        {
            float value = input[index];
            _realBuffer[_bufferIndex] = value;
            _magnitudeBuffer[_bufferIndex] =
                (float)Math.Log(Math.Abs((double)value + 1e-10d));
            _bufferIndex = (_bufferIndex + 1) % _realBuffer.Length;
            int middle =
                (_bufferIndex + (_realBuffer.Length / 2))
                % _realBuffer.Length;
            output[index] = _realBuffer[middle] * CalculateGain();
        }
    }

    private float CalculateGain()
    {
        float envelope = 1e-10f;
        int dataIndex = _bufferIndex;
        for (int shapeIndex = 0;
             shapeIndex < _attackShape.Length;
             shapeIndex++)
        {
            float sample =
                _magnitudeBuffer[dataIndex] + _attackShape[shapeIndex];
            if (sample > envelope)
            {
                envelope = sample;
            }

            dataIndex++;
            if (dataIndex == _magnitudeBuffer.Length)
            {
                dataIndex = 0;
            }
        }

        envelope = (float)Math.Exp(envelope);
        return (float)(
            _maxOutput
            * (1d - Math.Exp(-(double)envelope / _beta))
            / envelope);
    }
}
