using System.Globalization;
using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Dsp;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatJarlTruthColumnMappingTarget : IParityTarget
{
    internal const string ParityId =
        "contest.jarl-call-history-truth-column-mapping";
    internal const string FunctionalDivergenceCode =
        "contest-jarl-truth-column-mapping-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Engine.StationReferenceCatalog.Pick";

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

        JarlTruthColumnMappingInput input =
            JarlTruthColumnMappingInput.Parse(scenario);
        string[] values = input.ContestIds
            .Select(
                (contestId, ordinal) => ObserveContest(
                    contestId,
                    input.Seed,
                    ordinal))
            .ToArray();
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

    private static string ObserveContest(
        string contestId,
        int seed,
        int ordinal)
    {
        ContestId id = new(contestId);
        StationIdentity station = StationReferenceCatalog
            .Load(id)
            .Pick(new LegacyRandom(seed), id, serialNumber: 1);
        return "vector|ordinal="
            + ordinal.ToString(CultureInfo.InvariantCulture)
            + "|contest=" + contestId
            + "|seed=" + seed.ToString(CultureInfo.InvariantCulture)
            + "|call=" + station.Callsign
            + "|exchange1=" + station.Exchange1
            + "|exchange2=" + station.Exchange2;
    }
}

internal sealed record JarlTruthColumnMappingInput(
    int Seed,
    IReadOnlyList<string> ContestIds)
{
    public static JarlTruthColumnMappingInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        RequireExactProperties(
            input,
            ["scenario", "seed", "vectors"],
            scenario.Id);
        if (input.GetProperty("scenario").GetString()
                != XPlatJarlTruthColumnMappingTarget.ParityId
            || !input.GetProperty("seed").TryGetInt32(out int seed)
            || input.GetProperty("vectors").ValueKind
                != JsonValueKind.Array)
        {
            throw Invalid(scenario.Id);
        }

        string[] contestIds = input.GetProperty("vectors")
            .EnumerateArray()
            .Select(
                value =>
                {
                    RequireExactProperties(
                        value,
                        ["contestId"],
                        scenario.Id);
                    return value.GetProperty("contestId").GetString()
                        ?? string.Empty;
                })
            .ToArray();
        if (!contestIds.SequenceEqual(
                ["scAllJa", "scAcag"],
                StringComparer.Ordinal)
            || scenario.ExpectedValues.Count != contestIds.Length)
        {
            throw Invalid(scenario.Id);
        }

        for (int index = 0; index < contestIds.Length; index++)
        {
            string prefix = "vector|ordinal="
                + index.ToString(CultureInfo.InvariantCulture)
                + "|contest=" + contestIds[index]
                + "|seed=" + seed.ToString(CultureInfo.InvariantCulture)
                + "|";
            if (!scenario.ExpectedValues[index].StartsWith(
                    prefix,
                    StringComparison.Ordinal))
            {
                throw Invalid(scenario.Id);
            }
        }

        return new(seed, contestIds);
    }

    private static void RequireExactProperties(
        JsonElement input,
        IReadOnlyList<string> expectedNames,
        string scenarioId)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(scenarioId);
        }

        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal))
        {
            throw Invalid(scenarioId);
        }
    }

    private static InvalidDataException Invalid(string scenarioId) =>
        new($"Parity case '{scenarioId}' fixed vector is invalid.");
}
