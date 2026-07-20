using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MorseRunner.LegacyParity.Tests;

public interface IParityTarget
{
    Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken);
}

public sealed record ParityScenario
{
    public ParityScenario(
        string id,
        string capability,
        IReadOnlyList<string> expectedValues)
        : this(
            id,
            capability,
            expectedValues,
            ParityScenarioInput.CreateDiscriminatorOnly(id),
            caseDefinitionSha256: null,
            legacyOracle: null)
    {
    }

    public ParityScenario(
        string id,
        string capability,
        IReadOnlyList<string> expectedValues,
        JsonElement input)
        : this(
            id,
            capability,
            expectedValues,
            input,
            caseDefinitionSha256: null,
            legacyOracle: null)
    {
    }

    internal ParityScenario(
        string id,
        string capability,
        IReadOnlyList<string> expectedValues,
        JsonElement input,
        string? caseDefinitionSha256,
        LegacyOracleDescriptor? legacyOracle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        ArgumentNullException.ThrowIfNull(expectedValues);
        if (caseDefinitionSha256 is not null
            && !ParityCanonicalJson.IsLowercaseSha256(
                caseDefinitionSha256))
        {
            throw new InvalidDataException(
                $"Parity scenario '{id}' case-definition hash is invalid.");
        }

        Id = id;
        Capability = capability;
        ExpectedValues = expectedValues.ToImmutableArray();
        Input = ParityScenarioInput.CloneAndValidateDiscriminator(
            id,
            input);
        CanonicalInputJson = ParityCanonicalJson.Serialize(Input);
        InputSha256 = ParityCanonicalJson.ComputeSha256(Input);
        CaseDefinitionSha256 = caseDefinitionSha256;
        LegacyOracle = legacyOracle;
    }

    public string Id { get; }

    public string Capability { get; }

    public IReadOnlyList<string> ExpectedValues { get; }

    public JsonElement Input { get; }

    public string InputSha256 { get; }

    internal string CanonicalInputJson { get; }

    internal string? CaseDefinitionSha256 { get; }

    internal LegacyOracleDescriptor? LegacyOracle { get; }
}

internal sealed record LegacyOracleDescriptor(
    string AdapterId,
    string VersionId,
    string Source,
    string SourceSha256,
    string BuildRecipe,
    string BuildRecipeSha256);

public sealed record ParityObservation(
    ParityTargetOutcome Outcome,
    IReadOnlyList<string> Values,
    string? FailureCode,
    string EvidenceSource,
    LegacyOracleResultBinding? LegacyOracle = null);

public sealed record LegacyOracleResultBinding(
    [property: JsonPropertyName("adapterId")] string AdapterId,
    [property: JsonPropertyName("versionId")] string VersionId,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("sourceSha256")] string SourceSha256,
    [property: JsonPropertyName("buildRecipe")] string BuildRecipe,
    [property: JsonPropertyName("buildRecipeSha256")]
    string BuildRecipeSha256,
    [property: JsonPropertyName("registrySha256")]
    string RegistrySha256,
    [property: JsonPropertyName("executableSha256")]
    string ExecutableSha256,
    [property: JsonPropertyName("provenance")] string Provenance,
    [property: JsonPropertyName("provenanceSha256")]
    string ProvenanceSha256);

public enum ParityTargetOutcome
{
    Passed,
    Failed,
}

public enum ParityAssessment
{
    BothGreen,
    LegacyGreenXPlatRed,
    LegacyFailure,
}

public static class ParityAssessmentClassifier
{
    public static ParityAssessment Classify(
        ParityObservation legacy,
        ParityObservation xplat)
    {
        if (legacy.Outcome != ParityTargetOutcome.Passed)
        {
            return ParityAssessment.LegacyFailure;
        }

        return xplat.Outcome == ParityTargetOutcome.Passed
            ? ParityAssessment.BothGreen
            : ParityAssessment.LegacyGreenXPlatRed;
    }
}
