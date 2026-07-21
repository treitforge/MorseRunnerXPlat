using System.Globalization;
using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Dsp;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatLidSerialCorrectionRemoteExchangeFormatTarget :
    IParityTarget
{
    internal const string ParityId =
        "contest.lid-serial-correction-remote-exchange-format-seed-16";
    internal const string FunctionalDivergenceCode =
        "contest-lid-serial-correction-remote-exchange-format-mismatch";
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

        LidSerialCorrectionRemoteExchangeFormatInput input =
            LidSerialCorrectionRemoteExchangeFormatInput.Parse(scenario);
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
        LidSerialCorrectionRemoteExchangeFormatInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        string rst = Format(input.Rst);
        SimulatedStation station = CreateCandidate(input, rst);
        return
        [
            "configuration"
            + $"|scenario={ParityId}"
            + $"|station={input.StationCall}"
            + $"|seed={Format(input.Seed)}"
            + "|formatter-checkpoint-draw="
            + Format(input.FormatterCheckpointDraw)
            + $"|r1-milli={Format(input.R1Milli)}"
            + "|lids=true"
            + $"|run-mode={input.RunModeId}",
            "contest[0]"
            + "|id=scWpx"
            + $"|call={input.RemoteCall}"
            + $"|rst={rst}"
            + $"|exchange1={rst}"
            + $"|exchange2={Format(input.Serial)}"
            + $"|formatted={station.ObserveExchangeForParity()}",
        ];
    }

    private static SimulatedStation CreateCandidate(
        LidSerialCorrectionRemoteExchangeFormatInput input,
        string rst)
    {
        var random = new LegacyRandom(input.Seed);
        var station = SimulatedStation.CreateCandidate(
            () => new StationIdentity(
                input.RemoteCall,
                rst,
                input.Serial,
                rst,
                Format(input.Serial)),
            () => 25,
            random,
            new LegacyRandomEffects(random),
            OperatorRunMode.Wpx,
            input.Lids,
            sweepstakes: false,
            flutter: false,
            new ContestId("scWpx"),
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

internal sealed record LidSerialCorrectionRemoteExchangeFormatInput(
    string RunModeId,
    string StationCall,
    string RemoteCall,
    int Rst,
    int Serial,
    int FormatterCheckpointDraw,
    int R1Milli,
    bool Lids,
    int Seed)
{
    public static LidSerialCorrectionRemoteExchangeFormatInput Parse(
        ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames =
        [
            "formatterCheckpointDraw",
            "lids",
            "r1Milli",
            "remoteCall",
            "rst",
            "runModeId",
            "scenario",
            "seed",
            "serial",
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

        var result = new LidSerialCorrectionRemoteExchangeFormatInput(
            RequireString(input, "runModeId", scenario.Id),
            RequireString(input, "stationCall", scenario.Id),
            RequireString(input, "remoteCall", scenario.Id),
            RequireInt32(input, "rst", scenario.Id),
            RequireInt32(input, "serial", scenario.Id),
            RequireInt32(input, "formatterCheckpointDraw", scenario.Id),
            RequireInt32(input, "r1Milli", scenario.Id),
            RequireBoolean(input, "lids", scenario.Id),
            RequireInt32(input, "seed", scenario.Id));
        string discriminator = RequireString(
            input,
            "scenario",
            scenario.Id);
        if (discriminator
                != XPlatLidSerialCorrectionRemoteExchangeFormatTarget.ParityId
            || result.RunModeId != "rmWpx"
            || result.StationCall != "W7SST"
            || result.RemoteCall != "K1ABC"
            || result.Rst != 599
            || result.Serial != 123
            || result.FormatterCheckpointDraw != 9
            || result.R1Milli != 223
            || !result.Lids
            || result.Seed != 16
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

    private static bool RequireBoolean(
        JsonElement input,
        string propertyName,
        string scenarioId)
    {
        JsonElement value = input.GetProperty(propertyName);
        return value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw new InvalidDataException(
                $"Parity case '{scenarioId}' {propertyName} is invalid.");
    }
}
