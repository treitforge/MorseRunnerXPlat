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

    [Theory]
    [InlineData("scWpx", "5NN # EXTRA", true)]
    [InlineData("scCwt", "DAVID 123 EXTRA", true)]
    [InlineData("scFieldDay", "3A OR EXTRA", true)]
    [InlineData("scNaQp", "ALEX ON EXTRA", true)]
    [InlineData("scHst", "5NN # EXTRA", true)]
    [InlineData("scCQWW", "5NN 3 EXTRA", true)]
    [InlineData("scArrlDx", "5NN ON EXTRA", true)]
    [InlineData("scSst", "BRUCE MA EXTRA", true)]
    [InlineData("scAllJa", "5NN 10H EXTRA", true)]
    [InlineData("scAcag", "5NN 1002H EXTRA", true)]
    [InlineData("scIaruHf", "5NN 6 EXTRA", true)]
    [InlineData("scArrlSS", "A 72 OR EXTRA", false)]
    [InlineData("scCwt", "DAVID", false)]
    [InlineData("scNaQp", "ALEX", false)]
    [InlineData("scSst", "BRUCE", false)]
    [InlineData("scIaruHf", "5NN", false)]
    [InlineData("scAllJa", "5NN H", true)]
    [InlineData("scAcag", "5NN H", true)]
    [InlineData("scArrlDx", "5NN KW", false)]
    [InlineData("scArrlSS", "A 72OR", false)]
    [InlineData("scArrlSS", "A 72 OREGON", false)]
    [InlineData("scArrlSS", "A 72 OR", true)]
    [InlineData("scWpx", "5NN #", true)]
    public void OwnExchangeTokenizationMatchesCeBoundaries(
        string contestId,
        string exchange,
        bool expectedValid)
    {
        ContestId id = new(contestId);

        ContestValidation validation = ContestQsoRules.ValidateOwnExchange(
            id,
            exchange);

        Assert.Equal(expectedValid, validation.IsValid);
        if (expectedValid)
        {
            Assert.Empty(validation.Error);
            return;
        }

        Assert.Equal(
            $"Invalid exchange: '{exchange}' - expecting "
                + $"{ContestCatalog.Get(id).ValidationMessage}.",
            validation.Error);
    }

    [Theory]
    [InlineData("scArrlDx", "W7SST", false, false, "W7SST", "", ExchangeType1.Rst, ExchangeType2.StateProvince)]
    [InlineData("scArrlDx", "W7SST", false, true, "W7SST", "F6ABC", ExchangeType1.Rst, ExchangeType2.Power)]
    [InlineData("scArrlDx", "W7SST", true, false, "F6ABC", "", ExchangeType1.Rst, ExchangeType2.Power)]
    [InlineData("scArrlDx", "W7SST", true, true, "F6ABC", "W7SST", ExchangeType1.Rst, ExchangeType2.StateProvince)]
    [InlineData("scArrlDx", "F6ABC", false, false, "F6ABC", "", ExchangeType1.Rst, ExchangeType2.Power)]
    [InlineData("scArrlDx", "F6ABC", false, true, "F6ABC", "W7SST", ExchangeType1.Rst, ExchangeType2.StateProvince)]
    [InlineData("scArrlDx", "F6ABC", true, false, "W7SST", "", ExchangeType1.Rst, ExchangeType2.StateProvince)]
    [InlineData("scArrlDx", "F6ABC", true, true, "W7SST", "F6ABC", ExchangeType1.Rst, ExchangeType2.Power)]
    [InlineData("scNaQp", "W7SST", false, false, "W7SST", "", ExchangeType1.OperatorName, ExchangeType2.NaqpSecondField)]
    [InlineData("scNaQp", "F6ABC", false, false, "F6ABC", "", ExchangeType1.OperatorName, ExchangeType2.NaqpNonNorthAmericaSecondField)]
    [InlineData("scNaQp", "F6ABC", false, true, "F6ABC", "W7SST", ExchangeType1.OperatorName, ExchangeType2.NaqpSecondField)]
    [InlineData("scNaQp", "W7SST", false, true, "W7SST", "F6ABC", ExchangeType1.OperatorName, ExchangeType2.NaqpNonNorthAmericaSecondField)]
    public void DynamicExchangeTypesMatchCeLocalityMatrix(
        string contestId,
        string homeCall,
        bool isSimulatedStation,
        bool isReceivedMessage,
        string stationCall,
        string remoteCall,
        ExchangeType1 expectedFirst,
        ExchangeType2 expectedSecond)
    {
        ExchangeTypes actual = ContestQsoRules.ResolveExchangeTypes(
            new ContestId(contestId),
            homeCall,
            isSimulatedStation,
            isReceivedMessage,
            stationCall,
            remoteCall);

        Assert.Equal(new ExchangeTypes(expectedFirst, expectedSecond), actual);
    }
}
