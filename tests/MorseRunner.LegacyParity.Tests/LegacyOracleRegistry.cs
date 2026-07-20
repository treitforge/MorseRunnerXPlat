using System.Text.Json.Serialization;

namespace MorseRunner.LegacyParity.Tests;

internal sealed record LegacyOracleRegistryDocument(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("entries")]
    IReadOnlyList<LegacyOracleRegistryEntry>? Entries);

internal sealed record LegacyOracleRegistryEntry(
    [property: JsonPropertyName("adapterId")] string? AdapterId,
    [property: JsonPropertyName("versionId")] string? VersionId,
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("sourceSha256")] string? SourceSha256,
    [property: JsonPropertyName("buildRecipe")] string? BuildRecipe,
    [property: JsonPropertyName("buildRecipeSha256")]
    string? BuildRecipeSha256,
    [property: JsonPropertyName("executable")] string? Executable,
    [property: JsonPropertyName("executableSha256")]
    string? ExecutableSha256,
    [property: JsonPropertyName("provenance")] string? Provenance,
    [property: JsonPropertyName("provenanceSha256")]
    string? ProvenanceSha256);

internal sealed record LegacyOracleBuildRecipe(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("adapterId")] string? AdapterId,
    [property: JsonPropertyName("versionId")] string? VersionId,
    [property: JsonPropertyName("sourceClosure")]
    LegacyOracleBuildRecipeSourceClosure? SourceClosure,
    [property: JsonPropertyName("invocation")]
    LegacyOracleBuildRecipeInvocation? Invocation);

internal sealed record LegacyOracleBuildRecipeSourceClosure(
    [property: JsonPropertyName("oracleSource")]
    string? OracleSource,
    [property: JsonPropertyName("oracleSourceSha256")]
    string? OracleSourceSha256,
    [property: JsonPropertyName("legacyRevision")]
    string? LegacyRevision,
    [property: JsonPropertyName("legacyTree")] string? LegacyTree,
    [property: JsonPropertyName("legacyBundleSha256")]
    string? LegacyBundleSha256,
    [property: JsonPropertyName("toolchainFingerprintSha256")]
    string? ToolchainFingerprintSha256);

internal sealed record LegacyOracleBuildRecipeInvocation(
    [property: JsonPropertyName("compiler")] string? Compiler,
    [property: JsonPropertyName("arguments")]
    IReadOnlyList<string?>? Arguments);
