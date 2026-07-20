using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MorseRunner.Domain;
using MorseRunner.Dsp;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatQrmFirstTriggeredStationTarget :
    IParityTarget
{
    internal const string ParityId =
        "audio.qrm-first-triggered-station-seed-1843";
    internal const string FunctionalDivergenceCode =
        "audio-qrm-first-triggered-station-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Engine.MorseRunnerEngine"
        + "+MorseRunner.Engine.EngineSession"
        + "+MorseRunner.Engine.IAudioSink.WriteAsync"
        + "+MorseRunner.Engine.MorseRunnerEngine"
        + ".TakeNextSessionRandomSingleForParityAsync"
        + "+MorseRunner.Engine.MorseRunnerEngine"
        + ".ObserveQrmStationForParityAsync"
        + "+MorseRunner.Engine.MorseRunnerEngine.SubscribeAsync"
        + "+MorseRunner.Engine.QrmStationParityObservation"
        + "+MorseRunner.Dsp.LegacyReceiverNoiseGenerator"
        + "+MorseRunner.Dsp.LegacyRandom"
        + "+MorseRunner.Dsp.LegacyRandomEffects";

    private const double TriggerProbability = 0.0002d;
    private const string SelectedCall = "LU5MT";
    private const string ExpectedConstructorSequenceBits =
        "38e1bf40,3f03301e,3eac999c,3f04e9ec,3f155543,"
        + "3e293bc8,3f2dadc6,3da2d42c,3e9941cd,3f519e01";
    private static readonly ClientId ParityClient =
        new("qrm-first-triggered-station-parity");

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

        QrmFirstTriggeredStationInput input =
            QrmFirstTriggeredStationInput.Parse(scenario);
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
        QrmFirstTriggeredStationInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureLittleEndianSingleStorage();

        ConstructorReplay replay = ReplayDecisions(input);
        CapturedRun clean = await CaptureAsync(
            input,
            qrmEnabled: false,
            cancellationToken);
        CapturedRun qrm = await CaptureAsync(
            input,
            qrmEnabled: true,
            cancellationToken);
        return Normalize(input, replay, clean, qrm);
    }

    internal static ConstructorReplay ReplayDecisions(
        QrmFirstTriggeredStationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sequenceRandom = new LegacyRandom(input.Seed);
        for (int ordinal = 0;
             ordinal < input.QrmTriggerRandomOrdinal;
             ordinal++)
        {
            _ = sequenceRandom.NextDouble();
        }

        float[] sequence = new float[10];
        for (int index = 0; index < sequence.Length; index++)
        {
            sequence[index] = sequenceRandom.NextSingle();
        }

        var random = new LegacyRandom(input.Seed);
        for (int ordinal = 0;
             ordinal < input.QrmTriggerRandomOrdinal;
             ordinal++)
        {
            _ = random.NextDouble();
        }

        int ordinalCursor = input.QrmTriggerRandomOrdinal;
        int triggerOrdinal = ordinalCursor++;
        float triggerValue = random.NextSingle();
        int r1Ordinal = ordinalCursor++;
        float r1 = random.NextSingle();
        int patienceOrdinal = ordinalCursor++;
        int patience = 1 + random.Next(5);
        int callOrdinal = ordinalCursor++;
        int callIndex = random.Next(input.CallCatalogCount);
        int amplitudeOrdinal = ordinalCursor++;
        float amplitude = (float)(
            5_000d + (25_000d * random.NextDouble()));
        int firstGaussianOrdinal = ordinalCursor++;
        int secondGaussianOrdinal = ordinalCursor++;
        var effects = new LegacyRandomEffects(random);
        int pitchOffsetHz = (int)MathF.Round(
            effects.GaussianLimited(0f, 300f),
            MidpointRounding.ToEven);
        int wordsPerMinuteOrdinal = ordinalCursor++;
        int wordsPerMinute = 30 + random.Next(20);
        int messageOrdinal = ordinalCursor++;
        int messageChoice = random.Next(7);
        int terminalOrdinal = ordinalCursor;
        float terminalRandom = random.NextSingle();

        var replay = new ConstructorReplay(
            sequence,
            triggerOrdinal,
            triggerValue,
            r1Ordinal,
            r1,
            patienceOrdinal,
            patience,
            callOrdinal,
            callIndex,
            amplitudeOrdinal,
            amplitude,
            firstGaussianOrdinal,
            secondGaussianOrdinal,
            pitchOffsetHz,
            wordsPerMinuteOrdinal,
            wordsPerMinute,
            messageOrdinal,
            messageChoice,
            terminalOrdinal,
            terminalRandom);
        ValidateReplay(input, replay);
        return replay;
    }

    internal static async Task<CapturedRun> CaptureAsync(
        QrmFirstTriggeredStationInput input,
        bool qrmEnabled,
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
            StationCall = input.StationCall,
            WordsPerMinute = 30,
            PitchHz = input.PitchHz,
            BandwidthHz = input.BandwidthHz,
            Activity = 1,
            Qsk = false,
            Qsb = false,
            Flutter = false,
            Qrn = false,
            Qrm = qrmEnabled,
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
        RequireSnapshotFraming(snapshot, input);
        await RequireNoPublicCallerEventsAsync(
            engine,
            handle.SessionId,
            snapshot,
            cancellationToken);
        QrmStationParityObservation observation =
            await engine.ObserveQrmStationForParityAsync(
                handle.SessionId,
                snapshot.Revision,
                snapshot.SimulationBlock,
                cancellationToken);
        CapturedAudioBlock[] blocks =
            sink.RequireCompleteCapture(input.ComparedBlockCount);
        float terminalRandom =
            await engine.TakeNextSessionRandomSingleForParityAsync(
                handle.SessionId,
                snapshot.Revision,
                snapshot.SimulationBlock,
                cancellationToken);
        return new(blocks, observation, terminalRandom);
    }

    internal static string[] Normalize(
        QrmFirstTriggeredStationInput input,
        ConstructorReplay replay,
        CapturedRun clean,
        CapturedRun qrm)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(clean);
        ArgumentNullException.ThrowIfNull(qrm);
        EnsureLittleEndianSingleStorage();
        ValidateReplay(input, replay);
        ValidateCapture(input, clean, "clean");
        ValidateCapture(input, qrm, "qrm");
        if (clean.Station != QrmStationParityObservation.Empty)
        {
            throw new InvalidDataException(
                "The clean positive-QRM capture observed an internal "
                + "QRM station.");
        }

        var values = new List<string>(
            QrmFirstTriggeredStationInput.ExpectedValueCount)
        {
            "contract=ce-live-qrm-first-trigger-v1"
            + "|fresh-runs=clean,qrm"
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
            + "|qrn=false"
            + "|qsb=false"
            + "|flutter=false"
            + "|qsk=false"
            + "|lids=false",
            "catalog"
            + "|path=MASTER.DTA"
            + "|bytes=1239476"
            + $"|sha256={input.MasterDataSha256}"
            + $"|count={Format(input.CallCatalogCount)}"
            + $"|selected-index={Format(replay.CallIndex)}"
            + $"|selected-call={SelectedCall}",
            "random"
            + $"|trigger-ordinal={Format(replay.TriggerOrdinal)}"
            + $"|trigger-value={Format(replay.TriggerValue)}"
            + "|trigger-single-bits="
            + SingleBits(replay.TriggerValue)
            + "|draw-single-bits="
            + String.Join(',', replay.Sequence.Select(SingleBits))
            + $"|r1-ordinal={Format(replay.R1Ordinal)}"
            + $"|r1-single-bits={SingleBits(replay.R1)}"
            + "|patience-ordinal="
            + Format(replay.PatienceOrdinal)
            + $"|patience={Format(replay.Patience)}"
            + $"|call-ordinal={Format(replay.CallOrdinal)}"
            + $"|call-index={Format(replay.CallIndex)}"
            + "|amplitude-ordinal="
            + Format(replay.AmplitudeOrdinal)
            + $"|amplitude={Format(replay.Amplitude)}"
            + "|amplitude-single-bits="
            + SingleBits(replay.Amplitude)
            + "|gaussian-ordinals="
            + Format(replay.FirstGaussianOrdinal)
            + ","
            + Format(replay.SecondGaussianOrdinal)
            + "|pitch-offset-hz="
            + Format(replay.PitchOffsetHz)
            + "|wpm-ordinal="
            + Format(replay.WordsPerMinuteOrdinal)
            + $"|wpm={Format(replay.WordsPerMinute)}"
            + "|message-ordinal="
            + Format(replay.MessageOrdinal)
            + "|message-choice="
            + Format(replay.MessageChoice),
        };

        AddStationRows(values, qrm.Station, input.BlockSize);
        values.Add(
            "probes|sample-indexes="
            + String.Join(
                ',',
                input.ProbeSampleIndexes.Select(Format)));
        AddBlockStatistics(
            values,
            "clean-block[0]",
            clean.Blocks[0].Samples,
            input.ProbeSampleIndexes);
        AddBlockStatistics(
            values,
            "qrm-block[0]",
            qrm.Blocks[0].Samples,
            input.ProbeSampleIndexes);

        string cleanHash =
            ComputeRawSingleSha256(clean.Blocks[0].Samples);
        string qrmHash =
            ComputeRawSingleSha256(qrm.Blocks[0].Samples);
        values.Add(
            "comparison"
            + "|exact-equal="
            + Format(
                SamplesEqual(
                    clean.Blocks[0].Samples,
                    qrm.Blocks[0].Samples))
            + "|first-divergence="
            + Format(
                FindFirstDivergence(
                    clean.Blocks[0].Samples,
                    qrm.Blocks[0].Samples))
            + $"|clean-float-sha256={cleanHash}"
            + $"|qrm-float-sha256={qrmHash}"
            + "|station-counts="
            + Format(clean.Station.ActiveCount)
            + ","
            + Format(qrm.Station.ActiveCount)
            + "|pick-station-calls=0,"
            + Format(qrm.Station.ActiveCount)
            + "|get-call-calls=0,"
            + Format(qrm.Station.ActiveCount));
        values.Add(
            "terminal-random"
            + "|clean-ordinal="
            + Format(input.CleanTerminalRandomOrdinal)
            + "|clean-value="
            + Format(clean.TerminalRandom)
            + "|clean-single-bits="
            + SingleBits(clean.TerminalRandom)
            + "|qrm-ordinal="
            + Format(input.QrmTerminalRandomOrdinal)
            + "|qrm-value="
            + Format(qrm.TerminalRandom)
            + "|qrm-single-bits="
            + SingleBits(qrm.TerminalRandom));

        if (values.Count
            != QrmFirstTriggeredStationInput.ExpectedValueCount)
        {
            throw new InvalidDataException(
                "The positive-QRM capture emitted an invalid row "
                + "count.");
        }

        return [.. values];
    }

    private static void AddStationRows(
        List<string> values,
        QrmStationParityObservation station,
        int blockSize)
    {
        bool active = station.ActiveCount == 1;
        string stationClass = active ? "TQrmStation" : "none";
        string stationState = active && station.IsSending
            ? "stSending"
            : "none";
        string myCall = station.MyCall ?? string.Empty;
        string hisCall = station.HisCall ?? string.Empty;
        string messageSet = station.MessageSet ?? "none";
        string messageText = station.MessageText ?? string.Empty;
        int remainingBlocks = active
            ? (station.EnvelopeSampleCount - station.SendPosition)
                / blockSize
            : 0;
        values.Add(
            "station"
            + $"|count={Format(station.ActiveCount)}"
            + $"|class={stationClass}"
            + $"|state={stationState}"
            + $"|my-call={myCall}"
            + $"|his-call={hisCall}"
            + "|r1-single-bits=" + SingleBits(station.R1)
            + $"|amplitude={Format(station.Amplitude)}"
            + "|amplitude-single-bits="
            + SingleBits(station.Amplitude)
            + "|pitch-offset-hz="
            + Format(station.PitchOffsetHz)
            + $"|wpm-s={Format(station.SendingWordsPerMinute)}"
            + "|wpm-c="
            + Format(station.CharacterWordsPerMinute));
        values.Add(
            "message"
            + $"|set={messageSet}"
            + $"|text={messageText}"
            + "|envelope-samples="
            + Format(station.EnvelopeSampleCount)
            + "|envelope-blocks="
            + Format(station.EnvelopeSampleCount / blockSize)
            + "|send-position="
            + Format(station.SendPosition)
            + "|remaining-blocks="
            + Format(remainingBlocks));
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

        string probeBits = String.Join(
            ',',
            probeSampleIndexes.Select(
                sampleIndex => SingleBits(samples[sampleIndex])));
        values.Add(
            prefix
            + $"|probe-bits={probeBits}"
            + $"|peak={Format(peak)}"
            + "|rms="
            + Format(Math.Sqrt(sumSquares / samples.Length))
            + "|float-sha256="
            + ComputeRawSingleSha256(samples));
    }

    private static void ValidateReplay(
        QrmFirstTriggeredStationInput input,
        ConstructorReplay replay)
    {
        string sequenceBits =
            String.Join(',', replay.Sequence.Select(SingleBits));
        if (replay.Sequence.Length != 10
            || !StringComparer.Ordinal.Equals(
                sequenceBits,
                ExpectedConstructorSequenceBits)
            || replay.TriggerOrdinal != input.QrmTriggerRandomOrdinal
            || replay.TriggerValue >= TriggerProbability
            || SingleBits(replay.TriggerValue) != "38e1bf40"
            || replay.R1Ordinal != 1_025
            || SingleBits(replay.R1) != "3f03301e"
            || replay.PatienceOrdinal != 1_026
            || replay.Patience != 2
            || replay.CallOrdinal != 1_027
            || replay.CallIndex != input.SelectedCallIndex
            || replay.AmplitudeOrdinal != 1_028
            || SingleBits(replay.Amplitude) != "4698fe9d"
            || replay.FirstGaussianOrdinal != 1_029
            || replay.SecondGaussianOrdinal != 1_030
            || replay.PitchOffsetHz != -124
            || replay.WordsPerMinuteOrdinal != 1_031
            || replay.WordsPerMinute != 31
            || replay.MessageOrdinal != 1_032
            || replay.MessageChoice != 2
            || replay.TerminalOrdinal != input.QrmTerminalRandomOrdinal
            || SingleBits(replay.TerminalRandom) != "3f519e01")
        {
            throw new InvalidDataException(
                "The CE positive-QRM constructor replay is invalid.");
        }
    }

    private static void ValidateCapture(
        QrmFirstTriggeredStationInput input,
        CapturedRun capture,
        string run)
    {
        if (capture.Blocks.Length != input.ComparedBlockCount)
        {
            throw new InvalidDataException(
                $"The {run} positive-QRM capture has an invalid "
                + "block count.");
        }

        ValidateStationObservation(capture.Station, run, input.BlockSize);
        ValidateTerminalRandom(capture.TerminalRandom, run);
        for (int blockIndex = 0;
             blockIndex < capture.Blocks.Length;
             blockIndex++)
        {
            CapturedAudioBlock block = capture.Blocks[blockIndex];
            if (block.SimulationBlock != blockIndex
                || block.Samples.Length != input.BlockSize)
            {
                throw new InvalidDataException(
                    $"The {run} positive-QRM block {blockIndex} has "
                    + "invalid framing.");
            }

            foreach (float sample in block.Samples)
            {
                if (!float.IsFinite(sample)
                    || sample is < -1f or > 1f)
                {
                    throw new InvalidDataException(
                        $"The {run} positive-QRM block {blockIndex} "
                        + "contains an invalid normalized sample.");
                }
            }
        }
    }

    private static void ValidateStationObservation(
        QrmStationParityObservation observation,
        string run,
        int blockSize)
    {
        if (observation.ActiveCount is < 0 or > 1)
        {
            throw new InvalidDataException(
                $"The {run} internal QRM observation has an invalid "
                + "active count.");
        }

        if (observation.ActiveCount == 0)
        {
            if (observation != QrmStationParityObservation.Empty)
            {
                throw new InvalidDataException(
                    $"The {run} empty internal QRM observation is "
                    + "inconsistent.");
            }

            return;
        }

        if (!observation.IsSending
            || String.IsNullOrEmpty(observation.MyCall)
            || String.IsNullOrEmpty(observation.HisCall)
            || String.IsNullOrEmpty(observation.MessageSet)
            || observation.MessageText is null
            || !float.IsFinite(observation.R1)
            || observation.R1 is < 0f or >= 1f
            || !float.IsFinite(observation.Amplitude)
            || observation.Amplitude <= 0f
            || observation.SendingWordsPerMinute <= 0
            || observation.CharacterWordsPerMinute <= 0
            || observation.EnvelopeSampleCount <= 0
            || observation.EnvelopeSampleCount % blockSize != 0
            || observation.SendPosition is < 0
            || observation.SendPosition > observation.EnvelopeSampleCount
            || observation.SendPosition % blockSize != 0)
        {
            throw new InvalidDataException(
                $"The {run} internal QRM observation is inconsistent.");
        }
    }

    private static void ValidateTerminalRandom(float value, string run)
    {
        if (!float.IsFinite(value) || value is < 0f or >= 1f)
        {
            throw new InvalidDataException(
                $"The {run} positive-QRM terminal random sentinel is "
                + "invalid.");
        }
    }

    private static void RequireSnapshotFraming(
        SessionSnapshot snapshot,
        QrmFirstTriggeredStationInput input)
    {
        if (snapshot.State != SessionState.Running
            || snapshot.SimulationBlock != input.ComparedBlockCount
            || snapshot.RenderedSamples
                != (long)input.ComparedBlockCount * input.BlockSize
            || snapshot.LastCaller is not null
            || snapshot.ActiveStations is not { Count: 0 })
        {
            throw new InvalidOperationException(
                "The XPlat positive-QRM session snapshot has invalid "
                + "framing or exposed QRM as a public caller.");
        }
    }

    private static async Task RequireNoPublicCallerEventsAsync(
        MorseRunnerEngine engine,
        SessionId sessionId,
        SessionSnapshot expectedSnapshot,
        CancellationToken cancellationToken)
    {
        var observedKinds = new List<SessionEventKind>(6);
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
                        "The internal XPlat QRM station leaked a "
                        + "public caller event.");
                }
            }

            if (update.Snapshot is SessionSnapshot snapshot)
            {
                if (observedKinds.Count != 6
                    || observedKinds[0] != SessionEventKind.Created
                    || observedKinds[1] != SessionEventKind.Ready
                    || observedKinds[2] != SessionEventKind.Started
                    || observedKinds[3]
                        != SessionEventKind.CommandApplied
                    || observedKinds[4]
                        != SessionEventKind.CommandApplied
                    || observedKinds[5]
                        != SessionEventKind.CommandApplied
                    || snapshot.Revision != expectedSnapshot.Revision
                    || snapshot.SimulationBlock
                        != expectedSnapshot.SimulationBlock)
                {
                    throw new InvalidOperationException(
                        "The XPlat positive-QRM public-event replay "
                        + "had invalid framing.");
                }

                return;
            }
        }

        throw new InvalidOperationException(
            "The XPlat positive-QRM event-isolation check did not "
            + "receive its terminal snapshot.");
    }

    private static int FindFirstDivergence(
        float[] first,
        float[] second)
    {
        if (first.Length != second.Length)
        {
            throw new InvalidDataException(
                "The positive-QRM comparison block lengths differ.");
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

    private static bool SamplesEqual(
        float[] first,
        float[] second) =>
        FindFirstDivergence(first, second) == -1;

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
                $"The XPlat positive-QRM {action} command was "
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
        QrmStationParityObservation Station,
        float TerminalRandom);

    internal sealed record ConstructorReplay(
        float[] Sequence,
        int TriggerOrdinal,
        float TriggerValue,
        int R1Ordinal,
        float R1,
        int PatienceOrdinal,
        int Patience,
        int CallOrdinal,
        int CallIndex,
        int AmplitudeOrdinal,
        float Amplitude,
        int FirstGaussianOrdinal,
        int SecondGaussianOrdinal,
        int PitchOffsetHz,
        int WordsPerMinuteOrdinal,
        int WordsPerMinute,
        int MessageOrdinal,
        int MessageChoice,
        int TerminalOrdinal,
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
