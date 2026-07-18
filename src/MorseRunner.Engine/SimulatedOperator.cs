using MorseRunner.Domain;
using MorseRunner.Dsp;

namespace MorseRunner.Engine;

public sealed class SimulatedOperator
{
    private const int FullPatience = 5;
    private readonly LegacyRandom _random;
    private readonly LegacyRandomEffects _effects;
    private string _lastCheckedCall = string.Empty;
    private CallMatch _lastCallMatch;

    public SimulatedOperator(
        string callsign,
        OperatorState initialState,
        LegacyRandom random,
        OperatorRunMode runMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callsign);
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _effects = new LegacyRandomEffects(random);
        Callsign = callsign;
        RunMode = runMode;
        _random.NextDouble();
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
            ? 3 + RoundLegacy(_effects.Rayleigh(3f))
            : FullPatience;
        RepeatCount = state == OperatorState.NeedQso
            && RunMode is not (OperatorRunMode.SingleCall or OperatorRunMode.Hst)
            && _random.NextDouble() < 0.1d
                ? 2
                : 1;
    }

    public int GetSendDelay(int wordsPerMinute = 30)
    {
        float seconds = RunMode == OperatorRunMode.Hst
            ? (float)(0.05d + (0.5d * _random.NextDouble() * 10d / wordsPerMinute))
            : (float)(0.1d + (0.5d * _random.NextDouble()));
        return LegacyRandomEffects.SecondsToBlocks(seconds);
    }

    public int GetReplyTimeout(int wordsPerMinute = 30)
    {
        int blocks = RunMode == OperatorRunMode.Hst
            ? LegacyRandomEffects.SecondsToBlocks(60f / wordsPerMinute)
            : LegacyRandomEffects.SecondsToBlocks(6f - Skills);
        return RoundLegacy(_effects.GaussianLimited(blocks, blocks / 2f));
    }

    public void Receive(StationMessage messages, string? copiedCall = null)
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
            ReceiveCall(copiedCall ?? string.Empty);
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
                _random.NextDouble();
                SetState(OperatorState.NeedEnd);
            }
            else if (State == OperatorState.NeedCallAndNumber)
            {
                _random.NextDouble();
                SetState(OperatorState.NeedCall);
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
            else if (State == OperatorState.NeedEnd)
            {
                State = OperatorState.Done;
            }
        }

        if ((messages & StationMessage.Question) != 0
            && State is OperatorState.NeedNumber
                or OperatorState.NeedCall
                or OperatorState.NeedCallAndNumber
                or OperatorState.NeedEnd)
        {
            AddPatience();
        }

        if ((messages & StationMessage.Garbage) != 0
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
        if (pattern == _lastCheckedCall)
        {
            return _lastCallMatch;
        }

        _lastCheckedCall = pattern;
        if (pattern.Contains('?'))
        {
            string regexPattern = pattern.EndsWith('?')
                ? pattern.Replace("?", ".", StringComparison.Ordinal) + "*"
                : pattern.Replace("?", ".", StringComparison.Ordinal);
            bool matches = System.Text.RegularExpressions.Regex.IsMatch(
                Callsign,
                regexPattern,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            if (!matches)
            {
                return SetMatch(CallMatch.No, 0);
            }

            int penalty = Callsign.Length
                - pattern.Replace("?", string.Empty, StringComparison.Ordinal).Length;
            int confidence = 100 * (Callsign.Length - penalty) / Callsign.Length;
            return SetMatch(CallMatch.Almost, confidence == 0 ? 10 : confidence);
        }

        int penaltyValue = EditDistance(pattern, Callsign);
        CallMatch result = penaltyValue == 0
            ? CallMatch.Yes
            : penaltyValue <= (Callsign.Length - 1) / 2
                ? CallMatch.Almost
                : CallMatch.No;
        if (result == CallMatch.No
            && Callsign.Contains(pattern, StringComparison.Ordinal))
        {
            result = CallMatch.Almost;
            penaltyValue = Callsign.Length - pattern.Length;
        }

        int callConfidence = result switch
        {
            CallMatch.Yes => 100,
            CallMatch.Almost => 100 * (Callsign.Length - penaltyValue) / Callsign.Length,
            _ => 0,
        };
        return SetMatch(result, callConfidence == 0 && result == CallMatch.Almost ? 10 : callConfidence);
    }

    private void ReceiveCall(string copiedCall)
    {
        CallMatch match = MatchCall(copiedCall);
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

    private static int RoundLegacy(float value) =>
        (int)MathF.Round(value, MidpointRounding.ToEven);
}
