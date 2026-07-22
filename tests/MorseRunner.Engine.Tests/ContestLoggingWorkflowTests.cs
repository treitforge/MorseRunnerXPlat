using MorseRunner.Audio;
using MorseRunner.Domain;

namespace MorseRunner.Engine.Tests;

public sealed class ContestLoggingWorkflowTests
{
    public static TheoryData<string, string, string, string, string> Contacts =>
        new()
        {
            { "scWpx", "K1ABC", "599", "1", "" },
            { "scCwt", "K1ABC", "", "DAVID", "123" },
            { "scFieldDay", "K1ABC", "", "3A", "OR" },
            { "scNaQp", "W1ABC", "", "ALEX", "MA" },
            { "scHst", "E", "599", "1", "" },
            { "scCQWW", "DL1ABC", "599", "", "14" },
            { "scArrlDx", "DL1ABC", "599", "", "100" },
            { "scSst", "W1ABC", "", "BRUCE", "MA" },
            { "scAllJa", "JA1ABC", "599", "", "10H" },
            { "scAcag", "JA1ABC", "599", "", "1002H" },
            { "scIaruHf", "W1ABC", "599", "", "6" },
            { "scArrlSS", "K1ABC", "", "1 A", "72 OR" },
        };

    [Theory]
    [MemberData(nameof(Contacts))]
    public async Task ValidDirectAttemptsRemainNilUntilLiveTruthExists(
        string contest,
        string call,
        string rst,
        string exchange1,
        string exchange2)
    {
        await using var engine =
            new MorseRunnerEngine(_ => new NullAudioSink());
        SessionSettings settings = SessionSettings.CreateDefault(12_345) with
        {
            ContestId = new(contest),
        };
        SessionHandle handle = await engine.CreateSessionAsync(
            settings,
            TestContext.Current.CancellationToken);
        ClientId client = new("logging-workflow");
        await engine.ExecuteAsync(
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                client),
            TestContext.Current.CancellationToken);

        CommandResult invalid = await engine.ExecuteAsync(
            new LogQsoCommand(
                RequestId.New(),
                handle.SessionId,
                client,
                contest == "scHst" ? call : "AB",
                rst,
                contest == "scHst" ? string.Empty : exchange1,
                exchange2),
            TestContext.Current.CancellationToken);

        Assert.False(invalid.Accepted);
        Assert.Empty(engine.GetCompletedQsos(handle.SessionId));
        Assert.Equal(0, engine.GetSnapshot(handle.SessionId).Score);

        CommandResult corrected = await LogAsync(
            engine,
            handle.SessionId,
            client,
            call,
            rst,
            exchange1,
            exchange2);
        CommandResult repeated = await LogAsync(
            engine,
            handle.SessionId,
            client,
            call,
            rst,
            exchange1,
            exchange2);

        Assert.True(corrected.Accepted, corrected.Message);
        Assert.True(repeated.Accepted, repeated.Message);
        IReadOnlyList<Qso> qsos =
            engine.GetCompletedQsos(handle.SessionId);
        Assert.Equal(2, qsos.Count);
        Assert.All(qsos, qso => Assert.Equal(LogError.Nil, qso.ExchangeError));
        Assert.All(qsos, qso => Assert.False(qso.IsDuplicate));
        Assert.All(qsos, qso => Assert.Empty(qso.TrueCall));
        Assert.All(qsos, qso => Assert.Equal("NIL", qso.ErrorText));
        SessionSnapshot snapshot = engine.GetSnapshot(handle.SessionId);
        SessionResult result = engine.GetResult(handle.SessionId);
        Assert.Equal(qsos.Count, result.QsoCount);
        Assert.Equal(0, snapshot.Score);
        Assert.Equal(snapshot.Score, result.Score);
    }

    [Fact]
    public async Task SnapshotAndResultExposeRateFromCompletedQsos()
    {
        await using var engine =
            new MorseRunnerEngine(_ => new NullAudioSink());
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(12_345),
            TestContext.Current.CancellationToken);
        ClientId client = new("rate-result");
        await engine.ExecuteAsync(
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                client),
            TestContext.Current.CancellationToken);
        await engine.ExecuteAsync(
            new AdvanceSimulationCommand(
                RequestId.New(),
                handle.SessionId,
                client,
                1),
            TestContext.Current.CancellationToken);
        await LogAsync(
            engine,
            handle.SessionId,
            client,
            "K1ABC",
            "599",
            "1",
            "");
        await engine.ExecuteAsync(
            new AdvanceSimulationCommand(
                RequestId.New(),
                handle.SessionId,
                client,
                1_291),
            TestContext.Current.CancellationToken);

        SessionSnapshot snapshot = engine.GetSnapshot(handle.SessionId);
        SessionResult result = engine.GetResult(handle.SessionId);

        Assert.Equal(60, snapshot.QsoRatePerHour);
        Assert.Equal(snapshot.QsoRatePerHour, result.QsoRatePerHour);
        Assert.Equal(snapshot.QsoCount, result.QsoCount);
        Assert.Equal(snapshot.Score, result.Score);
    }

    private static Task<CommandResult> LogAsync(
        MorseRunnerEngine engine,
        SessionId sessionId,
        ClientId client,
        string call,
        string rst,
        string exchange1,
        string exchange2) =>
        engine.ExecuteAsync(
            new LogQsoCommand(
                RequestId.New(),
                sessionId,
                client,
                call,
                rst,
                exchange1,
                exchange2),
            TestContext.Current.CancellationToken);
}
