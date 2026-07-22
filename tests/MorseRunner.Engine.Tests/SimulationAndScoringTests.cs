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
            new DeterministicRandom(12_345),
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
    public void OperatorNormalAndCallCorrectionFlowsMatchPinnedStates()
    {
        var normal = new SimulatedOperator(
            "K1ABC",
            OperatorState.NeedQso,
            new DeterministicRandom(24_680),
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
            new DeterministicRandom(24_680),
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
            new DeterministicRandom(12_345),
            OperatorRunMode.Pileup,
            contestId: new("scCwt"));

        Assert.Equal("DAVID  123", station.CreateExchangeText());
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
            new DeterministicRandom(12_345),
            OperatorRunMode.Pileup,
            contestId: new("scFieldDay"));

        Assert.Equal("3A OR", station.CreateExchangeText());
    }

    [Fact]
    public void SstRemoteExchangeIncludesNameAndLocation()
    {
        var station = new SimulatedStation(
            new StationIdentity(
                "K1ABC",
                "599",
                Number: 0,
                "BRUCE",
                "MA"),
            wordsPerMinute: 25,
            pitchOffsetHz: 0,
            new DeterministicRandom(12_345),
            OperatorRunMode.Pileup,
            contestId: new("scSst"));

        Assert.Equal("BRUCE MA", station.CreateExchangeText());
    }

    [Fact]
    public void SweepstakesRemoteExchangeIncludesCallsignBetweenExchangeFields()
    {
        var station = new SimulatedStation(
            new StationIdentity(
                "K1ABC",
                "599",
                Number: 123,
                "123 A",
                "72 OR"),
            wordsPerMinute: 25,
            pitchOffsetHz: 0,
            new DeterministicRandom(12_345),
            OperatorRunMode.Pileup,
            sweepstakes: true,
            contestId: new("scArrlSS"));

        Assert.Equal(
            "123 A K1ABC 72 OR",
            station.CreateExchangeText());
    }

    [Theory]
    [InlineData("scArrlDx", "MA", "5NN MA")]
    [InlineData("scAllJa", "12H", "5NN 12H")]
    [InlineData("scAcag", "1234H", "5NN 1234H")]
    [InlineData("scIaruHf", "ARRL", "5NN ARRL")]
    public void DefaultTwoFieldRemoteExchangeSeparatesRstAndExchange(
        string contestId,
        string exchange2,
        string expected)
    {
        var station = new SimulatedStation(
            new StationIdentity(
                "K1ABC",
                "599",
                Number: 0,
                "599",
                exchange2),
            wordsPerMinute: 25,
            pitchOffsetHz: 0,
            new DeterministicRandom(12_345),
            OperatorRunMode.Pileup,
            contestId: new(contestId));

        Assert.Equal(expected, station.CreateExchangeText());
    }

    [Theory]
    [InlineData("scAllJa", "109H", "5NN 1ONH")]
    [InlineData("scAcag", "1009H", "5NN 1TTNH")]
    public void JarlRemoteExchangeUsesStationRandomAtCeCheckpoint(
        string contestId,
        string exchange2,
        string expected)
    {
        var random = new DeterministicRandom(12_345);
        var station = new SimulatedStation(
            new StationIdentity(
                "JA1ABC",
                "599",
                Number: 0,
                "599",
                exchange2),
            wordsPerMinute: 25,
            pitchOffsetHz: 0,
            random,
            OperatorRunMode.Pileup,
            contestId: new(contestId));

        _ = random.NextDouble();

        Assert.Equal(expected, station.CreateExchangeText());
    }

    [Theory]
    [InlineData("scCQWW", "K1ABC", "10", "5NN AT")]
    [InlineData("scArrlDx", "JA1ABC", "100", "5NN ATT")]
    public void FullCutNumericRemoteExchangeUsesRetainedR1(
        string contestId,
        string callsign,
        string exchange2,
        string expected)
    {
        var station = new SimulatedStation(
            new StationIdentity(
                callsign,
                "599",
                Number: 0,
                "599",
                exchange2),
            wordsPerMinute: 25,
            pitchOffsetHz: 0,
            new DeterministicRandom(12_345),
            OperatorRunMode.Pileup,
            contestId: new(contestId));

        Assert.Equal(expected, station.CreateExchangeText());
    }

    [Fact]
    public void ArrlDxHigherR1PowerKeepsLeadingDoubleZeroCut()
    {
        var random = new DeterministicRandom(12_345);
        var station = SimulatedStation.CreateCandidate(
            () => new StationIdentity(
                "JA1ABC",
                "599",
                Number: 0,
                "599",
                "100"),
            () => 25,
            random,
            new RandomEffects(random),
            OperatorRunMode.Pileup,
            lids: false,
            sweepstakes: false,
            flutter: false,
            new ContestId("scArrlDx"),
            SerialNumberRangeMode.StartOfContest,
            customSerialNumberMinimum: 1,
            customSerialNumberMinimumDigits: 2);

        Assert.Equal(
            930,
            (int)MathF.Round(
                station.R1 * 1_000f,
                MidpointRounding.ToEven));
        Assert.Equal("5NN 1TT", station.CreateExchangeText());
    }

    [Fact]
    public void CqwwHigherR1ConsumesSuppressedRemoteCutDecisions()
    {
        var random = new DeterministicRandom(12_345);
        var station = SimulatedStation.CreateCandidate(
            () => new StationIdentity(
                "K1ABC",
                "599",
                Number: 0,
                "599",
                "10"),
            () => 25,
            random,
            new RandomEffects(random),
            OperatorRunMode.Pileup,
            lids: false,
            sweepstakes: false,
            flutter: false,
            new ContestId("scCQWW"),
            SerialNumberRangeMode.StartOfContest,
            customSerialNumberMinimum: 1,
            customSerialNumberMinimumDigits: 2);

        Assert.Equal(
            930,
            (int)MathF.Round(
                station.R1 * 1_000f,
                MidpointRounding.ToEven));
        Assert.Equal("5NN 10", station.CreateExchangeText());
        Assert.Equal(
            0x3F15_06E1U,
            BitConverter.SingleToUInt32Bits(random.NextSingle()));
    }

    [Fact]
    public void IaruHeadquartersExchangeUsesRareRemoteRstErrorDraw()
    {
        var random = new DeterministicRandom(12_345);
        var station = new SimulatedStation(
            new StationIdentity(
                "DL1ABC",
                "599",
                Number: 0,
                "599",
                "ARRL"),
            wordsPerMinute: 25,
            pitchOffsetHz: 0,
            random,
            OperatorRunMode.Pileup,
            contestId: new("scIaruHf"));
        for (int draw = 2; draw < 5; draw++)
        {
            _ = random.NextDouble();
        }

        Assert.Equal("ENN ARRL", station.CreateExchangeText());
        Assert.Equal(0.20456027938053012d, random.NextDouble());
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
            new DeterministicRandom(12_345),
            OperatorRunMode.Pileup,
            contestId: new("scNaQp"));

        Assert.Equal(expected, station.CreateExchangeText());
    }

    [Fact]
    public void LidCandidateSendsOneCorrectedSerialThenClearsTheError()
    {
        var random = new DeterministicRandom(16);
        var station = SimulatedStation.CreateCandidate(
            () => new StationIdentity(
                "K1ABC",
                "599",
                Number: 123,
                "599",
                "123"),
            () => 25,
            random,
            new RandomEffects(random),
            OperatorRunMode.Wpx,
            lids: true,
            sweepstakes: false,
            flutter: false,
            new ContestId("scWpx"),
            SerialNumberRangeMode.StartOfContest,
            customSerialNumberMinimum: 1,
            customSerialNumberMinimumDigits: 2);

        Assert.Equal(
            223,
            (int)MathF.Round(
                station.R1 * 1_000f,
                MidpointRounding.ToEven));
        Assert.Equal(
            "5NN124EEEEE 123",
            station.CreateExchangeText());
        Assert.Equal("5NN123", station.CreateExchangeText());
    }

    [Fact]
    public void NonLidCandidateDoesNotSendSerialCorrection()
    {
        var random = new DeterministicRandom(16);
        var station = SimulatedStation.CreateCandidate(
            () => new StationIdentity(
                "K1ABC",
                "599",
                Number: 123,
                "599",
                "123"),
            () => 25,
            random,
            new RandomEffects(random),
            OperatorRunMode.Wpx,
            lids: false,
            sweepstakes: false,
            flutter: false,
            new ContestId("scWpx"),
            SerialNumberRangeMode.StartOfContest,
            customSerialNumberMinimum: 1,
            customSerialNumberMinimumDigits: 2);

        Assert.Equal("5NN123", station.CreateExchangeText());
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
            new DeterministicRandom(12_345),
            OperatorRunMode.Hst,
            contestId: new("scHst"));

        Assert.Equal(expected, station.CreateExchangeText());
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
            new DeterministicRandom(12_345),
            OperatorRunMode.Wpx,
            contestId: new("scWpx"),
            serialNumberRange: SerialNumberRangeMode.MidContest);

        Assert.Equal("5NN57", station.CreateExchangeText());
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
            new DeterministicRandom(12_345),
            OperatorRunMode.Wpx,
            contestId: new("scWpx"),
            serialNumberRange: SerialNumberRangeMode.Custom,
            customSerialNumberMinimum: 1,
            customSerialNumberMinimumDigits: 2);

        Assert.Equal("5NNT7", station.CreateExchangeText());
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
