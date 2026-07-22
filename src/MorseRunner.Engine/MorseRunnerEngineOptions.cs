namespace MorseRunner.Engine;

public sealed record MorseRunnerEngineOptions
{
    public bool AutomaticTiming { get; init; }

    public TimeSpan BlockPeriod { get; init; } = TimeSpan.FromSeconds(
        (double)Domain.SimulationAudioProfile.BlockSize
        / Domain.SimulationAudioProfile.SampleRate);
}
