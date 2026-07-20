using System.Text.Json;
using System.Text.Json.Nodes;
using MorseRunner.Domain;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatRandomPrimitivesTargetTests
{
    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void ObserveIsDeterministicAndFramesEveryPrimitiveGroup()
    {
        RandomPrimitivesInput input = ValidInput();

        string[] first = XPlatRandomPrimitivesTarget.Observe(input);
        string[] second = XPlatRandomPrimitivesTarget.Observe(input);

        Assert.Equal(first, second, StringComparer.Ordinal);
        Assert.Equal(
            RandomPrimitivesInput.ExpectedValueCount,
            first.Length);
        Assert.StartsWith(
            "configuration|seed=12345|probe-count=8"
            + "|sequence-count=4096|",
            first[0],
            StringComparison.Ordinal);
        Assert.Equal(
            [
                "raw-random-single",
                "raw-random-int",
                "rnd-uniform",
                "rnd-ushaped",
                "rnd-normal",
                "rnd-gauss-lim",
                "rnd-rayleigh",
                "rnd-poisson",
            ],
            first
                .Where(
                    value => value.Contains(
                        "|next-raw-single-bits=",
                        StringComparison.Ordinal))
                .Select(value => value.Split('|')[0])
                .ToArray(),
            StringComparer.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task CurrentXPlatRowsMatchThePinnedCeFixture()
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                XPlatRandomPrimitivesTarget.ParityId);

        ParityObservation observation =
            await new XPlatRandomPrimitivesTarget().ExecuteAsync(
                definition.Scenario,
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, observation.Outcome);
        Assert.Null(observation.FailureCode);
        Assert.Equal(
            XPlatRandomPrimitivesTarget.EvidenceSource,
            observation.EvidenceSource);
        Assert.Equal(
            definition.Scenario.ExpectedValues,
            observation.Values,
            StringComparer.Ordinal);
        Assert.Equal(
            "51e95b1ef37b187db282b114527a12c479ccc20a9b0480d6976aef210b3bc5be",
            ParityObservedValuesDigest.Compute(observation.Values));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task TargetPassesOnlyWhenObservedRowsMatch()
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                XPlatRandomPrimitivesTarget.ParityId);
        RandomPrimitivesInput input =
            RandomPrimitivesInput.Parse(definition.Scenario);
        string[] current =
            XPlatRandomPrimitivesTarget.Observe(input);
        ParityScenario passingScenario = new(
            definition.Id,
            definition.Scenario.Capability,
            current,
            definition.Scenario.Input);
        string[] changed = [.. current];
        changed[^1] += "-changed";
        ParityScenario failingScenario = new(
            definition.Id,
            definition.Scenario.Capability,
            changed,
            definition.Scenario.Input);

        ParityObservation passed =
            await new XPlatRandomPrimitivesTarget().ExecuteAsync(
                passingScenario,
                TestContext.Current.CancellationToken);
        ParityObservation failed =
            await new XPlatRandomPrimitivesTarget().ExecuteAsync(
                failingScenario,
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, passed.Outcome);
        Assert.Null(passed.FailureCode);
        Assert.Equal(current, passed.Values, StringComparer.Ordinal);
        Assert.Equal(ParityTargetOutcome.Failed, failed.Outcome);
        Assert.Equal(
            XPlatRandomPrimitivesTarget.FunctionalDivergenceCode,
            failed.FailureCode);
        Assert.Equal(current, failed.Values, StringComparer.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task UnsupportedScenarioFailsWithoutObservations()
    {
        ParityObservation observation =
            await new XPlatRandomPrimitivesTarget().ExecuteAsync(
                new ParityScenario(
                    "audio.some-other-case",
                    "simulation",
                    []),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            DomainErrorCodes.UnsupportedCapability,
            observation.FailureCode);
        Assert.Empty(observation.Values);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void ParserRejectsAnyPinnedInputDrift()
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                XPlatRandomPrimitivesTarget.ParityId);
        JsonObject valid = JsonNode.Parse(
            definition.Scenario.Input.GetRawText())!.AsObject();
        (string Name, int Value)[] mutations =
        [
            ("seed", 12_344),
            ("probeCount", 7),
            ("sequenceCount", 4_095),
            ("gaussianMeanMilli", 5_099),
            ("gaussianLimitMilli", 1_299),
            ("rayleighMeanMilli", 2_699),
            ("poissonMeanMilli", 3_299),
        ];

        foreach ((string name, int value) in mutations)
        {
            JsonObject changed = JsonNode.Parse(
                valid.ToJsonString())!.AsObject();
            changed[name] = value;
            Assert.Throws<InvalidDataException>(
                () => RandomPrimitivesInput.Parse(
                    Scenario(
                        definition,
                        changed)));
        }

        JsonObject extra = JsonNode.Parse(
            valid.ToJsonString())!.AsObject();
        extra["unexpected"] = true;
        Assert.Throws<InvalidDataException>(
            () => RandomPrimitivesInput.Parse(
                Scenario(definition, extra)));

        JsonObject changedBounds = JsonNode.Parse(
            valid.ToJsonString())!.AsObject();
        changedBounds["integerBounds"] = new JsonArray(
            1,
            1,
            2,
            3,
            10,
            1_000,
            65_536,
            Int32.MaxValue);
        Assert.Throws<InvalidDataException>(
            () => RandomPrimitivesInput.Parse(
                Scenario(definition, changedBounds)));
    }

    private static RandomPrimitivesInput ValidInput()
    {
        return RandomPrimitivesInput.Parse(
            ParityCertificationCase.LoadForInspection(
                XPlatRandomPrimitivesTarget.ParityId)
                .Scenario);
    }

    private static ParityScenario Scenario(
        ParityCertificationCase definition,
        JsonObject input)
    {
        return new ParityScenario(
            definition.Id,
            definition.Scenario.Capability,
            definition.Scenario.ExpectedValues,
            input.Deserialize<System.Text.Json.JsonElement>());
    }
}
