using MorseRunner.App.ViewModels;
using MorseRunner.Client;
using MorseRunner.Infrastructure;

namespace MorseRunner.App.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task CommandsDriveTheSessionThroughTheClientBoundary()
    {
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());

        Assert.True(viewModel.StartCommand.CanExecute(null));
        await viewModel.StartCommand.ExecuteAsync(null);

        Assert.Contains("running", viewModel.Status, StringComparison.OrdinalIgnoreCase);
        Assert.True(viewModel.AdvanceCommand.CanExecute(null));

        await viewModel.AdvanceCommand.ExecuteAsync(null);

        Assert.Equal(4, viewModel.SimulationBlock);
        Assert.Equal("00:00.185", viewModel.Elapsed);

        await viewModel.AdvanceCommand.ExecuteAsync(null);

        Assert.Equal("Calling", viewModel.CallerState);

        await viewModel.PauseCommand.ExecuteAsync(null);
        Assert.True(viewModel.ResumeCommand.CanExecute(null));

        await viewModel.ResumeCommand.ExecuteAsync(null);
        Assert.True(viewModel.StopCommand.CanExecute(null));

        await viewModel.StopCommand.ExecuteAsync(null);
        Assert.Contains("completed", viewModel.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OperatorIntentAndQsoLoggingUseSemanticClientCommands()
    {
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());
        await viewModel.StartCommand.ExecuteAsync(null);
        viewModel.CallEntry = "K1ABC";
        viewModel.Exchange1Entry = "123";
        viewModel.Exchange2Entry = "OR";

        await viewModel.SendExchangeCommand.ExecuteAsync(null);

        Assert.Equal("5NN 123 OR", viewModel.LastSent);

        await viewModel.CompleteQsoCommand.ExecuteAsync(null);

        Assert.Equal(1, viewModel.QsoCount);
        Assert.Equal(1, viewModel.Score);
        Assert.Empty(viewModel.CallEntry);
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
            first.Qsb = true;
            first.SelectedContest = first.Contests[6];
            first.SelectedDuration = first.Durations[4];
            await first.DisposeAsync();

            await using var second = new MainWindowViewModel(
                InProcessMorseRunnerClient.CreateDefault(),
                settingsStore: new SettingsStore(path));
            await second.InitializeAsync();

            Assert.Equal("K7ABC", second.StationCall);
            Assert.Equal(37, second.WordsPerMinute);
            Assert.True(second.Qsb);
            Assert.Equal(first.Contests[6].Id, second.SelectedContest.Id);
            Assert.Equal(30, second.SelectedDuration.Minutes);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
