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
