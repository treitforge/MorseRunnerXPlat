using System.Globalization;
using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Dsp;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatArrlDxHighR1PowerRemoteExchangeFormatTarget :
    IParityTarget
{
    internal const string ParityId =
        "contest.arrldx-high-r1-power-remote-exchange-format-seed-12345";
    internal const string FunctionalDivergenceCode =
        "contest-arrldx-high-r1-power-remote-exchange-format-mismatch";
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

        ArrlDxHighR1PowerRemoteExchangeFormatInput input =
            ArrlDxHighR1PowerRemoteExchangeFormatInput.Parse(scenario);
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
        ArrlDxHighR1PowerRemoteExchangeFormatInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        string rst = Format(input.Rst);
        SimulatedStation cqww = CreateCandidate(
            input,
            "scCQWW",
            input.CqwwRemoteCall,
            input.CqwwZone,
            rst);
        SimulatedStation arrlDx = CreateCandidate(
            input,
            "scArrlDx",
            input.ArrlDxRemoteCall,
            input.ArrlDxPower,
            rst);
        return
        [
            "configuration"
            + $"|scenario={ParityId}"
            + $"|station={input.StationCall}"
            + $"|seed={Format(input.Seed)}"
            + $"|formatter-checkpoint-draw={Format(input.FormatterCheckpointDraw)}"
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

    private static SimulatedStation CreateCandidate(
        ArrlDxHighR1PowerRemoteExchangeFormatInput input,
        string contestId,
        string remoteCall,
        string exchange2,
        string rst)
    {
        var random = new LegacyRandom(input.Seed);
        var station = SimulatedStation.CreateCandidate(
            () => new StationIdentity(
                remoteCall,
                rst,
                Number: 0,
                rst,
                exchange2),
            () => 25,
            random,
            new LegacyRandomEffects(random),
            OperatorRunMode.Pileup,
            lids: false,
            sweepstakes: false,
            flutter: false,
            new ContestId(contestId),
            SerialNumberRangeMode.StartOfContest,
            customSerialNumberMinimum: 1,
            customSerialNumberMinimumDigits: 2);
        int r1Milli = (int)MathF.Round(
            station.R1 * 1_000f,
            MidpointRounding.ToEven);
        if (r1Milli != input.R1Milli)
        {
            throw new InvalidOperationException(
                "Candidate R1 did not match the fixed parity checkpoint.");
        }

        return station;
    }

    private static string Format(int value) =>
        value.ToString(CultureInfo.InvariantCulture);
}

internal sealed record ArrlDxHighR1PowerRemoteExchangeFormatInput(
    string RunModeId,
    string StationCall,
    string CqwwRemoteCall,
    string CqwwZone,
    string ArrlDxRemoteCall,
    string ArrlDxPower,
    int Rst,
    int FormatterCheckpointDraw,
    int R1Milli,
    int Seed)
{
    public static ArrlDxHighR1PowerRemoteExchangeFormatInput Parse(
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
            "formatterCheckpointDraw",
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

        var result = new ArrlDxHighR1PowerRemoteExchangeFormatInput(
            RequireString(input, "runModeId", scenario.Id),
            RequireString(input, "stationCall", scenario.Id),
            RequireString(input, "cqwwRemoteCall", scenario.Id),
            RequireString(input, "cqwwZone", scenario.Id),
            RequireString(input, "arrlDxRemoteCall", scenario.Id),
            RequireString(input, "arrlDxPower", scenario.Id),
            RequireInt32(input, "rst", scenario.Id),
            RequireInt32(input, "formatterCheckpointDraw", scenario.Id),
            RequireInt32(input, "r1Milli", scenario.Id),
            RequireInt32(input, "seed", scenario.Id));
        string discriminator = RequireString(
            input,
            "scenario",
            scenario.Id);
        if (discriminator
                != XPlatArrlDxHighR1PowerRemoteExchangeFormatTarget.ParityId
            || result.RunModeId != "rmPileup"
            || result.StationCall != "W7SST"
            || result.CqwwRemoteCall != "K1ABC"
            || result.CqwwZone != "10"
            || result.ArrlDxRemoteCall != "JA1ABC"
            || result.ArrlDxPower != "100"
            || result.Rst != 599
            || result.FormatterCheckpointDraw != 7
            || result.R1Milli != 930
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
