using System.Buffers.Binary;
using MorseRunner.Audio;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.Engine.Tests;

public sealed class AudioSinkTests
{
    [Fact]
    public async Task WavSinkWritesDeterministicPcm16HeaderAndSamples()
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"morse-runner-{Guid.NewGuid():N}.wav");
        try
        {
            await using WavAudioSink sink = new(path);
            await sink.InitializeAsync(
                SessionId.New(),
                new AudioStreamFormat(11_025, Channels: 1, BlockSize: 4),
                TestContext.Current.CancellationToken);
            await sink.WriteAsync(
                new float[] { -1F, -0.5F, 0.5F, 1F },
                simulationBlock: 0,
                TestContext.Current.CancellationToken);
            await sink.CompleteAsync(TestContext.Current.CancellationToken);
            await sink.DisposeAsync();

            byte[] bytes = await File.ReadAllBytesAsync(
                path,
                TestContext.Current.CancellationToken);
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
            Assert.Equal(52, bytes.Length);
            Assert.Equal(8, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40, 4)));
            Assert.Equal(
                [Int16.MinValue, -16384, 16384, Int16.MaxValue],
                Enumerable.Range(0, 4)
                    .Select(
                        index => BinaryPrimitives.ReadInt16LittleEndian(
                            bytes.AsSpan(44 + index * 2, 2))));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
