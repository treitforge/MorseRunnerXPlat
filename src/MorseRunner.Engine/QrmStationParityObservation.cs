namespace MorseRunner.Engine;

internal sealed record QrmStationParityObservation(
    int ActiveCount,
    bool IsSending,
    string? MyCall,
    string? HisCall,
    float R1,
    float Amplitude,
    int PitchOffsetHz,
    int SendingWordsPerMinute,
    int CharacterWordsPerMinute,
    string? MessageSet,
    string? MessageText,
    int EnvelopeSampleCount,
    int SendPosition)
{
    public static QrmStationParityObservation Empty { get; } =
        new(
            0,
            false,
            null,
            null,
            0f,
            0f,
            0,
            0,
            0,
            null,
            null,
            0,
            0);
}
