using System.Globalization;
using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatOwnExchangeTokenizationTarget : IParityTarget
{
    internal const string ParityId =
        "contest.own-exchange-tokenization-boundaries";
    internal const string FunctionalDivergenceCode =
        "contest-own-exchange-tokenization-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Engine.ContestQsoRules.ValidateOwnExchange";

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

        OwnExchangeTokenizationInput input =
            OwnExchangeTokenizationInput.Parse(scenario);
        string[] values = input.Vectors
            .Select(ObserveVector)
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

    private static string ObserveVector(
        OwnExchangeTokenizationVector vector,
        int ordinal)
    {
        ContestValidation validation = ContestRulesCatalog
            .Get(new ContestId(vector.ContestId))
            .ValidateMyExchange(vector.Exchange);
        return "vector|ordinal="
            + ordinal.ToString(CultureInfo.InvariantCulture)
            + "|contest=" + vector.ContestId
            + "|exchange=" + vector.Exchange
            + "|valid=" + validation.IsValid.ToString().ToLowerInvariant()
            + "|error=" + validation.Error;
    }
}

internal sealed record OwnExchangeTokenizationVector(
    string ContestId,
    string Exchange);

internal sealed record OwnExchangeTokenizationInput(
    IReadOnlyList<OwnExchangeTokenizationVector> Vectors)
{
    public static OwnExchangeTokenizationInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        RequireExactProperties(
            input,
            ["scenario", "vectors"],
            scenario.Id);
        if (input.GetProperty("scenario").GetString()
                != XPlatOwnExchangeTokenizationTarget.ParityId
            || input.GetProperty("vectors").ValueKind
                != JsonValueKind.Array)
        {
            throw Invalid(scenario.Id);
        }

        var vectors = new List<OwnExchangeTokenizationVector>();
        foreach (JsonElement value in input.GetProperty("vectors")
                     .EnumerateArray())
        {
            RequireExactProperties(
                value,
                ["contestId", "exchange"],
                scenario.Id);
            string contestId = value.GetProperty("contestId").GetString()
                ?? string.Empty;
            string exchange = value.GetProperty("exchange").GetString()
                ?? string.Empty;
            if (!ContestCatalog.All.Any(
                    definition => definition.Id.Value == contestId))
            {
                throw Invalid(scenario.Id);
            }

            vectors.Add(new(contestId, exchange));
        }

        if (vectors.Count != 23
            || scenario.ExpectedValues.Count != vectors.Count)
        {
            throw Invalid(scenario.Id);
        }

        for (int index = 0; index < vectors.Count; index++)
        {
            OwnExchangeTokenizationVector vector = vectors[index];
            string prefix = "vector|ordinal="
                + index.ToString(CultureInfo.InvariantCulture)
                + "|contest=" + vector.ContestId
                + "|exchange=" + vector.Exchange + "|";
            if (!scenario.ExpectedValues[index].StartsWith(
                    prefix,
                    StringComparison.Ordinal))
            {
                throw Invalid(scenario.Id);
            }
        }

        return new(vectors);
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
