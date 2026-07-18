using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

public sealed class StaticCatalogParityTests
{
    private static readonly string[] ExpectedRunModes =
    [
        "rmStop",
        "rmPileup",
        "rmSingle",
        "rmWpx",
        "rmHst",
    ];

    private static readonly string[] ExpectedContestDefinitions =
    [
        "scWpx|CqWpx|CQ WPX|etRST|etSerialNr|True|5NN #",
        "scCwt|Cwt|CWOPS CWT|etOpName|etGenericField|True|DAVID 123",
        "scFieldDay|ArrlFd|ARRL Field Day|etFdClass|etArrlSection|True|3A OR",
        "scNaQp|NAQP|NCJ NAQP|etOpName|etNaQpExch2|True|ALEX ON",
        "scHst|HST|HST (High Speed Test)|etRST|etSerialNr|False|5NN #",
        "scCQWW|CQWW|CQ WW|etRST|etCQZone|True|5NN 3",
        "scARRLDX|ArrlDx|ARRL DX|etRST|etStateProv|True|5NN ON",
        "scSst|Sst|K1USN Slow Speed Test|etOpName|etGenericField|True|BRUCE MA",
        "scAllJa|AllJa|JARL ALL JA|etRST|etJaPref|True|5NN 10H",
        "scAcag|Acag|JARL ACAG|etRST|etJaCity|True|5NN 1002H",
        "scIaruHf|IaruHf|IARU HF|etRST|etGenericField|True|5NN 6",
        "scArrlSS|SSCW|ARRL Sweepstakes|etSSNrPrecedence|etSSCheckSection|True|A 72 OR",
    ];

    [Fact]
    public async Task RunModesAreBothGreen()
    {
        await AssertBothGreenAsync(
            new ParityScenario(
                "session.run-mode-enumeration",
                "session-lifecycle",
                ExpectedRunModes),
            new LegacyInventoryTarget(
                "legacy.ini.run-mode.",
                surface => surface.GetProperty("name").GetString()!));
    }

    [Fact]
    public async Task ContestDefinitionsAreBothGreen()
    {
        await AssertBothGreenAsync(
            new ParityScenario(
                "catalog.contest-definitions",
                "contest-catalog",
                ExpectedContestDefinitions),
            new LegacyInventoryTarget(
                "legacy.ini.contest-definition.",
                FormatContestDefinition));
    }

    [Theory]
    [InlineData("session.run-mode-enumeration")]
    [InlineData("catalog.contest-definitions")]
    public void ManifestRetainsStaticCatalogEvidence(string parityId)
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
            .Single(entry => entry.GetProperty("id").GetString() == parityId);

        Assert.Equal(
            "both-green",
            item.GetProperty("status").GetString());
        Assert.Equal("pass", item.GetProperty("legacyTestStatus").GetString());
        Assert.Equal("pass", item.GetProperty("xplatTestStatus").GetString());

        string fixturePath = Path.Combine(
            RepositoryPaths.Root,
            item.GetProperty("fixture").GetString()!);
        string evidencePath = Path.Combine(
            RepositoryPaths.Root,
            item.GetProperty("evidence").GetString()!);
        Assert.True(File.Exists(fixturePath), $"Fixture not found: {fixturePath}");
        Assert.True(File.Exists(evidencePath), $"Evidence not found: {evidencePath}");
    }

    private static string FormatContestDefinition(JsonElement surface)
    {
        JsonElement details = surface.GetProperty("details");
        return String.Join(
            '|',
            details.GetProperty("enum").GetString(),
            details.GetProperty("key").GetString(),
            surface.GetProperty("name").GetString(),
            details.GetProperty("exchangeType1").GetString(),
            details.GetProperty("exchangeType2").GetString(),
            details.GetProperty("exchangeFieldEditable").GetString(),
            details.GetProperty("exchangeDefault").GetString());
    }

    private static async Task AssertBothGreenAsync(
        ParityScenario scenario,
        LegacyInventoryTarget legacyTarget)
    {
        XPlatCatalogTarget xplatTarget = new();
        ParityObservation legacy = await legacyTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);
        ParityObservation xplat = await xplatTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, legacy.Outcome);
        Assert.Equal(scenario.ExpectedValues, legacy.Values);
        Assert.Equal(ParityTargetOutcome.Passed, xplat.Outcome);
        Assert.Equal(scenario.ExpectedValues, xplat.Values);
        Assert.Equal(
            ParityAssessment.BothGreen,
            ParityAssessmentClassifier.Classify(legacy, xplat));
    }
}
