namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatQsbRuntimeToggleTargetTests
{
    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task ProductionStationRuntimeToggleMatchesPinnedCeFixture()
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                XPlatQsbRuntimeToggleTarget.ParityId);

        ParityObservation observation =
            await new XPlatQsbRuntimeToggleTarget().ExecuteAsync(
                definition.Scenario,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            definition.Scenario.ExpectedValues,
            observation.Values);
        Assert.Equal(ParityTargetOutcome.Passed, observation.Outcome);
        Assert.Null(observation.FailureCode);
        Assert.Equal(
            "15c67eb36e309a1d88237761b7d6bda1"
                + "953a08d4e8a5b9085d314aaea56c864b",
            ParityObservedValuesDigest.Compute(observation.Values));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void PinnedCeFixtureCapturesTheRuntimeTransition()
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                XPlatQsbRuntimeToggleTarget.ParityId);

        Assert.Equal(
            QsbRuntimeToggleInput.ExpectedValueCount,
            definition.Scenario.ExpectedValues.Count);
        Assert.EndsWith(
            "|exact-equal=true",
            definition.Scenario.ExpectedValues[9],
            StringComparison.Ordinal);
        Assert.EndsWith(
            "|exact-equal=true",
            definition.Scenario.ExpectedValues[10],
            StringComparison.Ordinal);
        Assert.EndsWith(
            "|exact-equal=false",
            definition.Scenario.ExpectedValues[11],
            StringComparison.Ordinal);
        Assert.EndsWith(
            "|exact-equal=false",
            definition.Scenario.ExpectedValues[12],
            StringComparison.Ordinal);
        Assert.Equal(
            "15c67eb36e309a1d88237761b7d6bda1"
                + "953a08d4e8a5b9085d314aaea56c864b",
            ParityObservedValuesDigest.Compute(
                definition.Scenario.ExpectedValues));
    }

}
