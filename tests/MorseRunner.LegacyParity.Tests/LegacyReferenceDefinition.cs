using System.Text.Json;
using System.Text.Json.Serialization;

namespace MorseRunner.LegacyParity.Tests;

internal sealed record LegacyReferenceDefinition(
    int SchemaVersion,
    string DefinitionPath,
    string Repository,
    string Revision,
    string Tree,
    string BundlePath,
    string BundleSha256,
    LegacyReferenceToolchain Toolchain)
{
    public static async Task<LegacyReferenceDefinition?> LoadAsync(
        CancellationToken cancellationToken)
    {
        string definitionPath = Path.Combine(
            RepositoryPaths.Root,
            "tests",
            "parity",
            "legacy-reference.json");
        if (!File.Exists(definitionPath))
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(definitionPath);
        LegacyReferenceDocument? document =
            await JsonSerializer.DeserializeAsync<LegacyReferenceDocument>(
                stream,
                cancellationToken: cancellationToken);
        if (document?.Toolchain?.Fingerprint is null)
        {
            return null;
        }

        return new LegacyReferenceDefinition(
            document.SchemaVersion,
            definitionPath,
            document.Repository ?? String.Empty,
            document.Revision ?? String.Empty,
            document.Tree ?? String.Empty,
            ResolveRepositoryPath(document.Bundle),
            document.BundleSha256 ?? String.Empty,
            new LegacyReferenceToolchain(
                document.Toolchain.LazarusVersion ?? String.Empty,
                document.Toolchain.FpcVersion ?? String.Empty,
                document.Toolchain.TargetCpu ?? String.Empty,
                document.Toolchain.TargetOs ?? String.Empty,
                document.Toolchain.CompilerSha256 ?? String.Empty,
                document.Toolchain.BackendCompilerSha256 ?? String.Empty,
                document.Toolchain.LazbuildSha256 ?? String.Empty,
                new LegacyReferenceToolchainFingerprint(
                    document.Toolchain.Fingerprint.SchemaVersion,
                    document.Toolchain.Fingerprint.Canonicalization
                        ?? String.Empty,
                    document.Toolchain.Fingerprint.Roots
                        ?? [],
                    document.Toolchain.Fingerprint.AggregateSha256
                        ?? String.Empty,
                    document.Toolchain.Fingerprint.FileCount,
                    document.Toolchain.Fingerprint.ByteCount)));
    }

    private static string ResolveRepositoryPath(string? path)
    {
        return String.IsNullOrWhiteSpace(path)
            ? String.Empty
            : Path.GetFullPath(Path.Combine(RepositoryPaths.Root, path));
    }

    private sealed record LegacyReferenceDocument(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("repository")] string? Repository,
        [property: JsonPropertyName("revision")] string? Revision,
        [property: JsonPropertyName("tree")] string? Tree,
        [property: JsonPropertyName("bundle")] string? Bundle,
        [property: JsonPropertyName("bundleSha256")] string? BundleSha256,
        [property: JsonPropertyName("toolchain")]
        LegacyReferenceToolchainDocument? Toolchain);

    private sealed record LegacyReferenceToolchainDocument(
        [property: JsonPropertyName("lazarusVersion")]
        string? LazarusVersion,
        [property: JsonPropertyName("fpcVersion")] string? FpcVersion,
        [property: JsonPropertyName("targetCpu")] string? TargetCpu,
        [property: JsonPropertyName("targetOs")] string? TargetOs,
        [property: JsonPropertyName("compilerSha256")]
        string? CompilerSha256,
        [property: JsonPropertyName("backendCompilerSha256")]
        string? BackendCompilerSha256,
        [property: JsonPropertyName("lazbuildSha256")]
        string? LazbuildSha256,
        [property: JsonPropertyName("fingerprint")]
        LegacyReferenceToolchainFingerprintDocument? Fingerprint);

    private sealed record LegacyReferenceToolchainFingerprintDocument(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("canonicalization")]
        string? Canonicalization,
        [property: JsonPropertyName("roots")]
        IReadOnlyList<string>? Roots,
        [property: JsonPropertyName("aggregateSha256")]
        string? AggregateSha256,
        [property: JsonPropertyName("fileCount")] long FileCount,
        [property: JsonPropertyName("byteCount")] long ByteCount);
}

internal sealed record LegacyReferenceToolchain(
    string LazarusVersion,
    string FpcVersion,
    string TargetCpu,
    string TargetOs,
    string CompilerSha256,
    string BackendCompilerSha256,
    string LazbuildSha256,
    LegacyReferenceToolchainFingerprint Fingerprint);

internal sealed record LegacyReferenceToolchainFingerprint(
    int SchemaVersion,
    string Canonicalization,
    IReadOnlyList<string> Roots,
    string AggregateSha256,
    long FileCount,
    long ByteCount);
