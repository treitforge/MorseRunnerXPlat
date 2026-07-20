using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MorseRunner.Audio;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatStartupWarmupFilterTimingTarget : IParityTarget
{
    internal const string ParityId =
        "audio.startup-warmup-and-filter-timing-fresh-seed-12345";
    internal const string FunctionalDivergenceCode =
        "audio-startup-warmup-and-filter-timing-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Engine.MorseRunnerEngine"
        + "+MorseRunner.Engine.EngineSession"
        + "+MorseRunner.Dsp.LegacyReceiverPipeline"
        + "+MorseRunner.Engine.IAudioSink.InitializeAsync"
        + "+MorseRunner.Engine.IAudioSink.WriteAsync"
        + "+MorseRunner.Audio.PhysicalAudioSink.GetDiagnostics";

    private const string EmptySha256 =
        "e3b0c44298fc1c149afbf4c8996fb924"
        + "27ae41e4649b934ca495991b7852b855";

    private static readonly ClientId ParityClient =
        new("startup-warmup-filter-timing-parity");

    public async Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!StringComparer.Ordinal.Equals(scenario.Id, ParityId))
        {
            return new(
                ParityTargetOutcome.Failed,
                [],
                DomainErrorCodes.UnsupportedCapability,
                EvidenceSource);
        }

        StartupWarmupFilterTimingInput input =
            StartupWarmupFilterTimingInput.Parse(scenario);
        string[] values = await ObserveAsync(
            input,
            cancellationToken);
        bool matches = values.SequenceEqual(
            scenario.ExpectedValues,
            StringComparer.Ordinal);
        return new(
            matches
                ? ParityTargetOutcome.Passed
                : ParityTargetOutcome.Failed,
            values,
            matches ? null : FunctionalDivergenceCode,
            EvidenceSource);
    }

    internal static async Task<string[]> ObserveAsync(
        StartupWarmupFilterTimingInput input,
        CancellationToken cancellationToken)
    {
        await using var physicalSink = new PhysicalAudioSink();
        PhysicalAudioSinkDiagnostics physicalDiagnostics =
            physicalSink.GetDiagnostics();
        CapturedAudioBlock[] blocks = await CaptureAsync(
            input,
            cancellationToken);
        return Normalize(
            input,
            physicalDiagnostics.StartupFraming,
            blocks);
    }

    internal static async Task<CapturedAudioBlock[]> CaptureAsync(
        StartupWarmupFilterTimingInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureLittleEndianSingleStorage();

        var sink = new StrictCaptureAudioSink(
            input.SampleRate,
            input.BlockSize);
        await using var engine = new MorseRunnerEngine(
            _ => sink,
            new MorseRunnerEngineOptions
            {
                AutomaticTiming = false,
            });
        SessionSettings settings = new(
            input.Seed,
            new ContestId("scWpx"),
            new RunModeId("rmStop"),
            DurationBlocks: 0)
        {
            StationCall = "W7SST",
            WordsPerMinute = 30,
            PitchHz = input.PitchHz,
            BandwidthHz = input.BandwidthHz,
            Activity = 1,
            Qsk = false,
            Qsb = false,
            Qrm = false,
            Qrn = false,
            Flutter = false,
            Lids = false,
            MonitorLevelDb = 0d,
        };

        SessionHandle handle = await engine.CreateSessionAsync(
            settings,
            cancellationToken);
        sink.RequireInitializedWithoutAudio(handle.SessionId);

        await RequireAcceptedAsync(
            engine,
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                ParityClient),
            "start",
            cancellationToken);
        await RequireAcceptedAsync(
            engine,
            new SendOperatorIntentCommand(
                RequestId.New(),
                handle.SessionId,
                ParityClient,
                OperatorIntent.Abort,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty),
            "abort",
            cancellationToken);
        sink.RequireNoAudio("before explicit advance");

        await RequireAcceptedAsync(
            engine,
            new AdvanceSimulationCommand(
                RequestId.New(),
                handle.SessionId,
                ParityClient,
                input.FullBlockCount),
            "advance",
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        SessionSnapshot snapshot = engine.GetSnapshot(handle.SessionId);
        if (snapshot.State != SessionState.Running
            || snapshot.SimulationBlock != input.FullBlockCount
            || snapshot.RenderedSamples
                != (long)input.FullBlockCount * input.BlockSize
            || snapshot.ActiveStations is not { Count: 0 })
        {
            throw new InvalidOperationException(
                "The XPlat startup timing session did not remain a pure "
                + "receiver capture.");
        }

        return sink.RequireCompleteCapture(input.FullBlockCount);
    }

    internal static string[] Normalize(
        StartupWarmupFilterTimingInput input,
        IReadOnlyList<PhysicalAudioSinkStartupFrame> startupFraming,
        IReadOnlyList<CapturedAudioBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(startupFraming);
        ArgumentNullException.ThrowIfNull(blocks);
        EnsureLittleEndianSingleStorage();
        if (blocks.Count != input.FullBlockCount)
        {
            throw new InvalidDataException(
                "The startup timing capture has an invalid block count.");
        }

        ValidateStartupFraming(input, startupFraming);
        PhysicalAudioSinkStartupFrame[] prefillFrames =
            startupFraming
                .Where(frame => frame.IsSynchronousPrefill)
                .ToArray();
        int firstFullAbsoluteRequest = startupFraming.Count + 1;
        var values = new List<string>(
            StartupWarmupFilterTimingInput.ExpectedValueCount)
        {
            ConfigurationRow(input),
            PrefillRow(prefillFrames),
            "warmup|request-count="
                + Format(startupFraming.Count)
                + "|all-one-zero-single="
                + Format(
                    startupFraming.Count > 0
                    && startupFraming.All(IsOnePositiveZeroSingle))
                + "|first-full-absolute-request="
                + Format(firstFullAbsoluteRequest)
                + "|first-full-block-number="
                + Format(
                    startupFraming.Count == 0
                        ? 0
                        : firstFullAbsoluteRequest),
        };
        for (int requestIndex = 0;
             requestIndex < input.StartupRequestCount;
             requestIndex++)
        {
            values.Add(requestIndex < startupFraming.Count
                ? StartupRow(requestIndex, startupFraming[requestIndex])
                : AbsentStartupRow(requestIndex));
        }

        int startupSampleCount = startupFraming.Sum(
            frame => frame.Samples.Length);
        var aggregate = new float[checked(
            startupSampleCount
            + (input.FullBlockCount * input.BlockSize))];
        double squareSum = 0d;
        double peak = 0d;
        int aggregateOffset = 0;
        for (int requestIndex = 0;
             requestIndex < startupFraming.Count;
             requestIndex++)
        {
            PhysicalAudioSinkStartupFrame frame =
                startupFraming[requestIndex];
            ReadOnlySpan<float> samples = frame.Samples.AsSpan();
            samples.CopyTo(aggregate.AsSpan(aggregateOffset));
            AccumulateSamples(
                samples,
                $"Startup timing request {frame.LogicalRequestNumber}",
                ref squareSum,
                ref peak);
            aggregateOffset += samples.Length;
        }

        for (int blockIndex = 0;
             blockIndex < blocks.Count;
             blockIndex++)
        {
            CapturedAudioBlock block = blocks[blockIndex];
            if (block.SimulationBlock != blockIndex
                || block.Samples.Length != input.BlockSize)
            {
                throw new InvalidDataException(
                    $"Startup timing block {blockIndex} has invalid "
                    + "framing.");
            }

            block.Samples.CopyTo(
                aggregate.AsSpan(
                    aggregateOffset,
                    input.BlockSize));
            AccumulateSamples(
                block.Samples,
                $"Startup timing block {blockIndex}",
                ref squareSum,
                ref peak);
            aggregateOffset += block.Samples.Length;

            int absoluteRequest =
                input.StartupRequestCount + blockIndex + 1;
            bool swapAfter = absoluteRequest % 10 == 0;
            string probeBits = String.Join(
                ',',
                input.ProbeSampleIndexes.Select(
                    sampleIndex => BitConverter
                        .SingleToUInt32Bits(
                            block.Samples[sampleIndex])
                        .ToString(
                            "x8",
                            CultureInfo.InvariantCulture)));
            values.Add(
                $"full-block[{Format(blockIndex)}]"
                + $"|absolute-block={Format(absoluteRequest)}"
                + $"|absolute-request={Format(absoluteRequest)}"
                + $"|sample-count={Format(block.Samples.Length)}"
                + $"|swap-after={Format(swapAfter)}"
                + $"|probe-bits={probeBits}"
                + "|float-sha256="
                + ComputeRawSingleSha256(block.Samples));
        }

        double rms = Math.Sqrt(squareSum / aggregate.Length);
        values.Add(
            "aggregate"
            + $"|sample-count={Format(aggregate.Length)}"
            + $"|peak={Format(peak)}"
            + $"|rms={Format(rms)}"
            + "|float-sha256="
            + ComputeRawSingleSha256(aggregate));
        if (values.Count
            != StartupWarmupFilterTimingInput.ExpectedValueCount)
        {
            throw new InvalidDataException(
                "The startup timing capture emitted an invalid row "
                + "count.");
        }

        return [.. values];
    }

    private static void ValidateStartupFraming(
        StartupWarmupFilterTimingInput input,
        IReadOnlyList<PhysicalAudioSinkStartupFrame> startupFraming)
    {
        if (startupFraming.Count > input.StartupRequestCount)
        {
            throw new InvalidDataException(
                "The physical startup framing has too many requests.");
        }

        bool completionDrivenRequestSeen = false;
        for (int index = 0; index < startupFraming.Count; index++)
        {
            PhysicalAudioSinkStartupFrame frame = startupFraming[index];
            if (frame.LogicalRequestNumber != index + 1
                || frame.Samples.IsDefault)
            {
                throw new InvalidDataException(
                    "The physical startup framing has an invalid "
                    + "logical request sequence.");
            }

            if (!frame.IsSynchronousPrefill)
            {
                completionDrivenRequestSeen = true;
            }
            else if (completionDrivenRequestSeen)
            {
                throw new InvalidDataException(
                    "A synchronous physical prefill request follows a "
                    + "completion-driven startup request.");
            }
        }
    }

    private static string PrefillRow(
        PhysicalAudioSinkStartupFrame[] prefillFrames)
    {
        if (prefillFrames.Length == 0)
        {
            return "prefill|request-count=0|absolute-requests=none"
                + "|sample-counts=none";
        }

        string absoluteRequests = prefillFrames.Length == 1
            ? Format(prefillFrames[0].LogicalRequestNumber)
            : Format(prefillFrames[0].LogicalRequestNumber)
                + "-"
                + Format(prefillFrames[^1].LogicalRequestNumber);
        return "prefill|request-count="
            + Format(prefillFrames.Length)
            + "|absolute-requests="
            + absoluteRequests
            + "|sample-counts="
            + String.Join(
                ',',
                prefillFrames.Select(
                    frame => Format(frame.Samples.Length)));
    }

    private static bool IsOnePositiveZeroSingle(
        PhysicalAudioSinkStartupFrame frame)
    {
        return frame.Samples.Length == 1
            && BitConverter.SingleToUInt32Bits(frame.Samples[0]) == 0;
    }

    private static string StartupRow(
        int requestIndex,
        PhysicalAudioSinkStartupFrame frame)
    {
        return $"startup[{Format(requestIndex)}]"
            + "|absolute-request="
            + Format(frame.LogicalRequestNumber)
            + "|sample-count="
            + Format(frame.Samples.Length)
            + "|bits="
            + (frame.Samples.Length == 0
                ? "none"
                : String.Join(
                    ',',
                    frame.Samples.Select(
                        sample => BitConverter
                            .SingleToUInt32Bits(sample)
                            .ToString(
                                "x8",
                                CultureInfo.InvariantCulture))))
            + "|float-sha256="
            + ComputeRawSingleSha256(frame.Samples.AsSpan());
    }

    private static string AbsentStartupRow(int requestIndex)
    {
        return $"startup[{Format(requestIndex)}]"
            + "|absolute-request=absent"
            + "|sample-count=0"
            + "|bits=absent"
            + $"|float-sha256={EmptySha256}";
    }

    private static void AccumulateSamples(
        ReadOnlySpan<float> samples,
        string source,
        ref double squareSum,
        ref double peak)
    {
        for (int sampleIndex = 0;
             sampleIndex < samples.Length;
             sampleIndex++)
        {
            float sample = samples[sampleIndex];
            if (!float.IsFinite(sample)
                || sample is < -1f or > 1f)
            {
                throw new InvalidDataException(
                    source + " contains an invalid normalized sample.");
            }

            peak = Math.Max(
                peak,
                Math.Abs((double)sample));
            squareSum += (double)sample * sample;
        }
    }

    private static string ConfigurationRow(
        StartupWarmupFilterTimingInput input)
    {
        return "configuration"
            + $"|sample-rate={Format(input.SampleRate)}"
            + $"|block-size={Format(input.BlockSize)}"
            + $"|seed={Format(input.Seed)}"
            + $"|bandwidth-hz={Format(input.BandwidthHz)}"
            + $"|pitch-hz={Format(input.PitchHz)}"
            + "|prefill-request-count="
            + Format(input.PrefillRequestCount)
            + "|startup-request-count="
            + Format(input.StartupRequestCount)
            + $"|full-block-count={Format(input.FullBlockCount)}"
            + "|first-full-absolute-request="
            + Format(input.StartupRequestCount + 1)
            + "|probe-sample-indexes="
            + String.Join(
                ',',
                input.ProbeSampleIndexes.Select(Format))
            + "|seed-reset-after-startup=false"
            + "|normalization=ce-single-div-32768-clamp-unit";
    }

    private static async Task RequireAcceptedAsync(
        MorseRunnerEngine engine,
        SessionCommand command,
        string action,
        CancellationToken cancellationToken)
    {
        CommandResult result = await engine.ExecuteAsync(
            command,
            cancellationToken);
        if (!result.Accepted
            || result.ErrorCode is not null
            || result.Message is not null)
        {
            throw new InvalidOperationException(
                $"The XPlat startup timing {action} command was "
                + "rejected: "
                + $"{result.ErrorCode ?? "(no error code)"}; "
                + $"{result.Message ?? "(no message)"}.");
        }
    }

    internal static string ComputeRawSingleSha256(
        ReadOnlySpan<float> samples)
    {
        return Convert.ToHexStringLower(
            SHA256.HashData(MemoryMarshal.AsBytes(samples)));
    }

    private static string Format(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Format(double value)
    {
        return value.ToString(
            "F9",
            CultureInfo.InvariantCulture);
    }

    private static string Format(bool value)
    {
        return value ? "true" : "false";
    }

    private static void EnsureLittleEndianSingleStorage()
    {
        if (!BitConverter.IsLittleEndian
            || Marshal.SizeOf<float>() != sizeof(uint))
        {
            throw new PlatformNotSupportedException(
                "CE raw Single parity hashing requires 32-bit "
                + "little-endian sample storage.");
        }
    }

    internal sealed record CapturedAudioBlock(
        long SimulationBlock,
        float[] Samples);

    private sealed class StrictCaptureAudioSink : IAudioSink
    {
        private readonly int _expectedSampleRate;
        private readonly int _expectedBlockSize;
        private readonly List<CapturedAudioBlock> _blocks = [];
        private SessionId? _sessionId;
        private bool _initialized;
        private bool _completed;
        private bool _disposed;

        public StrictCaptureAudioSink(
            int expectedSampleRate,
            int expectedBlockSize)
        {
            _expectedSampleRate = expectedSampleRate;
            _expectedBlockSize = expectedBlockSize;
        }

        public ValueTask InitializeAsync(
            SessionId sessionId,
            AudioStreamFormat format,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_initialized || _disposed)
            {
                throw new InvalidOperationException(
                    "The parity audio sink was initialized more than "
                    + "once or after disposal.");
            }

            if (format.SampleRate != _expectedSampleRate
                || format.Channels != 1
                || format.BlockSize != _expectedBlockSize)
            {
                throw new InvalidOperationException(
                    "The parity audio sink received an unexpected "
                    + "stream format.");
            }

            _sessionId = sessionId;
            _initialized = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteAsync(
            ReadOnlyMemory<float> samples,
            long simulationBlock,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_initialized || _completed || _disposed)
            {
                throw new InvalidOperationException(
                    "The parity audio sink received audio outside its "
                    + "active lifetime.");
            }

            if (simulationBlock != _blocks.Count
                || samples.Length != _expectedBlockSize)
            {
                throw new InvalidOperationException(
                    "The parity audio sink received invalid block "
                    + "framing.");
            }

            _blocks.Add(
                new(
                    simulationBlock,
                    samples.ToArray()));
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_initialized || _completed || _disposed)
            {
                throw new InvalidOperationException(
                    "The parity audio sink was completed outside its "
                    + "active lifetime.");
            }

            _completed = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            return ValueTask.CompletedTask;
        }

        public void RequireInitializedWithoutAudio(
            SessionId sessionId)
        {
            if (!_initialized
                || _sessionId != sessionId
                || _completed
                || _disposed
                || _blocks.Count != 0)
            {
                throw new InvalidOperationException(
                    "The parity audio sink was not cleanly initialized.");
            }
        }

        public void RequireNoAudio(string phase)
        {
            if (_blocks.Count != 0)
            {
                throw new InvalidOperationException(
                    $"The parity audio sink received audio {phase}.");
            }
        }

        public CapturedAudioBlock[] RequireCompleteCapture(
            int expectedBlockCount)
        {
            if (!_initialized
                || _completed
                || _disposed
                || _blocks.Count != expectedBlockCount)
            {
                throw new InvalidOperationException(
                    "The parity audio sink did not capture the exact "
                    + "requested block sequence.");
            }

            return [.. _blocks];
        }
    }
}
