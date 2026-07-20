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
            "contest.exchange-shapes" =>
                ObserveExchangeShapes(
                    ContestExchangeShapesInput.Parse(
                        scenario).ContestIds),
            "contest.cq-wpx-scoring" =>
                await ObserveCqWpxScoringAsync(cancellationToken),
            "contest.cwt-scoring" =>
                await ObserveCwtScoringAsync(cancellationToken),
            "contest.remaining-scoring" =>
                await ObserveRemainingScoringAsync(cancellationToken),
            _ => [],
        };
        bool matches = values.SequenceEqual(
            scenario.ExpectedValues,
            StringComparer.Ordinal);
        return new ParityObservation(
            matches ? ParityTargetOutcome.Passed : ParityTargetOutcome.Failed,
            values,
            matches
                ? null
                : scenario.Id == "contest.exchange-shapes"
                    ? "contest-exchange-shape-mismatch"
                    : DomainErrorCodes.UnsupportedCapability,
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

    private static string[] ObserveExchangeShapes(
        IReadOnlyList<string> contestIds)
    {
        return
        [
            .. contestIds.Select(
                contestId =>
                {
                    ContestRules rules = ContestRulesCatalog.Get(
                        new ContestId(contestId));
                    ContestDefinition definition = ContestCatalog.Get(
                        rules.Id);
                    ContestValidation exchange = rules.ValidateMyExchange(
                        definition.ExchangeDefault);
                    return $"{rules.Id.Value}"
                        + $"|sent={(int)rules.SentExchangeTypes.First},"
                        + $"{(int)rules.SentExchangeTypes.Second}"
                        + $"|recv={(int)rules.ReceivedExchangeTypes.First},"
                        + $"{(int)rules.ReceivedExchangeTypes.Second}"
                        + $"|default-valid={exchange.IsValid}"
                        + $"|farnsworth={rules.AllowsFarnsworth}";
                }),
        ];
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

    private static async Task<string[]> ObserveRemainingScoringAsync(
        CancellationToken cancellationToken)
    {
        var values = new List<string>();
        await AddContestAsync(
            values,
            "field-day",
            new("scFieldDay"),
            [
                new("K1ABC", "599", "3A", "OR"),
                new("K2XYZ", "599", "1D", "EMA"),
                new("K1ABC", "599", "3A", "OR"),
            ],
            cancellationToken);
        await AddContestAsync(
            values,
            "naqp",
            new("scNaQp"),
            [
                new("W1ABC", string.Empty, "ALEX", "MA"),
                new("VE3ABC", string.Empty, "PAT", "ON"),
                new("DL1ABC", string.Empty, "HANS", "DX"),
                new("W1ABC", string.Empty, "ALEX", "MA"),
            ],
            cancellationToken);
        await AddContestAsync(
            values,
            "hst",
            new("scHst"),
            [
                new("E", "599", "1", string.Empty),
                new("T", "599", "2", string.Empty),
                new("K1ABC", "599", "3", string.Empty),
                new("K1ABC", "599", "4", string.Empty),
            ],
            cancellationToken,
            hst: true);
        await AddContestAsync(
            values,
            "cqww",
            new("scCQWW"),
            [
                new("W1ABC", "599", string.Empty, "3"),
                new("VE3ABC", "599", string.Empty, "4"),
                new("DL1ABC", "599", string.Empty, "14"),
                new("K1ABC/MM", "599", string.Empty, "5"),
                new("DL1ABC", "599", string.Empty, "14"),
            ],
            cancellationToken);
        await AddContestAsync(
            values,
            "arrl-dx",
            new("scArrlDx"),
            [
                new("DL1ABC", "599", string.Empty, "100"),
                new("F6ABC", "599", string.Empty, "100"),
                new("DL1ABC", "599", string.Empty, "100"),
            ],
            cancellationToken);
        await AddContestAsync(
            values,
            "sst",
            new("scSst"),
            [
                new("W1ABC", string.Empty, "BRUCE", "MA"),
                new("VE3ABC", string.Empty, "PAT", "ON"),
                new("DL1ABC", string.Empty, "HANS", "DX"),
                new("W1ABC", string.Empty, "BRUCE", "MA"),
            ],
            cancellationToken);
        await AddContestAsync(
            values,
            "all-ja",
            new("scAllJa"),
            [
                new("JA1ABC", "599", string.Empty, "10H"),
                new("JA2XYZ", "599", string.Empty, "101M"),
                new("JA1ABC", "599", string.Empty, "10H"),
            ],
            cancellationToken);
        await AddContestAsync(
            values,
            "acag",
            new("scAcag"),
            [
                new("JA1ABC", "599", string.Empty, "1002H"),
                new("JA2XYZ", "599", string.Empty, "01001M"),
                new("JA1ABC", "599", string.Empty, "1002H"),
            ],
            cancellationToken);
        await AddContestAsync(
            values,
            "iaru-hf",
            new("scIaruHf"),
            [
                new("W1ABC", "599", string.Empty, "6"),
                new("VE3ABC", "599", string.Empty, "9"),
                new("DL1ABC", "599", string.Empty, "28"),
                new("W1AW", "599", string.Empty, "ARRL"),
                new("DL1ABC", "599", string.Empty, "28"),
            ],
            cancellationToken);
        await AddContestAsync(
            values,
            "arrl-ss",
            new("scArrlSS"),
            [
                new("K1ABC", string.Empty, "1 A", "72 OR"),
                new("K2XYZ", string.Empty, "2 B", "71 ID"),
                new("K1ABC", string.Empty, "3 A", "72 OR"),
            ],
            cancellationToken);
        return [.. values];
    }

    private static async Task AddContestAsync(
        List<string> values,
        string name,
        ContestId contestId,
        IReadOnlyList<ReceivedQso> contacts,
        CancellationToken cancellationToken,
        bool hst = false)
    {
        ContestDefinition definition = ContestCatalog.Get(contestId);
        ContestValidation valid = ContestQsoRules.ValidateOwnExchange(
            contestId,
            definition.ExchangeDefault);
        ContestValidation invalid = ContestQsoRules.ValidateOwnExchange(
            contestId,
            string.Empty);
        values.Add($"{name}.valid={valid.IsValid}|{valid.Error}");
        values.Add($"{name}.invalid={invalid.IsValid}|{invalid.Error}");

        await using var engine =
            new MorseRunnerEngine(_ => new NullAudioSink());
        SessionSettings settings = SessionSettings.CreateDefault(12_345) with
        {
            ContestId = contestId,
            StationCall = "W7SST",
        };
        SessionHandle handle = await engine.CreateSessionAsync(
            settings,
            cancellationToken);
        ClientId client = new(name + "-parity");
        await engine.ExecuteAsync(
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                client),
            cancellationToken);

        for (int index = 0; index < contacts.Count; index++)
        {
            ReceivedQso contact = contacts[index];
            CommandResult result = await engine.ExecuteAsync(
                new LogQsoCommand(
                    RequestId.New(),
                    handle.SessionId,
                    client,
                    contact.Call,
                    contact.Rst,
                    contact.Exchange1,
                    contact.Exchange2),
                cancellationToken);
            Assert.True(result.Accepted, result.Message);
            IReadOnlyList<Qso> qsos =
                engine.GetCompletedQsos(handle.SessionId);
            Qso qso = qsos[^1];
            int score = engine.GetSnapshot(handle.SessionId).Score;
            if (hst)
            {
                values.Add(
                    $"{name}.qso[{index}]={qso.Call}|{qso.Points}"
                    + $"|{qso.IsDuplicate}|{score}");
                continue;
            }

            int verifiedPoints = qsos
                .Where(value => !value.IsDuplicate)
                .Sum(value => value.Points);
            int multiplierCount = qsos
                .Where(value => !value.IsDuplicate)
                .SelectMany(value => value.Multiplier.Split(';'))
                .Distinct(StringComparer.Ordinal)
                .Count();
            values.Add(
                $"{name}.qso[{index}]={qso.Call}|{qso.Multiplier}"
                + $"|{qso.Points}|{qso.IsDuplicate}|{verifiedPoints}"
                + $"|{multiplierCount}|{score}");
        }
    }

    private sealed record ReceivedQso(
        string Call,
        string Rst,
        string Exchange1,
        string Exchange2);
}
