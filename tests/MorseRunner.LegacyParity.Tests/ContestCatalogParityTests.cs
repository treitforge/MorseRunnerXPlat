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
    public async Task ContestEnumerationIsBothGreen()
    {
        ParityScenario scenario = new(
            "catalog.contest-enumeration",
            "contest-catalog",
            ExpectedContestIds);
        LegacyContestCatalogTarget legacyTarget = new();
        XPlatCatalogTarget xplatTarget = new();

        ParityObservation legacy = await legacyTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);
        ParityObservation xplat = await xplatTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, legacy.Outcome);
        Assert.Equal(ExpectedContestIds, legacy.Values);
        Assert.Equal(ParityTargetOutcome.Passed, xplat.Outcome);
        Assert.Equal(ExpectedContestIds, xplat.Values);
        Assert.Equal(
            ParityAssessment.BothGreen,
            ParityAssessmentClassifier.Classify(legacy, xplat));
    }

    [Fact]
    public void ManifestRetainsRedEvidenceAfterTurningGreen()
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
            "both-green",
            item.GetProperty("status").GetString());

        string evidencePath = Path.Combine(
            RepositoryPaths.Root,
            item.GetProperty("evidence").GetString()!);
        Assert.True(File.Exists(evidencePath), $"Evidence not found: {evidencePath}");
    }
}
