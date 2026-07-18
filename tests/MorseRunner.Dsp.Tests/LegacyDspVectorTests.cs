using System.Diagnostics;
using System.Security.Cryptography;
using MorseRunner.Dsp;

namespace MorseRunner.Dsp.Tests;

public sealed class LegacyDspVectorTests
{
    private static readonly float[] ExpectedEnvelope =
    [
        0.000001122f,
        0.019463515f,
        0.319153339f,
        0.851711273f,
        0.997168958f,
        1.000000000f,
        1.000000000f,
        1.000000000f,
    ];

    private static readonly ComplexSample[] ExpectedDownmix =
    [
        new(1.000000000f, 0.000000000f),
        new(1.414213538f, -1.414213538f),
        new(-0.000000131f, -3.000000000f),
        new(-2.828427315f, -2.828426838f),
        new(-5.000000000f, 0.000000437f),
        new(-4.242640018f, 4.242640972f),
        new(0.000000918f, 7.000000000f),
        new(5.656855106f, 5.656853199f),
    ];

    private static readonly float[] ExpectedQuickAverage =
    [
        0.062500000f,
        0.250000000f,
        0.625000000f,
        1.250000000f,
        2.062500000f,
        3.000000000f,
        4.000000000f,
        5.000000000f,
        6.000000000f,
        7.000000000f,
        8.000000000f,
        9.000000000f,
    ];

    [Fact]
    public void MorseEnvelopeMatchesLegacyVector()
    {
        var keyer = new MorseKeyer(sampleRate: 11_025, blockSize: 512);
        keyer.SetWordsPerMinute(30);

        string morse = MorseKeyer.Encode("CQ TEST");
        float[] envelope = keyer.CreateEnvelope(morse);

        Assert.Equal("-.-. --.-  - . ... -~", morse);
        Assert.Equal(26_624, envelope.Length);
        Assert.Equal(26_163, keyer.TrueEnvelopeLength);
        for (int index = 0; index < ExpectedEnvelope.Length; index++)
        {
            Assert.Equal(
                ExpectedEnvelope[index],
                envelope[index * 32],
                tolerance: 0.000001f);
        }
    }

    [Fact]
    public void DownMixerMatchesLegacyVector()
    {
        float[] input = [1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f];
        var output = new ComplexSample[input.Length];
        var mixer = new DownMixer
        {
            SampleRate = 8_000,
            Frequency = 1_000,
        };

        mixer.Mix(input, output);

        for (int index = 0; index < ExpectedDownmix.Length; index++)
        {
            Assert.Equal(
                ExpectedDownmix[index].Real,
                output[index].Real,
                tolerance: 0.000001f);
            Assert.Equal(
                ExpectedDownmix[index].Imaginary,
                output[index].Imaginary,
                tolerance: 0.000001f);
        }
    }

    [Fact]
    public void QuickAverageMatchesLegacyVector()
    {
        var average = new QuickAverage
        {
            Points = 4,
            Passes = 2,
        };

        for (int index = 0; index < ExpectedQuickAverage.Length; index++)
        {
            Assert.Equal(
                ExpectedQuickAverage[index],
                average.Filter(index + 1),
                tolerance: 0.000001f);
        }
    }

    [Fact]
    public void ToneRendererIsBlockStableAndDeterministic()
    {
        var first = new MorseToneRenderer(11_025, 512);
        var second = new MorseToneRenderer(11_025, 512);
        first.LoadMessage("CQ TEST");
        second.LoadMessage("CQ TEST");
        var firstOutput = new float[1_024];
        var secondOutput = new float[1_024];

        first.Render(firstOutput.AsSpan(0, 512));
        first.Render(firstOutput.AsSpan(512, 512));
        second.Render(secondOutput);

        Assert.Equal(firstOutput, secondOutput);
        Assert.Equal(
            "09BF83C236AE4833FE7808A21E8D641ED5F11529C8979E2F6735B2B75E2C7853",
            Convert.ToHexString(
                SHA256.HashData(
                    System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                        firstOutput.AsSpan()))));
    }

    [Fact]
    public void ToneRendererMeetsTheCompatibilityBlockBudget()
    {
        var renderer = new MorseToneRenderer(11_025, 512);
        renderer.LoadMessage(String.Concat(Enumerable.Repeat("CQ TEST ", 100)));
        var block = new float[512];
        var durations = new long[1_000];

        for (int index = 0; index < 8; index++)
        {
            renderer.Render(block);
        }

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < durations.Length; index++)
        {
            long started = Stopwatch.GetTimestamp();
            renderer.Render(block);
            durations[index] = Stopwatch.GetTimestamp() - started;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Array.Sort(durations);
        // A single sample may include an operating-system scheduler pause.
        // The p99 bound measures sustained callback safety without that noise.
        double p99Milliseconds = durations[989] * 1_000d / Stopwatch.Frequency;

        Assert.Equal(0, allocated);
        Assert.True(
            p99Milliseconds < 11.6d,
            $"p99 render duration was {p99Milliseconds:F3} ms.");
    }
}
