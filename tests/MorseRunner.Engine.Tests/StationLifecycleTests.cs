using MorseRunner.Audio;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.Engine.Tests;

public sealed class StationLifecycleTests
{
    private static readonly ClientId TestClient = new("station-tests");

    [Fact]
    public async Task PartialCallCorrectionCompletesAgainstStationTruth()
    {
        await using MorseRunnerEngine engine = new(_ => new NullAudioSink());
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(seed: 12_345) with
            {
                RunModeId = new("rmSingle"),
                Activity = 9,
            },
            TestContext.Current.CancellationToken);
        await StartAndAdvanceAsync(engine, handle.SessionId, blocks: 4);
        SessionSnapshot joined = engine.GetSnapshot(handle.SessionId);
        ActiveStationSnapshot station =
            Assert.Single(joined.ActiveStations ?? []);
        string partialCall = station.Callsign[..^1] + "?";

        await SendAsync(
            engine,
            handle.SessionId,
            OperatorIntent.HisCall,
            partialCall);
        Assert.Equal(
            OperatorState.NeedCallAndNumber,
            engine.GetSnapshot(handle.SessionId).ActiveOperatorState);

        await SendAsync(
            engine,
            handle.SessionId,
            OperatorIntent.HisCall,
            station.Callsign);
        Assert.Equal(
            OperatorState.NeedNumber,
            engine.GetSnapshot(handle.SessionId).ActiveOperatorState);

        await SendAsync(
            engine,
            handle.SessionId,
            OperatorIntent.Exchange,
            station.Callsign,
            station.TrueRst,
            station.TrueExchange2);
        await SendAsync(
            engine,
            handle.SessionId,
            OperatorIntent.ThankYou,
            station.Callsign);
        Assert.Equal(
            OperatorState.Done,
            engine.GetSnapshot(handle.SessionId).ActiveOperatorState);

        CommandResult logged = await engine.ExecuteAsync(
            new LogQsoCommand(
                RequestId.New(),
                handle.SessionId,
                TestClient,
                station.Callsign,
                station.TrueRst,
                station.TrueExchange2,
                string.Empty),
            TestContext.Current.CancellationToken);
        Qso qso = Assert.Single(engine.GetCompletedQsos(handle.SessionId));

        Assert.True(logged.Accepted);
        Assert.Equal(LogError.None, qso.ExchangeError);
        Assert.Equal(station.Callsign, qso.TrueCall);
        Assert.Equal(station.TrueExchange2, qso.TrueExchange2);
        Assert.Empty(engine.GetSnapshot(handle.SessionId).ActiveStations ?? []);
        SessionEvent loggedEvent = await FindEventAsync(
            engine,
            handle.SessionId,
            SessionEventKind.QsoLogged);
        Assert.StartsWith(
            station.Callsign + "|",
            loggedEvent.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoggingWithoutACompletedLiveStationProducesNil()
    {
        await using MorseRunnerEngine engine = new(_ => new NullAudioSink());
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(seed: 77) with
            {
                RunModeId = new("rmSingle"),
                Activity = 9,
            },
            TestContext.Current.CancellationToken);
        await StartAndAdvanceAsync(engine, handle.SessionId, blocks: 4);

        CommandResult logged = await engine.ExecuteAsync(
            new LogQsoCommand(
                RequestId.New(),
                handle.SessionId,
                TestClient,
                "K1ABC",
                "599",
                "1",
                string.Empty),
            TestContext.Current.CancellationToken);
        Qso qso = Assert.Single(engine.GetCompletedQsos(handle.SessionId));

        Assert.True(logged.Accepted);
        Assert.Equal(LogError.Nil, qso.ExchangeError);
        Assert.Empty(qso.TrueCall);
        Assert.Equal(0, qso.TrueRst);
        Assert.Equal(0, qso.TrueNumber);
        Assert.Empty(qso.TrueExchange1);
        Assert.Empty(qso.TrueExchange2);
        Assert.Equal(0, qso.Points);
        Assert.Equal(0, engine.GetSnapshot(handle.SessionId).Score);
    }

    [Fact]
    public async Task LoggingBeforeAnyStationExistsProducesNilTruth()
    {
        await using MorseRunnerEngine engine = new(_ => new NullAudioSink());
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(seed: 12_345) with
            {
                RunModeId = new("rmSingle"),
            },
            TestContext.Current.CancellationToken);
        await engine.ExecuteAsync(
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                TestClient),
            TestContext.Current.CancellationToken);

        CommandResult logged = await engine.ExecuteAsync(
            new LogQsoCommand(
                RequestId.New(),
                handle.SessionId,
                TestClient,
                "K1ABC",
                "599",
                "1",
                string.Empty),
            TestContext.Current.CancellationToken);
        Qso qso = Assert.Single(engine.GetCompletedQsos(handle.SessionId));

        Assert.True(logged.Accepted);
        Assert.Equal(LogError.Nil, qso.ExchangeError);
        Assert.Empty(qso.TrueCall);
        Assert.Equal(0, qso.TrueRst);
        Assert.Equal(0, qso.TrueNumber);
        Assert.Empty(qso.TrueExchange1);
        Assert.Empty(qso.TrueExchange2);
        Assert.Equal("NIL", qso.ErrorText);
        Assert.Equal(0, engine.GetSnapshot(handle.SessionId).Score);
    }

    [Fact]
    public async Task PileupActivityCreatesMultipleCanonicalCallers()
    {
        await using MorseRunnerEngine engine = new(_ => new NullAudioSink());
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(seed: 91) with
            {
                RunModeId = new("rmPileup"),
                Activity = 9,
            },
            TestContext.Current.CancellationToken);
        await StartAndAdvanceAsync(engine, handle.SessionId, blocks: 16);
        ActiveStationSnapshot[] stations =
            [.. engine.GetSnapshot(handle.SessionId).ActiveStations ?? []];

        Assert.True(stations.Length >= 3);
        Assert.Equal(
            stations.Length,
            stations.Select(station => station.Callsign).Distinct().Count());
        Assert.All(
            stations,
            station =>
            {
                Assert.Contains(station.Callsign, char.IsDigit);
                Assert.DoesNotMatch(
                    "^DX[0-9]{3}\\??$",
                    station.Callsign);
                Assert.NotEmpty(station.TrueExchange2);
            });
        SessionEvent replyEvent = await FindEventAsync(
            engine,
            handle.SessionId,
            SessionEventKind.StationReplyStarted);
        Assert.Contains("|", replyEvent.Detail);
    }


    [Theory]
    [InlineData("scWpx")]
    [InlineData("scCwt")]
    [InlineData("scFieldDay")]
    [InlineData("scNaQp")]
    [InlineData("scHst")]
    [InlineData("scCQWW")]
    [InlineData("scArrlDx")]
    [InlineData("scSst")]
    [InlineData("scAllJa")]
    [InlineData("scAcag")]
    [InlineData("scIaruHf")]
    [InlineData("scArrlSS")]
    public async Task EveryContestCreatesAStationFromItsPackagedReference(
        string contestId)
    {
        await using MorseRunnerEngine engine = new(_ => new NullAudioSink());
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(seed: 1_234) with
            {
                ContestId = new(contestId),
                RunModeId = new("rmSingle"),
                Activity = 9,
            },
            TestContext.Current.CancellationToken);
        await StartAndAdvanceAsync(engine, handle.SessionId, blocks: 4);

        ActiveStationSnapshot station = Assert.Single(
            engine.GetSnapshot(handle.SessionId).ActiveStations ?? []);
        Assert.Contains(station.Callsign, char.IsDigit);
        Assert.NotEmpty(station.TrueExchange1);
        Assert.NotEmpty(station.TrueExchange2);
    }

    [Fact]
    public async Task ReceiveSpeedBoundsControlSeededCallerSpeed()
    {
        await using MorseRunnerEngine engine = new(_ => new NullAudioSink());
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(seed: 405) with
            {
                RunModeId = new("rmPileup"),
                WordsPerMinute = 30,
                ReceiveSpeedBelowWpm = 6,
                ReceiveSpeedAboveWpm = 2,
                Activity = 9,
            },
            TestContext.Current.CancellationToken);
        await StartAndAdvanceAsync(engine, handle.SessionId, blocks: 20);

        ActiveStationSnapshot[] stations =
            [.. engine.GetSnapshot(handle.SessionId).ActiveStations ?? []];

        Assert.NotEmpty(stations);
        Assert.All(
            stations,
            station => Assert.InRange(station.WordsPerMinute, 24, 32));
        Assert.Contains(stations, station => station.WordsPerMinute < 30);
        Assert.Contains(stations, station => station.WordsPerMinute > 30);
    }


    [Theory]
    [InlineData(SerialNumberRangeMode.MidContest, 50, 499)]
    [InlineData(SerialNumberRangeMode.EndOfContest, 500, 4_999)]
    [InlineData(SerialNumberRangeMode.Custom, 70, 79)]
    public async Task SerialRangeControlsCallerExchange(
        SerialNumberRangeMode mode,
        int expectedMinimum,
        int expectedMaximum)
    {
        await using MorseRunnerEngine engine = new(_ => new NullAudioSink());
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(seed: 909) with
            {
                ContestId = new("scWpx"),
                RunModeId = new("rmPileup"),
                Activity = 9,
                SerialNumberRange = mode,
                CustomSerialNumberMinimum = 70,
                CustomSerialNumberExclusiveMaximum = 80,
            },
            TestContext.Current.CancellationToken);
        await StartAndAdvanceAsync(engine, handle.SessionId, blocks: 20);

        ActiveStationSnapshot[] stations =
            [.. engine.GetSnapshot(handle.SessionId).ActiveStations ?? []];

        Assert.NotEmpty(stations);
        Assert.All(
            stations,
            station => Assert.InRange(
                Int32.Parse(
                    station.TrueExchange2,
                    System.Globalization.CultureInfo.InvariantCulture),
                expectedMinimum,
                expectedMaximum));
    }

    [Fact]
    public async Task QskChangesStationAudioButRemainsSeedDeterministic()
    {
        SessionSettings baseSettings =
            SessionSettings.CreateDefault(seed: 8_675_309) with
            {
                Activity = 9,
                MonitorLevelDb = 0,
            };
        byte[] qskOff = await RenderHashAsync(baseSettings with { Qsk = false });
        byte[] first = await RenderHashAsync(baseSettings with { Qsk = true });
        byte[] second = await RenderHashAsync(baseSettings with { Qsk = true });

        Assert.NotEqual(qskOff, first);
        Assert.Equal(first, second);
    }

    private static async Task StartAndAdvanceAsync(
        MorseRunnerEngine engine,
        SessionId sessionId,
        int blocks)
    {
        Assert.True(
            (await engine.ExecuteAsync(
                new StartSessionCommand(
                    RequestId.New(),
                    sessionId,
                    TestClient),
                TestContext.Current.CancellationToken)).Accepted);
        await AdvanceAsync(engine, sessionId, blocks);
    }

    private static async Task AdvanceAsync(
        MorseRunnerEngine engine,
        SessionId sessionId,
        int blocks)
    {
        Assert.True(
            (await engine.ExecuteAsync(
                new AdvanceSimulationCommand(
                    RequestId.New(),
                    sessionId,
                    TestClient,
                    blocks),
                TestContext.Current.CancellationToken)).Accepted);
    }

    private static async Task SendAsync(
        MorseRunnerEngine engine,
        SessionId sessionId,
        OperatorIntent intent,
        string call,
        string rst = "",
        string exchange1 = "")
    {
        CommandResult result = await engine.ExecuteAsync(
            new SendOperatorIntentCommand(
                RequestId.New(),
                sessionId,
                TestClient,
                intent,
                call,
                rst,
                exchange1,
                string.Empty),
            TestContext.Current.CancellationToken);
        Assert.True(result.Accepted, result.Message);
    }

    private static async Task<byte[]> RenderHashAsync(
        SessionSettings settings)
    {
        var sink = new HashingAudioSink();
        await using MorseRunnerEngine engine = new(_ => sink);
        SessionHandle handle = await engine.CreateSessionAsync(
            settings,
            TestContext.Current.CancellationToken);
        Assert.True(
            (await engine.ExecuteAsync(
                new StartSessionCommand(
                    RequestId.New(),
                    handle.SessionId,
                    TestClient),
                TestContext.Current.CancellationToken)).Accepted);
        Assert.True(
            (await engine.ExecuteAsync(
                new SendOperatorIntentCommand(
                    RequestId.New(),
                    handle.SessionId,
                    TestClient,
                    OperatorIntent.Cq,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty),
                TestContext.Current.CancellationToken)).Accepted);
        await AdvanceAsync(engine, handle.SessionId, blocks: 24);
        return sink.GetHash();
    }

    private static async Task<SessionEvent> FindEventAsync(
        MorseRunnerEngine engine,
        SessionId sessionId,
        SessionEventKind kind)
    {
        await foreach (SessionUpdate update in engine.SubscribeAsync(
                           new(sessionId),
                           TestContext.Current.CancellationToken))
        {
            if (update.Event is { } sessionEvent
                && sessionEvent.Kind == kind)
            {
                return sessionEvent;
            }
        }

        throw new InvalidOperationException(
            $"Session event '{kind}' was not observed.");
    }

    private sealed class HashingAudioSink : IAudioSink
    {
        private readonly System.Security.Cryptography.IncrementalHash _hash =
            System.Security.Cryptography.IncrementalHash.CreateHash(
                System.Security.Cryptography.HashAlgorithmName.SHA256);

        public ValueTask InitializeAsync(
            SessionId sessionId,
            AudioStreamFormat format,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask WriteAsync(
            ReadOnlyMemory<float> block,
            long simulationBlock,
            CancellationToken cancellationToken)
        {
            _hash.AppendData(
                System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                    block.Span));
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public byte[] GetHash() => _hash.GetHashAndReset();

        public ValueTask DisposeAsync()
        {
            _hash.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
