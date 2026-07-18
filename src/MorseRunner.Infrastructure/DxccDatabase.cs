using System.Text.RegularExpressions;

namespace MorseRunner.Infrastructure;

public sealed record DxccRecord(
    string Entity,
    string Continent,
    string ItuZones,
    string CqZones);

public sealed class DxccDatabase
{
    private readonly Entry[] _entries;

    public DxccDatabase(PackagedDataCatalog? dataCatalog = null)
    {
        dataCatalog ??= new PackagedDataCatalog();
        using TextReader reader = dataCatalog.OpenTextRequired("DXCC.LIST");
        _entries = ReadEntries(reader).ToArray();
    }

    public bool TryFind(string callsign, out DxccRecord? record)
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
            if (fields.Length != 7 || fields[1].StartsWith("!ignore", StringComparison.Ordinal))
            {
                continue;
            }

            yield return new Entry(
                new Regex(
                    "^(" + fields[1] + ")",
                    RegexOptions.CultureInvariant),
                new DxccRecord(fields[2], fields[3], fields[4], fields[5]));
        }
    }

    private sealed record Entry(Regex Pattern, DxccRecord Record);
}
