using MorseRunner.Audio;
using MorseRunner.Domain;

namespace MorseRunner.Engine.Tests;

public sealed class CwtScoringTests
{
    private static readonly ClientId Client = new("cwt-tests");

    [Fact]
    public async Task DirectLogWithoutStationTruthDoesNotScore()
    {
        await using var engine =
            new MorseRunnerEngine(_ => new NullAudioSink());
        SessionSettings settings = SessionSettings.CreateDefault(12_345) with
        {
            ContestId = new("scCwt"),
        };
        SessionHandle handle = await engine.CreateSessionAsync(
            settings,
            TestContext.Current.CancellationToken);
        await engine.ExecuteAsync(
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                Client),
            TestContext.Current.CancellationToken);

        Assert.True(
            (await engine.ExecuteAsync(
                new LogQsoCommand(
                    RequestId.New(),
                    handle.SessionId,
                    Client,
                    "K1ABC",
                    "599",
                    "DAVID",
                    "123"),
                TestContext.Current.CancellationToken)).Accepted);

        Qso qso = Assert.Single(engine.GetCompletedQsos(handle.SessionId));
        Assert.Equal(qso.Call, qso.Multiplier);
        Assert.Equal(LogError.Nil, qso.ExchangeError);
        Assert.Empty(qso.TrueCall);
        Assert.Equal(0, qso.Points);
        Assert.Equal(0, engine.GetSnapshot(handle.SessionId).Score);
    }

    [Theory]
    [InlineData("AB", "DAVID", "123", "Invalid callsign")]
    [InlineData("K1ABC", "D", "123", "Missing/Invalid Name")]
    [InlineData("K1ABC", "DAVID", "", "Missing/Invalid QTH")]
    public void ReceivedExchangeValidationMatchesLegacyFieldRules(
        string call,
        string name,
        string exchange,
        string expectedError)
    {
        ContestValidation result =
            CwtContestRules.ValidateReceivedQso(call, name, exchange);

        Assert.False(result.IsValid);
        Assert.Equal(expectedError, result.Error);
    }
}
