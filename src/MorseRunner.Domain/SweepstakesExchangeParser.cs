using System.Text.RegularExpressions;
namespace MorseRunner.Domain;

public sealed record SweepstakesExchange(
    int SerialNumber,
    string Precedence,
    string Callsign,
    string Check,
    string Section)
{
    public string Summary =>
        $"{SerialNumber}{Precedence} {Callsign} {Check} {Section}";
}

public sealed record SweepstakesParseResult(
    SweepstakesExchange Exchange,
    string Error)
{
    public bool IsValid => Error.Length == 0;
}

public static partial class SweepstakesExchangeParser
{
    private static readonly HashSet<string> Sections =
    [
        "AB", "AK", "AL", "AR", "AZ", "BC", "CO", "CT", "DE", "EB", "EMA",
        "ENY", "EPA", "EWA", "GA", "GH", "GTA", "ID", "IL", "IN", "IA", "KS",
        "KY", "LA", "LAX", "MAR", "MB", "MDC", "ME", "MI", "MN", "MO", "MS",
        "MT", "NC", "ND", "NE", "NFL", "NH", "NL", "NM", "NNJ", "NNY", "NT",
        "NTX", "NV", "OH", "OK", "ONE", "ONN", "ONS", "OR", "ORG", "PAC",
        "PR", "QC", "RI", "SB", "SC", "SCV", "SD", "SDG", "SF", "SFL", "SJV",
        "SK", "SNJ", "STX", "SV", "TN", "UT", "VA", "VI", "VT", "WCF", "WI",
        "WMA", "WNY", "WPA", "WTX", "WV", "WWA", "WY",
    ];

    public static SweepstakesParseResult ParseOwn(string exchange)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        Match match = OwnExchangePattern().Match(exchange.ToUpperInvariant());
        if (!match.Success)
        {
            return Invalid("invalid exchange '" + exchange + "'");
        }

        string serialText = match.Groups["nr"].Value;
        int serialNumber = int.TryParse(serialText, out int parsed) ? parsed : 0;
        var result = new SweepstakesExchange(
            serialNumber,
            match.Groups["prec"].Value,
            string.Empty,
            match.Groups["chk"].Value,
            match.Groups["sect"].Value);
        return new SweepstakesParseResult(result, string.Empty);
    }

    public static SweepstakesParseResult ParseEntered(
        string previousCall,
        string exchange)
    {
        ArgumentNullException.ThrowIfNull(previousCall);
        ArgumentNullException.ThrowIfNull(exchange);
        string[] tokens = TokenPattern()
            .Matches(exchange.ToUpperInvariant())
            .Select(match => match.Value)
            .ToArray();

        string callsign = tokens.FirstOrDefault(
            token => CallsignParser.ExtractCallsign(token) == token)
            ?? previousCall;
        string precedence = tokens.FirstOrDefault(
            token => token.Length == 1 && "QABUMS".Contains(token, StringComparison.Ordinal))
            ?? string.Empty;
        string section = tokens.LastOrDefault(Sections.Contains) ?? string.Empty;

        int precedenceIndex = Array.IndexOf(tokens, precedence);
        int sectionIndex = Array.LastIndexOf(tokens, section);
        string serialText = precedenceIndex > 0 && IsDigits(tokens[precedenceIndex - 1])
            ? tokens[precedenceIndex - 1]
            : string.Empty;
        string check = sectionIndex > 0 && IsDigits(tokens[sectionIndex - 1])
            ? tokens[sectionIndex - 1]
            : string.Empty;
        if (sectionIndex >= 0)
        {
            string[] trailingNumbers = tokens[(sectionIndex + 1)..]
                .Where(IsDigits)
                .ToArray();
            if (trailingNumbers.Length >= 2)
            {
                serialText = trailingNumbers[^2];
                check = trailingNumbers[^1];
            }
        }

        int serialNumber = int.TryParse(serialText, out int parsed) ? Math.Min(parsed, 10_000) : 0;
        check = int.TryParse(check, out int checkValue)
            ? checkValue.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;

        var result = new SweepstakesExchange(
            serialNumber,
            precedence,
            callsign,
            check,
            section);
        string error = serialText.Length == 0
            ? "Missing/Invalid Serial Number"
            : precedence.Length == 0
                ? "Missing/Invalid Precedence"
                : check.Length == 0
                    ? "Missing/Invalid Check"
                    : section.Length == 0
                        ? "Missing/Invalid Section"
                        : string.Empty;
        return new SweepstakesParseResult(result, error);
    }

    private static SweepstakesParseResult Invalid(string error)
    {
        return new SweepstakesParseResult(
            new SweepstakesExchange(0, string.Empty, string.Empty, string.Empty, string.Empty),
            error);
    }

    private static bool IsDigits(string value) =>
        value.Length != 0 && value.All(char.IsAsciiDigit);

    [GeneratedRegex(
        "^ *(?<exch1>(?<nr>[0-9]+|#)? *(?<prec>[QABUMS])) +(?<chk>[0-9]{2}) *(?<sect>[A-Z]+) *$",
        RegexOptions.CultureInvariant)]
    private static partial Regex OwnExchangePattern();

    [GeneratedRegex("[A-Z0-9/]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
