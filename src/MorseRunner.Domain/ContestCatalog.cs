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
    bool ExchangeFieldEditable,
    string ExchangeDefault);

public static class ContestCatalog
{
    private static readonly ReadOnlyCollection<ContestDefinition> Definitions =
        Array.AsReadOnly<ContestDefinition>(
        [
            new(new("scWpx"), "CqWpx", "CQ WPX", "etRST", "etSerialNr", true, "5NN #"),
            new(new("scCwt"), "Cwt", "CWOPS CWT", "etOpName", "etGenericField", true, "DAVID 123"),
            new(new("scFieldDay"), "ArrlFd", "ARRL Field Day", "etFdClass", "etArrlSection", true, "3A OR"),
            new(new("scNaQp"), "NAQP", "NCJ NAQP", "etOpName", "etNaQpExch2", true, "ALEX ON"),
            new(new("scHst"), "HST", "HST (High Speed Test)", "etRST", "etSerialNr", false, "5NN #"),
            new(new("scCQWW"), "CQWW", "CQ WW", "etRST", "etCQZone", true, "5NN 3"),
            new(new("scArrlDx"), "ArrlDx", "ARRL DX", "etRST", "etStateProv", true, "5NN ON"),
            new(new("scSst"), "Sst", "K1USN Slow Speed Test", "etOpName", "etGenericField", true, "BRUCE MA"),
            new(new("scAllJa"), "AllJa", "JARL ALL JA", "etRST", "etJaPref", true, "5NN 10H"),
            new(new("scAcag"), "Acag", "JARL ACAG", "etRST", "etJaCity", true, "5NN 1002H"),
            new(new("scIaruHf"), "IaruHf", "IARU HF", "etRST", "etGenericField", true, "5NN 6"),
            new(new("scArrlSS"), "SSCW", "ARRL Sweepstakes", "etSSNrPrecedence", "etSSCheckSection", true, "A 72 OR"),
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
