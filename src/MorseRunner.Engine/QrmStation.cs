using MorseRunner.Domain;
using MorseRunner.Dsp;

namespace MorseRunner.Engine;

internal sealed class QrmStation
{
    private const double MinimumAmplitude = 5_000d;
    private const double AmplitudeRange = 25_000d;
    private const float MaximumPitchOffsetHz = 300f;
    private const int MinimumWordsPerMinute = 30;
    private const int WordsPerMinuteRange = 20;
    private const int MaximumTransmissionCount = 5;
    private const int MeanRetrySeconds = 4;
    private const int RetryLimitSeconds = 2;
    private const int MaximumRetryTimeoutBlocks = 129;

    private readonly LegacyMorseEnvelopeCursor _envelope;
    private readonly LegacyStationMixer _mixer;
    private ContestQrmMessageDescriptor _message;
    private bool _hasMessageText;

    internal QrmStation(LegacyMorseKeyingProfile keyingProfile)
    {
        _envelope = new(keyingProfile);
        _mixer = new(keyingProfile.SampleRate);
    }

    internal bool IsActive { get; private set; }

    internal bool IsSending { get; private set; }

    internal float R1 { get; private set; }

    internal int Patience { get; private set; }

    internal string? MyCall { get; private set; }

    internal string? HisCall { get; private set; }

    internal float Amplitude { get; private set; }

    internal int PitchOffsetHz { get; private set; }

    internal int SendingWordsPerMinute { get; private set; }

    internal int CharacterWordsPerMinute { get; private set; }

    internal int TimeoutBlocks { get; private set; }

    internal int TransmissionCount { get; private set; }

    internal string? MessageSet =>
        IsActive ? _message.MessageSet : null;

    internal string? MessageText =>
        IsActive && _hasMessageText
            ? _message.MaterializeForObservation()
            : null;

    internal int EnvelopeSampleCount =>
        IsSending ? _envelope.PaddedEnvelopeSampleCount : 0;

    internal int SendPosition => _envelope.SendPosition;

    internal int RemainingBlockCount =>
        IsSending ? _envelope.RemainingBlockCount : 0;

    internal void Activate(
        LegacyRandom random,
        LegacyRandomEffects randomEffects,
        StationReferenceCatalog stationCatalog,
        ContestId contestId,
        RunModeId runModeId,
        string stationCall,
        Func<string>? callsignOverride = null)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(randomEffects);
        ArgumentNullException.ThrowIfNull(stationCatalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(stationCall);
        if (IsActive)
        {
            throw new InvalidOperationException(
                "The QRM station is already active.");
        }

        R1 = random.NextSingle();
        Patience = 1 + random.Next(MaximumTransmissionCount);
        MyCall = callsignOverride is null
            ? stationCatalog.PickCallsignForQrm(random, runModeId)
            : callsignOverride();
        HisCall = stationCall;
        Amplitude = (float)(
            MinimumAmplitude
            + (AmplitudeRange * random.NextDouble()));
        PitchOffsetHz = (int)MathF.Round(
            randomEffects.GaussianLimited(
                mean: 0f,
                limit: MaximumPitchOffsetHz),
            MidpointRounding.ToEven);
        SendingWordsPerMinute =
            MinimumWordsPerMinute + random.Next(WordsPerMinuteRange);
        CharacterWordsPerMinute = SendingWordsPerMinute;
        int messageChoice = random.Next(7);

        IsActive = true;
        StartTransmission(
            ContestQrmMessageCatalog.CreateInitial(
                contestId,
                messageChoice,
                MyCall,
                HisCall));
    }

    internal void MixNextBlock(
        Span<float> envelopeBuffer,
        Span<float> receiverReal,
        Span<float> receiverImaginary,
        int ritOffsetHz,
        float ritPhase)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException(
                "The QRM station is not active.");
        }

        if (!IsSending)
        {
            return;
        }

        if (!_envelope.TryRenderNextBlock(envelopeBuffer))
        {
            throw new InvalidOperationException(
                "The sending QRM station has no pending envelope.");
        }

        _mixer.MixBlock(
            envelopeBuffer,
            receiverReal,
            receiverImaginary,
            ritOffsetHz,
            ritPhase);
    }

    internal bool Tick(
        LegacyRandomEffects randomEffects,
        ContestId contestId)
    {
        ArgumentNullException.ThrowIfNull(randomEffects);
        if (!IsActive)
        {
            throw new InvalidOperationException(
                "The QRM station is not active.");
        }

        if (IsSending && !_envelope.HasPendingAudio)
        {
            _hasMessageText = false;
            IsSending = false;
            Patience--;
            if (Patience == 0)
            {
                return true;
            }

            float meanBlocks = LegacyRandomEffects.SecondsToBlocks(
                MeanRetrySeconds);
            float limitBlocks = LegacyRandomEffects.SecondsToBlocks(
                RetryLimitSeconds);
            TimeoutBlocks = (int)MathF.Round(
                randomEffects.GaussianLimited(
                    meanBlocks,
                    limitBlocks),
                MidpointRounding.ToEven);
            return false;
        }

        if (!IsSending)
        {
            if (TimeoutBlocks > -1)
            {
                TimeoutBlocks--;
            }

            if (TimeoutBlocks == 0)
            {
                StartTransmission(
                    ContestQrmMessageCatalog.CreateLongCq(
                        contestId,
                        MyCall
                        ?? throw new InvalidOperationException(
                            "The QRM station has no callsign.")));
            }
        }

        return false;
    }

    internal void Release()
    {
        if (!IsActive || Patience != 0 || IsSending)
        {
            throw new InvalidOperationException(
                "The QRM station is not ready for release.");
        }

        IsActive = false;
        R1 = 0f;
        Patience = 0;
        MyCall = null;
        HisCall = null;
        Amplitude = 0f;
        PitchOffsetHz = 0;
        SendingWordsPerMinute = 0;
        CharacterWordsPerMinute = 0;
        TimeoutBlocks = 0;
        TransmissionCount = 0;
        _message = default;
        _hasMessageText = false;
    }

    internal static int CalculateMaximumConcurrentStations(
        LegacyMorseKeyingProfile keyingProfile,
        StationReferenceCatalog stationCatalog,
        ContestId contestId,
        string stationCall)
    {
        ArgumentNullException.ThrowIfNull(stationCatalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(stationCall);

        var cursor = new LegacyMorseEnvelopeCursor(keyingProfile);
        int maximumLongCqBlocks = 0;
        for (int index = 0;
             index < stationCatalog.QrmEnvelopeBoundCallsignCount;
             index++)
        {
            maximumLongCqBlocks = Math.Max(
                maximumLongCqBlocks,
                MeasureBlocks(
                    cursor,
                    ContestQrmMessageCatalog.CreateLongCq(
                        contestId,
                        stationCatalog
                            .GetQrmEnvelopeBoundCallsignAt(index))));
        }

        int maximumInitialBlocks = maximumLongCqBlocks;
        maximumInitialBlocks = Math.Max(
            maximumInitialBlocks,
            MeasureBlocks(
                cursor,
                ContestQrmMessageCatalog.CreateInitial(
                    contestId,
                    choice: 0,
                    qrmCall: "P29SX",
                    operatorCall: stationCall)));
        maximumInitialBlocks = Math.Max(
            maximumInitialBlocks,
            MeasureBlocks(
                cursor,
                ContestQrmMessageCatalog.CreateInitial(
                    contestId,
                    choice: 1,
                    qrmCall: "P29SX",
                    operatorCall: stationCall)));
        maximumInitialBlocks = Math.Max(
            maximumInitialBlocks,
            MeasureBlocks(
                cursor,
                ContestQrmMessageCatalog.CreateInitial(
                    contestId,
                    choice: 6,
                    qrmCall: "P29SX",
                    operatorCall: stationCall)));

        return checked(
            maximumInitialBlocks
            + ((MaximumTransmissionCount - 1)
                * (MaximumRetryTimeoutBlocks
                    + maximumLongCqBlocks)));
    }

    private static int MeasureBlocks(
        LegacyMorseEnvelopeCursor cursor,
        ContestQrmMessageDescriptor message)
    {
        cursor.Reset(
            ToMorseText(message),
            MinimumWordsPerMinute,
            amplitude: 0f);
        return cursor.PaddedEnvelopeSampleCount
            / cursor.Profile.BlockSize;
    }

    private void StartTransmission(
        ContestQrmMessageDescriptor message)
    {
        _message = message;
        _hasMessageText = true;
        _envelope.Reset(
            ToMorseText(message),
            SendingWordsPerMinute,
            Amplitude);
        _mixer.BeginTransmission(PitchOffsetHz);
        TimeoutBlocks = Int32.MaxValue;
        TransmissionCount++;
        IsSending = true;
    }

    private static LegacyMorseText ToMorseText(
        ContestQrmMessageDescriptor message)
    {
        return message.SegmentCount switch
        {
            1 => new(
                message.GetSegment(0)),
            2 => new(
                message.GetSegment(0),
                message.GetSegment(1)),
            3 => new(
                message.GetSegment(0),
                message.GetSegment(1),
                message.GetSegment(2)),
            4 => new(
                message.GetSegment(0),
                message.GetSegment(1),
                message.GetSegment(2),
                message.GetSegment(3)),
            5 => new(
                message.GetSegment(0),
                message.GetSegment(1),
                message.GetSegment(2),
                message.GetSegment(3),
                message.GetSegment(4)),
            _ => throw new InvalidOperationException(
                "The QRM message has an unsupported segment count."),
        };
    }
}
