using MorseRunner.App.ViewModels;
using MorseRunner.Client;

namespace MorseRunner.Tui.Tests;

public sealed class CrossUxWorkflowTests
{
    [Fact]
    public async Task AvaloniaAndTuiProduceTheSameLoggedQsoOutcome()
    {
        await using var avalonia = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());
        await avalonia.StartCommand.ExecuteAsync(null);
        avalonia.CallEntry = "K1ABC";
        avalonia.Exchange1Entry = "123";
        avalonia.Exchange2Entry = "OR";
        await avalonia.CompleteQsoCommand.ExecuteAsync(null);

        await using InProcessMorseRunnerClient tuiClient =
            InProcessMorseRunnerClient.CreateDefault();
        using var tui = new TuiApplication(tuiClient, isHosted: false);
        await tui.HandleAsync(
            new(TuiActionKind.StartPileup),
            CancellationToken.None);
        foreach (char character in "K1ABC")
        {
            await tui.HandleAsync(
                new(TuiActionKind.InsertCharacter, character),
                CancellationToken.None);
        }

        tui.State.Exchange1 = "123";
        tui.State.Exchange2 = "OR";
        await tui.HandleAsync(
            new(TuiActionKind.LogQso),
            CancellationToken.None);

        Assert.NotNull(tui.State.Snapshot);
        Assert.Equal(avalonia.QsoCount, tui.State.Snapshot.QsoCount);
        Assert.Equal(avalonia.Score, tui.State.Snapshot.Score);
        Assert.Single(avalonia.QsoLog);
        Assert.Single(tui.State.Qsos);
        Assert.Equal(avalonia.QsoLog[0].Call, tui.State.Qsos[0].Call);
        Assert.Equal(
            avalonia.QsoLog[0].Exchange,
            $"{tui.State.Qsos[0].Exchange1} {tui.State.Qsos[0].Exchange2}");
    }

    [Fact]
    public async Task BothUxSurfacesExposeEveryContestAndRunMode()
    {
        await using var avalonia = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());
        await using InProcessMorseRunnerClient tuiClient =
            InProcessMorseRunnerClient.CreateDefault();
        using var tui = new TuiApplication(tuiClient, isHosted: false);

        for (int index = 1; index < avalonia.Contests.Count; index++)
        {
            await tui.HandleAsync(
                new(TuiActionKind.NextContest),
                CancellationToken.None);
        }

        Assert.Equal(12, avalonia.Contests.Count);
        Assert.Equal(4, avalonia.RunModes.Count);
        Assert.Equal(
            avalonia.Contests[^1].Id,
            tui.State.Contest.Id);
        Assert.Equal(4, TuiState.RunModes.Count);
    }
}
