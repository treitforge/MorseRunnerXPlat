using System.Runtime.InteropServices;
using MiniAudioExNET;
using MorseRunner.Audio;

namespace MorseRunner.Audio.Tests;

public sealed class AudioBlockQueueTests
{
    [Fact]
    public void QueuePreservesBlocksAndRejectsOverflow()
    {
        var queue = new AudioBlockQueue(capacity: 2, blockSize: 4);

        Assert.True(queue.TryWrite([1f, 2f, 3f, 4f]));
        Assert.True(queue.TryWrite([5f, 6f, 7f, 8f]));
        Assert.False(queue.TryWrite([9f, 10f, 11f, 12f]));
        Assert.Equal(2, queue.Count);

        for (int expected = 1; expected <= 8; expected++)
        {
            Assert.True(queue.TryReadSample(out float sample));
            Assert.Equal(expected, sample);
        }

        Assert.False(queue.TryReadSample(out float silence));
        Assert.Equal(0f, silence);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void QueueSupportsPartialFinalBlock()
    {
        var queue = new AudioBlockQueue(capacity: 1, blockSize: 4);
        Assert.True(queue.TryWrite([0.25f, -0.5f]));

        Assert.True(queue.TryReadSample(out float first));
        Assert.True(queue.TryReadSample(out float second));
        Assert.False(queue.TryReadSample(out _));

        Assert.Equal(0.25f, first);
        Assert.Equal(-0.5f, second);
    }

    [Fact]
    public void CallbackWritesQueuedSamplesAndThenExplicitSilence()
    {
        var queue = new AudioBlockQueue(capacity: 1, blockSize: 4);
        Assert.True(queue.TryWrite([0.25f, -0.5f]));
        IntPtr memory = Marshal.AllocHGlobal(4 * sizeof(float));
        try
        {
            var output = new AudioBuffer<float>(memory, 4);
            bool complete = queue.FillInterleaved(
                output,
                frameCount: 4,
                channels: 1);

            Assert.False(complete);
            Assert.Equal(0.25f, output[0]);
            Assert.Equal(-0.5f, output[1]);
            Assert.Equal(0f, output[2]);
            Assert.Equal(0f, output[3]);
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    [Fact]
    public void CallbackPreservesContinuousSamplesAcrossBlockBoundaries()
    {
        const int blockSize = 512;
        var queue = new AudioBlockQueue(capacity: 2, blockSize);
        var expected = new float[blockSize * 2];
        for (int index = 0; index < expected.Length; index++)
        {
            expected[index] = MathF.Sin(
                2f * MathF.PI * 612.5f * index / 11_025f);
        }

        Assert.True(queue.TryWrite(expected.AsSpan(0, blockSize)));
        Assert.True(queue.TryWrite(expected.AsSpan(blockSize, blockSize)));
        IntPtr memory = Marshal.AllocHGlobal(expected.Length * sizeof(float));
        try
        {
            var output = new AudioBuffer<float>(memory, expected.Length);
            bool complete = queue.FillInterleaved(
                output,
                frameCount: (ulong)expected.Length,
                channels: 1);

            Assert.True(complete);
            for (int index = 0; index < expected.Length; index++)
            {
                Assert.Equal(expected[index], output[index]);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    [Fact]
    public void DeviceClockConsumesExactlySixtySecondsWithoutUnderrun()
    {
        const int sampleRate = 11_025;
        const int blockSize = 512;
        var queue = new AudioBlockQueue(capacity: 4, blockSize);
        var block = new float[blockSize];
        long producedSamples = 0;
        long consumedSamples = 0;
        long underruns = 0;

        WriteBlock();
        WriteBlock();
        while (consumedSamples < sampleRate * 60L)
        {
            if (queue.Count < 2)
            {
                WriteBlock();
            }

            if (!queue.TryReadSample(out _))
            {
                underruns++;
            }

            consumedSamples++;
        }

        Assert.Equal(sampleRate * 60L, consumedSamples);
        Assert.Equal(0, underruns);
        Assert.InRange(
            producedSamples - consumedSamples,
            0,
            blockSize * 2L);
        return;

        void WriteBlock()
        {
            Assert.True(queue.TryWrite(block));
            producedSamples += blockSize;
        }
    }
}
