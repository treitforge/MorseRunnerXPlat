using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using MorseRunner.Audio;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatContestOperatorMessagesTarget : IParityTarget
{
    internal const string ParityId =
        "engine.contest-specific-cq-tu-station-id-seed-12345";
    internal const string FunctionalDivergenceCode =
        "engine-contest-specific-cq-tu-station-id-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Engine.MorseRunnerEngine.ExecuteAsync"
        + "+MorseRunner.Engine.EngineSession.ApplyOperatorIntent"
        + "+MorseRunner.Engine.EngineSession.ApplyLogQsoCore"
        + "+MorseRunner.Engine.EngineSession.AdvanceOneBlock";

    private static readonly ClientId ParityClient =
        new("parity-contest-operator-messages");

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

        ContestOperatorMessagesInput input =
            ContestOperatorMessagesInput.Parse(scenario);
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
        ContestOperatorMessagesInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var values = new List<string>(input.Contests.Count + 1)
        {
            "configuration"
            + $"|scenario={ParityId}"
            + $"|station={input.StationCall}"
            + $"|seed={Format(input.Seed)}"
            + $"|station-id-rate={Format(input.StationIdRate)}"
            + $"|contest-count={Format(input.Contests.Count)}",
        };

        for (int index = 0; index < input.Contests.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ContestOperatorMessageContest contest = input.Contests[index];
            ContestMessageObservation observation = await ObserveContestAsync(
                input,
                contest,
                cancellationToken);
            values.Add(
                $"contest[{Format(index)}]"
                + $"|id={contest.ContestId}"
                + $"|run-mode={contest.RunModeId}"
                + $"|cq={observation.Cq}"
                + $"|tu-before={observation.TuBefore}"
                + $"|tu-threshold={observation.TuThreshold}"
                + $"|tu-after-reset={observation.TuAfterReset}");
        }

        return [.. values];
    }

    private static async Task<ContestMessageObservation> ObserveContestAsync(
        ContestOperatorMessagesInput input,
        ContestOperatorMessageContest contest,
        CancellationToken cancellationToken)
    {
        await using var engine = new MorseRunnerEngine(
            _ => new NullAudioSink(),
            new MorseRunnerEngineOptions
            {
                AutomaticTiming = false,
            });
        SessionSettings settings = new(
            input.Seed,
            new ContestId(contest.ContestId),
            new RunModeId(contest.RunModeId),
            DurationBlocks: 0)
        {
            StationCall = input.StationCall,
            WordsPerMinute = 25,
            PitchHz = 600,
            BandwidthHz = 500,
            Activity = 1,
            StationIdRate = input.StationIdRate,
            Qsk = false,
            Qsb = false,
            Qrm = false,
            Qrn = false,
            Flutter = false,
            Lids = false,
        };
        SessionHandle handle = await engine.CreateSessionAsync(
            settings,
            cancellationToken);
        await RequireAcceptedAsync(
            engine,
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                ParityClient),
            "start",
            contest,
            cancellationToken);

        string cq = await SendAndCaptureAsync(
            engine,
            handle.SessionId,
            OperatorIntent.Cq,
            contest,
            cancellationToken);
        await AbortAsync(
            engine,
            handle.SessionId,
            contest,
            cancellationToken);
        string tuBefore = await SendAndCaptureAsync(
            engine,
            handle.SessionId,
            OperatorIntent.ThankYou,
            contest,
            cancellationToken);
        await AbortAsync(
            engine,
            handle.SessionId,
            contest,
            cancellationToken);

        await LogQsoAsync(
            engine,
            handle.SessionId,
            contest,
            cancellationToken);
        await LogQsoAsync(
            engine,
            handle.SessionId,
            contest,
            cancellationToken);
        string tuThreshold = await SendAndCaptureAsync(
            engine,
            handle.SessionId,
            OperatorIntent.ThankYou,
            contest,
            cancellationToken);
        await LogQsoAsync(
            engine,
            handle.SessionId,
            contest,
            cancellationToken);
        await RequireAcceptedAsync(
            engine,
            new AdvanceSimulationCommand(
                RequestId.New(),
                handle.SessionId,
                ParityClient,
                BlockCount: 128),
            "complete threshold TU",
            contest,
            cancellationToken);
        string tuAfterReset = await SendAndCaptureAsync(
            engine,
            handle.SessionId,
            OperatorIntent.ThankYou,
            contest,
            cancellationToken);

        return new(cq, tuBefore, tuThreshold, tuAfterReset);
    }

    private static async Task<string> SendAndCaptureAsync(
        MorseRunnerEngine engine,
        SessionId sessionId,
        OperatorIntent intent,
        ContestOperatorMessageContest contest,
        CancellationToken cancellationToken)
    {
        await RequireAcceptedAsync(
            engine,
            new SendOperatorIntentCommand(
                RequestId.New(),
                sessionId,
                ParityClient,
                intent,
                String.Empty,
                String.Empty,
                String.Empty,
                String.Empty),
            $"send {intent}",
            contest,
            cancellationToken);
        return engine.GetSnapshot(sessionId).LastOperatorMessage
            ?? throw new InvalidOperationException(
                $"Contest '{contest.ContestId}' produced no operator "
                + $"message for {intent}.");
    }

    private static Task<CommandResult> AbortAsync(
        MorseRunnerEngine engine,
        SessionId sessionId,
        ContestOperatorMessageContest contest,
        CancellationToken cancellationToken)
    {
        return RequireAcceptedAsync(
            engine,
            new SendOperatorIntentCommand(
                RequestId.New(),
                sessionId,
                ParityClient,
                OperatorIntent.Abort,
                String.Empty,
                String.Empty,
                String.Empty,
                String.Empty),
            "abort",
            contest,
            cancellationToken);
    }

    private static Task<CommandResult> LogQsoAsync(
        MorseRunnerEngine engine,
        SessionId sessionId,
        ContestOperatorMessageContest contest,
        CancellationToken cancellationToken)
    {
        return RequireAcceptedAsync(
            engine,
            new LogQsoCommand(
                RequestId.New(),
                sessionId,
                ParityClient,
                contest.Call,
                contest.Rst,
                contest.Exchange1,
                contest.Exchange2),
            "log QSO",
            contest,
            cancellationToken);
    }

    private static async Task<CommandResult> RequireAcceptedAsync(
        MorseRunnerEngine engine,
        SessionCommand command,
        string operation,
        ContestOperatorMessageContest contest,
        CancellationToken cancellationToken)
    {
        CommandResult result = await engine.ExecuteAsync(
            command,
            cancellationToken);
        if (!result.Accepted)
        {
            throw new InvalidOperationException(
                $"Contest '{contest.ContestId}' rejected {operation} "
                + $"with '{result.ErrorCode ?? "<none>"}': "
                + $"{result.Message ?? "<no message>"}.");
        }

        return result;
    }

    private static string Format(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private sealed record ContestMessageObservation(
        string Cq,
        string TuBefore,
        string TuThreshold,
        string TuAfterReset);
}

internal sealed record ContestOperatorMessagesInput(
    string StationCall,
    int Seed,
    int StationIdRate,
    IReadOnlyList<ContestOperatorMessageContest> Contests)
{
    public static ContestOperatorMessagesInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        RequireExactProperties(
            input,
            ["contests", "scenario", "seed", "stationCall", "stationIdRate"],
            scenario.Id);
        string discriminator = RequireString(input, "scenario", scenario.Id);
        string stationCall = RequireString(input, "stationCall", scenario.Id);
        int seed = RequireInt32(input, "seed", scenario.Id);
        int stationIdRate = RequireInt32(
            input,
            "stationIdRate",
            scenario.Id);
        JsonElement contestsElement = input.GetProperty("contests");
        if (contestsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' contests is not an array.");
        }

        ContestOperatorMessageContest[] contests = contestsElement
            .EnumerateArray()
            .Select((value, index) => ParseContest(value, scenario.Id, index))
            .ToArray();
        if (discriminator != XPlatContestOperatorMessagesTarget.ParityId
            || stationCall != "W7SST"
            || seed != 12345
            || stationIdRate != 3
            || contests.Length != 12
            || scenario.ExpectedValues.Count != contests.Length + 1)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' fixed vector is invalid.");
        }

        return new(
            stationCall,
            seed,
            stationIdRate,
            contests.ToImmutableArray());
    }

    private static ContestOperatorMessageContest ParseContest(
        JsonElement value,
        string scenarioId,
        int index)
    {
        RequireExactProperties(
            value,
            ["call", "contestId", "exchange1", "exchange2", "rst", "runModeId"],
            scenarioId);
        return new(
            RequireString(value, "contestId", scenarioId),
            RequireString(value, "runModeId", scenarioId),
            RequireString(value, "call", scenarioId),
            RequireString(value, "rst", scenarioId),
            RequireString(value, "exchange1", scenarioId, allowEmpty: true),
            RequireString(value, "exchange2", scenarioId, allowEmpty: true),
            index);
    }

    private static string RequireString(
        JsonElement input,
        string propertyName,
        string scenarioId,
        bool allowEmpty = false)
    {
        JsonElement value = input.GetProperty(propertyName);
        string? result = value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
        return result is not null && (allowEmpty || result.Length > 0)
            ? result
            : throw new InvalidDataException(
                $"Parity case '{scenarioId}' {propertyName} is invalid.");
    }

    private static int RequireInt32(
        JsonElement input,
        string propertyName,
        string scenarioId)
    {
        JsonElement value = input.GetProperty(propertyName);
        return value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int result)
            ? result
            : throw new InvalidDataException(
                $"Parity case '{scenarioId}' {propertyName} is invalid.");
    }

    private static void RequireExactProperties(
        JsonElement input,
        IReadOnlyList<string> expectedNames,
        string scenarioId)
    {
        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Parity case '{scenarioId}' input has unsupported fields.");
        }
    }
}

internal sealed record ContestOperatorMessageContest(
    string ContestId,
    string RunModeId,
    string Call,
    string Rst,
    string Exchange1,
    string Exchange2,
    int Index);
