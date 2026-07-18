namespace MorseRunner.Domain;

public enum SessionEventKind
{
    Created,
    Ready,
    Started,
    Paused,
    Resumed,
    Stopping,
    Completed,
    Closed,
    CommandApplied,
    CommandRejected,
    CallerJoined,
    AudioDeviceFailed,
    AudioDeviceRecovered,
    ControlExpired,
    ResyncRequired,
}

public sealed record SessionEvent(
    Guid EngineEpoch,
    SessionId SessionId,
    long Sequence,
    long Revision,
    long SimulationBlock,
    SessionEventKind Kind,
    string? Detail);

public sealed record SessionUpdate(
    SessionEvent? Event,
    SessionSnapshot? Snapshot)
{
    public static SessionUpdate FromEvent(SessionEvent value) =>
        new(value, null);

    public static SessionUpdate FromSnapshot(SessionSnapshot value) =>
        new(null, value);
}

public sealed record SessionSubscription(
    SessionId SessionId,
    long AfterSequence = 0);
