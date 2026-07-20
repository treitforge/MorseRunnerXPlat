using System.Text.Json;
using System.Text.Json.Serialization;

namespace MorseRunner.LegacyParity.Tests;

public sealed record LegacyOracleProvenance(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("adapterId")] string? AdapterId,
    [property: JsonPropertyName("versionId")] string? VersionId,
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("sourceSha256")] string? SourceSha256,
    [property: JsonPropertyName("buildRecipe")] string? BuildRecipe,
    [property: JsonPropertyName("buildRecipeSha256")]
    string? BuildRecipeSha256,
    [property: JsonPropertyName("selectedCaseIds")]
    IReadOnlyList<string>? SelectedCaseIds,
    [property: JsonPropertyName("reference")]
    OracleReferenceProvenance? Reference,
    [property: JsonPropertyName("legacy")] LegacySourceProvenance? Legacy,
    [property: JsonPropertyName("xplat")] XPlatSourceProvenance? XPlat,
    [property: JsonPropertyName("oracle")] OracleBinaryProvenance? Oracle,
    [property: JsonPropertyName("toolchain")] OracleToolchainProvenance? Toolchain,
    [property: JsonPropertyName("build")] OracleBuildProvenance? Build,
    [property: JsonPropertyName("manifest")]
    OracleManifestProvenance? Manifest,
    [property: JsonPropertyName("observations")]
    IReadOnlyList<OracleScenarioProvenance>? Observations)
{
    public const int CurrentSchemaVersion = 1;
    public const string PinnedLegacyRevision =
        "55bbd019c29d8cf693184ea420a17a253f16fe1e";
    public const string PinnedLegacyTree =
        "a44212bfee5b1eebfd0129459d476736775adf36";
}

public sealed record OracleReferenceProvenance(
    [property: JsonPropertyName("definition")] string? Definition,
    [property: JsonPropertyName("definitionSha256")] string? DefinitionSha256,
    [property: JsonPropertyName("bundle")] string? Bundle,
    [property: JsonPropertyName("bundleSha256")] string? BundleSha256);

public sealed record LegacySourceProvenance(
    [property: JsonPropertyName("repository")] string? Repository,
    [property: JsonPropertyName("revision")] string? Revision,
    [property: JsonPropertyName("tree")] string? Tree,
    [property: JsonPropertyName("root")] string? Root,
    [property: JsonPropertyName("clean")] bool Clean);

public sealed record XPlatSourceProvenance(
    [property: JsonPropertyName("revision")] string? Revision,
    [property: JsonPropertyName("tree")] string? Tree,
    [property: JsonPropertyName("clean")] bool Clean);

public sealed record OracleBinaryProvenance(
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("sourcePath")] string? SourcePath,
    [property: JsonPropertyName("sourceSha256")] string? SourceSha256,
    [property: JsonPropertyName("buildRecipe")] string? BuildRecipe,
    [property: JsonPropertyName("buildRecipePath")]
    string? BuildRecipePath,
    [property: JsonPropertyName("buildRecipeSha256")]
    string? BuildRecipeSha256,
    [property: JsonPropertyName("executable")] string? Executable,
    [property: JsonPropertyName("executableSha256")] string? ExecutableSha256,
    [property: JsonPropertyName("length")] long Length);

public sealed record OracleToolchainProvenance(
    [property: JsonPropertyName("root")] string? Root,
    [property: JsonPropertyName("lazarusVersion")] string? LazarusVersion,
    [property: JsonPropertyName("fpcVersion")] string? FpcVersion,
    [property: JsonPropertyName("targetCpu")] string? TargetCpu,
    [property: JsonPropertyName("targetOs")] string? TargetOs,
    [property: JsonPropertyName("compiler")] string? Compiler,
    [property: JsonPropertyName("compilerSha256")] string? CompilerSha256,
    [property: JsonPropertyName("backendCompiler")] string? BackendCompiler,
    [property: JsonPropertyName("backendCompilerSha256")]
    string? BackendCompilerSha256,
    [property: JsonPropertyName("lazbuild")] string? Lazbuild,
    [property: JsonPropertyName("lazbuildSha256")] string? LazbuildSha256,
    [property: JsonPropertyName("fingerprint")]
    OracleToolchainFingerprintProvenance? Fingerprint);

public sealed record OracleToolchainFingerprintProvenance(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("canonicalization")]
    string? Canonicalization,
    [property: JsonPropertyName("roots")]
    IReadOnlyList<string>? Roots,
    [property: JsonPropertyName("aggregateSha256")]
    string? AggregateSha256,
    [property: JsonPropertyName("fileCount")] long FileCount,
    [property: JsonPropertyName("byteCount")] long ByteCount);

public sealed record OracleBuildProvenance(
    [property: JsonPropertyName("script")] string? Script,
    [property: JsonPropertyName("scriptSha256")] string? ScriptSha256,
    [property: JsonPropertyName("arguments")]
    IReadOnlyList<string?>? Arguments,
    [property: JsonPropertyName("invocation")]
    OracleBuildInvocationProvenance? Invocation,
    [property: JsonPropertyName("builtAtUtc")] string? BuiltAtUtc);

public sealed record OracleBuildInvocationProvenance(
    [property: JsonPropertyName("compiler")] string? Compiler,
    [property: JsonPropertyName("options")]
    IReadOnlyList<string?>? Options,
    [property: JsonPropertyName("unitSearchPaths")]
    IReadOnlyList<string?>? UnitSearchPaths,
    [property: JsonPropertyName("toolSearchPaths")]
    IReadOnlyList<string?>? ToolSearchPaths,
    [property: JsonPropertyName("librarySearchPaths")]
    IReadOnlyList<string?>? LibrarySearchPaths,
    [property: JsonPropertyName("unitOutputPath")]
    string? UnitOutputPath,
    [property: JsonPropertyName("executableOutputPath")]
    string? ExecutableOutputPath,
    [property: JsonPropertyName("outputExecutable")]
    string? OutputExecutable,
    [property: JsonPropertyName("source")] string? Source);

public sealed record OracleScenarioProvenance(
    [property: JsonPropertyName("scenario")] string? Scenario,
    [property: JsonPropertyName("valueCount")] int ValueCount,
    [property: JsonPropertyName("outputSha256")] string? OutputSha256);

public sealed record OracleManifestProvenance(
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("sha256")] string? Sha256);
