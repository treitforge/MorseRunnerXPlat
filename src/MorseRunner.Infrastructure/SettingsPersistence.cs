using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MorseRunner.Domain;

namespace MorseRunner.Infrastructure;

public sealed record SettingsDocument(
    int SchemaVersion,
    IReadOnlyDictionary<string, string> Values)
{
    public const int CurrentSchemaVersion = 2;

    public static SettingsDocument Empty { get; } =
        new(
            CurrentSchemaVersion,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

public sealed record SettingsLoadResult(
    SettingsDocument Document,
    bool Recovered,
    string? Diagnostic);

public sealed record LegacySettingsImportResult(
    SettingsDocument Document,
    IReadOnlyList<string> Diagnostics);

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly string? _legacyIniPath;

    public SettingsStore(string path, string? legacyIniPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _legacyIniPath = String.IsNullOrWhiteSpace(legacyIniPath)
            ? null
            : Path.GetFullPath(legacyIniPath);
    }

    public async Task<SettingsLoadResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            if (_legacyIniPath is not null && File.Exists(_legacyIniPath))
            {
                return await ImportLegacyAsync(cancellationToken);
            }

            return new SettingsLoadResult(SettingsDocument.Empty, false, null);
        }

        try
        {
            await using FileStream stream = File.OpenRead(_path);
            SettingsDocument? document =
                await JsonSerializer.DeserializeAsync<SettingsDocument>(
                    stream,
                    JsonOptions,
                    cancellationToken);
            if (document is null
                || document.SchemaVersion <= 0
                || document.SchemaVersion > SettingsDocument.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    "The settings schema version is unsupported.");
            }

            if (document.SchemaVersion < SettingsDocument.CurrentSchemaVersion)
            {
                SettingsDocument upgraded = Upgrade(document);
                return new SettingsLoadResult(
                    upgraded,
                    false,
                    $"Settings upgraded from schema {document.SchemaVersion}"
                    + $" to {upgraded.SchemaVersion}.");
            }

            return new SettingsLoadResult(document, false, null);
        }
        catch (Exception exception)
            when (exception is JsonException or IOException or InvalidDataException)
        {
            return new SettingsLoadResult(
                SettingsDocument.Empty,
                true,
                $"Settings recovery: {exception.Message}");
        }
    }

    public async Task SaveAsync(
        SettingsDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        SettingsDocument effectiveDocument = await MergeExistingValuesAsync(
            document,
            cancellationToken);
        string? directory = Path.GetDirectoryName(_path);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16_384,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    effectiveDocument,
                    JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static SettingsDocument Upgrade(SettingsDocument document)
    {
        IReadOnlyDictionary<string, string> values =
            new Dictionary<string, string>(
                document.Values,
                StringComparer.OrdinalIgnoreCase);
        return new(SettingsDocument.CurrentSchemaVersion, values);
    }

    private async Task<SettingsDocument> MergeExistingValuesAsync(
        SettingsDocument document,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return document;
        }

        SettingsDocument? existing;
        try
        {
            await using FileStream stream = File.OpenRead(_path);
            existing = await JsonSerializer.DeserializeAsync<SettingsDocument>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (Exception exception)
            when (exception is JsonException or IOException)
        {
            return document;
        }

        if (existing is null
            || existing.SchemaVersion <= 0
            || existing.SchemaVersion > SettingsDocument.CurrentSchemaVersion)
        {
            return document;
        }

        var values = new Dictionary<string, string>(
            existing.Values,
            StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in document.Values)
        {
            values[key] = value;
        }

        return new(document.SchemaVersion, values);
    }

    private async Task<SettingsLoadResult> ImportLegacyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            string text = await File.ReadAllTextAsync(
                _legacyIniPath!,
                cancellationToken);
            LegacySettingsImportResult imported =
                LegacySettingsImporter.ImportWithDiagnostics(
                    LegacyIniDocument.Parse(text));
            await SaveAsync(imported.Document, cancellationToken);
            string diagnostic = "Imported legacy settings from MorseRunner.ini.";
            if (imported.Diagnostics.Count > 0)
            {
                diagnostic += "\n" + String.Join("\n", imported.Diagnostics);
            }

            return new SettingsLoadResult(
                imported.Document,
                false,
                diagnostic);
        }
        catch (Exception exception)
            when (exception is FormatException
                or IOException
                or UnauthorizedAccessException)
        {
            return new SettingsLoadResult(
                SettingsDocument.Empty,
                true,
                $"Legacy settings recovery: {exception.Message}");
        }
    }
}

public static class LegacySettingsImporter
{
    private static readonly (string Key, string DefaultValue)[]
        SerialRangeSettings =
        [
            ("SerialNrMidContest", "50-500"),
            ("SerialNrEndContest", "500-5000"),
            ("SerialNrCustomRange", "01-99"),
        ];

    private static readonly HashSet<string> BooleanKeys = new(
        [
            "Band.Flutter",
            "Band.Lids",
            "Band.Qrm",
            "Band.Qrn",
            "Band.Qsb",
            "Debug.DebugCwDecoder",
            "Debug.DebugExchSettings",
            "Debug.DebugGhosting",
            "Station.CallsFromKeyer",
            "Station.GetWpmUsesGaussian",
            "Station.Qsk",
            "Station.SaveWav",
            "System.ShowCallsignInfo",
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string>
        MalformedIntegerDefaults = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Station.Pitch"] = "450",
            ["Station.BandWidth"] = "550",
            ["Station.Wpm"] = "25",
            ["Station.SerialNR"] = "0",
            ["Station.SelfMonVolume"] = "0",
            ["Contest.SimContest"] = "scWpx",
            ["Contest.DefaultRunMode"] = "rmPileup",
            ["Contest.Duration"] = "30",
            ["Contest.CompetitionDuration"] = "60",
            ["System.BufSize"] = "512",
            ["Settings.FarnsworthCharacterRate"] = "25",
            ["Settings.WpmStepRate"] = "2",
            ["Settings.RitStepIncr"] = "50",
            ["Settings.SingleCallStartDelay"] = "0",
            ["Band.Activity"] = "2",
        };

    public static SettingsDocument Import(
        LegacyIniDocument source,
        SettingsDocument? existing = null) =>
        ImportWithDiagnostics(source, existing).Document;

    public static LegacySettingsImportResult ImportWithDiagnostics(
        LegacyIniDocument source,
        SettingsDocument? existing = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var diagnostics = new List<string>();
        var values = new Dictionary<string, string>(
            existing?.Values
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        foreach (LegacySettingDescriptor descriptor in LegacySettingSchema.All)
        {
            if ((descriptor.Operations & LegacySettingOperation.Read) == 0
                || !source.TryGet(descriptor.Section, descriptor.Key, out string? value))
            {
                continue;
            }

            string targetKey = $"{descriptor.Section}.{descriptor.Key}";
            values.TryAdd(
                targetKey,
                TranslateValue(targetKey, value!));
        }

        if (source.TryGet("Station", "NRDigits", out string? legacyDigits))
        {
            values.Remove("Station.NRDigits");
            values["Station.SerialNR"] = TranslateLegacyNRDigits(
                legacyDigits!);
        }

        foreach ((string key, string defaultValue) in SerialRangeSettings)
        {
            string value = source.TryGet(
                "Station",
                key,
                out string? persistedValue)
                    ? persistedValue!
                    : defaultValue;
            string? error = GetSerialRangeError(value);
            if (error is not null)
            {
                diagnostics.Add(FormatSerialRangeDiagnostic(key, value, error));
            }
        }

        foreach ((string section, IReadOnlyDictionary<string, string> entries)
                 in source.Sections)
        {
            foreach ((string key, string value) in entries)
            {
                if (LegacySettingSchema.TryGet(section, key, out _))
                {
                    continue;
                }

                values.TryAdd(PreservedValueKey(section, key), value);
            }
        }

        return new LegacySettingsImportResult(
            new SettingsDocument(SettingsDocument.CurrentSchemaVersion, values),
            diagnostics);
    }

    public static string PreservedValueKey(string section, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return "LegacyIni.Preserved."
            + Convert.ToHexString(Encoding.UTF8.GetBytes(section))
            + "."
            + Convert.ToHexString(Encoding.UTF8.GetBytes(key));
    }

    private static string TranslateValue(string key, string value)
    {
        if (BooleanKeys.Contains(key))
        {
            return TranslateBoolean(value);
        }

        if (MalformedIntegerDefaults.TryGetValue(
                key,
                out string? defaultValue)
            && !TryParseInteger(value, out _))
        {
            return defaultValue;
        }

        return key switch
        {
            "Station.Pitch" => TranslateFrequencyIndex(
                value,
                minimumHz: 300,
                maximumIndex: 12),
            "Station.BandWidth" => TranslateFrequencyIndex(
                value,
                minimumHz: 100,
                maximumIndex: 10),
            "Contest.SimContest" => TranslateContest(value),
            "Contest.DefaultRunMode" => TranslateRunMode(value),
            "System.BufSize" => TranslateBufferSize(value),
            "Contest.CompetitionDuration" =>
                TranslateClampedInteger(value, 1, 60),
            "Station.SelfMonVolume" =>
                TranslateClampedInteger(value, -60, 0),
            "Settings.WpmStepRate" =>
                TranslateClampedInteger(value, 1, 20),
            "Settings.RitStepIncr" =>
                TranslateClampedInteger(value, -500, 500),
            "Settings.SingleCallStartDelay" =>
                TranslateClampedInteger(value, 0, 2_500),
            _ => value,
        };
    }

    private static string TranslateBoolean(string value)
    {
        if (Boolean.TryParse(value, out bool parsed))
        {
            return parsed.ToString();
        }

        return TryParseInteger(value, out int numeric)
            && numeric is 0 or 1
                ? (numeric == 1).ToString()
                : false.ToString();
    }

    private static string TranslateFrequencyIndex(
        string value,
        int minimumHz,
        int maximumIndex) =>
        TryParseInteger(value, out int index)
        && index >= 0
        && index <= maximumIndex
            ? (minimumHz + (index * 50)).ToString(
                CultureInfo.InvariantCulture)
            : value;

    private static string TranslateContest(string value)
    {
        if (!TryParseInteger(value, out int ordinal)
            || ordinal < 0
            || ordinal >= ContestCatalog.All.Count)
        {
            return value;
        }

        return ContestCatalog.All[ordinal].Id.Value;
    }

    private static string TranslateRunMode(string value)
    {
        if (!TryParseInteger(value, out int ordinal))
        {
            return value;
        }

        int effectiveOrdinal = Math.Clamp(ordinal, 1, 4);
        return RunModeCatalog.All[effectiveOrdinal].Value;
    }

    private static string TranslateBufferSize(string value)
    {
        if (!TryParseInteger(value, out int exponent))
        {
            return value;
        }

        int effectiveExponent = Math.Clamp(exponent == 0 ? 3 : exponent, 1, 5);
        return (64 << effectiveExponent).ToString(CultureInfo.InvariantCulture);
    }

    private static string TranslateLegacyNRDigits(string value)
    {
        int serialMode = TryParseInteger(value, out int legacyDigits)
            ? legacyDigits switch
            {
                2 => 3,
                3 => 1,
                4 => 2,
                _ => 0,
            }
            : 0;
        return serialMode.ToString(CultureInfo.InvariantCulture);
    }

    private static string? GetSerialRangeError(string value)
    {
        string[] parts = value.Split('-');
        if (parts.Length != 2
            || value.Count(character => character == '-') != 1
            || !TryParseInteger(parts[0], out int minimum)
            || !TryParseInteger(parts[1], out int maximum))
        {
            return $"Error: '{value}' is an invalid range.\r"
                + "Expecting min-max values with up to 4-digits each "
                + "(e.g. 100-300).";
        }

        if (minimum > 9_999 || maximum > 9_999)
        {
            return $"Error: '{value}' is an invalid range.\r"
                + "Expecting range values to be less than or equal to 9999.";
        }

        return minimum > maximum
            ? $"Error: '{value}' is an invalid range.\r"
                + "Expecting Min value to be less than Max value."
            : null;
    }

    private static string FormatSerialRangeDiagnostic(
        string key,
        string value,
        string error) =>
        "Error while reading MorseRunner.ini file.\r"
        + $"Invalid Keyword Value: '{key}={value}':\r"
        + error
        + "\rPlease correct this keyword or remove the MorseRunner.ini file.";

    private static string TranslateClampedInteger(
        string value,
        int minimum,
        int maximum) =>
        TryParseInteger(value, out int parsed)
            ? Math.Clamp(parsed, minimum, maximum).ToString(
                CultureInfo.InvariantCulture)
            : value;

    private static bool TryParseInteger(string value, out int parsed) =>
        Int32.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out parsed);
}
