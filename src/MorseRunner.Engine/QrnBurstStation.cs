using MorseRunner.Domain;
using MorseRunner.Dsp;

namespace MorseRunner.Engine;

internal sealed class QrnBurstStation
{
    internal const int MaximumConcurrentStations = 22;

    private const double TriggerProbability = 0.01d;
    private const double MinimumAmplitude = 100_000d;
    private const int MaximumEnvelopeSampleCount =
        MaximumConcurrentStations * CompatibilityProfile.BlockSize;
    private readonly float[] _envelope =
        new float[MaximumEnvelopeSampleCount];
    private int _sendPosition;

    internal bool IsActive { get; private set; }

    internal bool IsSending { get; private set; }

    internal bool HasRenderedEnvelope { get; private set; }

    internal int EnvelopeSampleCount { get; private set; }

    internal int DurationBlocks { get; private set; }

    internal float Amplitude { get; private set; }

    internal void Activate(LegacyRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (IsActive)
        {
            throw new InvalidOperationException(
                "The QRN burst station is already active.");
        }

        float durationSeconds = random.NextSingle();
        int durationBlocks = SecondsToBlocks(durationSeconds);
        int envelopeSampleCount =
            durationBlocks * CompatibilityProfile.BlockSize;
        _envelope.AsSpan(0, envelopeSampleCount).Clear();

        float amplitude = (float)(
            MinimumAmplitude
            * Math.Pow(10d, 2d * random.NextDouble()));
        for (int index = 0; index < envelopeSampleCount; index++)
        {
            if (random.NextDouble() < TriggerProbability)
            {
                _envelope[index] = (float)(
                    (random.NextDouble() - 0.5d) * amplitude);
            }
        }

        _sendPosition = 0;
        DurationBlocks = durationBlocks;
        EnvelopeSampleCount = envelopeSampleCount;
        Amplitude = amplitude;
        HasRenderedEnvelope = false;
        IsSending = true;
        IsActive = true;
    }

    internal void MixNextBlock(
        Span<float> receiverReal,
        Span<float> receiverImaginary)
    {
        if (receiverReal.Length != CompatibilityProfile.BlockSize
            || receiverImaginary.Length != CompatibilityProfile.BlockSize)
        {
            throw new ArgumentException(
                "QRN receiver buffers must each contain one "
                + "compatibility block.");
        }

        if (!IsActive)
        {
            throw new InvalidOperationException(
                "The QRN burst station is not active.");
        }

        if (HasRenderedEnvelope)
        {
            throw new InvalidOperationException(
                "The QRN burst envelope has already been rendered.");
        }

        if (EnvelopeSampleCount != 0)
        {
            ReadOnlySpan<float> envelopeBlock =
                _envelope.AsSpan(
                    _sendPosition,
                    CompatibilityProfile.BlockSize);
            for (int index = 0; index < receiverReal.Length; index++)
            {
                receiverReal[index] += envelopeBlock[index];
            }
        }

        _sendPosition += CompatibilityProfile.BlockSize;
        if (_sendPosition >= EnvelopeSampleCount)
        {
            HasRenderedEnvelope = true;
        }
    }

    internal void Release()
    {
        if (!IsActive)
        {
            throw new InvalidOperationException(
                "The QRN burst station is not active.");
        }

        if (!HasRenderedEnvelope)
        {
            throw new InvalidOperationException(
                "The QRN burst envelope has not finished rendering.");
        }

        _sendPosition = 0;
        DurationBlocks = 0;
        EnvelopeSampleCount = 0;
        Amplitude = 0f;
        HasRenderedEnvelope = false;
        IsSending = false;
        IsActive = false;
    }

    private static int SecondsToBlocks(float seconds) =>
        (int)Math.Round(
            (double)CompatibilityProfile.SampleRate
            / CompatibilityProfile.BlockSize
            * seconds,
            MidpointRounding.ToEven);
}
