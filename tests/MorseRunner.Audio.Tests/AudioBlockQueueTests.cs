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
}
