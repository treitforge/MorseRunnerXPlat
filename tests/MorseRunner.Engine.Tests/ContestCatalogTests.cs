using MorseRunner.Domain;

namespace MorseRunner.Engine.Tests;

public sealed class ContestCatalogTests
{
    [Fact]
    public void CatalogPreservesLegacyContestAndRunModeOrder()
    {
        Assert.Equal(
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
            ],
            ContestCatalog.All.Select(definition => definition.Id.Value));
        Assert.Equal(
            ["rmStop", "rmPileup", "rmSingle", "rmWpx", "rmHst"],
            RunModeCatalog.All.Select(mode => mode.Value));
    }

    [Fact]
    public void CatalogPreservesLegacyDefinitionValues()
    {
        Assert.Equal(
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
            ],
            ContestCatalog.All.Select(Format));
    }

    private static string Format(ContestDefinition definition)
    {
        return String.Join(
            '|',
            definition.Id.Value == "scArrlDx"
                ? "scARRLDX"
                : definition.Id.Value,
            definition.Key,
            definition.DisplayName,
            definition.ExchangeType1,
            definition.ExchangeType2,
            definition.ExchangeFieldEditable,
            definition.ExchangeDefault);
    }
}
