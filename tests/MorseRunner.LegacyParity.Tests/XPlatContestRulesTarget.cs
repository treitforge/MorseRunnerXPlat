using MorseRunner.Audio;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatContestRulesTarget : IParityTarget
{
    public async Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string[] values = scenario.Id switch
        {
            "contest.legacy-implementations" => Observe(),
            "contest.cq-wpx-scoring" =>
                await ObserveCqWpxScoringAsync(cancellationToken),
            "contest.cwt-scoring" =>
                await ObserveCwtScoringAsync(cancellationToken),
            _ => [],
        };
        bool matches = values.SequenceEqual(
            scenario.ExpectedValues,
            StringComparer.Ordinal);
        return new ParityObservation(
            matches ? ParityTargetOutcome.Passed : ParityTargetOutcome.Failed,
            values,
            matches ? null : DomainErrorCodes.UnsupportedCapability,
            "MorseRunner.Engine");
    }

    private static string[] Observe()
    {
        var values = new List<string>();
        for (int index = 0; index < ContestRulesCatalog.All.Count; index++)
        {
            ContestRules rules = ContestRulesCatalog.All[index];
            ContestDefinition definition = ContestCatalog.Get(rules.Id);
            ContestValidation call = ContestRules.ValidateMyCall("W7SST");
            ContestValidation exchange =
                rules.ValidateMyExchange(definition.ExchangeDefault);
            values.Add(
                $"contest[{index}].load={ContestRules.LoadCallHistory("W7SST")}");
            values.Add($"contest[{index}].my-call={call.IsValid}|{call.Error}");
            values.Add(
                $"contest[{index}].sent-types="
                + $"{(int)rules.SentExchangeTypes.First},"
                + $"{(int)rules.SentExchangeTypes.Second}");
            values.Add(
                $"contest[{index}].recv-types="
                + $"{(int)rules.ReceivedExchangeTypes.First},"
                + $"{(int)rules.ReceivedExchangeTypes.Second}");
            values.Add(
                $"contest[{index}].my-exchange={exchange.IsValid}|{exchange.Error}");
            values.Add($"contest[{index}].farnsworth={rules.AllowsFarnsworth}");
        }

        return [.. values];
    }

    private static async Task<string[]> ObserveCqWpxScoringAsync(
        CancellationToken cancellationToken)
    {
        var values = new List<string>();
        ContestValidation invalid = CqWpxContestRules.ValidateReceivedQso(
            "AB",
            "599",
            "1");
        ContestValidation valid = CqWpxContestRules.ValidateReceivedQso(
            "K1ABC",
            "599",
            "1");
        values.Add($"call[AB]={invalid.IsValid}|{invalid.Error}");
        values.Add($"call[K1ABC]={valid.IsValid}|{valid.Error}");

        await using var engine =
            new MorseRunnerEngine(_ => new NullAudioSink());
        SessionHandle handle = await engine.CreateSessionAsync(
            SessionSettings.CreateDefault(12_345),
            cancellationToken);
        ClientId client = new("cq-wpx-parity");
        await engine.ExecuteAsync(
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                client),
            cancellationToken);

        string[] calls = ["K1ABC", "K2XYZ", "K1ABC", "DL2XYZ", "F6/W7SST"];
        for (int index = 0; index < calls.Length; index++)
        {
            await engine.ExecuteAsync(
                new LogQsoCommand(
                    RequestId.New(),
                    handle.SessionId,
                    client,
                    calls[index],
                    "599",
                    (index + 1).ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    string.Empty),
                cancellationToken);
            IReadOnlyList<Qso> qsos =
                engine.GetCompletedQsos(handle.SessionId);
            Qso qso = qsos[^1];
            int verifiedPoints = qsos
                .Where(value => !value.IsDuplicate)
                .Sum(value => value.Points);
            int multiplierCount = qsos
                .Where(value => !value.IsDuplicate)
                .Select(value => value.Multiplier)
                .Distinct(StringComparer.Ordinal)
                .Count();
            values.Add(
                $"qso[{index}]={qso.Call}|{qso.Prefix}|{qso.Multiplier}"
                + $"|{qso.Points}|{qso.IsDuplicate}|{verifiedPoints}"
                + $"|{multiplierCount}|{engine.GetSnapshot(handle.SessionId).Score}");
        }

        return [.. values];
    }

    private static async Task<string[]> ObserveCwtScoringAsync(
        CancellationToken cancellationToken)
    {
        var values = new List<string>();
        ContestValidation invalid = CwtContestRules.ValidateReceivedQso(
            "AB",
            "DAVID",
            "123");
        ContestValidation valid = CwtContestRules.ValidateReceivedQso(
            "K1ABC",
            "DAVID",
            "123");
        values.Add($"call[AB]={invalid.IsValid}|{invalid.Error}");
        values.Add($"call[K1ABC]={valid.IsValid}|{valid.Error}");
        values.Add($"member[123]={CwtContestRules.IsMemberExchange("123")}");
        values.Add($"member[OR]={CwtContestRules.IsMemberExchange("OR")}");
        values.Add($"member[]={CwtContestRules.IsMemberExchange(string.Empty)}");

        await using var engine =
            new MorseRunnerEngine(_ => new NullAudioSink());
        SessionSettings settings = SessionSettings.CreateDefault(12_345) with
        {
            ContestId = new("scCwt"),
        };
        SessionHandle handle = await engine.CreateSessionAsync(
            settings,
            cancellationToken);
        ClientId client = new("cwt-parity");
        await engine.ExecuteAsync(
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                client),
            cancellationToken);

        string[] calls =
            ["K1ABC", "K2XYZ", "K1ABC", "K2XYZ/P", "K2XYZ/P"];
        for (int index = 0; index < calls.Length; index++)
        {
            await engine.ExecuteAsync(
                new LogQsoCommand(
                    RequestId.New(),
                    handle.SessionId,
                    client,
                    calls[index],
                    "599",
                    "DAVID",
                    "123"),
                cancellationToken);
            IReadOnlyList<Qso> qsos =
                engine.GetCompletedQsos(handle.SessionId);
            Qso qso = qsos[^1];
            int verifiedPoints = qsos
                .Where(value => !value.IsDuplicate)
                .Sum(value => value.Points);
            int multiplierCount = qsos
                .Where(value => !value.IsDuplicate)
                .Select(value => value.Multiplier)
                .Distinct(StringComparer.Ordinal)
                .Count();
            values.Add(
                $"qso[{index}]={qso.Call}|{qso.Multiplier}"
                + $"|{qso.Points}|{qso.IsDuplicate}|{verifiedPoints}"
                + $"|{multiplierCount}|{engine.GetSnapshot(handle.SessionId).Score}");
        }

        return [.. values];
    }
}
