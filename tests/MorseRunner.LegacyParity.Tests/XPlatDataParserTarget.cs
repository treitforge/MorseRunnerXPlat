using MorseRunner.Domain;
using MorseRunner.Dsp;
using MorseRunner.Engine;
using MorseRunner.Infrastructure;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatDataParserTarget : IParityTarget
{
    private static readonly string[] Calls =
    [
        "W7SST",
        "F6/W7SST",
        "W7SST/P",
        "RC2FX",
        "KG4AA",
        "KG4ABC",
        "CE9/W7SST",
        "N0CALL",
    ];

    public Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string[] values = scenario.Id == "data.legacy-parsers"
            ? Observe()
            : [];
        bool matches = values.SequenceEqual(
            scenario.ExpectedValues,
            StringComparer.Ordinal);
        return Task.FromResult(
            new ParityObservation(
                matches ? ParityTargetOutcome.Passed : ParityTargetOutcome.Failed,
                values,
                matches ? null : DomainErrorCodes.UnsupportedCapability,
                "MorseRunner.Infrastructure"));
    }

    private static string[] Observe()
    {
        var values = new List<string>();
        foreach (string call in Calls)
        {
            values.Add(
                $"call[{call}]={CallsignParser.ExtractCallsign(call)}"
                + $"|{CallsignParser.ExtractPrefix(call)}"
                + $"|{CallsignParser.ExtractPrefix(call, deleteTrailingLetters: false)}");
        }

        var dxcc = new DxccDatabase();
        foreach (string call in Calls)
        {
            values.Add(
                dxcc.TryFind(call, out DxccRecord? record)
                    ? $"dxcc[{call}]={record!.Entity}|{record.Continent}"
                        + $"|{record.ItuZones}|{record.CqZones}"
                    : $"dxcc[{call}]=not-found");
        }

        SweepstakesParseResult own =
            SweepstakesExchangeParser.ParseOwn("123 A 72 OR");
        values.Add(
            "my-ss=123 A 72 OR"
            + $"|{own.Exchange.SerialNumber}"
            + $"|{own.Exchange.Precedence}"
            + $"|{own.Exchange.Check}"
            + $"|{own.Exchange.Section}");

        SweepstakesParseResult entered =
            SweepstakesExchangeParser.ParseEntered(
                "W7SST",
                "123 A W7SST 72 OR");
        values.Add($"ss-exchange={entered.Exchange.Summary}|{entered.Error}");
        SweepstakesParseResult rotation =
            SweepstakesExchangeParser.ParseEntered(
                "K1ABC",
                "11 22 33 ID 44 55");
        values.Add($"ss-rotation={rotation.Exchange.Summary}|{rotation.Error}");

        var generator = new SerialNumberGenerator(
            new LegacyRandom(12_345),
            minimum: 10,
            exclusiveMaximum: 20);
        for (int index = 0; index < 8; index++)
        {
            values.Add($"serial[{index}]={generator.Next()}");
        }

        foreach (ExchangeType1 value in Enum.GetValues<ExchangeType1>())
        {
            values.Add($"exchange1-enum[{(int)value}]={(int)value}");
        }

        foreach (ExchangeType2 value in Enum.GetValues<ExchangeType2>())
        {
            values.Add($"exchange2-enum[{(int)value}]={(int)value}");
        }

        return [.. values];
    }
}
