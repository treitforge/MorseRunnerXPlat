using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using MorseRunner.App.ViewModels;
using MorseRunner.App.Views;
using MorseRunner.Client;
using MorseRunner.Domain;

namespace MorseRunner.App.Tests;

public sealed class HeadlessUiTests
{
    private static readonly object PlatformGate = new();
    private static bool _platformReady;

    [Fact]
    public void MainWindowOpensAndFocusesTheCallEntry()
    {
        EnsurePlatform();
        var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());
        var window = new MainWindow(viewModel);

        window.Show();

        Assert.True(window.IsVisible);
        TextBox? callEntry = window.FindControl<TextBox>("CallEntryBox");
        Assert.NotNull(callEntry);
        Assert.True(callEntry.IsFocused);
        callEntry.Text = "KC?";
        MethodInfo focusEntry = typeof(MainWindow).GetMethod(
            "FocusEntry",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        _ = focusEntry.Invoke(
            window,
            [null, new EntryFocusRequestedEventArgs(EntryFocusTarget.Call, true)]);
        Assert.Equal(2, callEntry.SelectionStart);
        Assert.Equal(3, callEntry.SelectionEnd);

        window.Close();
    }

    private static void EnsurePlatform()
    {
        lock (PlatformGate)
        {
            if (_platformReady)
            {
                return;
            }

            AppBuilder.Configure<HeadlessTestApplication>()
                .UseHeadless(
                    new AvaloniaHeadlessPlatformOptions
                    {
                        UseHeadlessDrawing = true,
                    })
                .SetupWithoutStarting();
            _platformReady = true;
        }
    }

    private sealed class HeadlessTestApplication : Application;
}
