using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

public sealed class LegacyInventoryTarget(
    string surfacePrefix,
    Func<JsonElement, string> formatSurface) : IParityTarget
{
    public Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string inventoryPath = Path.Combine(
            RepositoryPaths.Root,
            "tests",
            "parity",
            "legacy-surface-inventory.json");
        using FileStream stream = File.OpenRead(inventoryPath);
        using JsonDocument document = JsonDocument.Parse(stream);

        string? revision = document.RootElement
            .GetProperty("reference")
            .GetProperty("revision")
            .GetString();
        if (!StringComparer.Ordinal.Equals(
                revision,
                "55bbd019c29d8cf693184ea420a17a253f16fe1e"))
        {
            return Task.FromResult(
                new ParityObservation(
                    ParityTargetOutcome.Failed,
                    [],
                    "legacy-revision-mismatch",
                    inventoryPath));
        }

        string[] actual = document.RootElement
            .GetProperty("surfaces")
            .EnumerateArray()
            .Where(
                surface => surface
                    .GetProperty("id")
                    .GetString()!
                    .StartsWith(surfacePrefix, StringComparison.Ordinal))
            .OrderBy(
                surface => surface
                    .GetProperty("details")
                    .GetProperty("ordinal")
                    .GetInt32())
            .Select(formatSurface)
            .ToArray();
        bool matches = actual.SequenceEqual(
            scenario.ExpectedValues,
            StringComparer.Ordinal);

        return Task.FromResult(
            new ParityObservation(
                matches ? ParityTargetOutcome.Passed : ParityTargetOutcome.Failed,
                actual,
                matches ? null : "legacy-observation-mismatch",
                inventoryPath));
    }
}

public sealed class MissingXPlatCapabilityTarget : IParityTarget
{
    public Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            new ParityObservation(
                ParityTargetOutcome.Failed,
                [],
                "unsupported-capability",
                $"MorseRunnerXPlat Phase 0 testability seam for {scenario.Id}"));
    }
}
