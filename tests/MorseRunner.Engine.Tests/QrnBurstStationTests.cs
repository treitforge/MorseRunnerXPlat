using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Dsp;
using MorseRunner.Engine;

namespace MorseRunner.Engine.Tests;

[Collection(QrnPerformanceTestGroup.Name)]
public sealed class QrnBurstStationTests
{
    private const int BlockSize = CompatibilityProfile.BlockSize;
    private const int PerformanceWarmupBlockCount = 256;
    private const int PerformanceMeasuredBlockCount = 4_096;
    private static readonly JsonSerializerOptions
        PerformanceEvidenceJsonOptions = new()
        {
            WriteIndented = true,
        };

    [Fact]
    public void Seed1903MatchesTheCeEagerEnvelopeAndDrawOrder()
    {
        var random = new LegacyRandom(1_903);
        var noiseGenerator = new LegacyReceiverNoiseGenerator(random);
        var noiseReal = new float[BlockSize];
        var noiseImaginary = new float[BlockSize];

        Assert.True(
            noiseGenerator.PrepareInput(
                noiseReal,
                noiseImaginary,
                qrnEnabled: true));

        var station = new QrnBurstStation();
        station.Activate(random);

        Assert.True(station.IsActive);
        Assert.True(station.IsSending);
        Assert.False(station.HasRenderedEnvelope);
        Assert.Equal(2, station.DurationBlocks);
        Assert.Equal(1_024, station.EnvelopeSampleCount);
        Assert.Equal(
            0x48E9_76CFU,
            BitConverter.SingleToUInt32Bits(station.Amplitude));
        Assert.Equal(
            0x3F58_CE2DU,
            BitConverter.SingleToUInt32Bits(random.NextSingle()));

        var firstReal = new float[BlockSize];
        var firstImaginary = new float[BlockSize];
        Array.Fill(firstImaginary, -123.25f);
        station.MixNextBlock(firstReal, firstImaginary);

        AssertBurstBlock(firstReal, firstImaginary, envelopeOffset: 0);
        Assert.True(station.IsActive);
        Assert.True(station.IsSending);
        Assert.False(station.HasRenderedEnvelope);
        Assert.Equal(1_024, station.EnvelopeSampleCount);

        var secondReal = new float[BlockSize];
        var secondImaginary = new float[BlockSize];
        Array.Fill(secondImaginary, -123.25f);
        station.MixNextBlock(secondReal, secondImaginary);

        AssertBurstBlock(
            secondReal,
            secondImaginary,
            envelopeOffset: BlockSize);
        Assert.True(station.IsActive);
        Assert.True(station.IsSending);
        Assert.True(station.HasRenderedEnvelope);
        Assert.Equal(1_024, station.EnvelopeSampleCount);

        station.Release();

        Assert.False(station.IsActive);
        Assert.False(station.IsSending);
        Assert.False(station.HasRenderedEnvelope);
        Assert.Equal(0, station.DurationBlocks);
        Assert.Equal(0, station.EnvelopeSampleCount);
        Assert.Equal(0f, station.Amplitude);
    }

    [Fact]
    public void ZeroDurationStillConsumesAmplitudeAndCompletesOneBlock()
    {
        var random = new LegacyRandom(9);
        var station = new QrnBurstStation();
        station.Activate(random);

        Assert.True(station.IsActive);
        Assert.True(station.IsSending);
        Assert.False(station.HasRenderedEnvelope);
        Assert.Equal(0, station.DurationBlocks);
        Assert.Equal(0, station.EnvelopeSampleCount);
        Assert.Equal(
            0x4902_C948U,
            BitConverter.SingleToUInt32Bits(station.Amplitude));
        Assert.Equal(
            0x3F00_7ADAU,
            BitConverter.SingleToUInt32Bits(random.NextSingle()));

        var real = new float[BlockSize];
        var imaginary = new float[BlockSize];
        Array.Fill(real, 17.5f);
        Array.Fill(imaginary, -9.25f);
        station.MixNextBlock(real, imaginary);

        Assert.All(real, sample => Assert.Equal(17.5f, sample));
        Assert.All(imaginary, sample => Assert.Equal(-9.25f, sample));
        Assert.True(station.IsActive);
        Assert.True(station.IsSending);
        Assert.True(station.HasRenderedEnvelope);

        station.Release();
        Assert.False(station.IsActive);
    }

    [Fact]
    public void MaximumDurationUsesExactlyTwentyTwoBlocks()
    {
        var station = new QrnBurstStation();
        station.Activate(new LegacyRandom(1_989));
        var real = new float[BlockSize];
        var imaginary = new float[BlockSize];

        Assert.Equal(22, QrnBurstStation.MaximumConcurrentStations);
        Assert.Equal(22, station.DurationBlocks);
        Assert.Equal(22 * BlockSize, station.EnvelopeSampleCount);

        for (int block = 0; block < 21; block++)
        {
            station.MixNextBlock(real, imaginary);
            Assert.True(station.IsSending);
            Assert.False(station.HasRenderedEnvelope);
        }

        station.MixNextBlock(real, imaginary);

        Assert.True(station.IsSending);
        Assert.True(station.HasRenderedEnvelope);
        station.Release();
        Assert.False(station.IsSending);
    }

    [Fact]
    public void DurationConversionUsesCeBinary64ArithmeticAfterSingleRounding()
    {
        const int seed = 3_235_522;
        var durationRandom = new LegacyRandom(seed);
        Assert.Equal(
            0x3E82_C658U,
            BitConverter.SingleToUInt32Bits(
                durationRandom.NextSingle()));

        var station = new QrnBurstStation();
        station.Activate(new LegacyRandom(seed));
        var real = new float[BlockSize];
        var imaginary = new float[BlockSize];

        Assert.Equal(5, station.DurationBlocks);
        Assert.Equal(5 * BlockSize, station.EnvelopeSampleCount);
        for (int block = 0; block < station.DurationBlocks; block++)
        {
            station.MixNextBlock(real, imaginary);
        }

        Assert.True(station.HasRenderedEnvelope);
        station.Release();
    }

    [Fact]
    public void ReuseClearsEverySampleInTheNewActiveEnvelope()
    {
        var station = new QrnBurstStation();
        var real = new float[BlockSize];
        var imaginary = new float[BlockSize];

        station.Activate(new LegacyRandom(21));
        Assert.Equal(1, station.DurationBlocks);
        station.MixNextBlock(real, imaginary);
        Assert.Contains(real, sample => sample != 0f);
        station.Release();

        Array.Clear(real);
        station.Activate(new LegacyRandom(3_914));
        Assert.Equal(1, station.DurationBlocks);
        station.MixNextBlock(real, imaginary);

        Assert.All(
            real,
            sample => Assert.Equal(
                0U,
                BitConverter.SingleToUInt32Bits(sample)));
        Assert.True(station.HasRenderedEnvelope);
        station.Release();
    }

    [Fact]
    public void MixRequiresCompleteCompatibilityBuffers()
    {
        var station = new QrnBurstStation();
        station.Activate(new LegacyRandom(21));
        var real = new float[BlockSize];
        var imaginary = new float[BlockSize];
        var shortBuffer = new float[BlockSize - 1];

        Assert.Throws<ArgumentException>(
            () => station.MixNextBlock(shortBuffer, imaginary));
        Assert.Throws<ArgumentException>(
            () => station.MixNextBlock(real, shortBuffer));

        station.MixNextBlock(real, imaginary);
        Assert.True(station.HasRenderedEnvelope);
        station.Release();
    }

    [Fact]
    public void ReusedMaximumBurstsMeetTheBlockBudgetWithoutAllocation()
    {
        const int warmupCount = 128;
        const int iterationCount = 128;
        var warmup = new QrnBurstStation();
        var warmupReal = new float[BlockSize];
        var warmupImaginary = new float[BlockSize];
        for (int iteration = 0; iteration < warmupCount; iteration++)
        {
            warmup.Activate(new LegacyRandom(1_989));
            while (!warmup.HasRenderedEnvelope)
            {
                warmup.MixNextBlock(
                    warmupReal,
                    warmupImaginary);
            }

            warmup.Release();
        }

        var station = new QrnBurstStation();
        var real = new float[BlockSize];
        var imaginary = new float[BlockSize];
        var randomSources = new LegacyRandom[iterationCount];
        for (int index = 0; index < randomSources.Length; index++)
        {
            randomSources[index] = new LegacyRandom(1_989);
        }

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0;
             iteration < iterationCount;
             iteration++)
        {
            station.Activate(randomSources[iteration]);
            while (!station.HasRenderedEnvelope)
            {
                station.MixNextBlock(real, imaginary);
            }

            station.Release();
        }

        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(0, allocated);
        Assert.False(station.IsActive);
    }

    [Fact]
    public void WorstCaseConcurrentBurstBlockMeetsTheRenderBudget()
    {
        var randomSources = new LegacyRandom[
            PerformanceWarmupBlockCount
            + PerformanceMeasuredBlockCount];
        for (int index = 0; index < randomSources.Length; index++)
        {
            randomSources[index] = new LegacyRandom(1_989);
        }

        var harness = new QrnWorstCaseBlockHarness();
        for (int block = 0;
             block < PerformanceWarmupBlockCount;
             block++)
        {
            harness.RenderBlock(randomSources[block]);
        }

        var durations = new long[PerformanceMeasuredBlockCount];
        _ = Stopwatch.GetTimestamp();
        _ = Stopwatch.Frequency;
        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        for (int block = 0;
             block < PerformanceMeasuredBlockCount;
             block++)
        {
            long started = Stopwatch.GetTimestamp();
            harness.RenderBlock(
                randomSources[
                    PerformanceWarmupBlockCount + block]);
            durations[block] =
                Stopwatch.GetTimestamp() - started;
        }

        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Array.Sort(durations);
        double p99Milliseconds =
            durations[4_055] * 1_000d / Stopwatch.Frequency;
        double normalMaximumMilliseconds =
            durations[4_091] * 1_000d / Stopwatch.Frequency;
        double rawMaximumMilliseconds =
            durations[^1] * 1_000d / Stopwatch.Frequency;

        Assert.Equal(
            QrnBurstStation.MaximumConcurrentStations,
            harness.PeakActiveCount);
        Assert.Equal(
            QrnBurstStation.MaximumConcurrentStations - 1,
            harness.ActiveCount);
        Assert.Equal(0, allocated);
#if !DEBUG
        Assert.True(
            p99Milliseconds < 11.6d,
            $"p99 worst-case QRN block duration was "
            + $"{p99Milliseconds:F3} ms.");
        Assert.True(
            normalMaximumMilliseconds < 23.2d,
            $"Normal maximum worst-case QRN block duration was "
            + $"{normalMaximumMilliseconds:F3} ms.");
#endif
        TestContext.Current.TestOutputHelper?.WriteLine(
            "audio.qrn-burst-hot-path"
            + $"|allocated-bytes={allocated}"
            + $"|p99-ms={p99Milliseconds:F6}"
            + "|normal-maximum-definition=p99.9-nearest-rank"
            + $"|normal-maximum-ms={normalMaximumMilliseconds:F6}"
            + $"|raw-maximum-ms={rawMaximumMilliseconds:F6}");
        WritePerformanceEvidenceIfRequested(
            durations,
            allocated,
            p99Milliseconds,
            normalMaximumMilliseconds,
            rawMaximumMilliseconds);
    }

    private static void WritePerformanceEvidenceIfRequested(
        long[] sortedDurationTicks,
        long allocatedBytes,
        double p99Milliseconds,
        double normalMaximumMilliseconds,
        double rawMaximumMilliseconds)
    {
        const string evidencePathVariable =
            "MORSERUNNER_QRN_PERF_EVIDENCE_PATH";
        string? configuredPath =
            Environment.GetEnvironmentVariable(evidencePathVariable);
        if (String.IsNullOrWhiteSpace(configuredPath))
        {
            return;
        }

#if DEBUG
        throw new InvalidOperationException(
            "QRN performance evidence capture requires a Release build.");
#else
        string revision = RequireCaptureEnvironmentValue(
            "MORSERUNNER_QRN_PERF_SOURCE_REVISION");
        string tree = RequireCaptureEnvironmentValue(
            "MORSERUNNER_QRN_PERF_SOURCE_TREE");
        string clean = RequireCaptureEnvironmentValue(
            "MORSERUNNER_QRN_PERF_SOURCE_CLEAN");
        string cpu = RequireCaptureEnvironmentValue(
            "MORSERUNNER_QRN_PERF_CPU_DESCRIPTION");
        if (!StringComparer.OrdinalIgnoreCase.Equals(clean, "true"))
        {
            throw new InvalidOperationException(
                "QRN performance evidence capture requires an attested "
                + "clean source tree.");
        }

        bool passed = allocatedBytes == 0
            && p99Milliseconds < 11.6d
            && normalMaximumMilliseconds < 23.2d;
        if (!passed)
        {
            throw new InvalidOperationException(
                "Failing QRN performance measurements cannot be "
                + "captured as passing evidence.");
        }

        var evidence = new
        {
            schemaVersion = 1,
            scenarioId = "audio.qrn-burst-hot-path",
            capturedAtUtc = DateTimeOffset.UtcNow,
            source = new
            {
                revision,
                tree,
                clean = true,
            },
            environment = new
            {
                osDescription = RuntimeInformation.OSDescription,
                processArchitecture =
                    RuntimeInformation.ProcessArchitecture.ToString(),
                runtimeIdentifier = RuntimeInformation.RuntimeIdentifier,
                frameworkDescription =
                    RuntimeInformation.FrameworkDescription,
                configuration = "Release",
                cpu,
                processorCount = Environment.ProcessorCount,
                stopwatchFrequency = Stopwatch.Frequency,
            },
            scenario = new
            {
                sampleRate = CompatibilityProfile.SampleRate,
                blockSize = CompatibilityProfile.BlockSize,
                seed = 1_989,
                durationBlocks =
                    QrnBurstStation.MaximumConcurrentStations,
                maximumConcurrentBursts =
                    QrnBurstStation.MaximumConcurrentStations,
                warmupBlocks = PerformanceWarmupBlockCount,
                measuredBlocks = PerformanceMeasuredBlockCount,
            },
            measurements = new
            {
                allocatedBytesTotal = allocatedBytes,
                allocatedBytesPerBlock =
                    allocatedBytes / PerformanceMeasuredBlockCount,
                p99NearestRankIndex = 4_055,
                p99Milliseconds,
                normalMaximumDefinition =
                    "p99.9-nearest-rank",
                normalMaximumIndex = 4_091,
                normalMaximumMilliseconds,
                rawMaximumMilliseconds,
                schedulerTailSampleCount = 4,
                sortedDurationTicks,
            },
            gates = new
            {
                p99MaximumMilliseconds = 11.6d,
                normalMaximumMilliseconds = 23.2d,
                allocatedBytesPerBlock = 0,
                passed,
            },
        };
        string fullPath = Path.GetFullPath(configuredPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (String.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "The QRN performance evidence path has no directory.");
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(
            fullPath,
            JsonSerializer.Serialize(
                evidence,
                PerformanceEvidenceJsonOptions)
            + Environment.NewLine);
#endif
    }

    private static string RequireCaptureEnvironmentValue(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return String.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"Required QRN performance capture value '{name}' "
                + "is missing.")
            : value;
    }

    private sealed class QrnWorstCaseBlockHarness
    {
        private readonly QrnBurstStation[] _pool =
            new QrnBurstStation[
                QrnBurstStation.MaximumConcurrentStations];
        private readonly List<QrnBurstStation> _active =
            new(QrnBurstStation.MaximumConcurrentStations);
        private readonly float[] _receiverReal = new float[BlockSize];
        private readonly float[] _receiverImaginary =
            new float[BlockSize];

        public QrnWorstCaseBlockHarness()
        {
            for (int index = 0; index < _pool.Length; index++)
            {
                _pool[index] = new QrnBurstStation();
            }
        }

        public int ActiveCount => _active.Count;

        public int PeakActiveCount { get; private set; }

        public void RenderBlock(LegacyRandom random)
        {
            _receiverReal.AsSpan().Clear();
            _receiverImaginary.AsSpan().Clear();

            QrnBurstStation? available = null;
            for (int index = 0; index < _pool.Length; index++)
            {
                if (!_pool[index].IsActive)
                {
                    available = _pool[index];
                    break;
                }
            }

            if (available is null
                || _active.Count == _active.Capacity)
            {
                throw new InvalidOperationException(
                    "The worst-case QRN pool capacity was exhausted.");
            }

            available.Activate(random);
            _active.Add(available);
            PeakActiveCount = Math.Max(
                PeakActiveCount,
                _active.Count);

            for (int index = 0; index < _active.Count; index++)
            {
                _active[index].MixNextBlock(
                    _receiverReal,
                    _receiverImaginary);
            }

            for (int index = _active.Count - 1; index >= 0; index--)
            {
                if (!_active[index].HasRenderedEnvelope)
                {
                    continue;
                }

                _active[index].Release();
                _active.RemoveAt(index);
            }
        }
    }

    private static void AssertBurstBlock(
        float[] real,
        float[] imaginary,
        int envelopeOffset)
    {
        uint unchangedImaginaryBits =
            BitConverter.SingleToUInt32Bits(-123.25f);
        for (int index = 0; index < BlockSize; index++)
        {
            int envelopeIndex = envelopeOffset + index;
            uint expectedRealBits = envelopeIndex switch
            {
                359 => 0x47C4_6069U,
                411 => 0x4849_D6E3U,
                848 => 0xC7E5_B614U,
                907 => 0x476E_2C1CU,
                990 => 0xC75E_86F2U,
                _ => 0U,
            };
            Assert.Equal(
                expectedRealBits,
                BitConverter.SingleToUInt32Bits(real[index]));
            Assert.Equal(
                unchangedImaginaryBits,
                BitConverter.SingleToUInt32Bits(imaginary[index]));
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class QrnPerformanceTestGroup
{
    public const string Name = "QRN performance";
}
