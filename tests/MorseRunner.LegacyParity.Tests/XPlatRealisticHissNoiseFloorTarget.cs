using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatRealisticHissNoiseFloorTarget : IParityTarget
{
    internal const string ParityId =
        "audio.realistic-hiss-noise-floor";
    internal const string FunctionalDivergenceCode =
        "audio-realistic-hiss-noise-floor-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Engine.MorseRunnerEngine"
        + "+MorseRunner.Engine.EngineSession"
        + "+MorseRunner.Dsp.LegacyReceiverPipeline"
        + "+MorseRunner.Engine.IAudioSink.WriteAsync";

    private static readonly ClientId ParityClient =
        new("realistic-hiss-noise-floor-parity");

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

        RealisticHissNoiseFloorInput input =
            RealisticHissNoiseFloorInput.Parse(scenario);
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
        RealisticHissNoiseFloorInput input,
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
                input.TotalBlocks),
            "advance",
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        SessionSnapshot snapshot = engine.GetSnapshot(handle.SessionId);
        if (snapshot.State != SessionState.Running
            || snapshot.SimulationBlock != input.TotalBlocks
            || snapshot.RenderedSamples
                != (long)input.TotalBlocks * input.BlockSize
            || snapshot.ActiveStations is not { Count: 0 })
        {
            throw new InvalidOperationException(
                "The XPlat noise-floor session did not remain a pure "
                + "receiver capture.");
        }

        CapturedAudioBlock[] blocks =
            sink.RequireCompleteCapture(input.TotalBlocks);
        return Normalize(input, blocks);
    }

    internal static string[] Normalize(
        RealisticHissNoiseFloorInput input,
        IReadOnlyList<CapturedAudioBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(blocks);
        EnsureLittleEndianSingleStorage();
        if (blocks.Count != input.TotalBlocks)
        {
            throw new InvalidDataException(
                "The noise-floor capture has an invalid block count.");
        }

        var values = new List<string>(
            RealisticHissNoiseFloorInput.ExpectedValueCount)
        {
            "configuration"
            + $"|sample-rate={Format(input.SampleRate)}"
            + $"|block-size={Format(input.BlockSize)}"
            + $"|seed={Format(input.Seed)}"
            + $"|bandwidth-hz={Format(input.BandwidthHz)}"
            + $"|pitch-hz={Format(input.PitchHz)}"
            + $"|total-blocks={Format(input.TotalBlocks)}"
            + "|probe-sample-indexes="
            + String.Join(
                ',',
                input.ProbeSampleIndexes.Select(Format))
            + "|ce-startup-requests-discarded=5"
            + "|normalization=ce-single-div-32768-clamp-unit",
        };
        var aggregate =
            new float[input.TotalBlocks * input.BlockSize];
        double squareSum = 0d;
        double peak = 0d;

        for (int blockIndex = 0;
             blockIndex < blocks.Count;
             blockIndex++)
        {
            CapturedAudioBlock block = blocks[blockIndex];
            if (block.SimulationBlock != blockIndex
                || block.Samples.Length != input.BlockSize)
            {
                throw new InvalidDataException(
                    $"Noise-floor block {blockIndex} has invalid "
                    + "framing.");
            }

            int aggregateOffset = blockIndex * input.BlockSize;
            block.Samples.CopyTo(
                aggregate.AsSpan(
                    aggregateOffset,
                    input.BlockSize));
            for (int sampleIndex = 0;
                 sampleIndex < block.Samples.Length;
                 sampleIndex++)
            {
                float sample = block.Samples[sampleIndex];
                if (!float.IsFinite(sample)
                    || sample is < -1f or > 1f)
                {
                    throw new InvalidDataException(
                        $"Noise-floor block {blockIndex} contains an "
                        + "invalid normalized sample.");
                }

                peak = Math.Max(
                    peak,
                    Math.Abs((double)sample));
                squareSum += (double)sample * sample;
            }

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
                $"block[{Format(blockIndex)}]"
                + $"|sample-count={Format(block.Samples.Length)}"
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
            != RealisticHissNoiseFloorInput.ExpectedValueCount)
        {
            throw new InvalidDataException(
                "The noise-floor capture emitted an invalid row count.");
        }

        return [.. values];
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
                $"The XPlat noise-floor {action} command was rejected: "
                + $"{result.ErrorCode ?? "(no error code)"}; "
                + $"{result.Message ?? "(no message)"}.");
        }
    }

    private static string ComputeRawSingleSha256(
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
