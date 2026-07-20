using System.Globalization;
using MorseRunner.Audio;
using MorseRunner.Domain;
using MorseRunner.Dsp;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatQrmCallerCollisionTarget : IParityTarget
{
    internal const string ParityId =
        "audio.qrm-caller-collision-retry-limit-seed-24680";
    internal const string FunctionalDivergenceCode =
        "audio-qrm-caller-collision-retry-limit-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Engine.MorseRunnerEngine"
        + "+MorseRunner.Engine.EngineSession"
        + ".ObserveCallerCollisionForParityAsync"
        + "+MorseRunner.Engine.EngineSession.AddCaller"
        + "+MorseRunner.Engine.QrmStation.Activate"
        + "+MorseRunner.Engine.SimulatedStation"
        + "+MorseRunner.Engine.SimulatedOperator"
        + "+MorseRunner.Dsp.QsbProcessor"
        + "+MorseRunner.Dsp.LegacyRandom";

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

        QrmCallerCollisionInput input =
            QrmCallerCollisionInput.Parse(scenario);
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
        QrmCallerCollisionInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        await using var engine = new MorseRunnerEngine(
            _ => new NullAudioSink(),
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
            BandwidthHz = 500,
            PitchHz = 600,
            Activity = 1,
            ReceiveSpeedBelowWpm = 0,
            ReceiveSpeedAboveWpm = 0,
            Qsk = false,
            Qsb = false,
            Qrm = true,
            Qrn = false,
            Flutter = false,
            Lids = false,
        };
        SessionHandle handle = await engine.CreateSessionAsync(
            settings,
            cancellationToken);
        CallerCollisionParityObservation observation =
            await engine.ObserveCallerCollisionForParityAsync(
                handle.SessionId,
                handle.Revision,
                expectedSimulationBlock: 0,
                input.CollisionCall,
                input.RetryLimit,
                cancellationToken);
        return Normalize(input, observation);
    }

    internal static string[] Normalize(
        QrmCallerCollisionInput input,
        CallerCollisionParityObservation observation)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(observation);
        QrmStationParityObservation qrm = observation.Qrm;
        var values = new List<string>(
            QrmCallerCollisionInput.ExpectedValueCount)
        {
            "contract=ce-live-qrm-caller-collision-v1"
            + $"|seed={Format(input.Seed)}"
            + $"|sample-rate={Format(input.SampleRate)}"
            + $"|block-size={Format(input.BlockSize)}"
            + "|run-mode=rmStop|contest=scWpx"
            + $"|station-call={input.StationCall}"
            + "|qrm=true|qrn=false|qsb=false|flutter=false"
            + "|qsk=false|lids=false",
            "qrm|class=TQrmStation"
            + $"|state={(qrm.IsSending ? "stSending" : "stListening")}"
            + $"|call={qrm.MyCall}"
            + $"|his-call={qrm.HisCall}"
            + $"|r1-single-bits={SingleBits(qrm.R1)}"
            + $"|amplitude-single-bits={SingleBits(qrm.Amplitude)}"
            + $"|pitch-offset-hz={Format(qrm.PitchOffsetHz)}"
            + $"|wpm-s={Format(qrm.SendingWordsPerMinute)}"
            + $"|wpm-c={Format(qrm.CharacterWordsPerMinute)}"
            + $"|message-set={qrm.MessageSet}"
            + $"|message-text={qrm.MessageText}",
            "catalog"
            + "|pick-station-calls="
            + Format(observation.IdentitySelectionCount + 1)
            + "|get-call-calls="
            + Format(observation.IdentitySelectionCount + 1)
            + "|get-exchange-calls="
            + Format(observation.Candidates.Count)
            + "|drop-station-calls=0|qrm-id=9000"
            + "|caller-ids="
            + String.Join(
                ',',
                observation.Candidates.Select(
                    candidate => Format(candidate.Attempt)))
            + $"|all-calls={input.CollisionCall}",
        };

        for (int attempt = 1; attempt <= input.RetryLimit; attempt++)
        {
            CallerCandidateParityObservation? candidate =
                observation.Candidates.SingleOrDefault(
                    item => item.Attempt == attempt);
            if (candidate is null)
            {
                values.Add($"attempt[{attempt}]|missing=true");
                continue;
            }

            bool isAccepted = observation.AcceptedAttempt == attempt;
            string outcome = isAccepted
                ? attempt == input.AcceptedAttempt
                    ? "accepted-unconditionally"
                    : "accepted-early"
                : "discarded";
            values.Add(
                $"attempt[{attempt}]"
                + $"|id={Format(attempt)}"
                + $"|call={candidate.Identity.Callsign}"
                + "|r1-random-ordinal="
                + Format(FindRandomOrdinal(input.Seed, candidate.R1))
                + $"|r1-single-bits={SingleBits(candidate.R1)}"
                + "|wpm-s="
                + Format(candidate.SendingWordsPerMinute)
                + "|wpm-c="
                + Format(candidate.CharacterWordsPerMinute)
                + $"|skills={Format(candidate.Skills)}"
                + $"|patience={Format(candidate.Patience)}"
                + "|operator-state="
                + ToLegacyOperatorState(candidate.OperatorState)
                + "|collision-checked="
                + (attempt < input.AcceptedAttempt ? "True" : "False")
                + $"|outcome={outcome}");
        }

        values.Add(
            "collision-outcome"
            + $"|retry-limit={Format(input.RetryLimit)}"
            + "|checked-attempts=1,2,3,4,5,6,7,8,9"
            + "|discarded-attempts="
            + String.Join(
                ',',
                observation.Candidates
                    .Where(
                        candidate =>
                            candidate.Attempt
                            != observation.AcceptedAttempt)
                    .Select(
                        candidate => Format(candidate.Attempt)))
            + "|unchecked-attempt=10"
            + "|accepted-attempt="
            + Format(observation.AcceptedAttempt)
            + "|station-count="
            + Format(1 + (observation.AcceptedCaller is null ? 0 : 1))
            + "|duplicate-active-calls="
            + Format(observation.DuplicateActiveCallsignCount)
            + "|qrm-retained=true");

        ActiveStationSnapshot? accepted = observation.AcceptedCaller;
        CallerCandidateParityObservation? acceptedCandidate =
            observation.Candidates.SingleOrDefault(
                candidate =>
                    candidate.Attempt == observation.AcceptedAttempt);
        if (accepted is null || acceptedCandidate is null)
        {
            values.Add("accepted-caller|missing=true");
        }
        else
        {
            values.Add(
                "accepted-caller|class=TDxStation"
                + $"|call={accepted.Callsign}"
                + "|oper-id="
                + Format(observation.AcceptedAttempt)
                + "|state="
                + ToLegacyStationState(accepted.StationState)
                + "|operator-state="
                + ToLegacyOperatorState(accepted.OperatorState)
                + "|operator-patience="
                + Format(accepted.Patience)
                + "|operator-skills="
                + Format(acceptedCandidate.Skills)
                + "|r1-single-bits="
                + SingleBits(acceptedCandidate.R1)
                + "|amplitude-single-bits="
                + SingleBits(acceptedCandidate.Amplitude)
                + "|pitch-offset-hz="
                + Format(accepted.PitchOffsetHz)
                + "|wpm-s="
                + Format(accepted.WordsPerMinute)
                + "|wpm-c="
                + Format(acceptedCandidate.CharacterWordsPerMinute)
                + $"|rst={accepted.TrueRst}"
                + "|nr="
                + Format(acceptedCandidate.Identity.Number)
                + $"|exch1={accepted.TrueExchange1}"
                + $"|exch2={accepted.TrueExchange2}"
                + $"|op-name={observation.AcceptedOperatorName}"
                + $"|user-text={observation.AcceptedUserText}");
        }

        values.Add(
            "terminal-random|ordinal="
            + Format(
                FindRandomOrdinal(
                    input.Seed,
                    observation.TerminalRandom))
            + $"|value={Format(observation.TerminalRandom)}"
            + "|single-bits="
            + SingleBits(observation.TerminalRandom));
        if (values.Count != QrmCallerCollisionInput.ExpectedValueCount)
        {
            throw new InvalidDataException(
                "The caller collision target emitted an invalid row "
                + "count.");
        }

        return [.. values];
    }

    private static int FindRandomOrdinal(int seed, float target)
    {
        var random = new LegacyRandom(seed);
        uint targetBits = BitConverter.SingleToUInt32Bits(target);
        for (int ordinal = 0; ordinal < 100_000; ordinal++)
        {
            if (BitConverter.SingleToUInt32Bits(random.NextSingle())
                == targetBits)
            {
                return ordinal;
            }
        }

        return -1;
    }

    private static string ToLegacyOperatorState(OperatorState state) =>
        state switch
        {
            OperatorState.NeedPreviousEnd => "osNeedPrevEnd",
            OperatorState.NeedQso => "osNeedQso",
            OperatorState.NeedNumber => "osNeedNr",
            OperatorState.NeedCall => "osNeedCall",
            OperatorState.NeedCallAndNumber => "osNeedCallNr",
            OperatorState.NeedEnd => "osNeedEnd",
            OperatorState.Done => "osDone",
            OperatorState.Failed => "osFailed",
            _ => throw new InvalidOperationException(
                $"Unknown operator state '{state}'."),
        };

    private static string ToLegacyStationState(StationState state) =>
        state switch
        {
            StationState.Listening => "stListening",
            StationState.Copying => "stCopying",
            StationState.PreparingToSend => "stPreparingToSend",
            StationState.Sending => "stSending",
            _ => throw new InvalidOperationException(
                $"Unknown station state '{state}'."),
        };

    private static string SingleBits(float value) =>
        BitConverter.SingleToUInt32Bits(value)
            .ToString("x8", CultureInfo.InvariantCulture);

    private static string Format(float value) =>
        ((double)value).ToString(
            "0.000000000",
            CultureInfo.InvariantCulture);

    private static string Format(int value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
