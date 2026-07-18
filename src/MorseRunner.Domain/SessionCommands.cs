namespace MorseRunner.Domain;

public abstract record SessionCommand(
    RequestId RequestId,
    SessionId SessionId,
    ClientId ClientId,
    long? ExpectedRevision);

public sealed record StartSessionCommand(
    RequestId RequestId,
    SessionId SessionId,
    ClientId ClientId,
    long? ExpectedRevision = null)
    : SessionCommand(RequestId, SessionId, ClientId, ExpectedRevision);

public sealed record PauseSessionCommand(
    RequestId RequestId,
    SessionId SessionId,
    ClientId ClientId,
    long? ExpectedRevision = null)
    : SessionCommand(RequestId, SessionId, ClientId, ExpectedRevision);

public sealed record ResumeSessionCommand(
    RequestId RequestId,
    SessionId SessionId,
    ClientId ClientId,
    long? ExpectedRevision = null)
    : SessionCommand(RequestId, SessionId, ClientId, ExpectedRevision);

public sealed record StopSessionCommand(
    RequestId RequestId,
    SessionId SessionId,
    ClientId ClientId,
    long? ExpectedRevision = null)
    : SessionCommand(RequestId, SessionId, ClientId, ExpectedRevision);

public sealed record RecoverAudioCommand(
    RequestId RequestId,
    SessionId SessionId,
    ClientId ClientId,
    string? DeviceName = null,
    long? ExpectedRevision = null)
    : SessionCommand(RequestId, SessionId, ClientId, ExpectedRevision);

public sealed record AdvanceSimulationCommand(
    RequestId RequestId,
    SessionId SessionId,
    ClientId ClientId,
    int BlockCount,
    long? ExpectedRevision = null)
    : SessionCommand(RequestId, SessionId, ClientId, ExpectedRevision);

public enum OperatorIntent
{
    Cq,
    Exchange,
    ThankYou,
    MyCall,
    HisCall,
    Before,
    Question,
    Nil,
    NumberQuestion,
    Abort,
}

public sealed record SendOperatorIntentCommand(
    RequestId RequestId,
    SessionId SessionId,
    ClientId ClientId,
    OperatorIntent Intent,
    string Call,
    string Rst,
    string Exchange1,
    string Exchange2,
    long? ExpectedRevision = null)
    : SessionCommand(RequestId, SessionId, ClientId, ExpectedRevision);

public enum RadioControl
{
    Rit,
    Bandwidth,
    Speed,
}

public sealed record AdjustRadioControlCommand(
    RequestId RequestId,
    SessionId SessionId,
    ClientId ClientId,
    RadioControl Control,
    int Delta,
    long? ExpectedRevision = null)
    : SessionCommand(RequestId, SessionId, ClientId, ExpectedRevision);

public sealed record LogQsoCommand(
    RequestId RequestId,
    SessionId SessionId,
    ClientId ClientId,
    string Call,
    string Rst,
    string Exchange1,
    string Exchange2,
    long? ExpectedRevision = null)
    : SessionCommand(RequestId, SessionId, ClientId, ExpectedRevision);

public sealed record ExpireControlLeaseCommand(
    RequestId RequestId,
    SessionId SessionId,
    ClientId ClientId,
    long? ExpectedRevision = null)
    : SessionCommand(RequestId, SessionId, ClientId, ExpectedRevision);

public sealed record CommandResult(
    bool Accepted,
    string? ErrorCode,
    string? Message,
    long AppliedRevision,
    long AppliedBlock);

public static class DomainErrorCodes
{
    public const string SessionNotFound = "session-not-found";
    public const string InvalidSessionState = "invalid-session-state";
    public const string StaleRevision = "stale-revision";
    public const string DuplicateRequestConflict = "duplicate-request-conflict";
    public const string InvalidSetting = "invalid-setting";
    public const string CommandQueueFull = "command-queue-full";
    public const string UnsupportedCapability = "unsupported-capability";
    public const string AudioDeviceUnavailable = "audio-device-unavailable";
    public const string ControlLeaseRequired = "control-lease-required";
    public const string ControlLeaseHeld = "control-lease-held";
    public const string ControlLeaseExpired = "control-lease-expired";
    public const string InvalidControlLease = "invalid-control-lease";
    public const string ResyncRequired = "resync-required";
}
