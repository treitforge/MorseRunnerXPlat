using System.Collections.ObjectModel;
using System.Text;

namespace MorseRunner.Infrastructure;

public sealed class LegacyIniDocument
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Sections =>
        new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(
            _sections.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyDictionary<string, string>)
                    new ReadOnlyDictionary<string, string>(pair.Value),
                StringComparer.OrdinalIgnoreCase));

    public static LegacyIniDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var document = new LegacyIniDocument();
        string? section = null;
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0
                || trimmed.StartsWith(';')
                || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                section = trimmed[1..^1].Trim();
                document.GetOrCreateSection(section);
                continue;
            }

            int separator = trimmed.IndexOf('=');
            if (section is null || separator < 1)
            {
                throw new FormatException(
                    $"Invalid legacy INI line: '{line}'.");
            }

            document.GetOrCreateSection(section)[trimmed[..separator].Trim()] =
                trimmed[(separator + 1)..].Trim();
        }

        return document;
    }

    public bool TryGet(string section, string key, out string? value)
    {
        value = null;
        return _sections.TryGetValue(section, out Dictionary<string, string>? values)
            && values.TryGetValue(key, out value);
    }

    public string Serialize()
    {
        var builder = new StringBuilder();
        foreach ((string section, Dictionary<string, string> values) in _sections)
        {
            builder.Append('[').Append(section).AppendLine("]");
            foreach ((string key, string value) in values)
            {
                builder.Append(key).Append('=').AppendLine(value);
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private Dictionary<string, string> GetOrCreateSection(string section)
    {
        if (!_sections.TryGetValue(section, out Dictionary<string, string>? values))
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _sections.Add(section, values);
        }

        return values;
    }
}
