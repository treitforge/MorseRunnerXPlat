using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatQsbNoStationNoiseInvarianceTarget :
    IParityTarget
{
    internal const string ParityId =
        "audio.qsb-no-station-noise-invariance-seed-12345";
    internal const string FunctionalDivergenceCode =
        "audio-qsb-no-station-noise-invariance-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Engine.MorseRunnerEngine"
        + "+MorseRunner.Engine.EngineSession"
        + "+MorseRunner.Engine.IAudioSink.WriteAsync";

    private static readonly ClientId ParityClient =
        new("qsb-no-station-noise-invariance-parity");

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

        QsbNoStationNoiseInvarianceInput input =
            QsbNoStationNoiseInvarianceInput.Parse(scenario);
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
        QsbNoStationNoiseInvarianceInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureLittleEndianSingleStorage();

        CapturedAudioBlock[] clean = await CaptureAsync(
            input,
            qsbEnabled: false,
            cancellationToken);
        CapturedAudioBlock[] qsb = await CaptureAsync(
            input,
            qsbEnabled: true,
            cancellationToken);
        return Normalize(
            input,
            clean,
            qsb);
    }

    internal static async Task<CapturedAudioBlock[]> CaptureAsync(
        QsbNoStationNoiseInvarianceInput input,
        bool qsbEnabled,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

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
            Qsb = qsbEnabled,
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
                input.ComparedBlockCount),
            "advance",
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        SessionSnapshot snapshot = engine.GetSnapshot(handle.SessionId);
        if (snapshot.State != SessionState.Running
            || snapshot.SimulationBlock != input.ComparedBlockCount
            || snapshot.RenderedSamples
                != (long)input.ComparedBlockCount * input.BlockSize
            || snapshot.ActiveStations is not { Count: 0 })
        {
            throw new InvalidOperationException(
                "The XPlat QSB invariance session did not remain a "
                + "pure receiver capture.");
        }

        return sink.RequireCompleteCapture(
            input.ComparedBlockCount);
    }

    internal static string[] Normalize(
        QsbNoStationNoiseInvarianceInput input,
        IReadOnlyList<CapturedAudioBlock> clean,
        IReadOnlyList<CapturedAudioBlock> qsb)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(clean);
        ArgumentNullException.ThrowIfNull(qsb);
        EnsureLittleEndianSingleStorage();
        ValidateCapture(input, clean, "clean");
        ValidateCapture(input, qsb, "QSB");

        var values = new List<string>(
            QsbNoStationNoiseInvarianceInput.ExpectedValueCount)
        {
            "configuration"
            + $"|sample-rate={Format(input.SampleRate)}"
            + $"|block-size={Format(input.BlockSize)}"
            + $"|seed={Format(input.Seed)}"
            + $"|bandwidth-hz={Format(input.BandwidthHz)}"
            + $"|pitch-hz={Format(input.PitchHz)}"
            + "|startup-request-count="
            + Format(input.StartupRequestCount)
            + "|compared-block-count="
            + Format(input.ComparedBlockCount)
            + "|probe-sample-indexes="
            + String.Join(
                ',',
                input.ProbeSampleIndexes.Select(Format))
            + "|fresh-runs=clean,qsb"
            + "|station-count=0"
            + "|normalization=ce-single-div-32768-clamp-unit",
        };

        AddBlockRows(values, "clean", input, clean);
        AddBlockRows(values, "qsb", input, qsb);
        for (int blockIndex = 0;
             blockIndex < input.ComparedBlockCount;
             blockIndex++)
        {
            string cleanHash =
                ComputeRawSingleSha256(clean[blockIndex].Samples);
            string qsbHash =
                ComputeRawSingleSha256(qsb[blockIndex].Samples);
            values.Add(
                $"output-invariance[{Format(blockIndex)}]"
                + $"|clean-float-sha256={cleanHash}"
                + $"|qsb-float-sha256={qsbHash}"
                + "|exact-equal="
                + Format(
                    SamplesEqual(
                        clean[blockIndex].Samples,
                        qsb[blockIndex].Samples)));
        }

        float[] cleanAggregate =
            CreateAggregate(input, clean);
        float[] qsbAggregate =
            CreateAggregate(input, qsb);
        values.Add(
            "aggregate-invariance"
            + $"|sample-count={Format(cleanAggregate.Length)}"
            + "|clean-float-sha256="
            + ComputeRawSingleSha256(cleanAggregate)
            + "|qsb-float-sha256="
            + ComputeRawSingleSha256(qsbAggregate)
            + "|exact-equal="
            + Format(SamplesEqual(cleanAggregate, qsbAggregate)));

        if (values.Count
            != QsbNoStationNoiseInvarianceInput.ExpectedValueCount)
        {
            throw new InvalidDataException(
                "The QSB invariance capture emitted an invalid row "
                + "count.");
        }

        return [.. values];
    }

    private static void AddBlockRows(
        List<string> values,
        string run,
        QsbNoStationNoiseInvarianceInput input,
        IReadOnlyList<CapturedAudioBlock> blocks)
    {
        for (int blockIndex = 0;
             blockIndex < input.ComparedBlockCount;
             blockIndex++)
        {
            float[] samples = blocks[blockIndex].Samples;
            string probeBits = String.Join(
                ',',
                input.ProbeSampleIndexes.Select(
                    sampleIndex => BitConverter
                        .SingleToUInt32Bits(samples[sampleIndex])
                        .ToString(
                            "x8",
                            CultureInfo.InvariantCulture)));
            values.Add(
                $"{run}-block[{Format(blockIndex)}]"
                + $"|sample-count={Format(samples.Length)}"
                + $"|probe-bits={probeBits}"
                + "|float-sha256="
                + ComputeRawSingleSha256(samples));
        }
    }

    private static void ValidateCapture(
        QsbNoStationNoiseInvarianceInput input,
        IReadOnlyList<CapturedAudioBlock> blocks,
        string run)
    {
        if (blocks.Count != input.ComparedBlockCount)
        {
            throw new InvalidDataException(
                $"The {run} QSB invariance capture has an invalid "
                + "block count.");
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
                    $"The {run} QSB invariance block {blockIndex} has "
                    + "invalid framing.");
            }

            foreach (float sample in block.Samples)
            {
                if (!float.IsFinite(sample)
                    || sample is < -1f or > 1f)
                {
                    throw new InvalidDataException(
                        $"The {run} QSB invariance block {blockIndex} "
                        + "contains an invalid normalized sample.");
                }
            }
        }
    }

    private static float[] CreateAggregate(
        QsbNoStationNoiseInvarianceInput input,
        IReadOnlyList<CapturedAudioBlock> blocks)
    {
        var aggregate =
            new float[input.ComparedBlockCount * input.BlockSize];
        for (int blockIndex = 0;
             blockIndex < input.ComparedBlockCount;
             blockIndex++)
        {
            blocks[blockIndex].Samples.CopyTo(
                aggregate,
                blockIndex * input.BlockSize);
        }

        return aggregate;
    }

    private static bool SamplesEqual(
        float[] first,
        float[] second)
    {
        if (first.Length != second.Length)
        {
            return false;
        }

        for (int index = 0; index < first.Length; index++)
        {
            if (BitConverter.SingleToUInt32Bits(first[index])
                != BitConverter.SingleToUInt32Bits(second[index]))
            {
                return false;
            }
        }

        return true;
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
                $"The XPlat QSB invariance {action} command was "
                + $"rejected: {result.ErrorCode ?? "(no error code)"}; "
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
