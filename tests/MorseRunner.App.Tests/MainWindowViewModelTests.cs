using MorseRunner.App.ViewModels;
using MorseRunner.Client;

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
}
