using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatContestRulesTarget : IParityTarget
{
    public Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string[] values = scenario.Id == "contest.legacy-implementations"
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
                "MorseRunner.Engine"));
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
}
