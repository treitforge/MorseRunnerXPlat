using MorseRunner.Domain;

namespace MorseRunner.Engine;

internal sealed record CallerCandidateParityObservation(
    int Attempt,
    StationIdentity Identity,
    float R1,
    int SendingWordsPerMinute,
    int CharacterWordsPerMinute,
    int Skills,
    int Patience,
    OperatorState OperatorState,
    float Amplitude,
    int PitchOffsetHz);

internal sealed record CallerCollisionParityObservation(
    QrmStationParityObservation Qrm,
    IReadOnlyList<CallerCandidateParityObservation> Candidates,
    int IdentitySelectionCount,
    int AcceptedAttempt,
    ActiveStationSnapshot? AcceptedCaller,
    string AcceptedOperatorName,
    string AcceptedUserText,
    int DuplicateActiveCallsignCount,
    float TerminalRandom);
