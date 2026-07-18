using MorseRunner.Audio;
using MorseRunner.Domain;

namespace MorseRunner.Engine.Tests;

public sealed class CwtScoringTests
{
    private static readonly ClientId Client = new("cwt-tests");

    [Fact]
    public async Task ScoreUsesPointsTimesUniqueWorkedCalls()
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

        foreach (string call in
                 new[] { "K1ABC", "K2XYZ", "K1ABC", "K2XYZ/P" })
        {
            Assert.True(
                (await engine.ExecuteAsync(
                    new LogQsoCommand(
                        RequestId.New(),
                        handle.SessionId,
                        Client,
                        call,
                        "599",
                        "DAVID",
                        "123"),
                    TestContext.Current.CancellationToken)).Accepted);
        }

        IReadOnlyList<Qso> qsos =
            engine.GetCompletedQsos(handle.SessionId);
        Assert.Equal([false, false, true, false], qsos.Select(qso => qso.IsDuplicate));
        Assert.All(qsos, qso => Assert.Equal(qso.Call, qso.Multiplier));
        Assert.Equal(9, engine.GetSnapshot(handle.SessionId).Score);
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
