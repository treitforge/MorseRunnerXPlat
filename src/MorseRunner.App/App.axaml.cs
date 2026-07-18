using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MorseRunner.App.ViewModels;
using MorseRunner.App.Views;
using MorseRunner.Client;

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
            InProcessMorseRunnerClient client =
                InProcessMorseRunnerClient.CreateWithPhysicalAudio();
            desktop.MainWindow = new MainWindow(
                new MainWindowViewModel(client));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
