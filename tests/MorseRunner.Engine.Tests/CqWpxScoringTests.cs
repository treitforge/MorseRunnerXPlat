using MorseRunner.Audio;
using MorseRunner.Domain;

namespace MorseRunner.Engine.Tests;

public sealed class CqWpxScoringTests
{
    private static readonly ClientId Client = new("cq-wpx-test");

    [Fact]
    public async Task DirectLogWithoutStationTruthDoesNotScore()
    {
        await using var engine =
            new MorseRunnerEngine(_ => new NullAudioSink());
        SessionHandle handle = await StartAsync(engine);
        CommandResult result = await LogAsync(
            engine,
            handle.SessionId,
            "K1ABC",
            serialNumber: 1);

        Qso qso = Assert.Single(engine.GetCompletedQsos(handle.SessionId));
        Assert.True(result.Accepted, result.Message);
        Assert.Equal("K1", qso.Prefix);
        Assert.Equal(LogError.Nil, qso.ExchangeError);
        Assert.Empty(qso.TrueCall);
        Assert.Equal(0, qso.Points);
        Assert.Equal(0, engine.GetSnapshot(handle.SessionId).Score);
    }

    [Theory]
    [InlineData("AB", "599", "1", "Invalid callsign")]
    [InlineData("K1ABC", "59", "1", "Missing/Invalid RST")]
    [InlineData("K1ABC", "599", "", "Missing/Invalid Nr.")]
    public async Task InvalidReceivedExchangeIsRejectedWithoutChangingTheLog(
        string call,
        string rst,
        string serialNumber,
        string expectedMessage)
    {
        await using var engine =
            new MorseRunnerEngine(_ => new NullAudioSink());
        SessionHandle handle = await StartAsync(engine);

        CommandResult result = await engine.ExecuteAsync(
            new LogQsoCommand(
                RequestId.New(),
                handle.SessionId,
                Client,
                call,
                rst,
                serialNumber,
                string.Empty),
            TestContext.Current.CancellationToken);

        Assert.False(result.Accepted);
        Assert.Equal(DomainErrorCodes.InvalidSetting, result.ErrorCode);
        Assert.Equal(expectedMessage, result.Message);
        Assert.Empty(engine.GetCompletedQsos(handle.SessionId));
        Assert.Equal(0, engine.GetSnapshot(handle.SessionId).Score);
    }

    private static async Task<SessionHandle> StartAsync(
        MorseRunnerEngine engine)
    {
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(12_345),
            TestContext.Current.CancellationToken);
        CommandResult started = await engine.ExecuteAsync(
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                Client),
            TestContext.Current.CancellationToken);
        Assert.True(started.Accepted, started.Message);
        return handle;
    }

    private static async Task<CommandResult> LogAsync(
        MorseRunnerEngine engine,
        SessionId sessionId,
        string call,
        int serialNumber)
    {
        return await engine.ExecuteAsync(
            new LogQsoCommand(
                RequestId.New(),
                sessionId,
                Client,
                call,
                "599",
                serialNumber.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                string.Empty),
            TestContext.Current.CancellationToken);
    }
}
