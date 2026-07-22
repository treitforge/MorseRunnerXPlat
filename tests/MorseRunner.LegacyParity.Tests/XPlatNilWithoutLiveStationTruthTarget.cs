using System.Globalization;
using System.Text.Json;
using MorseRunner.Audio;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatNilWithoutLiveStationTruthTarget : IParityTarget
{
    internal const string ParityId =
        "logging.nil-without-live-station-truth-seed-12345";
    internal const string FunctionalDivergenceCode =
        "logging-nil-without-live-station-truth-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Engine.MorseRunnerEngine.ExecuteAsync"
        + "+MorseRunner.Engine.EngineSession.ApplyLogQsoCore";

    private static readonly ClientId ParityClient =
        new("parity-nil-without-live-station-truth");

    public async Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        if (!StringComparer.Ordinal.Equals(scenario.Id, ParityId))
        {
            return new ParityObservation(
                ParityTargetOutcome.Failed,
                [],
                DomainErrorCodes.UnsupportedCapability,
                EvidenceSource);
        }

        NilWithoutLiveStationTruthInput input =
            NilWithoutLiveStationTruthInput.Parse(scenario);
        string[] values = await ObserveAsync(input, cancellationToken);
        bool matches = values.SequenceEqual(
            scenario.ExpectedValues,
            StringComparer.Ordinal);
        return new ParityObservation(
            matches
                ? ParityTargetOutcome.Passed
                : ParityTargetOutcome.Failed,
            values,
            matches ? null : FunctionalDivergenceCode,
            EvidenceSource);
    }

    internal static async Task<string[]> ObserveAsync(
        NilWithoutLiveStationTruthInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var engine =
            new MorseRunnerEngine(_ => new NullAudioSink());
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(input.Seed) with
            {
                ContestId = new(input.ContestId),
                RunModeId = new("rmSingle"),
                Activity = 1,
            },
            cancellationToken);
        await RequireAcceptedAsync(
            engine,
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                ParityClient),
            cancellationToken);
        await RequireAcceptedAsync(
            engine,
            new LogQsoCommand(
                RequestId.New(),
                handle.SessionId,
                ParityClient,
                input.Call,
                input.Rst.ToString(CultureInfo.InvariantCulture),
                input.Number.ToString(CultureInfo.InvariantCulture),
                string.Empty),
            cancellationToken);

        Qso qso = Assert.Single(engine.GetCompletedQsos(handle.SessionId));
        return
        [
            "configuration"
                + $"|scenario={ParityId}"
                + $"|seed={Format(input.Seed)}"
                + $"|contest={input.ContestId}"
                + $"|call={input.Call}",
            "qso"
                + $"|call={qso.Call}"
                + $"|true-call={qso.TrueCall}"
                + $"|rst={Format(qso.Rst)}"
                + $"|true-rst={Format(qso.TrueRst)}"
                + $"|number={Format(qso.Number)}"
                + $"|true-number={Format(qso.TrueNumber)}"
                + $"|exchange-error={Format(qso.ExchangeError)}"
                + $"|error-text={qso.ErrorText}",
        ];
    }

    private static async Task RequireAcceptedAsync(
        MorseRunnerEngine engine,
        SessionCommand command,
        CancellationToken cancellationToken)
    {
        CommandResult result = await engine.ExecuteAsync(
            command,
            cancellationToken);
        if (!result.Accepted)
        {
            throw new InvalidOperationException(
                $"Parity command failed: {result.ErrorCode} {result.Message}");
        }
    }

    private static string Format(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string Format(LogError value) =>
        value switch
        {
            LogError.None => "leNONE",
            LogError.Nil => "leNIL",
            LogError.Duplicate => "leDUP",
            _ => "le" + value.ToString().ToUpperInvariant(),
        };
}

internal sealed record NilWithoutLiveStationTruthInput(
    int Seed,
    string ContestId,
    string Call,
    int Rst,
    int Number)
{
    public static NilWithoutLiveStationTruthInput Parse(
        ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames =
        [
            "call",
            "contestId",
            "number",
            "rst",
            "scenario",
            "seed",
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

        var result = new NilWithoutLiveStationTruthInput(
            RequireInt32(input, "seed", scenario.Id),
            RequireString(input, "contestId", scenario.Id),
            RequireString(input, "call", scenario.Id),
            RequireInt32(input, "rst", scenario.Id),
            RequireInt32(input, "number", scenario.Id));
        if (RequireString(input, "scenario", scenario.Id)
                != XPlatNilWithoutLiveStationTruthTarget.ParityId
            || result != new NilWithoutLiveStationTruthInput(
                12_345,
                "scWpx",
                "K1ABC",
                599,
                1)
            || scenario.ExpectedValues.Count != 2)
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
}
