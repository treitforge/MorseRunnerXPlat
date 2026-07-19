using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MorseRunner.Audio;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.Engine.Tests;

public sealed class SessionLoopTests
{
    private static readonly ClientId TestClient = new("engine-tests");

    [Fact]
    public async Task SeededSessionAdvancesOnlyOnTheSessionWorker()
    {
        Dictionary<SessionId, NullAudioSink> sinks = [];
        await using MorseRunnerEngine engine = new(
            sessionId =>
            {
                NullAudioSink sink = new();
                sinks.Add(sessionId, sink);
                return sink;
            });
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(seed: 12345),
            TestContext.Current.CancellationToken);

        CommandResult start = await engine.ExecuteAsync(
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                TestClient),
            TestContext.Current.CancellationToken);
        CommandResult advance = await engine.ExecuteAsync(
            new AdvanceSimulationCommand(
                RequestId.New(),
                handle.SessionId,
                TestClient,
                BlockCount: 16),
            TestContext.Current.CancellationToken);
        SessionSnapshot snapshot = engine.GetSnapshot(handle.SessionId);

        Assert.True(start.Accepted);
        Assert.True(advance.Accepted);
        Assert.Equal(0, advance.AppliedBlock);
        Assert.Equal(SessionState.Running, snapshot.State);
        Assert.Equal(16, snapshot.SimulationBlock);
        Assert.Equal(16 * CompatibilityProfile.BlockSize, snapshot.RenderedSamples);
        Assert.Equal("M0MCV", snapshot.LastCaller);
        Assert.Equal(2, snapshot.ActiveStations?.Count);
        Assert.Contains(
            snapshot.ActiveStations!,
            station => station.Callsign == "M0MCV");
        Assert.Equal(16, sinks[handle.SessionId].BlocksWritten);
        Assert.Equal(
            TimeSpan.FromSeconds(
                16D * CompatibilityProfile.BlockSize
                / CompatibilityProfile.SampleRate),
            snapshot.ElapsedSimulationTime);
    }

    [Fact]
    public async Task SameSeedProducesTheSameObservableCallerSequence()
    {
        string? first = await RunSeededScenarioAsync(8675309);
        string? second = await RunSeededScenarioAsync(8675309);

        Assert.NotNull(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task AutomaticTimingAdvancesInsideTheSessionLoop()
    {
        await using MorseRunnerEngine engine = new(
            _ => new NullAudioSink(),
            new MorseRunnerEngineOptions
            {
                AutomaticTiming = true,
                BlockPeriod = TimeSpan.FromMilliseconds(1),
            });
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(seed: 91),
            TestContext.Current.CancellationToken);
        Assert.True(
            (await engine.ExecuteAsync(
                new StartSessionCommand(
                    RequestId.New(),
                    handle.SessionId,
                    TestClient),
                TestContext.Current.CancellationToken)).Accepted);

        SessionSnapshot? observed = null;
        await foreach (SessionUpdate update in engine.SubscribeAsync(
                           new(handle.SessionId, AfterSequence: 0),
                           TestContext.Current.CancellationToken))
        {
            if (update.Snapshot is { SimulationBlock: >= 3 } snapshot)
            {
                observed = snapshot;
                break;
            }
        }

        Assert.NotNull(observed);
        Assert.True(observed.SimulationBlock >= 3);
        Assert.Equal(SessionState.Running, observed.State);
    }

    [Fact]
    public async Task AutomaticTimingUsesTheCompatibilityBlockPeriod()
    {
        await using MorseRunnerEngine engine = new(
            _ => new NullAudioSink(),
            new MorseRunnerEngineOptions
            {
                AutomaticTiming = true,
            });
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(seed: 73),
            TestContext.Current.CancellationToken);
        var stopwatch = Stopwatch.StartNew();
        await engine.ExecuteAsync(
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                TestClient),
            TestContext.Current.CancellationToken);

        SessionSnapshot? observed = null;
        await foreach (SessionUpdate update in engine.SubscribeAsync(
                           new(handle.SessionId),
                           TestContext.Current.CancellationToken))
        {
            if (update.Snapshot is { SimulationBlock: >= 8 } snapshot)
            {
                observed = snapshot;
                break;
            }
        }

        stopwatch.Stop();
        Assert.NotNull(observed);
        Assert.InRange(
            stopwatch.Elapsed,
            TimeSpan.FromMilliseconds(280),
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void AutomaticClockUsesAbsoluteDeadlinesAndBoundedCatchUp()
    {
        var clock = new AutomaticBlockClock(
            CompatibilityProfile.SampleRate,
            CompatibilityProfile.BlockSize,
            maximumCatchUpBlocks: 2);
        long expectedAtSixtySeconds = (long)Math.Floor(
            60d * CompatibilityProfile.SampleRate
            / CompatibilityProfile.BlockSize);
        long sampleDriftAtSixtySeconds =
            (60L * CompatibilityProfile.SampleRate)
            - (expectedAtSixtySeconds * CompatibilityProfile.BlockSize);

        Assert.Equal(
            1,
            clock.GetDueBlockCount(
                TimeSpan.FromSeconds(
                    (double)CompatibilityProfile.BlockSize
                    / CompatibilityProfile.SampleRate),
                renderedBlocks: 0));
        Assert.Equal(
            2,
            clock.GetDueBlockCount(
                TimeSpan.FromSeconds(1),
                renderedBlocks: 0));
        Assert.Equal(
            0,
            clock.GetDueBlockCount(
                TimeSpan.FromSeconds(60),
                renderedBlocks: expectedAtSixtySeconds));
        Assert.Equal(
            1,
            clock.GetDueBlockCount(
                TimeSpan.FromSeconds(60),
                renderedBlocks: expectedAtSixtySeconds - 1));
        Assert.InRange(
            sampleDriftAtSixtySeconds,
            0,
            CompatibilityProfile.BlockSize - 1);
    }

    [Fact]
    public async Task InvalidStateAndDuplicateRequestConflictAreStable()
    {
        await using MorseRunnerEngine engine = new(_ => new NullAudioSink());
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(seed: 7),
            TestContext.Current.CancellationToken);
        RequestId requestId = RequestId.New();
        StartSessionCommand start = new(
            requestId,
            handle.SessionId,
            TestClient);

        CommandResult first = await engine.ExecuteAsync(
            start,
            TestContext.Current.CancellationToken);
        CommandResult repeated = await engine.ExecuteAsync(
            start,
            TestContext.Current.CancellationToken);
        CommandResult conflict = await engine.ExecuteAsync(
            new PauseSessionCommand(
                requestId,
                handle.SessionId,
                TestClient),
            TestContext.Current.CancellationToken);
        CommandResult secondStart = await engine.ExecuteAsync(
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                TestClient),
            TestContext.Current.CancellationToken);

        Assert.Equal(first, repeated);
        Assert.False(conflict.Accepted);
        Assert.Equal(DomainErrorCodes.DuplicateRequestConflict, conflict.ErrorCode);
        Assert.False(secondStart.Accepted);
        Assert.Equal(DomainErrorCodes.InvalidSessionState, secondStart.ErrorCode);
    }

    [Fact]
    public async Task SubscriptionOutsideRetainedHistoryRequiresResync()
    {
        await using MorseRunnerEngine engine = new(_ => new NullAudioSink());
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(seed: 17),
            TestContext.Current.CancellationToken);
        Assert.True(
            (await engine.ExecuteAsync(
                new StartSessionCommand(
                    RequestId.New(),
                    handle.SessionId,
                    TestClient),
                TestContext.Current.CancellationToken)).Accepted);

        for (int index = 0; index < 140; index++)
        {
            Assert.True(
                (await engine.ExecuteAsync(
                    new PauseSessionCommand(
                        RequestId.New(),
                        handle.SessionId,
                        TestClient),
                    TestContext.Current.CancellationToken)).Accepted);
            Assert.True(
                (await engine.ExecuteAsync(
                    new ResumeSessionCommand(
                        RequestId.New(),
                        handle.SessionId,
                        TestClient),
                    TestContext.Current.CancellationToken)).Accepted);
        }

        await using IAsyncEnumerator<SessionUpdate> subscription =
            engine.SubscribeAsync(
                    new(handle.SessionId, AfterSequence: 1),
                    TestContext.Current.CancellationToken)
                .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await subscription.MoveNextAsync());
        Assert.Equal(
            SessionEventKind.ResyncRequired,
            subscription.Current.Event?.Kind);
        Assert.Equal(
            DomainErrorCodes.ResyncRequired,
            subscription.Current.Event?.Detail);
    }

    [Fact]
    public async Task AudioFailurePausesAtABlockBoundaryAndCanRecover()
    {
        var sink = new RecoverableTestSink();
        await using MorseRunnerEngine engine = new(_ => sink);
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(seed: 17),
            TestContext.Current.CancellationToken);
        await engine.ExecuteAsync(
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                TestClient),
            TestContext.Current.CancellationToken);

        CommandResult advance = await engine.ExecuteAsync(
            new AdvanceSimulationCommand(
                RequestId.New(),
                handle.SessionId,
                TestClient,
                BlockCount: 4),
            TestContext.Current.CancellationToken);
        SessionSnapshot failed = engine.GetSnapshot(handle.SessionId);

        Assert.True(advance.Accepted);
        Assert.Equal(SessionState.Paused, failed.State);
        Assert.Equal(1, failed.SimulationBlock);
        Assert.False(failed.AudioOutputHealthy);

        CommandResult recovery = await engine.ExecuteAsync(
            new RecoverAudioCommand(
                RequestId.New(),
                handle.SessionId,
                TestClient,
                DeviceName: "replacement"),
            TestContext.Current.CancellationToken);
        CommandResult resume = await engine.ExecuteAsync(
            new ResumeSessionCommand(
                RequestId.New(),
                handle.SessionId,
                TestClient),
            TestContext.Current.CancellationToken);

        Assert.True(recovery.Accepted);
        Assert.True(resume.Accepted);
        Assert.Equal("replacement", sink.RecoveredDeviceName);
        Assert.True(engine.GetSnapshot(handle.SessionId).AudioOutputHealthy);
    }

    [Fact]
    public async Task AudioDeviceCanBeSelectedBeforeSessionStart()
    {
        var sink = new RecoverableTestSink();
        await using MorseRunnerEngine engine = new(_ => sink);
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(seed: 18),
            TestContext.Current.CancellationToken);

        CommandResult recovery = await engine.ExecuteAsync(
            new RecoverAudioCommand(
                RequestId.New(),
                handle.SessionId,
                TestClient,
                DeviceName: "preferred"),
            TestContext.Current.CancellationToken);

        Assert.True(recovery.Accepted);
        Assert.Equal("preferred", sink.RecoveredDeviceName);
        Assert.Equal(
            SessionState.Ready,
            engine.GetSnapshot(handle.SessionId).State);
    }

    [Fact]
    public async Task BandConditionsAreDeterministicAndChangeRenderedAudio()
    {
        SessionSettings cleanSettings =
            SessionSettings.CreateDefault(seed: 12345) with
            {
                MonitorLevelDb = 0,
                Qsk = true,
            };
        SessionSettings conditionSettings = cleanSettings with
        {
            Qsb = true,
            Qrm = true,
            Qrn = true,
            Flutter = true,
        };

        byte[] clean = await RenderHashAsync(cleanSettings);
        byte[] first = await RenderHashAsync(conditionSettings);
        byte[] second = await RenderHashAsync(conditionSettings);

        Assert.NotEqual(clean, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ActivityControlsCallerArrivalCadence()
    {
        string? lowActivity = await CallerAfterFiveBlocksAsync(activity: 1);
        string? highActivity = await CallerAfterFiveBlocksAsync(activity: 9);

        Assert.Null(lowActivity);
        Assert.NotNull(highActivity);
    }

    private static async Task<string?> RunSeededScenarioAsync(int seed)
    {
        await using MorseRunnerEngine engine = new(_ => new NullAudioSink());
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(seed),
            TestContext.Current.CancellationToken);
        await engine.ExecuteAsync(
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                TestClient),
            TestContext.Current.CancellationToken);
        await engine.ExecuteAsync(
            new AdvanceSimulationCommand(
                RequestId.New(),
                handle.SessionId,
                TestClient,
                BlockCount: 24),
            TestContext.Current.CancellationToken);

        return engine.GetSnapshot(handle.SessionId).LastCaller;
    }

    private static async Task<byte[]> RenderHashAsync(SessionSettings settings)
    {
        var sink = new HashingAudioSink();
        await using MorseRunnerEngine engine = new(_ => sink);
        SessionHandle handle = await engine.CreateSessionAsync(
            settings,
            TestContext.Current.CancellationToken);
        await engine.ExecuteAsync(
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                TestClient),
            TestContext.Current.CancellationToken);
        await engine.ExecuteAsync(
            new AdvanceSimulationCommand(
                RequestId.New(),
                handle.SessionId,
                TestClient,
                BlockCount: 6),
            TestContext.Current.CancellationToken);
        return sink.GetHash();
    }

    private static async Task<string?> CallerAfterFiveBlocksAsync(int activity)
    {
        await using MorseRunnerEngine engine = new(_ => new NullAudioSink());
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(seed: 15) with
            {
                Activity = activity,
            },
            TestContext.Current.CancellationToken);
        await engine.ExecuteAsync(
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                TestClient),
            TestContext.Current.CancellationToken);
        await engine.ExecuteAsync(
            new AdvanceSimulationCommand(
                RequestId.New(),
                handle.SessionId,
                TestClient,
                BlockCount: 5),
            TestContext.Current.CancellationToken);
        return engine.GetSnapshot(handle.SessionId).LastCaller;
    }

    private sealed class HashingAudioSink : IAudioSink
    {
        private readonly IncrementalHash _hash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public ValueTask InitializeAsync(
            SessionId sessionId,
            AudioStreamFormat format,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteAsync(
            ReadOnlyMemory<float> samples,
            long simulationBlock,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _hash.AppendData(MemoryMarshal.AsBytes(samples.Span));
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public byte[] GetHash() => _hash.GetHashAndReset();

        public ValueTask DisposeAsync()
        {
            _hash.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecoverableTestSink :
        IAudioSink,
        IAudioSinkMetricsSource,
        IRecoverableAudioSink
    {
        private bool _healthy = true;

        public string? RecoveredDeviceName { get; private set; }

        public ValueTask InitializeAsync(
            SessionId sessionId,
            AudioStreamFormat format,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteAsync(
            ReadOnlyMemory<float> samples,
            long simulationBlock,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _healthy = false;
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public AudioSinkMetrics GetMetrics()
        {
            return new(
                QueuedBlocks: 0,
                UnderrunCount: 0,
                DroppedBlockCount: 0,
                IsHealthy: _healthy);
        }

        public ValueTask RecoverAsync(
            string? deviceName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RecoveredDeviceName = deviceName;
            _healthy = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
