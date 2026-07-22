namespace MorseRunner.Dsp.Tests;

public sealed class RandomEffectsTests
{
    [Fact]
    public void SeedOneProducesThePinnedUniformSequence()
    {
        var effects = new RandomEffects(new DeterministicRandom(12_345));
        float[] expected =
        [
            0.859232187f,
            0.780309439f,
            -0.367248893f,
            -0.738585413f,
            -0.632162392f,
            -0.920481026f,
            -0.590879440f,
            0.652872264f,
        ];

        foreach (float value in expected)
        {
            Assert.Equal(value, effects.Uniform());
        }
    }

    [Fact]
    public void ReceiverNoiseUsesCeFinalSingleRounding()
    {
        var random = new DeterministicRandom(12_345);

        float firstReal =
            (float)(18_000d * (random.NextDouble() - 0.5d));
        float firstImaginary =
            (float)(18_000d * (random.NextDouble() - 0.5d));

        Assert.Equal(
            0x45F1A8B7U,
            BitConverter.SingleToUInt32Bits(firstReal));
        Assert.Equal(
            0x45DB7647U,
            BitConverter.SingleToUInt32Bits(firstImaginary));
    }

    [Fact]
    public void TimeConversionUsesPinnedBlockSemantics()
    {
        Assert.Equal(27, RandomEffects.SecondsToBlocks(1.25f));
        Assert.Equal(0.557278931f, RandomEffects.BlocksToSeconds(12f));
    }

    [Fact]
    public void QsbEnvelopeMatchesPinnedSamples()
    {
        var processor = new QsbProcessor(
            new RandomEffects(new DeterministicRandom(12_345)));
        processor.Level = 0.75f;
        processor.Bandwidth = 0.5f;
        var samples = new float[512];
        Array.Fill(samples, 1f);

        processor.Apply(samples);

        Assert.Equal(1.324409127f, samples[0]);
        Assert.Equal(1.303519845f, samples[128]);
        Assert.Equal(1.282279611f, samples[256]);
        Assert.Equal(1.260856271f, samples[384]);
        Assert.Equal(656.485778809f, samples.Sum());
    }
}
