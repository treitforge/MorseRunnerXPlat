using System.Globalization;
using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Dsp;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatWpxMidContestRemoteExchangeFormatTarget : IParityTarget
{
    internal const string ParityId =
        "contest.wpx-midcontest-remote-exchange-format-seed-12345";
    internal const string FunctionalDivergenceCode =
        "contest-wpx-midcontest-remote-exchange-format-mismatch";
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

        WpxMidContestRemoteExchangeFormatInput input =
            WpxMidContestRemoteExchangeFormatInput.Parse(scenario);
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
        WpxMidContestRemoteExchangeFormatInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var station = new SimulatedStation(
            new StationIdentity(
                input.RemoteCall,
                input.Rst.ToString(CultureInfo.InvariantCulture),
                Number: 57,
                input.Exchange1,
                input.Exchange2),
            wordsPerMinute: 25,
            pitchOffsetHz: 0,
            new LegacyRandom(input.Seed),
            OperatorRunMode.Wpx,
            contestId: new(input.ContestId),
            serialNumberRange: SerialNumberRangeMode.MidContest);
        string formatted = station.ObserveExchangeForParity();
        return
        [
            "configuration"
            + $"|scenario={ParityId}"
            + $"|station={input.StationCall}"
            + $"|seed={Format(input.Seed)}"
            + $"|contest={input.ContestId}"
            + $"|run-mode={input.RunModeId}"
            + "|serial-range=snMidContest",
            "remote-exchange"
            + $"|call={input.RemoteCall}"
            + $"|rst={Format(input.Rst)}"
            + $"|exchange1={input.Exchange1}"
            + $"|exchange2={input.Exchange2}"
            + $"|formatted={formatted}",
        ];
    }

    private static string Format(int value) =>
        value.ToString(CultureInfo.InvariantCulture);
}

internal sealed record WpxMidContestRemoteExchangeFormatInput(
    string ContestId,
    string RunModeId,
    string StationCall,
    string RemoteCall,
    int Rst,
    string Exchange1,
    string Exchange2,
    int Seed)
{
    public static WpxMidContestRemoteExchangeFormatInput Parse(
        ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames =
        [
            "contestId",
            "exchange1",
            "exchange2",
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

        var result = new WpxMidContestRemoteExchangeFormatInput(
            RequireString(input, "contestId", scenario.Id),
            RequireString(input, "runModeId", scenario.Id),
            RequireString(input, "stationCall", scenario.Id),
            RequireString(input, "remoteCall", scenario.Id),
            RequireInt32(input, "rst", scenario.Id),
            RequireString(input, "exchange1", scenario.Id),
            RequireString(input, "exchange2", scenario.Id),
            RequireInt32(input, "seed", scenario.Id));
        string discriminator = RequireString(
            input,
            "scenario",
            scenario.Id);
        if (discriminator
                != XPlatWpxMidContestRemoteExchangeFormatTarget.ParityId
            || result.ContestId != "scWpx"
            || result.RunModeId != "rmWpx"
            || result.StationCall != "W7SST"
            || result.RemoteCall != "K1ABC"
            || result.Rst != 599
            || result.Exchange1 != "599"
            || result.Exchange2 != "57"
            || result.Seed != 12345
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
