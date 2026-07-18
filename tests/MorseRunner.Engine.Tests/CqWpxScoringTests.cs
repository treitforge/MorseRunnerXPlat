using MorseRunner.Audio;
using MorseRunner.Domain;

namespace MorseRunner.Engine.Tests;

public sealed class CqWpxScoringTests
{
    private static readonly ClientId Client = new("cq-wpx-test");

    [Fact]
    public async Task ScoreUsesPointsTimesUniquePrefixesAndExcludesDuplicates()
    {
        await using var engine =
            new MorseRunnerEngine(_ => new NullAudioSink());
        SessionHandle handle = await StartAsync(engine);
        string[] calls = ["K1ABC", "K2XYZ", "K1ABC", "DL2XYZ", "F6/W7SST"];
        int[] expectedScores = [1, 4, 4, 9, 16];

        for (int index = 0; index < calls.Length; index++)
        {
            CommandResult result = await LogAsync(
                engine,
                handle.SessionId,
                calls[index],
                index + 1);

            Assert.True(result.Accepted, result.Message);
            Assert.Equal(
                expectedScores[index],
                engine.GetSnapshot(handle.SessionId).Score);
        }

        IReadOnlyList<Qso> qsos =
            engine.GetCompletedQsos(handle.SessionId);
        Assert.Equal(5, qsos.Count);
        Assert.Equal(
            ["K1", "K2", "K1", "DL2", "F6"],
            qsos.Select(qso => qso.Prefix));
        Assert.Equal(
            [false, false, true, false, false],
            qsos.Select(qso => qso.IsDuplicate));
        Assert.Equal(LogError.Duplicate, qsos[2].ExchangeError);
        Assert.Equal("DUP", qsos[2].ErrorText);
        Assert.All(qsos, qso => Assert.Equal(1, qso.Points));
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
