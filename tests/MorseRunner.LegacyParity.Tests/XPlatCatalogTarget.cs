using MorseRunner.Domain;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatCatalogTarget : IParityTarget
{
    public Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string[] actual = scenario.Id switch
        {
            "catalog.contest-enumeration" => ContestCatalog.All
                .Select(definition => definition.Id.Value)
                .ToArray(),
            "session.run-mode-enumeration" => RunModeCatalog.All
                .Select(mode => mode.Value)
                .ToArray(),
            "catalog.contest-definitions" => ContestCatalog.All
                .Select(FormatContestDefinition)
                .ToArray(),
            _ => [],
        };
        bool matches = actual.SequenceEqual(
            scenario.ExpectedValues,
            StringComparer.Ordinal);

        return Task.FromResult(
            new ParityObservation(
                matches ? ParityTargetOutcome.Passed : ParityTargetOutcome.Failed,
                actual,
                matches ? null : DomainErrorCodes.UnsupportedCapability,
                "MorseRunner.Domain.ContestCatalog"));
    }

    private static string FormatContestDefinition(ContestDefinition definition)
    {
        string legacyEnumToken = definition.Id.Value == "scArrlDx"
            ? "scARRLDX"
            : definition.Id.Value;
        return String.Join(
            '|',
            legacyEnumToken,
            definition.Key,
            definition.DisplayName,
            definition.ExchangeType1,
            definition.ExchangeType2 == "etCqZone"
                ? "etCQZone"
                : definition.ExchangeType2,
            definition.ExchangeFieldEditable,
            definition.ExchangeDefault);
    }
}
