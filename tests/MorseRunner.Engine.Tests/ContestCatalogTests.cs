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
                "scWpx|CqWpx|CQ WPX|etRST|etSerialNr|||True|5NN #|'RST <serial>' (e.g. 5NN #|123)",
                "scCwt|Cwt|CWOPS CWT|etOpName|etGenericField|Name|Exch|True|DAVID 123|'<op name> <CWOPS Number|State|Country>' (e.g. DAVID 123)",
                "scFieldDay|ArrlFd|ARRL Field Day|etFdClass|etArrlSection|||True|3A OR|'<class> <section>' (e.g. 3A OR)",
                "scNaQp|NAQP|NCJ NAQP|etOpName|etNaQpExch2|||True|ALEX ON|'<name> [<state|prov|dxcc-entity>]' (e.g. ALEX ON)",
                "scHst|HST|HST (High Speed Test)|etRST|etSerialNr|||False|5NN #|'RST <serial>' (e.g. 5NN #)",
                "scCQWW|CQWW|CQ WW|etRST|etCqZone|||True|5NN 3|'RST <cq-zone>' (e.g. 5NN 3)",
                "scArrlDx|ArrlDx|ARRL DX|etRST|etStateProv|||True|5NN ON|'RST <state|province|power>' (e.g. 5NN ON)",
                "scSst|Sst|K1USN Slow Speed Test|etOpName|etGenericField|Name|State/Prov/DX|True|BRUCE MA|'<op name> <State|Prov|DX>' (e.g. BRUCE MA)",
                "scAllJa|AllJa|JARL ALL JA|etRST|etJaPref|||True|5NN 10H|'RST <Pref><Power>' (e.g. 5NN 10H)",
                "scAcag|Acag|JARL ACAG|etRST|etJaCity|||True|5NN 1002H|'RST <City|Gun|Ku><Power>' (e.g. 5NN 1002H)",
                "scIaruHf|IaruHf|IARU HF|etRST|etGenericField|RST|Zone/Soc|True|5NN 6|'RST <Itu-zone|IARU Society>' (e.g. 5NN 6)",
                "scArrlSS|SSCW|ARRL Sweepstakes|etSSNrPrecedence|etSSCheckSection|||True|A 72 OR|'[#|123] <precedence> <check> <section>' (e.g. A 72 OR)",
            ],
            ContestCatalog.All.Select(Format));
    }

    private static string Format(ContestDefinition definition)
    {
        return String.Join(
            '|',
            definition.Id.Value,
            definition.Key,
            definition.DisplayName,
            definition.ExchangeType1,
            definition.ExchangeType2,
            definition.ExchangeCaption1,
            definition.ExchangeCaption2,
            definition.ExchangeFieldEditable,
            definition.ExchangeDefault,
            definition.ValidationMessage);
    }
}
