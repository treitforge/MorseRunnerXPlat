using System.Collections.ObjectModel;

namespace MorseRunner.Domain;

public readonly record struct ContestId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct RunModeId(string Value)
{
    public override string ToString() => Value;
}

public sealed record ContestDefinition(
    ContestId Id,
    string Key,
    string DisplayName,
    string ExchangeType1,
    string ExchangeType2,
    string ExchangeCaption1,
    string ExchangeCaption2,
    bool ExchangeFieldEditable,
    string ExchangeDefault,
    string ValidationMessage);

public static class ContestCatalog
{
    private static readonly ReadOnlyCollection<ContestDefinition> Definitions =
        Array.AsReadOnly<ContestDefinition>(
        [
            new(
                new("scWpx"),
                "CqWpx",
                "CQ WPX",
                "etRST",
                "etSerialNr",
                string.Empty,
                string.Empty,
                true,
                "5NN #",
                "'RST <serial>' (e.g. 5NN #|123)"),
            new(
                new("scCwt"),
                "Cwt",
                "CWOPS CWT",
                "etOpName",
                "etGenericField",
                "Name",
                "Exch",
                true,
                "DAVID 123",
                "'<op name> <CWOPS Number|State|Country>' (e.g. DAVID 123)"),
            new(
                new("scFieldDay"),
                "ArrlFd",
                "ARRL Field Day",
                "etFdClass",
                "etArrlSection",
                string.Empty,
                string.Empty,
                true,
                "3A OR",
                "'<class> <section>' (e.g. 3A OR)"),
            new(
                new("scNaQp"),
                "NAQP",
                "NCJ NAQP",
                "etOpName",
                "etNaQpExch2",
                string.Empty,
                string.Empty,
                true,
                "ALEX ON",
                "'<name> [<state|prov|dxcc-entity>]' (e.g. ALEX ON)"),
            new(
                new("scHst"),
                "HST",
                "HST (High Speed Test)",
                "etRST",
                "etSerialNr",
                string.Empty,
                string.Empty,
                false,
                "5NN #",
                "'RST <serial>' (e.g. 5NN #)"),
            new(
                new("scCQWW"),
                "CQWW",
                "CQ WW",
                "etRST",
                "etCqZone",
                string.Empty,
                string.Empty,
                true,
                "5NN 3",
                "'RST <cq-zone>' (e.g. 5NN 3)"),
            new(
                new("scArrlDx"),
                "ArrlDx",
                "ARRL DX",
                "etRST",
                "etStateProv",
                string.Empty,
                string.Empty,
                true,
                "5NN ON",
                "'RST <state|province|power>' (e.g. 5NN ON)"),
            new(
                new("scSst"),
                "Sst",
                "K1USN Slow Speed Test",
                "etOpName",
                "etGenericField",
                "Name",
                "State/Prov/DX",
                true,
                "BRUCE MA",
                "'<op name> <State|Prov|DX>' (e.g. BRUCE MA)"),
            new(
                new("scAllJa"),
                "AllJa",
                "JARL ALL JA",
                "etRST",
                "etJaPref",
                string.Empty,
                string.Empty,
                true,
                "5NN 10H",
                "'RST <Pref><Power>' (e.g. 5NN 10H)"),
            new(
                new("scAcag"),
                "Acag",
                "JARL ACAG",
                "etRST",
                "etJaCity",
                string.Empty,
                string.Empty,
                true,
                "5NN 1002H",
                "'RST <City|Gun|Ku><Power>' (e.g. 5NN 1002H)"),
            new(
                new("scIaruHf"),
                "IaruHf",
                "IARU HF",
                "etRST",
                "etGenericField",
                "RST",
                "Zone/Soc",
                true,
                "5NN 6",
                "'RST <Itu-zone|IARU Society>' (e.g. 5NN 6)"),
            new(
                new("scArrlSS"),
                "SSCW",
                "ARRL Sweepstakes",
                "etSSNrPrecedence",
                "etSSCheckSection",
                string.Empty,
                string.Empty,
                true,
                "A 72 OR",
                "'[#|123] <precedence> <check> <section>' (e.g. A 72 OR)"),
        ]);

    public static IReadOnlyList<ContestDefinition> All => Definitions;

    public static ContestDefinition Get(ContestId id)
    {
        return Definitions.Single(
            definition => definition.Id == id);
    }
}

public static class RunModeCatalog
{
    private static readonly ReadOnlyCollection<RunModeId> Modes =
        Array.AsReadOnly<RunModeId>(
        [
            new("rmStop"),
            new("rmPileup"),
            new("rmSingle"),
            new("rmWpx"),
            new("rmHst"),
        ]);

    public static IReadOnlyList<RunModeId> All => Modes;
}
