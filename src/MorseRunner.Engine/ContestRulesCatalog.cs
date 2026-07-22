using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using MorseRunner.Domain;

namespace MorseRunner.Engine;

public sealed record ExchangeTypes(ExchangeType1 First, ExchangeType2 Second);

public sealed record ContestValidation(bool IsValid, string Error);

public sealed record ContestRules(
    ContestId Id,
    ExchangeTypes BaselineSentExchangeTypes,
    ExchangeTypes BaselineReceivedExchangeTypes,
    bool AllowsFarnsworth)
{
    public static bool LoadCallHistory(string userCallsign)
    {
        return ValidateMyCall(userCallsign).IsValid;
    }

    public static ContestValidation ValidateMyCall(string callsign)
    {
        ArgumentNullException.ThrowIfNull(callsign);
        return CallsignPattern().IsMatch(callsign)
            ? new ContestValidation(true, string.Empty)
            : new ContestValidation(false, $"Invalid callsign: '{callsign}'.");
    }

    public ContestValidation ValidateMyExchange(string exchange)
    {
        return ContestQsoRules.ValidateOwnExchange(Id, exchange);
    }

    private static Regex CallsignPattern() =>
        new(
            "^(([0-9][A-Z])|([A-Z]{1,2}))[0-9][A-Z0-9]*[A-Z]$",
            RegexOptions.CultureInvariant);
}

public static class ContestRulesCatalog
{
    private static readonly ExchangeTypes RstSerial =
        new(ExchangeType1.Rst, ExchangeType2.SerialNumber);

    private static readonly ExchangeTypes OperatorNameGeneric =
        new(ExchangeType1.OperatorName, ExchangeType2.GenericField);

    // Exchange pairs are the pinned W7SST-to-F6ABC baseline. ARRL DX and
    // NAQP require call-dependent resolution before these values can serve
    // general runtime entry layout or validation.
    private static readonly ReadOnlyCollection<ContestRules> Rules =
        Array.AsReadOnly<ContestRules>(
        [
            new(new("scWpx"), RstSerial, RstSerial, false),
            new(
                new("scCwt"),
                OperatorNameGeneric,
                OperatorNameGeneric,
                false),
            new(
                new("scFieldDay"),
                new(ExchangeType1.FieldDayClass, ExchangeType2.ArrlSection),
                new(ExchangeType1.FieldDayClass, ExchangeType2.ArrlSection),
                false),
            new(
                new("scNaQp"),
                new(ExchangeType1.OperatorName, ExchangeType2.NaqpSecondField),
                new(ExchangeType1.OperatorName, ExchangeType2.NaqpSecondField),
                false),
            new(new("scHst"), RstSerial, RstSerial, false),
            new(
                new("scCQWW"),
                new(ExchangeType1.Rst, ExchangeType2.CqZone),
                new(ExchangeType1.Rst, ExchangeType2.CqZone),
                false),
            new(
                new("scArrlDx"),
                new(ExchangeType1.Rst, ExchangeType2.StateProvince),
                new(ExchangeType1.Rst, ExchangeType2.StateProvince),
                false),
            new(new("scSst"), OperatorNameGeneric, OperatorNameGeneric, true),
            new(
                new("scAllJa"),
                new(ExchangeType1.Rst, ExchangeType2.JapanPrefecture),
                new(ExchangeType1.Rst, ExchangeType2.JapanPrefecture),
                false),
            new(
                new("scAcag"),
                new(ExchangeType1.Rst, ExchangeType2.JapanCity),
                new(ExchangeType1.Rst, ExchangeType2.JapanCity),
                false),
            new(
                new("scIaruHf"),
                new(ExchangeType1.Rst, ExchangeType2.GenericField),
                new(ExchangeType1.Rst, ExchangeType2.GenericField),
                false),
            new(
                new("scArrlSS"),
                new(
                    ExchangeType1.SweepstakesNumberPrecedence,
                    ExchangeType2.SweepstakesCheckSection),
                new(
                    ExchangeType1.SweepstakesNumberPrecedence,
                    ExchangeType2.SweepstakesCheckSection),
                false),
        ]);

    public static IReadOnlyList<ContestRules> All => Rules;

    public static ContestRules Get(ContestId id) =>
        Rules.Single(rule => rule.Id == id);
}
