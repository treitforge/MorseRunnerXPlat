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
        Assert.Equal(1, snapshot.QsoCount);
        Assert.Equal(1, snapshot.Score);
        Assert.Equal("K1ABC", snapshot.LastLoggedCall);
    }
}
