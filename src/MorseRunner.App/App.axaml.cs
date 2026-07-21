using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MorseRunner.App.ViewModels;
using MorseRunner.App.Views;
using MorseRunner.Client;
using MorseRunner.Infrastructure;

namespace MorseRunner.App;

public sealed class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var paths = new ApplicationPaths();
            paths.EnsureWritableDirectories();
            var recording = new RecordingPreference(paths);
            var settings = new SettingsStore(
                Path.Combine(paths.Settings, "settings.json"),
                paths.LegacySettingsImport);
            var highScores = new HighScoreStore(
                Path.Combine(paths.Results, "high-scores.json"));
            InProcessMorseRunnerClient client =
                InProcessMorseRunnerClient.CreateWithPhysicalAudio(
                    recordingPathProvider: recording.CreatePath);
            desktop.MainWindow = new MainWindow(
                new MainWindowViewModel(
                    client,
                    recordingPreference: recording,
                    settingsStore: settings,
                    highScoreStore: highScores,
                    resultsDirectory: paths.Results));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
