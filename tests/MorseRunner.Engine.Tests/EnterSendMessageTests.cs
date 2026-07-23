using MorseRunner.Domain;

namespace MorseRunner.Engine.Tests;

public sealed class EnterSendMessageTests
{
    private static readonly ClientId TestClient = new("esm-tests");

    [Fact]
    public async Task EmptyCallSendsCq()
    {
        await using var session = await StartedSession.CreateAsync();

        CommandResult result = await session.EnterAsync("", "5NN", "", "");

        Assert.True(result.Accepted);
        Assert.Equal(
            EnterSendMessageOutcome.SendCq,
            result.EnterSendMessage?.Outcome);
        Assert.Equal(
            ["CQ W7SST TEST"],
            result.EnterSendMessage?.SentMessages);
        Assert.Equal(
            "CQ W7SST TEST",
            session.Snapshot.LastOperatorMessage);
        Assert.Equal(0, session.Snapshot.QsoCount);
    }

    [Fact]
    public async Task ExplicitCqUsesConfiguredCallForCqWpx()
    {
        await using var session = await StartedSession.CreateAsync();

        CommandResult result = await session.SendAsync(OperatorIntent.Cq);

        Assert.True(result.Accepted);
        Assert.Equal(
            "CQ W7SST TEST",
            session.Snapshot.LastOperatorMessage);
    }

    [Theory]
    [InlineData("scWpx", "CQ W7SST TEST")]
    [InlineData("scCwt", "CQ CWT W7SST")]
    [InlineData("scFieldDay", "CQ FD W7SST")]
    [InlineData("scNaQp", "CQ W7SST TEST")]
    [InlineData("scHst", "CQ W7SST TEST")]
    [InlineData("scCQWW", "CQ W7SST TEST")]
    [InlineData("scArrlDx", "CQ W7SST TEST")]
    [InlineData("scSst", "CQ SST W7SST")]
    [InlineData("scAllJa", "CQ W7SST TEST")]
    [InlineData("scAcag", "CQ W7SST TEST")]
    [InlineData("scIaruHf", "CQ W7SST TEST")]
    [InlineData("scArrlSS", "CQ SS W7SST")]
    public async Task ExplicitCqUsesCeContestText(
        string contestId,
        string expected)
    {
        await using var session = await StartedSession.CreateAsync(
            new(contestId));

        CommandResult result = await session.SendAsync(OperatorIntent.Cq);

        Assert.True(result.Accepted);
        Assert.Equal(expected, session.Snapshot.LastOperatorMessage);
    }

    [Fact]
    public async Task ShortCqWpxCallSendsOnlyCallAndFocusesSerialExchange()
    {
        await using var session = await StartedSession.CreateAsync();

        CommandResult result = await session.EnterAsync("KC", "5NN", "", "");

        Assert.True(result.Accepted);
        Assert.Equal(
            EnterSendMessageOutcome.SendEnteredCall,
            result.EnterSendMessage?.Outcome);
        Assert.Equal(["KC"], result.EnterSendMessage?.SentMessages);
        Assert.Equal(
            EntryFocusTarget.Exchange1,
            result.EnterSendMessage?.FocusTarget);
        Assert.False(result.EnterSendMessage?.SelectQuestionMark);
        Assert.False(result.EnterSendMessage?.ClearEntry);
        Assert.Equal("KC", session.Snapshot.LastOperatorMessage);
        Assert.Equal(0, session.Snapshot.QsoCount);
    }

    [Fact]
    public async Task ShortCallOutsideCertifiedWpxRouteRetainsCallFocus()
    {
        await using var session = await StartedSession.CreateAsync(
            new("scCwt"));

        CommandResult result = await session.EnterAsync("KC", "", "", "");

        Assert.True(result.Accepted);
        Assert.Equal(
            EnterSendMessageOutcome.SendEnteredCall,
            result.EnterSendMessage?.Outcome);
        Assert.Equal(["KC"], result.EnterSendMessage?.SentMessages);
        Assert.Equal(
            EntryFocusTarget.Call,
            result.EnterSendMessage?.FocusTarget);
        Assert.False(result.EnterSendMessage?.SelectQuestionMark);
    }

    [Theory]
    [InlineData("KC?")]
    [InlineData("KC7?")]
    public async Task UncertainCallSendsOnlyEnteredCallAndSelectsQuestionMark(
        string call)
    {
        await using var session = await StartedSession.CreateAsync();

        CommandResult result = await session.EnterAsync(call, "5NN", "", "");

        Assert.True(result.Accepted);
        Assert.Equal(
            EnterSendMessageOutcome.SendEnteredCall,
            result.EnterSendMessage?.Outcome);
        Assert.Equal([call], result.EnterSendMessage?.SentMessages);
        Assert.Equal(EntryFocusTarget.Call, result.EnterSendMessage?.FocusTarget);
        Assert.True(result.EnterSendMessage?.SelectQuestionMark);
        Assert.False(result.EnterSendMessage?.ClearEntry);
        Assert.Equal(call, session.Snapshot.LastOperatorMessage);
        Assert.Equal(0, session.Snapshot.QsoCount);
    }

    [Fact]
    public async Task CompleteCallFirstSendsCallAndOwnExchange()
    {
        await using var session = await StartedSession.CreateAsync();

        CommandResult result = await session.EnterAsync("KC7AVA", "5NN", "", "");

        Assert.True(result.Accepted);
        Assert.Equal(
            EnterSendMessageOutcome.SendCallAndExchange,
            result.EnterSendMessage?.Outcome);
        Assert.Equal(
            ["KC7AVA", "5NN 001"],
            result.EnterSendMessage?.SentMessages);
        Assert.Equal("KC7AVA 5NN 001", session.Snapshot.LastOperatorMessage);
        Assert.Equal(0, session.Snapshot.QsoCount);
    }

    [Fact]
    public async Task LowercaseCallIsNormalizedBeforeTransmission()
    {
        await using var session = await StartedSession.CreateAsync();

        CommandResult result = await session.EnterAsync("kc7ava", "5NN", "", "");

        Assert.True(result.Accepted);
        Assert.Equal(
            ["KC7AVA", "5NN 001"],
            result.EnterSendMessage?.SentMessages);
        Assert.Equal("KC7AVA 5NN 001", session.Snapshot.LastOperatorMessage);
    }

    [Fact]
    public async Task WipeResetsEsmStateWithoutSendingAnAbortMessage()
    {
        await using var session = await StartedSession.CreateAsync();
        _ = await session.SendAsync(OperatorIntent.HisCall);
        _ = await session.SendAsync(OperatorIntent.Exchange);

        CommandResult wiped = await session.Engine.ExecuteAsync(
            new ResetOperatorEntryCommand(
                RequestId.New(),
                session.SessionId,
                TestClient),
            TestContext.Current.CancellationToken);

        Assert.True(wiped.Accepted);
        Assert.Equal("5NN 001", session.Snapshot.LastOperatorMessage);

        CommandResult result = await session.EnterAsync("KC7AVA", "5NN", "123", "");

        Assert.True(result.Accepted);
        Assert.Equal(
            EnterSendMessageOutcome.SendCallAndExchange,
            result.EnterSendMessage?.Outcome);
        Assert.Equal(
            ["KC7AVA", "5NN 001"],
            result.EnterSendMessage?.SentMessages);
        Assert.Equal(0, session.Snapshot.QsoCount);
    }

    [Fact]
    public async Task AbortResetsEsmStateBeforeTheNextEnterWorkflow()
    {
        await using var session = await StartedSession.CreateAsync();
        _ = await session.SendAsync(OperatorIntent.HisCall);
        _ = await session.SendAsync(OperatorIntent.Exchange);

        CommandResult aborted = await session.SendAsync(OperatorIntent.Abort);

        Assert.True(aborted.Accepted);
        Assert.Empty(session.Snapshot.LastOperatorMessage ?? string.Empty);

        CommandResult result = await session.EnterAsync("KC7AVA", "5NN", "123", "");

        Assert.True(result.Accepted);
        Assert.Equal(
            EnterSendMessageOutcome.SendCallAndExchange,
            result.EnterSendMessage?.Outcome);
        Assert.Equal(
            ["KC7AVA", "5NN 001"],
            result.EnterSendMessage?.SentMessages);
        Assert.Equal(0, session.Snapshot.QsoCount);
    }

    [Fact]
    public async Task CqWpxCallWithBlankRstStillFocusesSerialExchange()
    {
        await using var session = await StartedSession.CreateAsync();

        CommandResult result = await session.EnterAsync(
            "KC7AVA",
            "",
            "",
            "");

        Assert.True(result.Accepted);
        Assert.Equal(
            EnterSendMessageOutcome.SendCallAndExchange,
            result.EnterSendMessage?.Outcome);
        Assert.Equal(
            EntryFocusTarget.Exchange1,
            result.EnterSendMessage?.FocusTarget);
    }

    [Fact]
    public async Task MissingReceivedExchangeAfterOwnExchangeRequestsRepeat()
    {
        await using var session = await StartedSession.CreateAsync();
        _ = await session.EnterAsync("KC7AVA", "5NN", "", "");

        CommandResult result = await session.EnterAsync("KC7AVA", "5NN", "", "");

        Assert.True(result.Accepted);
        Assert.Equal(
            EnterSendMessageOutcome.RequestExchangeRepeat,
            result.EnterSendMessage?.Outcome);
        Assert.Equal(["?"], result.EnterSendMessage?.SentMessages);
        Assert.Equal("?", session.Snapshot.LastOperatorMessage);
        Assert.Equal(0, session.Snapshot.QsoCount);
    }

    [Fact]
    public async Task InvalidCompleteExchangeDoesNotSendTuOrLog()
    {
        await using var session = await StartedSession.CreateAsync();
        _ = await session.EnterAsync("KC7AVA", "5N", "123", "");

        CommandResult result = await session.EnterAsync(
            "KC7AVA",
            "5N",
            "123",
            "");

        Assert.False(result.Accepted);
        Assert.Equal(
            EnterSendMessageOutcome.RejectEntry,
            result.EnterSendMessage?.Outcome);
        Assert.Empty(result.EnterSendMessage?.SentMessages ?? []);
        Assert.DoesNotContain(
            "TU",
            session.Snapshot.LastOperatorMessage ?? "",
            StringComparison.Ordinal);
        Assert.Equal(0, session.Snapshot.QsoCount);
    }

    [Fact]
    public async Task ValidCompleteExchangeAtomicallySendsTuAndLogsOnce()
    {
        await using var session = await StartedSession.CreateAsync();
        _ = await session.EnterAsync("KC7AVA", "5NN", "123", "");

        CommandResult result = await session.EnterAsync(
            "KC7AVA",
            "5NN",
            "123",
            "");

        Assert.True(result.Accepted);
        Assert.Equal(
            EnterSendMessageOutcome.CompleteAndLogQso,
            result.EnterSendMessage?.Outcome);
        Assert.Equal(["TU"], result.EnterSendMessage?.SentMessages);
        Assert.True(result.EnterSendMessage?.ClearEntry);
        Assert.Equal("TU", session.Snapshot.LastOperatorMessage);
        Assert.Equal(1, session.Snapshot.QsoCount);
        Assert.Single(session.Engine.GetCompletedQsos(session.SessionId));
    }

    [Fact]
    public async Task RepeatedEnterDuringUncertainCallRepeatsOnlyTheCall()
    {
        await using var session = await StartedSession.CreateAsync();
        _ = await session.EnterAsync("KC?", "5NN", "", "");

        CommandResult repeated = await session.EnterAsync(
            "KC?",
            "5NN",
            "",
            "");

        Assert.Equal(
            EnterSendMessageOutcome.SendEnteredCall,
            repeated.EnterSendMessage?.Outcome);
        Assert.Equal(["KC?"], repeated.EnterSendMessage?.SentMessages);
        Assert.Equal(0, session.Snapshot.QsoCount);
    }

    [Fact]
    public async Task ChangedCallIsSentBeforeRequestingMissingExchange()
    {
        await using var session = await StartedSession.CreateAsync();
        _ = await session.EnterAsync("K1ABC", "5NN", "", "");

        CommandResult corrected = await session.EnterAsync(
            "K2XYZ",
            "5NN",
            "",
            "");

        Assert.Equal(
            EnterSendMessageOutcome.RequestExchangeRepeat,
            corrected.EnterSendMessage?.Outcome);
        Assert.Equal(["K2XYZ", "?"], corrected.EnterSendMessage?.SentMessages);
        Assert.Equal("K2XYZ ?", session.Snapshot.LastOperatorMessage);
        Assert.Equal(0, session.Snapshot.QsoCount);
    }

    [Fact]
    public async Task EnterAfterCompletedQsoStartsNextInteractionWithCq()
    {
        await using var session = await StartedSession.CreateAsync();
        _ = await session.EnterAsync("K1ABC", "5NN", "123", "");
        _ = await session.EnterAsync("K1ABC", "5NN", "123", "");

        CommandResult next = await session.EnterAsync("", "5NN", "", "");

        Assert.Equal(
            EnterSendMessageOutcome.SendCq,
            next.EnterSendMessage?.Outcome);
        Assert.Equal(
            "CQ W7SST TEST",
            session.Snapshot.LastOperatorMessage);
        Assert.Equal(1, session.Snapshot.QsoCount);
    }

    [Fact]
    public async Task ExplicitF3AndF7IntentsRemainDistinctFromEsm()
    {
        await using var session = await StartedSession.CreateAsync();

        _ = await session.SendAsync(OperatorIntent.ThankYou);

        Assert.Equal("TU", session.Snapshot.LastOperatorMessage);
        Assert.Equal(0, session.Snapshot.QsoCount);

        _ = await session.SendAsync(OperatorIntent.Question);

        Assert.Equal("?", session.Snapshot.LastOperatorMessage);
        Assert.Equal(0, session.Snapshot.QsoCount);
    }

    [Fact]
    public async Task StationIdRateAddsCallAtThresholdAndResetsAfterTuFinishes()
    {
        await using var session = await StartedSession.CreateAsync(
            new("scWpx"),
            new("rmPileup"),
            stationIdRate: 3);

        _ = await session.SendAsync(OperatorIntent.ThankYou);
        Assert.Equal("TU", session.Snapshot.LastOperatorMessage);
        _ = await session.SendAsync(OperatorIntent.Abort);

        _ = await session.LogAsync("K1ABC", "5NN", "123", "");
        _ = await session.LogAsync("K1ABC", "5NN", "123", "");
        _ = await session.SendAsync(OperatorIntent.ThankYou);
        Assert.Equal("TU W7SST", session.Snapshot.LastOperatorMessage);

        _ = await session.LogAsync("K1ABC", "5NN", "123", "");
        _ = await session.AdvanceAsync(128);
        _ = await session.SendAsync(OperatorIntent.ThankYou);

        Assert.Equal("TU", session.Snapshot.LastOperatorMessage);
    }

    [Theory]
    [InlineData("scWpx", "K1ABC", "5NN", "123", "")]
    [InlineData("scCwt", "K1ABC", "5NN", "DAVID", "123")]
    [InlineData("scFieldDay", "K1ABC", "5NN", "3A", "OR")]
    [InlineData("scNaQp", "K1ABC", "5NN", "ALEX", "ON")]
    [InlineData("scHst", "K1ABC", "5NN", "123", "")]
    [InlineData("scCQWW", "DL1ABC", "5NN", "", "14")]
    [InlineData("scArrlDx", "DL1ABC", "5NN", "", "KW")]
    [InlineData("scSst", "K1ABC", "5NN", "BRUCE", "MA")]
    [InlineData("scAllJa", "JA1ABC", "5NN", "", "10H")]
    [InlineData("scAcag", "JA1ABC", "5NN", "", "1002H")]
    [InlineData("scIaruHf", "DL1ABC", "5NN", "", "28")]
    [InlineData("scArrlSS", "K1ABC", "5NN", "123 A", "72 OR")]
    public async Task EveryContestUsesTheSameValidatedEsmCompletion(
        string contestId,
        string call,
        string rst,
        string exchange1,
        string exchange2)
    {
        await using var session = await StartedSession.CreateAsync(
            new(contestId));

        CommandResult first = await session.EnterAsync(
            call,
            rst,
            exchange1,
            exchange2);
        CommandResult completed = await session.EnterAsync(
            call,
            rst,
            exchange1,
            exchange2);

        Assert.Equal(
            EnterSendMessageOutcome.SendCallAndExchange,
            first.EnterSendMessage?.Outcome);
        Assert.Equal(
            EnterSendMessageOutcome.CompleteAndLogQso,
            completed.EnterSendMessage?.Outcome);
        Assert.Equal(
            contestId == "scSst" ? ["TU W7SST"] : ["TU"],
            completed.EnterSendMessage?.SentMessages);
        Assert.Equal(1, session.Snapshot.QsoCount);
    }

    private sealed class StartedSession : IAsyncDisposable
    {
        private StartedSession(MorseRunnerEngine engine, SessionId sessionId)
        {
            Engine = engine;
            SessionId = sessionId;
        }

        public MorseRunnerEngine Engine { get; }

        public SessionId SessionId { get; }

        public SessionSnapshot Snapshot => Engine.GetSnapshot(SessionId);

        public static async Task<StartedSession> CreateAsync(
            ContestId? contestId = null,
            RunModeId? runModeId = null,
            int stationIdRate = 3)
        {
            var engine = new MorseRunnerEngine();
            SessionSettings settings = SessionSettings.CreateDefault(12_345)
                with
            {
                StationCall = "W7SST",
                StationIdRate = stationIdRate,
            };
            if (contestId is ContestId selectedContest)
            {
                settings = settings with { ContestId = selectedContest };
            }

            if (runModeId is RunModeId selectedRunMode)
            {
                settings = settings with { RunModeId = selectedRunMode };
            }

            SessionHandle handle = await engine.CreateSessionAsync(
                settings,
                TestContext.Current.CancellationToken);
            _ = await engine.ExecuteAsync(
                new StartSessionCommand(
                    RequestId.New(),
                    handle.SessionId,
                    TestClient),
                TestContext.Current.CancellationToken);
            return new(engine, handle.SessionId);
        }

        public Task<CommandResult> EnterAsync(
            string call,
            string rst,
            string exchange1,
            string exchange2) =>
            Engine.ExecuteAsync(
                new TriggerEnterSendMessageCommand(
                    RequestId.New(),
                    SessionId,
                    TestClient,
                    new(call, rst, exchange1, exchange2)),
                TestContext.Current.CancellationToken);

        public Task<CommandResult> SendAsync(OperatorIntent intent) =>
            Engine.ExecuteAsync(
                new SendOperatorIntentCommand(
                    RequestId.New(),
                    SessionId,
                    TestClient,
                    intent,
                    "",
                    "5NN",
                    "",
                    ""),
                TestContext.Current.CancellationToken);

        public Task<CommandResult> LogAsync(
            string call,
            string rst,
            string exchange1,
            string exchange2) =>
            Engine.ExecuteAsync(
                new LogQsoCommand(
                    RequestId.New(),
                    SessionId,
                    TestClient,
                    call,
                    rst,
                    exchange1,
                    exchange2),
                TestContext.Current.CancellationToken);

        public Task<CommandResult> AdvanceAsync(int blockCount) =>
            Engine.ExecuteAsync(
                new AdvanceSimulationCommand(
                    RequestId.New(),
                    SessionId,
                    TestClient,
                    blockCount),
                TestContext.Current.CancellationToken);

        public ValueTask DisposeAsync() => Engine.DisposeAsync();
    }
}
