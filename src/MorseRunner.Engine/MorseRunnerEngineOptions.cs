namespace MorseRunner.Engine;

public sealed record MorseRunnerEngineOptions
{
    public bool AutomaticTiming { get; init; }

    public TimeSpan BlockPeriod { get; init; } = TimeSpan.FromSeconds(
        (double)Domain.CompatibilityProfile.BlockSize
        / Domain.CompatibilityProfile.SampleRate);
}
