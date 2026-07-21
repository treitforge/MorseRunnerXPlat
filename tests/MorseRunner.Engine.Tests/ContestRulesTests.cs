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

    [Theory]
    [InlineData(
        "scWpx",
        ExchangeType1.Rst,
        ExchangeType2.SerialNumber,
        false)]
    [InlineData(
        "scCwt",
        ExchangeType1.OperatorName,
        ExchangeType2.GenericField,
        false)]
    [InlineData(
        "scFieldDay",
        ExchangeType1.FieldDayClass,
        ExchangeType2.ArrlSection,
        false)]
    [InlineData(
        "scNaQp",
        ExchangeType1.OperatorName,
        ExchangeType2.NaqpSecondField,
        false)]
    [InlineData(
        "scHst",
        ExchangeType1.Rst,
        ExchangeType2.SerialNumber,
        false)]
    [InlineData(
        "scCQWW",
        ExchangeType1.Rst,
        ExchangeType2.CqZone,
        false)]
    [InlineData(
        "scArrlDx",
        ExchangeType1.Rst,
        ExchangeType2.StateProvince,
        false)]
    [InlineData(
        "scSst",
        ExchangeType1.OperatorName,
        ExchangeType2.GenericField,
        true)]
    [InlineData(
        "scAllJa",
        ExchangeType1.Rst,
        ExchangeType2.JapanPrefecture,
        false)]
    [InlineData(
        "scAcag",
        ExchangeType1.Rst,
        ExchangeType2.JapanCity,
        false)]
    [InlineData(
        "scIaruHf",
        ExchangeType1.Rst,
        ExchangeType2.GenericField,
        false)]
    [InlineData(
        "scArrlSS",
        ExchangeType1.SweepstakesNumberPrecedence,
        ExchangeType2.SweepstakesCheckSection,
        false)]
    public void ContestMetadataMatchesPinnedW7SstToF6AbcBaseline(
        string contestId,
        ExchangeType1 first,
        ExchangeType2 second,
        bool allowsFarnsworth)
    {
        ContestId id = new(contestId);
        ContestRules rules = ContestRulesCatalog.Get(id);
        ContestDefinition definition = ContestCatalog.Get(id);

        Assert.Equal(
            new ExchangeTypes(first, second),
            rules.BaselineSentExchangeTypes);
        Assert.Equal(
            new ExchangeTypes(first, second),
            rules.BaselineReceivedExchangeTypes);
        Assert.Equal(allowsFarnsworth, rules.AllowsFarnsworth);

        ContestValidation validation = rules.ValidateMyExchange(
            definition.ExchangeDefault);
        Assert.True(validation.IsValid, validation.Error);
        Assert.Empty(validation.Error);

        ContestValidation invalid = rules.ValidateMyExchange(string.Empty);
        Assert.False(invalid.IsValid);
        Assert.Equal(
            $"Invalid exchange: '' - expecting "
                + $"{definition.ValidationMessage}.",
            invalid.Error);
    }

    [Fact]
    public void ValidatesOperatorCallsign()
    {
        Assert.True(ContestRules.ValidateMyCall("W7SST").IsValid);
    }
}
