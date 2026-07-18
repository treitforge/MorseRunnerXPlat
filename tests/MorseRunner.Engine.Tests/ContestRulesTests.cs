using MorseRunner.Domain;

namespace MorseRunner.Engine.Tests;

public sealed class ContestRulesTests
{
    [Fact]
    public void EveryContestHasOneRulesEntry()
    {
        Assert.Equal(ContestCatalog.All.Count, ContestRulesCatalog.All.Count);
        Assert.Equal(
            ContestCatalog.All.Select(definition => definition.Id),
            ContestRulesCatalog.All.Select(rules => rules.Id));
    }

    [Fact]
    public void NaqpUsesNameAndLocationExchangeTypes()
    {
        ContestRules rules = ContestRulesCatalog.Get(new ContestId("scNaQp"));

        Assert.Equal(ExchangeType1.OperatorName, rules.SentExchangeTypes.First);
        Assert.Equal(
            ExchangeType2.NaqpSecondField,
            rules.SentExchangeTypes.Second);
        Assert.True(ContestRules.ValidateMyCall("W7SST").IsValid);
        Assert.True(rules.ValidateMyExchange("ALEX ON").IsValid);
    }
}
