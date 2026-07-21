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
        Assert.NotNull(
            window.FindControl<ComboBox>("SerialNumberRangeSelector"));
        Assert.NotNull(
            window.FindControl<NumericUpDown>(
                "CustomSerialNumberMinimumDigitsInput"));
        Assert.NotNull(
            window.FindControl<NumericUpDown>(
                "CustomSerialNumberMaximumDigitsInput"));
        NumericUpDown? cwSpeedInput =
            window.FindControl<NumericUpDown>("CwSpeedInput");
        Assert.NotNull(cwSpeedInput);
        Assert.Equal(10m, cwSpeedInput.Minimum);
        Assert.Equal(120m, cwSpeedInput.Maximum);
        NumericUpDown? durationInput =
            window.FindControl<NumericUpDown>("DurationInput");
        Assert.NotNull(durationInput);
        Assert.Equal(1m, durationInput.Minimum);
        Assert.Equal(240m, durationInput.Maximum);
        Assert.NotNull(
            window.FindControl<ComboBox>("AudioOutputDeviceSelector"));
        Assert.True(window.Bounds.Width >= window.MinWidth);
        Assert.True(window.Bounds.Height >= window.MinHeight);

        window.Close();
    }

    [Fact]
    public void ImportantViewsProduceVisualEvidence()
    {
        EnsurePlatform();
        string? evidenceRoot =
            Environment.GetEnvironmentVariable(
                "MORSE_RUNNER_VISUAL_EVIDENCE_DIR");
        bool preserveEvidence = !string.IsNullOrWhiteSpace(evidenceRoot);
        string directory = preserveEvidence
            ? Path.GetFullPath(evidenceRoot!)
            : Path.Combine(
                Path.GetTempPath(),
                $"MorseRunnerXPlat-visual-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var mainViewModel = new MainWindowViewModel(
                InProcessMorseRunnerClient.CreateDefault());
            mainViewModel.SelectedSerialNumberRange =
                mainViewModel.SerialNumberRanges.Single(
                    option => option.Mode == SerialNumberRangeMode.Custom);
            var mainWindow = new MainWindow(mainViewModel);
            mainWindow.Show();
            string mainPath = Path.Combine(
                directory,
                "avalonia-main-window.png");
            Avalonia.Media.Imaging.WriteableBitmap? mainFrame =
                mainWindow.CaptureRenderedFrame();
            Assert.NotNull(mainFrame);
            mainFrame.Save(mainPath);
            mainWindow.Close();

            var scoreWindow = new ScoreWindow(
                new ScoreWindowViewModel(
                    12_345,
                    123,
                    246,
                    54_321,
                    "CQ WPX",
                    "05:00.000"));
            scoreWindow.Show();
            string scorePath = Path.Combine(
                directory,
                "avalonia-score-window.png");
            Avalonia.Media.Imaging.WriteableBitmap? scoreFrame =
                scoreWindow.CaptureRenderedFrame();
            Assert.NotNull(scoreFrame);
            scoreFrame.Save(scorePath);
            scoreWindow.Close();

            Assert.True(new FileInfo(mainPath).Length > 0);
            Assert.True(new FileInfo(scorePath).Length > 0);
        }
        finally
        {
            if (!preserveEvidence && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void EnsurePlatform()
    {
        lock (PlatformGate)
        {
            if (_platformReady)
            {
                return;
            }

            AppBuilder.Configure<MorseRunner.App.App>()
                .UseSkia()
                .UseHeadless(
                    new AvaloniaHeadlessPlatformOptions
                    {
                        UseHeadlessDrawing = false,
                    })
                .SetupWithoutStarting();
            _platformReady = true;
        }
    }

}
