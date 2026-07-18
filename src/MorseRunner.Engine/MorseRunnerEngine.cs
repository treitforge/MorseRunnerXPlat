using System.Collections.Concurrent;
using MorseRunner.Domain;

namespace MorseRunner.Engine;

public sealed class MorseRunnerEngine : IAsyncDisposable
{
    private readonly ConcurrentDictionary<SessionId, EngineSession> _sessions = new();
    private readonly Func<SessionId, IAudioSink> _audioSinkFactory;
    private readonly EngineId _engineId = EngineId.New();
    private readonly Guid _engineEpoch = Guid.NewGuid();
    private int _disposed;

    public MorseRunnerEngine()
        : this(_ => new UnconfiguredAudioSink())
    {
    }

    public MorseRunnerEngine(Func<SessionId, IAudioSink> audioSinkFactory)
    {
        ArgumentNullException.ThrowIfNull(audioSinkFactory);
        _audioSinkFactory = audioSinkFactory;
    }

    public EngineInfo GetEngineInfo()
    {
        ThrowIfDisposed();
        return new(
            _engineId,
            "MorseRunnerXPlat",
            "0.1.0",
            _engineEpoch,
            [
                "session",
                "session-events",
                "contest-catalog",
                "audio-output",
                "wav-recording",
                "deterministic-scenarios",
                "results",
            ],
            IsInProcess: true);
    }

    public async Task<SessionHandle> CreateSessionAsync(
        SessionSettings settings,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ValidateSettings(settings);

        SessionId sessionId = SessionId.New();
        IAudioSink sink = _audioSinkFactory(sessionId);
        EngineSession session = new(
            _engineEpoch,
            sessionId,
            settings,
            sink);
        if (!_sessions.TryAdd(sessionId, session))
        {
            await sink.DisposeAsync();
            throw new InvalidOperationException("Could not register a new session.");
        }

        try
        {
            return await session.InitializeAsync(cancellationToken);
        }
        catch
        {
            _sessions.TryRemove(sessionId, out _);
            await session.DisposeAsync();
            throw;
        }
    }

    public async Task<CommandResult> ExecuteAsync(
        SessionCommand command,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(command);
        if (!_sessions.TryGetValue(command.SessionId, out EngineSession? session))
        {
            return new(
                Accepted: false,
                DomainErrorCodes.SessionNotFound,
                "The session does not exist.",
                AppliedRevision: 0,
                AppliedBlock: 0);
        }

        return await session.ExecuteAsync(command, cancellationToken);
    }

    public SessionSnapshot GetSnapshot(SessionId sessionId)
    {
        ThrowIfDisposed();
        return GetSession(sessionId).Snapshot;
    }

    public IAsyncEnumerable<SessionUpdate> SubscribeAsync(
        SessionSubscription subscription,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return GetSession(subscription.SessionId).SubscribeAsync(
            subscription,
            cancellationToken);
    }

    public IReadOnlyList<Qso> GetCompletedQsos(SessionId sessionId)
    {
        ThrowIfDisposed();
        return GetSession(sessionId).CompletedQsos;
    }

    public SessionResult GetResult(SessionId sessionId)
    {
        ThrowIfDisposed();
        SessionSnapshot snapshot = GetSession(sessionId).Snapshot;
        return new(
            snapshot.SessionId,
            snapshot.ContestId,
            snapshot.QsoCount,
            snapshot.Score,
            snapshot.ElapsedSimulationTime,
            snapshot.State);
    }

    public async Task CloseSessionAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!_sessions.TryRemove(sessionId, out EngineSession? session))
        {
            return;
        }

        await session.CloseAsync(cancellationToken);
        await session.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        EngineSession[] sessions = _sessions.Values.ToArray();
        _sessions.Clear();
        foreach (EngineSession session in sessions)
        {
            await session.DisposeAsync();
        }
    }

    private static void ValidateSettings(SessionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.DurationBlocks < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "Duration blocks cannot be negative.");
        }

        ContestCatalog.Get(settings.ContestId);
        if (!RunModeCatalog.All.Contains(settings.RunModeId))
        {
            throw new ArgumentException(
                "The run mode is not supported.",
                nameof(settings));
        }
    }

    private EngineSession GetSession(SessionId sessionId)
    {
        return _sessions.TryGetValue(sessionId, out EngineSession? session)
            ? session
            : throw new KeyNotFoundException(
                $"Session '{sessionId}' does not exist.");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
    }
}

file sealed class UnconfiguredAudioSink : IAudioSink
{
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
        return ValueTask.CompletedTask;
    }

    public ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
