using System.Reflection;
using System.Text.RegularExpressions;
using MorseRunner.Domain;

namespace MorseRunner.Engine;

internal sealed record ContestDxccRecord(
    string Entity,
    string Continent,
    string ItuZones,
    string CqZones,
    string PrefixPattern);

internal sealed class ContestDxccDatabase
{
    private const string ResourceName = "MorseRunner.Engine.Data.DXCC.LIST";
    private readonly Entry[] _entries;

    public ContestDxccDatabase()
    {
        Stream stream = typeof(ContestDxccDatabase).Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' was not found.");
        using (stream)
        using (var reader = new StreamReader(stream))
        {
            _entries = ReadEntries(reader).ToArray();
        }
    }

    public bool TryFind(string callsign, out ContestDxccRecord? record)
    {
        string prefix = CallsignParser.ExtractPrefix(
            callsign,
            deleteTrailingLetters: false);
        if (prefix.StartsWith("KG4", StringComparison.Ordinal)
            && !prefix.StartsWith("KG44", StringComparison.Ordinal)
            && (prefix.Length == 4 || prefix.Length == 6))
        {
            prefix = "K";
        }

        if ((prefix.Length == 3 && (prefix == "CE9" || prefix == "KC4"))
            || (prefix.Length == 6
                && (prefix.StartsWith("KC4AA", StringComparison.Ordinal)
                    || prefix.StartsWith("KC4US", StringComparison.Ordinal))))
        {
            prefix = "CE9KC4";
        }

        for (int index = _entries.Length - 1; index >= 0; index--)
        {
            Entry entry = _entries[index];
            if (entry.Pattern.IsMatch(prefix))
            {
                record = entry.Record;
                return true;
            }
        }

        record = null;
        return false;
    }

    private static IEnumerable<Entry> ReadEntries(TextReader reader)
    {
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith('#'))
            {
                continue;
            }

            string[] fields = line.Split(';');
            if (fields.Length != 7
                || fields[1].StartsWith("!ignore", StringComparison.Ordinal))
            {
                continue;
            }

            yield return new Entry(
                new Regex(
                    "^(" + fields[1] + ")",
                    RegexOptions.CultureInvariant),
                new ContestDxccRecord(
                    fields[2],
                    fields[3],
                    fields[4],
                    fields[5],
                    fields[1]));
        }
    }

    private sealed record Entry(
        Regex Pattern,
        ContestDxccRecord Record);
}
