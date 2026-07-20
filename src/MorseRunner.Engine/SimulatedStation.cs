using System.Globalization;
using MorseRunner.Domain;
using MorseRunner.Dsp;

namespace MorseRunner.Engine;

public sealed class SimulatedStation
{
    private readonly MorseToneRenderer _renderer;
    private readonly ContestId _contestId;
    private readonly QsbProcessor? _qsb;
    private readonly float[] _scratch =
        new float[CompatibilityProfile.BlockSize];
    private double _bfoPhase;
    private int _timeoutBlocks = int.MaxValue;
    private bool _transmissionCompletedInRenderedBlock;

    public SimulatedStation(
        StationIdentity identity,
        int wordsPerMinute,
        int pitchOffsetHz,
        LegacyRandom random,
        OperatorRunMode runMode,
        bool lids = false,
        bool sweepstakes = false,
        OperatorState initialOperatorState = OperatorState.NeedPreviousEnd,
        ContestId? contestId = null)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _contestId = contestId ?? new("scWpx");
        WordsPerMinute = wordsPerMinute;
        PitchOffsetHz = pitchOffsetHz;
        Operator = new(
            identity.Callsign,
            initialOperatorState,
            random,
            runMode,
            lids,
            sweepstakes);
        State = StationState.Copying;
        R1 = 0f;
        Amplitude = 36_000f;
        CharacterWordsPerMinute = wordsPerMinute;
        _renderer = new(
            CompatibilityProfile.SampleRate,
            CompatibilityProfile.BlockSize,
            wordsPerMinute,
            carrierFrequency: 600f,
            gain: 1f);
    }

    public StationIdentity Identity { get; }

    public SimulatedOperator Operator { get; }

    public StationState State { get; private set; }

    public int WordsPerMinute { get; }

    internal int CharacterWordsPerMinute { get; }

    public int PitchOffsetHz { get; }

    internal float R1 { get; private set; }

    internal float Amplitude { get; private set; }

    public StationReply LastReply { get; private set; }

    public string? LastReplyText { get; private set; }

    public bool IsComplete =>
        Operator.State is OperatorState.Done or OperatorState.Failed;

    private SimulatedStation(
        StationIdentity identity,
        int wordsPerMinute,
        int characterWordsPerMinute,
        int pitchOffsetHz,
        SimulatedOperator simulatedOperator,
        QsbProcessor qsb,
        float r1,
        float amplitude,
        ContestId contestId)
    {
        Identity = identity;
        _contestId = contestId;
        WordsPerMinute = wordsPerMinute;
        CharacterWordsPerMinute = characterWordsPerMinute;
        PitchOffsetHz = pitchOffsetHz;
        Operator = simulatedOperator;
        State = StationState.Copying;
        _qsb = qsb;
        R1 = r1;
        Amplitude = amplitude;
        _renderer = new(
            CompatibilityProfile.SampleRate,
            CompatibilityProfile.BlockSize,
            wordsPerMinute,
            carrierFrequency: 600f,
            gain: 1f);
    }

    internal static SimulatedStation CreateCandidate(
        Func<StationIdentity> identityFactory,
        Func<int> wordsPerMinuteFactory,
        LegacyRandom random,
        LegacyRandomEffects randomEffects,
        OperatorRunMode runMode,
        bool lids,
        bool sweepstakes,
        bool flutter,
        ContestId contestId)
    {
        ArgumentNullException.ThrowIfNull(identityFactory);
        ArgumentNullException.ThrowIfNull(wordsPerMinuteFactory);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(randomEffects);

        float r1 = random.NextSingle();
        StationIdentity identity = identityFactory();
        var simulatedOperator = new SimulatedOperator(
            identity.Callsign,
            OperatorState.NeedPreviousEnd,
            random,
            runMode,
            lids,
            sweepstakes);
        _ = lids && random.NextDouble() < 0.1d;
        int wordsPerMinute = wordsPerMinuteFactory();
        if (lids && random.NextDouble() < 0.03d)
        {
            identity = identity with
            {
                Rst = (559 + (10 * random.Next(4))).ToString(
                    CultureInfo.InvariantCulture),
            };
        }

        var qsb = new QsbProcessor(randomEffects)
        {
            Bandwidth = (float)(
                0.1d + (random.NextDouble() / 2d)),
        };
        if (flutter && random.NextDouble() < 0.3d)
        {
            qsb.Bandwidth = (float)(
                3d + (random.NextDouble() * 30d));
        }

        float amplitude = (float)(
            9_000f
            + (18_000f * (1f + randomEffects.UShaped())));
        int pitchRange = runMode == OperatorRunMode.SingleCall ? 50 : 300;
        int pitchOffsetHz = (int)MathF.Round(
            randomEffects.GaussianLimited(0f, pitchRange),
            MidpointRounding.ToEven);
        return new(
            identity,
            wordsPerMinute,
            wordsPerMinute,
            pitchOffsetHz,
            simulatedOperator,
            qsb,
            r1,
            amplitude,
            contestId);
    }

    public static SimulatedStation CreateReadyCaller(
        StationIdentity identity,
        int wordsPerMinute,
        int pitchOffsetHz,
        LegacyRandom random,
        OperatorRunMode runMode,
        bool lids,
        bool sweepstakes,
        ContestId? contestId = null)
    {
        var station = new SimulatedStation(
            identity,
            wordsPerMinute,
            pitchOffsetHz,
            random,
            runMode,
            lids,
            sweepstakes,
            OperatorState.NeedQso,
            contestId);
        station.State = StationState.PreparingToSend;
        station._timeoutBlocks = station.Operator.GetSendDelay(wordsPerMinute);
        return station;
    }

    internal void PrepareReadyCaller()
    {
        Operator.SetState(OperatorState.NeedQso);
        State = StationState.PreparingToSend;
        _timeoutBlocks = Operator.GetSendDelay(WordsPerMinute);
    }

    public void ReceiveOperatorStarted()
    {
        if (State != StationState.Sending)
        {
            State = StationState.Copying;
        }

        _timeoutBlocks = int.MaxValue;
    }

    public void ReceiveOperatorFinished(
        StationMessage message,
        string copiedCall = "",
        int minimumCallConfidence = 1,
        bool allowLidErrors = false)
    {
        if (Operator.State == OperatorState.Done)
        {
            return;
        }

        if (State != StationState.Sending)
        {
            StationMessage copiedMessage = State == StationState.Copying
                || (message
                    & (StationMessage.Cq
                        | StationMessage.ThankYou
                        | StationMessage.Nil)) != 0
                    ? message
                    : StationMessage.Garbage;
            Operator.Receive(
                copiedMessage,
                copiedCall,
                minimumCallConfidence,
                allowLidErrors);

            if (Operator.State == OperatorState.Failed)
            {
                return;
            }

            if (Operator.IsGhosting)
            {
                State = StationState.Listening;
                _timeoutBlocks = int.MaxValue;
            }
            else
            {
                State = StationState.PreparingToSend;
                _timeoutBlocks = Operator.GetSendDelay(WordsPerMinute);
            }
        }
        else if (Operator.State == OperatorState.NeedCall
            && (message & StationMessage.ThankYou) != 0)
        {
            Operator.Receive(message, copiedCall);
        }
    }

    internal void RenderBlock(
        Span<float> receiverReal,
        Span<float> receiverImaginary,
        bool qsbEnabled,
        bool mixOutput = true,
        Span<float> envelopeObservation = default)
    {
        _transmissionCompletedInRenderedBlock = false;
        if (State != StationState.Sending)
        {
            return;
        }

        bool hadAudio = _renderer.HasPendingAudio;
        _renderer.RenderEnvelope(_scratch);
        if (qsbEnabled)
        {
            (_qsb
                ?? throw new InvalidOperationException(
                    "The QSB-enabled station has no QSB processor."))
                .Apply(_scratch);
        }

        if (!envelopeObservation.IsEmpty)
        {
            if (envelopeObservation.Length != _scratch.Length)
            {
                throw new ArgumentException(
                    "The station envelope observation must match the "
                    + "compatibility block size.",
                    nameof(envelopeObservation));
            }

            _scratch.CopyTo(envelopeObservation);
        }

        if (mixOutput)
        {
            double phaseStep =
                2d * Math.PI * PitchOffsetHz
                / CompatibilityProfile.SampleRate;
            for (int index = 0; index < receiverReal.Length; index++)
            {
                float amplitude = _scratch[index] * Amplitude;
                receiverReal[index] +=
                    amplitude * (float)Math.Cos(_bfoPhase);
                receiverImaginary[index] -=
                    amplitude * (float)Math.Sin(_bfoPhase);
                _bfoPhase += phaseStep;
                if (_bfoPhase >= 2d * Math.PI)
                {
                    _bfoPhase -= 2d * Math.PI;
                }
                else if (_bfoPhase <= -2d * Math.PI)
                {
                    _bfoPhase += 2d * Math.PI;
                }
            }
        }

        _transmissionCompletedInRenderedBlock =
            hadAudio && !_renderer.HasPendingAudio;
    }

    internal void StartScriptedTransmissionForParity(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (State == StationState.Sending || _renderer.HasPendingAudio)
        {
            throw new InvalidOperationException(
                "The station already has a pending transmission.");
        }

        _renderer.LoadMessage(message);
        State = StationState.Sending;
        _timeoutBlocks = int.MaxValue;
    }

    internal StationBlockTransition Tick()
    {
        if (State == StationState.Sending
            && _transmissionCompletedInRenderedBlock)
        {
            _transmissionCompletedInRenderedBlock = false;
            FinishTransmission();
            return StationBlockTransition.ReplyCompleted;
        }

        if (State == StationState.Sending
            || _timeoutBlocks == int.MaxValue)
        {
            return StationBlockTransition.None;
        }

        _timeoutBlocks--;
        if (_timeoutBlocks != 0)
        {
            return StationBlockTransition.None;
        }

        ExpireTimeout();
        return State == StationState.Sending
            ? StationBlockTransition.ReplyStarted
            : StationBlockTransition.None;
    }

    public void ExpireTimeout()
    {
        if (State == StationState.Listening)
        {
            Operator.Receive(StationMessage.None);
            if (Operator.State == OperatorState.Failed)
            {
                return;
            }

            if (Operator.IsGhosting)
            {
                State = StationState.Listening;
                _timeoutBlocks = int.MaxValue;
                return;
            }

            State = StationState.PreparingToSend;
        }

        if (State == StationState.PreparingToSend)
        {
            StartReply();
        }
    }

    public void FinishTransmission()
    {
        _transmissionCompletedInRenderedBlock = false;
        State = StationState.Listening;
        _timeoutBlocks = Operator.GetReplyTimeout(WordsPerMinute);
    }

    public ActiveStationSnapshot CreateSnapshot() =>
        new(
            Identity.Callsign,
            State,
            Operator.State,
            Operator.Patience,
            Operator.RepeatCount,
            WordsPerMinute,
            PitchOffsetHz,
            Identity.Rst,
            Identity.Exchange1,
            Identity.Exchange2,
            LastReplyText);

    private void StartReply()
    {
        LastReply = Operator.GetReply();
        LastReplyText = FormatReply(LastReply);
        if (LastReply == StationReply.None)
        {
            State = StationState.Listening;
            _timeoutBlocks = int.MaxValue;
            return;
        }

        string message = string.Join(
            ' ',
            Enumerable.Repeat(LastReplyText, Operator.RepeatCount));
        _renderer.LoadMessage(message);
        State = StationState.Sending;
        _timeoutBlocks = int.MaxValue;
    }

    private string FormatReply(StationReply reply)
    {
        string call = Identity.Callsign;
        string number = FormatExchange();
        return reply switch
        {
            StationReply.None => string.Empty,
            StationReply.MyCall => call,
            StationReply.NumberQuestion => "NR?",
            StationReply.Again => "AGN",
            StationReply.DeMyCall => $"DE {call}",
            StationReply.DeMyCallTwice => $"DE {call} {call}",
            StationReply.MyCallTwice => $"{call} {call}",
            StationReply.DeMyCallAndNumber => $"DE {call} {number}",
            StationReply.DeMyCallTwiceAndNumber =>
                $"DE {call} {call} {number}",
            StationReply.MyCallAndNumber => $"{call} {number}",
            StationReply.MyCallTwiceAndNumber => $"{call} {call} {number}",
            StationReply.Number => number,
            StationReply.RogerNumber => $"R {number}",
            StationReply.RogerNumberTwice => $"R {number} {number}",
            _ => throw new InvalidOperationException(
                $"Unknown station reply '{reply}'."),
        };
    }

    private string FormatExchange()
    {
        if (_contestId.Value == "scCwt")
        {
            return $"{Identity.Exchange1}  {Identity.Exchange2}";
        }

        string rst = ToCutNumbers(Identity.Rst);
        string exchange = Identity.Exchange2;
        if (int.TryParse(exchange, out int number))
        {
            exchange = ToCutNumbers(
                number.ToString("000", CultureInfo.InvariantCulture));
        }

        return string.IsNullOrWhiteSpace(exchange)
            ? rst
            : rst + exchange;
    }

    internal string ObserveExchangeForParity()
    {
        return FormatExchange();
    }

    private static string ToCutNumbers(string value) =>
        value.Replace('9', 'N').Replace('0', 'T');
}

internal enum StationBlockTransition
{
    None,
    ReplyStarted,
    ReplyCompleted,
}

public sealed record StationIdentity(
    string Callsign,
    string Rst,
    int Number,
    string Exchange1,
    string Exchange2,
    string Precedence = "",
    int Check = 0,
    string Section = "",
    string OperatorName = "",
    string UserText = "");
