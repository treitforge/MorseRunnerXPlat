using System.Reflection;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatLoggingTarget : IParityTarget
{
    public Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string[] values = scenario.Id switch
        {
            "logging.scoring-rate-and-results" => ObserveScoring(),
            "logging.qso-model" => ObserveQsoContract(),
            _ => [],
        };
        bool matches = values.SequenceEqual(
            scenario.ExpectedValues,
            StringComparer.Ordinal);
        return Task.FromResult(
            new ParityObservation(
                matches ? ParityTargetOutcome.Passed : ParityTargetOutcome.Failed,
                values,
                matches ? null : DomainErrorCodes.UnsupportedCapability,
                "MorseRunner.Domain and MorseRunner.Engine"));
    }

    private static string[] ObserveScoring()
    {
        var values = new List<string>
        {
            $"score[-1]={ScoreFormatter.Format(-1)}",
            $"score[0]={ScoreFormatter.Format(0)}",
            $"score[123]={ScoreFormatter.Format(123)}",
            $"score[999999]={ScoreFormatter.Format(999_999)}",
        };
        var multipliers = new MultiplierSet();
        multipliers.Apply("OR;WA;OR;CA");
        values.Add($"multiplier-count={multipliers.Values.Count}");
        for (int index = 0; index < multipliers.Values.Count; index++)
        {
            values.Add($"multiplier[{index}]={multipliers.Values[index]}");
        }

        Qso qso = new Qso()
            .WithColumnError(0)
            .WithColumnError(5)
            .WithColumnError(31);
        values.Add($"column-flags={qso.ColumnErrorFlags:X8}");
        values.Add($"column-0={qso.HasColumnError(0)}");
        values.Add($"column-1={qso.HasColumnError(1)}");
        values.Add($"column-5={qso.HasColumnError(5)}");
        values.Add($"column-31={qso.HasColumnError(31)}");
        return [.. values];
    }

    private static string[] ObserveQsoContract()
    {
        var values = new List<string>();
        (string Id, string LegacyName, LogError Value)[] errors =
        [
            ("lecall", "leCALL", LogError.Call),
            ("lechk", "leCHK", LogError.Check),
            ("leclass", "leCLASS", LogError.Class),
            ("ledup", "leDUP", LogError.Duplicate),
            ("leerr", "leERR", LogError.Error),
            ("lename", "leNAME", LogError.Name),
            ("lenil", "leNIL", LogError.Nil),
            ("lenone", "leNONE", LogError.None),
            ("lenr", "leNR", LogError.Number),
            ("leprec", "lePREC", LogError.Precedence),
            ("lepwr", "lePWR", LogError.Power),
            ("leqth", "leQTH", LogError.Qth),
            ("lerst", "leRST", LogError.Rst),
            ("lesec", "leSEC", LogError.Section),
            ("lesoc", "leSOC", LogError.Society),
            ("lest", "leST", LogError.State),
            ("lezn", "leZN", LogError.Zone),
        ];
        foreach ((string id, string legacyName, LogError value) in errors)
        {
            values.Add(
                $"legacy.log.error.{id}|{legacyName}|"
                + $"{{\"type\":\"TLogError\",\"ordinal\":{(int)value}}}");
        }

        (string Id, string LegacyName, string Property, string LegacyType)[] fields =
        [
            ("call", "Call", nameof(Qso.Call), "string"),
            ("check", "Check", nameof(Qso.Check), "integer"),
            ("columnerrorflags", "ColumnErrorFlags", nameof(Qso.ColumnErrorFlags), "Integer"),
            ("dupe", "Dupe", nameof(Qso.IsDuplicate), "boolean"),
            ("err", "Err", nameof(Qso.ErrorText), "string"),
            ("exch1", "Exch1", nameof(Qso.Exchange1), "string"),
            ("exch1error", "Exch1Error", nameof(Qso.Exchange1Error), "TLogError"),
            ("exch1exerror", "Exch1ExError", nameof(Qso.Exchange1SecondaryError), "TLogError"),
            ("exch2", "Exch2", nameof(Qso.Exchange2), "string"),
            ("exch2error", "Exch2Error", nameof(Qso.Exchange2Error), "TLogError"),
            ("exch2exerror", "Exch2ExError", nameof(Qso.Exchange2SecondaryError), "TLogError"),
            ("excherror", "ExchError", nameof(Qso.ExchangeError), "TLogError"),
            ("multstr", "MultStr", nameof(Qso.Multiplier), "string"),
            ("nr", "Nr", nameof(Qso.Number), "integer"),
            ("pfx", "Pfx", nameof(Qso.Prefix), "string"),
            ("points", "Points", nameof(Qso.Points), "integer"),
            ("prec", "Prec", nameof(Qso.Precedence), "string"),
            ("rawcallsign", "RawCallsign", nameof(Qso.RawCallsign), "string"),
            ("rst", "Rst", nameof(Qso.Rst), "integer"),
            ("sect", "Sect", nameof(Qso.Section), "string"),
            ("t", "T", nameof(Qso.Timestamp), "TDateTime"),
            ("truecall", "TrueCall", nameof(Qso.TrueCall), "string"),
            ("truecheck", "TrueCheck", nameof(Qso.TrueCheck), "integer"),
            ("trueexch1", "TrueExch1", nameof(Qso.TrueExchange1), "string"),
            ("trueexch2", "TrueExch2", nameof(Qso.TrueExchange2), "string"),
            ("truenr", "TrueNr", nameof(Qso.TrueNumber), "integer"),
            ("trueprec", "TruePrec", nameof(Qso.TruePrecedence), "string"),
            ("truerst", "TrueRst", nameof(Qso.TrueRst), "integer"),
            ("truesect", "TrueSect", nameof(Qso.TrueSection), "string"),
            ("truewpm", "TrueWpm", nameof(Qso.TrueWpm), "string"),
        ];
        Type qsoType = typeof(Qso);
        foreach ((string id, string legacyName, string property, string legacyType) in fields)
        {
            _ = qsoType.GetProperty(property, BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException(
                    $"QSO property '{property}' is missing.");
            values.Add(
                $"legacy.log.qso-field.{id}|{legacyName}|"
                + $"{{\"record\":\"TQso\",\"field\":\"{legacyName}\","
                + $"\"type\":\"{legacyType}\"}}");
        }

        return [.. values];
    }
}
