using MorseRunner.Domain;

namespace MorseRunner.Engine.Tests;

public sealed class OperatorIntentTests
{
    [Fact]
    public async Task ExchangeIntentUsesTheSessionOwnedContestExchange()
    {
        await using var engine = new MorseRunnerEngine();
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(12_345),
            TestContext.Current.CancellationToken);
        ClientId client = new("test");
        await engine.ExecuteAsync(
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                client),
            TestContext.Current.CancellationToken);

        CommandResult result = await engine.ExecuteAsync(
            new SendOperatorIntentCommand(
                RequestId.New(),
                handle.SessionId,
                client,
                OperatorIntent.Exchange,
                "K1ABC",
                "5NN",
                "123",
                "OR"),
            TestContext.Current.CancellationToken);

        Assert.True(result.Accepted);
        Assert.Equal(
            "5NN 001",
            engine.GetSnapshot(handle.SessionId).LastOperatorMessage);
    }
}
