using System.Diagnostics.CodeAnalysis;

namespace MorseRunner.LegacyParity.Tests;

public sealed class ParityFunctionalDivergenceExceptionTests
{
    public static IEnumerable<TheoryDataRow>
        ZeroArgumentDisplayNameRows()
    {
        yield return new TheoryDataRow()
            .WithTestDisplayName("parity-display-probe");
    }

    [Theory]
    [MemberData(nameof(ZeroArgumentDisplayNameRows))]
    [Trait("Category", "ParityInfrastructure")]
    [SuppressMessage(
        "xUnit",
        "xUnit1006",
        Justification =
            "This probes exact zero-argument theory display-name behavior.")]
    public void ZeroArgumentTheoryUsesExactRowDisplayName()
    {
        Assert.Equal(
            "parity-display-probe()",
            TestContext.Current.Test?.TestDisplayName);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void ExactZeroArgumentDisplayNameMapsToActiveCase()
    {
        Assert.Equal(
            "contest.exchange-shapes",
            LegacyOracleParityTests.ParseCurrentParityId(
                "parity:contest.exchange-shapes()"));
    }

    [Theory]
    [Trait("Category", "ParityInfrastructure")]
    [InlineData("parity:contest.exchange-shapes")]
    [InlineData("parity:contest.exchange-shapes()()")]
    [InlineData("parity:not-an-active-case()")]
    public void AlteredDisplayNameCannotMapToAcceptanceCase(
        string displayName)
    {
        Assert.Throws<InvalidOperationException>(
            () => LegacyOracleParityTests.ParseCurrentParityId(
                displayName));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void ExactRegisteredDivergenceUsesDedicatedMachineReadableFailure()
    {
        ParityCertificationCase definition = CreateDefinition();
        SelectedParityObservation selected = new(
            ParityTargetKind.XPlat,
            new ParityObservation(
                ParityTargetOutcome.Failed,
                ["actual"],
                "contest-exchange-shape-mismatch",
                "test:expected-divergence"));

        ParityFunctionalDivergenceException exception =
            Assert.Throws<ParityFunctionalDivergenceException>(
                () => LegacyOracleParityTests
                    .AssertCertifyingObservation(
                        definition,
                        selected));

        Assert.Equal(
            "PARITY_FUNCTIONAL_DIVERGENCE|contest.exchange-shapes"
            + "|contest-exchange-shape-mismatch",
            exception.Message);
        Assert.Equal("contest.exchange-shapes", exception.ParityId);
        Assert.Equal(
            "contest-exchange-shape-mismatch",
            exception.FailureCode);
    }

    [Theory]
    [Trait("Category", "ParityInfrastructure")]
    [InlineData(
        ParityTargetKind.XPlat,
        "parity-adapter-exception")]
    [InlineData(
        ParityTargetKind.Legacy,
        "contest-exchange-shape-mismatch")]
    public void OtherFailuresCannotMasqueradeAsFunctionalDivergence(
        ParityTargetKind target,
        string failureCode)
    {
        ParityCertificationCase definition = CreateDefinition();
        SelectedParityObservation selected = new(
            target,
            new ParityObservation(
                ParityTargetOutcome.Failed,
                ["actual"],
                failureCode,
                "test:unexpected-failure"));

        Exception exception = Assert.ThrowsAny<Exception>(
            () => LegacyOracleParityTests
                .AssertCertifyingObservation(
                    definition,
                    selected));

        Assert.IsNotType<ParityFunctionalDivergenceException>(
            exception);
        Assert.DoesNotContain(
            "PARITY_FUNCTIONAL_DIVERGENCE|",
            exception.Message,
            StringComparison.Ordinal);
    }

    private static ParityCertificationCase CreateDefinition()
    {
        return new ParityCertificationCase(
            new ParityScenario(
                "contest.exchange-shapes",
                "test",
                ["expected"]),
            new string('a', 64),
            new string('b', 64),
            "test-fixture.json",
            ["test-obligation"],
            ["LegacyOracleTarget", "XPlatContestRulesTarget"],
            ["windows", "linux", "macos"],
            "contest-exchange-shape-mismatch");
    }
}
