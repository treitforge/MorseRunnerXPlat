using System.Globalization;
using System.Reflection;
using System.Text;
using MorseRunner.Domain;
using MorseRunner.Dsp;

namespace MorseRunner.Engine;

internal sealed class StationReferenceCatalog
{
    private const string EmptyMasterDataFallbackCallsign = "P29SX";
    private static readonly ContestDxccDatabase Dxcc = new();

    private static readonly Dictionary<string, string> ContestFiles =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["scWpx"] = "MASTER.DTA",
            ["scHst"] = "MASTER.DTA",
            ["scCwt"] = "CWOPS.LIST",
            ["scFieldDay"] = "FDGOTA.txt",
            ["scNaQp"] = "NAQPCW.txt",
            ["scCQWW"] = "CQWWCW.txt",
            ["scArrlDx"] = "ARRLDXCW_USDX.txt",
            ["scSst"] = "K1USNSST.txt",
            ["scAllJa"] = "JARL_ALLJA.TXT",
            ["scAcag"] = "JARL_ACAG.TXT",
            ["scIaruHf"] = "IARU_HF.txt",
            ["scArrlSS"] = "SSCW.txt",
        };

    private readonly ContestId _contestId;
    private readonly bool? _homeCallIsLocal;
    private readonly List<ReferenceRow> _rows;

    private StationReferenceCatalog(
        ContestId contestId,
        IEnumerable<ReferenceRow> rows,
        bool? homeCallIsLocal)
    {
        _contestId = contestId;
        _homeCallIsLocal = homeCallIsLocal;
        _rows = [.. rows];
    }

    public int QrmCallsignCount => _rows.Count;

    public int QrmEnvelopeBoundCallsignCount =>
        _rows.Count + (UsesMasterCallList ? 1 : 0);

    private bool UsesMasterCallList =>
        _contestId.Value is "scWpx" or "scHst";

    public static StationReferenceCatalog Load(ContestId contestId) =>
        LoadCore(contestId, stationCall: null);

    public static StationReferenceCatalog Load(
        ContestId contestId,
        string stationCall)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stationCall);
        return LoadCore(contestId, stationCall);
    }

    private static StationReferenceCatalog LoadCore(
        ContestId contestId,
        string? stationCall)
    {
        string canonicalName = ContestFiles[contestId.Value];
        using Stream stream = OpenResource(canonicalName);
        ReferenceRow[] rows = canonicalName.Equals(
            "MASTER.DTA",
            StringComparison.OrdinalIgnoreCase)
                ? ReadMasterData(stream)
                : ReadCallHistory(stream);
        if (contestId.Value == "scNaQp")
        {
            rows = rows
                .Where(IsValidNaqpHistoryRow)
                .ToArray();
        }

        bool? homeCallIsLocal = stationCall is null
            ? null
            : contestId.Value switch
            {
                "scArrlDx" => IsArrlDxHomeCallLocal(stationCall),
                "scNaQp" => IsNaqpCallLocal(
                    stationCall,
                    useFallback: true),
                _ => null,
            };
        if (contestId.Value == "scArrlDx"
            && homeCallIsLocal.HasValue)
        {
            rows = rows
                .Where(
                    row => homeCallIsLocal.Value
                        ? row.HasValue("Power")
                        : row.HasValue("State"))
                .ToArray();
        }

        if (homeCallIsLocal.HasValue
            && contestId.Value is "scArrlDx" or "scNaQp")
        {
            foreach (ReferenceRow row in rows)
            {
                CacheQrmDxccMetadata(contestId, row);
            }
        }

        if (rows.Length == 0)
        {
            throw new InvalidDataException(
                $"Packaged station reference '{canonicalName}' is empty.");
        }

        return new(contestId, rows, homeCallIsLocal);
    }

    public string GetQrmCallsignAt(int index) => _rows[index].Call;

    public string GetQrmEnvelopeBoundCallsignAt(int index)
    {
        if ((uint)index < (uint)_rows.Count)
        {
            return GetQrmCallsignAt(index);
        }

        return UsesMasterCallList && index == _rows.Count
            ? EmptyMasterDataFallbackCallsign
            : throw new ArgumentOutOfRangeException(nameof(index));
    }

    public string PickCallsignForQrm(
        LegacyRandom random,
        RunModeId runModeId)
    {
        ArgumentNullException.ThrowIfNull(random);
        RequireConfiguredHomeCall();
        if (_rows.Count == 0)
        {
            return UsesMasterCallList
                ? EmptyMasterDataFallbackCallsign
                : throw new InvalidOperationException(
                    $"Contest '{_contestId.Value}' has no QRM callsigns.");
        }

        int selectedIndex = random.Next(_rows.Count);
        while (_rows.Count > 1
               && !PrepareEligibleQrmCall(_rows[selectedIndex]))
        {
            _rows.RemoveAt(selectedIndex);
            selectedIndex = random.Next(_rows.Count);
        }

        string selected = _rows[selectedIndex].Call;
        if (runModeId.Value == "rmHst"
            && _contestId.Value is "scWpx" or "scHst")
        {
            _rows.RemoveAt(selectedIndex);
        }

        return selected;
    }

    public StationIdentity Pick(
        LegacyRandom random,
        ContestId contestId,
        int serialNumber)
    {
        ReferenceRow row = _rows[random.Next(_rows.Count)];
        string rst = "599";
        string exchange1;
        string exchange2;
        switch (contestId.Value)
        {
            case "scWpx":
            case "scHst":
                exchange1 = rst;
                exchange2 = serialNumber.ToString(CultureInfo.InvariantCulture);
                break;
            case "scCQWW":
                exchange1 = rst;
                exchange2 = row.Get("CQZone", "Exch1", fallback: "5");
                break;
            case "scArrlDx":
                exchange1 = rst;
                exchange2 = row.Get("Power", "State", fallback: "100");
                break;
            case "scCwt":
            case "scSst":
                exchange1 = row.Get("Name", fallback: "ALEX");
                exchange2 = row.Get(
                    "Exch1",
                    fallback: serialNumber.ToString(CultureInfo.InvariantCulture));
                break;
            case "scNaQp":
                exchange1 = row.Get("Name", fallback: "ALEX");
                exchange2 = row.Get("State", fallback: "DX");
                break;
            case "scArrlSS":
                exchange1 = row.Get("Sect", fallback: "OR");
                exchange2 = row.Get("CK", fallback: "72");
                break;
            default:
                exchange1 = row.Get("Exch1", "Name", fallback: rst);
                exchange2 = row.Get(
                    "Exch2",
                    "Sect",
                    "State",
                    fallback: serialNumber.ToString(CultureInfo.InvariantCulture));
                break;
        }

        return new(
            row.Call,
            rst,
            serialNumber,
            exchange1,
            exchange2,
            row.Get("Prec", fallback: string.Empty),
            ParseInt(row.Get("CK", fallback: string.Empty)),
            row.Get("Sect", fallback: string.Empty));
    }

    private void RequireConfiguredHomeCall()
    {
        if (_contestId.Value is "scArrlDx" or "scNaQp"
            && !_homeCallIsLocal.HasValue)
        {
            throw new InvalidOperationException(
                $"Contest '{_contestId.Value}' requires the station call "
                + "when loading a QRM callsign catalog.");
        }
    }

    private bool PrepareEligibleQrmCall(ReferenceRow row)
    {
        if (_contestId.Value == "scArrlDx")
        {
            return row.QrmDxccEligible;
        }

        if (_contestId.Value != "scNaQp")
        {
            return true;
        }

        if (!row.QrmDxccEligible)
        {
            return false;
        }

        row.ApplyQrmNaqpStateRepair();

        return _homeCallIsLocal == true || row.QrmNaqpLocal;
    }

    private static void CacheQrmDxccMetadata(
        ContestId contestId,
        ReferenceRow row)
    {
        bool found = Dxcc.TryFind(
            row.Call,
            out ContestDxccRecord? record);
        if (contestId.Value == "scArrlDx")
        {
            row.SetQrmDxccMetadata(
                eligible: found,
                naqpLocal: false,
                naqpStateRepair: null);
            return;
        }

        bool callIsLocal = found
            && record is not null
            && (record.Continent == "NA"
                || record.Entity == "Hawaii");
        string? stateRepair = null;
        bool eligible = found;
        if (eligible && callIsLocal && !row.HasValue("State"))
        {
            bool simplePrefix = !record!.PrefixPattern.Contains(
                "()|,[]*+-",
                StringComparison.Ordinal);
            eligible = simplePrefix;
            if (simplePrefix)
            {
                stateRepair = record.PrefixPattern;
            }
        }

        row.SetQrmDxccMetadata(
            eligible,
            callIsLocal,
            stateRepair);
    }

    private static bool IsValidNaqpHistoryRow(ReferenceRow row)
    {
        string name = row.Get("Name");
        return name.Length is > 0 and <= 12;
    }

    internal static bool IsArrlDxHomeCallLocal(string call)
    {
        if (Dxcc.TryFind(call, out ContestDxccRecord? record)
            && record is not null)
        {
            return record.Entity is
                "United States of America" or "Canada";
        }

        string normalized = call.ToUpperInvariant();
        return normalized.StartsWith('A')
            || normalized.StartsWith('K')
            || normalized.StartsWith('N')
            || normalized.StartsWith('W')
            || normalized.StartsWith(
                "VE",
                StringComparison.Ordinal);
    }

    internal static bool IsNaqpCallLocal(
        string call,
        bool useFallback)
    {
        if (Dxcc.TryFind(call, out ContestDxccRecord? record)
            && record is not null)
        {
            return record.Continent == "NA"
                || record.Entity == "Hawaii";
        }

        if (!useFallback)
        {
            return false;
        }

        string normalized = call.ToUpperInvariant();
        return normalized.StartsWith('A')
            || normalized.StartsWith('K')
            || normalized.StartsWith('N')
            || normalized.StartsWith('W')
            || normalized.StartsWith(
                "VE",
                StringComparison.Ordinal)
            || normalized.StartsWith(
                "XE",
                StringComparison.Ordinal);
    }

    private static Stream OpenResource(string canonicalName)
    {
        Assembly assembly = typeof(StationReferenceCatalog).Assembly;
        string resourceName = assembly
            .GetManifestResourceNames()
            .Single(
                name => name.EndsWith(
                    "." + canonicalName,
                    StringComparison.OrdinalIgnoreCase));
        return assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException(
                $"Packaged station reference '{canonicalName}' was not found.");
    }

    private static ReferenceRow[] ReadMasterData(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        byte[] data = memory.ToArray();
        const int characterCount = 37;
        const int indexBytes =
            ((characterCount * characterCount) + 1) * sizeof(int);
        if (data.Length < indexBytes
            || BitConverter.ToInt32(data, 0) != indexBytes
            || BitConverter.ToInt32(data, indexBytes - sizeof(int))
                != data.Length)
        {
            throw new InvalidDataException(
                "The packaged MASTER.DTA index is invalid.");
        }

        var calls = new SortedSet<string>(StringComparer.Ordinal);
        int start = indexBytes;
        for (int index = indexBytes; index <= data.Length; index++)
        {
            if (index != data.Length && data[index] != 0)
            {
                continue;
            }

            if (index > start)
            {
                string value = Encoding.ASCII.GetString(
                    data,
                    start,
                    index - start);
                if (!value.StartsWith("VER2", StringComparison.Ordinal))
                {
                    calls.Add(value);
                }
            }

            start = index + 1;
        }

        return calls
            .Select(call => new ReferenceRow(call, EmptyFields))
            .ToArray();
    }

    private static ReferenceRow[] ReadCallHistory(Stream stream)
    {
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        string[]? headers = null;
        var rows = new List<ReferenceRow>();
        while (reader.ReadLine() is string line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            string[] values = ParseCsv(trimmed);
            if (values.Length == 0)
            {
                continue;
            }

            if (values[0].Equals("!!Order!!", StringComparison.Ordinal))
            {
                headers = values.Skip(1).ToArray();
                continue;
            }

            int callIndex = headers is null
                ? 0
                : Array.FindIndex(
                    headers,
                    value => value.Equals(
                        "Call",
                        StringComparison.OrdinalIgnoreCase));
            if (callIndex < 0 || callIndex >= values.Length)
            {
                continue;
            }

            string call = values[callIndex].Trim().ToUpperInvariant();
            if (call.Length < 3 || !call.Any(char.IsDigit))
            {
                continue;
            }

            var fields = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            if (headers is not null)
            {
                for (int index = 0;
                     index < Math.Min(headers.Length, values.Length);
                     index++)
                {
                    fields[headers[index]] = values[index].Trim();
                }
            }
            else if (values.Length > 1)
            {
                fields["Exch1"] = values[1].Trim();
            }

            rows.Add(new(call, fields));
        }

        return [.. rows];
    }

    private static string[] ParseCsv(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        for (int index = 0; index < line.Length; index++)
        {
            char value = line[index];
            if (value == '"')
            {
                if (quoted
                    && index + 1 < line.Length
                    && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (value == ',' && !quoted)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(value);
            }
        }

        values.Add(current.ToString());
        return [.. values];
    }

    private static int ParseInt(string value) =>
        int.TryParse(value, out int parsed) ? parsed : 0;

    private static readonly IReadOnlyDictionary<string, string> EmptyFields =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private sealed record ReferenceRow(
        string Call,
        IReadOnlyDictionary<string, string> Fields)
    {
        public bool QrmDxccEligible { get; private set; }

        public bool QrmNaqpLocal { get; private set; }

        public string? QrmNaqpStateRepair { get; private set; }

        public string Get(
            string first,
            string? second = null,
            string? third = null,
            string fallback = "")
        {
            foreach (string? key in new[] { first, second, third })
            {
                if (key is not null
                    && Fields.TryGetValue(key, out string? value)
                    && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return fallback;
        }

        public bool HasValue(string key)
        {
            return Fields.TryGetValue(key, out string? value)
                && !string.IsNullOrWhiteSpace(value);
        }

        public void Set(string key, string value)
        {
            if (Fields is not Dictionary<string, string> mutable)
            {
                throw new InvalidOperationException(
                    "The station reference row is immutable.");
            }

            mutable[key] = value;
        }

        public void SetQrmDxccMetadata(
            bool eligible,
            bool naqpLocal,
            string? naqpStateRepair)
        {
            QrmDxccEligible = eligible;
            QrmNaqpLocal = naqpLocal;
            QrmNaqpStateRepair = naqpStateRepair;
        }

        public void ApplyQrmNaqpStateRepair()
        {
            if (QrmNaqpStateRepair is not string stateRepair)
            {
                return;
            }

            Set("State", stateRepair);
            QrmNaqpStateRepair = null;
        }
    }
}
