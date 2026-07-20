using System.Text.RegularExpressions;

namespace MorseRunner.LegacyParity.Tests;

public sealed partial class LegacyContestCatalogTarget : IParityTarget
{
    public async Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        string iniPath = Path.Combine(RepositoryPaths.LegacyRoot, "Ini.pas");

        if (!File.Exists(iniPath))
        {
            return new ParityObservation(
                ParityTargetOutcome.Failed,
                [],
                "legacy-source-not-found",
                iniPath);
        }

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
            .Split(
                ',',
                StringSplitOptions.TrimEntries
                | StringSplitOptions.RemoveEmptyEntries);

        return Observe(scenario, values, iniPath);
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
