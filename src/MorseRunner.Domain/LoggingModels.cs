namespace MorseRunner.Domain;

public enum LogError
{
    None = 0,
    Nil = 1,
    Duplicate = 2,
    Call = 3,
    Rst = 4,
    Name = 5,
    Class = 6,
    Number = 7,
    Section = 8,
    Qth = 9,
    Zone = 10,
    Society = 11,
    State = 12,
    Power = 13,
    Error = 14,
    Precedence = 15,
    Check = 16,
}

public sealed record Qso
{
    public DateTimeOffset Timestamp { get; init; }

    public string Call { get; init; } = string.Empty;

    public string TrueCall { get; init; } = string.Empty;

    public string RawCallsign { get; init; } = string.Empty;

    public int Rst { get; init; }

    public int TrueRst { get; init; }

    public int Number { get; init; }

    public int TrueNumber { get; init; }

    public string Precedence { get; init; } = string.Empty;

    public string TruePrecedence { get; init; } = string.Empty;

    public int Check { get; init; }

    public int TrueCheck { get; init; }

    public string Section { get; init; } = string.Empty;

    public string TrueSection { get; init; } = string.Empty;

    public string Exchange1 { get; init; } = string.Empty;

    public string TrueExchange1 { get; init; } = string.Empty;

    public string Exchange2 { get; init; } = string.Empty;

    public string TrueExchange2 { get; init; } = string.Empty;

    public string TrueWpm { get; init; } = string.Empty;

    public string Prefix { get; init; } = string.Empty;

    public string Multiplier { get; init; } = string.Empty;

    public int Points { get; init; }

    public bool IsDuplicate { get; init; }

    public bool AwaitingStationConfirmation { get; init; }

    public LogError ExchangeError { get; init; }

    public LogError Exchange1Error { get; init; }

    public LogError Exchange1SecondaryError { get; init; }

    public LogError Exchange2Error { get; init; }

    public LogError Exchange2SecondaryError { get; init; }

    public string ErrorText { get; init; } = string.Empty;

    public uint ColumnErrorFlags { get; init; }

    public Qso WithColumnError(int columnIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(columnIndex, 31);
        return this with
        {
            ColumnErrorFlags = ColumnErrorFlags | (1U << columnIndex),
        };
    }

    public bool HasColumnError(int columnIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(columnIndex, 31);
        return (ColumnErrorFlags & (1U << columnIndex)) != 0;
    }
}
