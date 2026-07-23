using System.Globalization;
using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Dsp;
using MorseRunner.Engine;

return Run(args);

static int Run(string[] arguments)
{
    if (!TryParseInput(arguments, out StationResponseProbeInput input))
    {
        Console.Error.WriteLine(
            "station-response probe requires --call, --initial, --copied-call, "
            + "and --messages. Messages are comma-separated: cq, number, "
            + "thank-you, his-call, before, question, or nil.");
        return 2;
    }

    var station = new SimulatedOperator(
        input.Call,
        input.InitialState,
        new DeterministicRandom(input.Seed),
        OperatorRunMode.SingleCall);
    int bestConfidence = 1;
    if ((input.Messages & StationMessage.HisCall) != 0)
    {
        station.MatchCall(input.CopiedCall);
        bestConfidence = Math.Max(bestConfidence, station.CallConfidence);
    }

    station.Receive(input.Messages, input.CopiedCall, bestConfidence);
    StationReply reply = station.GetReply();
    Console.WriteLine(
        JsonSerializer.Serialize(
            new
            {
                Engine = "xplat",
                input.Call,
                InitialState = ToProbeState(input.InitialState),
                input.CopiedCall,
                Messages = ToProbeMessages(input.Messages),
                State = ToProbeState(station.State),
                ReplyKind = ToProbeReplyKind(reply, station.State),
                RawReply = reply.ToString(),
                station.CallConfidence,
                station.Patience,
            }));
    return 0;
}

static bool TryParseInput(string[] arguments, out StationResponseProbeInput input)
{
    string? call = null;
    string? initial = null;
    string? copiedCall = null;
    string? messages = null;
    int seed = 24_680;
    for (int index = 0; index < arguments.Length; index += 2)
    {
        if (index + 1 >= arguments.Length)
        {
            input = default;
            return false;
        }

        string option = arguments[index];
        string value = arguments[index + 1];
        if (option.Equals("--call", StringComparison.OrdinalIgnoreCase))
        {
            call = value.ToUpperInvariant();
        }
        else if (option.Equals("--initial", StringComparison.OrdinalIgnoreCase))
        {
            initial = value;
        }
        else if (option.Equals("--copied-call", StringComparison.OrdinalIgnoreCase))
        {
            copiedCall = value.ToUpperInvariant();
        }
        else if (option.Equals("--messages", StringComparison.OrdinalIgnoreCase))
        {
            messages = value;
        }
        else if (option.Equals("--seed", StringComparison.OrdinalIgnoreCase)
            && Int32.TryParse(value, CultureInfo.InvariantCulture, out int parsedSeed))
        {
            seed = parsedSeed;
        }
        else
        {
            input = default;
            return false;
        }
    }

    if (String.IsNullOrWhiteSpace(call)
        || String.IsNullOrWhiteSpace(copiedCall)
        || !TryParseState(initial, out OperatorState initialState)
        || !TryParseMessages(messages, out StationMessage parsedMessages))
    {
        input = default;
        return false;
    }

    input = new(call, initialState, copiedCall, parsedMessages, seed);
    return true;
}

static bool TryParseState(string? value, out OperatorState state)
{
    state = value?.ToLowerInvariant() switch
    {
        "need-previous-end" => OperatorState.NeedPreviousEnd,
        "need-qso" => OperatorState.NeedQso,
        "need-number" => OperatorState.NeedNumber,
        "need-call" => OperatorState.NeedCall,
        "need-call-and-number" => OperatorState.NeedCallAndNumber,
        "need-end" => OperatorState.NeedEnd,
        _ => default,
    };
    return value is not null && value.ToLowerInvariant() is
        "need-previous-end" or "need-qso" or "need-number" or "need-call"
        or "need-call-and-number" or "need-end";
}

static bool TryParseMessages(string? value, out StationMessage messages)
{
    messages = StationMessage.None;
    if (String.IsNullOrWhiteSpace(value))
    {
        return false;
    }

    foreach (string item in value.Split(',', StringSplitOptions.TrimEntries))
    {
        StationMessage message = item.ToLowerInvariant() switch
        {
            "cq" => StationMessage.Cq,
            "number" => StationMessage.Number,
            "thank-you" => StationMessage.ThankYou,
            "his-call" => StationMessage.HisCall,
            "before" => StationMessage.Before,
            "question" => StationMessage.Question,
            "nil" => StationMessage.Nil,
            _ => StationMessage.None,
        };
        if (message == StationMessage.None)
        {
            return false;
        }

        messages |= message;
    }

    return true;
}

static string ToProbeState(OperatorState state) => state switch
{
    OperatorState.NeedPreviousEnd => "need-previous-end",
    OperatorState.NeedQso => "need-qso",
    OperatorState.NeedNumber => "need-number",
    OperatorState.NeedCall => "need-call",
    OperatorState.NeedCallAndNumber => "need-call-and-number",
    OperatorState.NeedEnd => "need-end",
    OperatorState.Done => "done",
    OperatorState.Failed => "failed",
    _ => throw new InvalidOperationException($"Unknown operator state '{state}'."),
};

static string ToProbeMessages(StationMessage messages) => String.Join(
    ',', new[]
    {
        (StationMessage.Cq, "cq"),
        (StationMessage.Number, "number"),
        (StationMessage.ThankYou, "thank-you"),
        (StationMessage.HisCall, "his-call"),
        (StationMessage.Before, "before"),
        (StationMessage.Question, "question"),
        (StationMessage.Nil, "nil"),
    }.Where(item => (messages & item.Item1) != 0).Select(item => item.Item2));

static string ToProbeReplyKind(StationReply reply, OperatorState state) => reply switch
{
    StationReply.None => "none",
    StationReply.MyCall when state == OperatorState.NeedQso => "call",
    StationReply.MyCall => "call-correction",
    StationReply.NumberQuestion or StationReply.Again => "exchange-request",
    StationReply.Number or StationReply.RogerNumber or StationReply.RogerNumberTwice =>
        "exchange",
    _ => "call-correction",
};

readonly record struct StationResponseProbeInput(
    string Call,
    OperatorState InitialState,
    string CopiedCall,
    StationMessage Messages,
    int Seed);
