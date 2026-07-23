using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.Engine.Tests;

public sealed class ReceivedExchangeParityTests
{
    public static TheoryData<string, ExchangeType1, ExchangeType2> LegacyExchangeTypes =>
        new()
        {
            { "scWpx", ExchangeType1.Rst, ExchangeType2.SerialNumber },
            { "scCwt", ExchangeType1.OperatorName, ExchangeType2.GenericField },
            { "scFieldDay", ExchangeType1.FieldDayClass, ExchangeType2.ArrlSection },
            { "scNaQp", ExchangeType1.OperatorName, ExchangeType2.NaqpSecondField },
            { "scHst", ExchangeType1.Rst, ExchangeType2.SerialNumber },
            { "scCQWW", ExchangeType1.Rst, ExchangeType2.CqZone },
            { "scArrlDx", ExchangeType1.Rst, ExchangeType2.StateProvince },
            { "scSst", ExchangeType1.OperatorName, ExchangeType2.GenericField },
            { "scAllJa", ExchangeType1.Rst, ExchangeType2.JapanPrefecture },
            { "scAcag", ExchangeType1.Rst, ExchangeType2.JapanCity },
            { "scIaruHf", ExchangeType1.Rst, ExchangeType2.GenericField },
            { "scArrlSS", ExchangeType1.SweepstakesNumberPrecedence, ExchangeType2.SweepstakesCheckSection },
        };

    [Theory]
    [MemberData(nameof(LegacyExchangeTypes))]
    public void ReceivedExchangeTypesMatchTheLegacyContestDefinitions(
        string contestId,
        ExchangeType1 first,
        ExchangeType2 second)
    {
        ExchangeTypes received = ContestRulesCatalog.Get(
            new ContestId(contestId)).BaselineReceivedExchangeTypes;

        Assert.Equal(first, received.First);
        Assert.Equal(second, received.Second);
    }
}
