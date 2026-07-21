using MorseRunner.Domain;

namespace MorseRunner.Engine.Tests;

public sealed class RadioAndLoggingCommandTests
{
    [Fact]
    public async Task QsbCanBeChangedAtRunningAndPausedBoundaries()
    {
        await using var engine = new MorseRunnerEngine();
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(12_345),
            TestContext.Current.CancellationToken);
        ClientId client = new("test");

        CommandResult beforeStart = await engine.ExecuteAsync(
            new SetRadioConditionCommand(
                RequestId.New(),
                handle.SessionId,
                client,
                RadioCondition.Qsb,
                Enabled: true),
            TestContext.Current.CancellationToken);
        Assert.False(beforeStart.Accepted);
        Assert.Equal(
            DomainErrorCodes.InvalidSessionState,
            beforeStart.ErrorCode);

        Assert.True(
            (await engine.ExecuteAsync(
                new StartSessionCommand(
                    RequestId.New(),
                    handle.SessionId,
                    client),
                TestContext.Current.CancellationToken)).Accepted);
        CommandResult enabled = await engine.ExecuteAsync(
            new SetRadioConditionCommand(
                RequestId.New(),
                handle.SessionId,
                client,
                RadioCondition.Qsb,
                Enabled: true),
            TestContext.Current.CancellationToken);
        Assert.True(enabled.Accepted);
        Assert.True(engine.GetSnapshot(handle.SessionId).QsbEnabled);

        Assert.True(
            (await engine.ExecuteAsync(
                new PauseSessionCommand(
                    RequestId.New(),
                    handle.SessionId,
                    client),
                TestContext.Current.CancellationToken)).Accepted);
        CommandResult disabled = await engine.ExecuteAsync(
            new SetRadioConditionCommand(
                RequestId.New(),
                handle.SessionId,
                client,
                RadioCondition.Qsb,
                Enabled: false),
            TestContext.Current.CancellationToken);
        Assert.True(disabled.Accepted);
        Assert.False(engine.GetSnapshot(handle.SessionId).QsbEnabled);
    }

    [Fact]
    public async Task QskCanBeChangedAtARunningBoundary()
    {
        await using var engine = new MorseRunnerEngine();
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(12_345),
            TestContext.Current.CancellationToken);
        ClientId client = new("test");
        Assert.True(
            (await engine.ExecuteAsync(
                new StartSessionCommand(
                    RequestId.New(),
                    handle.SessionId,
                    client),
                TestContext.Current.CancellationToken)).Accepted);

        CommandResult enabled = await engine.ExecuteAsync(
            new SetRadioConditionCommand(
                RequestId.New(),
                handle.SessionId,
                client,
                RadioCondition.Qsk,
                Enabled: true),
            TestContext.Current.CancellationToken);

        Assert.True(enabled.Accepted);
        Assert.True(engine.GetSnapshot(handle.SessionId).QskEnabled);
    }

    [Fact]
    public async Task UnknownRadioConditionIsRejectedWithoutMutation()
    {
        await using var engine = new MorseRunnerEngine();
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(12_345) with { Qsb = true },
            TestContext.Current.CancellationToken);
        ClientId client = new("test");
        Assert.True(
            (await engine.ExecuteAsync(
                new StartSessionCommand(
                    RequestId.New(),
                    handle.SessionId,
                    client),
                TestContext.Current.CancellationToken)).Accepted);

        CommandResult result = await engine.ExecuteAsync(
            new SetRadioConditionCommand(
                RequestId.New(),
                handle.SessionId,
                client,
                (RadioCondition)Int32.MaxValue,
                Enabled: false),
            TestContext.Current.CancellationToken);

        Assert.False(result.Accepted);
        Assert.Equal(DomainErrorCodes.InvalidSetting, result.ErrorCode);
        Assert.True(engine.GetSnapshot(handle.SessionId).QsbEnabled);
    }

    [Fact]
    public async Task RadioAdjustmentsAndQsoLoggingStayOnSessionLoop()
    {
        await using var engine = new MorseRunnerEngine();
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(12_345),
            TestContext.Current.CancellationToken);
        ClientId client = new("test");
        await engine.ExecuteAsync(
            new StartSessionCommand(RequestId.New(), handle.SessionId, client),
            TestContext.Current.CancellationToken);
        await engine.ExecuteAsync(
            new AdjustRadioControlCommand(
                RequestId.New(),
                handle.SessionId,
                client,
                RadioControl.Rit,
                50),
            TestContext.Current.CancellationToken);
        await engine.ExecuteAsync(
            new AdjustRadioControlCommand(
                RequestId.New(),
                handle.SessionId,
                client,
                RadioControl.MonitorLevel,
                -100),
            TestContext.Current.CancellationToken);
        await engine.ExecuteAsync(
            new LogQsoCommand(
                RequestId.New(),
                handle.SessionId,
                client,
                "K1ABC",
                "5NN",
                "123",
                "OR"),
            TestContext.Current.CancellationToken);

        SessionSnapshot snapshot = engine.GetSnapshot(handle.SessionId);
        Assert.Equal(50, snapshot.RitOffsetHz);
        Assert.Equal(-60d, snapshot.CurrentMonitorLevelDb);
        Assert.Equal(1, snapshot.QsoCount);
        Assert.Equal(1, snapshot.Score);
        Assert.Equal("K1ABC", snapshot.LastLoggedCall);
    }

    [Fact]
    public async Task RitAdjustmentsClampToCeRange()
    {
        await using var engine = new MorseRunnerEngine();
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(12_345),
            TestContext.Current.CancellationToken);
        ClientId client = new("test");
        await engine.ExecuteAsync(
            new StartSessionCommand(RequestId.New(), handle.SessionId, client),
            TestContext.Current.CancellationToken);

        CommandResult upper = await engine.ExecuteAsync(
            new AdjustRadioControlCommand(
                RequestId.New(),
                handle.SessionId,
                client,
                RadioControl.Rit,
                550),
            TestContext.Current.CancellationToken);
        Assert.True(upper.Accepted);
        Assert.Equal(500, engine.GetSnapshot(handle.SessionId).RitOffsetHz);

        CommandResult lower = await engine.ExecuteAsync(
            new AdjustRadioControlCommand(
                RequestId.New(),
                handle.SessionId,
                client,
                RadioControl.Rit,
                -1_050),
            TestContext.Current.CancellationToken);
        Assert.True(lower.Accepted);
        Assert.Equal(-500, engine.GetSnapshot(handle.SessionId).RitOffsetHz);
    }

    [Fact]
    public async Task SpeedAdjustmentsClampToCeUpperRange()
    {
        await using var engine = new MorseRunnerEngine();
        SessionSettings settings = SessionSettings.CreateDefault(12_345) with
        {
            WordsPerMinute = 118,
        };
        SessionHandle handle = await engine.CreateSessionAsync(
            settings,
            TestContext.Current.CancellationToken);
        ClientId client = new("test");
        await engine.ExecuteAsync(
            new StartSessionCommand(RequestId.New(), handle.SessionId, client),
            TestContext.Current.CancellationToken);

        CommandResult upper = await engine.ExecuteAsync(
            new AdjustRadioControlCommand(
                RequestId.New(),
                handle.SessionId,
                client,
                RadioControl.Speed,
                2),
            TestContext.Current.CancellationToken);
        Assert.True(upper.Accepted);
        Assert.Equal(
            120,
            engine.GetSnapshot(handle.SessionId).CurrentWordsPerMinute);

        CommandResult extra = await engine.ExecuteAsync(
            new AdjustRadioControlCommand(
                RequestId.New(),
                handle.SessionId,
                client,
                RadioControl.Speed,
                2),
            TestContext.Current.CancellationToken);
        Assert.True(extra.Accepted);
        Assert.Equal(
            120,
            engine.GetSnapshot(handle.SessionId).CurrentWordsPerMinute);
    }
}
