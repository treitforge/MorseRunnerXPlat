using System.Globalization;
using MorseRunner.Domain;
using MorseRunner.Dsp;

namespace MorseRunner.Engine.Tests;

public sealed class SimulationAndScoringTests
{
    [Fact]
    public void OperatorTransitionsAndCallMatchingAreDeterministic()
    {
        var value = new SimulatedOperator(
            "W7SST",
            OperatorState.NeedPreviousEnd,
            new LegacyRandom(12_345),
            OperatorRunMode.Pileup);

        Assert.Equal(3, value.Skills);
        Assert.Equal(5, value.Patience);
        value.Receive(StationMessage.Cq);
        Assert.Equal(OperatorState.NeedQso, value.State);
        Assert.Equal(8, value.Patience);
        Assert.Equal(CallMatch.Yes, value.MatchCall("W7SST"));
        Assert.Equal(100, value.CallConfidence);
        Assert.Equal(CallMatch.Almost, value.MatchCall("SST"));
        Assert.Equal(60, value.CallConfidence);
    }

    [Fact]
    public void OperatorNormalAndCallCorrectionFlowsMatchLegacyStates()
    {
        var normal = new SimulatedOperator(
            "K1ABC",
            OperatorState.NeedQso,
            new LegacyRandom(24_680),
            OperatorRunMode.SingleCall);

        normal.Receive(StationMessage.HisCall, "K1ABC");
        Assert.Equal(OperatorState.NeedNumber, normal.State);
        normal.Receive(StationMessage.Number);
        Assert.Equal(OperatorState.NeedEnd, normal.State);
        normal.Receive(StationMessage.ThankYou);
        Assert.Equal(OperatorState.Done, normal.State);

        var correction = new SimulatedOperator(
            "K1ABC",
            OperatorState.NeedQso,
            new LegacyRandom(24_680),
            OperatorRunMode.SingleCall);

        correction.Receive(StationMessage.HisCall, "K1AB");
        Assert.Equal(OperatorState.NeedCallAndNumber, correction.State);
        correction.Receive(StationMessage.Number);
        Assert.Equal(OperatorState.NeedCall, correction.State);
        correction.Receive(StationMessage.HisCall, "K1ABC");
        Assert.Equal(OperatorState.NeedEnd, correction.State);
        correction.Receive(StationMessage.ThankYou);
        Assert.Equal(OperatorState.Done, correction.State);
    }

    [Fact]
    public void CwtRemoteExchangeIncludesNameAndMemberNumber()
    {
        var station = new SimulatedStation(
            new StationIdentity(
                "K1ABC",
                "599",
                Number: 0,
                "DAVID",
                "123"),
            wordsPerMinute: 25,
            pitchOffsetHz: 0,
            new LegacyRandom(12_345),
            OperatorRunMode.Pileup,
            contestId: new("scCwt"));

        Assert.Equal("DAVID  123", station.ObserveExchangeForParity());
    }

    [Fact]
    public void FieldDayRemoteExchangeIncludesClassAndSection()
    {
        var station = new SimulatedStation(
            new StationIdentity(
                "K1ABC",
                "599",
                Number: 0,
                "3A",
                "OR"),
            wordsPerMinute: 25,
            pitchOffsetHz: 0,
            new LegacyRandom(12_345),
            OperatorRunMode.Pileup,
            contestId: new("scFieldDay"));

        Assert.Equal("3A OR", station.ObserveExchangeForParity());
    }

    [Theory]
    [InlineData("CO", "DAVID CO")]
    [InlineData("", "DAVID")]
    public void NaqpRemoteExchangeIncludesNameAndOptionalLocation(
        string location,
        string expected)
    {
        var station = new SimulatedStation(
            new StationIdentity(
                "K1ABC",
                "599",
                Number: 0,
                "DAVID",
                location),
            wordsPerMinute: 25,
            pitchOffsetHz: 0,
            new LegacyRandom(12_345),
            OperatorRunMode.Pileup,
            contestId: new("scNaQp"));

        Assert.Equal(expected, station.ObserveExchangeForParity());
    }

    [Theory]
    [InlineData(7, "5NN007")]
    [InlineData(1234, "5NN1234")]
    public void HstRemoteExchangeUsesUncutMinimumThreeDigitSerial(
        int serialNumber,
        string expected)
    {
        var station = new SimulatedStation(
            new StationIdentity(
                "K1ABC",
                "599",
                serialNumber,
                "599",
                serialNumber.ToString(CultureInfo.InvariantCulture)),
            wordsPerMinute: 25,
            pitchOffsetHz: 0,
            new LegacyRandom(12_345),
            OperatorRunMode.Hst,
            contestId: new("scHst"));

        Assert.Equal(expected, station.ObserveExchangeForParity());
    }

    [Fact]
    public void WpxMidContestRemoteExchangeUsesTwoDigitMinimumSerial()
    {
        var station = new SimulatedStation(
            new StationIdentity(
                "K1ABC",
                "599",
                Number: 57,
                "599",
                "57"),
            wordsPerMinute: 25,
            pitchOffsetHz: 0,
            new LegacyRandom(12_345),
            OperatorRunMode.Wpx,
            contestId: new("scWpx"),
            serialNumberRange: SerialNumberRangeMode.MidContest);

        Assert.Equal("5NN57", station.ObserveExchangeForParity());
    }

    [Fact]
    public void WpxCustomRangeRemoteExchangePreservesMinimumDigitWidth()
    {
        var station = new SimulatedStation(
            new StationIdentity(
                "K1ABC",
                "599",
                Number: 7,
                "599",
                "7"),
            wordsPerMinute: 25,
            pitchOffsetHz: 0,
            new LegacyRandom(12_345),
            OperatorRunMode.Wpx,
            contestId: new("scWpx"),
            serialNumberRange: SerialNumberRangeMode.Custom,
            customSerialNumberMinimum: 1,
            customSerialNumberMinimumDigits: 2);

        Assert.Equal("5NNT7", station.ObserveExchangeForParity());
    }

    [Fact]
    public void QsoColumnErrorsUseAllThirtyTwoBits()
    {
        Qso value = new Qso()
            .WithColumnError(0)
            .WithColumnError(5)
            .WithColumnError(31);

        Assert.Equal(0x80000021U, value.ColumnErrorFlags);
        Assert.True(value.HasColumnError(0));
        Assert.False(value.HasColumnError(1));
        Assert.True(value.HasColumnError(5));
        Assert.True(value.HasColumnError(31));
    }

    [Fact]
    public void MultipliersAreUniqueAndSorted()
    {
        var values = new MultiplierSet();

        values.Apply("OR;WA;OR;CA");

        Assert.Equal(["CA", "OR", "WA"], values.Values);
        Assert.Equal("   123", ScoreFormatter.Format(123));
    }
}
