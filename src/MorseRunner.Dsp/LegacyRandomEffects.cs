namespace MorseRunner.Dsp;

public sealed class LegacyRandomEffects
{
    private readonly LegacyRandom _random;

    public LegacyRandomEffects(LegacyRandom random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public float Uniform() => (float)((2d * _random.NextDouble()) - 1d);

    public float UShaped() =>
        (float)Math.Sin(Math.PI * (_random.NextDouble() - 0.5d));

    public float Normal()
    {
        while (true)
        {
            double first = _random.NextDouble();
            if (first == 0d)
            {
                continue;
            }

            return (float)(
                Math.Sqrt(-2d * Math.Log(first))
                * Math.Cos(2d * Math.PI * _random.NextDouble()));
        }
    }

    public float Rayleigh(float mean) =>
        (float)(
            mean
            * Math.Sqrt(
                -Math.Log(_random.NextDouble())
                - Math.Log(_random.NextDouble())));

    public float GaussianLimited(float mean, float limit)
    {
        float offset;
        do
        {
            offset = Normal() * 0.5f * limit;
        }
        while (Math.Abs(offset) > limit);

        return mean + offset;
    }

    public int Poisson(float mean)
    {
        float threshold = MathF.Exp(-mean);
        float product = 1f;
        for (int value = 0; value <= 30; value++)
        {
            product *= (float)_random.NextDouble();
            if (product <= threshold)
            {
                return value;
            }
        }

        return 30;
    }

    public static int SecondsToBlocks(
        float seconds,
        int sampleRate = 11_025,
        int blockSize = 512)
    {
        return (int)MathF.Round(
            (float)sampleRate / blockSize * seconds,
            MidpointRounding.ToEven);
    }

    public static float BlocksToSeconds(
        float blocks,
        int sampleRate = 11_025,
        int blockSize = 512)
    {
        return blocks * blockSize / sampleRate;
    }
}
