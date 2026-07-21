using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatRuntimeBandwidthChangeTarget : IParityTarget
{
    internal const string ParityId =
        "audio.bandwidth-runtime-narrow-second-cq-block-seed-12345";
    internal const string FunctionalDivergenceCode =
        "audio-runtime-bandwidth-change-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Engine.EngineSession.RenderBlocksAsync"
        + "+MorseRunner.Engine.EngineSession.MixOperatorMonitorIntoReceiver"
        + "+MorseRunner.Dsp.LegacyReceiverPipeline"
        + "+MorseRunner.Engine.IAudioSink.WriteAsync";

    private static readonly ClientId ParityClient =
        new("runtime-bandwidth-change-parity");

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

        RuntimeBandwidthChangeInput input =
            RuntimeBandwidthChangeInput.Parse(scenario);
        string[] values = await ObserveAsync(input, cancellationToken);
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
        RuntimeBandwidthChangeInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureLittleEndianSingleStorage();
        CapturedRun fixedBandwidth = await CaptureAsync(
            input,
            runtimeNarrowAfterFirstBlock: false,
            cancellationToken);
        CapturedRun runtimeNarrowBandwidth = await CaptureAsync(
            input,
            runtimeNarrowAfterFirstBlock: true,
            cancellationToken);
        return Normalize(input, fixedBandwidth, runtimeNarrowBandwidth);
    }

    private static async Task<CapturedRun> CaptureAsync(
        RuntimeBandwidthChangeInput input,
        bool runtimeNarrowAfterFirstBlock,
        CancellationToken cancellationToken)
    {
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
            StationCall = input.StationCall,
            WordsPerMinute = 30,
            PitchHz = input.PitchHz,
            BandwidthHz = input.InitialBandwidthHz,
            Activity = 1,
            Qsk = false,
            Qsb = false,
            Qrm = false,
            Qrn = false,
            Flutter = false,
            Lids = false,
            MonitorLevelDb = input.MonitorLevelDb,
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
                OperatorIntent.Cq,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty),
            "CQ",
            cancellationToken);
        sink.RequireNoAudio("before explicit advance");
        await RequireAcceptedAsync(
            engine,
            new AdvanceSimulationCommand(
                RequestId.New(),
                handle.SessionId,
                ParityClient,
                BlockCount: 1),
            "first advance",
            cancellationToken);

        SessionSnapshot firstSnapshot = engine.GetSnapshot(handle.SessionId);
        if (firstSnapshot.State != SessionState.Running
            || firstSnapshot.SimulationBlock != 1
            || firstSnapshot.RenderedSamples != input.BlockSize
            || firstSnapshot.CurrentBandwidthHz
                != input.InitialBandwidthHz
            || firstSnapshot.CurrentMonitorLevelDb
                != input.MonitorLevelDb
            || sink.BlockCount != 1)
        {
            throw new InvalidOperationException(
                "The XPlat runtime-bandwidth session left its first-CQ "
                + "boundary.");
        }

        if (runtimeNarrowAfterFirstBlock)
        {
            await RequireAcceptedAsync(
                engine,
                new AdjustRadioControlCommand(
                    RequestId.New(),
                    handle.SessionId,
                    ParityClient,
                    RadioControl.Bandwidth,
                    input.RuntimeBandwidthHz
                        - input.InitialBandwidthHz),
                "runtime bandwidth change",
                cancellationToken);
        }

        await RequireAcceptedAsync(
            engine,
            new AdvanceSimulationCommand(
                RequestId.New(),
                handle.SessionId,
                ParityClient,
                BlockCount: 1),
            "second advance",
            cancellationToken);

        SessionSnapshot snapshot = engine.GetSnapshot(handle.SessionId);
        if (snapshot.State != SessionState.Running
            || snapshot.SimulationBlock != 2
            || snapshot.RenderedSamples != input.BlockSize * 2L
            || snapshot.ActiveStations is not { Count: 0 }
            || snapshot.LastOperatorMessage != input.MessageText
            || snapshot.CurrentBandwidthHz
                != (runtimeNarrowAfterFirstBlock
                    ? input.RuntimeBandwidthHz
                    : input.InitialBandwidthHz)
            || snapshot.CurrentMonitorLevelDb != input.MonitorLevelDb)
        {
            throw new InvalidOperationException(
                "The XPlat runtime-bandwidth session left its second-CQ "
                + "boundary.");
        }

        CapturedAudioBlock[] blocks = sink.RequireTwoBlocks();
        float terminalRandom =
            await engine.TakeNextSessionRandomSingleForParityAsync(
                handle.SessionId,
                snapshot.Revision,
                snapshot.SimulationBlock,
                cancellationToken);
        return new(blocks, snapshot, terminalRandom);
    }

    internal static string[] Normalize(
        RuntimeBandwidthChangeInput input,
        CapturedRun fixedBandwidth,
        CapturedRun runtimeNarrowBandwidth)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(fixedBandwidth);
        ArgumentNullException.ThrowIfNull(runtimeNarrowBandwidth);
        ValidateRun(input, fixedBandwidth, "fixed-bandwidth");
        ValidateRun(input, runtimeNarrowBandwidth, "runtime-bandwidth");

        int firstDivergence = FirstDivergence(
            fixedBandwidth.Blocks[1].Samples,
            runtimeNarrowBandwidth.Blocks[1].Samples);
        var values = new List<string>(
            RuntimeBandwidthChangeInput.ExpectedValueCount)
        {
            "configuration"
            + "|run-mode=rmStop"
            + $"|seed={Format(input.Seed)}"
            + $"|sample-rate={Format(input.SampleRate)}"
            + $"|block-size={Format(input.BlockSize)}"
            + "|startup-requests="
            + Format(input.StartupRequestCount)
            + "|absolute-blocks=6,7"
            + "|bandwidth-change-hz="
            + Format(input.InitialBandwidthHz)
            + "-to-" + Format(input.RuntimeBandwidthHz)
            + $"|pitch-hz={Format(input.PitchHz)}"
            + $"|station-call={input.StationCall}"
            + "|monitor-level-db=" + Format(input.MonitorLevelDb)
            + "|change-before-absolute-block=7"
            + "|filter-reset=true"
            + "|qrn=false|qrm=false|qsb=false|flutter=false|qsk=false|lids=false",
            "local-message"
            + $"|text={input.MessageText}"
            + "|rendered-local-samples="
            + Format(input.BlockSize * 2)
            + "|probe-sample-indexes="
            + String.Join(',', input.ProbeSampleIndexes.Select(Format)),
        };
        AddBlockStatistics(
            values,
            "bandwidth-before-change-block[0]",
            fixedBandwidth.Blocks[0].Samples,
            input.ProbeSampleIndexes);
        AddBlockStatistics(
            values,
            "bandwidth-fixed-500-block[1]",
            fixedBandwidth.Blocks[1].Samples,
            input.ProbeSampleIndexes);
        AddBlockStatistics(
            values,
            "bandwidth-runtime-250-block[1]",
            runtimeNarrowBandwidth.Blocks[1].Samples,
            input.ProbeSampleIndexes);
        values.Add(
            "comparison"
            + "|exact-equal=" + Format(firstDivergence < 0)
            + "|first-divergence=" + Format(firstDivergence)
            + "|bandwidth-fixed-500-float-sha256="
            + ComputeRawSingleSha256(fixedBandwidth.Blocks[1].Samples)
            + "|bandwidth-runtime-250-float-sha256="
            + ComputeRawSingleSha256(runtimeNarrowBandwidth.Blocks[1].Samples));
        values.Add(
            "terminal-random"
            + "|ordinal=" + Format(input.TerminalRandomOrdinal)
            + "|bandwidth-fixed-500-single-bits="
            + SingleBits(fixedBandwidth.TerminalRandom)
            + "|bandwidth-runtime-250-single-bits="
            + SingleBits(runtimeNarrowBandwidth.TerminalRandom));

        if (values.Count != RuntimeBandwidthChangeInput.ExpectedValueCount)
        {
            throw new InvalidDataException(
                "The runtime-bandwidth capture emitted an invalid row "
                + "count.");
        }

        return [.. values];
    }

    private static void AddBlockStatistics(
        List<string> values,
        string prefix,
        float[] samples,
        IReadOnlyList<int> probeSampleIndexes)
    {
        double peak = 0d;
        double sumSquares = 0d;
        foreach (float sample in samples)
        {
            peak = Math.Max(peak, Math.Abs((double)sample));
            sumSquares += (double)sample * sample;
        }

        values.Add(
            prefix
            + "|probe-bits="
            + String.Join(
                ',',
                probeSampleIndexes.Select(
                    index => SingleBits(samples[index])))
            + "|peak=" + Format(peak)
            + "|rms="
            + Format(Math.Sqrt(sumSquares / samples.Length))
            + "|float-sha256=" + ComputeRawSingleSha256(samples));
    }

    private static void ValidateRun(
        RuntimeBandwidthChangeInput input,
        CapturedRun run,
        string name)
    {
        if (run.Blocks.Length != 2
            || run.Blocks[0].SimulationBlock != 0
            || run.Blocks[1].SimulationBlock != 1
            || run.Blocks.Any(block => block.Samples.Length != input.BlockSize)
            || run.Snapshot.RenderedSamples != input.BlockSize * 2L
            || !float.IsFinite(run.TerminalRandom)
            || run.TerminalRandom is < 0f or >= 1f)
        {
            throw new InvalidDataException(
                $"The {name} capture has invalid framing.");
        }

        foreach (float sample in run.Blocks.SelectMany(block => block.Samples))
        {
            if (!float.IsFinite(sample) || sample is < -1f or > 1f)
            {
                throw new InvalidDataException(
                    $"The {name} capture contains an invalid sample.");
            }
        }
    }

    private static int FirstDivergence(float[] first, float[] second)
    {
        if (first.Length != second.Length)
        {
            throw new InvalidDataException(
                "The runtime-bandwidth captures have different lengths.");
        }

        for (int index = 0; index < first.Length; index++)
        {
            if (BitConverter.SingleToUInt32Bits(first[index])
                != BitConverter.SingleToUInt32Bits(second[index]))
            {
                return index;
            }
        }

        return -1;
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
                $"The XPlat runtime-bandwidth {action} command was rejected: "
                + $"{result.ErrorCode ?? "(no error code)"}; "
                + $"{result.Message ?? "(no message)"}.");
        }
    }

    private static string ComputeRawSingleSha256(
        ReadOnlySpan<float> samples) =>
        Convert.ToHexStringLower(
            SHA256.HashData(MemoryMarshal.AsBytes(samples)));

    private static string SingleBits(float value) =>
        BitConverter
            .SingleToUInt32Bits(value)
            .ToString("x8", CultureInfo.InvariantCulture);

    private static string Format(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string Format(long value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string Format(double value) =>
        value.ToString("F9", CultureInfo.InvariantCulture);

    private static string Format(bool value) => value ? "true" : "false";

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
        SessionSnapshot Snapshot,
        float TerminalRandom);

    private sealed class StrictCaptureAudioSink : IAudioSink
    {
        private readonly int _sampleRate;
        private readonly int _blockSize;
        private SessionId? _sessionId;
        private readonly List<CapturedAudioBlock> _blocks = [];
        private bool _initialized;
        private bool _disposed;

        public StrictCaptureAudioSink(int sampleRate, int blockSize)
        {
            _sampleRate = sampleRate;
            _blockSize = blockSize;
        }

        public int BlockCount => _blocks.Count;

        public ValueTask InitializeAsync(
            SessionId sessionId,
            AudioStreamFormat format,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_initialized
                || _disposed
                || format.SampleRate != _sampleRate
                || format.Channels != 1
                || format.BlockSize != _blockSize)
            {
                throw new InvalidOperationException(
                    "The runtime-bandwidth parity sink received invalid "
                    + "initialization.");
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
            if (!_initialized
                || _disposed
                || _blocks.Count >= 2
                || simulationBlock != _blocks.Count
                || samples.Length != _blockSize)
            {
                throw new InvalidOperationException(
                    "The runtime-bandwidth parity sink received invalid "
                    + "audio.");
            }

            _blocks.Add(new(simulationBlock, samples.ToArray()));
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _disposed = true;
            return ValueTask.CompletedTask;
        }

        public void RequireInitializedWithoutAudio(SessionId sessionId)
        {
            if (!_initialized
                || _sessionId != sessionId
                || _disposed
                || _blocks.Count != 0)
            {
                throw new InvalidOperationException(
                    "The runtime-bandwidth parity sink was not cleanly "
                    + "initialized.");
            }
        }

        public void RequireNoAudio(string phase)
        {
            if (_blocks.Count != 0)
            {
                throw new InvalidOperationException(
                    $"The runtime-bandwidth parity sink received audio "
                    + $"{phase}.");
            }
        }

        public CapturedAudioBlock[] RequireTwoBlocks() =>
            _blocks.Count == 2
                ? [.. _blocks]
                : throw new InvalidOperationException(
                    "The runtime-bandwidth parity sink did not capture two "
                    + "blocks.");
    }
}

internal sealed record RuntimeBandwidthChangeInput(
    int SampleRate,
    int BlockSize,
    int Seed,
    int StartupRequestCount,
    int InitialBandwidthHz,
    int PitchHz,
    int MonitorLevelDb,
    int RuntimeBandwidthHz,
    string StationCall,
    string MessageText,
    int TerminalRandomOrdinal,
    IReadOnlyList<int> ProbeSampleIndexes)
{
    internal const int ExpectedValueCount = 7;

    public static RuntimeBandwidthChangeInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames =
        [
            "blockSize",
            "initialBandwidthHz",
            "messageText",
            "monitorLevelDb",
            "pitchHz",
            "probeSampleIndexes",
            "runtimeBandwidthHz",
            "sampleRate",
            "scenario",
            "seed",
            "startupRequestCount",
            "stationCall",
            "terminalRandomOrdinal",
        ];
        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' input has unsupported fields.");
        }

        int[] probes = input
            .GetProperty("probeSampleIndexes")
            .EnumerateArray()
            .Select(value => value.GetInt32())
            .ToArray();
        var result = new RuntimeBandwidthChangeInput(
            input.GetProperty("sampleRate").GetInt32(),
            input.GetProperty("blockSize").GetInt32(),
            input.GetProperty("seed").GetInt32(),
            input.GetProperty("startupRequestCount").GetInt32(),
            input.GetProperty("initialBandwidthHz").GetInt32(),
            input.GetProperty("pitchHz").GetInt32(),
            input.GetProperty("monitorLevelDb").GetInt32(),
            input.GetProperty("runtimeBandwidthHz").GetInt32(),
            RequireString(input, "stationCall", scenario.Id),
            RequireString(input, "messageText", scenario.Id),
            input.GetProperty("terminalRandomOrdinal").GetInt32(),
            probes);
        string discriminator = RequireString(input, "scenario", scenario.Id);
        int[] expectedProbes =
            [0, 1, 2, 148, 149, 150, 255, 310, 384, 509, 510, 511];
        if (discriminator != XPlatRuntimeBandwidthChangeTarget.ParityId
            || result.SampleRate != 11_025
            || result.BlockSize != 512
            || result.Seed != 12_345
            || result.StartupRequestCount != 5
            || result.InitialBandwidthHz != 500
            || result.PitchHz != 600
            || result.MonitorLevelDb != 0
            || result.RuntimeBandwidthHz != 250
            || result.StationCall != "W7SST"
            || result.MessageText != "CQ W7SST TEST"
            || result.TerminalRandomOrdinal != 2_048
            || !probes.SequenceEqual(expectedProbes)
            || scenario.ExpectedValues.Count != ExpectedValueCount)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' fixed vector is invalid.");
        }

        return result;
    }

    private static string RequireString(
        JsonElement input,
        string propertyName,
        string scenarioId)
    {
        JsonElement value = input.GetProperty(propertyName);
        string? result = value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
        return !String.IsNullOrEmpty(result)
            ? result
            : throw new InvalidDataException(
                $"Parity case '{scenarioId}' {propertyName} is invalid.");
    }
}
