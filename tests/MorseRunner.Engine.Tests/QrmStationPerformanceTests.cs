using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Dsp;
using MorseRunner.Engine;

namespace MorseRunner.Engine.Tests;

[Collection(QrmPerformanceTestGroup.Name)]
public sealed class QrmStationPerformanceTests
{
    private const int RepresentativeConcurrentStationCount = 8;
    private const int WarmupBlockCount = 256;
    private const int MeasuredBlockCount = 4_096;
    private static readonly JsonSerializerOptions EvidenceJsonOptions =
        new()
        {
            WriteIndented = true,
        };

    [Fact]
    public void RepresentativeConcurrentBlockMeetsBudgetWithoutAllocation()
    {
        var harness = new QrmSteadyStateHarness(
            RepresentativeConcurrentStationCount);
        for (int block = 0; block < WarmupBlockCount; block++)
        {
            harness.RenderBlock();
        }

        var durations = new long[MeasuredBlockCount];
        _ = Stopwatch.GetTimestamp();
        _ = Stopwatch.Frequency;
        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        for (int block = 0; block < MeasuredBlockCount; block++)
        {
            long started = Stopwatch.GetTimestamp();
            harness.RenderBlock();
            durations[block] = Stopwatch.GetTimestamp() - started;
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
            RepresentativeConcurrentStationCount,
            harness.PeakActiveCount);
        Assert.Equal(
            RepresentativeConcurrentStationCount,
            harness.ActiveCount);
        Assert.True(harness.ActivationCount > 8);
        Assert.True(harness.ReleaseCount > 0);
        Assert.True(harness.RetryCount > 0);
        Assert.Equal(0, allocated);
#if !DEBUG
        Assert.True(
            p99Milliseconds < 11.6d,
            $"p99 representative QRM block duration was "
            + $"{p99Milliseconds:F3} ms.");
        Assert.True(
            normalMaximumMilliseconds < 23.2d,
            $"Normal maximum representative QRM block duration was "
            + $"{normalMaximumMilliseconds:F3} ms.");
#endif
        TestContext.Current.TestOutputHelper?.WriteLine(
            "audio.qrm-station-hot-path"
            + $"|allocated-bytes={allocated}"
            + $"|activations={harness.ActivationCount}"
            + $"|retries={harness.RetryCount}"
            + $"|releases={harness.ReleaseCount}"
            + $"|p99-ms={p99Milliseconds:F6}"
            + "|normal-maximum-definition=p99.9-nearest-rank"
            + $"|normal-maximum-ms={normalMaximumMilliseconds:F6}"
            + $"|raw-maximum-ms={rawMaximumMilliseconds:F6}");
        WriteEvidenceIfRequested(
            durations,
            harness,
            allocated,
            p99Milliseconds,
            normalMaximumMilliseconds,
            rawMaximumMilliseconds);
    }

    private static void WriteEvidenceIfRequested(
        long[] sortedDurationTicks,
        QrmSteadyStateHarness harness,
        long allocatedBytes,
        double p99Milliseconds,
        double normalMaximumMilliseconds,
        double rawMaximumMilliseconds)
    {
        const string evidencePathVariable =
            "MORSERUNNER_QRM_PERF_EVIDENCE_PATH";
        string? configuredPath =
            Environment.GetEnvironmentVariable(evidencePathVariable);
        if (String.IsNullOrWhiteSpace(configuredPath))
        {
            return;
        }

#if DEBUG
        throw new InvalidOperationException(
            "QRM performance evidence capture requires a Release build.");
#else
        string revision = RequireCaptureEnvironmentValue(
            "MORSERUNNER_QRM_PERF_SOURCE_REVISION");
        string tree = RequireCaptureEnvironmentValue(
            "MORSERUNNER_QRM_PERF_SOURCE_TREE");
        string clean = RequireCaptureEnvironmentValue(
            "MORSERUNNER_QRM_PERF_SOURCE_CLEAN");
        string cpu = RequireCaptureEnvironmentValue(
            "MORSERUNNER_QRM_PERF_CPU_DESCRIPTION");
        if (!StringComparer.OrdinalIgnoreCase.Equals(clean, "true"))
        {
            throw new InvalidOperationException(
                "QRM performance evidence capture requires an attested "
                + "clean source tree.");
        }

        bool passed = allocatedBytes == 0
            && p99Milliseconds < 11.6d
            && normalMaximumMilliseconds < 23.2d;
        if (!passed)
        {
            throw new InvalidOperationException(
                "Failing QRM performance measurements cannot be "
                + "captured as passing evidence.");
        }

        var evidence = new
        {
            schemaVersion = 1,
            scenarioId = "audio.qrm-station-hot-path",
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
                sampleRate = SimulationAudioProfile.SampleRate,
                blockSize = SimulationAudioProfile.BlockSize,
                seed = QrmSteadyStateHarness.Seed,
                contestId = "scWpx",
                runModeId = "rmStop",
                stationCall = "W7SST",
                structuralPoolBound = harness.StructuralPoolBound,
                concurrentStations =
                    RepresentativeConcurrentStationCount,
                warmupBlocks = WarmupBlockCount,
                measuredBlocks = MeasuredBlockCount,
                activationCount = harness.ActivationCount,
                retryCount = harness.RetryCount,
                releaseCount = harness.ReleaseCount,
            },
            measurements = new
            {
                allocatedBytesTotal = allocatedBytes,
                allocatedBytesPerBlock =
                    allocatedBytes / MeasuredBlockCount,
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
                "The QRM performance evidence path has no directory.");
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(
            fullPath,
            JsonSerializer.Serialize(evidence, EvidenceJsonOptions)
            + Environment.NewLine);
#endif
    }

    private static string RequireCaptureEnvironmentValue(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return String.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"Required QRM performance capture value '{name}' "
                + "is missing.")
            : value;
    }

    private sealed class QrmSteadyStateHarness
    {
        internal const int Seed = 77_019;

        private readonly ContestId _contestId = new("scWpx");
        private readonly RunModeId _runModeId = new("rmStop");
        private readonly StationReferenceCatalog _catalog;
        private readonly DeterministicRandom _random = new(Seed);
        private readonly RandomEffects _randomEffects;
        private readonly QrmStation[] _pool;
        private readonly List<QrmStation> _active;
        private readonly float[] _envelope =
            new float[SimulationAudioProfile.BlockSize];
        private readonly float[] _receiverReal =
            new float[SimulationAudioProfile.BlockSize];
        private readonly float[] _receiverImaginary =
            new float[SimulationAudioProfile.BlockSize];
        private float _ritPhase;

        public QrmSteadyStateHarness(int stationCount)
        {
            _catalog = StationReferenceCatalog.Load(
                _contestId,
                "W7SST");
            _randomEffects = new(_random);
            var profile = new MorseKeyingProfile(
                SimulationAudioProfile.SampleRate,
                SimulationAudioProfile.BlockSize,
                MorseKeyingMode.Standard);
            StructuralPoolBound =
                QrmStation.CalculateMaximumConcurrentStations(
                    profile,
                    _catalog,
                    _contestId,
                    "W7SST");
            _pool = new QrmStation[stationCount];
            _active = new List<QrmStation>(stationCount);
            for (int index = 0; index < _pool.Length; index++)
            {
                _pool[index] = new QrmStation(profile);
            }
        }

        public int ActiveCount => _active.Count;

        public int PeakActiveCount { get; private set; }

        public int ActivationCount { get; private set; }

        public int RetryCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public int StructuralPoolBound { get; }

        public void RenderBlock()
        {
            _receiverReal.AsSpan().Clear();
            _receiverImaginary.AsSpan().Clear();
            while (_active.Count < _active.Capacity)
            {
                QrmStation station = FindAvailableStation();
                station.Activate(
                    _random,
                    _randomEffects,
                    _catalog,
                    _contestId,
                    _runModeId,
                    "W7SST");
                _active.Add(station);
                ActivationCount++;
                PeakActiveCount = Math.Max(
                    PeakActiveCount,
                    _active.Count);
            }

            for (int index = 0; index < _active.Count; index++)
            {
                _active[index].MixNextBlock(
                    _envelope,
                    _receiverReal,
                    _receiverImaginary,
                    ritOffsetHz: 0,
                    _ritPhase);
            }

            _ritPhase = StationMixer.AdvanceRitPhase(
                _ritPhase,
                SimulationAudioProfile.BlockSize,
                ritHz: 0,
                SimulationAudioProfile.SampleRate);
            for (int index = _active.Count - 1; index >= 0; index--)
            {
                QrmStation station = _active[index];
                int transmissionsBefore = station.TransmissionCount;
                if (station.Tick(_randomEffects, _contestId))
                {
                    station.Release();
                    _active.RemoveAt(index);
                    ReleaseCount++;
                }
                else if (station.TransmissionCount > transmissionsBefore)
                {
                    RetryCount++;
                }
            }
        }

        private QrmStation FindAvailableStation()
        {
            for (int index = 0; index < _pool.Length; index++)
            {
                if (!_pool[index].IsActive)
                {
                    return _pool[index];
                }
            }

            throw new InvalidOperationException(
                "The representative QRM station pool was exhausted.");
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class QrmPerformanceTestGroup
{
    public const string Name = "QRM performance";
}
