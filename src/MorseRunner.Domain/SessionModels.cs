namespace MorseRunner.Domain;

public static class CompatibilityProfile
{
    public const int SampleRate = 11_025;
    public const int BlockSize = 512;
    public const int AudioStartupPrefillRequestCount = 4;
    public const int AudioStartupRequestCount = 5;
}

public enum SessionState
{
    Created,
    Ready,
    Running,
    Paused,
    Stopping,
    Completed,
    Faulted,
    Closed,
}

public enum SerialNumberRangeMode
{
    StartOfContest = 0,
    MidContest = 1,
    EndOfContest = 2,
    Custom = 3,
}

public sealed record SessionSettings(
    int Seed,
    ContestId ContestId,
    RunModeId RunModeId,
    long DurationBlocks)
{
    public string StationCall { get; init; } = "W7SST";

    public int WordsPerMinute { get; init; } = 30;

    public int PitchHz { get; init; } = 600;

    public int BandwidthHz { get; init; } = 500;

    public int Activity { get; init; } = 5;

    public int StationIdRate { get; init; } = 3;

    public bool Qsk { get; init; }

    public bool Qsb { get; init; }

    public bool Qrm { get; init; }

    public bool Qrn { get; init; }

    public bool Flutter { get; init; }

    public bool Lids { get; init; }

    public double MonitorLevelDb { get; init; }

    public int ReceiveSpeedBelowWpm { get; init; } = -1;

    public int ReceiveSpeedAboveWpm { get; init; } = -1;

    public SerialNumberRangeMode SerialNumberRange { get; init; }

    public int CustomSerialNumberMinimum { get; init; } = 1;

    public int CustomSerialNumberExclusiveMaximum { get; init; } = 99;

    public int CustomSerialNumberMinimumDigits { get; init; } = 2;

    public int CustomSerialNumberMaximumDigits { get; init; } = 2;

    public string HstOperatorName { get; init; } = string.Empty;

    public string? AudioOutputDeviceName { get; init; }

    public static SessionSettings CreateDefault(int seed)
    {
        return new(
            seed,
            ContestCatalog.All[0].Id,
            RunModeCatalog.All[1],
            DurationBlocks: 0);
    }
}

public sealed record SessionHandle(
    SessionId SessionId,
    Guid EngineEpoch,
    SessionState State,
    long Revision);

public sealed record EngineInfo(
    EngineId EngineId,
    string DisplayName,
    string SemanticVersion,
    Guid EngineEpoch,
    IReadOnlyList<string> Capabilities,
    bool IsInProcess)
{
    public string MinimumContractVersion { get; init; } = "1.0";

    public string MaximumContractVersion { get; init; } = "1.0";

    public string DiagnosticVersion { get; init; } = "0.1.0";
}

public sealed record SessionSnapshot(
    Guid EngineEpoch,
    SessionId SessionId,
    SessionState State,
    long Revision,
    long SimulationBlock,
    long RenderedSamples,
    TimeSpan ElapsedSimulationTime,
    int Seed,
    ContestId ContestId,
    RunModeId RunModeId,
    string? LastCaller,
    int QsoCount,
    int Score,
    string? LastError,
    int AudioQueuedBlocks = 0,
    long AudioUnderrunCount = 0,
    long AudioDroppedBlockCount = 0,
    bool AudioOutputHealthy = true,
    string? LastOperatorMessage = null,
    int CurrentWordsPerMinute = 30,
    int CurrentBandwidthHz = 500,
    int RitOffsetHz = 0,
    string? LastLoggedCall = null,
    OperatorState? ActiveOperatorState = null,
    int QsoRatePerHour = 0,
    IReadOnlyList<ActiveStationSnapshot>? ActiveStations = null,
    bool QsbEnabled = false,
    double CurrentMonitorLevelDb = 0d);

public sealed record AudioOutputDevice(
    string Name,
    int Index,
    bool IsDefault);

public sealed record ControlLease(
    string Token,
    SessionId SessionId,
    ClientId OwningClientId,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    long Revision);

public sealed record ControlLeaseSummary(
    ClientId OwningClientId,
    DateTimeOffset ExpiresAt,
    long Revision);

public sealed record SessionResult(
    SessionId SessionId,
    ContestId ContestId,
    int QsoCount,
    int Score,
    TimeSpan ElapsedSimulationTime,
    SessionState State,
    int QsoRatePerHour = 0);
