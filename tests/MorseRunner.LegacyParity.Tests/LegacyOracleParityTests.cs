using System.Text.Json;
using System.Text.Json.Serialization;

namespace MorseRunner.LegacyParity.Tests;

public sealed class LegacyOracleParityTests
{
    [Fact]
    public async Task CqWpxScoringVectorIsBothGreen()
    {
        const string parityId = "contest.cq-wpx-scoring";
        const string fixturePath =
            "tests/parity/fixtures/legacy/contest-cq-wpx-scoring.json";
        OracleFixture fixture = LoadFixture(fixturePath);
        ParityScenario scenario = new(
            parityId,
            "contest-rules",
            fixture.Values);
        LegacyOracleTarget legacyTarget = new(fixturePath);
        XPlatContestRulesTarget xplatTarget = new();

        ParityObservation legacy = await legacyTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);
        ParityObservation xplat = await xplatTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, legacy.Outcome);
        Assert.Equal(fixture.Values, xplat.Values);
        Assert.Equal(ParityTargetOutcome.Passed, xplat.Outcome);
        Assert.Equal(
            ParityAssessment.BothGreen,
            ParityAssessmentClassifier.Classify(legacy, xplat));
    }

    [Fact]
    public async Task CwtScoringVectorIsBothGreen()
    {
        const string parityId = "contest.cwt-scoring";
        const string fixturePath =
            "tests/parity/fixtures/legacy/contest-cwt-scoring.json";
        OracleFixture fixture = LoadFixture(fixturePath);
        ParityScenario scenario = new(
            parityId,
            "contest-rules",
            fixture.Values);
        LegacyOracleTarget legacyTarget = new(fixturePath);
        XPlatContestRulesTarget xplatTarget = new();

        ParityObservation legacy = await legacyTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);
        ParityObservation xplat = await xplatTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, legacy.Outcome);
        Assert.Equal(fixture.Values, xplat.Values);
        Assert.Equal(ParityTargetOutcome.Passed, xplat.Outcome);
        Assert.Equal(
            ParityAssessment.BothGreen,
            ParityAssessmentClassifier.Classify(legacy, xplat));
    }

    [Fact]
    public async Task LiveOperatorSessionVectorIsBothGreen()
    {
        const string parityId = "simulation.live-operator-session";
        const string fixturePath =
            "tests/parity/fixtures/legacy/simulation-live-operator-session.json";
        OracleFixture fixture = LoadFixture(fixturePath);
        ParityScenario scenario = new(
            parityId,
            "simulation",
            fixture.Values);
        LegacyOracleTarget legacyTarget = new(fixturePath);
        XPlatSimulationTarget xplatTarget = new();

        ParityObservation legacy = await legacyTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);
        ParityObservation xplat = await xplatTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, legacy.Outcome);
        Assert.Equal(fixture.Values, xplat.Values);
        Assert.Equal(ParityTargetOutcome.Passed, xplat.Outcome);
        Assert.Equal(
            ParityAssessment.BothGreen,
            ParityAssessmentClassifier.Classify(legacy, xplat));
    }

    [Fact]
    public async Task ContestRuleVectorsAreBothGreen()
    {
        const string parityId = "contest.legacy-implementations";
        ManifestItem item = LoadManifestItem(parityId, expectedStatus: "both-green");
        OracleFixture fixture = LoadFixture(item.Fixture);
        ParityScenario scenario = new(parityId, item.Category, fixture.Values);
        LegacyOracleTarget legacyTarget = new(item.Fixture);
        XPlatContestRulesTarget xplatTarget = new();

        ParityObservation legacy = await legacyTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);
        ParityObservation xplat = await xplatTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, legacy.Outcome);
        Assert.Equal(fixture.Values, xplat.Values);
        Assert.Equal(ParityTargetOutcome.Passed, xplat.Outcome);
        Assert.Equal(
            ParityAssessment.BothGreen,
            ParityAssessmentClassifier.Classify(legacy, xplat));
    }

    [Theory]
    [InlineData("simulation.state-models", typeof(XPlatSimulationTarget))]
    [InlineData("simulation.runtime-routines", typeof(XPlatSimulationTarget))]
    [InlineData("logging.scoring-rate-and-results", typeof(XPlatLoggingTarget))]
    public async Task EngineBehaviorVectorsAreBothGreen(
        string parityId,
        Type targetType)
    {
        ManifestItem item = LoadManifestItem(parityId, expectedStatus: "both-green");
        OracleFixture fixture = LoadFixture(item.Fixture);
        ParityScenario scenario = new(parityId, item.Category, fixture.Values);
        LegacyOracleTarget legacyTarget = new(item.Fixture);
        var xplatTarget = (IParityTarget)Activator.CreateInstance(targetType)!;

        ParityObservation legacy = await legacyTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);
        ParityObservation xplat = await xplatTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, legacy.Outcome);
        Assert.Equal(fixture.Values, xplat.Values);
        Assert.Equal(ParityTargetOutcome.Passed, xplat.Outcome);
        Assert.Equal(
            ParityAssessment.BothGreen,
            ParityAssessmentClassifier.Classify(legacy, xplat));
    }

    [Theory]
    [InlineData("data.legacy-parsers", typeof(XPlatDataParserTarget))]
    [InlineData("simulation.legacy-effects", typeof(XPlatLegacyEffectsTarget))]
    public async Task PhaseThreeVectorsAreBothGreen(
        string parityId,
        Type targetType)
    {
        ManifestItem item = LoadManifestItem(parityId, expectedStatus: "both-green");
        OracleFixture fixture = LoadFixture(item.Fixture);
        ParityScenario scenario = new(parityId, item.Category, fixture.Values);
        LegacyOracleTarget legacyTarget = new(item.Fixture);
        var xplatTarget = (IParityTarget)Activator.CreateInstance(targetType)!;

        ParityObservation legacy = await legacyTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);
        ParityObservation xplat = await xplatTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, legacy.Outcome);
        Assert.Equal(ParityTargetOutcome.Passed, xplat.Outcome);
        Assert.Equal(fixture.Values, xplat.Values);
        Assert.Equal(
            ParityAssessment.BothGreen,
            ParityAssessmentClassifier.Classify(legacy, xplat));
    }

    [Fact]
    public async Task DspVectorsAreBothGreen()
    {
        const string parityId = "audio-dsp.legacy-processing";
        ManifestItem item = LoadManifestItem(parityId, expectedStatus: "both-green");
        OracleFixture fixture = LoadFixture(item.Fixture);
        ParityScenario scenario = new(
            parityId,
            item.Category,
            fixture.Values);
        LegacyOracleTarget legacyTarget = new(item.Fixture);
        XPlatDspTarget xplatTarget = new();

        ParityObservation legacy = await legacyTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);
        ParityObservation xplat = await xplatTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, legacy.Outcome);
        Assert.Equal(fixture.Values, legacy.Values);
        Assert.Equal(ParityTargetOutcome.Passed, xplat.Outcome);
        Assert.Equal(fixture.Values, xplat.Values);
        Assert.Equal(
            ParityAssessment.BothGreen,
            ParityAssessmentClassifier.Classify(legacy, xplat));
        Assert.True(
            File.Exists(Path.Combine(RepositoryPaths.Root, item.Evidence)),
            $"Evidence not found: {item.Evidence}");
    }

    [Fact]
    public async Task AudioAdapterVectorsAreBothGreen()
    {
        const string parityId = "audio.legacy-adapters";
        ManifestItem item = LoadManifestItem(parityId, expectedStatus: "both-green");
        OracleFixture fixture = LoadFixture(item.Fixture);
        ParityScenario scenario = new(
            parityId,
            item.Category,
            fixture.Values);
        LegacyOracleTarget legacyTarget = new(item.Fixture);
        XPlatAudioAdapterTarget xplatTarget = new();

        ParityObservation legacy = await legacyTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);
        ParityObservation xplat = await xplatTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, legacy.Outcome);
        Assert.Equal(fixture.Values, legacy.Values);
        Assert.Equal(ParityTargetOutcome.Passed, xplat.Outcome);
        Assert.Equal(fixture.Values, xplat.Values);
        Assert.Equal(
            ParityAssessment.BothGreen,
            ParityAssessmentClassifier.Classify(legacy, xplat));
        Assert.True(
            File.Exists(Path.Combine(RepositoryPaths.Root, item.Evidence)),
            $"Evidence not found: {item.Evidence}");
    }

    private static ManifestItem LoadManifestItem(
        string parityId,
        string expectedStatus = "both-green")
    {
        string path = Path.Combine(
            RepositoryPaths.Root,
            "tests",
            "parity",
            "parity-manifest.json");
        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement element = document.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == parityId);

        Assert.Equal(
            expectedStatus,
            element.GetProperty("status").GetString());
        return JsonSerializer.Deserialize<ManifestItem>(element.GetRawText())!;
    }

    private static OracleFixture LoadFixture(string relativePath)
    {
        string path = Path.Combine(RepositoryPaths.Root, relativePath);
        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<OracleFixture>(stream)!;
    }

    private sealed record ManifestItem(
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("failureCode")] string FailureCode,
        [property: JsonPropertyName("fixture")] string Fixture,
        [property: JsonPropertyName("evidence")] string Evidence);

    private sealed record OracleFixture(
        [property: JsonPropertyName("revision")] string Revision,
        [property: JsonPropertyName("parityId")] string ParityId,
        [property: JsonPropertyName("values")] IReadOnlyList<string> Values);
}
