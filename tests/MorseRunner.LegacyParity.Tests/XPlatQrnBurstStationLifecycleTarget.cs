using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MorseRunner.Domain;
using MorseRunner.Dsp;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatQrnBurstStationLifecycleTarget :
    IParityTarget
{
    internal const string ParityId =
        "audio.qrn-burst-station-lifecycle-seed-1903";
    internal const string FunctionalDivergenceCode =
        "audio-qrn-burst-station-lifecycle-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Engine.MorseRunnerEngine"
        + "+MorseRunner.Engine.EngineSession"
        + "+MorseRunner.Engine.IAudioSink.WriteAsync"
        + "+MorseRunner.Engine.MorseRunnerEngine"
        + ".TakeNextSessionRandomSingleForParityAsync"
        + "+MorseRunner.Engine.MorseRunnerEngine"
        + ".ObserveQrnBurstForParityAsync"
        + "+MorseRunner.Engine.MorseRunnerEngine.SubscribeAsync"
        + "+MorseRunner.Engine.QrnBurstParityObservation"
        + "+MorseRunner.Engine.QrnBurstStation"
        + "+MorseRunner.Dsp.LegacyReceiverNoiseGenerator"
        + "+MorseRunner.Dsp.LegacyRandom";

    private const double TriggerProbability = 0.01d;
    private const double NoiseAmplitude = 6_000d;
    private static readonly ClientId ParityClient =
        new("qrn-burst-station-lifecycle-parity");

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

        QrnBurstStationLifecycleInput input =
            QrnBurstStationLifecycleInput.Parse(scenario);
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
        QrnBurstStationLifecycleInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureLittleEndianSingleStorage();

        BurstReplay replay = ReplayDecisions(input);
        CapturedRun oneBlock = await CaptureAsync(
            input,
            requestedBlockCount: 1,
            cancellationToken);
        CapturedRun twoBlocks = await CaptureAsync(
            input,
            requestedBlockCount: input.ComparedBlockCount,
            cancellationToken);
        return Normalize(input, replay, oneBlock, twoBlocks);
    }

    internal static BurstReplay ReplayDecisions(
        QrnBurstStationLifecycleInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var random = new LegacyRandom(input.Seed);
        int ordinal = 0;

        BackgroundDecision block1 = ReplayBackground(
            random,
            input.BlockSize,
            ref ordinal);
        if (!block1.BurstCreated)
        {
            throw new InvalidDataException(
                "The CE block-one QRN decision replay did not create "
                + "a burst.");
        }

        BurstConstructorDecision constructor =
            ReplayBurstConstructor(
                random,
                input,
                ref ordinal);
        if (ordinal != input.Block1TerminalRandomOrdinal)
        {
            throw new InvalidDataException(
                "The CE QRN burst constructor random framing is "
                + "invalid.");
        }

        BackgroundDecision block2 = ReplayBackground(
            random,
            input.BlockSize,
            ref ordinal);
        if (block2.BurstCreated
            || ordinal != input.Block2TerminalRandomOrdinal)
        {
            throw new InvalidDataException(
                "The CE block-two QRN decision replay is invalid.");
        }

        float block2TerminalRandom = random.NextSingle();
        float block1TerminalRandom = ReplaySingleAtOrdinal(
            input.Seed,
            input.Block1TerminalRandomOrdinal);
        var replay = new BurstReplay(
            block1,
            constructor,
            block1TerminalRandom,
            block2,
            block2TerminalRandom);
        ValidateReplay(input, replay);
        return replay;
    }

    private static BackgroundDecision ReplayBackground(
        LegacyRandom random,
        int blockSize,
        ref int ordinal)
    {
        for (int sampleIndex = 0;
             sampleIndex < blockSize;
             sampleIndex++)
        {
            _ = random.NextDouble();
            ordinal++;
            _ = random.NextDouble();
            ordinal++;
        }

        var replacementIndexes = new List<int>();
        var triggerOrdinals = new List<int>();
        var triggerValues = new List<double>();
        var replacementOrdinals = new List<int>();
        var replacementRandomValues = new List<double>();
        var replacementSamples = new List<float>();
        for (int sampleIndex = 0;
             sampleIndex < blockSize;
             sampleIndex++)
        {
            double trigger = random.NextDouble();
            int triggerOrdinal = ordinal++;
            if (trigger >= TriggerProbability)
            {
                continue;
            }

            replacementIndexes.Add(sampleIndex);
            triggerOrdinals.Add(triggerOrdinal);
            triggerValues.Add(trigger);
            replacementOrdinals.Add(ordinal++);
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
        return new(
            [.. replacementIndexes],
            [.. triggerOrdinals],
            [.. triggerValues],
            [.. replacementOrdinals],
            [.. replacementRandomValues],
            [.. replacementSamples],
            burstTriggerOrdinal,
            burstTriggerValue,
            burstTriggerValue < TriggerProbability);
    }

    private static BurstConstructorDecision ReplayBurstConstructor(
        LegacyRandom random,
        QrnBurstStationLifecycleInput input,
        ref int ordinal)
    {
        int durationRandomOrdinal = ordinal++;
        double durationRandomValue = random.NextDouble();
        float durationRandomSingle = (float)durationRandomValue;
        int durationBlocks = (int)Math.Round(
            11_025d / input.BlockSize * durationRandomSingle,
            MidpointRounding.ToEven);
        int durationSamples = checked(durationBlocks * input.BlockSize);

        int amplitudeRandomOrdinal = ordinal++;
        double amplitudeRandomValue = random.NextDouble();
        float amplitude = (float)(
            100_000d
            * Math.Pow(10d, 2d * amplitudeRandomValue));

        var replacementIndexes = new List<int>();
        var triggerOrdinals = new List<int>();
        var triggerValues = new List<double>();
        var replacementOrdinals = new List<int>();
        var replacementRandomValues = new List<double>();
        var replacementSamples = new List<float>();
        for (int sampleIndex = 0;
             sampleIndex < durationSamples;
             sampleIndex++)
        {
            double trigger = random.NextDouble();
            int triggerOrdinal = ordinal++;
            if (trigger >= TriggerProbability)
            {
                continue;
            }

            replacementIndexes.Add(sampleIndex);
            triggerOrdinals.Add(triggerOrdinal);
            triggerValues.Add(trigger);
            replacementOrdinals.Add(ordinal++);
            double replacementRandom = random.NextDouble();
            replacementRandomValues.Add(replacementRandom);
            replacementSamples.Add(
                (float)((replacementRandom - 0.5d) * amplitude));
        }

        return new(
            durationRandomOrdinal,
            durationRandomValue,
            durationBlocks,
            durationSamples,
            amplitudeRandomOrdinal,
            amplitudeRandomValue,
            amplitude,
            [.. replacementIndexes],
            [.. triggerOrdinals],
            [.. triggerValues],
            [.. replacementOrdinals],
            [.. replacementRandomValues],
            [.. replacementSamples]);
    }

    private static float ReplaySingleAtOrdinal(
        int seed,
        int ordinal)
    {
        var random = new LegacyRandom(seed);
        float result = 0f;
        for (int index = 0; index <= ordinal; index++)
        {
            result = random.NextSingle();
        }

        return result;
    }

    internal static async Task<CapturedRun> CaptureAsync(
        QrnBurstStationLifecycleInput input,
        int requestedBlockCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        if (requestedBlockCount is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedBlockCount));
        }

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
            Qrn = true,
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
                1),
            "first advance",
            cancellationToken);
        SessionSnapshot afterBlock1 =
            engine.GetSnapshot(handle.SessionId);
        RequireSnapshotFraming(
            afterBlock1,
            expectedBlock: 1,
            input);
        QrnBurstProbe block1Burst =
            await CaptureBurstProbeAsync(
                engine,
                handle.SessionId,
                afterBlock1.Revision,
                afterBlock1.SimulationBlock,
                cancellationToken);

        SessionSnapshot? afterBlock2 = null;
        QrnBurstProbe? block2Burst = null;
        if (requestedBlockCount == 2)
        {
            await RequireAcceptedAsync(
                engine,
                new AdvanceSimulationCommand(
                    RequestId.New(),
                    handle.SessionId,
                    ParityClient,
                    1),
                "second advance",
                cancellationToken);
            afterBlock2 = engine.GetSnapshot(handle.SessionId);
            RequireSnapshotFraming(
                afterBlock2,
                expectedBlock: 2,
                input);
            block2Burst = await CaptureBurstProbeAsync(
                engine,
                handle.SessionId,
                afterBlock2.Revision,
                afterBlock2.SimulationBlock,
                cancellationToken);
        }

        SessionSnapshot terminalSnapshot =
            afterBlock2 ?? afterBlock1;
        await RequireNoPublicCallerEventsAsync(
            engine,
            handle.SessionId,
            terminalSnapshot,
            requestedBlockCount,
            cancellationToken);
        CapturedAudioBlock[] blocks =
            sink.RequireCompleteCapture(requestedBlockCount);
        float terminalRandom =
            await engine.TakeNextSessionRandomSingleForParityAsync(
                handle.SessionId,
                terminalSnapshot.Revision,
                terminalSnapshot.SimulationBlock,
                cancellationToken);
        return new(
            blocks,
            block1Burst,
            block2Burst,
            terminalRandom);
    }

    internal static string[] Normalize(
        QrnBurstStationLifecycleInput input,
        BurstReplay replay,
        CapturedRun oneBlock,
        CapturedRun twoBlocks)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(oneBlock);
        ArgumentNullException.ThrowIfNull(twoBlocks);
        EnsureLittleEndianSingleStorage();
        ValidateReplay(input, replay);
        ValidateCapture(input, oneBlock, expectedBlockCount: 1);
        ValidateCapture(
            input,
            twoBlocks,
            expectedBlockCount: input.ComparedBlockCount);
        if (!SamplesEqual(
                oneBlock.Blocks[0].Samples,
                twoBlocks.Blocks[0].Samples))
        {
            throw new InvalidDataException(
                "The XPlat QRN block-one fresh runs are not "
                + "reproducible.");
        }

        var values = new List<string>(
            QrnBurstStationLifecycleInput.ExpectedValueCount)
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
            + "|block1-probe-sample-indexes="
            + String.Join(
                ',',
                input.Block1ProbeSampleIndexes.Select(Format))
            + "|block2-probe-sample-indexes="
            + String.Join(
                ',',
                input.Block2ProbeSampleIndexes.Select(Format))
            + "|fresh-runs=one-block,two-block"
            + "|run-mode=rmStop"
            + "|qrn=true"
            + "|qsb=false"
            + "|flutter=false"
            + "|qrm=false"
            + "|qsk=false"
            + "|lids=false"
            + "|operator-transmission=false"
            + "|normal-dx-stations=false"
            + "|normalization=ce-single-div-32768-clamp-unit",
            FormatBackgroundRow(
                "block1-background-decisions",
                replay.Block1Background),
            "burst-constructor-decisions"
            + "|duration-random-ordinal="
            + Format(replay.Constructor.DurationRandomOrdinal)
            + "|duration-random-value="
            + Format(replay.Constructor.DurationRandomValue)
            + "|duration-random-single-bits="
            + SingleBits(
                (float)replay.Constructor.DurationRandomValue)
            + "|duration-blocks="
            + Format(replay.Constructor.DurationBlocks)
            + "|duration-samples="
            + Format(replay.Constructor.DurationSamples)
            + "|amplitude-random-ordinal="
            + Format(replay.Constructor.AmplitudeRandomOrdinal)
            + "|amplitude-random-value="
            + Format(replay.Constructor.AmplitudeRandomValue)
            + "|amplitude-random-single-bits="
            + SingleBits(
                (float)replay.Constructor.AmplitudeRandomValue)
            + "|amplitude="
            + Format(replay.Constructor.Amplitude)
            + "|amplitude-single-bits="
            + SingleBits(replay.Constructor.Amplitude)
            + "|envelope-replacement-indexes="
            + String.Join(
                ',',
                replay.Constructor.ReplacementSampleIndexes
                    .Select(Format))
            + "|envelope-trigger-ordinals="
            + String.Join(
                ',',
                replay.Constructor.TriggerRandomOrdinals.Select(Format))
            + "|envelope-trigger-values="
            + String.Join(
                ',',
                replay.Constructor.TriggerValues.Select(Format))
            + "|envelope-trigger-single-bits="
            + String.Join(
                ',',
                replay.Constructor.TriggerValues.Select(
                    value => SingleBits((float)value)))
            + "|envelope-replacement-value-ordinals="
            + String.Join(
                ',',
                replay.Constructor.ReplacementRandomOrdinals
                    .Select(Format))
            + "|envelope-replacement-random-values="
            + String.Join(
                ',',
                replay.Constructor.ReplacementRandomValues
                    .Select(Format))
            + "|envelope-replacement-random-single-bits="
            + String.Join(
                ',',
                replay.Constructor.ReplacementRandomValues.Select(
                    value => SingleBits((float)value)))
            + "|envelope-replacement-sample-bits="
            + String.Join(
                ',',
                replay.Constructor.ReplacementSamples
                    .Select(SingleBits)),
        };

        int afterBlock1Count = twoBlocks.Block1Burst.ActiveCount;
        string afterBlock1Class =
            afterBlock1Count == 1 ? "TQrnStation" : "none";
        string afterBlock1State = afterBlock1Count == 1
            ? FormatLegacyStationState(
                twoBlocks.Block1Burst.State
                ?? throw new InvalidDataException(
                    "The internal QRN burst probe omitted station "
                    + "state."))
            : "none";
        int afterBlock1EnvelopeSamples =
            twoBlocks.Block1Burst.EnvelopeSampleCount;
        int afterBlock2Count =
            twoBlocks.Block2Burst?.ActiveCount
            ?? throw new InvalidDataException(
                "The two-block XPlat QRN capture omitted its "
                + "block-two internal burst probe.");
        values.Add(
            "station-lifecycle"
            + "|after-block1-count="
            + Format(afterBlock1Count)
            + "|after-block1-class=" + afterBlock1Class
            + "|after-block1-state=" + afterBlock1State
            + "|after-block1-envelope-samples="
            + Format(afterBlock1EnvelopeSamples)
            + "|after-block2-count="
            + Format(afterBlock2Count));

        AddOutputBlockRow(
            values,
            blockIndex: 0,
            twoBlocks.Blocks[0].Samples,
            input.Block1ProbeSampleIndexes);
        values.Add(
            FormatBackgroundRow(
                "block2-background-decisions",
                replay.Block2Background));
        AddOutputBlockRow(
            values,
            blockIndex: 1,
            twoBlocks.Blocks[1].Samples,
            input.Block2ProbeSampleIndexes);
        values.Add(
            "terminal-random-sentinels"
            + "|one-block-next-ordinal="
            + Format(input.Block1TerminalRandomOrdinal)
            + "|one-block-value="
            + Format(oneBlock.TerminalRandom)
            + "|one-block-single-bits="
            + SingleBits(oneBlock.TerminalRandom)
            + "|two-block-next-ordinal="
            + Format(input.Block2TerminalRandomOrdinal)
            + "|two-block-value="
            + Format(twoBlocks.TerminalRandom)
            + "|two-block-single-bits="
            + SingleBits(twoBlocks.TerminalRandom));
        float[] aggregateSamples = twoBlocks.Blocks
            .SelectMany(block => block.Samples)
            .ToArray();
        values.Add(
            "two-block-output"
            + "|sample-count=" + Format(aggregateSamples.Length)
            + "|float-sha256="
            + ComputeRawSingleSha256(aggregateSamples));

        if (values.Count
            != QrnBurstStationLifecycleInput.ExpectedValueCount)
        {
            throw new InvalidDataException(
                "The QRN burst lifecycle capture emitted an invalid "
                + "row count.");
        }

        return [.. values];
    }

    private static string FormatBackgroundRow(
        string name,
        BackgroundDecision decision)
    {
        return name
            + "|replacement-indexes="
            + String.Join(
                ',',
                decision.ReplacementSampleIndexes.Select(Format))
            + "|trigger-ordinals="
            + String.Join(
                ',',
                decision.TriggerRandomOrdinals.Select(Format))
            + "|trigger-values="
            + String.Join(',', decision.TriggerValues.Select(Format))
            + "|trigger-single-bits="
            + String.Join(
                ',',
                decision.TriggerValues.Select(
                    value => SingleBits((float)value)))
            + "|replacement-value-ordinals="
            + String.Join(
                ',',
                decision.ReplacementRandomOrdinals.Select(Format))
            + "|replacement-random-values="
            + String.Join(
                ',',
                decision.ReplacementRandomValues.Select(Format))
            + "|replacement-random-single-bits="
            + String.Join(
                ',',
                decision.ReplacementRandomValues.Select(
                    value => SingleBits((float)value)))
            + "|replacement-sample-bits="
            + String.Join(
                ',',
                decision.ReplacementSamples.Select(SingleBits))
            + "|burst-trigger-ordinal="
            + Format(decision.BurstTriggerOrdinal)
            + "|burst-trigger-value="
            + Format(decision.BurstTriggerValue)
            + "|burst-trigger-single-bits="
            + SingleBits((float)decision.BurstTriggerValue)
            + "|burst-created="
            + Format(decision.BurstCreated);
    }

    private static void AddOutputBlockRow(
        List<string> values,
        int blockIndex,
        float[] samples,
        IReadOnlyList<int> probeSampleIndexes)
    {
        string probeBits = String.Join(
            ',',
            probeSampleIndexes.Select(
                sampleIndex => SingleBits(samples[sampleIndex])));
        values.Add(
            $"qrn-block[{Format(blockIndex)}]"
            + $"|sample-count={Format(samples.Length)}"
            + $"|probe-bits={probeBits}"
            + "|float-sha256="
            + ComputeRawSingleSha256(samples));
    }

    private static void ValidateReplay(
        QrnBurstStationLifecycleInput input,
        BurstReplay replay)
    {
        ValidateBackgroundReplay(
            replay.Block1Background,
            input.Block1BackgroundReplacementIndexes,
            input.Block1BackgroundTriggerRandomOrdinals,
            input.Block1BackgroundReplacementRandomOrdinals,
            input.Block1BurstTriggerRandomOrdinal,
            expectedBurstCreated: true,
            "block one");
        if (replay.Constructor.DurationRandomOrdinal
                != input.DurationRandomOrdinal
            || replay.Constructor.DurationBlocks
                != input.DurationBlocks
            || replay.Constructor.DurationSamples
                != input.DurationSamples
            || replay.Constructor.AmplitudeRandomOrdinal
                != input.AmplitudeRandomOrdinal
            || !replay.Constructor.ReplacementSampleIndexes
                .SequenceEqual(input.EnvelopeReplacementIndexes)
            || !replay.Constructor.TriggerRandomOrdinals
                .SequenceEqual(input.EnvelopeTriggerRandomOrdinals)
            || !replay.Constructor.ReplacementRandomOrdinals
                .SequenceEqual(
                    input.EnvelopeReplacementRandomOrdinals)
            || replay.Constructor.TriggerValues.Length
                != input.EnvelopeReplacementIndexes.Count
            || replay.Constructor.ReplacementRandomValues.Length
                != input.EnvelopeReplacementIndexes.Count
            || replay.Constructor.ReplacementSamples.Length
                != input.EnvelopeReplacementIndexes.Count
            || !float.IsFinite(replay.Constructor.Amplitude)
            || replay.Constructor.Amplitude <= 0f)
        {
            throw new InvalidDataException(
                "The CE QRN eager burst constructor replay is "
                + "invalid.");
        }

        ValidateBackgroundReplay(
            replay.Block2Background,
            input.Block2BackgroundReplacementIndexes,
            input.Block2BackgroundTriggerRandomOrdinals,
            input.Block2BackgroundReplacementRandomOrdinals,
            input.Block2BurstTriggerRandomOrdinal,
            expectedBurstCreated: false,
            "block two");
        ValidateTerminalRandom(
            replay.Block1TerminalRandom,
            "replayed block-one");
        ValidateTerminalRandom(
            replay.Block2TerminalRandom,
            "replayed block-two");
    }

    private static void ValidateBackgroundReplay(
        BackgroundDecision decision,
        IReadOnlyList<int> expectedReplacementIndexes,
        IReadOnlyList<int> expectedTriggerOrdinals,
        IReadOnlyList<int> expectedReplacementOrdinals,
        int expectedBurstTriggerOrdinal,
        bool expectedBurstCreated,
        string phase)
    {
        if (!decision.ReplacementSampleIndexes.SequenceEqual(
                expectedReplacementIndexes)
            || !decision.TriggerRandomOrdinals.SequenceEqual(
                expectedTriggerOrdinals)
            || !decision.ReplacementRandomOrdinals.SequenceEqual(
                expectedReplacementOrdinals)
            || decision.BurstTriggerOrdinal
                != expectedBurstTriggerOrdinal
            || decision.BurstCreated != expectedBurstCreated
            || decision.TriggerValues.Length
                != expectedReplacementIndexes.Count
            || decision.ReplacementRandomValues.Length
                != expectedReplacementIndexes.Count
            || decision.ReplacementSamples.Length
                != expectedReplacementIndexes.Count)
        {
            throw new InvalidDataException(
                $"The CE QRN {phase} source-order replay is invalid.");
        }
    }

    private static void ValidateCapture(
        QrnBurstStationLifecycleInput input,
        CapturedRun capture,
        int expectedBlockCount)
    {
        if (capture.Blocks.Length != expectedBlockCount
            || capture.Block1Burst.ActiveCount is < 0 or > 1
            || (expectedBlockCount == 1
                && capture.Block2Burst is not null)
            || (expectedBlockCount == 2
                && (capture.Block2Burst is null
                    || capture.Block2Burst.ActiveCount is < 0 or > 1)))
        {
            throw new InvalidDataException(
                "The XPlat QRN burst capture has invalid lifecycle "
                + "framing.");
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
                    $"The XPlat QRN burst block {blockIndex} has "
                    + "invalid framing.");
            }

            foreach (float sample in block.Samples)
            {
                if (!float.IsFinite(sample)
                    || sample is < -1f or > 1f)
                {
                    throw new InvalidDataException(
                        $"The XPlat QRN burst block {blockIndex} "
                        + "contains an invalid normalized sample.");
                }
            }
        }

        ValidateBurstProbe(capture.Block1Burst, "block one");
        if (capture.Block2Burst is not null)
        {
            ValidateBurstProbe(capture.Block2Burst, "block two");
        }
        ValidateTerminalRandom(
            capture.TerminalRandom,
            "captured");
    }

    private static void ValidateBurstProbe(
        QrnBurstProbe probe,
        string phase)
    {
        if (!probe.IsAvailable)
        {
            throw new InvalidDataException(
                $"The XPlat {phase} internal QRN burst probe was "
                + "unavailable.");
        }

        bool empty = probe.ActiveCount == 0
            && probe.State is null
            && probe.EnvelopeSampleCount == 0;
        bool active = probe.ActiveCount == 1
            && probe.State is not null
            && probe.EnvelopeSampleCount > 0;
        if (!empty && !active)
        {
            throw new InvalidDataException(
                $"The XPlat {phase} internal QRN burst probe is "
                + "inconsistent.");
        }
    }

    private static void ValidateTerminalRandom(
        float value,
        string name)
    {
        if (!float.IsFinite(value) || value is < 0f or >= 1f)
        {
            throw new InvalidDataException(
                $"The {name} QRN terminal random sentinel is "
                + "invalid.");
        }
    }

    private static void RequireSnapshotFraming(
        SessionSnapshot snapshot,
        int expectedBlock,
        QrnBurstStationLifecycleInput input)
    {
        if (snapshot.State != SessionState.Running
            || snapshot.SimulationBlock != expectedBlock
            || snapshot.RenderedSamples
                != (long)expectedBlock * input.BlockSize
            || snapshot.LastCaller is not null
            || snapshot.ActiveStations is not { Count: 0 })
        {
            throw new InvalidOperationException(
                "The XPlat QRN burst session snapshot has invalid "
                + "framing or exposed internal QRN as a public "
                + "caller.");
        }
    }

    private static async Task RequireNoPublicCallerEventsAsync(
        MorseRunnerEngine engine,
        SessionId sessionId,
        SessionSnapshot expectedSnapshot,
        int expectedAdvanceCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var observedKinds = new List<SessionEventKind>(
            5 + expectedAdvanceCount);
        await foreach (SessionUpdate update in engine.SubscribeAsync(
                           new(sessionId, AfterSequence: 0),
                           cancellationToken))
        {
            if (update.Event is SessionEvent sessionEvent)
            {
                observedKinds.Add(sessionEvent.Kind);
                if (sessionEvent.Kind is
                    SessionEventKind.CallerJoined
                    or SessionEventKind.StationReplyStarted
                    or SessionEventKind.StationReplyCompleted
                    or SessionEventKind.CallerLeft)
                {
                    throw new InvalidOperationException(
                        "The internal XPlat QRN burst leaked a public "
                        + "caller event.");
                }
            }

            if (update.Snapshot is SessionSnapshot snapshot)
            {
                int expectedEventCount = 5 + expectedAdvanceCount;
                bool hasExpectedPrefix =
                    observedKinds.Count == expectedEventCount
                    && observedKinds[0] == SessionEventKind.Created
                    && observedKinds[1] == SessionEventKind.Ready
                    && observedKinds[2] == SessionEventKind.Started;
                for (int index = 3;
                     hasExpectedPrefix
                        && index < observedKinds.Count;
                     index++)
                {
                    hasExpectedPrefix =
                        observedKinds[index]
                            == SessionEventKind.CommandApplied;
                }

                if (!hasExpectedPrefix
                    || snapshot.Revision != expectedSnapshot.Revision
                    || snapshot.SimulationBlock
                        != expectedSnapshot.SimulationBlock)
                {
                    throw new InvalidOperationException(
                        "The XPlat QRN burst public-event replay had "
                        + "invalid framing.");
                }

                return;
            }
        }

        throw new InvalidOperationException(
            "The XPlat QRN burst event-isolation check did not "
            + "receive its terminal snapshot.");
    }

    private static async ValueTask<QrnBurstProbe>
        CaptureBurstProbeAsync(
            MorseRunnerEngine engine,
            SessionId sessionId,
            long expectedRevision,
            long expectedSimulationBlock,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        cancellationToken.ThrowIfCancellationRequested();
        QrnBurstParityObservation observation =
            await engine.ObserveQrnBurstForParityAsync(
                sessionId,
                expectedRevision,
                expectedSimulationBlock,
                cancellationToken);
        StationState? state = observation.ActiveCount == 0
            ? null
            : observation.IsSending
                ? StationState.Sending
                : StationState.Listening;
        return new(
            observation.ActiveCount,
            state,
            observation.EnvelopeSampleCount,
            IsAvailable: true);
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
                $"The XPlat QRN burst {action} command was rejected: "
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

    private static string SingleBits(float value)
    {
        return BitConverter
            .SingleToUInt32Bits(value)
            .ToString("x8", CultureInfo.InvariantCulture);
    }

    private static string FormatLegacyStationState(
        StationState state)
    {
        return state switch
        {
            StationState.Listening => "stListening",
            StationState.Copying => "stCopying",
            StationState.PreparingToSend => "stPreparingToSend",
            StationState.Sending => "stSending",
            _ => throw new InvalidDataException(
                "The XPlat QRN station state is unsupported."),
        };
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
        QrnBurstProbe Block1Burst,
        QrnBurstProbe? Block2Burst,
        float TerminalRandom);

    internal sealed record QrnBurstProbe(
        int ActiveCount,
        StationState? State,
        int EnvelopeSampleCount,
        bool IsAvailable);

    internal sealed record BackgroundDecision(
        int[] ReplacementSampleIndexes,
        int[] TriggerRandomOrdinals,
        double[] TriggerValues,
        int[] ReplacementRandomOrdinals,
        double[] ReplacementRandomValues,
        float[] ReplacementSamples,
        int BurstTriggerOrdinal,
        double BurstTriggerValue,
        bool BurstCreated);

    internal sealed record BurstConstructorDecision(
        int DurationRandomOrdinal,
        double DurationRandomValue,
        int DurationBlocks,
        int DurationSamples,
        int AmplitudeRandomOrdinal,
        double AmplitudeRandomValue,
        float Amplitude,
        int[] ReplacementSampleIndexes,
        int[] TriggerRandomOrdinals,
        double[] TriggerValues,
        int[] ReplacementRandomOrdinals,
        double[] ReplacementRandomValues,
        float[] ReplacementSamples);

    internal sealed record BurstReplay(
        BackgroundDecision Block1Background,
        BurstConstructorDecision Constructor,
        float Block1TerminalRandom,
        BackgroundDecision Block2Background,
        float Block2TerminalRandom);

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

            _blocks.Add(new(simulationBlock, samples.ToArray()));
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
                    "The parity audio sink was not cleanly "
                    + "initialized.");
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
