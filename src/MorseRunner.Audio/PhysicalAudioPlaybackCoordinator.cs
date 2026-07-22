using System.Collections.Immutable;
using MiniAudioExNET;
using MorseRunner.Domain;

namespace MorseRunner.Audio;

internal sealed class PhysicalAudioPlaybackCoordinator
{
    private static readonly ImmutableArray<float> PositiveZeroSamples;
    private static readonly
        ImmutableArray<PhysicalAudioSinkStartupFrame>
        SynchronousPrefillFraming;
    private static readonly
        ImmutableArray<PhysicalAudioSinkStartupFrame>
        CompleteStartupFraming;

    private readonly float[] _stagedCanonicalBlock;
    private int _synchronousPrefillPresentationCount;
    private int _completionDrivenPresentationCompleted;
    private int _startupOutputSampleIndex;
    private int _stagedCanonicalLength;
    private int _stagedCanonicalOffset;
    private int _hasStagedCanonicalBlock;

    static PhysicalAudioPlaybackCoordinator()
    {
        PositiveZeroSamples = ImmutableArray.Create(0f);
        SynchronousPrefillFraming =
            CreateSynchronousPrefillFraming();
        CompleteStartupFraming =
            SynchronousPrefillFraming.Add(
                new PhysicalAudioSinkStartupFrame(
                    LogicalRequestNumber:
                        SimulationAudioProfile.AudioStartupRequestCount,
                    IsSynchronousPrefill: false,
                    Samples: PositiveZeroSamples));
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

    public void PresentSynchronousStartupPrefill()
    {
        while (true)
        {
            int completedCount = Volatile.Read(
                ref _synchronousPrefillPresentationCount);
            if (completedCount
                >= SimulationAudioProfile.AudioStartupPrefillRequestCount)
            {
                return;
            }

            Interlocked.CompareExchange(
                ref _synchronousPrefillPresentationCount,
                completedCount + 1,
                completedCount);
        }
    }

    public void PresentCompletionDrivenStartup()
    {
        if (Volatile.Read(
                ref _synchronousPrefillPresentationCount)
            != SimulationAudioProfile.AudioStartupPrefillRequestCount)
        {
            throw new InvalidOperationException(
                "Completion-driven startup requires the synchronous "
                + "prefill phase.");
        }

        Interlocked.CompareExchange(
            ref _completionDrivenPresentationCompleted,
            1,
            0);
    }

    public ImmutableArray<PhysicalAudioSinkStartupFrame>
        GetObservedStartupFraming()
    {
        int synchronousCount = Math.Min(
            Volatile.Read(
                ref _synchronousPrefillPresentationCount),
            SimulationAudioProfile.AudioStartupPrefillRequestCount);
        if (Volatile.Read(
                ref _completionDrivenPresentationCompleted) != 0)
        {
            return CompleteStartupFraming;
        }

        return synchronousCount
            == SimulationAudioProfile.AudioStartupPrefillRequestCount
            ? SynchronousPrefillFraming
            : SynchronousPrefillFraming.Slice(
                0,
                synchronousCount);
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
        if (frameCount > 0
            && Volatile.Read(
                ref _completionDrivenPresentationCompleted) == 0)
        {
            throw new InvalidOperationException(
                "Startup output requires the completion-driven "
                + "presentation phase.");
        }

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
        if (_startupOutputSampleIndex
            >= SimulationAudioProfile.AudioStartupRequestCount)
        {
            sample = 0f;
            return false;
        }

        sample = 0f;
        return true;
    }

    private void CompleteStartupSample()
    {
        _startupOutputSampleIndex++;
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
        CreateSynchronousPrefillFraming()
    {
        var frames =
            ImmutableArray.CreateBuilder<PhysicalAudioSinkStartupFrame>(
                SimulationAudioProfile.AudioStartupPrefillRequestCount);
        for (int index = 0;
             index
                < SimulationAudioProfile.AudioStartupPrefillRequestCount;
             index++)
        {
            frames.Add(
                new PhysicalAudioSinkStartupFrame(
                    LogicalRequestNumber: index + 1,
                    IsSynchronousPrefill: true,
                    Samples: PositiveZeroSamples));
        }

        return frames.MoveToImmutable();
    }
}
