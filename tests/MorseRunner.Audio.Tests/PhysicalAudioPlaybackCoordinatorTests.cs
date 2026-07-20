using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using MiniAudioExNET;
using MorseRunner.Audio;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.Audio.Tests;

public sealed class PhysicalAudioPlaybackCoordinatorTests
{
    [Fact]
    public void ObservedFramingAdvancesOnlyAsFramesAreConsumed()
    {
        var coordinator = new PhysicalAudioPlaybackCoordinator(
            canonicalBlockSize: 4);
        var queue = new AudioBlockQueue(capacity: 1, blockSize: 4);

        AssertObservedFraming(coordinator, expectedCount: 0);
        for (int expectedCount = 1;
             expectedCount <= CompatibilityProfile.AudioStartupRequestCount;
             expectedCount++)
        {
            CallbackResult result = Read(
                coordinator,
                queue,
                frameCount: 1);

            Assert.True(result.Complete);
            Assert.Equal(
                0U,
                BitConverter.SingleToUInt32Bits(
                    Assert.Single(result.Samples)));
            AssertObservedFraming(coordinator, expectedCount);
        }

        CallbackResult underrun = Read(
            coordinator,
            queue,
            frameCount: 1);

        Assert.False(underrun.Complete);
        AssertObservedFraming(
            coordinator,
            CompatibilityProfile.AudioStartupRequestCount);
    }

    [Fact]
    public void FirstFillCallDoesNotAllocateWithoutWarmup()
    {
        var coordinator = new PhysicalAudioPlaybackCoordinator(
            canonicalBlockSize: 4);
        var queue = new AudioBlockQueue(capacity: 1, blockSize: 4);
        Assert.True(queue.TryWrite([1f, 2f, 3f, 4f]));
        IntPtr memory = Marshal.AllocHGlobal(6 * sizeof(float));
        try
        {
            var output = new AudioBuffer<float>(memory, 6);

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            bool complete = coordinator.FillInterleaved(
                queue,
                output,
                frameCount: 6,
                channels: 1);
            long allocatedBytes =
                GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Assert.True(complete);
            Assert.Equal(0, allocatedBytes);
            Assert.Equal(0U, BitConverter.SingleToUInt32Bits(output[0]));
            Assert.Equal(1f, output[5]);
            AssertObservedFraming(coordinator, expectedCount: 5);
            Assert.Equal(1, coordinator.StagedCanonicalBlockCount);
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    [Fact]
    public void CallbackFlattensStartupFramesBeforeCanonicalAudio()
    {
        var coordinator = new PhysicalAudioPlaybackCoordinator(
            canonicalBlockSize: 4);
        var queue = new AudioBlockQueue(capacity: 2, blockSize: 4);
        Assert.True(queue.TryWrite([1f, -2f, 3f, -4f]));

        CallbackResult result = Read(
            coordinator,
            queue,
            frameCount: 9);

        Assert.True(result.Complete);
        Assert.Equal(
            [0f, 0f, 0f, 0f, 0f, 1f, -2f, 3f, -4f],
            result.Samples);
        Assert.All(
            result.Samples[..5],
            sample => Assert.Equal(
                0U,
                BitConverter.SingleToUInt32Bits(sample)));
        AssertObservedFraming(
            coordinator,
            CompatibilityProfile.AudioStartupRequestCount);
        Assert.Equal(0, queue.Count);
        Assert.Equal(0, coordinator.StagedCanonicalBlockCount);
    }

    [Fact]
    public void PartialCallbacksPreserveObservedAndCanonicalBoundaries()
    {
        var coordinator = new PhysicalAudioPlaybackCoordinator(
            canonicalBlockSize: 4);
        var queue = new AudioBlockQueue(capacity: 1, blockSize: 4);
        Assert.True(queue.TryWrite([10f, 20f, 30f, 40f]));

        CallbackResult first = Read(
            coordinator,
            queue,
            frameCount: 2);
        Assert.True(first.Complete);
        Assert.Equal([0f, 0f], first.Samples);
        AssertObservedFraming(coordinator, expectedCount: 2);
        Assert.Equal(0, queue.Count);
        Assert.Equal(1, coordinator.StagedCanonicalBlockCount);

        CallbackResult second = Read(
            coordinator,
            queue,
            frameCount: 2);
        Assert.True(second.Complete);
        Assert.Equal([0f, 0f], second.Samples);
        AssertObservedFraming(coordinator, expectedCount: 4);

        CallbackResult crossing = Read(
            coordinator,
            queue,
            frameCount: 3);
        Assert.True(crossing.Complete);
        Assert.Equal([0f, 10f, 20f], crossing.Samples);
        AssertObservedFraming(coordinator, expectedCount: 5);
        Assert.Equal(1, coordinator.StagedCanonicalBlockCount);

        CallbackResult remaining = Read(
            coordinator,
            queue,
            frameCount: 2);
        Assert.True(remaining.Complete);
        Assert.Equal([30f, 40f], remaining.Samples);
        Assert.Equal(0, coordinator.StagedCanonicalBlockCount);
    }

    [Fact]
    public void RecoveryDiscardPreservesConsumedPrefixWithoutReplay()
    {
        var coordinator = new PhysicalAudioPlaybackCoordinator(
            canonicalBlockSize: 2);
        var initialQueue = new AudioBlockQueue(capacity: 1, blockSize: 2);
        Assert.True(initialQueue.TryWrite([7f, 8f]));

        CallbackResult beforeRecovery = Read(
            coordinator,
            initialQueue,
            frameCount: 4);

        Assert.True(beforeRecovery.Complete);
        Assert.Equal([0f, 0f, 0f, 0f], beforeRecovery.Samples);
        AssertObservedFraming(coordinator, expectedCount: 4);
        Assert.Equal(1, coordinator.StagedCanonicalBlockCount);

        coordinator.DiscardCanonicalStaging();
        var recoveredQueue =
            new AudioBlockQueue(capacity: 1, blockSize: 2);
        Assert.True(recoveredQueue.TryWrite([9f, 10f]));

        CallbackResult afterRecovery = Read(
            coordinator,
            recoveredQueue,
            frameCount: 3);

        Assert.True(afterRecovery.Complete);
        Assert.Equal([0f, 9f, 10f], afterRecovery.Samples);
        AssertObservedFraming(coordinator, expectedCount: 5);
        Assert.Equal(0, coordinator.StagedCanonicalBlockCount);
    }

    [Fact]
    public void NewCoordinatorStartsWithFreshUnobservedPrefix()
    {
        var firstCoordinator = new PhysicalAudioPlaybackCoordinator(
            canonicalBlockSize: 1);
        var queue = new AudioBlockQueue(capacity: 1, blockSize: 1);
        Read(
            firstCoordinator,
            queue,
            frameCount: CompatibilityProfile.AudioStartupRequestCount);
        AssertObservedFraming(firstCoordinator, expectedCount: 5);

        var freshCoordinator = new PhysicalAudioPlaybackCoordinator(
            canonicalBlockSize: 1);

        AssertObservedFraming(freshCoordinator, expectedCount: 0);
        CallbackResult firstFrame = Read(
            freshCoordinator,
            queue,
            frameCount: 1);
        Assert.True(firstFrame.Complete);
        AssertObservedFraming(freshCoordinator, expectedCount: 1);
    }

    [Fact]
    public void CapacityOneProducerCanWriteAfterPrefixCrossing()
    {
        var coordinator = new PhysicalAudioPlaybackCoordinator(
            canonicalBlockSize: 4);
        var queue = new AudioBlockQueue(capacity: 1, blockSize: 4);
        Assert.True(queue.TryWrite([1f, 2f, 3f, 4f]));

        CallbackResult crossing = Read(
            coordinator,
            queue,
            frameCount: 6);

        Assert.True(crossing.Complete);
        Assert.Equal([0f, 0f, 0f, 0f, 0f, 1f], crossing.Samples);
        Assert.Equal(0, queue.Count);
        Assert.Equal(1, coordinator.StagedCanonicalBlockCount);
        Assert.True(queue.TryWrite([5f, 6f, 7f, 8f]));
        Assert.Equal(
            2,
            queue.Count + coordinator.StagedCanonicalBlockCount);

        CallbackResult remainder = Read(
            coordinator,
            queue,
            frameCount: 7);

        Assert.True(remainder.Complete);
        Assert.Equal([2f, 3f, 4f, 5f, 6f, 7f, 8f], remainder.Samples);
        Assert.Equal(0, queue.Count);
        Assert.Equal(0, coordinator.StagedCanonicalBlockCount);
    }

    [Fact]
    public void CapacityTwoProducerCanWriteAfterPrefixCrossing()
    {
        var coordinator = new PhysicalAudioPlaybackCoordinator(
            canonicalBlockSize: 4);
        var queue = new AudioBlockQueue(capacity: 2, blockSize: 4);
        Assert.True(queue.TryWrite([1f, 2f, 3f, 4f]));
        Assert.True(queue.TryWrite([5f, 6f, 7f, 8f]));

        CallbackResult crossing = Read(
            coordinator,
            queue,
            frameCount: 6);

        Assert.True(crossing.Complete);
        Assert.Equal([0f, 0f, 0f, 0f, 0f, 1f], crossing.Samples);
        Assert.Equal(1, queue.Count);
        Assert.Equal(1, coordinator.StagedCanonicalBlockCount);
        Assert.True(queue.TryWrite([9f, 10f, 11f, 12f]));
        Assert.Equal(
            3,
            queue.Count + coordinator.StagedCanonicalBlockCount);

        CallbackResult remainder = Read(
            coordinator,
            queue,
            frameCount: 11);

        Assert.True(remainder.Complete);
        Assert.Equal(
            [2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f, 11f, 12f],
            remainder.Samples);
        Assert.Equal(0, queue.Count);
        Assert.Equal(0, coordinator.StagedCanonicalBlockCount);
    }

    [Fact]
    public void UnderrunBeginsOnlyAfterPrefixAndCanonicalAudioAreEmpty()
    {
        var coordinator = new PhysicalAudioPlaybackCoordinator(
            canonicalBlockSize: 2);
        var queue = new AudioBlockQueue(capacity: 1, blockSize: 2);
        Assert.True(queue.TryWrite([0.25f, -0.5f]));

        CallbackResult available = Read(
            coordinator,
            queue,
            frameCount: 7);
        CallbackResult underrun = Read(
            coordinator,
            queue,
            frameCount: 1);

        Assert.True(available.Complete);
        Assert.Equal(
            [0f, 0f, 0f, 0f, 0f, 0.25f, -0.5f],
            available.Samples);
        Assert.False(underrun.Complete);
        Assert.Equal(
            0U,
            BitConverter.SingleToUInt32Bits(
                Assert.Single(underrun.Samples)));
        Assert.Equal(0, queue.Count);
        Assert.Equal(0, coordinator.StagedCanonicalBlockCount);
    }

    [Fact]
    public void CallbackCrossingIntoMissingCanonicalAudioReportsUnderrun()
    {
        var coordinator = new PhysicalAudioPlaybackCoordinator(
            canonicalBlockSize: 1);
        var queue = new AudioBlockQueue(capacity: 1, blockSize: 1);
        Assert.True(queue.TryWrite([0.75f]));

        CallbackResult result = Read(
            coordinator,
            queue,
            frameCount: 7);

        Assert.False(result.Complete);
        Assert.Equal(
            [0f, 0f, 0f, 0f, 0f, 0.75f, 0f],
            result.Samples);
        AssertObservedFraming(coordinator, expectedCount: 5);
    }

    [Fact]
    public void CallbackInterleavesEachLogicalSampleAcrossChannels()
    {
        var coordinator = new PhysicalAudioPlaybackCoordinator(
            canonicalBlockSize: 1);
        var queue = new AudioBlockQueue(capacity: 1, blockSize: 1);
        Assert.True(queue.TryWrite([0.5f]));

        CallbackResult result = Read(
            coordinator,
            queue,
            frameCount: 6,
            channels: 2);

        Assert.True(result.Complete);
        Assert.Equal(
            [
                0f, 0f,
                0f, 0f,
                0f, 0f,
                0f, 0f,
                0f, 0f,
                0.5f, 0.5f,
            ],
            result.Samples);
    }

    [Fact]
    public async Task
        DeviceFreeSinkSeamExecutesCoordinatorAndReportsObservedFrames()
    {
        var sink = new PhysicalAudioSink();
        var canonicalBlock =
            new float[CompatibilityProfile.BlockSize];
        for (int index = 0; index < canonicalBlock.Length; index++)
        {
            canonicalBlock[index] = index + 1;
        }

        CallbackResult result = ReadFromSink(
            sink,
            canonicalBlock,
            frameCount:
                CompatibilityProfile.AudioStartupRequestCount
                + CompatibilityProfile.BlockSize);

        Assert.True(result.Complete);
        for (int index = 0;
             index < CompatibilityProfile.AudioStartupRequestCount;
             index++)
        {
            Assert.Equal(
                0U,
                BitConverter.SingleToUInt32Bits(result.Samples[index]));
        }

        Assert.True(
            result.Samples[
                CompatibilityProfile.AudioStartupRequestCount..]
                .AsSpan()
                .SequenceEqual(canonicalBlock));
        PhysicalAudioSinkDiagnostics diagnostics = sink.GetDiagnostics();
        Assert.Equal(PhysicalAudioSinkState.Created, diagnostics.State);
        AssertObservedFraming(
            diagnostics.StartupFraming,
            CompatibilityProfile.AudioStartupRequestCount);
        Assert.Equal(0, diagnostics.QueuedBlocks);
        Assert.Equal(0, diagnostics.CallbackCount);
        Assert.Equal(-1, diagnostics.LastSimulationBlock);
        await sink.DisposeAsync();
    }

    [Fact]
    public async Task DeviceFreeSinkSeamRejectsNonCreatedSinkStates()
    {
        var canonicalBlock =
            new float[CompatibilityProfile.BlockSize];
        IntPtr memory = Marshal.AllocHGlobal(sizeof(float));
        try
        {
            var completedSink = new PhysicalAudioSink();
            await completedSink.CompleteAsync(
                TestContext.Current.CancellationToken);

            Assert.IsType<InvalidOperationException>(
                CaptureDeviceFreeFillException(
                    completedSink,
                    canonicalBlock,
                    memory));

            await completedSink.DisposeAsync();
            Assert.IsType<ObjectDisposedException>(
                CaptureDeviceFreeFillException(
                    completedSink,
                    canonicalBlock,
                    memory));

            await using var attemptedSink = new PhysicalAudioSink();
            SetField(
                attemptedSink,
                "_physicalInitializationAttempted",
                1);
            Assert.IsType<InvalidOperationException>(
                CaptureDeviceFreeFillException(
                    attemptedSink,
                    canonicalBlock,
                    memory));

            await using var runningSink = new PhysicalAudioSink();
            SetField(
                runningSink,
                "_state",
                (int)PhysicalAudioSinkState.Running);
            Assert.IsType<InvalidOperationException>(
                CaptureDeviceFreeFillException(
                    runningSink,
                    canonicalBlock,
                    memory));
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    [Fact]
    public async Task DeviceFreeSinkSeamRejectsInvalidBlocksAndOverflow()
    {
        await using var sink = new PhysicalAudioSink(
            new PhysicalAudioSinkOptions(QueueDepth: 1));
        var canonicalBlock =
            new float[CompatibilityProfile.BlockSize];
        IntPtr memory = Marshal.AllocHGlobal(sizeof(float));
        try
        {
            var output = new AudioBuffer<float>(memory, 1);
            Assert.IsType<ArgumentOutOfRangeException>(
                CaptureDeviceFreeFillException(
                    sink,
                    canonicalBlock,
                    memory,
                    canonicalOffset: 1));

            Assert.True(
                sink.FillDeviceFreeDiagnostics(
                    canonicalBlock,
                    output,
                    frameCount: 0,
                    channels: 1));
            Assert.IsType<InvalidOperationException>(
                CaptureDeviceFreeFillException(
                    sink,
                    canonicalBlock,
                    memory,
                    frameCount: 0));
            Assert.Empty(sink.GetDiagnostics().StartupFraming);
            Assert.Equal(1, sink.GetDiagnostics().QueuedBlocks);
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    [Fact]
    public async Task DiagnosticsReportObservedAndStagedStateThroughDisposal()
    {
        var coordinator = new PhysicalAudioPlaybackCoordinator(
            canonicalBlockSize: 4);
        var queue = new AudioBlockQueue(capacity: 1, blockSize: 4);
        var sink = new PhysicalAudioSink();

        PhysicalAudioSinkDiagnostics created = sink.GetDiagnostics();
        Assert.Equal(PhysicalAudioSinkState.Created, created.State);
        Assert.Empty(created.StartupFraming);
        Assert.False(created.StartupFraming.IsDefault);
        Assert.Equal(0, created.QueuedBlocks);

        Assert.True(queue.TryWrite([1f, 2f, 3f, 4f]));
        Read(coordinator, queue, frameCount: 2);
        SetField(sink, "_playbackCoordinator", coordinator);
        SetField(sink, "_queue", queue);

        PhysicalAudioSinkDiagnostics runningEvidence =
            sink.GetDiagnostics();
        AssertObservedFraming(
            runningEvidence.StartupFraming,
            expectedCount: 2);
        Assert.Equal(1, runningEvidence.QueuedBlocks);
        Assert.True(queue.TryWrite([5f, 6f, 7f, 8f]));
        Assert.Equal(2, sink.GetDiagnostics().QueuedBlocks);

        await sink.CompleteAsync(TestContext.Current.CancellationToken);
        PhysicalAudioSinkDiagnostics completed = sink.GetDiagnostics();
        Assert.Equal(PhysicalAudioSinkState.Completed, completed.State);
        Assert.Equal(0, completed.QueuedBlocks);
        AssertObservedFraming(
            completed.StartupFraming,
            expectedCount: 2);

        await sink.DisposeAsync();
        PhysicalAudioSinkDiagnostics disposed = sink.GetDiagnostics();
        Assert.Equal(PhysicalAudioSinkState.Disposed, disposed.State);
        Assert.Equal(0, disposed.QueuedBlocks);
        AssertObservedFraming(
            disposed.StartupFraming,
            expectedCount: 2);
    }

    [Fact]
    public async Task
        SinkPlaybackStatePreservesPrefixForRecoveryAndResetsForNewSession()
    {
        var sink = new PhysicalAudioSink();
        SessionId sessionId = SessionId.New();
        PhysicalAudioPlaybackCoordinator initialCoordinator =
            PreparePlaybackState(
                sink,
                sessionId,
                AudioStreamFormat.Compatibility);
        AudioBlockQueue initialQueue =
            GetField<AudioBlockQueue>(sink, "_queue");
        Assert.True(initialQueue.TryWrite(
            new float[CompatibilityProfile.BlockSize]));
        Read(initialCoordinator, initialQueue, frameCount: 2);
        AssertObservedFraming(initialCoordinator, expectedCount: 2);
        Assert.Equal(1, initialCoordinator.StagedCanonicalBlockCount);

        PhysicalAudioPlaybackCoordinator recoveredCoordinator =
            PreparePlaybackState(
                sink,
                sessionId,
                AudioStreamFormat.Compatibility);

        Assert.Same(initialCoordinator, recoveredCoordinator);
        AssertObservedFraming(recoveredCoordinator, expectedCount: 2);
        Assert.Equal(0, recoveredCoordinator.StagedCanonicalBlockCount);
        AudioBlockQueue recoveredQueue =
            GetField<AudioBlockQueue>(sink, "_queue");
        Assert.NotSame(initialQueue, recoveredQueue);
        CallbackResult remainingPrefix = Read(
            recoveredCoordinator,
            recoveredQueue,
            frameCount: 3);
        Assert.True(remainingPrefix.Complete);
        Assert.Equal([0f, 0f, 0f], remainingPrefix.Samples);
        AssertObservedFraming(recoveredCoordinator, expectedCount: 5);

        PhysicalAudioPlaybackCoordinator freshCoordinator =
            PreparePlaybackState(
                sink,
                SessionId.New(),
                AudioStreamFormat.Compatibility);

        Assert.NotSame(recoveredCoordinator, freshCoordinator);
        AssertObservedFraming(freshCoordinator, expectedCount: 0);
        await sink.DisposeAsync();
    }

    [Fact]
    public async Task RecoveryFaultBaselineKeepsCumulativeDiagnostics()
    {
        await using var sink = new PhysicalAudioSink();
        SetField(
            sink,
            "_state",
            (int)PhysicalAudioSinkState.Running);
        SetField(
            sink,
            "_lastCallbackTimestamp",
            Stopwatch.GetTimestamp());
        SetField(sink, "_callbackFaultCount", 2L);
        SetField(sink, "_callbackFaultGenerationBaseline", 2L);

        Assert.Equal(2, sink.GetDiagnostics().CallbackFaultCount);
        Assert.True(sink.GetMetrics().IsHealthy);

        SetField(sink, "_callbackFaultCount", 3L);

        Assert.Equal(3, sink.GetDiagnostics().CallbackFaultCount);
        Assert.False(sink.GetMetrics().IsHealthy);

        SetField(sink, "_callbackFaultGenerationBaseline", 3L);

        Assert.Equal(3, sink.GetDiagnostics().CallbackFaultCount);
        Assert.True(sink.GetMetrics().IsHealthy);
    }

    private static void AssertObservedFraming(
        PhysicalAudioPlaybackCoordinator coordinator,
        int expectedCount)
    {
        AssertObservedFraming(
            coordinator.GetObservedStartupFraming(),
            expectedCount);
    }

    private static void AssertObservedFraming(
        ImmutableArray<PhysicalAudioSinkStartupFrame> framing,
        int expectedCount)
    {
        Assert.False(framing.IsDefault);
        Assert.Equal(expectedCount, framing.Length);
        for (int index = 0; index < framing.Length; index++)
        {
            PhysicalAudioSinkStartupFrame frame = framing[index];
            Assert.Equal(index + 1, frame.LogicalRequestNumber);
            Assert.Equal(
                index
                    < CompatibilityProfile
                        .AudioStartupPrefillRequestCount,
                frame.IsSynchronousPrefill);
            Assert.False(frame.Samples.IsDefault);
            float sample = Assert.Single(frame.Samples);
            Assert.Equal(
                0U,
                BitConverter.SingleToUInt32Bits(sample));
        }
    }

    private static CallbackResult Read(
        PhysicalAudioPlaybackCoordinator coordinator,
        AudioBlockQueue queue,
        int frameCount,
        int channels = 1)
    {
        int sampleCount = checked(frameCount * channels);
        IntPtr memory = Marshal.AllocHGlobal(
            checked(sampleCount * sizeof(float)));
        try
        {
            var output = new AudioBuffer<float>(memory, sampleCount);
            bool complete = coordinator.FillInterleaved(
                queue,
                output,
                checked((ulong)frameCount),
                channels);
            var samples = new float[sampleCount];
            for (int index = 0; index < samples.Length; index++)
            {
                samples[index] = output[index];
            }

            return new CallbackResult(complete, samples);
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    private static CallbackResult ReadFromSink(
        PhysicalAudioSink sink,
        ReadOnlySpan<float> canonicalBlock,
        int frameCount,
        int channels = 1)
    {
        int sampleCount = checked(frameCount * channels);
        IntPtr memory = Marshal.AllocHGlobal(
            checked(sampleCount * sizeof(float)));
        try
        {
            var output = new AudioBuffer<float>(memory, sampleCount);
            bool complete = sink.FillDeviceFreeDiagnostics(
                canonicalBlock,
                output,
                checked((ulong)frameCount),
                channels);
            var samples = new float[sampleCount];
            for (int index = 0; index < samples.Length; index++)
            {
                samples[index] = output[index];
            }

            return new CallbackResult(complete, samples);
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    private static Exception? CaptureDeviceFreeFillException(
        PhysicalAudioSink sink,
        float[] canonicalBlock,
        IntPtr outputMemory,
        ulong frameCount = 1,
        int canonicalOffset = 0)
    {
        try
        {
            var output = new AudioBuffer<float>(outputMemory, 1);
            sink.FillDeviceFreeDiagnostics(
                canonicalBlock.AsSpan(canonicalOffset),
                output,
                frameCount,
                channels: 1);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static void SetField<T>(
        PhysicalAudioSink sink,
        string fieldName,
        T value)
    {
        GetRequiredField(fieldName).SetValue(sink, value);
    }

    private static T GetField<T>(
        PhysicalAudioSink sink,
        string fieldName)
        where T : class
    {
        object? value = GetRequiredField(fieldName).GetValue(sink);
        Assert.IsType<T>(value);
        return (T)value;
    }

    private static PhysicalAudioPlaybackCoordinator PreparePlaybackState(
        PhysicalAudioSink sink,
        SessionId sessionId,
        AudioStreamFormat format)
    {
        MethodInfo? method = typeof(PhysicalAudioSink).GetMethod(
            "PreparePlaybackState",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        object? result = method.Invoke(sink, [sessionId, format]);
        Assert.IsType<PhysicalAudioPlaybackCoordinator>(result);
        return (PhysicalAudioPlaybackCoordinator)result;
    }

    private static FieldInfo GetRequiredField(string fieldName)
    {
        FieldInfo? field = typeof(PhysicalAudioSink).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field;
    }

    private sealed record CallbackResult(
        bool Complete,
        float[] Samples);
}
