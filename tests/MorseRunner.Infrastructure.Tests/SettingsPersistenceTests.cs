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
        Assert.Equal(
            "preserve-in-source",
            first.Values[LegacySettingsImporter.PreservedValueKey(
                "Station",
                "Unknown")]);
    }

    [Fact]
    public void LegacyImportTranslatesCeEncodingSemantics()
    {
        LegacyIniDocument source = LegacyIniDocument.Parse(
            """
            [Station]
            Pitch=6
            BandWidth=4
            SerialNR=2
            SelfMonVolume=-99
            CqWpxExchange=5NN 007
            Qsk=1
            SaveWav=1
            [Contest]
            SimContest=9
            DefaultRunMode=3
            Duration=47
            CompetitionDuration=99
            [System]
            BufSize=4
            ShowCallsignInfo=0
            [Settings]
            WpmStepRate=0
            RitStepIncr=700
            SingleCallStartDelay=3000
            [Band]
            Qsb=1
            Qrm=0
            Qrn=1
            Flutter=0
            Lids=1
            """);

        SettingsDocument imported = LegacySettingsImporter.Import(source);

        Assert.Equal("600", imported.Values["Station.Pitch"]);
        Assert.Equal("300", imported.Values["Station.BandWidth"]);
        Assert.Equal("scAcag", imported.Values["Contest.SimContest"]);
        Assert.Equal("rmWpx", imported.Values["Contest.DefaultRunMode"]);
        Assert.Equal("2", imported.Values["Station.SerialNR"]);
        Assert.Equal("1024", imported.Values["System.BufSize"]);
        Assert.Equal("47", imported.Values["Contest.Duration"]);
        Assert.Equal("60", imported.Values["Contest.CompetitionDuration"]);
        Assert.Equal("-60", imported.Values["Station.SelfMonVolume"]);
        Assert.Equal("1", imported.Values["Settings.WpmStepRate"]);
        Assert.Equal("500", imported.Values["Settings.RitStepIncr"]);
        Assert.Equal(
            "2500",
            imported.Values["Settings.SingleCallStartDelay"]);
        Assert.Equal(
            "5NN 007",
            imported.Values["Station.CqWpxExchange"]);
        Assert.Equal("True", imported.Values["Station.Qsk"]);
        Assert.Equal("True", imported.Values["Station.SaveWav"]);
        Assert.Equal("True", imported.Values["Band.Qsb"]);
        Assert.Equal("False", imported.Values["Band.Qrm"]);
        Assert.Equal("True", imported.Values["Band.Qrn"]);
        Assert.Equal("False", imported.Values["Band.Flutter"]);
        Assert.Equal("True", imported.Values["Band.Lids"]);
        Assert.Equal("False", imported.Values["System.ShowCallsignInfo"]);
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
    public async Task MissingProjectSettingsImportsLegacyIniOnlyOnce()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "MorseRunnerXPlat.LegacyImport",
            Guid.NewGuid().ToString("N"));
        var paths = new ApplicationPaths(root);
        string settingsPath = Path.Combine(paths.Settings, "settings.json");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                paths.LegacySettingsImport,
                "[Station]" + Environment.NewLine
                    + "Call=K7ABC" + Environment.NewLine
                    + "Pitch=6" + Environment.NewLine,
                TestContext.Current.CancellationToken);
            var store = new SettingsStore(
                settingsPath,
                paths.LegacySettingsImport);

            SettingsLoadResult imported = await store.LoadAsync(
                TestContext.Current.CancellationToken);

            Assert.False(imported.Recovered);
            Assert.Contains("Imported legacy", imported.Diagnostic);
            Assert.Equal("K7ABC", imported.Document.Values["Station.Call"]);
            Assert.Equal("600", imported.Document.Values["Station.Pitch"]);
            Assert.True(File.Exists(settingsPath));

            await File.WriteAllTextAsync(
                paths.LegacySettingsImport,
                "[Station]" + Environment.NewLine
                    + "Call=N0NEW" + Environment.NewLine
                    + "Pitch=2" + Environment.NewLine,
                TestContext.Current.CancellationToken);
            SettingsLoadResult restarted = await store.LoadAsync(
                TestContext.Current.CancellationToken);

            Assert.False(restarted.Recovered);
            Assert.Null(restarted.Diagnostic);
            Assert.Equal("K7ABC", restarted.Document.Values["Station.Call"]);
            Assert.Equal("600", restarted.Document.Values["Station.Pitch"]);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SettingsSavePreservesUnknownAndUnconsumedLegacyValues()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "MorseRunnerXPlat.PreservedSettings",
            Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "settings.json");
        try
        {
            SettingsDocument imported = LegacySettingsImporter.Import(
                LegacyIniDocument.Parse(
                    """
                    [Future]
                    Mystery=keep-me
                    [Station]
                    CallsFromKeyer=1
                    Call=K7ABC
                    """));
            var store = new SettingsStore(path);
            await store.SaveAsync(
                imported,
                TestContext.Current.CancellationToken);
            await store.SaveAsync(
                new SettingsDocument(
                    SettingsDocument.CurrentSchemaVersion,
                    new Dictionary<string, string>
                    {
                        ["Station.Call"] = "K7XYZ",
                    }),
                TestContext.Current.CancellationToken);

            SettingsLoadResult restarted = await store.LoadAsync(
                TestContext.Current.CancellationToken);

            Assert.Equal("K7XYZ", restarted.Document.Values["Station.Call"]);
            Assert.Equal(
                "True",
                restarted.Document.Values["Station.CallsFromKeyer"]);
            Assert.Equal(
                "keep-me",
                restarted.Document.Values[
                    LegacySettingsImporter.PreservedValueKey(
                        "Future",
                        "Mystery")]);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
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
                paths.LegacySettingsImport,
                paths.Results,
                paths.Recordings,
                paths.Cache,
                paths.Runtime,
                paths.Temporary,
            },
            path => Assert.StartsWith(paths.Root, path, StringComparison.Ordinal));
    }
}
