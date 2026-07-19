using System.Globalization;
using MorseRunner.Domain;
using MorseRunner.Dsp;

namespace MorseRunner.Engine;

public sealed class SimulatedStation
{
    private readonly MorseToneRenderer _renderer;
    private readonly float[] _scratch =
        new float[CompatibilityProfile.BlockSize];
    private double _bfoPhase;
    private int _timeoutBlocks = int.MaxValue;

    public SimulatedStation(
        StationIdentity identity,
        int wordsPerMinute,
        int pitchOffsetHz,
        LegacyRandom random,
        OperatorRunMode runMode,
        bool lids = false,
        bool sweepstakes = false,
        OperatorState initialOperatorState = OperatorState.NeedPreviousEnd)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
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

    public int PitchOffsetHz { get; }

    public StationReply LastReply { get; private set; }

    public string? LastReplyText { get; private set; }

    public bool IsComplete =>
        Operator.State is OperatorState.Done or OperatorState.Failed;

    public static SimulatedStation CreateReadyCaller(
        StationIdentity identity,
        int wordsPerMinute,
        int pitchOffsetHz,
        LegacyRandom random,
        OperatorRunMode runMode,
        bool lids,
        bool sweepstakes)
    {
        var station = new SimulatedStation(
            identity,
            wordsPerMinute,
            pitchOffsetHz,
            random,
            runMode,
            lids,
            sweepstakes,
            OperatorState.NeedQso);
        station.State = StationState.PreparingToSend;
        station._timeoutBlocks = station.Operator.GetSendDelay(wordsPerMinute);
        return station;
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

    public void AdvanceBlock(
        Span<float> receiverReal,
        Span<float> receiverImaginary,
        bool mixOutput = true)
    {
        if (State == StationState.Sending)
        {
            bool hadAudio = _renderer.HasPendingAudio;
            _renderer.RenderEnvelope(_scratch);
            if (mixOutput)
            {
                double phaseStep =
                    2d * Math.PI * PitchOffsetHz
                    / CompatibilityProfile.SampleRate;
                for (int index = 0; index < receiverReal.Length; index++)
                {
                    float amplitude = _scratch[index] * 36_000f;
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

            if (hadAudio && !_renderer.HasPendingAudio)
            {
                FinishTransmission();
            }

            return;
        }

        if (_timeoutBlocks != int.MaxValue)
        {
            _timeoutBlocks--;
            if (_timeoutBlocks == 0)
            {
                ExpireTimeout();
            }
        }
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

    private static string ToCutNumbers(string value) =>
        value.Replace('9', 'N').Replace('0', 'T');
}

public sealed record StationIdentity(
    string Callsign,
    string Rst,
    int Number,
    string Exchange1,
    string Exchange2,
    string Precedence = "",
    int Check = 0,
    string Section = "");
