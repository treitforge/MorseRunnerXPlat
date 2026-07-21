using System.Globalization;
using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Dsp;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatJarlRandomCutRemoteExchangeFormatTarget :
    IParityTarget
{
    internal const string ParityId =
        "contest.jarl-random-cut-remote-exchange-format-seed-12345";
    internal const string FunctionalDivergenceCode =
        "contest-jarl-random-cut-remote-exchange-format-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Engine.SimulatedStation.ObserveExchangeForParity";

    public Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!StringComparer.Ordinal.Equals(scenario.Id, ParityId))
        {
            return Task.FromResult(
                new ParityObservation(
                    ParityTargetOutcome.Failed,
                    [],
                    DomainErrorCodes.UnsupportedCapability,
                    EvidenceSource));
        }

        JarlRandomCutRemoteExchangeFormatInput input =
            JarlRandomCutRemoteExchangeFormatInput.Parse(scenario);
        string[] values = Observe(input);
        bool matches = values.SequenceEqual(
            scenario.ExpectedValues,
            StringComparer.Ordinal);
        return Task.FromResult(
            new ParityObservation(
                matches
                    ? ParityTargetOutcome.Passed
                    : ParityTargetOutcome.Failed,
                values,
                matches ? null : FunctionalDivergenceCode,
                EvidenceSource));
    }

    internal static string[] Observe(
        JarlRandomCutRemoteExchangeFormatInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        string rst = Format(input.Rst);
        string allJa = ObserveContest(
            input,
            "scAllJa",
            input.AllJaExchange2,
            rst);
        string acag = ObserveContest(
            input,
            "scAcag",
            input.AcagExchange2,
            rst);
        return
        [
            "configuration"
            + $"|scenario={ParityId}"
            + $"|station={input.StationCall}"
            + $"|seed={Format(input.Seed)}"
            + "|formatter-checkpoint-draw="
            + Format(input.FormatterCheckpointDraw)
            + $"|run-mode={input.RunModeId}",
            "contest[0]"
            + "|id=scAllJa"
            + $"|call={input.RemoteCall}"
            + $"|rst={rst}"
            + $"|exchange1={rst}"
            + $"|exchange2={input.AllJaExchange2}"
            + $"|formatted={allJa}",
            "contest[1]"
            + "|id=scAcag"
            + $"|call={input.RemoteCall}"
            + $"|rst={rst}"
            + $"|exchange1={rst}"
            + $"|exchange2={input.AcagExchange2}"
            + $"|formatted={acag}",
        ];
    }

    private static string ObserveContest(
        JarlRandomCutRemoteExchangeFormatInput input,
        string contestId,
        string exchange2,
        string rst)
    {
        var random = new LegacyRandom(input.Seed);
        var station = new SimulatedStation(
            new StationIdentity(
                input.RemoteCall,
                rst,
                Number: 0,
                rst,
                exchange2),
            wordsPerMinute: 25,
            pitchOffsetHz: 0,
            random,
            OperatorRunMode.Pileup,
            contestId: new(contestId));
        const int ConstructorDrawCount = 2;
        for (int draw = ConstructorDrawCount;
             draw < input.FormatterCheckpointDraw;
             draw++)
        {
            _ = random.NextDouble();
        }

        return station.ObserveExchangeForParity();
    }

    private static string Format(int value) =>
        value.ToString(CultureInfo.InvariantCulture);
}

internal sealed record JarlRandomCutRemoteExchangeFormatInput(
    string RunModeId,
    string StationCall,
    string RemoteCall,
    int Rst,
    string AllJaExchange2,
    string AcagExchange2,
    int FormatterCheckpointDraw,
    int Seed)
{
    public static JarlRandomCutRemoteExchangeFormatInput Parse(
        ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames =
        [
            "acagExchange2",
            "allJaExchange2",
            "formatterCheckpointDraw",
            "remoteCall",
            "rst",
            "runModeId",
            "scenario",
            "seed",
            "stationCall",
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

        var result = new JarlRandomCutRemoteExchangeFormatInput(
            RequireString(input, "runModeId", scenario.Id),
            RequireString(input, "stationCall", scenario.Id),
            RequireString(input, "remoteCall", scenario.Id),
            RequireInt32(input, "rst", scenario.Id),
            RequireString(input, "allJaExchange2", scenario.Id),
            RequireString(input, "acagExchange2", scenario.Id),
            RequireInt32(input, "formatterCheckpointDraw", scenario.Id),
            RequireInt32(input, "seed", scenario.Id));
        string discriminator = RequireString(
            input,
            "scenario",
            scenario.Id);
        if (discriminator
                != XPlatJarlRandomCutRemoteExchangeFormatTarget.ParityId
            || result.RunModeId != "rmPileup"
            || result.StationCall != "W7SST"
            || result.RemoteCall != "JA1ABC"
            || result.Rst != 599
            || result.AllJaExchange2 != "109H"
            || result.AcagExchange2 != "1009H"
            || result.FormatterCheckpointDraw != 3
            || result.Seed != 12345
            || scenario.ExpectedValues.Count != 3)
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
