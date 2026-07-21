using System.Globalization;
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
                    document,
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

    private async Task<SettingsLoadResult> ImportLegacyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            string text = await File.ReadAllTextAsync(
                _legacyIniPath!,
                cancellationToken);
            SettingsDocument imported = LegacySettingsImporter.Import(
                LegacyIniDocument.Parse(text));
            await SaveAsync(imported, cancellationToken);
            return new SettingsLoadResult(
                imported,
                false,
                "Imported legacy settings from MorseRunner.ini.");
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

    public static SettingsDocument Import(
        LegacyIniDocument source,
        SettingsDocument? existing = null)
    {
        ArgumentNullException.ThrowIfNull(source);
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

        return new SettingsDocument(SettingsDocument.CurrentSchemaVersion, values);
    }

    private static string TranslateValue(string key, string value)
    {
        if (BooleanKeys.Contains(key))
        {
            return TranslateBoolean(value);
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
                : value;
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
