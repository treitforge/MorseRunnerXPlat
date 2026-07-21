using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatRuntimeRitChangeTarget : IParityTarget
{
    internal const string ParityId =
        "audio.rit-runtime-plus-50-second-caller-block-seed-12345";
    internal const string FunctionalDivergenceCode =
        "audio-runtime-RIT-change-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Engine.EngineSession.RenderBlocksAsync"
        + "+MorseRunner.Engine.SimulatedStation.RenderBlock"
        + "+MorseRunner.Dsp.LegacyReceiverPipeline"
        + "+MorseRunner.Engine.IAudioSink.WriteAsync";

    private static readonly ClientId ParityClient =
        new("runtime-RIT-change-parity");

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

        RuntimeRitChangeInput input =
            RuntimeRitChangeInput.Parse(scenario);
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
        RuntimeRitChangeInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureLittleEndianSingleStorage();
        CapturedRun fixedRit = await CaptureAsync(
            input,
            ritChangeCount: 0,
            cancellationToken);
        CapturedRun runtimeRit = await CaptureAsync(
            input,
            ritChangeCount: 1,
            cancellationToken);
        return Normalize(input, fixedRit, runtimeRit);
    }

    internal static async Task<CapturedRun> CaptureAsync(
        RuntimeRitChangeInput input,
        int ritChangeCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ritChangeCount);
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
            PitchHz = 600,
            BandwidthHz = 500,
            Activity = 1,
            Qsk = false,
            Qsb = false,
            Qrm = false,
            Qrn = false,
            Flutter = false,
            Lids = false,
            MonitorLevelDb = 0,
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
        SessionSnapshot started = engine.GetSnapshot(handle.SessionId);
        await engine.AddScriptedStationForParityAsync(
            handle.SessionId,
            started.Revision,
            started.SimulationBlock,
            "N0CALL",
            input.MessageText,
            wordsPerMinute: 30,
            input.RemotePitchHz,
            input.RemoteAmplitude,
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
            || firstSnapshot.CurrentBandwidthHz != 500
            || firstSnapshot.RitOffsetHz != 0
            || firstSnapshot.ActiveStations is not { Count: 1 }
            || sink.BlockCount != 1)
        {
            throw new InvalidOperationException(
                "The XPlat runtime-RIT session left its first caller "
                + "boundary.");
        }

        for (int changeIndex = 0;
             changeIndex < ritChangeCount;
             changeIndex++)
        {
            await RequireAcceptedAsync(
                engine,
                new AdjustRadioControlCommand(
                    RequestId.New(),
                    handle.SessionId,
                    ParityClient,
                    RadioControl.Rit,
                    input.RuntimeRitHz),
                $"runtime RIT change {changeIndex + 1}",
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
            || snapshot.ActiveStations is not { Count: 1 }
            || snapshot.LastOperatorMessage is not null
            || snapshot.CurrentBandwidthHz != 500)
        {
            throw new InvalidOperationException(
                "The XPlat runtime-RIT session left its second caller "
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
        RuntimeRitChangeInput input,
        CapturedRun fixedRit,
        CapturedRun runtimeRit)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(fixedRit);
        ArgumentNullException.ThrowIfNull(runtimeRit);
        ValidateRun(input, fixedRit, "fixed-RIT");
        ValidateRun(input, runtimeRit, "runtime-RIT");

        int firstDivergence = FirstDivergence(
            fixedRit.Blocks[1].Samples,
            runtimeRit.Blocks[1].Samples);
        var values = new List<string>(
            RuntimeRitChangeInput.ExpectedValueCount)
        {
            "configuration"
            + "|run-mode=rmStop"
            + $"|seed={Format(input.Seed)}"
            + $"|sample-rate={Format(input.SampleRate)}"
            + $"|block-size={Format(input.BlockSize)}"
            + "|startup-requests="
            + Format(input.StartupRequestCount)
            + "|absolute-blocks=6,7"
            + "|rit-change-hz=0-to-" + Format(input.RuntimeRitHz)
            + "|change-before-absolute-block=7"
            + "|handler=TMainForm.Panel8MouseDown"
            + "|qrn=false|qrm=false|qsb=false|flutter=false|qsk=false|lids=false",
            "remote-station"
            + "|class=TScriptedStation"
            + "|pitch-offset-hz=" + Format(input.RemotePitchHz)
            + "|amplitude=" + Format(input.RemoteAmplitude)
            + "|message=" + input.MessageText
            + "|rendered-samples="
            + Format(input.BlockSize * 2)
            + "|probe-sample-indexes="
            + String.Join(',', input.ProbeSampleIndexes.Select(Format)),
        };
        AddBlockStatistics(
            values,
            "rit-before-change-block[0]",
            fixedRit.Blocks[0].Samples,
            input.ProbeSampleIndexes);
        AddBlockStatistics(
            values,
            "rit-fixed-0-block[1]",
            fixedRit.Blocks[1].Samples,
            input.ProbeSampleIndexes);
        AddBlockStatistics(
            values,
            "rit-runtime-plus-50-block[1]",
            runtimeRit.Blocks[1].Samples,
            input.ProbeSampleIndexes);
        values.Add(
            "comparison"
            + "|exact-equal=" + Format(firstDivergence < 0)
            + "|first-divergence=" + Format(firstDivergence)
            + "|rit-fixed-0-float-sha256="
            + ComputeRawSingleSha256(fixedRit.Blocks[1].Samples)
            + "|rit-runtime-plus-50-float-sha256="
            + ComputeRawSingleSha256(runtimeRit.Blocks[1].Samples));
        values.Add(
            "terminal-random"
            + "|ordinal=" + Format(input.TerminalRandomOrdinal)
            + "|rit-fixed-0-single-bits="
            + SingleBits(fixedRit.TerminalRandom)
            + "|rit-runtime-plus-50-single-bits="
            + SingleBits(runtimeRit.TerminalRandom));

        if (values.Count != RuntimeRitChangeInput.ExpectedValueCount)
        {
            throw new InvalidDataException(
                "The runtime-RIT capture emitted an invalid row "
                + "count.");
        }

        return [.. values];
    }

    internal static void AddBlockStatistics(
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

    internal static void ValidateRun(
        RuntimeRitChangeInput input,
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

    internal static int FirstDivergence(float[] first, float[] second)
    {
        if (first.Length != second.Length)
        {
            throw new InvalidDataException(
                "The runtime-RIT captures have different lengths.");
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
                $"The XPlat runtime-RIT {action} command was rejected: "
                + $"{result.ErrorCode ?? "(no error code)"}; "
                + $"{result.Message ?? "(no message)"}.");
        }
    }

    internal static string ComputeRawSingleSha256(
        ReadOnlySpan<float> samples) =>
        Convert.ToHexStringLower(
            SHA256.HashData(MemoryMarshal.AsBytes(samples)));

    internal static string SingleBits(float value) =>
        BitConverter
            .SingleToUInt32Bits(value)
            .ToString("x8", CultureInfo.InvariantCulture);

    internal static string Format(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    internal static string Format(long value) =>
        value.ToString(CultureInfo.InvariantCulture);

    internal static string Format(double value) =>
        value.ToString("F9", CultureInfo.InvariantCulture);

    internal static string Format(bool value) => value ? "true" : "false";

    internal static void EnsureLittleEndianSingleStorage()
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
                    "The runtime-RIT parity sink received invalid "
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
                    "The runtime-RIT parity sink received invalid "
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
                    "The runtime-RIT parity sink was not cleanly "
                    + "initialized.");
            }
        }

        public void RequireNoAudio(string phase)
        {
            if (_blocks.Count != 0)
            {
                throw new InvalidOperationException(
                    $"The runtime-RIT parity sink received audio "
                    + $"{phase}.");
            }
        }

        public CapturedAudioBlock[] RequireTwoBlocks() =>
            _blocks.Count == 2
                ? [.. _blocks]
                : throw new InvalidOperationException(
                    "The runtime-RIT parity sink did not capture two "
                    + "blocks.");
    }
}

internal sealed record RuntimeRitChangeInput(
    int SampleRate,
    int BlockSize,
    int Seed,
    int StartupRequestCount,
    int RuntimeRitHz,
    int RemotePitchHz,
    int RemoteAmplitude,
    string MessageText,
    int TerminalRandomOrdinal,
    IReadOnlyList<int> ProbeSampleIndexes)
{
    internal const int ExpectedValueCount = 7;

    public static RuntimeRitChangeInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames =
        [
            "blockSize",
            "messageText",
            "probeSampleIndexes",
            "remoteAmplitude",
            "remotePitchHz",
            "runtimeRitHz",
            "sampleRate",
            "scenario",
            "seed",
            "startupRequestCount",
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
        var result = new RuntimeRitChangeInput(
            input.GetProperty("sampleRate").GetInt32(),
            input.GetProperty("blockSize").GetInt32(),
            input.GetProperty("seed").GetInt32(),
            input.GetProperty("startupRequestCount").GetInt32(),
            input.GetProperty("runtimeRitHz").GetInt32(),
            input.GetProperty("remotePitchHz").GetInt32(),
            input.GetProperty("remoteAmplitude").GetInt32(),
            RequireString(input, "messageText", scenario.Id),
            input.GetProperty("terminalRandomOrdinal").GetInt32(),
            probes);
        string discriminator = RequireString(input, "scenario", scenario.Id);
        int[] expectedProbes =
            [0, 1, 2, 148, 149, 150, 255, 310, 384, 509, 510, 511];
        if (discriminator != XPlatRuntimeRitChangeTarget.ParityId
            || result.SampleRate != 11_025
            || result.BlockSize != 512
            || result.Seed != 12_345
            || result.StartupRequestCount != 5
            || result.RuntimeRitHz != 50
            || result.RemotePitchHz != 360
            || result.RemoteAmplitude != 18_000
            || result.MessageText != "TEST TEST"
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
