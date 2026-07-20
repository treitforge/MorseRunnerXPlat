using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MorseRunner.LegacyParity.Tests;

public sealed class LegacyOracleParityTests
{
    public static IEnumerable<TheoryDataRow>
        ActiveParityCases()
    {
        return ParityAcceptanceRegistry
            .SelectedIdsForCurrentRun()
            .Select(
            parityId => new TheoryDataRow()
                .WithTestDisplayName($"parity:{parityId}"));
    }

    [Fact]
    [Trait("Category", "LegacyV1Noncertifying")]
    public Task CqWpxScoringVectorMatchesSelectedTarget()
    {
        return AssertNoncertifyingFixtureVectorAsync(
            "contest.cq-wpx-scoring",
            "contest-rules",
            "tests/parity/fixtures/legacy/contest-cq-wpx-scoring.json",
            static () => new XPlatContestRulesTarget());
    }

    [Fact]
    [Trait("Category", "LegacyV1Noncertifying")]
    public Task CwtScoringVectorMatchesSelectedTarget()
    {
        return AssertNoncertifyingFixtureVectorAsync(
            "contest.cwt-scoring",
            "contest-rules",
            "tests/parity/fixtures/legacy/contest-cwt-scoring.json",
            static () => new XPlatContestRulesTarget());
    }

    [Theory]
    [MemberData(nameof(ActiveParityCases))]
    [Trait("Category", "ParityAcceptance")]
    [SuppressMessage(
        "xUnit",
        "xUnit1006",
        Justification =
            "Zero-argument rows preserve exact per-case certification IDs.")]
    public Task ManifestCaseMatchesSelectedTarget()
    {
        return AssertCertifyingManifestCaseAsync(
            GetCurrentParityId());
    }

    [Fact]
    [Trait("Category", "LegacyV1Noncertifying")]
    public Task RemainingContestScoringVectorMatchesSelectedTarget()
    {
        return AssertNoncertifyingFixtureVectorAsync(
            "contest.remaining-scoring",
            "contest-rules",
            "tests/parity/fixtures/legacy/contest-remaining-scoring.json",
            static () => new XPlatContestRulesTarget());
    }

    [Fact]
    [Trait("Category", "LegacyV1Noncertifying")]
    public Task LiveOperatorSessionVectorMatchesSelectedTarget()
    {
        return AssertNoncertifyingFixtureVectorAsync(
            "simulation.live-operator-session",
            "simulation",
            "tests/parity/fixtures/legacy/simulation-live-operator-session.json",
            static () => new XPlatSimulationTarget());
    }

    [Fact]
    [Trait("Category", "LegacyV1Noncertifying")]
    public Task LiveStationSessionVectorMatchesSelectedTarget()
    {
        return AssertNoncertifyingFixtureVectorAsync(
            "simulation.live-station-session",
            "simulation",
            "tests/parity/fixtures/legacy/simulation-live-station-session.json",
            static () => new XPlatSimulationTarget());
    }

    [Fact]
    [Trait("Category", "LegacyV1Noncertifying")]
    public void ContestRuleVectorRemainsRetainedForProvenance()
    {
        const string parityId = "contest.legacy-implementations";
        OracleFixture fixture = LoadFixture(
            GetLegacyV1FixturePath(parityId),
            parityId);

        // Schema-v1 records are immutable provenance, not current behavior
        // expectations. The live schema-v3 case owns contest shape parity.
        Assert.Equal(72, fixture.Values.Count);
        AssertEvidenceExists(GetLegacyV1EvidencePath(parityId));
    }

    [Theory]
    [Trait("Category", "LegacyV1Noncertifying")]
    [InlineData("simulation.state-models", typeof(XPlatSimulationTarget))]
    [InlineData("simulation.runtime-routines", typeof(XPlatSimulationTarget))]
    [InlineData("logging.scoring-rate-and-results", typeof(XPlatLoggingTarget))]
    public Task EngineBehaviorVectorsMatchSelectedTarget(
        string parityId,
        Type targetType)
    {
        return AssertLegacyV1InventoryVectorAsync(
            parityId,
            () => (IParityTarget)Activator.CreateInstance(targetType)!);
    }

    [Theory]
    [Trait("Category", "LegacyV1Noncertifying")]
    [InlineData("data.legacy-parsers", typeof(XPlatDataParserTarget))]
    [InlineData("simulation.legacy-effects", typeof(XPlatLegacyEffectsTarget))]
    public Task PhaseThreeVectorsMatchSelectedTarget(
        string parityId,
        Type targetType)
    {
        return AssertLegacyV1InventoryVectorAsync(
            parityId,
            () => (IParityTarget)Activator.CreateInstance(targetType)!);
    }

    [Fact]
    [Trait("Category", "LegacyV1Noncertifying")]
    public async Task DspVectorsMatchSelectedTarget()
    {
        const string parityId = "audio-dsp.legacy-processing";
        string category = LoadInventoryCategory(parityId);
        OracleFixture fixture = LoadFixture(
            GetLegacyV1FixturePath(parityId),
            parityId);
        ParityScenario scenario = new(
            parityId,
            category,
            fixture.Values);
        SelectedParityObservation selected =
            await ParityRegressionRunner.ExecuteSelectedAsync(
                scenario,
                static () => new LegacyOracleTarget(),
                static () => new XPlatDspTarget(),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ParityTargetOutcome.Passed,
            selected.Observation.Outcome);
        if (selected.Target == ParityTargetKind.Legacy)
        {
            Assert.Equal(fixture.Values, selected.Observation.Values);
        }
        else
        {
            Assert.True(
                XPlatDspTarget.ValuesEquivalent(
                    selected.Observation.Values,
                    fixture.Values),
                "XPlat DSP values exceeded the documented numeric tolerance.");
        }

        AssertEvidenceExists(GetLegacyV1EvidencePath(parityId));
    }

    [Fact]
    [Trait("Category", "LegacyV1Noncertifying")]
    public async Task AudioAdapterVectorsMatchSelectedTarget()
    {
        const string parityId = "audio.legacy-adapters";
        string category = LoadInventoryCategory(parityId);
        OracleFixture fixture = LoadFixture(
            GetLegacyV1FixturePath(parityId),
            parityId);
        ParityScenario scenario = new(
            parityId,
            category,
            fixture.Values);
        SelectedParityObservation selected =
            await ParityRegressionRunner.ExecuteSelectedAsync(
                scenario,
                static () => new LegacyOracleTarget(),
                static () => new XPlatAudioAdapterTarget(),
                TestContext.Current.CancellationToken);

        Assert.Equal(fixture.Values, selected.Observation.Values);
        Assert.Equal(
            ParityTargetOutcome.Passed,
            selected.Observation.Outcome);
        AssertEvidenceExists(GetLegacyV1EvidencePath(parityId));
    }

    private static Task AssertLegacyV1InventoryVectorAsync(
        string parityId,
        Func<IParityTarget> createXPlat)
    {
        return AssertNoncertifyingFixtureVectorAsync(
            parityId,
            LoadInventoryCategory(parityId),
            GetLegacyV1FixturePath(parityId),
            createXPlat);
    }

    private static async Task AssertNoncertifyingFixtureVectorAsync(
        string parityId,
        string category,
        string fixturePath,
        Func<IParityTarget> createXPlat)
    {
        OracleFixture fixture = LoadFixture(fixturePath, parityId);
        ParityScenario scenario = new(
            parityId,
            category,
            fixture.Values);
        SelectedParityObservation selected =
            await ParityRegressionRunner.ExecuteSelectedAsync(
                scenario,
                static () => new LegacyOracleTarget(),
                createXPlat,
                TestContext.Current.CancellationToken);

        Assert.Equal(fixture.Values, selected.Observation.Values);
        Assert.Equal(
            ParityTargetOutcome.Passed,
            selected.Observation.Outcome);
    }

    private static async Task AssertCertifyingManifestCaseAsync(
        string parityId)
    {
        Assert.Equal(
            $"parity:{parityId}()",
            TestContext.Current.Test?.TestDisplayName);
        ParityCertificationCase definition =
            ParityCertificationCase.Load(parityId);
        SelectedParityObservation selected =
            await ParityAcceptanceRunner.ExecuteSelectedAsync(
                definition,
                TestContext.Current.CancellationToken);

        AssertCertifyingObservation(definition, selected);
    }

    internal static void AssertCertifyingObservation(
        ParityCertificationCase definition,
        SelectedParityObservation selected)
    {
        ParityAcceptanceRegistration registration =
            ParityAcceptanceRegistry.Get(definition.Id);
        bool valuesMatch =
            definition.Scenario.ExpectedValues.SequenceEqual(
                selected.Observation.Values,
                StringComparer.Ordinal);
        if (selected.Observation.Outcome
                == ParityTargetOutcome.Failed
            && !valuesMatch
            && registration.IsFunctionalDivergence(
                selected.Target,
                selected.Observation.FailureCode))
        {
            throw new ParityFunctionalDivergenceException(
                definition.Id,
                selected.Observation.FailureCode!);
        }

        Assert.Equal(
            definition.Scenario.ExpectedValues,
            selected.Observation.Values);
        Assert.Equal(
            ParityTargetOutcome.Passed,
            selected.Observation.Outcome);
        Assert.Null(selected.Observation.FailureCode);
    }

    internal static string GetCurrentAcceptanceTestName()
    {
        return $"parity:{GetCurrentParityId()}()";
    }

    private static string GetCurrentParityId()
    {
        string displayName =
            TestContext.Current.Test?.TestDisplayName
            ?? throw new InvalidOperationException(
                "Parity acceptance test identity is unavailable.");
        return ParseCurrentParityId(displayName);
    }

    internal static string ParseCurrentParityId(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        const string Prefix = "parity:";
        // xUnit v3 appends this suffix to zero-argument theory row names.
        const string EmptyArgumentSuffix = "()";
        if (!displayName.StartsWith(
                Prefix,
                StringComparison.Ordinal)
            || !displayName.EndsWith(
                EmptyArgumentSuffix,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Parity acceptance test identity '{displayName}' "
                + "does not use the zero-argument row format.");
        }

        string parityId = displayName[
            Prefix.Length..^EmptyArgumentSuffix.Length];
        if (!ParityAcceptanceRegistry
                .SelectedIdsForCurrentRun()
                .Contains(
                parityId,
                StringComparer.Ordinal)
            || !StringComparer.Ordinal.Equals(
                displayName,
                $"parity:{parityId}()"))
        {
            throw new InvalidOperationException(
                $"Parity acceptance test identity '{displayName}' "
                + "does not map to exactly one active case.");
        }

        return parityId;
    }

    private static string LoadInventoryCategory(string parityId)
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

        return element.GetProperty("category").GetString()!;
    }

    private static string GetLegacyV1FixturePath(string parityId)
    {
        return $"tests/parity/fixtures/legacy/"
            + $"{parityId.Replace('.', '-')}.json";
    }

    private static string GetLegacyV1EvidencePath(string parityId)
    {
        return $"tests/parity/evidence/"
            + $"{parityId.Replace('.', '-')}.baseline.json";
    }

    private static OracleFixture LoadFixture(
        string relativePath,
        string expectedParityId)
    {
        string path = Path.Combine(RepositoryPaths.Root, relativePath);
        using FileStream stream = File.OpenRead(path);
        OracleFixture fixture =
            JsonSerializer.Deserialize<OracleFixture>(stream)!;
        Assert.Equal(
            LegacyOracleProvenance.PinnedLegacyRevision,
            fixture.Revision);
        Assert.Equal(expectedParityId, fixture.ParityId);
        return fixture;
    }

    private static void AssertEvidenceExists(string relativePath)
    {
        Assert.True(
            File.Exists(Path.Combine(RepositoryPaths.Root, relativePath)),
            $"Evidence not found: {relativePath}");
    }

    private sealed record OracleFixture(
        [property: JsonPropertyName("revision")] string Revision,
        [property: JsonPropertyName("parityId")] string ParityId,
        [property: JsonPropertyName("values")] IReadOnlyList<string> Values);
}

public sealed class ParityFunctionalDivergenceException : Exception
{
    public ParityFunctionalDivergenceException(
        string parityId,
        string failureCode)
        : base(
            $"PARITY_FUNCTIONAL_DIVERGENCE|{parityId}|{failureCode}")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        if (parityId.Contains('|', StringComparison.Ordinal)
            || failureCode.Contains('|', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Parity functional divergence identity contains a delimiter.");
        }

        ParityId = parityId;
        FailureCode = failureCode;
    }

    public string ParityId { get; }

    public string FailureCode { get; }
}
