using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatOperatorMonitorMuteTarget : IParityTarget
{
    internal const string ParityId =
        "audio.operator-monitor-minus-60db-mute-first-cq-block-seed-12345";
    internal const string FunctionalDivergenceCode =
        "audio-operator-monitor-minus-60db-mute-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Engine.EngineSession.RenderBlocksAsync"
        + "+MorseRunner.Engine.EngineSession.MixOperatorMonitorIntoReceiver"
        + "+MorseRunner.Engine.IAudioSink.WriteAsync";

    private static readonly ClientId ParityClient =
        new("operator-monitor-mute-parity");

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

        OperatorMonitorMuteInput input =
            OperatorMonitorMuteInput.Parse(scenario);
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
        OperatorMonitorMuteInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureLittleEndianSingleStorage();
        CapturedRun fullMonitor = await CaptureAsync(
            input,
            input.FullMonitorLevelDb,
            cancellationToken);
        CapturedRun mutedMonitor = await CaptureAsync(
            input,
            input.MutedMonitorLevelDb,
            cancellationToken);
        return Normalize(input, fullMonitor, mutedMonitor);
    }

    private static async Task<CapturedRun> CaptureAsync(
        OperatorMonitorMuteInput input,
        int monitorLevelDb,
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
            BandwidthHz = input.BandwidthHz,
            Activity = 1,
            Qsk = false,
            Qsb = false,
            Qrm = false,
            Qrn = false,
            Flutter = false,
            Lids = false,
            MonitorLevelDb = monitorLevelDb,
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
            "advance",
            cancellationToken);

        SessionSnapshot snapshot = engine.GetSnapshot(handle.SessionId);
        if (snapshot.State != SessionState.Running
            || snapshot.SimulationBlock != 1
            || snapshot.RenderedSamples != input.BlockSize
            || snapshot.ActiveStations is not { Count: 0 }
            || snapshot.LastOperatorMessage != input.MessageText)
        {
            throw new InvalidOperationException(
                "The XPlat monitor session left its fixed first-CQ boundary.");
        }

        CapturedAudioBlock block = sink.RequireSingleBlock();
        float terminalRandom =
            await engine.TakeNextSessionRandomSingleForParityAsync(
                handle.SessionId,
                snapshot.Revision,
                snapshot.SimulationBlock,
                cancellationToken);
        return new(block, snapshot, terminalRandom);
    }

    internal static string[] Normalize(
        OperatorMonitorMuteInput input,
        CapturedRun fullMonitor,
        CapturedRun mutedMonitor)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(fullMonitor);
        ArgumentNullException.ThrowIfNull(mutedMonitor);
        ValidateRun(input, fullMonitor, "full-monitor");
        ValidateRun(input, mutedMonitor, "muted-monitor");

        int firstDivergence = FirstDivergence(
            fullMonitor.Block.Samples,
            mutedMonitor.Block.Samples);
        var values = new List<string>(
            OperatorMonitorMuteInput.ExpectedValueCount)
        {
            "configuration"
            + "|run-mode=rmStop"
            + $"|seed={Format(input.Seed)}"
            + $"|sample-rate={Format(input.SampleRate)}"
            + $"|block-size={Format(input.BlockSize)}"
            + "|startup-requests="
            + Format(input.StartupRequestCount)
            + "|absolute-block=6"
            + $"|bandwidth-hz={Format(input.BandwidthHz)}"
            + $"|pitch-hz={Format(input.PitchHz)}"
            + $"|station-call={input.StationCall}"
            + "|monitor-levels-db="
            + Format(input.FullMonitorLevelDb)
            + "," + Format(input.MutedMonitorLevelDb)
            + "|qrn=false|qrm=false|qsb=false|flutter=false|qsk=false|lids=false",
            "local-message"
            + $"|text={input.MessageText}"
            + "|rendered-local-samples="
            + Format(input.BlockSize)
            + "|probe-sample-indexes="
            + String.Join(',', input.ProbeSampleIndexes.Select(Format)),
        };
        AddBlockStatistics(
            values,
            "monitor-full-block[0]",
            fullMonitor.Block.Samples,
            input.ProbeSampleIndexes);
        AddBlockStatistics(
            values,
            "monitor-muted-block[0]",
            mutedMonitor.Block.Samples,
            input.ProbeSampleIndexes);
        values.Add(
            "comparison"
            + "|exact-equal=" + Format(firstDivergence < 0)
            + "|first-divergence=" + Format(firstDivergence)
            + "|monitor-full-float-sha256="
            + ComputeRawSingleSha256(fullMonitor.Block.Samples)
            + "|monitor-muted-float-sha256="
            + ComputeRawSingleSha256(mutedMonitor.Block.Samples));
        values.Add(
            "terminal-random"
            + "|ordinal=" + Format(input.TerminalRandomOrdinal)
            + "|monitor-full-single-bits="
            + SingleBits(fullMonitor.TerminalRandom)
            + "|monitor-muted-single-bits="
            + SingleBits(mutedMonitor.TerminalRandom));

        if (values.Count != OperatorMonitorMuteInput.ExpectedValueCount)
        {
            throw new InvalidDataException(
                "The monitor capture emitted an invalid row count.");
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
        OperatorMonitorMuteInput input,
        CapturedRun run,
        string name)
    {
        if (run.Block.SimulationBlock != 0
            || run.Block.Samples.Length != input.BlockSize
            || run.Snapshot.RenderedSamples != input.BlockSize
            || !float.IsFinite(run.TerminalRandom)
            || run.TerminalRandom is < 0f or >= 1f)
        {
            throw new InvalidDataException(
                $"The {name} capture has invalid framing.");
        }

        foreach (float sample in run.Block.Samples)
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
                "The monitor captures have different lengths.");
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
                $"The XPlat monitor {action} command was rejected: "
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
        CapturedAudioBlock Block,
        SessionSnapshot Snapshot,
        float TerminalRandom);

    private sealed class StrictCaptureAudioSink : IAudioSink
    {
        private readonly int _sampleRate;
        private readonly int _blockSize;
        private SessionId? _sessionId;
        private CapturedAudioBlock? _block;
        private bool _initialized;
        private bool _disposed;

        public StrictCaptureAudioSink(int sampleRate, int blockSize)
        {
            _sampleRate = sampleRate;
            _blockSize = blockSize;
        }

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
                    "The monitor parity sink received invalid initialization.");
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
                || _block is not null
                || simulationBlock != 0
                || samples.Length != _blockSize)
            {
                throw new InvalidOperationException(
                    "The monitor parity sink received invalid audio.");
            }

            _block = new(simulationBlock, samples.ToArray());
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
                || _block is not null)
            {
                throw new InvalidOperationException(
                    "The monitor parity sink was not cleanly initialized.");
            }
        }

        public void RequireNoAudio(string phase)
        {
            if (_block is not null)
            {
                throw new InvalidOperationException(
                    $"The monitor parity sink received audio {phase}.");
            }
        }

        public CapturedAudioBlock RequireSingleBlock() =>
            _block
            ?? throw new InvalidOperationException(
                "The monitor parity sink did not capture one block.");
    }
}

internal sealed record OperatorMonitorMuteInput(
    int SampleRate,
    int BlockSize,
    int Seed,
    int StartupRequestCount,
    int BandwidthHz,
    int PitchHz,
    int FullMonitorLevelDb,
    int MutedMonitorLevelDb,
    string StationCall,
    string MessageText,
    int TerminalRandomOrdinal,
    IReadOnlyList<int> ProbeSampleIndexes)
{
    internal const int ExpectedValueCount = 6;

    public static OperatorMonitorMuteInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames =
        [
            "bandwidthHz",
            "blockSize",
            "fullMonitorLevelDb",
            "messageText",
            "mutedMonitorLevelDb",
            "pitchHz",
            "probeSampleIndexes",
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
        var result = new OperatorMonitorMuteInput(
            input.GetProperty("sampleRate").GetInt32(),
            input.GetProperty("blockSize").GetInt32(),
            input.GetProperty("seed").GetInt32(),
            input.GetProperty("startupRequestCount").GetInt32(),
            input.GetProperty("bandwidthHz").GetInt32(),
            input.GetProperty("pitchHz").GetInt32(),
            input.GetProperty("fullMonitorLevelDb").GetInt32(),
            input.GetProperty("mutedMonitorLevelDb").GetInt32(),
            RequireString(input, "stationCall", scenario.Id),
            RequireString(input, "messageText", scenario.Id),
            input.GetProperty("terminalRandomOrdinal").GetInt32(),
            probes);
        string discriminator = RequireString(input, "scenario", scenario.Id);
        int[] expectedProbes =
            [0, 1, 2, 148, 149, 150, 255, 310, 384, 509, 510, 511];
        if (discriminator != XPlatOperatorMonitorMuteTarget.ParityId
            || result.SampleRate != 11_025
            || result.BlockSize != 512
            || result.Seed != 12_345
            || result.StartupRequestCount != 5
            || result.BandwidthHz != 500
            || result.PitchHz != 600
            || result.FullMonitorLevelDb != 0
            || result.MutedMonitorLevelDb != -60
            || result.StationCall != "W7SST"
            || result.MessageText != "CQ W7SST TEST"
            || result.TerminalRandomOrdinal != 1_024
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
