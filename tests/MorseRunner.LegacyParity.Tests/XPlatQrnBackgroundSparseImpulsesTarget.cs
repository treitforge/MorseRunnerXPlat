using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MorseRunner.Domain;
using MorseRunner.Dsp;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatQrnBackgroundSparseImpulsesTarget :
    IParityTarget
{
    internal const string ParityId =
        "audio.qrn-background-sparse-impulses-seed-12345";
    internal const string FunctionalDivergenceCode =
        "audio-qrn-background-sparse-impulses-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Engine.MorseRunnerEngine"
        + "+MorseRunner.Engine.EngineSession"
        + "+MorseRunner.Engine.IAudioSink.WriteAsync"
        + "+MorseRunner.Engine.MorseRunnerEngine"
        + ".TakeNextSessionRandomSingleForParityAsync"
        + "+MorseRunner.Dsp.LegacyReceiverNoiseGenerator"
        + "+MorseRunner.Dsp.LegacyRandom";

    private const double BackgroundTriggerProbability = 0.01d;
    private const double NoiseAmplitude = 6_000d;
    private static readonly ClientId ParityClient =
        new("qrn-background-sparse-impulses-parity");

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

        QrnBackgroundSparseImpulsesInput input =
            QrnBackgroundSparseImpulsesInput.Parse(scenario);
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
        QrnBackgroundSparseImpulsesInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureLittleEndianSingleStorage();

        QrnDecisionReplay decisions = ReplayDecisions(input);
        CapturedRun clean = await CaptureAsync(
            input,
            qrnEnabled: false,
            cancellationToken);
        CapturedRun qrn = await CaptureAsync(
            input,
            qrnEnabled: true,
            cancellationToken);
        return Normalize(input, decisions, clean, qrn);
    }

    internal static async Task<CapturedRun> CaptureAsync(
        QrnBackgroundSparseImpulsesInput input,
        bool qrnEnabled,
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
            Qrn = qrnEnabled,
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
                input.ComparedBlockCount),
            "advance",
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        SessionSnapshot snapshot = engine.GetSnapshot(handle.SessionId);
        int activeStationCount = snapshot.ActiveStations?.Count
            ?? throw new InvalidOperationException(
                "The XPlat QRN background snapshot omitted stations.");
        if (snapshot.State != SessionState.Running
            || snapshot.SimulationBlock != input.ComparedBlockCount
            || snapshot.RenderedSamples
                != (long)input.ComparedBlockCount * input.BlockSize
            || activeStationCount != 0)
        {
            throw new InvalidOperationException(
                "The XPlat QRN background session did not remain a "
                + "station-free receiver capture.");
        }

        CapturedAudioBlock[] blocks =
            sink.RequireCompleteCapture(input.ComparedBlockCount);
        float terminalRandom =
            await engine.TakeNextSessionRandomSingleForParityAsync(
                handle.SessionId,
                snapshot.Revision,
                snapshot.SimulationBlock,
                cancellationToken);
        return new(
            blocks,
            activeStationCount,
            terminalRandom);
    }

    internal static QrnDecisionReplay ReplayDecisions(
        QrnBackgroundSparseImpulsesInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var random = new LegacyRandom(input.Seed);
        for (int sampleIndex = 0;
             sampleIndex < input.BlockSize;
             sampleIndex++)
        {
            _ = random.NextDouble();
            _ = random.NextDouble();
        }

        var replacementSampleIndexes = new List<int>();
        var triggerRandomOrdinals = new List<int>();
        var triggerValues = new List<double>();
        var replacementRandomOrdinals = new List<int>();
        var replacementRandomValues = new List<double>();
        var replacementSamples = new List<float>();
        int ordinal = input.BlockSize * 2;
        for (int sampleIndex = 0;
             sampleIndex < input.BlockSize;
             sampleIndex++)
        {
            double trigger = random.NextDouble();
            int triggerOrdinal = ordinal++;
            if (trigger >= BackgroundTriggerProbability)
            {
                continue;
            }

            replacementSampleIndexes.Add(sampleIndex);
            triggerRandomOrdinals.Add(triggerOrdinal);
            triggerValues.Add(trigger);
            replacementRandomOrdinals.Add(ordinal++);
            double replacementRandom = random.NextDouble();
            replacementRandomValues.Add(replacementRandom);
            replacementSamples.Add(
                (float)(
                    60d
                    * NoiseAmplitude
                    * (replacementRandom - 0.5d)));
        }

        int burstTriggerOrdinal = ordinal++;
        double burstTriggerValue = random.NextDouble();
        bool burstCreated =
            burstTriggerValue < BackgroundTriggerProbability;
        float terminalRandom = random.NextSingle();
        var replay = new QrnDecisionReplay(
            [.. replacementSampleIndexes],
            [.. triggerRandomOrdinals],
            [.. triggerValues],
            [.. replacementRandomOrdinals],
            [.. replacementRandomValues],
            [.. replacementSamples],
            burstTriggerOrdinal,
            burstTriggerValue,
            burstCreated,
            terminalRandom);
        ValidateReplay(input, replay, ordinal);
        return replay;
    }

    internal static string[] Normalize(
        QrnBackgroundSparseImpulsesInput input,
        QrnDecisionReplay decisions,
        CapturedRun clean,
        CapturedRun qrn)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(clean);
        ArgumentNullException.ThrowIfNull(qrn);
        EnsureLittleEndianSingleStorage();
        ValidateReplay(
            input,
            decisions,
            input.QrnTerminalRandomOrdinal);
        ValidateCapture(input, clean, "clean");
        ValidateCapture(input, qrn, "qrn");

        var values = new List<string>(
            QrnBackgroundSparseImpulsesInput.ExpectedValueCount)
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
            + "|fresh-runs=clean,qrn"
            + "|run-mode=rmStop"
            + "|qsb=false"
            + "|flutter=false"
            + "|qrm=false"
            + "|qsk=false"
            + "|lids=false"
            + "|operator-transmission=false"
            + "|normal-dx-stations=false"
            + "|normalization=ce-single-div-32768-clamp-unit",
            "qrn-background-decisions"
            + "|replacement-indexes="
            + String.Join(
                ',',
                decisions.ReplacementSampleIndexes.Select(Format))
            + "|trigger-ordinals="
            + String.Join(
                ',',
                decisions.TriggerRandomOrdinals.Select(Format))
            + "|trigger-values="
            + String.Join(',', decisions.TriggerValues.Select(Format))
            + "|trigger-single-bits="
            + String.Join(
                ',',
                decisions.TriggerValues.Select(
                    value => SingleBits((float)value)))
            + "|replacement-value-ordinals="
            + String.Join(
                ',',
                decisions.ReplacementRandomOrdinals.Select(Format))
            + "|replacement-random-values="
            + String.Join(
                ',',
                decisions.ReplacementRandomValues.Select(Format))
            + "|replacement-random-single-bits="
            + String.Join(
                ',',
                decisions.ReplacementRandomValues.Select(
                    value => SingleBits((float)value)))
            + "|replacement-sample-bits="
            + String.Join(
                ',',
                decisions.ReplacementSamples.Select(SingleBits))
            + "|burst-trigger-ordinal="
            + Format(decisions.BurstTriggerOrdinal)
            + "|burst-trigger-value="
            + Format(decisions.BurstTriggerValue)
            + "|burst-trigger-single-bits="
            + SingleBits((float)decisions.BurstTriggerValue)
            + "|burst-created="
            + Format(decisions.BurstCreated),
        };

        AddBlockRows(values, "clean", input, clean.Blocks);
        AddBlockRows(values, "qrn", input, qrn.Blocks);
        values.Add(
            "station-counts"
            + $"|clean={Format(clean.ActiveStationCount)}"
            + $"|qrn={Format(qrn.ActiveStationCount)}"
            + "|burst-created="
            + Format(decisions.BurstCreated));
        values.Add(
            "terminal-random-sentinels"
            + "|clean-next-ordinal="
            + Format(input.CleanTerminalRandomOrdinal)
            + "|clean-value=" + Format(clean.TerminalRandom)
            + "|clean-single-bits="
            + SingleBits(clean.TerminalRandom)
            + "|qrn-next-ordinal="
            + Format(input.QrnTerminalRandomOrdinal)
            + "|qrn-value=" + Format(qrn.TerminalRandom)
            + "|qrn-single-bits="
            + SingleBits(qrn.TerminalRandom));

        for (int blockIndex = 0;
             blockIndex < input.ComparedBlockCount;
             blockIndex++)
        {
            float[] cleanSamples = clean.Blocks[blockIndex].Samples;
            float[] qrnSamples = qrn.Blocks[blockIndex].Samples;
            values.Add(
                $"output-difference[{Format(blockIndex)}]"
                + "|clean-float-sha256="
                + ComputeRawSingleSha256(cleanSamples)
                + "|qrn-float-sha256="
                + ComputeRawSingleSha256(qrnSamples)
                + "|exact-equal="
                + Format(SamplesEqual(cleanSamples, qrnSamples))
                + "|first-divergence="
                + Format(
                    FindFirstSampleDivergence(
                        cleanSamples,
                        qrnSamples)));
        }

        if (values.Count
            != QrnBackgroundSparseImpulsesInput.ExpectedValueCount)
        {
            throw new InvalidDataException(
                "The QRN background capture emitted an invalid row "
                + "count.");
        }

        return [.. values];
    }

    private static void AddBlockRows(
        List<string> values,
        string run,
        QrnBackgroundSparseImpulsesInput input,
        CapturedAudioBlock[] blocks)
    {
        for (int blockIndex = 0;
             blockIndex < input.ComparedBlockCount;
             blockIndex++)
        {
            float[] samples = blocks[blockIndex].Samples;
            string probeBits = String.Join(
                ',',
                input.ProbeSampleIndexes.Select(
                    sampleIndex => SingleBits(samples[sampleIndex])));
            values.Add(
                $"{run}-block[{Format(blockIndex)}]"
                + $"|sample-count={Format(samples.Length)}"
                + $"|probe-bits={probeBits}"
                + "|float-sha256="
                + ComputeRawSingleSha256(samples));
        }
    }

    private static void ValidateReplay(
        QrnBackgroundSparseImpulsesInput input,
        QrnDecisionReplay replay,
        int nextOrdinal)
    {
        if (!replay.ReplacementSampleIndexes.SequenceEqual(
                input.ReplacementSampleIndexes)
            || !replay.TriggerRandomOrdinals.SequenceEqual(
                input.TriggerRandomOrdinals)
            || !replay.ReplacementRandomOrdinals.SequenceEqual(
                input.ReplacementRandomOrdinals)
            || replay.BurstTriggerOrdinal
                != input.BurstTriggerRandomOrdinal
            || replay.BurstCreated
            || nextOrdinal != input.QrnTerminalRandomOrdinal
            || replay.TriggerValues.Length
                != input.ReplacementSampleIndexes.Count
            || replay.ReplacementRandomValues.Length
                != input.ReplacementSampleIndexes.Count
            || replay.ReplacementSamples.Length
                != input.ReplacementSampleIndexes.Count
            || !float.IsFinite(replay.TerminalRandom)
            || replay.TerminalRandom is < 0f or >= 1f)
        {
            throw new InvalidDataException(
                "The QRN source-order decision replay is invalid.");
        }
    }

    private static void ValidateCapture(
        QrnBackgroundSparseImpulsesInput input,
        CapturedRun capture,
        string run)
    {
        if (capture.ActiveStationCount != 0)
        {
            throw new InvalidDataException(
                $"The {run} QRN background capture created a station.");
        }

        if (capture.Blocks.Length != input.ComparedBlockCount)
        {
            throw new InvalidDataException(
                $"The {run} QRN background capture has an invalid "
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
                    $"The {run} QRN background block {blockIndex} has "
                    + "invalid framing.");
            }

            foreach (float sample in block.Samples)
            {
                if (!float.IsFinite(sample)
                    || sample is < -1f or > 1f)
                {
                    throw new InvalidDataException(
                        $"The {run} QRN background block {blockIndex} "
                        + "contains an invalid normalized sample.");
                }
            }
        }

        if (!float.IsFinite(capture.TerminalRandom)
            || capture.TerminalRandom is < 0f or >= 1f)
        {
            throw new InvalidDataException(
                $"The {run} QRN terminal random sentinel is invalid.");
        }
    }

    private static int FindFirstSampleDivergence(
        float[] first,
        float[] second)
    {
        int commonLength = Math.Min(first.Length, second.Length);
        for (int index = 0; index < commonLength; index++)
        {
            if (BitConverter.SingleToUInt32Bits(first[index])
                != BitConverter.SingleToUInt32Bits(second[index]))
            {
                return index;
            }
        }

        return first.Length == second.Length ? -1 : commonLength;
    }

    private static bool SamplesEqual(
        float[] first,
        float[] second)
    {
        return FindFirstSampleDivergence(first, second) < 0;
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
                $"The XPlat QRN background {action} command was "
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

    private static string SingleBits(float value)
    {
        return BitConverter
            .SingleToUInt32Bits(value)
            .ToString("x8", CultureInfo.InvariantCulture);
    }

    private static string Format(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Format(bool value)
    {
        return value ? "true" : "false";
    }

    private static string Format(double value)
    {
        return value.ToString("F9", CultureInfo.InvariantCulture);
    }

    private static string Format(float value)
    {
        return value.ToString("F9", CultureInfo.InvariantCulture);
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
        float TerminalRandom);

    internal sealed record QrnDecisionReplay(
        int[] ReplacementSampleIndexes,
        int[] TriggerRandomOrdinals,
        double[] TriggerValues,
        int[] ReplacementRandomOrdinals,
        double[] ReplacementRandomValues,
        float[] ReplacementSamples,
        int BurstTriggerOrdinal,
        double BurstTriggerValue,
        bool BurstCreated,
        float TerminalRandom);

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
