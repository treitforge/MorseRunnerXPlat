using MorseRunner.App.ViewModels;
using MorseRunner.Client;
using MorseRunner.Domain;
using MorseRunner.Infrastructure;

namespace MorseRunner.App.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void AudioDropIsReportedAsDegradedHealth()
    {
        var snapshot = new SessionSnapshot(
            Guid.NewGuid(),
            SessionId.New(),
            SessionState.Running,
            1,
            0,
            0,
            TimeSpan.Zero,
            12_345,
            new ContestId("scWpx"),
            new RunModeId("rmPileup"),
            null,
            0,
            0,
            null,
            AudioQueuedBlocks: 4,
            AudioDroppedBlockCount: 2,
            AudioOutputHealthy: true);

        Assert.Equal(
            "Degraded, 2 audio blocks dropped",
            MainWindowViewModel.FormatAudioHealth(snapshot));
    }

    [Fact]
    public async Task CleanProfileUsesApplicationDefaults()
    {
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());

        Assert.Equal("VE3NEA", viewModel.StationCall);
        Assert.Equal(25, viewModel.WordsPerMinute);
        Assert.Equal(450, viewModel.PitchHz);
        Assert.Equal(550, viewModel.BandwidthHz);
        Assert.Equal(2, viewModel.Activity);
        Assert.Equal(30, viewModel.DurationMinutes);
        Assert.Equal(60, viewModel.CompetitionDurationMinutes);
        Assert.Equal("scWpx", viewModel.SelectedContest.Id.Value);
        Assert.Equal("rmPileup", viewModel.SelectedRunMode.Id.Value);
        Assert.Equal(0, viewModel.ReceiveSpeedBelowWpm);
        Assert.Equal(0, viewModel.ReceiveSpeedAboveWpm);
        Assert.Empty(viewModel.HstOperatorName);
        Assert.False(viewModel.Qsk);
        Assert.False(viewModel.Qsb);
        Assert.False(viewModel.Qrm);
        Assert.False(viewModel.Qrn);
        Assert.False(viewModel.Flutter);
        Assert.False(viewModel.Lids);
    }

    [Fact]
    public async Task RstEntryStartsEmptyAndWipeRestoresEmptyEntryFields()
    {
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());

        Assert.Empty(viewModel.RstEntry);

        viewModel.CallEntry = "K1ABC";
        viewModel.RstEntry = "579";
        viewModel.Exchange1Entry = "123";
        viewModel.Exchange2Entry = "OR";
        EntryFocusRequestedEventArgs? focus = null;
        viewModel.EntryFocusRequested += (_, args) => focus = args;

        await viewModel.WipeCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.CallEntry);
        Assert.Empty(viewModel.RstEntry);
        Assert.Empty(viewModel.Exchange1Entry);
        Assert.Empty(viewModel.Exchange2Entry);
        Assert.Equal(EntryFocusTarget.Call, focus?.Target);
        Assert.False(focus?.SelectQuestionMark);
    }

    [Fact]
    public async Task WipeResetsRunningSessionEsmState()
    {
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());
        await viewModel.StartCommand.ExecuteAsync(null);
        viewModel.CallEntry = "KC7AVA";
        await viewModel.SendHisCallCommand.ExecuteAsync(null);
        await viewModel.SendExchangeCommand.ExecuteAsync(null);

        await viewModel.WipeCommand.ExecuteAsync(null);
        viewModel.CallEntry = "KC7AVA";
        viewModel.RstEntry = "5NN";
        viewModel.Exchange1Entry = "123";
        await viewModel.EnterSendMessageCommand.ExecuteAsync(null);

        Assert.Equal("KC7AVA 5NN 001", viewModel.LastSent);
        Assert.Equal(0, viewModel.QsoCount);
    }

    [Fact]
    public async Task CommandsDriveTheSessionThroughTheClientBoundary()
    {
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault())
        {
            Activity = 5,
        };

        Assert.True(viewModel.StartCommand.CanExecute(null));
        await viewModel.StartCommand.ExecuteAsync(null);

        Assert.Contains("running", viewModel.Status, StringComparison.OrdinalIgnoreCase);
        Assert.True(viewModel.AdvanceCommand.CanExecute(null));

        await viewModel.AdvanceCommand.ExecuteAsync(null);

        Assert.Equal(4, viewModel.SimulationBlock);
        Assert.Equal("00:00.185", viewModel.Elapsed);

        await viewModel.AdvanceCommand.ExecuteAsync(null);

        Assert.Equal(8, viewModel.SimulationBlock);
        Assert.Equal(1, viewModel.ActiveCallerCount);
        Assert.Equal("Calling", viewModel.CallerState);
        Assert.Contains("WPM", viewModel.CallsignInformation);

        await viewModel.PauseCommand.ExecuteAsync(null);
        Assert.True(viewModel.ResumeCommand.CanExecute(null));

        await viewModel.ResumeCommand.ExecuteAsync(null);
        Assert.True(viewModel.StopCommand.CanExecute(null));

        await viewModel.StopCommand.ExecuteAsync(null);
        Assert.Contains("completed", viewModel.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MonitorLevelCanBeAdjustedThroughTheLiveClientBoundary()
    {
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());
        await viewModel.StartCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsMonitorLevelEnabled);
        Task firstAdjustment = viewModel.SetMonitorLevelAsync(-5d);
        Task latestAdjustment = viewModel.SetMonitorLevelAsync(-60d);
        await Task.WhenAll(firstAdjustment, latestAdjustment);

        Assert.Equal(-60d, viewModel.MonitorLevel);
        Assert.Contains("-60 dB", viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RitUpUsesCeDefaultFiftyHertzStep()
    {
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());
        await viewModel.StartCommand.ExecuteAsync(null);

        await viewModel.RitUpCommand.ExecuteAsync(null);

        Assert.Equal(50, viewModel.RitOffsetHz);
    }

    [Fact]
    public async Task SpeedUpUsesCeDefaultTwoWpmStep()
    {
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault())
        {
            WordsPerMinute = 30,
        };
        await viewModel.StartSingleCommand.ExecuteAsync(null);

        await viewModel.SpeedUpCommand.ExecuteAsync(null);

        Assert.Equal(32, viewModel.WordsPerMinute);
    }

    [Fact]
    public async Task SpeedUpUsesPersistedCustomWpmStep()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"MorseRunnerXPlat-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            await store.SaveAsync(
                new SettingsDocument(
                    SettingsDocument.CurrentSchemaVersion,
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["Settings.WpmStepRate"] = "7",
                    }),
                TestContext.Current.CancellationToken);
            await using var viewModel = new MainWindowViewModel(
                InProcessMorseRunnerClient.CreateDefault(),
                settingsStore: store)
            {
                WordsPerMinute = 30,
            };
            await viewModel.InitializeAsync();
            await viewModel.StartSingleCommand.ExecuteAsync(null);

            await viewModel.SpeedUpCommand.ExecuteAsync(null);

            Assert.Equal(37, viewModel.WordsPerMinute);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SpeedUpClampsPersistedWpmStepToCeLowerBound()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"MorseRunnerXPlat-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            await store.SaveAsync(
                new SettingsDocument(
                    SettingsDocument.CurrentSchemaVersion,
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["Settings.WpmStepRate"] = "0",
                    }),
                TestContext.Current.CancellationToken);
            await using var viewModel = new MainWindowViewModel(
                InProcessMorseRunnerClient.CreateDefault(),
                settingsStore: store)
            {
                WordsPerMinute = 30,
            };
            await viewModel.InitializeAsync();
            await viewModel.StartSingleCommand.ExecuteAsync(null);

            await viewModel.SpeedUpCommand.ExecuteAsync(null);

            Assert.Equal(31, viewModel.WordsPerMinute);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SpeedUpClampsPersistedWpmStepToCeUpperBound()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"MorseRunnerXPlat-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            await store.SaveAsync(
                new SettingsDocument(
                    SettingsDocument.CurrentSchemaVersion,
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["Settings.WpmStepRate"] = "21",
                    }),
                TestContext.Current.CancellationToken);
            await using var viewModel = new MainWindowViewModel(
                InProcessMorseRunnerClient.CreateDefault(),
                settingsStore: store)
            {
                WordsPerMinute = 30,
            };
            await viewModel.InitializeAsync();
            await viewModel.StartSingleCommand.ExecuteAsync(null);

            await viewModel.SpeedUpCommand.ExecuteAsync(null);

            Assert.Equal(50, viewModel.WordsPerMinute);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SpeedDownUsesPersistedCustomWpmStep()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"MorseRunnerXPlat-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            await store.SaveAsync(
                new SettingsDocument(
                    SettingsDocument.CurrentSchemaVersion,
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["Settings.WpmStepRate"] = "7",
                    }),
                TestContext.Current.CancellationToken);
            await using var viewModel = new MainWindowViewModel(
                InProcessMorseRunnerClient.CreateDefault(),
                settingsStore: store)
            {
                WordsPerMinute = 30,
            };
            await viewModel.InitializeAsync();
            await viewModel.StartSingleCommand.ExecuteAsync(null);

            await viewModel.SpeedDownCommand.ExecuteAsync(null);

            Assert.Equal(23, viewModel.WordsPerMinute);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SpeedUpClampsAtCeUpperRange()
    {
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault())
        {
            WordsPerMinute = 118,
        };
        await viewModel.StartSingleCommand.ExecuteAsync(null);

        await viewModel.SpeedUpCommand.ExecuteAsync(null);
        Assert.Equal(120, viewModel.WordsPerMinute);

        await viewModel.SpeedUpCommand.ExecuteAsync(null);
        Assert.Equal(120, viewModel.WordsPerMinute);
    }

    [Fact]
    public async Task SpeedUpRoundsToNextFiveWpmBoundaryInHstMode()
    {
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault())
        {
            WordsPerMinute = 32,
        };
        viewModel.SelectedContest = viewModel.Contests.Single(
            contest => contest.Id.Value == "scHst");
        await viewModel.StartHstCommand.ExecuteAsync(null);

        await viewModel.SpeedUpCommand.ExecuteAsync(null);

        Assert.Equal(35, viewModel.WordsPerMinute);
    }

    [Fact]
    public async Task SpeedDownRoundsToPreviousFiveWpmBoundaryInHstMode()
    {
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault())
        {
            WordsPerMinute = 33,
        };
        viewModel.SelectedContest = viewModel.Contests.Single(
            contest => contest.Id.Value == "scHst");
        await viewModel.StartHstCommand.ExecuteAsync(null);

        await viewModel.SpeedDownCommand.ExecuteAsync(null);

        Assert.Equal(30, viewModel.WordsPerMinute);
    }

    [Fact]
    public async Task OperatorIntentAndNilQsoLoggingUseSemanticClientCommands()
    {
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());
        viewModel.OperatorExchange = "599 #";
        await viewModel.StartCommand.ExecuteAsync(null);
        viewModel.CallEntry = "K1ABC";
        viewModel.RstEntry = "5NN";
        viewModel.Exchange1Entry = "123";
        viewModel.Exchange2Entry = "OR";

        await viewModel.SendExchangeCommand.ExecuteAsync(null);

        Assert.Equal("599 001", viewModel.LastSent);

        await viewModel.CompleteQsoCommand.ExecuteAsync(null);

        Assert.Equal(1, viewModel.QsoCount);
        Assert.Equal(0, viewModel.Score);
        Assert.Equal("NIL", Assert.Single(viewModel.QsoLog).Result);
        Assert.Empty(viewModel.CallEntry);
    }

    [Fact]
    public async Task EnterUsesEngineEsmOutcomeAndRetainsUncertainCall()
    {
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());
        await viewModel.StartCommand.ExecuteAsync(null);
        viewModel.CallEntry = "KC?";
        EntryFocusRequestedEventArgs? focus = null;
        viewModel.EntryFocusRequested += (_, args) => focus = args;

        await viewModel.EnterSendMessageCommand.ExecuteAsync(null);

        Assert.Equal("KC?", viewModel.LastSent);
        Assert.Equal(0, viewModel.QsoCount);
        Assert.Equal("KC?", viewModel.CallEntry);
        Assert.Equal(EntryFocusTarget.Call, focus?.Target);
        Assert.True(focus?.SelectQuestionMark);
    }

    [Fact]
    public async Task EmptyEnterLeavesRstEntryEmpty()
    {
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());
        await viewModel.StartCommand.ExecuteAsync(null);

        await viewModel.EnterSendMessageCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.RstEntry);
    }

    [Fact]
    public async Task NonemptyEnterDefaultsRstEntryTo599ForRstContest()
    {
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());
        await viewModel.StartCommand.ExecuteAsync(null);
        viewModel.CallEntry = "K1ABC";

        await viewModel.EnterSendMessageCommand.ExecuteAsync(null);

        Assert.Equal("599", viewModel.RstEntry);
    }

    [Fact]
    public async Task NonemptyEnterPreservesExplicitRst()
    {
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());
        await viewModel.StartCommand.ExecuteAsync(null);
        viewModel.CallEntry = "K1ABC";
        viewModel.RstEntry = "579";

        await viewModel.EnterSendMessageCommand.ExecuteAsync(null);

        Assert.Equal("579", viewModel.RstEntry);
    }

    [Fact]
    public async Task NonemptyEnterLeavesRstBlankWhenContestDoesNotReceiveRst()
    {
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());
        viewModel.SelectedContest = Assert.Single(
            viewModel.Contests,
            contest => contest.Id == new ContestId("scCwt"));
        await viewModel.StartCommand.ExecuteAsync(null);
        viewModel.CallEntry = "K1ABC";

        await viewModel.EnterSendMessageCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.RstEntry);
    }

    [Fact]
    public async Task EnterClearsFieldsOnlyAfterValidatedCompletion()
    {
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());
        await viewModel.StartCommand.ExecuteAsync(null);
        viewModel.CallEntry = "K1ABC";
        viewModel.Exchange1Entry = "123";

        await viewModel.EnterSendMessageCommand.ExecuteAsync(null);

        Assert.Equal(0, viewModel.QsoCount);
        Assert.Equal("K1ABC 5NN 001", viewModel.LastSent);
        Assert.Equal("K1ABC", viewModel.CallEntry);
        Assert.Equal("599", viewModel.RstEntry);

        await viewModel.EnterSendMessageCommand.ExecuteAsync(null);

        Assert.Equal(1, viewModel.QsoCount);
        Assert.Equal("TU", viewModel.LastSent);
        Assert.Empty(viewModel.CallEntry);
        Assert.Empty(viewModel.RstEntry);
    }

    [Fact]
    public async Task BlankRstIsNotDefaultedUntilAfterRepeatDecision()
    {
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());
        await viewModel.StartCommand.ExecuteAsync(null);
        viewModel.CallEntry = "K1ABC";

        await viewModel.EnterSendMessageCommand.ExecuteAsync(null);

        viewModel.RstEntry = string.Empty;
        viewModel.Exchange1Entry = "123";
        await viewModel.EnterSendMessageCommand.ExecuteAsync(null);

        Assert.Equal("?", viewModel.LastSent);
        Assert.Equal(0, viewModel.QsoCount);
        Assert.Equal("K1ABC", viewModel.CallEntry);
        Assert.Equal("599", viewModel.RstEntry);
        Assert.Equal("123", viewModel.Exchange1Entry);
    }

    [Fact]
    public async Task DurationAcceptsEveryCeMinuteAndClampsToCeRange()
    {
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());

        Assert.Equal(30, viewModel.DurationMinutes);
        viewModel.DurationMinutes = 17;
        Assert.Equal(17, viewModel.DurationMinutes);
        viewModel.DurationMinutes = 0;
        Assert.Equal(1, viewModel.DurationMinutes);
        viewModel.DurationMinutes = 241;
        Assert.Equal(240, viewModel.DurationMinutes);
    }

    [Fact]
    public async Task OperatorSettingsRoundTripThroughTheSettingsStore()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"MorseRunnerXPlat-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            var first = new MainWindowViewModel(
                InProcessMorseRunnerClient.CreateDefault(),
                settingsStore: store);
            await first.InitializeAsync();
            first.StationCall = "K7ABC";
            first.WordsPerMinute = 37;
            first.ReceiveSpeedBelowWpm = 6;
            first.ReceiveSpeedAboveWpm = 2;
            first.SelectedSerialNumberRange = first.SerialNumberRanges[3];
            first.CustomSerialNumberMinimum = 70;
            first.CustomSerialNumberExclusiveMaximum = 80;
            first.CustomSerialNumberMinimumDigits = 3;
            first.CustomSerialNumberMaximumDigits = 4;
            first.HstOperatorName = "Randy";
            first.ShowCallsignInformation = false;
            first.Qsb = true;
            first.SelectedContest = first.Contests[6];
            first.OperatorExchange = "ALICE 123";
            first.DurationMinutes = 17;
            first.CompetitionDurationMinutes = 23;
            await first.DisposeAsync();

            await using var second = new MainWindowViewModel(
                InProcessMorseRunnerClient.CreateDefault(),
                settingsStore: new SettingsStore(path));
            await second.InitializeAsync();

            Assert.Equal("K7ABC", second.StationCall);
            Assert.Equal(37, second.WordsPerMinute);
            Assert.Equal(6, second.ReceiveSpeedBelowWpm);
            Assert.Equal(2, second.ReceiveSpeedAboveWpm);
            Assert.Equal(
                SerialNumberRangeMode.Custom,
                second.SelectedSerialNumberRange.Mode);
            Assert.Equal(70, second.CustomSerialNumberMinimum);
            Assert.Equal(80, second.CustomSerialNumberExclusiveMaximum);
            Assert.Equal(3, second.CustomSerialNumberMinimumDigits);
            Assert.Equal(4, second.CustomSerialNumberMaximumDigits);
            Assert.Equal("RANDY", second.HstOperatorName);
            Assert.False(second.ShowCallsignInformation);
            Assert.True(second.Qsb);
            Assert.Equal(first.Contests[6].Id, second.SelectedContest.Id);
            Assert.Equal("ALICE 123", second.OperatorExchange);
            Assert.Equal(17, second.DurationMinutes);
            Assert.Equal(23, second.CompetitionDurationMinutes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LegacySerialRangeErrorsAreVisibleAfterStartupImport()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"MorseRunnerXPlat-range-errors-{Guid.NewGuid():N}");
        string settingsPath = Path.Combine(root, "settings.json");
        string legacyPath = Path.Combine(root, "MorseRunner.ini");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                legacyPath,
                """
                [Station]
                SerialNrCustomRange=99-1
                """,
                TestContext.Current.CancellationToken);
            await using var viewModel = new MainWindowViewModel(
                InProcessMorseRunnerClient.CreateDefault(),
                settingsStore: new SettingsStore(settingsPath, legacyPath));

            await viewModel.InitializeAsync();

            Assert.Contains(
                "Invalid Keyword Value: 'SerialNrCustomRange=99-1'",
                viewModel.Status,
                StringComparison.Ordinal);
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
    public async Task ResultExportWritesJsonThroughTheViewModel()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"MorseRunnerXPlat-export-{Guid.NewGuid():N}");
        try
        {
            await using var viewModel = new MainWindowViewModel(
                InProcessMorseRunnerClient.CreateDefault(),
                resultsDirectory: directory);
            await viewModel.StartCommand.ExecuteAsync(null);
            await viewModel.StopCommand.ExecuteAsync(null);

            await viewModel.ExportJsonCommand.ExecuteAsync(null);

            string path = Assert.Single(
                Directory.GetFiles(directory, "*.json"));
            Assert.Equal(path, viewModel.LastExportPath);
            Assert.Contains(
                "Exported",
                viewModel.Status,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
