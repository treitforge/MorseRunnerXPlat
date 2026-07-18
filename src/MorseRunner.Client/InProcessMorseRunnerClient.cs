using MorseRunner.Audio;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.Client;

public sealed class InProcessMorseRunnerClient(
    MorseRunnerEngine engine,
    bool ownsEngine = true) : IMorseRunnerClient
{
    private readonly MorseRunnerEngine _engine =
        engine ?? throw new ArgumentNullException(nameof(engine));
    private readonly bool _ownsEngine = ownsEngine;
    private int _disposed;

    public static InProcessMorseRunnerClient CreateDefault()
    {
        return new(new MorseRunnerEngine());
    }

    public static InProcessMorseRunnerClient CreateWithPhysicalAudio(
        string? deviceName = null,
        int queueDepth = 4)
    {
        var options = new PhysicalAudioSinkOptions(deviceName, queueDepth);
        return new(
            new MorseRunnerEngine(
                _ => new PhysicalAudioSink(options)));
    }

    public Task<EngineInfo> GetEngineInfoAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_engine.GetEngineInfo());
    }

    public Task<IReadOnlyList<AudioOutputDevice>> GetAudioOutputDevicesAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<AudioOutputDevice> devices = PhysicalAudioSink
            .GetPlaybackDevices()
            .Select(device => new AudioOutputDevice(
                device.Name,
                device.Index,
                device.IsDefault))
            .ToArray();
        return Task.FromResult(devices);
    }

    public Task<SessionHandle> CreateSessionAsync(
        SessionSettings settings,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _engine.CreateSessionAsync(settings, cancellationToken);
    }

    public Task<CommandResult> ExecuteAsync(
        SessionCommand command,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _engine.ExecuteAsync(command, cancellationToken);
    }

    public Task<SessionSnapshot> GetSnapshotAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_engine.GetSnapshot(sessionId));
    }

    public IAsyncEnumerable<SessionUpdate> SubscribeAsync(
        SessionSubscription subscription,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _engine.SubscribeAsync(subscription, cancellationToken);
    }

    public Task<IReadOnlyList<Qso>> ListCompletedQsosAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_engine.GetCompletedQsos(sessionId));
    }

    public Task<SessionResult> GetResultAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_engine.GetResult(sessionId));
    }

    public Task CloseSessionAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _engine.CloseSessionAsync(sessionId, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_ownsEngine)
        {
            await _engine.DisposeAsync();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
    }
}
