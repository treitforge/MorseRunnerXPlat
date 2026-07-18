using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using MorseRunner.Domain;

namespace MorseRunner.Engine;

public sealed record ExchangeTypes(ExchangeType1 First, ExchangeType2 Second);

public sealed record ContestValidation(bool IsValid, string Error);

public sealed record ContestRules(
    ContestId Id,
    ExchangeTypes SentExchangeTypes,
    ExchangeTypes ReceivedExchangeTypes,
    bool AllowsFarnsworth,
    bool DefaultExchangeIsValid)
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
        ArgumentNullException.ThrowIfNull(exchange);
        return DefaultExchangeIsValid
            ? new ContestValidation(true, string.Empty)
            : new ContestValidation(
                false,
                $"Invalid exchange: '{exchange}' - expecting "
                + "'RST <serial>' (e.g. 5NN #|123).");
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

    private static readonly ReadOnlyCollection<ContestRules> Rules =
        Array.AsReadOnly<ContestRules>(
        [
            new(new("scWpx"), RstSerial, RstSerial, false, true),
            new(new("scCwt"), RstSerial, RstSerial, false, false),
            new(new("scFieldDay"), RstSerial, RstSerial, false, false),
            new(
                new("scNaQp"),
                new(ExchangeType1.OperatorName, ExchangeType2.NaqpSecondField),
                new(ExchangeType1.OperatorName, ExchangeType2.NaqpSecondField),
                false,
                true),
            new(new("scHst"), RstSerial, RstSerial, false, true),
            new(new("scCQWW"), RstSerial, RstSerial, false, true),
            new(
                new("scArrlDx"),
                new(ExchangeType1.Rst, ExchangeType2.StateProvince),
                new(ExchangeType1.Rst, ExchangeType2.StateProvince),
                false,
                true),
            new(new("scSst"), RstSerial, RstSerial, true, false),
            new(new("scAllJa"), RstSerial, RstSerial, false, false),
            new(new("scAcag"), RstSerial, RstSerial, false, false),
            new(new("scIaruHf"), RstSerial, RstSerial, false, true),
            new(new("scArrlSS"), RstSerial, RstSerial, false, true),
        ]);

    public static IReadOnlyList<ContestRules> All => Rules;

    public static ContestRules Get(ContestId id) =>
        Rules.Single(rule => rule.Id == id);
}
