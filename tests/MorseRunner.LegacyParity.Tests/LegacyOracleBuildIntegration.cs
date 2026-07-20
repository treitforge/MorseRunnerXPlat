using System.Text.Json;
using System.Text.Json.Serialization;

namespace MorseRunner.LegacyParity.Tests;

internal static class LegacyOracleBuildIntegration
{
    private static readonly JsonSerializerOptions StrictJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    internal static void ValidateArtifacts(
        IReadOnlyList<ParityCertificationCase> selectedCases,
        string registryPath)
    {
        ArgumentNullException.ThrowIfNull(selectedCases);
        ArgumentException.ThrowIfNullOrWhiteSpace(registryPath);
        LegacyOracleRegistryDocument registry =
            DeserializeStrict<LegacyOracleRegistryDocument>(
                File.ReadAllBytes(registryPath),
                "legacy oracle registry");
        LegacyOracleRegistryEntry[] entries =
            registry.SchemaVersion == 1
            && registry.Entries is { Count: > 0 }
                ? [.. registry.Entries]
                : throw new InvalidDataException(
                    "Legacy oracle registry shape is invalid.");
        var provenances =
            new List<LegacyOracleBuildProvenanceIdentity>(
                entries.Length);
        foreach (LegacyOracleRegistryEntry entry in entries)
        {
            string provenancePath = ResolveRepositoryArtifact(
                entry.Provenance,
                "legacy oracle provenance");
            LegacyOracleProvenance provenance =
                DeserializeStrict<LegacyOracleProvenance>(
                    File.ReadAllBytes(provenancePath),
                    "legacy oracle provenance");
            provenances.Add(
                LegacyOracleBuildProvenanceIdentity.From(
                    provenance));
        }

        Validate(
            selectedCases.Select(
                    definition =>
                        new LegacyOracleBuildSelection(
                            definition.Id,
                            definition.Scenario.LegacyOracle
                            ?? throw new InvalidDataException(
                                $"Selected parity case "
                                + $"'{definition.Id}' has no legacy "
                                + "oracle descriptor.")))
                .ToArray(),
            entries,
            provenances);
    }

    internal static void Validate(
        IReadOnlyList<LegacyOracleBuildSelection> selectedCases,
        IReadOnlyList<LegacyOracleRegistryEntry> registryEntries,
        IReadOnlyList<LegacyOracleBuildProvenanceIdentity> provenances)
    {
        ArgumentNullException.ThrowIfNull(selectedCases);
        ArgumentNullException.ThrowIfNull(registryEntries);
        ArgumentNullException.ThrowIfNull(provenances);
        if (selectedCases.Count == 0
            || selectedCases.Any(
                selected => String.IsNullOrWhiteSpace(
                    selected.CaseId))
            || selectedCases
                .Select(selected => selected.CaseId)
                .Distinct(StringComparer.Ordinal)
                .Count() != selectedCases.Count)
        {
            throw new InvalidDataException(
                "Selected parity case IDs must be nonempty and unique.");
        }

        Dictionary<string, LegacyOracleVersionSelection>
            selectedByVersion = GroupSelectedCases(selectedCases);
        ValidateRegistryVersionSet(
            selectedByVersion,
            registryEntries);
        ValidateProvenanceSet(
            selectedByVersion,
            provenances);
    }

    private static Dictionary<string, LegacyOracleVersionSelection>
        GroupSelectedCases(
            IReadOnlyList<LegacyOracleBuildSelection> selectedCases)
    {
        var result =
            new Dictionary<string, LegacyOracleVersionSelection>(
                StringComparer.Ordinal);
        foreach (IGrouping<
                     string,
                     LegacyOracleBuildSelection> group
                 in selectedCases.GroupBy(
                     selected => selected.Descriptor.VersionId,
                     StringComparer.Ordinal))
        {
            LegacyOracleBuildSelection[] cases = group
                .OrderBy(
                    selected => selected.CaseId,
                    StringComparer.Ordinal)
                .ToArray();
            LegacyOracleDescriptor descriptor = cases[0].Descriptor;
            if (cases.Any(
                    selected =>
                        selected.Descriptor != descriptor))
            {
                throw new InvalidDataException(
                    $"Selected parity cases sharing legacy oracle "
                    + $"version '{group.Key}' have conflicting complete "
                    + "descriptors.");
            }

            result.Add(
                group.Key,
                new(
                    descriptor,
                    cases.Select(selected => selected.CaseId)
                        .ToArray()));
        }

        return result;
    }

    private static void ValidateRegistryVersionSet(
        IReadOnlyDictionary<
            string,
            LegacyOracleVersionSelection> selectedByVersion,
        IReadOnlyList<LegacyOracleRegistryEntry> registryEntries)
    {
        if (registryEntries.Count == 0
            || registryEntries.Any(
                entry => String.IsNullOrWhiteSpace(
                    entry.VersionId))
            || registryEntries
                .Select(entry => entry.VersionId)
                .Distinct(StringComparer.Ordinal)
                .Count() != registryEntries.Count)
        {
            throw new InvalidDataException(
                "Legacy oracle registry versions must be nonempty "
                + "and unique.");
        }

        string[] expectedVersions = selectedByVersion.Keys
            .OrderBy(version => version, StringComparer.Ordinal)
            .ToArray();
        string[] actualVersions = registryEntries
            .Select(entry => entry.VersionId!)
            .OrderBy(version => version, StringComparer.Ordinal)
            .ToArray();
        if (!actualVersions.SequenceEqual(
                expectedVersions,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "Legacy oracle registry version set does not exactly "
                + "match the selected descriptor versions.");
        }

        foreach (LegacyOracleRegistryEntry entry in registryEntries)
        {
            LegacyOracleDescriptor descriptor =
                selectedByVersion[entry.VersionId!].Descriptor;
            if (!RegistryMatchesDescriptor(entry, descriptor))
            {
                throw new InvalidDataException(
                    $"Legacy oracle registry version "
                    + $"'{entry.VersionId}' does not exactly match its "
                    + "selected complete descriptor.");
            }
        }
    }

    private static void ValidateProvenanceSet(
        IReadOnlyDictionary<
            string,
            LegacyOracleVersionSelection> selectedByVersion,
        IReadOnlyList<LegacyOracleBuildProvenanceIdentity> provenances)
    {
        if (provenances.Count == 0
            || provenances.Any(
                provenance => String.IsNullOrWhiteSpace(
                    provenance.VersionId))
            || provenances
                .Select(provenance => provenance.VersionId)
                .Distinct(StringComparer.Ordinal)
                .Count() != provenances.Count)
        {
            throw new InvalidDataException(
                "Legacy oracle provenance versions must be nonempty "
                + "and unique.");
        }

        string[] expectedVersions = selectedByVersion.Keys
            .OrderBy(version => version, StringComparer.Ordinal)
            .ToArray();
        string[] actualVersions = provenances
            .Select(provenance => provenance.VersionId!)
            .OrderBy(version => version, StringComparer.Ordinal)
            .ToArray();
        if (!actualVersions.SequenceEqual(
                expectedVersions,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "Legacy oracle provenance version set does not exactly "
                + "match the selected descriptor versions.");
        }

        foreach (LegacyOracleBuildProvenanceIdentity provenance
                 in provenances)
        {
            LegacyOracleVersionSelection selection =
                selectedByVersion[provenance.VersionId!];
            if (!ProvenanceMatchesDescriptor(
                    provenance,
                    selection.Descriptor))
            {
                throw new InvalidDataException(
                    $"Legacy oracle provenance version "
                    + $"'{provenance.VersionId}' does not exactly match "
                    + "its selected complete descriptor.");
            }

            if (provenance.SelectedCaseIds is null
                || !provenance.SelectedCaseIds.SequenceEqual(
                    selection.SortedCaseIds,
                    StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    $"Legacy oracle provenance version "
                    + $"'{provenance.VersionId}' selectedCaseIds do not "
                    + "exactly match the sorted selected cases for that "
                    + "version.");
            }
        }
    }

    private static bool RegistryMatchesDescriptor(
        LegacyOracleRegistryEntry entry,
        LegacyOracleDescriptor descriptor)
    {
        return StringComparer.Ordinal.Equals(
                entry.AdapterId,
                descriptor.AdapterId)
            && StringComparer.Ordinal.Equals(
                entry.VersionId,
                descriptor.VersionId)
            && StringComparer.Ordinal.Equals(
                entry.Source,
                descriptor.Source)
            && StringComparer.Ordinal.Equals(
                entry.SourceSha256,
                descriptor.SourceSha256)
            && StringComparer.Ordinal.Equals(
                entry.BuildRecipe,
                descriptor.BuildRecipe)
            && StringComparer.Ordinal.Equals(
                entry.BuildRecipeSha256,
                descriptor.BuildRecipeSha256);
    }

    private static bool ProvenanceMatchesDescriptor(
        LegacyOracleBuildProvenanceIdentity provenance,
        LegacyOracleDescriptor descriptor)
    {
        return StringComparer.Ordinal.Equals(
                provenance.AdapterId,
                descriptor.AdapterId)
            && StringComparer.Ordinal.Equals(
                provenance.VersionId,
                descriptor.VersionId)
            && StringComparer.Ordinal.Equals(
                provenance.Source,
                descriptor.Source)
            && StringComparer.Ordinal.Equals(
                provenance.SourceSha256,
                descriptor.SourceSha256)
            && StringComparer.Ordinal.Equals(
                provenance.BuildRecipe,
                descriptor.BuildRecipe)
            && StringComparer.Ordinal.Equals(
                provenance.BuildRecipeSha256,
                descriptor.BuildRecipeSha256);
    }

    private static T DeserializeStrict<T>(
        byte[] utf8Json,
        string description)
    {
        try
        {
            using JsonDocument document =
                JsonDocument.Parse(utf8Json);
            _ = ParityCanonicalJson.SerializeToUtf8Bytes(
                document.RootElement);
            return JsonSerializer.Deserialize<T>(
                    utf8Json,
                    StrictJsonOptions)
                ?? throw new InvalidDataException(
                    $"{description} is null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"{description} is invalid.",
                exception);
        }
    }

    private static string ResolveRepositoryArtifact(
        string? identity,
        string description)
    {
        if (String.IsNullOrWhiteSpace(identity)
            || Path.IsPathFullyQualified(identity)
            || identity.Contains('\\', StringComparison.Ordinal)
            || identity.Split('/').Any(
                segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException(
                $"{description} identity is not canonical.");
        }

        string root = Path.GetFullPath(RepositoryPaths.Root);
        string resolved = Path.GetFullPath(
            Path.Combine(
                root,
                identity.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!resolved.StartsWith(
                root + Path.DirectorySeparatorChar,
                comparison))
        {
            throw new InvalidDataException(
                $"{description} escaped the repository.");
        }

        return resolved;
    }
}

internal sealed record LegacyOracleBuildSelection(
    string CaseId,
    LegacyOracleDescriptor Descriptor);

internal sealed record LegacyOracleBuildProvenanceIdentity(
    string? AdapterId,
    string? VersionId,
    string? Source,
    string? SourceSha256,
    string? BuildRecipe,
    string? BuildRecipeSha256,
    IReadOnlyList<string>? SelectedCaseIds)
{
    internal static LegacyOracleBuildProvenanceIdentity From(
        LegacyOracleProvenance provenance)
    {
        return new(
            provenance.AdapterId,
            provenance.VersionId,
            provenance.Source,
            provenance.SourceSha256,
            provenance.BuildRecipe,
            provenance.BuildRecipeSha256,
            provenance.SelectedCaseIds);
    }
}

internal sealed record LegacyOracleVersionSelection(
    LegacyOracleDescriptor Descriptor,
    IReadOnlyList<string> SortedCaseIds);
