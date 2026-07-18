using System.Text.Json;
using System.Text.Json.Serialization;

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

    public SettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task<SettingsLoadResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
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
}

public static class LegacySettingsImporter
{
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
            values.TryAdd(targetKey, value!);
        }

        return new SettingsDocument(SettingsDocument.CurrentSchemaVersion, values);
    }
}
