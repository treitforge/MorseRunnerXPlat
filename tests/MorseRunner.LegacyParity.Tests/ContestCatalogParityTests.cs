using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

public sealed class ContestCatalogParityTests
{
    private static readonly string[] ExpectedContestIds =
    [
        "scWpx",
        "scCwt",
        "scFieldDay",
        "scNaQp",
        "scHst",
        "scCQWW",
        "scArrlDx",
        "scSst",
        "scAllJa",
        "scAcag",
        "scIaruHf",
        "scArrlSS",
    ];

    [Fact]
    [Trait("Category", "LegacyV1Noncertifying")]
    public async Task ContestEnumerationMatchesSelectedTarget()
    {
        ParityScenario scenario = new(
            "catalog.contest-enumeration",
            "contest-catalog",
            ExpectedContestIds);
        SelectedParityObservation selected =
            await ParityRegressionRunner.ExecuteSelectedAsync(
                scenario,
                static () => new LegacyContestCatalogTarget(),
                static () => new XPlatCatalogTarget(),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ParityTargetOutcome.Passed,
            selected.Observation.Outcome);
        Assert.Equal(ExpectedContestIds, selected.Observation.Values);
    }

    [Fact]
    [Trait("Category", "ParityMetadata")]
    public void ManifestSeparatesContestEnumerationFromLegacyV1Evidence()
    {
        string manifestPath = Path.Combine(
            RepositoryPaths.Root,
            "tests",
            "parity",
            "parity-manifest.json");
        using FileStream stream = File.OpenRead(manifestPath);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement item = document.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Single(
                entry => entry.GetProperty("id").GetString()
                    == "catalog.contest-enumeration");

        Assert.Equal(
            "catalog.contest-enumeration",
            item.GetProperty("id").GetString());
        Assert.Equal(
            "not-authored",
            item.GetProperty("acceptanceStatus").GetString());
        Assert.Empty(item.GetProperty("caseIds").EnumerateArray());

        string evidencePath = Path.Combine(
            RepositoryPaths.Root,
            "tests",
            "parity",
            "evidence",
            "catalog-contest-enumeration.baseline.json");
        Assert.True(File.Exists(evidencePath), $"Evidence not found: {evidencePath}");
    }
}
