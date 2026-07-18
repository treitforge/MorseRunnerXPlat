using System.Text.RegularExpressions;

namespace MorseRunner.Domain;

public static partial class CallsignParser
{
    public static string ExtractCallsign(string call)
    {
        ArgumentNullException.ThrowIfNull(call);
        Match match = CallsignPattern().Match(call.ToUpperInvariant());
        if (!match.Success || (match.Index > 0 && call[match.Index - 1] != '/'))
        {
            return string.Empty;
        }

        return match.Value;
    }

    public static string ExtractPrefix(
        string call,
        bool deleteTrailingLetters = true)
    {
        ArgumentNullException.ThrowIfNull(call);
        call = RemoveModifiers(call.ToUpperInvariant());
        if (call.Length < 2)
        {
            return string.Empty;
        }

        string digit = string.Empty;
        int slash = call.IndexOf('/');
        string result;
        if (slash < 0)
        {
            result = call;
        }
        else if (slash == 0)
        {
            result = call[1..];
        }
        else if (slash == call.Length - 1)
        {
            result = call[..slash];
        }
        else
        {
            string first = call[..slash];
            string second = call[(slash + 1)..];
            if (first.Length == 1 && char.IsAsciiDigit(first[0]))
            {
                digit = first;
                result = second;
            }
            else if (second.Length == 1 && char.IsAsciiDigit(second[0]))
            {
                digit = second;
                result = first;
            }
            else
            {
                result = first.Length <= second.Length ? first : second;
            }
        }

        if (result.Contains('/', StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (!deleteTrailingLetters)
        {
            return result;
        }

        int trailingIndex = result.Length - 1;
        while (trailingIndex >= 2 && !char.IsAsciiDigit(result[trailingIndex]))
        {
            trailingIndex--;
        }

        result = result[..(trailingIndex + 1)];
        if (!char.IsAsciiDigit(result[^1]))
        {
            result += "0";
        }

        if (digit.Length != 0)
        {
            result = result[..^1] + digit;
        }

        return result[..Math.Min(result.Length, 5)];
    }

    private static string RemoveModifiers(string call)
    {
        string result = call + "|";
        foreach (string modifier in new[] { "/QRP|", "/MM|", "/M|", "/P|" })
        {
            result = result.Replace(modifier, string.Empty, StringComparison.Ordinal);
        }

        return result
            .Replace("|", string.Empty, StringComparison.Ordinal)
            .Replace("//", "/", StringComparison.Ordinal);
    }

    [GeneratedRegex(
        "(([0-9][A-Z])|([A-Z]{1,2}))[0-9][A-Z0-9]*[A-Z]",
        RegexOptions.CultureInvariant)]
    private static partial Regex CallsignPattern();
}
