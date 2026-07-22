using MorseRunner.App.ViewModels;
using MorseRunner.Client;
using MorseRunner.Domain;

namespace MorseRunner.Tui.Tests;

public sealed class CrossUxWorkflowTests
{
    [Fact]
    public async Task AvaloniaAndTuiUseTheSameEnterSendMessageFlow()
    {
        await using var avalonia = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());
        await avalonia.StartCommand.ExecuteAsync(null);
        avalonia.CallEntry = "K1ABC";
        avalonia.Exchange1Entry = "123";

        await using InProcessMorseRunnerClient tuiClient =
            InProcessMorseRunnerClient.CreateDefault();
        using var tui = new TuiApplication(tuiClient, isHosted: false);
        await tui.HandleAsync(
            new(TuiActionKind.StartPileup),
            CancellationToken.None);
        tui.State.Call = "K1ABC";
        tui.State.Exchange1 = "123";

        await avalonia.EnterSendMessageCommand.ExecuteAsync(null);
        await tui.HandleAsync(
            new(TuiActionKind.EnterSendMessage),
            CancellationToken.None);

        Assert.Equal("K1ABC 5NN 001", avalonia.LastSent);
        Assert.Equal(
            avalonia.LastSent,
            tui.State.Snapshot?.LastOperatorMessage);
        Assert.Equal(0, avalonia.QsoCount);
        Assert.Equal(0, tui.State.Snapshot?.QsoCount);

        await avalonia.EnterSendMessageCommand.ExecuteAsync(null);
        await tui.HandleAsync(
            new(TuiActionKind.EnterSendMessage),
            CancellationToken.None);

        Assert.Equal("TU", avalonia.LastSent);
        Assert.Equal(
            avalonia.LastSent,
            tui.State.Snapshot?.LastOperatorMessage);
        Assert.Equal(1, avalonia.QsoCount);
        Assert.Equal(1, tui.State.Snapshot?.QsoCount);
        Assert.Empty(avalonia.CallEntry);
        Assert.Empty(tui.State.Call);
    }

    [Fact]
    public async Task AvaloniaAndTuiProduceTheSameLoggedQsoOutcome()
    {
        await using var avalonia = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());
        await avalonia.StartCommand.ExecuteAsync(null);
        avalonia.CallEntry = "K1ABC";
        avalonia.RstEntry = "599";
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

        tui.State.Rst = "599";
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

    [Fact]
    public async Task CqWpxNilRowsDoNotScoreOrCreateDuplicatesAcrossBothUxSurfaces()
    {
        await using var avalonia = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());
        await avalonia.StartCommand.ExecuteAsync(null);
        await using InProcessMorseRunnerClient tuiClient =
            InProcessMorseRunnerClient.CreateDefault();
        using var tui = new TuiApplication(tuiClient, isHosted: false);
        await tui.HandleAsync(
            new(TuiActionKind.StartPileup),
            CancellationToken.None);

        string[] calls = ["K1ABC", "K2XYZ", "K1ABC"];
        for (int index = 0; index < calls.Length; index++)
        {
            avalonia.CallEntry = calls[index];
            avalonia.RstEntry = "599";
            avalonia.Exchange1Entry = (index + 1).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            await avalonia.CompleteQsoCommand.ExecuteAsync(null);

            tui.State.Call = calls[index];
            tui.State.Rst = "599";
            tui.State.Exchange1 = (index + 1).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            await tui.HandleAsync(
                new(TuiActionKind.LogQso),
                CancellationToken.None);
        }

        Assert.NotNull(tui.State.Snapshot);
        Assert.Equal(0, avalonia.Score);
        Assert.Equal(avalonia.Score, tui.State.Snapshot.Score);
        Assert.Equal(3, avalonia.QsoCount);
        Assert.Equal(3, tui.State.Qsos.Count);
        Assert.All(avalonia.QsoLog, qso => Assert.False(qso.IsDuplicate));
        Assert.All(tui.State.Qsos, qso => Assert.False(qso.IsDuplicate));
        Assert.All(avalonia.QsoLog, qso => Assert.Equal("NIL", qso.Result));
        Assert.All(tui.State.Qsos, qso => Assert.Equal("NIL", qso.ErrorText));
    }

    [Fact]
    public async Task CompletedResultAndRateMatchAcrossBothUxSurfaces()
    {
        await using var avalonia = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());
        await avalonia.StartCommand.ExecuteAsync(null);
        avalonia.CallEntry = "K1ABC";
        avalonia.RstEntry = "599";
        avalonia.Exchange1Entry = "123";
        await avalonia.CompleteQsoCommand.ExecuteAsync(null);
        await avalonia.StopCommand.ExecuteAsync(null);

        await using InProcessMorseRunnerClient tuiClient =
            InProcessMorseRunnerClient.CreateDefault();
        using var tui = new TuiApplication(tuiClient, isHosted: false);
        await tui.HandleAsync(
            new(TuiActionKind.StartPileup),
            CancellationToken.None);
        tui.State.Call = "K1ABC";
        tui.State.Rst = "599";
        tui.State.Exchange1 = "123";
        await tui.HandleAsync(
            new(TuiActionKind.LogQso),
            CancellationToken.None);
        await tui.HandleAsync(
            new(TuiActionKind.Stop),
            CancellationToken.None);

        Assert.NotNull(tui.State.Result);
        Assert.Equal(avalonia.Score, tui.State.Result.Score);
        Assert.Equal(avalonia.QsoCount, tui.State.Result.QsoCount);
        Assert.Equal(avalonia.QsoRatePerHour, tui.State.Result.QsoRatePerHour);
    }

    [Fact]
    public async Task TuiAdvancedSettingsReachTheAuthoritativeEngine()
    {
        await using InProcessMorseRunnerClient client =
            InProcessMorseRunnerClient.CreateDefault();
        using var tui = new TuiApplication(client, isHosted: false);
        tui.State.WordsPerMinute = 60;
        tui.State.ReceiveSpeedBelowWpm = 0;
        tui.State.ReceiveSpeedAboveWpm = 0;
        tui.State.Activity = 9;

        await tui.HandleAsync(
            new(TuiActionKind.StartPileup),
            CancellationToken.None);

        Assert.NotNull(tui.State.Snapshot);
        SessionId sessionId = tui.State.Snapshot.SessionId;
        CommandResult advance = await client.ExecuteAsync(
            new AdvanceSimulationCommand(
                RequestId.New(),
                sessionId,
                new("tui"),
                BlockCount: 20),
            TestContext.Current.CancellationToken);
        Assert.True(advance.Accepted, advance.Message);
        SessionSnapshot snapshot = await client.GetSnapshotAsync(
            sessionId,
            TestContext.Current.CancellationToken);
        IReadOnlyList<ActiveStationSnapshot> stations =
            Assert.IsAssignableFrom<IReadOnlyList<ActiveStationSnapshot>>(
                snapshot.ActiveStations);
        Assert.NotEmpty(stations);
        Assert.All(
            stations,
            station => Assert.Equal(60, station.WordsPerMinute));
    }
}
