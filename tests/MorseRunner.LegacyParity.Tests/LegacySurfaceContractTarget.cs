using System.Text.Encodings.Web;
using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

public sealed class LegacySurfaceContractTarget(
    IReadOnlyList<string> surfacePrefixes) : IParityTarget
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

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
        string[] actual = document.RootElement
            .GetProperty("surfaces")
            .EnumerateArray()
            .Where(
                surface =>
                {
                    string id = surface.GetProperty("id").GetString()!;
                    return surfacePrefixes.Any(
                        prefix => id.StartsWith(prefix, StringComparison.Ordinal));
                })
            .OrderBy(
                surface => surface.GetProperty("id").GetString(),
                StringComparer.Ordinal)
            .Select(FormatSurface)
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

    private static string FormatSurface(JsonElement surface)
    {
        return String.Join(
            '|',
            surface.GetProperty("id").GetString(),
            surface.GetProperty("name").GetString(),
            JsonSerializer.Serialize(
                surface.GetProperty("details"),
                SerializerOptions));
    }
}
