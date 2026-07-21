using System.Globalization;
using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Dsp;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatDefaultTwoFieldRemoteExchangeFormatTarget :
    IParityTarget
{
    internal const string ParityId =
        "contest.default-two-field-remote-exchange-format-seed-12345";
    internal const string FunctionalDivergenceCode =
        "contest-default-two-field-remote-exchange-format-mismatch";
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

        DefaultTwoFieldRemoteExchangeFormatInput input =
            DefaultTwoFieldRemoteExchangeFormatInput.Parse(scenario);
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
        DefaultTwoFieldRemoteExchangeFormatInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        (string ContestId, string Exchange2)[] contests =
        [
            ("scArrlDx", input.ArrlDxExchange2),
            ("scAllJa", input.AllJaExchange2),
            ("scAcag", input.AcagExchange2),
            ("scIaruHf", input.IaruHfExchange2),
        ];
        var values = new List<string>(contests.Length + 1)
        {
            "configuration"
            + $"|scenario={ParityId}"
            + $"|station={input.StationCall}"
            + $"|seed={Format(input.Seed)}"
            + $"|run-mode={input.RunModeId}",
        };
        string rst = Format(input.Rst);
        for (int index = 0; index < contests.Length; index++)
        {
            (string contestId, string exchange2) = contests[index];
            var station = new SimulatedStation(
                new StationIdentity(
                    input.RemoteCall,
                    rst,
                    Number: 0,
                    rst,
                    exchange2),
                wordsPerMinute: 25,
                pitchOffsetHz: 0,
                new LegacyRandom(input.Seed),
                OperatorRunMode.Pileup,
                contestId: new(contestId));
            string formatted = station.ObserveExchangeForParity();
            values.Add(
                $"contest[{Format(index)}]"
                + $"|id={contestId}"
                + $"|call={input.RemoteCall}"
                + $"|rst={rst}"
                + $"|exchange1={rst}"
                + $"|exchange2={exchange2}"
                + $"|formatted={formatted}");
        }

        return [.. values];
    }

    private static string Format(int value) =>
        value.ToString(CultureInfo.InvariantCulture);
}

internal sealed record DefaultTwoFieldRemoteExchangeFormatInput(
    string RunModeId,
    string StationCall,
    string RemoteCall,
    int Rst,
    string ArrlDxExchange2,
    string AllJaExchange2,
    string AcagExchange2,
    string IaruHfExchange2,
    int Seed)
{
    public static DefaultTwoFieldRemoteExchangeFormatInput Parse(
        ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames =
        [
            "acagExchange2",
            "allJaExchange2",
            "arrlDxExchange2",
            "iaruHfExchange2",
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

        var result = new DefaultTwoFieldRemoteExchangeFormatInput(
            RequireString(input, "runModeId", scenario.Id),
            RequireString(input, "stationCall", scenario.Id),
            RequireString(input, "remoteCall", scenario.Id),
            RequireInt32(input, "rst", scenario.Id),
            RequireString(input, "arrlDxExchange2", scenario.Id),
            RequireString(input, "allJaExchange2", scenario.Id),
            RequireString(input, "acagExchange2", scenario.Id),
            RequireString(input, "iaruHfExchange2", scenario.Id),
            RequireInt32(input, "seed", scenario.Id));
        string discriminator = RequireString(
            input,
            "scenario",
            scenario.Id);
        if (discriminator
                != XPlatDefaultTwoFieldRemoteExchangeFormatTarget.ParityId
            || result.RunModeId != "rmPileup"
            || result.StationCall != "W7SST"
            || result.RemoteCall != "K1ABC"
            || result.Rst != 599
            || result.ArrlDxExchange2 != "MA"
            || result.AllJaExchange2 != "12H"
            || result.AcagExchange2 != "1234H"
            || result.IaruHfExchange2 != "ARRL"
            || result.Seed != 12345
            || scenario.ExpectedValues.Count != 5)
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
