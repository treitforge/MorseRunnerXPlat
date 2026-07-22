using System.Diagnostics;

namespace MorseRunner.Dsp.Tests;

public sealed class LegacyReceiverNoiseGeneratorTests
{
    private static readonly int[] ExpectedReplacementIndexes =
    [
        92,
        248,
        323,
        482,
        488,
        507,
    ];

    [Fact]
    public void QrnBackgroundMatchesCeReplacementAndDrawOrder()
    {
        var cleanRandom = new LegacyRandom(12_345);
        var qrnRandom = new LegacyRandom(12_345);
        var cleanGenerator =
            new LegacyReceiverNoiseGenerator(cleanRandom);
        var qrnGenerator =
            new LegacyReceiverNoiseGenerator(qrnRandom);
        var cleanReal = new float[512];
        var cleanImaginary = new float[512];
        var qrnReal = new float[512];
        var qrnImaginary = new float[512];

        bool cleanBurstTriggered = cleanGenerator.PrepareInput(
            cleanReal,
            cleanImaginary,
            qrnEnabled: false);
        bool qrnBurstTriggered = qrnGenerator.PrepareInput(
            qrnReal,
            qrnImaginary,
            qrnEnabled: true);

        Assert.False(cleanBurstTriggered);
        Assert.False(qrnBurstTriggered);
        Assert.Equal(cleanImaginary, qrnImaginary);
        Assert.Equal(
            ExpectedReplacementIndexes,
            Enumerable.Range(0, cleanReal.Length)
                .Where(index => cleanReal[index] != qrnReal[index]));
        Assert.Equal(
            [
                0x480F_72E0U,
                0x470A_2735U,
                0x4820_1E5AU,
                0xC7AE_5068U,
                0x4799_5390U,
                0xC819_E28DU,
            ],
            ExpectedReplacementIndexes.Select(
                index => BitConverter.SingleToUInt32Bits(
                    qrnReal[index])));
        Assert.Equal(
            0x3E32_0354U,
            BitConverter.SingleToUInt32Bits(
                cleanRandom.NextSingle()));
        Assert.Equal(
            0x3F43_412EU,
            BitConverter.SingleToUInt32Bits(
                qrnRandom.NextSingle()));
    }

    [Fact]
    public void ReceiverInputRequiresEqualComplexBufferLengths()
    {
        var generator =
            new LegacyReceiverNoiseGenerator(new LegacyRandom(12_345));

        Assert.Throws<ArgumentException>(
            () => generator.PrepareInput(
                new float[512],
                new float[511],
                qrnEnabled: true));
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void QrnBackgroundMeetsTheCompatibilityBlockBudget()
    {
        var generator =
            new LegacyReceiverNoiseGenerator(new LegacyRandom(12_345));
        var real = new float[512];
        var imaginary = new float[512];
        var durations = new long[1_000];

        for (int index = 0; index < 8; index++)
        {
            _ = generator.PrepareInput(
                real,
                imaginary,
                qrnEnabled: true);
        }

        _ = Stopwatch.GetTimestamp();
        _ = Stopwatch.Frequency;
        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < durations.Length; index++)
        {
            long started = Stopwatch.GetTimestamp();
            _ = generator.PrepareInput(
                real,
                imaginary,
                qrnEnabled: true);
            durations[index] = Stopwatch.GetTimestamp() - started;
        }

        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Array.Sort(durations);
        double p95Milliseconds =
            durations[949] * 1_000d / Stopwatch.Frequency;
        double blockPeriodMilliseconds =
            1_000d * 512d / 11_025d;

        Assert.Equal(0, allocated);
        Assert.True(
            p95Milliseconds < blockPeriodMilliseconds,
            $"p95 QRN input preparation was "
            + $"{p95Milliseconds:F3} ms.");
    }
}
