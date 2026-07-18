using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.Audio;

public sealed class CompositeAudioSink(
    IAudioSink primary,
    IAudioSink secondary) :
    IAudioSink,
    IAudioSinkMetricsSource,
    IRecoverableAudioSink
{
    private readonly IAudioSink _primary =
        primary ?? throw new ArgumentNullException(nameof(primary));
    private readonly IAudioSink _secondary =
        secondary ?? throw new ArgumentNullException(nameof(secondary));

    public async ValueTask InitializeAsync(
        SessionId sessionId,
        AudioStreamFormat format,
        CancellationToken cancellationToken)
    {
        await _primary.InitializeAsync(sessionId, format, cancellationToken);
        await _secondary.InitializeAsync(sessionId, format, cancellationToken);
    }

    public async ValueTask WriteAsync(
        ReadOnlyMemory<float> samples,
        long simulationBlock,
        CancellationToken cancellationToken)
    {
        await _primary.WriteAsync(
            samples,
            simulationBlock,
            cancellationToken);
        await _secondary.WriteAsync(
            samples,
            simulationBlock,
            cancellationToken);
    }

    public async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        await _secondary.CompleteAsync(cancellationToken);
        await _primary.CompleteAsync(cancellationToken);
    }

    public AudioSinkMetrics GetMetrics() =>
        _primary is IAudioSinkMetricsSource metrics
            ? metrics.GetMetrics()
            : new(0, 0, 0, true);

    public ValueTask RecoverAsync(
        string? deviceName,
        CancellationToken cancellationToken) =>
        _primary is IRecoverableAudioSink recoverable
            ? recoverable.RecoverAsync(deviceName, cancellationToken)
            : ValueTask.FromException(
                new NotSupportedException(
                    "The primary audio sink cannot recover a device."));

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _secondary.DisposeAsync();
        }
        finally
        {
            await _primary.DisposeAsync();
        }
    }
}
