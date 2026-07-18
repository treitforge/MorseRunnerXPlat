using System.Globalization;
using System.Text.RegularExpressions;
using MorseRunner.Domain;
using MorseRunner.Dsp;

namespace MorseRunner.Engine;

internal sealed record ContestQsoEvaluation(
    ContestValidation Validation,
    string Call,
    int Rst,
    int Number,
    string Precedence,
    int Check,
    string Section,
    string Prefix,
    string Multiplier,
    int Points,
    bool UsesAdditiveScore = false);

public static class ContestQsoRules
{
    private static readonly ContestDxccDatabase Dxcc = new();

    public static ContestValidation ValidateOwnExchange(
        ContestId contestId,
        string exchange)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        string value = exchange.Trim().ToUpperInvariant();
        bool valid = contestId.Value switch
        {
            "scWpx" or "scHst" => Regex.IsMatch(
                value,
                @"^[1-5E][1-9N][1-9N] +([0-9OTN]+|#)$",
                RegexOptions.CultureInvariant),
            "scCwt" => Regex.IsMatch(
                value,
                @"^[A-Z][A-Z]* +[0-9A-Z]+$",
                RegexOptions.CultureInvariant),
            "scFieldDay" => Regex.IsMatch(
                value,
                @"^[1-9][0-9]*[A-F] +[A-Z]{2,3}$",
                RegexOptions.CultureInvariant),
            "scNaQp" or "scSst" => Regex.IsMatch(
                value,
                @"^[A-Z][A-Z]* +[0-9A-Z/]+$",
                RegexOptions.CultureInvariant),
            "scCQWW" => Regex.IsMatch(
                value,
                @"^[1-5E][1-9N][1-9N] +[0-9OANT]+$",
                RegexOptions.CultureInvariant),
            "scArrlDx" => Regex.IsMatch(
                value,
                @"^[1-5E][1-9N][1-9N] +[0-9A-Z]+$",
                RegexOptions.CultureInvariant),
            "scAllJa" or "scAcag" => Regex.IsMatch(
                value,
                @"^[1-5E][1-9N][1-9N] +[0-9AOTN]+[LMHP]$",
                RegexOptions.CultureInvariant),
            "scIaruHf" => Regex.IsMatch(
                value,
                @"^[1-5E][1-9N][1-9N] +[0-9A-Z]+$",
                RegexOptions.CultureInvariant),
            "scArrlSS" => SweepstakesExchangeParser.ParseOwn(value).IsValid,
            _ => false,
        };
        return valid
            ? new(true, string.Empty)
            : new(false, InvalidOwnExchangeMessage(contestId, exchange));
    }

    internal static ContestQsoEvaluation EvaluateReceived(
        ContestId contestId,
        string stationCall,
        LogQsoCommand command)
    {
        string call = CqWpxContestRules.NormalizeCall(command.Call);
        if (call.Length < 3 && contestId.Value != "scHst")
        {
            return Invalid(call, "Invalid callsign");
        }

        return contestId.Value switch
        {
            "scWpx" => EvaluateWpx(command, call),
            "scCwt" => EvaluateCwt(command, call),
            "scFieldDay" => EvaluateFieldDay(command, call),
            "scNaQp" => EvaluateNaqp(command, call),
            "scHst" => EvaluateHst(command, call),
            "scCQWW" => EvaluateCqWw(stationCall, command, call),
            "scArrlDx" => EvaluateArrlDx(stationCall, command, call),
            "scSst" => EvaluateSst(command, call),
            "scAllJa" => EvaluateJapanese(command, call, "Prefecture"),
            "scAcag" => EvaluateJapanese(command, call, "City/Gun/Ku"),
            "scIaruHf" => EvaluateIaru(stationCall, command, call),
            "scArrlSS" => EvaluateSweepstakes(command, call),
            _ => Invalid(call, $"Unsupported contest '{contestId.Value}'."),
        };
    }

    private static ContestQsoEvaluation EvaluateWpx(
        LogQsoCommand command,
        string call)
    {
        ContestValidation validation = CqWpxContestRules.ValidateReceivedQso(
            command.Call,
            command.Rst,
            command.Exchange1);
        string prefix = CallsignParser.ExtractPrefix(call);
        return Create(
            validation,
            call,
            command,
            ParseCutNumber(command.Exchange1),
            prefix,
            prefix,
            CqWpxContestRules.PointsPerQso);
    }

    private static ContestQsoEvaluation EvaluateCwt(
        LogQsoCommand command,
        string call)
    {
        ContestValidation validation = CwtContestRules.ValidateReceivedQso(
            command.Call,
            command.Exchange1,
            command.Exchange2);
        return Create(
            validation,
            call,
            command,
            ParseCutNumber(command.Exchange2),
            CallsignParser.ExtractPrefix(call),
            call,
            CwtContestRules.PointsPerQso);
    }

    private static ContestQsoEvaluation EvaluateFieldDay(
        LogQsoCommand command,
        string call)
    {
        ContestValidation validation = ValidateFields(
            true,
            command.Exchange1.Length > 1
                && command.Exchange2.Length > 1,
            "Missing/Invalid Field Day exchange");
        return Create(
            validation,
            call,
            command,
            0,
            CallsignParser.ExtractPrefix(call),
            "1",
            2,
            section: command.Exchange2);
    }

    private static ContestQsoEvaluation EvaluateNaqp(
        LogQsoCommand command,
        string call)
    {
        ContestValidation validation = ValidateFields(
            command.Exchange1.Length > 1,
            IsNaqpSecondFieldValid(call, command.Exchange2),
            "Missing/Invalid NAQP exchange");
        string multiplier = Dxcc.TryFind(call, out ContestDxccRecord? record)
            && record is not null
            && (record.Continent == "NA" || record.Entity == "Hawaii")
                ? command.Exchange2.ToUpperInvariant()
                : string.Empty;
        return Create(
            validation,
            call,
            command,
            0,
            CallsignParser.ExtractPrefix(call),
            multiplier,
            1,
            section: command.Exchange2);
    }

    private static ContestQsoEvaluation EvaluateHst(
        LogQsoCommand command,
        string call)
    {
        ContestValidation validation = !IsRst(command.Rst)
            ? new(false, "Missing/Invalid RST")
            : command.Exchange1.Length == 0
                ? new(false, "Missing/Invalid Nr.")
                : new(true, string.Empty);
        string encoded = MorseKeyer.Encode(call);
        int points = -1;
        foreach (char symbol in encoded)
        {
            points += symbol switch
            {
                '.' => 2,
                '-' => 4,
                ' ' => 2,
                _ => 0,
            };
        }

        return Create(
            validation,
            call,
            command,
            ParseCutNumber(command.Exchange1),
            CallsignParser.ExtractPrefix(call),
            points.ToString(CultureInfo.InvariantCulture),
            points,
            usesAdditiveScore: true);
    }

    private static ContestQsoEvaluation EvaluateCqWw(
        string stationCall,
        LogQsoCommand command,
        string call)
    {
        ContestValidation validation = ValidateFields(
            IsRst(command.Rst),
            command.Exchange2.Length > 0,
            "Missing/Invalid CQ zone");
        int zone = ParseCutNumber(command.Exchange2);
        string multiplier = $"ZN-{zone}";
        int points = 0;
        if (!call.EndsWith("/MM", StringComparison.Ordinal)
            && Dxcc.TryFind(call, out ContestDxccRecord? worked)
            && worked is not null)
        {
            string entity = worked.Entity is "Alaska" or "Hawaii"
                ? "United States of America"
                : worked.Entity;
            multiplier += ";" + entity;
            if (Dxcc.TryFind(stationCall, out ContestDxccRecord? home)
                && home is not null)
            {
                points = worked.Continent != home.Continent
                    ? 3
                    : worked.Entity == home.Entity
                        ? 0
                        : worked.Continent == "NA"
                            ? 2
                            : 1;
            }
        }

        return Create(
            validation,
            call,
            command,
            zone,
            CallsignParser.ExtractPrefix(call),
            multiplier,
            points);
    }

    private static ContestQsoEvaluation EvaluateArrlDx(
        string stationCall,
        LogQsoCommand command,
        string call)
    {
        ContestValidation validation = ValidateFields(
            IsRst(command.Rst),
            IsArrlDxSecondFieldValid(stationCall, command.Exchange2),
            "Missing/Invalid State/Province/Power");
        string multiplier = command.Exchange2.ToUpperInvariant();
        if (Dxcc.TryFind(stationCall, out ContestDxccRecord? home)
            && home is not null
            && home.Entity is "United States of America" or "Canada"
            && Dxcc.TryFind(call, out ContestDxccRecord? worked)
            && worked is not null)
        {
            multiplier = worked.Entity;
        }

        return Create(
            validation,
            call,
            command,
            ParseCutNumber(command.Exchange2),
            CallsignParser.ExtractPrefix(call),
            multiplier,
            3,
            section: command.Exchange2);
    }

    private static ContestQsoEvaluation EvaluateSst(
        LogQsoCommand command,
        string call)
    {
        ContestValidation validation = ValidateFields(
            command.Exchange1.Length > 1,
            command.Exchange2.Length > 0,
            "Missing/Invalid SST exchange");
        string multiplier = command.Exchange2.ToUpperInvariant();
        if (multiplier == "DX"
            && Dxcc.TryFind(call, out ContestDxccRecord? worked)
            && worked is not null)
        {
            multiplier = worked.Entity;
        }

        return Create(
            validation,
            call,
            command,
            0,
            CallsignParser.ExtractPrefix(call),
            multiplier,
            1,
            section: command.Exchange2);
    }

    private static ContestQsoEvaluation EvaluateJapanese(
        LogQsoCommand command,
        string call,
        string fieldName)
    {
        string exchange = command.Exchange2.ToUpperInvariant();
        int minimumLength = fieldName == "Prefecture" ? 3 : 4;
        bool validExchange = exchange.Length >= minimumLength;
        ContestValidation validation = ValidateFields(
            IsRst(command.Rst),
            validExchange,
            $"Missing/Invalid {fieldName}");
        string multiplier = validExchange
            ? exchange[..^1]
            : string.Empty;
        return Create(
            validation,
            call,
            command,
            ParseCutNumber(multiplier),
            CallsignParser.ExtractPrefix(call),
            multiplier,
            1,
            section: multiplier);
    }

    private static ContestQsoEvaluation EvaluateIaru(
        string stationCall,
        LogQsoCommand command,
        string call)
    {
        string exchange = command.Exchange2.ToUpperInvariant();
        ContestValidation validation = ValidateFields(
            IsRst(command.Rst),
            exchange.Length > 0 && exchange.All(IsGenericExchangeCharacter),
            "Missing/Invalid ITU zone or IARU society");
        int points = 1;
        bool numeric = TryParsePositiveCutNumber(exchange, out int zone);
        if (numeric
            && zone != HomeItuZone(stationCall)
            && Dxcc.TryFind(call, out ContestDxccRecord? worked)
            && worked is not null
            && Dxcc.TryFind(stationCall, out ContestDxccRecord? home)
            && home is not null)
        {
            points = worked.Continent == home.Continent ? 3 : 5;
        }

        return Create(
            validation,
            call,
            command,
            numeric ? zone : 0,
            CallsignParser.ExtractPrefix(call),
            exchange,
            points,
            section: exchange);
    }

    private static ContestQsoEvaluation EvaluateSweepstakes(
        LogQsoCommand command,
        string call)
    {
        SweepstakesParseResult parsed = SweepstakesExchangeParser.ParseEntered(
            call,
            $"{command.Exchange1} {command.Exchange2}");
        ContestValidation validation = parsed.IsValid
            ? new(true, string.Empty)
            : new(false, parsed.Error);
        SweepstakesExchange exchange = parsed.Exchange;
        return new(
            validation,
            call,
            0,
            exchange.SerialNumber,
            exchange.Precedence,
            ParseCutNumber(exchange.Check),
            exchange.Section,
            CallsignParser.ExtractPrefix(call),
            exchange.Section,
            2);
    }

    private static ContestQsoEvaluation Create(
        ContestValidation validation,
        string call,
        LogQsoCommand command,
        int number,
        string prefix,
        string multiplier,
        int points,
        string precedence = "",
        int check = 0,
        string section = "",
        bool usesAdditiveScore = false) =>
        new(
            validation,
            call,
            CqWpxContestRules.ParseRst(command.Rst),
            number,
            precedence,
            check,
            section,
            prefix,
            multiplier,
            points,
            usesAdditiveScore);

    private static ContestQsoEvaluation Invalid(
        string call,
        string error) =>
        new(
            new(false, error),
            call,
            0,
            0,
            string.Empty,
            0,
            string.Empty,
            string.Empty,
            string.Empty,
            0);

    private static ContestValidation ValidateFields(
        bool firstValid,
        bool secondValid,
        string secondError)
    {
        if (!firstValid)
        {
            return new(false, "Missing/Invalid first exchange field");
        }

        return secondValid
            ? new(true, string.Empty)
            : new(false, secondError);
    }

    private static bool IsRst(string value) => value.Length == 3;

    private static bool IsGenericExchangeCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value == '/';

    private static bool IsCutNumberCharacter(char value) =>
        char.IsAsciiDigit(value) || "AOTN".Contains(value, StringComparison.Ordinal);

    private static int ParseCutNumber(string value)
    {
        string numeric = value
            .Replace("A", "1", StringComparison.OrdinalIgnoreCase)
            .Replace("E", "5", StringComparison.OrdinalIgnoreCase)
            .Replace("N", "9", StringComparison.OrdinalIgnoreCase)
            .Replace("O", "0", StringComparison.OrdinalIgnoreCase)
            .Replace("T", "0", StringComparison.OrdinalIgnoreCase);
        return int.TryParse(
            numeric,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int parsed)
                ? parsed
                : 0;
    }

    private static bool TryParsePositiveCutNumber(
        string value,
        out int number)
    {
        number = ParseCutNumber(value);
        return value.Length > 0
            && value.All(IsCutNumberCharacter)
            && number > 0;
    }

    private static bool IsNaqpSecondFieldValid(
        string call,
        string exchange)
    {
        bool isLocal = Dxcc.TryFind(call, out ContestDxccRecord? record)
            && record is not null
            && (record.Continent == "NA" || record.Entity == "Hawaii");
        return !isLocal || exchange.Length > 0;
    }

    private static bool IsArrlDxSecondFieldValid(
        string stationCall,
        string exchange)
    {
        bool homeIsLocal = Dxcc.TryFind(
                stationCall,
                out ContestDxccRecord? home)
            && home is not null
            && home.Entity is "United States of America" or "Canada";
        return homeIsLocal
            ? exchange.Length > 0
            : exchange.Length > 1;
    }

    private static int HomeItuZone(string stationCall)
    {
        if (!Dxcc.TryFind(stationCall, out ContestDxccRecord? record)
            || record is null)
        {
            return 0;
        }

        string first = record.ItuZones.Split(',')[0];
        return int.TryParse(
            first,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int zone)
                ? zone
                : 0;
    }

    private static string InvalidOwnExchangeMessage(
        ContestId contestId,
        string exchange)
    {
        string expectation = contestId.Value switch
        {
            "scWpx" or "scHst" =>
                "'RST <serial>' (e.g. 5NN #).",
            "scCwt" =>
                "'<name> <member nr|qth>' (e.g. DAVID 123).",
            "scFieldDay" =>
                "'<class> <section>' (e.g. 3A OR).",
            "scNaQp" =>
                "'<name> [<state|prov|dxcc-entity>]' (e.g. ALEX ON).",
            "scCQWW" =>
                "'RST <cq-zone>' (e.g. 5NN 3).",
            "scArrlDx" =>
                "'RST <state|province|power>' (e.g. 5NN ON).",
            "scSst" =>
                "'<op name> <State|Prov|DX>' (e.g. BRUCE MA).",
            "scAllJa" =>
                "'RST <Pref><Power>' (e.g. 5NN 10H).",
            "scAcag" =>
                "'RST <City|Gun|Ku><Power>' (e.g. 5NN 1002H).",
            "scIaruHf" =>
                "'RST <Itu-zone|IARU Society>' (e.g. 5NN 6).",
            "scArrlSS" =>
                "'[#|123] <precedence> <check> <section>' (e.g. A 72 OR).",
            _ => "'supported contest exchange'.",
        };
        return $"Invalid exchange: '{exchange}' - expecting {expectation}";
    }
}
