using MorseRunner.Domain;

namespace MorseRunner.Client;

public interface IMorseRunnerClient : IAsyncDisposable
{
    Task<EngineInfo> GetEngineInfoAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AudioOutputDevice>> GetAudioOutputDevicesAsync(
        CancellationToken cancellationToken);

    Task<SessionHandle> CreateSessionAsync(
        SessionSettings settings,
        CancellationToken cancellationToken);

    Task<CommandResult> ExecuteAsync(
        SessionCommand command,
        CancellationToken cancellationToken);

    Task<SessionSnapshot> GetSnapshotAsync(
        SessionId sessionId,
        CancellationToken cancellationToken);

    IAsyncEnumerable<SessionUpdate> SubscribeAsync(
        SessionSubscription subscription,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Qso>> ListCompletedQsosAsync(
        SessionId sessionId,
        CancellationToken cancellationToken);

    Task<SessionResult> GetResultAsync(
        SessionId sessionId,
        CancellationToken cancellationToken);

    Task CloseSessionAsync(
        SessionId sessionId,
        CancellationToken cancellationToken);
}
