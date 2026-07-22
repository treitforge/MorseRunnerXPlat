using MorseRunner.Domain;

namespace MorseRunner.Engine;

public interface IAudioSink : IAsyncDisposable
{
    ValueTask InitializeAsync(
        SessionId sessionId,
        AudioStreamFormat format,
        CancellationToken cancellationToken);

    ValueTask WriteAsync(
        ReadOnlyMemory<float> samples,
        long simulationBlock,
        CancellationToken cancellationToken);

    ValueTask CompleteAsync(CancellationToken cancellationToken);
}

public interface IAudioSinkMetricsSource
{
    AudioSinkMetrics GetMetrics();
}

public interface IRecoverableAudioSink
{
    ValueTask RecoverAsync(
        string? deviceName,
        CancellationToken cancellationToken);
}

public readonly record struct AudioSinkMetrics(
    int QueuedBlocks,
    long UnderrunCount,
    long DroppedBlockCount,
    bool IsHealthy);

public readonly record struct AudioStreamFormat(
    int SampleRate,
    int Channels,
    int BlockSize)
{
    public static AudioStreamFormat Default =>
        new(
            SimulationAudioProfile.SampleRate,
            Channels: 1,
            SimulationAudioProfile.BlockSize);
}
