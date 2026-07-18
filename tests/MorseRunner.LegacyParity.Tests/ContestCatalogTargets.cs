using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MorseRunner.LegacyParity.Tests;

public sealed partial class LegacyContestCatalogTarget : IParityTarget
{
    public async Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        string iniPath = Path.Combine(RepositoryPaths.LegacyRoot, "Ini.pas");

        if (File.Exists(iniPath))
        {
            string source = await File.ReadAllTextAsync(iniPath, cancellationToken);
            Match match = ContestEnumerationRegex().Match(source);

            if (!match.Success)
            {
                return new ParityObservation(
                    ParityTargetOutcome.Failed,
                    [],
                    "legacy-contest-enumeration-not-found",
                    iniPath);
            }

            string[] values = match.Groups["values"].Value
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            return Observe(scenario, values, iniPath);
        }

        string fixturePath = Path.Combine(
            RepositoryPaths.Root,
            "tests",
            "parity",
            "fixtures",
            "legacy",
            "catalog-contest-enumeration.json");
        await using FileStream stream = File.OpenRead(fixturePath);
        LegacyContestCatalogFixture? fixture =
            await JsonSerializer.DeserializeAsync<LegacyContestCatalogFixture>(
                stream,
                cancellationToken: cancellationToken);

        return fixture is null
            ? new ParityObservation(
                ParityTargetOutcome.Failed,
                [],
                "invalid-legacy-fixture",
                fixturePath)
            : Observe(scenario, fixture.Values, fixturePath);
    }

    private static ParityObservation Observe(
        ParityScenario scenario,
        IReadOnlyList<string> actual,
        string evidenceSource)
    {
        bool matches = actual.SequenceEqual(
            scenario.ExpectedValues,
            StringComparer.Ordinal);

        return new ParityObservation(
            matches ? ParityTargetOutcome.Passed : ParityTargetOutcome.Failed,
            actual,
            matches ? null : "legacy-observation-mismatch",
            evidenceSource);
    }

    [GeneratedRegex(
        @"TSimContest\s*=\s*\((?<values>.*?)\);",
        RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ContestEnumerationRegex();

    private sealed record LegacyContestCatalogFixture(
        [property: JsonPropertyName("revision")] string Revision,
        [property: JsonPropertyName("values")] IReadOnlyList<string> Values);
}

public sealed class MissingXPlatContestCatalogTarget : IParityTarget
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
                "MorseRunnerXPlat Phase 0 testability seam"));
    }
}
