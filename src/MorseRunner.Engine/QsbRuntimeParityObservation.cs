namespace MorseRunner.Engine;

internal sealed record QsbRuntimeParityObservation(
    IReadOnlyList<float[]> Blocks,
    float TerminalRandom);
