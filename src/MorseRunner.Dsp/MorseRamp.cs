namespace MorseRunner.Dsp;

internal static class MorseRamp
{
    public static float[] CreateOn(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        var response = new float[length];
        for (int index = 0; index < response.Length; index++)
        {
            float position = (float)((double)index / length);
            response[index] = (float)(
                0.35875d
                - (0.48829d * Math.Cos(2d * Math.PI * position))
                + (0.14128d * Math.Cos(4d * Math.PI * position))
                - (0.01168d * Math.Cos(6d * Math.PI * position)));
        }

        for (int index = 1; index < response.Length; index++)
        {
            response[index] = response[index - 1] + response[index];
        }

        float scale = 1f / response[^1];
        for (int index = 0; index < response.Length; index++)
        {
            response[index] *= scale;
        }

        return response;
    }

    public static float[] CreateOff(ReadOnlySpan<float> rampOn)
    {
        var rampOff = new float[rampOn.Length];
        for (int index = 0; index < rampOn.Length; index++)
        {
            rampOff[rampOff.Length - 1 - index] = rampOn[index];
        }

        return rampOff;
    }
}
