using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MorseRunner.Dsp;

namespace MorseRunner.Dsp.Tests;

public sealed class LegacyMorseEnvelopeCursorTests
{
    private const int SampleRate = 11_025;
    private const int BlockSize = 512;
    private const int WordsPerMinute = 31;
    private const float QrmAmplitude = 19_583.306640625f;

    [Fact]
    public void StandardSegmentedQrmEnvelopeMatchesEagerCeEnvelope()
    {
        var profile = new LegacyMorseKeyingProfile(
            SampleRate,
            BlockSize,
            LegacyMorseKeyingMode.Standard);
        var cursor = new LegacyMorseEnvelopeCursor(profile);
        var text = new LegacyMorseText(
            "QRL?",
            " ",
            " ",
            " ",
            "QRL?");

        cursor.Reset(text, WordsPerMinute, QrmAmplitude);
        float[] actual = RenderAll(cursor);
        float[] expected = CreateEagerEnvelope(
            "QRL?   QRL?",
            sstFarnsworth: false,
            QrmAmplitude,
            WordsPerMinute);

        Assert.Equal(52_955, cursor.TrueEnvelopeSampleCount);
        Assert.Equal(53_248, cursor.PaddedEnvelopeSampleCount);
        Assert.Equal(104, cursor.SendPosition / BlockSize);
        Assert.Equal(expected, actual);
        Assert.Equal(
            "537ef6b868fb82bdc3883a6a93313a55"
            + "56895599bfbd3a4339c89e9203aee443",
            Hash(actual));
    }

    [Fact]
    public void SstSegmentedEncodingTransformsAcrossBoundaries()
    {
        var profile = new LegacyMorseKeyingProfile(
            SampleRate,
            BlockSize,
            LegacyMorseKeyingMode.SstFarnsworth);
        var cursor = new LegacyMorseEnvelopeCursor(profile);
        var text = new LegacyMorseText(
            "QRL?",
            " ",
            " ",
            " ",
            "QRL?");

        cursor.Reset(text, WordsPerMinute, QrmAmplitude);
        float[] actual = RenderAll(cursor);
        float[] expected = CreateEagerEnvelope(
            "QRL?   QRL?",
            sstFarnsworth: true,
            QrmAmplitude,
            WordsPerMinute);

        Assert.Equal(55_625, cursor.TrueEnvelopeSampleCount);
        Assert.Equal(55_808, cursor.PaddedEnvelopeSampleCount);
        Assert.Equal(expected, actual);
        Assert.Equal(
            "ec024d3a967500eeb655cd196de8bc15"
            + "ca0c932795afb0fcebfee928f95d6f02",
            Hash(actual));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(49)]
    public void CeQrmSpeedBoundsAreInclusive(int wordsPerMinute)
    {
        var profile = new LegacyMorseKeyingProfile(
            SampleRate,
            BlockSize,
            LegacyMorseKeyingMode.Standard);
        var cursor = new LegacyMorseEnvelopeCursor(profile);

        cursor.Reset(
            new LegacyMorseText("QRL?"),
            wordsPerMinute,
            QrmAmplitude);

        Assert.True(cursor.HasPendingAudio);
        Assert.Equal(wordsPerMinute, cursor.SendingWordsPerMinute);
        Assert.Equal(wordsPerMinute, cursor.CharacterWordsPerMinute);
    }

    [Fact]
    public void BlockCursorExposesCeSendPositionAndPaddedLifecycle()
    {
        var profile = new LegacyMorseKeyingProfile(
            SampleRate,
            BlockSize,
            LegacyMorseKeyingMode.Standard);
        var cursor = new LegacyMorseEnvelopeCursor(profile);
        var block = new float[BlockSize];
        cursor.Reset(
            new LegacyMorseText("QRL?   QRL?"),
            WordsPerMinute,
            QrmAmplitude);

        bool firstRendered = cursor.TryRenderNextBlock(block);

        Assert.True(firstRendered);
        Assert.Equal(BlockSize, cursor.SendPosition);
        Assert.Equal(103, cursor.RemainingBlockCount);
        while (cursor.TryRenderNextBlock(block))
        {
        }

        Assert.False(cursor.HasPendingAudio);
        Assert.Equal(
            cursor.PaddedEnvelopeSampleCount,
            cursor.SendPosition);
        Array.Fill(block, 1f);
        Assert.False(cursor.TryRenderNextBlock(block));
        Assert.All(block, sample => Assert.Equal(0f, sample));
    }

    [Theory]
    [InlineData(29)]
    [InlineData(50)]
    public void CeQrmSpeedBoundsRejectValuesOutsideConstructorRange(
        int wordsPerMinute)
    {
        var profile = new LegacyMorseKeyingProfile(
            SampleRate,
            BlockSize,
            LegacyMorseKeyingMode.Standard);
        var cursor = new LegacyMorseEnvelopeCursor(profile);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => cursor.Reset(
                new LegacyMorseText("QRL?"),
                wordsPerMinute,
                QrmAmplitude));
    }

    [Fact]
    public void FixedTextSupportsEightSegmentsWithoutImplicitSpacing()
    {
        var profile = new LegacyMorseKeyingProfile(
            SampleRate,
            BlockSize,
            LegacyMorseKeyingMode.Standard);
        var segmentedCursor =
            new LegacyMorseEnvelopeCursor(profile);
        var contiguousCursor =
            new LegacyMorseEnvelopeCursor(profile);
        var segmentedText = new LegacyMorseText(
            "C",
            "Q",
            " ",
            "T",
            "E",
            "S",
            "T",
            "?");

        segmentedCursor.Reset(
            segmentedText,
            WordsPerMinute,
            amplitude: 1f);
        contiguousCursor.Reset(
            new LegacyMorseText("CQ TEST?"),
            WordsPerMinute,
            amplitude: 1f);

        Assert.Equal(
            LegacyMorseText.MaximumSegmentCount,
            segmentedText.SegmentCount);
        Assert.Equal(
            RenderAll(contiguousCursor),
            RenderAll(segmentedCursor));
    }

    [Fact]
    public void SharedProfileOwnsOneRampPairForPooledCursors()
    {
        var profile = new LegacyMorseKeyingProfile(
            SampleRate,
            BlockSize,
            LegacyMorseKeyingMode.Standard);
        var first = new LegacyMorseEnvelopeCursor(profile);
        var second = new LegacyMorseEnvelopeCursor(profile);

        Assert.Same(profile, first.Profile);
        Assert.Same(profile, second.Profile);
        Assert.Equal(149, profile.RampLength);
    }

    [Fact]
    public void EveryQrmSpeedAndMessageShapeMatchesEagerCeKeying()
    {
        (LegacyMorseText Segments, string Materialized)[] messages =
        [
            (new("QRL?"), "QRL?"),
            (new("W7SST", "  QSY QSY"), "W7SST  QSY QSY"),
            (
                new(
                    "CQ CQ TEST ",
                    "LU5MT",
                    " ",
                    "LU5MT",
                    " TEST"),
                "CQ CQ TEST LU5MT LU5MT TEST"),
        ];

        foreach (LegacyMorseKeyingMode mode in
                 Enum.GetValues<LegacyMorseKeyingMode>())
        {
            var profile = new LegacyMorseKeyingProfile(
                SampleRate,
                BlockSize,
                mode);
            var cursor = new LegacyMorseEnvelopeCursor(profile);
            for (int wordsPerMinute =
                     LegacyMorseKeyingProfile
                         .MinimumQrmWordsPerMinute;
                 wordsPerMinute
                     <= LegacyMorseKeyingProfile
                         .MaximumQrmWordsPerMinute;
                 wordsPerMinute++)
            {
                foreach ((LegacyMorseText segments, string materialized)
                         in messages)
                {
                    cursor.Reset(
                        segments,
                        wordsPerMinute,
                        QrmAmplitude);

                    Assert.Equal(
                        CreateEagerEnvelope(
                            materialized,
                            mode
                                == LegacyMorseKeyingMode
                                    .SstFarnsworth,
                            QrmAmplitude,
                            wordsPerMinute),
                        RenderAll(cursor));
                }
            }
        }
    }

    [Fact]
    public void RampLengthUsesTheFpcDoubleExpression()
    {
        float riseTimeSeconds =
            BitConverter.Int32BitsToSingle(0x3b5f7643);
        int floatIntermediateLength = (int)MathF.Round(
            2.7f * riseTimeSeconds * SampleRate,
            MidpointRounding.ToEven);

        var profile = new LegacyMorseKeyingProfile(
            SampleRate,
            BlockSize,
            LegacyMorseKeyingMode.Standard,
            riseTimeSeconds);

        Assert.Equal(102, floatIntermediateLength);
        Assert.Equal(101, profile.RampLength);
    }

    [Fact]
    public void ResetAndBlockRenderingAllocateNoManagedMemory()
    {
        var profile = new LegacyMorseKeyingProfile(
            SampleRate,
            BlockSize,
            LegacyMorseKeyingMode.Standard);
        var cursor = new LegacyMorseEnvelopeCursor(profile);
        var text = new LegacyMorseText(
            "CQ CQ TEST ",
            "LU5MT",
            " ",
            "LU5MT",
            " TEST");
        var block = new float[BlockSize];

        RenderScenario(cursor, text, block);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 32; iteration++)
        {
            RenderScenario(cursor, text, block);
        }

        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    private static float[] RenderAll(
        LegacyMorseEnvelopeCursor cursor)
    {
        var result = new float[cursor.PaddedEnvelopeSampleCount];
        int position = 0;
        while (cursor.HasPendingAudio)
        {
            bool rendered = cursor.TryRenderNextBlock(
                result.AsSpan(position, BlockSize));
            Assert.True(rendered);
            position += BlockSize;
        }

        Assert.Equal(result.Length, position);
        return result;
    }

    private static float[] CreateEagerEnvelope(
        string message,
        bool sstFarnsworth,
        float amplitude,
        int wordsPerMinute)
    {
        var keyer = new MorseKeyer(SampleRate, BlockSize);
        keyer.SetWordsPerMinute(
            wordsPerMinute,
            sstFarnsworth ? wordsPerMinute : 0);
        string encoded = sstFarnsworth
            ? keyer.EncodeText(message)
            : MorseKeyer.Encode(message);
        float[] envelope = keyer.CreateEnvelope(encoded);
        for (int index = 0; index < envelope.Length; index++)
        {
            envelope[index] *= amplitude;
        }

        return envelope;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RenderScenario(
        LegacyMorseEnvelopeCursor cursor,
        LegacyMorseText text,
        float[] block)
    {
        cursor.Reset(text, WordsPerMinute, QrmAmplitude);
        while (cursor.TryRenderNextBlock(block))
        {
        }
    }

    private static string Hash(ReadOnlySpan<float> samples)
    {
        return Convert.ToHexStringLower(
            SHA256.HashData(MemoryMarshal.AsBytes(samples)));
    }
}
