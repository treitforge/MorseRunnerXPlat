namespace MorseRunner.Dsp;

public sealed class QuickAverage
{
    private int _points = 128;
    private int _passes = 4;
    private float _scale;
    private double[][] _realBuffers = [];
    private double[][] _imaginaryBuffers = [];
    private int _index;
    private int _previousIndex;

    public QuickAverage()
    {
        Reset();
    }

    public int Points
    {
        get => _points;
        set
        {
            _points = Math.Max(1, value);
            Reset();
        }
    }

    public int Passes
    {
        get => _passes;
        set
        {
            _passes = Math.Clamp(value, 1, 8);
            Reset();
        }
    }

    public void Reset()
    {
        _realBuffers = CreateBuffers();
        _imaginaryBuffers = CreateBuffers();
        _scale = (float)Math.Pow(_points, -_passes);
        _index = 0;
        _previousIndex = _points - 1;
    }

    public float Filter(float value)
    {
        float result = FilterCore(value, _realBuffers);
        Advance();
        return result;
    }

    public ComplexSample Filter(ComplexSample value)
    {
        var result = new ComplexSample(
            FilterCore(value.Real, _realBuffers),
            FilterCore(value.Imaginary, _imaginaryBuffers));
        Advance();
        return result;
    }

    public float FilterMagnitude(ComplexSample value)
    {
        ComplexSample filtered = Filter(value);
        return MathF.Sqrt(
            (filtered.Real * filtered.Real)
            + (filtered.Imaginary * filtered.Imaginary));
    }

    private double[][] CreateBuffers()
    {
        var buffers = new double[_passes + 1][];
        for (int pass = 0; pass < buffers.Length; pass++)
        {
            buffers[pass] = new double[_points];
        }

        return buffers;
    }

    private float FilterCore(float value, double[][] buffers)
    {
        float result = value;
        for (int pass = 1; pass <= _passes; pass++)
        {
            value = result;
            result = (float)(
                buffers[pass][_previousIndex]
                - buffers[pass - 1][_index]
                + value);
            buffers[pass - 1][_index] = value;
        }

        buffers[_passes][_index] = result;
        return result * _scale;
    }

    private void Advance()
    {
        _previousIndex = _index;
        _index = (_index + 1) % _points;
    }
}
