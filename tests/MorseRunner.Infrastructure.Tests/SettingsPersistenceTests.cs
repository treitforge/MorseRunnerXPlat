namespace MorseRunner.Infrastructure.Tests;

public sealed class SettingsPersistenceTests
{
    [Fact]
    public void LegacySchemaAccountsForEveryPinnedSetting()
    {
        Assert.Equal(60, LegacySettingSchema.All.Count);
        Assert.True(
            LegacySettingSchema.TryGet(
                "Station",
                "NRDigits",
                out LegacySettingDescriptor? migrated));
        Assert.True(
            migrated!.Operations.HasFlag(LegacySettingOperation.Delete));
        Assert.True(
            migrated.Operations.HasFlag(LegacySettingOperation.Exists));
    }

    [Fact]
    public void LegacyImportIsOneWayAndIdempotent()
    {
        LegacyIniDocument source = LegacyIniDocument.Parse(
            """
            [Station]
            Call=W7SST
            Wpm=32
            Unknown=preserve-in-source
            [Band]
            Qrn=0.25
            """);

        SettingsDocument first = LegacySettingsImporter.Import(source);
        SettingsDocument second = LegacySettingsImporter.Import(source, first);

        Assert.Equal("W7SST", first.Values["Station.Call"]);
        Assert.Equal("32", first.Values["Station.Wpm"]);
        Assert.Equal("0.25", first.Values["Band.Qrn"]);
        Assert.Equal(first.SchemaVersion, second.SchemaVersion);
        Assert.Equal(
            first.Values.OrderBy(pair => pair.Key),
            second.Values.OrderBy(pair => pair.Key));
        Assert.DoesNotContain("Station.Unknown", first.Values.Keys);
    }

    [Fact]
    public async Task SettingsWriteIsAtomicAndMalformedInputRecovers()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "MorseRunnerXPlat.Tests",
            Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new SettingsStore(path);
            var document = new SettingsDocument(
                SettingsDocument.CurrentSchemaVersion,
                new Dictionary<string, string> { ["Station.Call"] = "W7SST" });

            await store.SaveAsync(document, TestContext.Current.CancellationToken);
            SettingsLoadResult loaded = await store.LoadAsync(
                TestContext.Current.CancellationToken);

            Assert.False(loaded.Recovered);
            Assert.Equal("W7SST", loaded.Document.Values["Station.Call"]);

            await File.WriteAllTextAsync(
                path,
                "{bad-json",
                TestContext.Current.CancellationToken);
            SettingsLoadResult recovered = await store.LoadAsync(
                TestContext.Current.CancellationToken);
            Assert.True(recovered.Recovered);
            Assert.NotNull(recovered.Diagnostic);
            Assert.Empty(recovered.Document.Values);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PriorSettingsSchemaUpgradesWithoutLosingValues()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "MorseRunnerXPlat.SettingsUpgrade",
            Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(
                path,
                """
                {
                  "schemaVersion": 1,
                  "values": {
                    "Station.Call": "W7SST"
                  }
                }
                """,
                TestContext.Current.CancellationToken);

            SettingsLoadResult loaded = await new SettingsStore(path).LoadAsync(
                TestContext.Current.CancellationToken);

            Assert.False(loaded.Recovered);
            Assert.Equal(
                SettingsDocument.CurrentSchemaVersion,
                loaded.Document.SchemaVersion);
            Assert.Equal("W7SST", loaded.Document.Values["Station.Call"]);
            Assert.Contains("upgraded", loaded.Diagnostic);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ApplicationPathsRemainInsideTheSelectedRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "MorseRunnerXPlat.Paths",
            Guid.NewGuid().ToString("N"));
        var paths = new ApplicationPaths(root);

        Assert.All(
            new[]
            {
                paths.Settings,
                paths.Results,
                paths.Recordings,
                paths.Cache,
                paths.Runtime,
                paths.Temporary,
            },
            path => Assert.StartsWith(paths.Root, path, StringComparison.Ordinal));
    }
}
