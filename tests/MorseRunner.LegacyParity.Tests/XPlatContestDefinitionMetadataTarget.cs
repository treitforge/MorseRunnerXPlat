using System.Globalization;
using System.Text.Json;
using MorseRunner.Domain;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatContestDefinitionMetadataTarget : IParityTarget
{
    internal const string ParityId =
        "catalog.contest-definition-metadata-ce-order";
    internal const string FunctionalDivergenceCode =
        "catalog-contest-definition-metadata-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Domain.ContestCatalog";

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

        _ = ContestDefinitionMetadataInput.Parse(scenario);
        string[] values = ContestCatalog.All
            .Select(FormatDefinition)
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

    private static string FormatDefinition(
        ContestDefinition definition,
        int ordinal)
    {
        return String.Join(
            '|',
            "contest",
            "ordinal=" + ordinal.ToString(CultureInfo.InvariantCulture),
            "id=" + definition.Id.Value,
            "key=" + definition.Key,
            "name=" + definition.DisplayName,
            "exchange1=" + definition.ExchangeType1,
            "exchange2=" + definition.ExchangeType2,
            "caption1=<missing>",
            "caption2=<missing>",
            "editable=" + (definition.ExchangeFieldEditable
                ? "true"
                : "false"),
            "default=" + definition.ExchangeDefault,
            "message=<missing>");
    }
}

internal sealed record ContestDefinitionMetadataInput
{
    public static ContestDefinitionMetadataInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!actualNames.SequenceEqual(["scenario"], StringComparer.Ordinal)
            || input.GetProperty("scenario").GetString()
                != XPlatContestDefinitionMetadataTarget.ParityId
            || scenario.ExpectedValues.Count != 12)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' fixed vector is invalid.");
        }

        return new ContestDefinitionMetadataInput();
    }
}
