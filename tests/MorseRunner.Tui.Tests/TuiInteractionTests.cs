using MorseRunner.Client;
using MorseRunner.Domain;
using MorseRunner.Infrastructure;

namespace MorseRunner.Tui.Tests;

public sealed class TuiInteractionTests
{
    [Theory]
    [InlineData(ConsoleKey.F1, '\0', ConsoleModifiers.None, TuiActionKind.SendCq)]
    [InlineData(ConsoleKey.F9, '\0', ConsoleModifiers.None, TuiActionKind.StartPileup)]
    [InlineData(ConsoleKey.F9, '\0', ConsoleModifiers.Shift, TuiActionKind.StartSingle)]
    [InlineData(ConsoleKey.F9, '\0', ConsoleModifiers.Control, TuiActionKind.StartHst)]
    [InlineData(ConsoleKey.F10, '\0', ConsoleModifiers.None, TuiActionKind.Stop)]
    [InlineData(ConsoleKey.F11, '\0', ConsoleModifiers.None, TuiActionKind.Wipe)]
    [InlineData(ConsoleKey.F12, '\0', ConsoleModifiers.None, TuiActionKind.SendNumberQuestion)]
    [InlineData(ConsoleKey.Enter, '\r', ConsoleModifiers.None, TuiActionKind.EnterSendMessage)]
    [InlineData(ConsoleKey.Enter, '\r', ConsoleModifiers.Control, TuiActionKind.SaveQso)]
    [InlineData(ConsoleKey.Enter, '\r', ConsoleModifiers.Shift, TuiActionKind.SaveQso)]
    [InlineData(ConsoleKey.Enter, '\r', ConsoleModifiers.Alt, TuiActionKind.SaveQso)]
    [InlineData(ConsoleKey.UpArrow, '\0', ConsoleModifiers.None, TuiActionKind.RitUp)]
    [InlineData(ConsoleKey.UpArrow, '\0', ConsoleModifiers.Control, TuiActionKind.BandwidthUp)]
    [InlineData(ConsoleKey.W, '\u0017', ConsoleModifiers.Control, TuiActionKind.Wipe)]
    [InlineData(ConsoleKey.D2, '\0', ConsoleModifiers.Control, TuiActionKind.ToggleQsb)]
    [InlineData(ConsoleKey.S, '\u0013', ConsoleModifiers.Control, TuiActionKind.ToggleSettings)]
    [InlineData(ConsoleKey.T, '\u0014', ConsoleModifiers.Control, TuiActionKind.ToggleResults)]
    [InlineData(ConsoleKey.G, '\a', ConsoleModifiers.Control, TuiActionKind.ToggleDiagnostics)]
    [InlineData(ConsoleKey.A, '\u0001', ConsoleModifiers.Control, TuiActionKind.ToggleRecording)]
    [InlineData(ConsoleKey.E, '\u0005', ConsoleModifiers.Control, TuiActionKind.ExportJson)]
    public void LegacyKeysMapToSemanticActions(
        ConsoleKey key,
        char character,
        ConsoleModifiers modifiers,
        TuiActionKind expected)
    {
        var keyInfo = new ConsoleKeyInfo(
            character,
            key,
            modifiers.HasFlag(ConsoleModifiers.Shift),
            modifiers.HasFlag(ConsoleModifiers.Alt),
            modifiers.HasFlag(ConsoleModifiers.Control));

        Assert.Equal(expected, TuiKeyRouter.Map(keyInfo).Kind);
    }

    [Theory]
    [InlineData(';', TuiActionKind.SendCallAndExchange)]
    [InlineData('.', TuiActionKind.LogQso)]
    [InlineData(',', TuiActionKind.LogQso)]
    [InlineData('+', TuiActionKind.LogQso)]
    [InlineData('[', TuiActionKind.LogQso)]
    [InlineData('?', TuiActionKind.ToggleHelp)]
    public void LegacyPunctuationMapsToSemanticActions(
        char character,
        TuiActionKind expected)
    {
        var keyInfo = new ConsoleKeyInfo(
            character,
            ConsoleKey.Oem1,
            shift: false,
            alt: false,
            control: false);

        Assert.Equal(expected, TuiKeyRouter.Map(keyInfo).Kind);
    }

    [Fact]
    public async Task RitUpUsesCeDefaultFiftyHertzStep()
    {
        await using InProcessMorseRunnerClient client =
            InProcessMorseRunnerClient.CreateDefault();
        using var application = new TuiApplication(client, isHosted: false);
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await application.InitializeAsync(cancellationToken);
        await application.HandleAsync(
            new(TuiActionKind.StartSingle),
            cancellationToken);

        await application.HandleAsync(
            new(TuiActionKind.RitUp),
            cancellationToken);

        SessionSnapshot snapshot = Assert.IsType<SessionSnapshot>(
            application.State.Snapshot);
        Assert.Equal(50, snapshot.RitOffsetHz);
    }

    [Fact]
    public async Task SpeedUpUsesCeDefaultTwoWpmStep()
    {
        await using InProcessMorseRunnerClient client =
            InProcessMorseRunnerClient.CreateDefault();
        using var application = new TuiApplication(client, isHosted: false);
        application.State.WordsPerMinute = 30;
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await application.InitializeAsync(cancellationToken);
        await application.HandleAsync(
            new(TuiActionKind.StartSingle),
            cancellationToken);

        await application.HandleAsync(
            new(TuiActionKind.SpeedUp),
            cancellationToken);

        SessionSnapshot snapshot = Assert.IsType<SessionSnapshot>(
            application.State.Snapshot);
        Assert.Equal(32, snapshot.CurrentWordsPerMinute);
    }

    [Fact]
    public async Task SpeedUpUsesPersistedCustomWpmStep()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"MorseRunner-Tui-{Guid.NewGuid():N}");
        try
        {
            var paths = new ApplicationPaths(root);
            paths.EnsureWritableDirectories();
            var store = new SettingsStore(
                Path.Combine(paths.Settings, "settings.json"));
            CancellationToken cancellationToken =
                TestContext.Current.CancellationToken;
            await store.SaveAsync(
                new SettingsDocument(
                    SettingsDocument.CurrentSchemaVersion,
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["Settings.WpmStepRate"] = "7",
                    }),
                cancellationToken);
            await using InProcessMorseRunnerClient client =
                InProcessMorseRunnerClient.CreateDefault();
            using var application = new TuiApplication(
                client,
                isHosted: false,
                paths);
            application.State.WordsPerMinute = 30;
            await application.InitializeAsync(cancellationToken);
            await application.HandleAsync(
                new(TuiActionKind.StartSingle),
                cancellationToken);

            await application.HandleAsync(
                new(TuiActionKind.SpeedUp),
                cancellationToken);

            SessionSnapshot snapshot = Assert.IsType<SessionSnapshot>(
                application.State.Snapshot);
            Assert.Equal(37, snapshot.CurrentWordsPerMinute);
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
    public async Task SpeedDownUsesPersistedCustomWpmStep()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"MorseRunner-Tui-{Guid.NewGuid():N}");
        try
        {
            var paths = new ApplicationPaths(root);
            paths.EnsureWritableDirectories();
            var store = new SettingsStore(
                Path.Combine(paths.Settings, "settings.json"));
            CancellationToken cancellationToken =
                TestContext.Current.CancellationToken;
            await store.SaveAsync(
                new SettingsDocument(
                    SettingsDocument.CurrentSchemaVersion,
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["Settings.WpmStepRate"] = "7",
                    }),
                cancellationToken);
            await using InProcessMorseRunnerClient client =
                InProcessMorseRunnerClient.CreateDefault();
            using var application = new TuiApplication(
                client,
                isHosted: false,
                paths);
            application.State.WordsPerMinute = 30;
            await application.InitializeAsync(cancellationToken);
            await application.HandleAsync(
                new(TuiActionKind.StartSingle),
                cancellationToken);

            await application.HandleAsync(
                new(TuiActionKind.SpeedDown),
                cancellationToken);

            SessionSnapshot snapshot = Assert.IsType<SessionSnapshot>(
                application.State.Snapshot);
            Assert.Equal(23, snapshot.CurrentWordsPerMinute);
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
    public async Task SpeedUpRoundsToNextFiveWpmBoundaryInHstMode()
    {
        await using InProcessMorseRunnerClient client =
            InProcessMorseRunnerClient.CreateDefault();
        using var application = new TuiApplication(client, isHosted: false);
        application.State.WordsPerMinute = 32;
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await application.InitializeAsync(cancellationToken);
        await application.HandleAsync(
            new(TuiActionKind.StartHst),
            cancellationToken);

        await application.HandleAsync(
            new(TuiActionKind.SpeedUp),
            cancellationToken);

        SessionSnapshot snapshot = Assert.IsType<SessionSnapshot>(
            application.State.Snapshot);
        Assert.Equal(35, snapshot.CurrentWordsPerMinute);
    }

    [Fact]
    public async Task SpeedDownRoundsToPreviousFiveWpmBoundaryInHstMode()
    {
        await using InProcessMorseRunnerClient client =
            InProcessMorseRunnerClient.CreateDefault();
        using var application = new TuiApplication(client, isHosted: false);
        application.State.WordsPerMinute = 33;
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await application.InitializeAsync(cancellationToken);
        await application.HandleAsync(
            new(TuiActionKind.StartHst),
            cancellationToken);

        await application.HandleAsync(
            new(TuiActionKind.SpeedDown),
            cancellationToken);

        SessionSnapshot snapshot = Assert.IsType<SessionSnapshot>(
            application.State.Snapshot);
        Assert.Equal(30, snapshot.CurrentWordsPerMinute);
    }

    [Fact]
    public async Task SettingsWpmCanEnterCeUpperRange()
    {
        await using InProcessMorseRunnerClient client =
            InProcessMorseRunnerClient.CreateDefault();
        using var application = new TuiApplication(client, isHosted: false);
        application.State.View = TuiView.Settings;
        application.State.SettingsIndex = 1;
        application.State.WordsPerMinute = 100;
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await application.InitializeAsync(cancellationToken);

        await application.HandleAsync(
            new(TuiActionKind.IncreaseSetting),
            cancellationToken);

        Assert.Equal(101, application.State.WordsPerMinute);
    }

    [Fact]
    public async Task SettingsWpmClampsAtCeLowerRange()
    {
        await using InProcessMorseRunnerClient client =
            InProcessMorseRunnerClient.CreateDefault();
        using var application = new TuiApplication(client, isHosted: false);
        application.State.View = TuiView.Settings;
        application.State.SettingsIndex = 1;
        application.State.WordsPerMinute = 10;
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await application.InitializeAsync(cancellationToken);

        await application.HandleAsync(
            new(TuiActionKind.DecreaseSetting),
            cancellationToken);

        Assert.Equal(10, application.State.WordsPerMinute);
    }

    [Fact]
    public void RendererAdaptsToCompactAndWideTerminals()
    {
        var state = new TuiState
        {
            Snapshot = new SessionSnapshot(
                Guid.Empty,
                SessionId.New(),
                SessionState.Running,
                1,
                12,
                6144,
                TimeSpan.FromMilliseconds(557),
                12345,
                ContestCatalog.All[0].Id,
                new("rmPileup"),
                "K1ABC",
                1,
                1,
                null,
                ActiveOperatorState: OperatorState.NeedNumber),
            Call = "K1ABC",
            Qsos =
            [
                new Qso
                {
                    Timestamp = DateTimeOffset.UnixEpoch,
                    Call = "K1ABC",
                    Rst = 599,
                    Exchange1 = "123",
                    Points = 1,
                },
            ],
        };

        string compact = TuiRenderer.Render(state, 80, 24);
        string wide = TuiRenderer.Render(state, 140, 40);

        Assert.Contains("K1ABC", compact, StringComparison.Ordinal);
        Assert.Contains("SCORE 1", wide, StringComparison.Ordinal);
        Assert.Contains("CALLER NEED EXCHANGE", wide, StringComparison.Ordinal);
        Assert.Contains(
            "PgUp/PgDn WPM",
            wide,
            StringComparison.Ordinal);
        Assert.Contains(
            "Ctrl+G diagnostics",
            wide,
            StringComparison.Ordinal);
        Assert.False(compact.EndsWith(Environment.NewLine, StringComparison.Ordinal));
        Assert.False(wide.EndsWith(Environment.NewLine, StringComparison.Ordinal));
        Assert.Equal(24, compact.Split(Environment.NewLine).Length);
        Assert.Equal(40, wide.Split(Environment.NewLine).Length);
        Assert.All(
            compact.Split(Environment.NewLine),
            line => Assert.True(line.Length <= 80));
        Assert.All(
            wide.Split(Environment.NewLine),
            line => Assert.True(line.Length <= 140));
    }

    [Fact]
    public void RendererAddsAnsiStylingOnlyWhenRequested()
    {
        var state = new TuiState();

        string plain = TuiRenderer.Render(state, 100, 28);
        string styled = TuiRenderer.Render(state, 100, 28, useColor: true);

        Assert.DoesNotContain("\u001b[", plain, StringComparison.Ordinal);
        Assert.Contains("\u001b[", styled, StringComparison.Ordinal);
        Assert.False(styled.EndsWith(Environment.NewLine, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SettingsPersistAcrossTuiRestarts()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"MorseRunner-Tui-{Guid.NewGuid():N}");
        try
        {
            var paths = new ApplicationPaths(root);
            paths.EnsureWritableDirectories();
            await using (InProcessMorseRunnerClient firstClient =
                InProcessMorseRunnerClient.CreateDefault())
            using (var first = new TuiApplication(
                firstClient,
                isHosted: false,
                paths))
            {
                await first.InitializeAsync(CancellationToken.None);
                first.State.WordsPerMinute = 37;
                first.State.ReceiveSpeedBelowWpm = 4;
                first.State.SerialNumberRange =
                    SerialNumberRangeMode.Custom;
                first.State.CustomSerialNumberMinimum = 40;
                first.State.CustomSerialNumberExclusiveMaximum = 80;
                first.State.CustomSerialNumberMinimumDigits = 3;
                first.State.CustomSerialNumberMaximumDigits = 4;
                first.State.HstOperatorName = "W7SST";
                await first.HandleAsync(
                    new(TuiActionKind.ToggleQsb),
                    CancellationToken.None);
                await first.HandleAsync(
                    new(TuiActionKind.ToggleRecording),
                    CancellationToken.None);
                await first.SavePreferencesAsync(CancellationToken.None);
            }

            await using InProcessMorseRunnerClient secondClient =
                InProcessMorseRunnerClient.CreateDefault();
            using var second = new TuiApplication(
                secondClient,
                isHosted: false,
                paths);
            await second.InitializeAsync(CancellationToken.None);

            Assert.Equal(37, second.State.WordsPerMinute);
            Assert.Equal(4, second.State.ReceiveSpeedBelowWpm);
            Assert.Equal(
                SerialNumberRangeMode.Custom,
                second.State.SerialNumberRange);
            Assert.Equal(40, second.State.CustomSerialNumberMinimum);
            Assert.Equal(80, second.State.CustomSerialNumberExclusiveMaximum);
            Assert.Equal(3, second.State.CustomSerialNumberMinimumDigits);
            Assert.Equal(4, second.State.CustomSerialNumberMaximumDigits);
            Assert.Equal("W7SST", second.State.HstOperatorName);
            Assert.True(second.State.Qsb);
            Assert.True(second.State.RecordingEnabled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CompletedResultCanBeViewedAndExported()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"MorseRunner-Tui-{Guid.NewGuid():N}");
        try
        {
            var paths = new ApplicationPaths(root);
            paths.EnsureWritableDirectories();
            await using InProcessMorseRunnerClient client =
                InProcessMorseRunnerClient.CreateDefault();
            using var application = new TuiApplication(
                client,
                isHosted: false,
                paths);
            await application.InitializeAsync(CancellationToken.None);
            await application.HandleAsync(
                new(TuiActionKind.StartPileup),
                CancellationToken.None);
            application.State.Call = "K1ABC";
            application.State.Exchange1 = "123";
            await application.HandleAsync(
                new(TuiActionKind.LogQso),
                CancellationToken.None);
            await application.HandleAsync(
                new(TuiActionKind.Stop),
                CancellationToken.None);
            await application.HandleAsync(
                new(TuiActionKind.ToggleResults),
                CancellationToken.None);
            await application.HandleAsync(
                new(TuiActionKind.ExportJson),
                CancellationToken.None);

            Assert.Equal(TuiView.Results, application.State.View);
            Assert.NotNull(application.State.Result);
            Assert.Equal(1, application.State.Result.QsoCount);
            Assert.NotNull(application.State.PersonalHighScore);
            Assert.True(File.Exists(application.State.LastExportPath));
            Assert.Contains(
                "\"QsoCount\": 1",
                await File.ReadAllTextAsync(
                    application.State.LastExportPath,
                    TestContext.Current.CancellationToken),
                StringComparison.Ordinal);
            Assert.Contains(
                "QSO RATE",
                TuiRenderer.Render(application.State, 100, 28),
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RendererExposesSettingsResultsAndDiagnosticsTextually()
    {
        var state = new TuiState
        {
            View = TuiView.Settings,
            HstOperatorName = "W7SST",
            CustomSerialNumberMinimumDigits = 3,
            CustomSerialNumberMaximumDigits = 4,
            ConnectionStatus = "Connected to authenticated local host.",
        };

        string settings = TuiRenderer.Render(state, 100, 28);
        state.View = TuiView.Results;
        string results = TuiRenderer.Render(state, 100, 28);
        state.View = TuiView.Diagnostics;
        string diagnostics = TuiRenderer.Render(state, 64, 18);

        Assert.Contains("ADVANCED SETTINGS", settings, StringComparison.Ordinal);
        Assert.Contains("HST OPERATOR", settings, StringComparison.Ordinal);
        Assert.Contains("CUSTOM MINIMUM      001", settings, StringComparison.Ordinal);
        Assert.Contains("CUSTOM MAXIMUM      0099", settings, StringComparison.Ordinal);
        Assert.Contains("RESULTS", results, StringComparison.Ordinal);
        Assert.Contains("No completed result", results, StringComparison.Ordinal);
        Assert.Contains("DIAGNOSTICS", diagnostics, StringComparison.Ordinal);
        Assert.Contains("Connected", diagnostics, StringComparison.Ordinal);
        Assert.All(
            diagnostics.Split(Environment.NewLine),
            line => Assert.True(line.Length <= 64));
    }

    [Theory]
    [InlineData(null, null, false, true)]
    [InlineData("xterm-256color", "1", false, false)]
    [InlineData("dumb", null, false, false)]
    [InlineData("xterm-256color", null, true, false)]
    public void TerminalCapabilitiesHonorNoColorAndDumbTerminals(
        string? term,
        string? noColor,
        bool forceNoColor,
        bool expectedColor)
    {
        TerminalCapabilities capabilities =
            TerminalCapabilities.Detect(term, noColor, forceNoColor);

        Assert.Equal(expectedColor, capabilities.UseColor);
        Assert.Equal(
            !String.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase),
            capabilities.UseAnsi);
    }

    [Fact]
    public async Task HostedModeExplainsThatRecordingBelongsToTheHost()
    {
        await using InProcessMorseRunnerClient client =
            InProcessMorseRunnerClient.CreateDefault();
        using var application = new TuiApplication(client, isHosted: true);

        await application.HandleAsync(
            new(TuiActionKind.ToggleRecording),
            CancellationToken.None);

        Assert.False(application.State.RecordingEnabled);
        Assert.Contains(
            "controlled by the engine host",
            application.State.Status,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecordingPreferenceCreatesAndDiscoversWavPaths()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"MorseRunner-Tui-{Guid.NewGuid():N}");
        try
        {
            var paths = new ApplicationPaths(root);
            paths.EnsureWritableDirectories();
            var preference = new TuiRecordingPreference(paths);

            Assert.Null(preference.CreatePath());
            preference.Enabled = true;
            string path = Assert.IsType<string>(preference.CreatePath());
            File.WriteAllBytes(path, [0x52, 0x49, 0x46, 0x46]);

            Assert.EndsWith(
                ".wav",
                path,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(path, preference.DiscoverLatest());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
