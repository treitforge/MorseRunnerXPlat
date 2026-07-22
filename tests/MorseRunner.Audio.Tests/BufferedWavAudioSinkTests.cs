using System.Buffers.Binary;
using MorseRunner.Audio;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.Audio.Tests;

public sealed class BufferedWavAudioSinkTests
{
    [Fact]
    public async Task FullRecordingQueueDropsOnlyTheRecordingBlock()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"MorseRunnerXPlat-{Guid.NewGuid():N}.wav");
        var writerStart = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            CancellationToken cancellationToken =
                TestContext.Current.CancellationToken;
            await using var sink = new BufferedWavAudioSink(
                path,
                queueCapacity: 1,
                writerStart.Task);
            await sink.InitializeAsync(
                SessionId.New(),
                AudioStreamFormat.Default,
                cancellationToken);
            float[] samples = new float[SimulationAudioProfile.BlockSize];

            await sink.WriteAsync(samples, 0, cancellationToken);
            await sink.WriteAsync(samples, 1, cancellationToken);

            AudioSinkMetrics metrics = sink.GetMetrics();
            Assert.Equal(1, metrics.QueuedBlocks);
            Assert.Equal(1, metrics.DroppedBlockCount);

            writerStart.SetResult();
            await sink.CompleteAsync(cancellationToken);
        }
        finally
        {
            writerStart.TrySetResult();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task BufferedSinkWritesACompletePcm16WaveFile()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"MorseRunnerXPlat-{Guid.NewGuid():N}.wav");
        try
        {
            CancellationToken cancellationToken =
                TestContext.Current.CancellationToken;
            await using var sink = new BufferedWavAudioSink(path);
            await sink.InitializeAsync(
                SessionId.New(),
                AudioStreamFormat.Default,
                cancellationToken);
            float[] samples = Enumerable
                .Range(0, SimulationAudioProfile.BlockSize)
                .Select(index => index % 2 == 0 ? 0.5F : -0.5F)
                .ToArray();

            await sink.WriteAsync(samples, 0, cancellationToken);
            await sink.WriteAsync(samples, 1, cancellationToken);
            await sink.CompleteAsync(cancellationToken);

            byte[] file = await File.ReadAllBytesAsync(path, cancellationToken);
            Assert.Equal("RIFF"u8.ToArray(), file[..4]);
            Assert.Equal("WAVE"u8.ToArray(), file[8..12]);
            Assert.Equal("data"u8.ToArray(), file[36..40]);
            Assert.Equal(
                samples.Length * 2 * sizeof(short),
                BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(40, 4)));
            Assert.Equal(44 + (samples.Length * 2 * sizeof(short)), file.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
