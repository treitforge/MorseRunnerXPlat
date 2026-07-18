namespace MorseRunner.Dsp.Tests;

public sealed class LegacyRandomEffectsTests
{
    [Fact]
    public void FreePascalSeedProducesThePinnedUniformSequence()
    {
        var effects = new LegacyRandomEffects(new LegacyRandom(12_345));
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
    public void TimeConversionUsesLegacyBlockSemantics()
    {
        Assert.Equal(27, LegacyRandomEffects.SecondsToBlocks(1.25f));
        Assert.Equal(0.557278931f, LegacyRandomEffects.BlocksToSeconds(12f));
    }

    [Fact]
    public void QsbEnvelopeMatchesPinnedLegacySamples()
    {
        var processor = new QsbProcessor(
            new LegacyRandomEffects(new LegacyRandom(12_345)));
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
