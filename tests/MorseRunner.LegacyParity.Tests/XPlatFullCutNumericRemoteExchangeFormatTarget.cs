using System.Globalization;
using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Dsp;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatFullCutNumericRemoteExchangeFormatTarget :
    IParityTarget
{
    internal const string ParityId =
        "contest.full-cut-numeric-remote-exchange-format-seed-12345";
    internal const string FunctionalDivergenceCode =
        "contest-full-cut-numeric-remote-exchange-format-mismatch";
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

        FullCutNumericRemoteExchangeFormatInput input =
            FullCutNumericRemoteExchangeFormatInput.Parse(scenario);
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
        FullCutNumericRemoteExchangeFormatInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        string rst = Format(input.Rst);
        var cqww = new SimulatedStation(
            new StationIdentity(
                input.CqwwRemoteCall,
                rst,
                Number: 0,
                rst,
                input.CqwwZone),
            wordsPerMinute: 25,
            pitchOffsetHz: 0,
            new LegacyRandom(input.Seed),
            OperatorRunMode.Pileup,
            contestId: new("scCQWW"));
        var arrlDx = new SimulatedStation(
            new StationIdentity(
                input.ArrlDxRemoteCall,
                rst,
                Number: 0,
                rst,
                input.ArrlDxPower),
            wordsPerMinute: 25,
            pitchOffsetHz: 0,
            new LegacyRandom(input.Seed),
            OperatorRunMode.Pileup,
            contestId: new("scArrlDx"));
        return
        [
            "configuration"
            + $"|scenario={ParityId}"
            + $"|station={input.StationCall}"
            + $"|seed={Format(input.Seed)}"
            + $"|r1-milli={Format(input.R1Milli)}"
            + $"|run-mode={input.RunModeId}",
            "contest[0]"
            + "|id=scCQWW"
            + $"|call={input.CqwwRemoteCall}"
            + $"|rst={rst}"
            + $"|exchange1={rst}"
            + $"|exchange2={input.CqwwZone}"
            + $"|formatted={cqww.ObserveExchangeForParity()}",
            "contest[1]"
            + "|id=scArrlDx"
            + $"|call={input.ArrlDxRemoteCall}"
            + $"|rst={rst}"
            + $"|exchange1={rst}"
            + $"|exchange2={input.ArrlDxPower}"
            + $"|formatted={arrlDx.ObserveExchangeForParity()}",
        ];
    }

    private static string Format(int value) =>
        value.ToString(CultureInfo.InvariantCulture);
}

internal sealed record FullCutNumericRemoteExchangeFormatInput(
    string RunModeId,
    string StationCall,
    string CqwwRemoteCall,
    string CqwwZone,
    string ArrlDxRemoteCall,
    string ArrlDxPower,
    int Rst,
    int R1Milli,
    int Seed)
{
    public static FullCutNumericRemoteExchangeFormatInput Parse(
        ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames =
        [
            "arrlDxPower",
            "arrlDxRemoteCall",
            "cqwwRemoteCall",
            "cqwwZone",
            "r1Milli",
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

        var result = new FullCutNumericRemoteExchangeFormatInput(
            RequireString(input, "runModeId", scenario.Id),
            RequireString(input, "stationCall", scenario.Id),
            RequireString(input, "cqwwRemoteCall", scenario.Id),
            RequireString(input, "cqwwZone", scenario.Id),
            RequireString(input, "arrlDxRemoteCall", scenario.Id),
            RequireString(input, "arrlDxPower", scenario.Id),
            RequireInt32(input, "rst", scenario.Id),
            RequireInt32(input, "r1Milli", scenario.Id),
            RequireInt32(input, "seed", scenario.Id));
        string discriminator = RequireString(
            input,
            "scenario",
            scenario.Id);
        if (discriminator
                != XPlatFullCutNumericRemoteExchangeFormatTarget.ParityId
            || result.RunModeId != "rmPileup"
            || result.StationCall != "W7SST"
            || result.CqwwRemoteCall != "K1ABC"
            || result.CqwwZone != "10"
            || result.ArrlDxRemoteCall != "JA1ABC"
            || result.ArrlDxPower != "100"
            || result.Rst != 599
            || result.R1Milli != 0
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
