using System.Buffers;
using System.Threading.Channels;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.Audio;

public sealed class BufferedWavAudioSink : IAudioSink
{
    private const int QueueCapacity = 128;
    private readonly WavAudioSink _wav;
    private readonly Channel<RecordedBlock> _blocks =
        Channel.CreateBounded<RecordedBlock>(
            new BoundedChannelOptions(QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            });
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _writer;
    private int _completed;

    public BufferedWavAudioSink(string path)
    {
        _wav = new WavAudioSink(path);
    }

    public string Path => _wav.Path;

    public async ValueTask InitializeAsync(
        SessionId sessionId,
        AudioStreamFormat format,
        CancellationToken cancellationToken)
    {
        await _wav.InitializeAsync(sessionId, format, cancellationToken);
        _writer = WriteBlocksAsync(_lifetime.Token);
    }

    public ValueTask WriteAsync(
        ReadOnlyMemory<float> samples,
        long simulationBlock,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_completed != 0, this);
        float[] buffer = ArrayPool<float>.Shared.Rent(samples.Length);
        samples.Span.CopyTo(buffer);
        if (!_blocks.Writer.TryWrite(
                new RecordedBlock(buffer, samples.Length, simulationBlock)))
        {
            ArrayPool<float>.Shared.Return(buffer);
            return ValueTask.FromException(
                new IOException(
                    "The WAV recording queue is full."));
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }

        _blocks.Writer.TryComplete();
        if (_writer is not null)
        {
            await _writer.WaitAsync(cancellationToken);
        }

        await _wav.CompleteAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await CompleteAsync(CancellationToken.None);
        }
        finally
        {
            await _lifetime.CancelAsync();
            _lifetime.Dispose();
            await _wav.DisposeAsync();
        }
    }

    private async Task WriteBlocksAsync(CancellationToken cancellationToken)
    {
        await foreach (RecordedBlock block in _blocks.Reader.ReadAllAsync(
            cancellationToken))
        {
            try
            {
                await _wav.WriteAsync(
                    block.Buffer.AsMemory(0, block.Length),
                    block.SimulationBlock,
                    cancellationToken);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(block.Buffer);
            }
        }
    }

    private sealed record RecordedBlock(
        float[] Buffer,
        int Length,
        long SimulationBlock);
}
