namespace MorseRunner.Engine;

internal sealed record QrnBurstParityObservation(
    int ActiveCount,
    bool IsSending,
    int EnvelopeSampleCount)
{
    public static QrnBurstParityObservation Empty { get; } =
        new(0, false, 0);
}
