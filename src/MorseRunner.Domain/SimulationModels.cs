namespace MorseRunner.Domain;

public enum OperatorState
{
    NeedPreviousEnd = 0,
    NeedQso = 1,
    NeedNumber = 2,
    NeedCall = 3,
    NeedCallAndNumber = 4,
    NeedEnd = 5,
    Done = 6,
    Failed = 7,
}

public enum CallMatch
{
    No = 0,
    Yes = 1,
    Almost = 2,
}

public enum OperatorRunMode
{
    Stop = 0,
    Pileup = 1,
    SingleCall = 2,
    Wpx = 3,
    Hst = 4,
}

public enum StationState
{
    Listening = 0,
    Copying = 1,
    PreparingToSend = 2,
    Sending = 3,
}

public enum StationReply
{
    None = 0,
    MyCall = 1,
    NumberQuestion = 2,
    Again = 3,
    DeMyCall = 4,
    DeMyCallTwice = 5,
    MyCallTwice = 6,
    DeMyCallAndNumber = 7,
    DeMyCallTwiceAndNumber = 8,
    MyCallAndNumber = 9,
    MyCallTwiceAndNumber = 10,
    Number = 11,
    RogerNumber = 12,
    RogerNumberTwice = 13,
}

[Flags]
public enum StationMessage
{
    None = 0,
    Cq = 1 << 0,
    Number = 1 << 1,
    ThankYou = 1 << 2,
    MyCall = 1 << 3,
    HisCall = 1 << 4,
    Before = 1 << 5,
    Question = 1 << 6,
    Nil = 1 << 7,
    Garbage = 1 << 8,
}

public sealed record ActiveStationSnapshot(
    string Callsign,
    StationState StationState,
    OperatorState OperatorState,
    int Patience,
    int RepeatCount,
    int WordsPerMinute,
    int PitchOffsetHz,
    string TrueRst,
    string TrueExchange1,
    string TrueExchange2,
    string? LastReply);
