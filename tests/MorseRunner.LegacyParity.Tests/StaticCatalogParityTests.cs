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
    [Trait("Category", "LegacyV1Noncertifying")]
    public async Task RunModesMatchSelectedTarget()
    {
        await AssertSelectedTargetAsync(
            new ParityScenario(
                "session.run-mode-enumeration",
                "session-lifecycle",
                ExpectedRunModes),
            () => new LegacyInventoryTarget(
                "legacy.ini.run-mode.",
                surface => surface.GetProperty("name").GetString()!));
    }

    [Fact]
    [Trait("Category", "LegacyV1Noncertifying")]
    public async Task ContestDefinitionsMatchSelectedTarget()
    {
        await AssertSelectedTargetAsync(
            new ParityScenario(
                "catalog.contest-definitions",
                "contest-catalog",
                ExpectedContestDefinitions),
            () => new LegacyInventoryTarget(
                "legacy.ini.contest-definition.",
                FormatContestDefinition));
    }

    [Theory]
    [Trait("Category", "ParityMetadata")]
    [InlineData("session.run-mode-enumeration")]
    [InlineData("catalog.contest-definitions")]
    public void ManifestSeparatesStaticCatalogFromLegacyV1Evidence(
        string parityId)
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
        if (parityId == "catalog.contest-definitions")
        {
            Assert.Equal(
                "partial",
                item.GetProperty("acceptanceStatus").GetString());
            Assert.Equal(
                ["catalog.contest-definition-metadata-ce-order"],
                item.GetProperty("caseIds")
                    .EnumerateArray()
                    .Select(caseId => caseId.GetString()));
        }
        else
        {
            Assert.Equal(
                "not-authored",
                item.GetProperty("acceptanceStatus").GetString());
            Assert.Empty(item.GetProperty("caseIds").EnumerateArray());
        }

        string legacyV1Name = parityId.Replace('.', '-');
        string fixturePath = Path.Combine(
            RepositoryPaths.Root,
            "tests",
            "parity",
            "fixtures",
            "legacy",
            $"{legacyV1Name}.json");
        string evidencePath = Path.Combine(
            RepositoryPaths.Root,
            "tests",
            "parity",
            "evidence",
            $"{legacyV1Name}.baseline.json");
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

    private static async Task AssertSelectedTargetAsync(
        ParityScenario scenario,
        Func<IParityTarget> createLegacy)
    {
        SelectedParityObservation selected =
            await ParityRegressionRunner.ExecuteSelectedAsync(
                scenario,
                createLegacy,
                static () => new XPlatCatalogTarget(),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ParityTargetOutcome.Passed,
            selected.Observation.Outcome);
        Assert.Equal(scenario.ExpectedValues, selected.Observation.Values);
    }
}
