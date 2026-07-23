using MorseRunner.Domain;
using MorseRunner.Dsp;

namespace MorseRunner.Engine;

public sealed class SimulatedOperator
{
    private const int FullPatience = 5;
    private readonly DeterministicRandom _random;
    private readonly RandomEffects _effects;
    private readonly double _responseChoice;
    private readonly bool _lids;
    private readonly bool _sweepstakes;
    private string _lastCheckedCall = string.Empty;
    private CallMatch _lastCallMatch;
    private int _numberQuestionCount;
    private bool _correctedCallAndExchangeSent;
    private bool _isActiveInQso;

    public SimulatedOperator(
        string callsign,
        OperatorState initialState,
        DeterministicRandom random,
        OperatorRunMode runMode,
        bool lids = false,
        bool sweepstakes = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callsign);
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _effects = new RandomEffects(random);
        Callsign = callsign;
        RunMode = runMode;
        _lids = lids;
        _sweepstakes = sweepstakes;
        _responseChoice = _random.NextDouble();
        Skills = 1 + _random.Next(3);
        RepeatCount = 1;
        SetState(initialState);
    }

    public string Callsign { get; }

    public OperatorRunMode RunMode { get; }

    public OperatorState State { get; private set; }

    public int Skills { get; }

    public int Patience { get; private set; }

    public int RepeatCount { get; private set; }

    public int CallConfidence { get; private set; }

    public bool IsGhosting => Patience == 0;

    public void SetState(OperatorState state)
    {
        State = state;
        Patience = state == OperatorState.NeedQso
            ? 3 + RoundToEven(_effects.Rayleigh(3f))
            : FullPatience;
        RepeatCount = state == OperatorState.NeedQso
            && RunMode is not (OperatorRunMode.SingleCall or OperatorRunMode.Hst)
            && _random.NextDouble() < 0.1d
                ? 2
                : 1;
        if (state == OperatorState.NeedQso)
        {
            _correctedCallAndExchangeSent = false;
        }

        if (state == OperatorState.NeedNumber)
        {
            _numberQuestionCount = 0;
        }
    }

    public int GetSendDelay(int wordsPerMinute = 30)
    {
        if (State == OperatorState.NeedPreviousEnd)
        {
            return int.MaxValue;
        }

        float seconds = RunMode == OperatorRunMode.Hst
            ? (float)(0.05d + (0.5d * _random.NextDouble() * 10d / wordsPerMinute))
            : (float)(0.1d + (0.5d * _random.NextDouble()));
        return RandomEffects.SecondsToBlocks(seconds);
    }

    public int GetReplyTimeout(int wordsPerMinute = 30)
    {
        int blocks = RunMode == OperatorRunMode.Hst
            ? RandomEffects.SecondsToBlocks(60f / wordsPerMinute)
            : RandomEffects.SecondsToBlocks(6f - Skills);
        return RoundToEven(_effects.GaussianLimited(blocks, blocks / 2f));
    }

    public void Receive(
        StationMessage messages,
        string? copiedCall = null,
        int minimumCallConfidence = 1,
        bool allowLidErrors = false)
    {
        if ((messages & StationMessage.Cq) != 0)
        {
            if (State == OperatorState.NeedPreviousEnd)
            {
                SetState(OperatorState.NeedQso);
            }
            else if (State == OperatorState.NeedQso)
            {
                DecreasePatience();
            }
            else if (State is OperatorState.NeedNumber
                or OperatorState.NeedCall
                or OperatorState.NeedCallAndNumber)
            {
                State = OperatorState.Failed;
            }
            else if (State == OperatorState.NeedEnd)
            {
                State = OperatorState.Done;
            }

            return;
        }

        if ((messages & StationMessage.Nil) != 0)
        {
            if (State == OperatorState.NeedPreviousEnd)
            {
                SetState(OperatorState.NeedQso);
            }
            else if (State == OperatorState.NeedQso)
            {
                DecreasePatience();
            }
            else if (State is OperatorState.NeedNumber
                or OperatorState.NeedCall
                or OperatorState.NeedCallAndNumber
                or OperatorState.NeedEnd)
            {
                State = OperatorState.Failed;
            }

            return;
        }

        if ((messages & StationMessage.HisCall) != 0)
        {
            ReceiveCall(
                copiedCall ?? string.Empty,
                minimumCallConfidence,
                allowLidErrors);
        }

        if ((messages & StationMessage.Before) != 0)
        {
            if (State is OperatorState.NeedPreviousEnd or OperatorState.NeedQso)
            {
                SetState(OperatorState.NeedQso);
            }
            else if (State is OperatorState.NeedNumber or OperatorState.NeedEnd)
            {
                State = OperatorState.Failed;
            }
        }

        if ((messages & StationMessage.Number) != 0)
        {
            if (State == OperatorState.NeedQso)
            {
                State = OperatorState.NeedPreviousEnd;
            }
            else if (State == OperatorState.NeedNumber)
            {
                if (_random.NextDouble() < 0.9d
                    || RunMode is OperatorRunMode.Hst
                        or OperatorRunMode.SingleCall)
                {
                    SetState(OperatorState.NeedEnd);
                }
                else
                {
                    AddPatience();
                }
            }
            else if (State == OperatorState.NeedCallAndNumber)
            {
                if (_random.NextDouble() < 0.9d
                    || RunMode is OperatorRunMode.Hst
                        or OperatorRunMode.SingleCall)
                {
                    SetState(OperatorState.NeedCall);
                }
                else
                {
                    AddPatience();
                }
            }
            else if (State is OperatorState.NeedCall or OperatorState.NeedEnd)
            {
                AddPatience();
            }
        }

        if ((messages & StationMessage.ThankYou) != 0)
        {
            if (State is OperatorState.NeedPreviousEnd or OperatorState.NeedQso)
            {
                SetState(OperatorState.NeedQso);
            }
            else if (State is OperatorState.NeedNumber or OperatorState.NeedCall)
            {
                if (_isActiveInQso)
                {
                    State = OperatorState.Done;
                }
                else
                {
                    SetState(OperatorState.NeedQso);
                }
            }
            else if (State == OperatorState.NeedCallAndNumber)
            {
                if (_isActiveInQso && _correctedCallAndExchangeSent)
                {
                    State = OperatorState.Done;
                }
                else
                {
                    SetState(OperatorState.NeedQso);
                }
            }
            else if (State == OperatorState.NeedEnd)
            {
                State = OperatorState.Done;
            }
        }

        if ((messages & StationMessage.Question) != 0)
        {
            if (State == OperatorState.NeedPreviousEnd
                && string.IsNullOrEmpty(copiedCall))
            {
                SetState(OperatorState.NeedQso);
            }
            else if (State is OperatorState.NeedNumber
                or OperatorState.NeedCall
                or OperatorState.NeedCallAndNumber
                or OperatorState.NeedEnd)
            {
                AddPatience();
            }
        }

        if ((messages & StationMessage.Garbage) != 0
            && !_lids
            && State is OperatorState.NeedQso
                or OperatorState.NeedNumber
                or OperatorState.NeedCall
                or OperatorState.NeedCallAndNumber)
        {
            AddPatience();
        }

        if (State != OperatorState.NeedPreviousEnd)
        {
            DecreasePatience();
        }
    }

    public CallMatch MatchCall(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        string normalizedPattern = pattern.ToUpperInvariant();
        if (normalizedPattern == _lastCheckedCall)
        {
            return _lastCallMatch;
        }

        _lastCheckedCall = normalizedPattern;
        if (normalizedPattern.Contains('?'))
        {
            string regexPattern = normalizedPattern.EndsWith('?')
                ? normalizedPattern.Replace("?", ".", StringComparison.Ordinal) + "*"
                : normalizedPattern.Replace("?", ".", StringComparison.Ordinal);
            bool matches = System.Text.RegularExpressions.Regex.IsMatch(
                Callsign,
                regexPattern,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            if (!matches)
            {
                return SetMatch(CallMatch.No, 0);
            }

            int penalty = Callsign.Length
                - normalizedPattern.Replace("?", string.Empty, StringComparison.Ordinal).Length;
            int confidence = 100 * (Callsign.Length - penalty) / Callsign.Length;
            return SetMatch(CallMatch.Almost, confidence == 0 ? 10 : confidence);
        }

        int penaltyValue = EditDistance(normalizedPattern, Callsign);
        CallMatch result = penaltyValue == 0
            ? CallMatch.Yes
            : penaltyValue <= (Callsign.Length - 1) / 2
                ? CallMatch.Almost
                : CallMatch.No;
        if (result == CallMatch.No
            && Callsign.Contains(normalizedPattern, StringComparison.Ordinal))
        {
            result = CallMatch.Almost;
            penaltyValue = Callsign.Length - normalizedPattern.Length;
        }

        int callConfidence = result switch
        {
            CallMatch.Yes => 100,
            CallMatch.Almost => 100 * (Callsign.Length - penaltyValue) / Callsign.Length,
            _ => 0,
        };
        return SetMatch(result, callConfidence == 0 && result == CallMatch.Almost ? 10 : callConfidence);
    }

    public StationReply GetReply()
    {
        if (IsGhosting)
        {
            return StationReply.None;
        }

        return State switch
        {
            OperatorState.NeedPreviousEnd
                or OperatorState.Done
                or OperatorState.Failed => StationReply.None,
            OperatorState.NeedQso => StationReply.MyCall,
            OperatorState.NeedNumber => GetNumberRequestReply(),
            OperatorState.NeedCall => GetCallCorrectionReply(includeNumber: true),
            OperatorState.NeedCallAndNumber =>
                GetCallCorrectionReply(includeNumber: false),
            OperatorState.NeedEnd when Patience < FullPatience - 1 =>
                StationReply.Number,
            OperatorState.NeedEnd when RunMode == OperatorRunMode.Hst
                || _sweepstakes
                || _random.NextDouble() < 0.9d => StationReply.RogerNumber,
            OperatorState.NeedEnd => StationReply.RogerNumberTwice,
            _ => throw new InvalidOperationException(
                $"Unknown operator state '{State}'."),
        };
    }

    private void ReceiveCall(
        string copiedCall,
        int minimumCallConfidence,
        bool allowLidErrors)
    {
        CallMatch match = MatchCall(copiedCall);
        if (match == CallMatch.Almost
            && CallConfidence < minimumCallConfidence)
        {
            match = CallMatch.No;
        }

        if (allowLidErrors
            && _lids
            && copiedCall.Length > 3
            && !copiedCall.Contains('?', StringComparison.Ordinal))
        {
            if (match == CallMatch.Yes && _random.NextDouble() < 0.01d)
            {
                match = CallMatch.Almost;
                CallConfidence = 100 * (Callsign.Length - 1) / Callsign.Length;
            }
            else if (match == CallMatch.Almost
                && _random.NextDouble() < 0.04d)
            {
                match = CallMatch.Yes;
                CallConfidence = 100;
            }
        }

        _isActiveInQso = CallConfidence >= minimumCallConfidence;
        switch (match)
        {
            case CallMatch.Yes:
                if (State is OperatorState.NeedPreviousEnd or OperatorState.NeedQso)
                {
                    SetState(OperatorState.NeedNumber);
                }
                else if (State == OperatorState.NeedCallAndNumber)
                {
                    SetState(OperatorState.NeedNumber);
                }
                else if (State is OperatorState.NeedNumber or OperatorState.NeedEnd)
                {
                    AddPatience();
                }
                else if (State == OperatorState.NeedCall)
                {
                    SetState(OperatorState.NeedEnd);
                }

                break;
            case CallMatch.Almost:
                if (State is OperatorState.NeedPreviousEnd or OperatorState.NeedQso)
                {
                    SetState(OperatorState.NeedCallAndNumber);
                }
                else if (State is OperatorState.NeedCallAndNumber
                    or OperatorState.NeedCall)
                {
                    AddPatience();
                }
                else if (State == OperatorState.NeedNumber)
                {
                    SetState(OperatorState.NeedCallAndNumber);
                }
                else if (State == OperatorState.NeedEnd)
                {
                    SetState(OperatorState.NeedCall);
                }

                break;
            case CallMatch.No:
                if (State is OperatorState.NeedQso
                    or OperatorState.NeedNumber
                    or OperatorState.NeedCall
                    or OperatorState.NeedCallAndNumber)
                {
                    State = OperatorState.NeedPreviousEnd;
                }
                else if (State == OperatorState.NeedEnd)
                {
                    State = OperatorState.Done;
                }

                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown call match '{match}'.");
        }
    }

    private StationReply GetNumberRequestReply()
    {
        bool sendNumberQuestion = Patience == FullPatience - 1
            || _numberQuestionCount++ % 3 == 0
            || _random.NextDouble() < 0.2d;
        return sendNumberQuestion
            ? StationReply.NumberQuestion
            : StationReply.Again;
    }

    private StationReply GetCallCorrectionReply(bool includeNumber)
    {
        if (RunMode == OperatorRunMode.Hst)
        {
            return includeNumber
                ? StationReply.DeMyCallAndNumber
                : StationReply.DeMyCall;
        }

        int selection = (int)(_responseChoice * 6d);
        if (includeNumber)
        {
            if (_sweepstakes)
            {
                return selection == 0
                    ? StationReply.DeMyCallAndNumber
                    : StationReply.MyCallAndNumber;
            }

            return selection switch
            {
                0 => StationReply.DeMyCallAndNumber,
                1 => StationReply.DeMyCallTwiceAndNumber,
                2 or 3 => StationReply.MyCallAndNumber,
                4 => StationReply.MyCallTwiceAndNumber,
                _ => StationReply.MyCall,
            };
        }

        StationReply reply = selection switch
        {
            0 => StationReply.DeMyCall,
            1 => StationReply.DeMyCallTwice,
            2 or 3 => StationReply.MyCall,
            4 => StationReply.MyCallTwice,
            _ => StationReply.MyCall,
        };
        if (selection == 5 && _lids && _responseChoice < 0.88d)
        {
            _correctedCallAndExchangeSent = true;
            reply = StationReply.MyCallAndNumber;
        }

        return reply;
    }

    private CallMatch SetMatch(CallMatch match, int confidence)
    {
        _lastCallMatch = match;
        CallConfidence = confidence;
        return match;
    }

    private void DecreasePatience()
    {
        if (State == OperatorState.Done)
        {
            return;
        }

        if (Patience > 0)
        {
            Patience--;
        }

        if (Patience < 1
            && State is OperatorState.NeedPreviousEnd or OperatorState.NeedQso)
        {
            State = OperatorState.Failed;
        }
    }

    private void AddPatience()
    {
        if (State == OperatorState.Done || Patience == FullPatience)
        {
            return;
        }

        Patience = RunMode == OperatorRunMode.SingleCall
            ? 4
            : Patience == 0
                ? 3
                : Math.Min(Patience + 2, 4);
    }

    private static int EditDistance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (int index = 0; index <= right.Length; index++)
        {
            previous[index] = index;
        }

        for (int leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = leftIndex;
            for (int rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                current[rightIndex] = left[leftIndex - 1] == right[rightIndex - 1]
                    ? previous[rightIndex - 1]
                    : 1 + Math.Min(
                        current[rightIndex - 1],
                        Math.Min(previous[rightIndex], previous[rightIndex - 1]));
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static int RoundToEven(float value) =>
        (int)MathF.Round(value, MidpointRounding.ToEven);
}
