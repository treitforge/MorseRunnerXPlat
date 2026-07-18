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
