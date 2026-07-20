using System.Collections.Immutable;
using MiniAudioExNET;
using MorseRunner.Domain;

namespace MorseRunner.Audio;

internal sealed class PhysicalAudioPlaybackCoordinator
{
    private static readonly
        ImmutableArray<PhysicalAudioSinkStartupFrame> PlannedStartupFraming;

    private readonly float[] _stagedCanonicalBlock;
    private int _startupFrameIndex;
    private int _startupSampleIndex;
    private int _completedStartupFrameCount;
    private int _stagedCanonicalLength;
    private int _stagedCanonicalOffset;
    private int _hasStagedCanonicalBlock;

    static PhysicalAudioPlaybackCoordinator()
    {
        PlannedStartupFraming = CreatePlannedStartupFraming();
    }

    public PhysicalAudioPlaybackCoordinator(int canonicalBlockSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            canonicalBlockSize);
        _stagedCanonicalBlock = new float[canonicalBlockSize];
    }

    public int StagedCanonicalBlockCount =>
        Volatile.Read(ref _hasStagedCanonicalBlock);

    public int CanonicalBlockSize => _stagedCanonicalBlock.Length;

    public ImmutableArray<PhysicalAudioSinkStartupFrame>
        GetObservedStartupFraming()
    {
        int completedFrameCount =
            Volatile.Read(ref _completedStartupFrameCount);
        return completedFrameCount == PlannedStartupFraming.Length
            ? PlannedStartupFraming
            : PlannedStartupFraming.Slice(0, completedFrameCount);
    }

    public void DiscardCanonicalStaging()
    {
        _stagedCanonicalOffset = 0;
        _stagedCanonicalLength = 0;
        Volatile.Write(ref _hasStagedCanonicalBlock, 0);
    }

    public bool FillInterleaved(
        AudioBlockQueue canonicalQueue,
        AudioBuffer<float> output,
        ulong frameCount,
        int channels)
    {
        ArgumentNullException.ThrowIfNull(canonicalQueue);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);

        if (frameCount > 0)
        {
            TryStageCanonicalBlock(canonicalQueue);
        }

        bool complete = true;
        int outputIndex = 0;
        for (ulong frame = 0; frame < frameCount; frame++)
        {
            bool isStartupSample =
                TryGetStartupSample(out float sample);
            bool hasSample = isStartupSample
                || TryReadCanonicalSample(canonicalQueue, out sample);
            if (!hasSample)
            {
                complete = false;
            }

            for (int channel = 0; channel < channels; channel++)
            {
                output[outputIndex] = sample;
                outputIndex++;
            }

            if (isStartupSample)
            {
                CompleteStartupSample();
            }
        }

        return complete;
    }

    private bool TryGetStartupSample(out float sample)
    {
        if (_startupFrameIndex >= PlannedStartupFraming.Length)
        {
            sample = 0f;
            return false;
        }

        PhysicalAudioSinkStartupFrame frame =
            PlannedStartupFraming[_startupFrameIndex];
        sample = frame.Samples[_startupSampleIndex];
        return true;
    }

    private void CompleteStartupSample()
    {
        PhysicalAudioSinkStartupFrame frame =
            PlannedStartupFraming[_startupFrameIndex];
        _startupSampleIndex++;
        if (_startupSampleIndex != frame.Samples.Length)
        {
            return;
        }

        _startupFrameIndex++;
        _startupSampleIndex = 0;
        Volatile.Write(
            ref _completedStartupFrameCount,
            _startupFrameIndex);
    }

    private bool TryReadCanonicalSample(
        AudioBlockQueue canonicalQueue,
        out float sample)
    {
        if (_stagedCanonicalOffset >= _stagedCanonicalLength
            && !TryStageCanonicalBlock(canonicalQueue))
        {
            sample = 0f;
            return false;
        }

        sample = _stagedCanonicalBlock[_stagedCanonicalOffset];
        _stagedCanonicalOffset++;
        if (_stagedCanonicalOffset == _stagedCanonicalLength)
        {
            DiscardCanonicalStaging();
        }

        return true;
    }

    private bool TryStageCanonicalBlock(AudioBlockQueue canonicalQueue)
    {
        if (Volatile.Read(ref _hasStagedCanonicalBlock) != 0)
        {
            return true;
        }

        while (canonicalQueue.TryReadBlock(
            _stagedCanonicalBlock,
            out int length))
        {
            if (length == 0)
            {
                continue;
            }

            _stagedCanonicalOffset = 0;
            _stagedCanonicalLength = length;
            Volatile.Write(ref _hasStagedCanonicalBlock, 1);
            return true;
        }

        return false;
    }

    private static ImmutableArray<PhysicalAudioSinkStartupFrame>
        CreatePlannedStartupFraming()
    {
        var frames =
            ImmutableArray.CreateBuilder<PhysicalAudioSinkStartupFrame>(
                CompatibilityProfile.AudioStartupRequestCount);
        for (int index = 0;
             index < CompatibilityProfile.AudioStartupRequestCount;
             index++)
        {
            frames.Add(
                new PhysicalAudioSinkStartupFrame(
                    LogicalRequestNumber: index + 1,
                    IsSynchronousPrefill:
                        index
                        < CompatibilityProfile
                            .AudioStartupPrefillRequestCount,
                    Samples: ImmutableArray.Create(0f)));
        }

        return frames.MoveToImmutable();
    }
}
