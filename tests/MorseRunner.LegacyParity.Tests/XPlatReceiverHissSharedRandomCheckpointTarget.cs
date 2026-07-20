using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatReceiverHissSharedRandomCheckpointTarget :
    IParityTarget
{
    internal const string ParityId =
        "audio.receiver-hiss-shared-random-checkpoint-seed-12345";
    internal const string FunctionalDivergenceCode =
        "audio-receiver-hiss-shared-random-checkpoint-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Engine.MorseRunnerEngine"
        + "+MorseRunner.Engine.EngineSession"
        + "+MorseRunner.Engine.IAudioSink.WriteAsync"
        + "+MorseRunner.Engine.MorseRunnerEngine"
        + ".TakeNextSessionRandomSingleForParityAsync";

    private static readonly ClientId ParityClient =
        new("receiver-hiss-shared-random-checkpoint-parity");

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

        ReceiverHissSharedRandomCheckpointInput input =
            ReceiverHissSharedRandomCheckpointInput.Parse(scenario);
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
        ReceiverHissSharedRandomCheckpointInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureLittleEndianSingleStorage();

        CapturedRun capture = await CaptureAsync(
            input,
            cancellationToken);
        return Normalize(input, capture);
    }

    internal static async Task<CapturedRun> CaptureAsync(
        ReceiverHissSharedRandomCheckpointInput input,
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
            Qsb = false,
            Flutter = false,
            Qrn = false,
            Qrm = false,
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
                input.CompleteBlockCount),
            "advance",
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        SessionSnapshot snapshot = engine.GetSnapshot(handle.SessionId);
        int activeStationCount = snapshot.ActiveStations?.Count
            ?? throw new InvalidOperationException(
                "The XPlat shared-random snapshot omitted stations.");
        if (snapshot.State != SessionState.Running
            || snapshot.SimulationBlock != input.CompleteBlockCount
            || snapshot.RenderedSamples
                != (long)input.CompleteBlockCount * input.BlockSize
            || activeStationCount != 0)
        {
            throw new InvalidOperationException(
                "The XPlat shared-random session did not remain a "
                + "pure receiver capture.");
        }

        CapturedAudioBlock[] blocks =
            sink.RequireCompleteCapture(input.CompleteBlockCount);
        float randomCheckpoint =
            await engine.TakeNextSessionRandomSingleForParityAsync(
                handle.SessionId,
                snapshot.Revision,
                snapshot.SimulationBlock,
                cancellationToken);
        return new(
            blocks,
            activeStationCount,
            randomCheckpoint);
    }

    internal static string[] Normalize(
        ReceiverHissSharedRandomCheckpointInput input,
        CapturedRun capture)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(capture);
        EnsureLittleEndianSingleStorage();
        ValidateCapture(input, capture);

        CapturedAudioBlock block = capture.Blocks[0];
        string probeBits = String.Join(
            ',',
            input.ProbeSampleIndexes.Select(
                sampleIndex => BitConverter
                    .SingleToUInt32Bits(block.Samples[sampleIndex])
                    .ToString("x8", CultureInfo.InvariantCulture)));
        string[] values =
        [
            "configuration"
                + $"|sample-rate={Format(input.SampleRate)}"
                + $"|block-size={Format(input.BlockSize)}"
                + $"|seed={Format(input.Seed)}"
                + $"|bandwidth-hz={Format(input.BandwidthHz)}"
                + $"|pitch-hz={Format(input.PitchHz)}"
                + "|startup-request-count="
                + Format(input.StartupRequestCount)
                + "|complete-block-count="
                + Format(input.CompleteBlockCount)
                + "|hiss-random-draw-count="
                + Format(input.HissRandomDrawCount)
                + "|random-checkpoint-ordinal="
                + Format(input.RandomCheckpointOrdinal)
                + "|probe-sample-indexes="
                + String.Join(
                    ',',
                    input.ProbeSampleIndexes.Select(Format))
                + "|run-mode=rmStop"
                + "|qsb=false"
                + "|flutter=false"
                + "|qrm=false"
                + "|qrn=false"
                + "|qsk=false"
                + "|lids=false"
                + "|operator-transmission=false"
                + "|normal-dx-stations=false"
                + "|normalization=ce-single-div-32768-clamp-unit",
            "receiver-block[0]"
                + $"|sample-count={Format(block.Samples.Length)}"
                + $"|probe-bits={probeBits}"
                + "|float-sha256="
                + ComputeRawSingleSha256(block.Samples),
            "shared-random-checkpoint"
                + "|draw-count-before-checkpoint="
                + Format(input.HissRandomDrawCount)
                + "|ordinal="
                + Format(input.RandomCheckpointOrdinal)
                + "|single-bits="
                + BitConverter
                    .SingleToUInt32Bits(capture.RandomCheckpoint)
                    .ToString("x8", CultureInfo.InvariantCulture),
        ];

        if (values.Length
            != ReceiverHissSharedRandomCheckpointInput
                .ExpectedValueCount)
        {
            throw new InvalidDataException(
                "The receiver hiss shared-random capture emitted an "
                + "invalid row count.");
        }

        return values;
    }

    private static void ValidateCapture(
        ReceiverHissSharedRandomCheckpointInput input,
        CapturedRun capture)
    {
        if (capture.ActiveStationCount != 0)
        {
            throw new InvalidDataException(
                "The receiver hiss shared-random capture created a "
                + "station.");
        }

        if (capture.Blocks.Length != input.CompleteBlockCount)
        {
            throw new InvalidDataException(
                "The receiver hiss shared-random capture has an invalid "
                + "block count.");
        }

        for (int blockIndex = 0;
             blockIndex < capture.Blocks.Length;
             blockIndex++)
        {
            CapturedAudioBlock block = capture.Blocks[blockIndex];
            if (block.SimulationBlock != blockIndex
                || block.Samples.Length != input.BlockSize)
            {
                throw new InvalidDataException(
                    "The receiver hiss shared-random block has invalid "
                    + "framing.");
            }

            foreach (float sample in block.Samples)
            {
                if (!float.IsFinite(sample)
                    || sample is < -1f or > 1f)
                {
                    throw new InvalidDataException(
                        "The receiver hiss shared-random block contains "
                        + "an invalid normalized sample.");
                }
            }
        }

        if (!float.IsFinite(capture.RandomCheckpoint)
            || capture.RandomCheckpoint is < 0f or >= 1f)
        {
            throw new InvalidDataException(
                "The receiver hiss shared-random checkpoint is invalid.");
        }
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
                $"The XPlat shared-random {action} command was "
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

    internal sealed record CapturedRun(
        CapturedAudioBlock[] Blocks,
        int ActiveStationCount,
        float RandomCheckpoint);

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
