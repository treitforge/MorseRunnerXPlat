using MorseRunner.Domain;

namespace MorseRunner.Engine.Tests;

public sealed class RadioAndLoggingCommandTests
{
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
