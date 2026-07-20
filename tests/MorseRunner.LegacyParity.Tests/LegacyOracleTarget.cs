using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MorseRunner.LegacyParity.Tests;

public sealed class LegacyOracleTarget : IParityTarget
{
    private const int ToolchainFingerprintSchemaVersion = 1;
    private const string ToolchainFingerprintCanonicalization =
        "utf8-lf-nul-lowercase-relative-path-v1";

    private const string RegistryEnvironmentVariable =
        "MORSE_RUNNER_LEGACY_ORACLE_REGISTRY";
    private const string RegistryHashEnvironmentVariable =
        "MORSE_RUNNER_LEGACY_ORACLE_REGISTRY_SHA256";
    private const string LegacyRootEnvironmentVariable =
        "MORSE_RUNNER_LEGACY_ROOT";

    private static readonly JsonSerializerOptions StrictJsonOptions =
        new()
        {
            UnmappedMemberHandling =
                JsonUnmappedMemberHandling.Disallow,
        };

    private readonly LegacyOracleConfiguration? _configuration;
    private readonly ILegacyOracleProcessRunner _processRunner;
    private readonly LegacyReferenceDefinition? _referenceDefinition;
    private readonly ILegacyRepositoryInspector _repositoryInspector;

    public LegacyOracleTarget()
        : this(
            configuration: null,
            new LegacyOracleProcessRunner(),
            referenceDefinition: null,
            new GitLegacyRepositoryInspector())
    {
    }

    internal LegacyOracleTarget(
        LegacyOracleConfiguration? configuration,
        ILegacyOracleProcessRunner processRunner,
        LegacyReferenceDefinition? referenceDefinition = null,
        ILegacyRepositoryInspector? repositoryInspector = null)
    {
        _configuration = configuration;
        _processRunner = processRunner;
        _referenceDefinition = referenceDefinition;
        _repositoryInspector =
            repositoryInspector ?? new GitLegacyRepositoryInspector();
    }

    public async Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        LegacyOracleConfiguration configuration =
            _configuration ?? LegacyOracleConfiguration.FromEnvironment();
        ParityObservation? configurationFailure =
            ValidateConfigurationPresence(configuration);
        if (configurationFailure is not null)
        {
            return configurationFailure;
        }

        if (!TryNormalizePaths(
                configuration,
                out string registryPath,
                out string legacyRoot))
        {
            return Failure(
                "legacy-configuration-path-invalid",
                RegistryEnvironmentVariable);
        }

        if (!Directory.Exists(legacyRoot))
        {
            return Failure("legacy-root-not-found", legacyRoot);
        }

        if (!File.Exists(registryPath))
        {
            return Failure(
                "legacy-oracle-registry-not-found",
                registryPath);
        }

        if (!PathHasNoReparsePoints(registryPath))
        {
            return Failure(
                "legacy-oracle-registry-path-invalid",
                registryPath);
        }

        LegacyOracleDescriptor? descriptor =
            scenario.LegacyOracle;
        if (descriptor is null
            || !ParityCanonicalJson.IsLowercaseSha256(
                scenario.CaseDefinitionSha256)
            || !ParityCanonicalJson.IsLowercaseSha256(
                descriptor.SourceSha256)
            || !ParityCanonicalJson.IsLowercaseSha256(
                descriptor.BuildRecipeSha256))
        {
            return Failure(
                "legacy-oracle-case-binding-missing",
                scenario.Id);
        }

        byte[] registryBytes;
        try
        {
            registryBytes = await File.ReadAllBytesAsync(
                registryPath,
                cancellationToken);
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException)
        {
            return Failure(
                "legacy-oracle-registry-invalid",
                $"{registryPath}: {exception.Message}");
        }

        string registrySha256 =
            ParityCanonicalJson.ComputeSha256(registryBytes);
        if (!StringComparer.Ordinal.Equals(
                registrySha256,
                configuration.RegistrySha256))
        {
            return Failure(
                "legacy-oracle-registry-hash-mismatch",
                registryPath);
        }

        ResolvedLegacyOracleEntry? resolvedEntry;
        string? registryFailure;
        try
        {
            (resolvedEntry, registryFailure) =
                ResolveRegistryEntry(
                    registryBytes,
                    registrySha256,
                    descriptor,
                    registryPath);
        }
        catch (Exception exception)
            when (exception is InvalidDataException
                or JsonException
                or UnauthorizedAccessException)
        {
            return Failure(
                "legacy-oracle-registry-invalid",
                $"{registryPath}: {exception.Message}");
        }

        if (registryFailure is not null)
        {
            return Failure(registryFailure, registryPath);
        }

        string oraclePath = resolvedEntry!.ExecutablePath;
        string provenancePath = resolvedEntry.ProvenancePath;
        LegacyOracleResultBinding resultBinding =
            CreateResultBinding(
                descriptor,
                resolvedEntry);
        ParityObservation BoundFailure(
            string failureCode,
            string _)
        {
            return Failure(
                failureCode,
                descriptor.Source,
                resultBinding);
        }

        string? anchorFailure = await ValidateRegistryEntryHashesAsync(
            resolvedEntry,
            cancellationToken);
        if (anchorFailure is not null)
        {
            return BoundFailure(anchorFailure, provenancePath);
        }

        LegacyReferenceDefinition? reference;
        try
        {
            reference = _referenceDefinition
                ?? await LegacyReferenceDefinition.LoadAsync(
                    cancellationToken);
        }
        catch (JsonException)
        {
            return BoundFailure(
                "legacy-reference-invalid",
                GetReferenceDefinitionPath());
        }
        catch (IOException)
        {
            return BoundFailure(
                "legacy-reference-invalid",
                GetReferenceDefinitionPath());
        }

        if (!ValidateReferenceDefinition(reference))
        {
            return BoundFailure(
                "legacy-reference-invalid",
                reference?.DefinitionPath ?? GetReferenceDefinitionPath());
        }

        LegacyOracleProvenance? provenance;
        try
        {
            byte[] provenanceBytes =
                await File.ReadAllBytesAsync(
                    provenancePath,
                    cancellationToken);
            ValidateProvenanceDocumentShape(provenanceBytes);
            provenance = DeserializeStrict<
                LegacyOracleProvenance>(provenanceBytes);
        }
        catch (JsonException)
        {
            return BoundFailure(
                "legacy-provenance-invalid",
                provenancePath);
        }
        catch (InvalidDataException)
        {
            return BoundFailure(
                "legacy-provenance-invalid",
                provenancePath);
        }
        catch (IOException)
        {
            return BoundFailure(
                "legacy-provenance-invalid",
                provenancePath);
        }

        string? provenanceFailure = ValidateProvenanceShape(provenance);
        if (provenanceFailure is not null)
        {
            return BoundFailure(provenanceFailure, provenancePath);
        }

        LegacyReferenceDefinition validatedReference = reference!;
        LegacyOracleProvenance validatedProvenance = provenance!;
        string? identityFailure = ValidateProvenanceIdentity(
            validatedProvenance,
            validatedReference,
            descriptor,
            resolvedEntry,
            oraclePath,
            legacyRoot);
        if (identityFailure is not null)
        {
            return BoundFailure(identityFailure, provenancePath);
        }

        LegacyRepositoryInspection inspection =
            await _repositoryInspector.InspectAsync(
                legacyRoot,
                cancellationToken);
        if (!String.IsNullOrWhiteSpace(inspection.Failure))
        {
            return BoundFailure(
                "legacy-root-inspection-failed",
                $"{legacyRoot}: {inspection.Failure}");
        }

        if (!StringComparer.Ordinal.Equals(
                inspection.Revision,
                validatedReference.Revision))
        {
            return BoundFailure(
                "legacy-revision-mismatch",
                legacyRoot);
        }

        if (!StringComparer.Ordinal.Equals(
                inspection.Tree,
                validatedReference.Tree))
        {
            return BoundFailure(
                "legacy-tree-mismatch",
                legacyRoot);
        }

        if (!inspection.Clean)
        {
            return BoundFailure(
                "legacy-worktree-dirty",
                legacyRoot);
        }

        LegacyRepositoryInspection xplatInspection =
            await _repositoryInspector.InspectAsync(
                RepositoryPaths.Root,
                cancellationToken);
        if (!String.IsNullOrWhiteSpace(xplatInspection.Failure))
        {
            return BoundFailure(
                "xplat-root-inspection-failed",
                $"{RepositoryPaths.Root}: {xplatInspection.Failure}");
        }

        if (!StringComparer.Ordinal.Equals(
                xplatInspection.Revision,
                validatedProvenance.XPlat!.Revision)
            || !StringComparer.Ordinal.Equals(
                xplatInspection.Tree,
                validatedProvenance.XPlat.Tree)
            || xplatInspection.Clean != validatedProvenance.XPlat.Clean)
        {
            return BoundFailure(
                "xplat-provenance-mismatch",
                provenancePath);
        }

        string? artifactFailure = await ValidateArtifactHashesAsync(
            validatedProvenance,
            validatedReference,
            oraclePath,
            cancellationToken);
        if (artifactFailure is not null)
        {
            return BoundFailure(artifactFailure, provenancePath);
        }

        if (!ValidateBuildProvenance(
                validatedProvenance,
                validatedReference,
                descriptor,
                resolvedEntry,
                oraclePath,
                legacyRoot))
        {
            return BoundFailure(
                "legacy-build-provenance-mismatch",
                provenancePath);
        }

        string? runtimeAnchorFailure =
            await ValidateRuntimeUseHashesAsync(
                resolvedEntry,
                afterLaunch: false,
                cancellationToken);
        if (runtimeAnchorFailure is not null)
        {
            return BoundFailure(
                runtimeAnchorFailure,
                provenancePath);
        }

        LegacyOracleProcessResult result;
        try
        {
            result = await _processRunner.ExecuteAsync(
                oraclePath,
                legacyRoot,
                new LegacyOracleInvocation(
                    scenario.Id,
                    scenario.CaseDefinitionSha256!,
                    scenario.InputSha256,
                    scenario.CanonicalInputJson,
                    resolvedEntry.Entry.ExecutableSha256!,
                    descriptor),
                cancellationToken);
        }
        catch (Exception exception)
            when (exception is Win32Exception
                or IOException
                or InvalidOperationException
                or UnauthorizedAccessException)
        {
            return BoundFailure(
                "legacy-oracle-launch-failed",
                $"{oraclePath}: {exception.Message}");
        }

        string? postLaunchAnchorFailure =
            await ValidateRuntimeUseHashesAsync(
                resolvedEntry,
                afterLaunch: true,
                cancellationToken);
        if (postLaunchAnchorFailure is not null)
        {
            return BoundFailure(
                postLaunchAnchorFailure,
                provenancePath);
        }

        LegacyRepositoryInspection postExecutionInspection =
            await _repositoryInspector.InspectAsync(
                legacyRoot,
                cancellationToken);
        if (!String.IsNullOrWhiteSpace(postExecutionInspection.Failure))
        {
            return BoundFailure(
                "legacy-root-inspection-failed",
                $"{legacyRoot}: {postExecutionInspection.Failure}");
        }

        if (!StringComparer.Ordinal.Equals(
                postExecutionInspection.Revision,
                validatedReference.Revision)
            || !StringComparer.Ordinal.Equals(
                postExecutionInspection.Tree,
                validatedReference.Tree)
            || !postExecutionInspection.Clean)
        {
            return BoundFailure(
                "legacy-worktree-changed-during-oracle",
                legacyRoot);
        }

        if (result.ExitCode != 0)
        {
            return BoundFailure(
                "legacy-oracle-failed",
                $"{oraclePath}: {result.StandardError.Trim()}");
        }

        string normalizedOutput = result.StandardOutput.Trim();
        OracleObservation? observation;
        try
        {
            observation = DeserializeStrict<OracleObservation>(
                Encoding.UTF8.GetBytes(normalizedOutput));
        }
        catch (JsonException)
        {
            return BoundFailure(
                "legacy-oracle-output-invalid",
                oraclePath);
        }
        catch (InvalidDataException)
        {
            return BoundFailure(
                "legacy-oracle-output-invalid",
                oraclePath);
        }

        if (observation?.Scenario is null
            || observation.Values is null
            || observation.AdapterId is null
            || observation.VersionId is null
            || observation.Source is null
            || observation.SourceSha256 is null
            || observation.BuildRecipe is null
            || observation.BuildRecipeSha256 is null
            || observation.CaseDefinitionSha256 is null
            || observation.InputSha256 is null)
        {
            return BoundFailure(
                "legacy-oracle-output-invalid",
                oraclePath);
        }

        if (!ObservationBindingMatches(
                observation,
                scenario,
                descriptor))
        {
            return Failure(
                "legacy-oracle-binding-mismatch",
                descriptor.Source,
                resultBinding);
        }

        OracleScenarioProvenance[] scenarioProvenance =
            validatedProvenance.Observations!
                .Where(
                    item => StringComparer.Ordinal.Equals(
                        item.Scenario,
                        scenario.Id))
                .ToArray();
        string outputHash = ComputeSha256(
            Encoding.UTF8.GetBytes(normalizedOutput));
        if (scenarioProvenance.Length != 1
            || scenarioProvenance[0].ValueCount
                != observation.Values.Count
            || !StringComparer.Ordinal.Equals(
                scenarioProvenance[0].OutputSha256,
                outputHash))
        {
            return BoundFailure(
                "legacy-oracle-observation-provenance-mismatch",
                provenancePath);
        }

        bool matches = observation.Values.SequenceEqual(
                scenario.ExpectedValues,
                StringComparer.Ordinal);

        return new ParityObservation(
            matches ? ParityTargetOutcome.Passed : ParityTargetOutcome.Failed,
            observation.Values,
            matches ? null : "legacy-observation-mismatch",
            descriptor.Source,
            resultBinding);
    }

    private static ParityObservation? ValidateConfigurationPresence(
        LegacyOracleConfiguration configuration)
    {
        if (String.IsNullOrWhiteSpace(configuration.RegistryPath))
        {
            return Failure(
                "legacy-oracle-registry-not-configured",
                RegistryEnvironmentVariable);
        }

        if (String.IsNullOrWhiteSpace(
                configuration.RegistrySha256))
        {
            return Failure(
                "legacy-oracle-registry-hash-not-configured",
                RegistryHashEnvironmentVariable);
        }

        if (!ParityCanonicalJson.IsLowercaseSha256(
                configuration.RegistrySha256))
        {
            return Failure(
                "legacy-oracle-registry-hash-invalid",
                RegistryHashEnvironmentVariable);
        }

        return String.IsNullOrWhiteSpace(configuration.LegacyRoot)
            ? Failure(
                "legacy-root-not-configured",
                LegacyRootEnvironmentVariable)
            : null;
    }

    private static (
        ResolvedLegacyOracleEntry? Entry,
        string? FailureCode) ResolveRegistryEntry(
        byte[] registryBytes,
        string registrySha256,
        LegacyOracleDescriptor descriptor,
        string registryPath)
    {
        using JsonDocument document =
            JsonDocument.Parse(registryBytes);
        _ = ParityCanonicalJson.SerializeToUtf8Bytes(
            document.RootElement);
        LegacyOracleRegistryDocument? registry =
            JsonSerializer.Deserialize<
                LegacyOracleRegistryDocument>(
                registryBytes,
                StrictJsonOptions);
        if (registry?.SchemaVersion != 1
            || registry.Entries is not { Count: > 0 }
            || registry.Entries.Any(
                entry => String.IsNullOrWhiteSpace(
                    entry.VersionId))
            || registry.Entries
                .Select(entry => entry.VersionId)
                .Distinct(StringComparer.Ordinal)
                .Count() != registry.Entries.Count)
        {
            return (null, "legacy-oracle-registry-invalid");
        }

        LegacyOracleRegistryEntry[] matches = registry.Entries
            .Where(
                entry => StringComparer.Ordinal.Equals(
                    entry.VersionId,
                    descriptor.VersionId))
            .ToArray();
        if (matches.Length != 1)
        {
            return (null, "legacy-oracle-version-not-found");
        }

        LegacyOracleRegistryEntry entry = matches[0];
        if (!RegistryEntryMatchesDescriptor(
                entry,
                descriptor)
            || !IsLowercaseSha256(entry.ExecutableSha256)
            || !IsLowercaseSha256(entry.ProvenanceSha256)
            || String.IsNullOrWhiteSpace(entry.Executable)
            || String.IsNullOrWhiteSpace(entry.Provenance))
        {
            return (null, "legacy-oracle-registry-binding-mismatch");
        }

        try
        {
            string executablePath =
                ResolveRuntimeArtifactPath(entry.Executable!);
            string provenancePath =
                ResolveRuntimeArtifactPath(entry.Provenance!);
            string sourcePath = ResolveRepositoryIdentityPath(
                entry.Source!);
            string buildRecipePath = ResolveRepositoryIdentityPath(
                entry.BuildRecipe!);
            if (!File.Exists(executablePath)
                || !File.Exists(provenancePath)
                || !File.Exists(sourcePath)
                || !File.Exists(buildRecipePath))
            {
                return (null, "legacy-oracle-registry-artifact-not-found");
            }

            if (!PathHasNoReparsePoints(executablePath)
                || !PathHasNoReparsePoints(provenancePath)
                || !PathHasNoReparsePoints(sourcePath)
                || !PathHasNoReparsePoints(buildRecipePath))
            {
                return (null, "legacy-oracle-registry-path-invalid");
            }

            return (
                new ResolvedLegacyOracleEntry(
                    entry,
                    executablePath,
                    provenancePath,
                    sourcePath,
                    buildRecipePath,
                    registryPath,
                    registrySha256),
                null);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or InvalidDataException
                or NotSupportedException
                or PathTooLongException)
        {
            return (null, "legacy-oracle-registry-path-invalid");
        }
    }

    private static async Task<string?>
        ValidateRegistryEntryHashesAsync(
            ResolvedLegacyOracleEntry resolved,
            CancellationToken cancellationToken)
    {
        (string Path, string Expected, string Failure)[] files =
        [
            (
                resolved.SourcePath,
                resolved.Entry.SourceSha256!,
                "legacy-oracle-source-hash-mismatch"),
            (
                resolved.BuildRecipePath,
                resolved.Entry.BuildRecipeSha256!,
                "legacy-oracle-build-recipe-hash-mismatch"),
            (
                resolved.ExecutablePath,
                resolved.Entry.ExecutableSha256!,
                "legacy-oracle-anchor-mismatch"),
            (
                resolved.ProvenancePath,
                resolved.Entry.ProvenanceSha256!,
                "legacy-provenance-anchor-mismatch"),
        ];
        foreach ((string path, string expected, string failure) in files)
        {
            string actual = await ComputeSha256Async(
                path,
                cancellationToken);
            if (!StringComparer.Ordinal.Equals(
                    actual,
                    expected))
            {
                return failure;
            }
        }

        return null;
    }

    private static async Task<string?> ValidateRuntimeUseHashesAsync(
        ResolvedLegacyOracleEntry resolved,
        bool afterLaunch,
        CancellationToken cancellationToken)
    {
        (
            string Path,
            string Expected,
            string InitialFailure,
            string PostLaunchFailure)[] files =
        [
            (
                resolved.RegistryPath,
                resolved.RegistrySha256,
                "legacy-oracle-registry-hash-mismatch",
                "legacy-oracle-registry-changed-during-oracle"),
            (
                resolved.SourcePath,
                resolved.Entry.SourceSha256!,
                "legacy-oracle-source-hash-mismatch",
                "legacy-oracle-source-changed-during-oracle"),
            (
                resolved.BuildRecipePath,
                resolved.Entry.BuildRecipeSha256!,
                "legacy-oracle-build-recipe-hash-mismatch",
                "legacy-oracle-build-recipe-changed-during-oracle"),
            (
                resolved.ExecutablePath,
                resolved.Entry.ExecutableSha256!,
                "legacy-oracle-anchor-mismatch",
                "legacy-oracle-changed-during-oracle"),
            (
                resolved.ProvenancePath,
                resolved.Entry.ProvenanceSha256!,
                "legacy-provenance-anchor-mismatch",
                "legacy-provenance-changed-during-oracle"),
        ];
        foreach ((
                     string path,
                     string expected,
                     string initialFailure,
                     string postLaunchFailure) in files)
        {
            if (!PathHasNoReparsePoints(path))
            {
                return afterLaunch
                    ? "legacy-oracle-path-changed-during-oracle"
                    : "legacy-oracle-registry-path-invalid";
            }

            string actual;
            try
            {
                actual = await ComputeSha256Async(
                    path,
                    cancellationToken);
            }
            catch (Exception exception)
                when (exception is IOException
                    or UnauthorizedAccessException)
            {
                return afterLaunch
                    ? postLaunchFailure
                    : initialFailure;
            }

            if (!StringComparer.Ordinal.Equals(actual, expected))
            {
                return afterLaunch
                    ? postLaunchFailure
                    : initialFailure;
            }
        }

        return null;
    }

    private static bool RegistryEntryMatchesDescriptor(
        LegacyOracleRegistryEntry entry,
        LegacyOracleDescriptor descriptor)
    {
        return StringComparer.Ordinal.Equals(
                entry.AdapterId,
                descriptor.AdapterId)
            && StringComparer.Ordinal.Equals(
                entry.VersionId,
                descriptor.VersionId)
            && RegistryIdentityMatches(
                entry.Source,
                descriptor.Source)
            && StringComparer.Ordinal.Equals(
                entry.SourceSha256,
                descriptor.SourceSha256)
            && RegistryIdentityMatches(
                entry.BuildRecipe,
                descriptor.BuildRecipe)
            && StringComparer.Ordinal.Equals(
                entry.BuildRecipeSha256,
                descriptor.BuildRecipeSha256);
    }

    private static bool RegistryIdentityMatches(
        string? actual,
        string expectedRepositoryIdentity)
    {
        if (String.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        return StringComparer.Ordinal.Equals(
            actual,
            expectedRepositoryIdentity);
    }

    private static string ResolveRepositoryIdentityPath(
        string identity)
    {
        if (Path.IsPathFullyQualified(identity)
            || identity.Contains('\\', StringComparison.Ordinal)
            || identity
                .Split('/')
                .Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException(
                "Legacy oracle registry repository identity is unsafe.");
        }

        string repositoryRoot = Path.GetFullPath(
            RepositoryPaths.Root);
        string resolved = Path.GetFullPath(
            Path.Combine(
                repositoryRoot,
                identity.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));
        if (!IsDescendantPath(repositoryRoot, resolved))
        {
            throw new InvalidDataException(
                "Legacy oracle registry repository identity escaped "
                + "the repository.");
        }

        return resolved;
    }

    private static string ResolveRuntimeArtifactPath(
        string identity)
    {
        string resolved = ResolveRepositoryIdentityPath(identity);
        string artifactRoot = Path.GetFullPath(
            Path.Combine(
                RepositoryPaths.Root,
                "artifacts",
                "legacy-oracle"));
        if (!IsDescendantPath(artifactRoot, resolved))
        {
            throw new InvalidDataException(
                "Legacy oracle runtime artifact is outside "
                + "'artifacts/legacy-oracle'.");
        }

        return resolved;
    }

    private static bool IsDescendantPath(
        string root,
        string candidate)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string normalizedRoot =
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(root));
        string normalizedCandidate =
            Path.GetFullPath(candidate);
        return StringComparerFrom(comparison).Equals(
                normalizedRoot,
                normalizedCandidate)
            || normalizedCandidate.StartsWith(
                normalizedRoot
                + Path.DirectorySeparatorChar,
                comparison);
    }

    internal static bool PathHasNoReparsePoints(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            string root = Path.GetPathRoot(fullPath)
                ?? throw new InvalidDataException(
                    "Legacy oracle path has no root.");
            string current = root;
            if (HasReparsePoint(current))
            {
                return false;
            }

            string relativePath = Path.GetRelativePath(
                root,
                fullPath);
            if (relativePath == ".")
            {
                return true;
            }

            foreach (string segment in relativePath.Split(
                         [
                             Path.DirectorySeparatorChar,
                             Path.AltDirectorySeparatorChar,
                         ],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if ((!File.Exists(current)
                        && !Directory.Exists(current))
                    || HasReparsePoint(current))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or IOException
                or InvalidDataException
                or NotSupportedException
                or PathTooLongException
                or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool HasReparsePoint(string path)
    {
        return (File.GetAttributes(path)
                & FileAttributes.ReparsePoint)
            != 0;
    }

    private static StringComparer StringComparerFrom(
        StringComparison comparison)
    {
        return comparison == StringComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }

    private static bool TryNormalizePaths(
        LegacyOracleConfiguration configuration,
        out string registryPath,
        out string legacyRoot)
    {
        try
        {
            registryPath = Path.GetFullPath(
                configuration.RegistryPath!);
            legacyRoot = Path.GetFullPath(configuration.LegacyRoot!);
            return true;
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            registryPath = String.Empty;
            legacyRoot = String.Empty;
            return false;
        }
    }

    private static bool ValidateReferenceDefinition(
        LegacyReferenceDefinition? reference)
    {
        return reference is not null
            && reference.SchemaVersion == 1
            && !String.IsNullOrWhiteSpace(reference.Repository)
            && StringComparer.Ordinal.Equals(
                reference.Revision,
                LegacyOracleProvenance.PinnedLegacyRevision)
            && StringComparer.Ordinal.Equals(
                reference.Tree,
                LegacyOracleProvenance.PinnedLegacyTree)
            && File.Exists(reference.DefinitionPath)
            && File.Exists(reference.BundlePath)
            && IsLowercaseSha256(reference.BundleSha256)
            && StringComparer.Ordinal.Equals(
                reference.Toolchain.LazarusVersion,
                "4.6")
            && StringComparer.Ordinal.Equals(
                reference.Toolchain.FpcVersion,
                "3.2.2")
            && StringComparer.Ordinal.Equals(
                reference.Toolchain.TargetCpu,
                "x86_64")
            && StringComparer.Ordinal.Equals(
                reference.Toolchain.TargetOs,
                "win64")
            && IsLowercaseSha256(reference.Toolchain.CompilerSha256)
            && IsLowercaseSha256(
                reference.Toolchain.BackendCompilerSha256)
            && IsLowercaseSha256(reference.Toolchain.LazbuildSha256)
            && reference.Toolchain.Fingerprint.SchemaVersion
                == ToolchainFingerprintSchemaVersion
            && StringComparer.Ordinal.Equals(
                reference.Toolchain.Fingerprint.Canonicalization,
                ToolchainFingerprintCanonicalization)
            && ValidateFingerprintRoots(
                reference.Toolchain.Fingerprint.Roots)
            && IsLowercaseSha256(
                reference.Toolchain.Fingerprint.AggregateSha256)
            && reference.Toolchain.Fingerprint.FileCount > 0
            && reference.Toolchain.Fingerprint.ByteCount > 0;
    }

    private static string? ValidateProvenanceShape(
        LegacyOracleProvenance? provenance)
    {
        if (provenance is null
            || provenance.SchemaVersion
                != LegacyOracleProvenance.CurrentSchemaVersion)
        {
            return "legacy-provenance-schema-mismatch";
        }

        if (String.IsNullOrWhiteSpace(provenance.AdapterId)
            || String.IsNullOrWhiteSpace(provenance.VersionId)
            || String.IsNullOrWhiteSpace(provenance.Source)
            || !IsLowercaseSha256(provenance.SourceSha256)
            || String.IsNullOrWhiteSpace(provenance.BuildRecipe)
            || !IsLowercaseSha256(provenance.BuildRecipeSha256)
            || provenance.SelectedCaseIds is not { Count: > 0 }
            || provenance.SelectedCaseIds.Any(
                String.IsNullOrWhiteSpace)
            || provenance.SelectedCaseIds
                .Distinct(StringComparer.Ordinal)
                .Count() != provenance.SelectedCaseIds.Count
            || !provenance.SelectedCaseIds.SequenceEqual(
                provenance.SelectedCaseIds.OrderBy(
                    id => id,
                    StringComparer.Ordinal),
                StringComparer.Ordinal)
            || provenance.Reference is null
            || String.IsNullOrWhiteSpace(provenance.Reference.Definition)
            || !IsLowercaseSha256(
                provenance.Reference.DefinitionSha256)
            || String.IsNullOrWhiteSpace(provenance.Reference.Bundle)
            || !IsLowercaseSha256(
                provenance.Reference.BundleSha256)
            || provenance.Legacy is null
            || String.IsNullOrWhiteSpace(provenance.Legacy.Repository)
            || String.IsNullOrWhiteSpace(provenance.Legacy.Revision)
            || String.IsNullOrWhiteSpace(provenance.Legacy.Tree)
            || String.IsNullOrWhiteSpace(provenance.Legacy.Root)
            || provenance.XPlat is null
            || !IsGitObjectId(provenance.XPlat.Revision)
            || !IsGitObjectId(provenance.XPlat.Tree)
            || provenance.Oracle is null
            || String.IsNullOrWhiteSpace(provenance.Oracle.Source)
            || String.IsNullOrWhiteSpace(
                provenance.Oracle.SourcePath)
            || !IsLowercaseSha256(
                provenance.Oracle.SourceSha256)
            || String.IsNullOrWhiteSpace(
                provenance.Oracle.BuildRecipe)
            || String.IsNullOrWhiteSpace(
                provenance.Oracle.BuildRecipePath)
            || !IsLowercaseSha256(
                provenance.Oracle.BuildRecipeSha256)
            || String.IsNullOrWhiteSpace(provenance.Oracle.Executable)
            || !IsLowercaseSha256(
                provenance.Oracle.ExecutableSha256)
            || provenance.Oracle.Length < 1
            || provenance.Toolchain is null
            || String.IsNullOrWhiteSpace(provenance.Toolchain.Root)
            || String.IsNullOrWhiteSpace(provenance.Toolchain.LazarusVersion)
            || String.IsNullOrWhiteSpace(provenance.Toolchain.FpcVersion)
            || String.IsNullOrWhiteSpace(provenance.Toolchain.TargetCpu)
            || String.IsNullOrWhiteSpace(provenance.Toolchain.TargetOs)
            || String.IsNullOrWhiteSpace(provenance.Toolchain.Compiler)
            || !IsLowercaseSha256(
                provenance.Toolchain.CompilerSha256)
            || String.IsNullOrWhiteSpace(
                provenance.Toolchain.BackendCompiler)
            || !IsLowercaseSha256(
                provenance.Toolchain.BackendCompilerSha256)
            || String.IsNullOrWhiteSpace(provenance.Toolchain.Lazbuild)
            || !IsLowercaseSha256(
                provenance.Toolchain.LazbuildSha256)
            || provenance.Toolchain.Fingerprint is null
            || provenance.Toolchain.Fingerprint.SchemaVersion < 1
            || String.IsNullOrWhiteSpace(
                provenance.Toolchain.Fingerprint.Canonicalization)
            || !ValidateFingerprintRoots(
                provenance.Toolchain.Fingerprint.Roots)
            || !IsLowercaseSha256(
                provenance.Toolchain.Fingerprint.AggregateSha256)
            || provenance.Toolchain.Fingerprint.FileCount < 1
            || provenance.Toolchain.Fingerprint.ByteCount < 1
            || provenance.Build is null
            || ((provenance.Build.Script is null)
                != (provenance.Build.ScriptSha256 is null))
            || (provenance.Build.Script is not null
                && (String.IsNullOrWhiteSpace(
                        provenance.Build.Script)
                    || !IsLowercaseSha256(
                        provenance.Build.ScriptSha256)))
            || !ValidateStringSequence(provenance.Build.Arguments)
            || provenance.Build.Invocation is null
            || String.IsNullOrWhiteSpace(
                provenance.Build.Invocation.Compiler)
            || !ValidateStringSequence(
                provenance.Build.Invocation.Options)
            || !ValidateStringSequence(
                provenance.Build.Invocation.UnitSearchPaths)
            || !ValidateStringSequence(
                provenance.Build.Invocation.ToolSearchPaths)
            || !ValidateStringSequence(
                provenance.Build.Invocation.LibrarySearchPaths)
            || String.IsNullOrWhiteSpace(
                provenance.Build.Invocation.UnitOutputPath)
            || String.IsNullOrWhiteSpace(
                provenance.Build.Invocation.ExecutableOutputPath)
            || String.IsNullOrWhiteSpace(
                provenance.Build.Invocation.OutputExecutable)
            || String.IsNullOrWhiteSpace(
                provenance.Build.Invocation.Source)
            || !DateTimeOffset.TryParse(
                provenance.Build.BuiltAtUtc,
                out _)
            || provenance.Manifest is null
            || String.IsNullOrWhiteSpace(provenance.Manifest.Path)
            || !IsLowercaseSha256(provenance.Manifest.Sha256)
            || provenance.Observations is null
            || provenance.Observations.Count == 0
            || provenance.Observations.Any(
                observation =>
                    String.IsNullOrWhiteSpace(observation.Scenario)
                    || observation.ValueCount < 0
                    || !IsLowercaseSha256(
                        observation.OutputSha256))
            || provenance.Observations
                .GroupBy(
                    observation => observation.Scenario,
                    StringComparer.Ordinal)
                .Any(group => group.Count() != 1)
            || !provenance.SelectedCaseIds
                .OrderBy(id => id, StringComparer.Ordinal)
                .SequenceEqual(
                    provenance.Observations
                        .Select(observation => observation.Scenario!)
                        .OrderBy(id => id, StringComparer.Ordinal),
                    StringComparer.Ordinal))
        {
            return "legacy-provenance-invalid";
        }

        return null;
    }

    private static void ValidateProvenanceDocumentShape(
        ReadOnlySpan<byte> utf8Json)
    {
        using JsonDocument document = JsonDocument.Parse(
            utf8Json.ToArray());
        _ = ParityCanonicalJson.SerializeToUtf8Bytes(
            document.RootElement);
        JsonElement root = document.RootElement;
        RequireExactObjectProperties(
            root,
            "schemaVersion",
            "adapterId",
            "versionId",
            "source",
            "sourceSha256",
            "buildRecipe",
            "buildRecipeSha256",
            "selectedCaseIds",
            "reference",
            "legacy",
            "xplat",
            "oracle",
            "toolchain",
            "build",
            "manifest",
            "observations");
        RequireArray(root.GetProperty("selectedCaseIds"));
        RequireExactObjectProperties(
            root.GetProperty("reference"),
            "definition",
            "definitionSha256",
            "bundle",
            "bundleSha256");
        RequireExactObjectProperties(
            root.GetProperty("legacy"),
            "repository",
            "revision",
            "tree",
            "root",
            "clean");
        RequireExactObjectProperties(
            root.GetProperty("xplat"),
            "revision",
            "tree",
            "clean");
        RequireExactObjectProperties(
            root.GetProperty("oracle"),
            "source",
            "sourcePath",
            "sourceSha256",
            "buildRecipe",
            "buildRecipePath",
            "buildRecipeSha256",
            "executable",
            "executableSha256",
            "length");
        JsonElement toolchain = root.GetProperty("toolchain");
        RequireExactObjectProperties(
            toolchain,
            "root",
            "lazarusVersion",
            "fpcVersion",
            "targetCpu",
            "targetOs",
            "compiler",
            "compilerSha256",
            "backendCompiler",
            "backendCompilerSha256",
            "lazbuild",
            "lazbuildSha256",
            "fingerprint");
        RequireExactObjectProperties(
            toolchain.GetProperty("fingerprint"),
            "schemaVersion",
            "canonicalization",
            "roots",
            "aggregateSha256",
            "fileCount",
            "byteCount");
        RequireArray(
            toolchain.GetProperty("fingerprint")
                .GetProperty("roots"));
        JsonElement build = root.GetProperty("build");
        RequireExactObjectProperties(
            build,
            "script",
            "scriptSha256",
            "arguments",
            "invocation",
            "builtAtUtc");
        RequireArray(build.GetProperty("arguments"));
        JsonElement invocation = build.GetProperty("invocation");
        RequireExactObjectProperties(
            invocation,
            "compiler",
            "options",
            "unitSearchPaths",
            "toolSearchPaths",
            "librarySearchPaths",
            "unitOutputPath",
            "executableOutputPath",
            "outputExecutable",
            "source");
        RequireArray(invocation.GetProperty("options"));
        RequireArray(invocation.GetProperty("unitSearchPaths"));
        RequireArray(invocation.GetProperty("toolSearchPaths"));
        RequireArray(invocation.GetProperty("librarySearchPaths"));
        RequireExactObjectProperties(
            root.GetProperty("manifest"),
            "path",
            "sha256");
        JsonElement observations =
            root.GetProperty("observations");
        RequireArray(observations);
        foreach (JsonElement observation
                 in observations.EnumerateArray())
        {
            RequireExactObjectProperties(
                observation,
                "scenario",
                "valueCount",
                "outputSha256");
        }
    }

    private static void RequireExactObjectProperties(
        JsonElement element,
        params string[] expectedProperties)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "Legacy oracle provenance contains a non-object value.");
        }

        string[] actualProperties = element
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        string[] expected = expectedProperties
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!actualProperties.SequenceEqual(
                expected,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "Legacy oracle provenance property set is not exact.");
        }
    }

    private static void RequireArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "Legacy oracle provenance contains a non-array value.");
        }
    }

    private static string? ValidateProvenanceIdentity(
        LegacyOracleProvenance provenance,
        LegacyReferenceDefinition reference,
        LegacyOracleDescriptor descriptor,
        ResolvedLegacyOracleEntry resolvedEntry,
        string oraclePath,
        string legacyRoot)
    {
        LegacyOracleRegistryEntry entry =
            resolvedEntry.Entry;
        if (!StringComparer.Ordinal.Equals(
                provenance.AdapterId,
                descriptor.AdapterId)
            || !StringComparer.Ordinal.Equals(
                provenance.VersionId,
                descriptor.VersionId)
            || !RegistryIdentityMatches(
                provenance.Source,
                descriptor.Source)
            || !StringComparer.Ordinal.Equals(
                provenance.SourceSha256,
                descriptor.SourceSha256)
            || !RegistryIdentityMatches(
                provenance.BuildRecipe,
                descriptor.BuildRecipe)
            || !StringComparer.Ordinal.Equals(
                provenance.BuildRecipeSha256,
                descriptor.BuildRecipeSha256)
            || !StringComparer.Ordinal.Equals(
                provenance.Oracle!.ExecutableSha256,
                entry.ExecutableSha256))
        {
            return "legacy-oracle-descriptor-provenance-mismatch";
        }

        if (!PathsEqual(
                provenance.Reference!.Definition!,
                reference.DefinitionPath)
            || !PathsEqual(
                provenance.Reference.Bundle!,
                reference.BundlePath)
            || !StringComparer.Ordinal.Equals(
                provenance.Reference.BundleSha256,
                reference.BundleSha256))
        {
            return "legacy-reference-provenance-mismatch";
        }

        if (!StringComparer.Ordinal.Equals(
                provenance.Legacy!.Repository,
                reference.Repository)
            || !StringComparer.Ordinal.Equals(
                provenance.Legacy.Revision,
                reference.Revision)
            || !StringComparer.Ordinal.Equals(
                provenance.Legacy.Tree,
                reference.Tree)
            || !provenance.Legacy.Clean)
        {
            return "legacy-revision-mismatch";
        }

        if (!PathsEqual(provenance.Legacy.Root!, legacyRoot))
        {
            return "legacy-root-mismatch";
        }

        if (!PathsEqual(provenance.Oracle!.Executable!, oraclePath))
        {
            return "legacy-oracle-path-mismatch";
        }

        if (!RegistryIdentityMatches(
                provenance.Oracle.Source,
                descriptor.Source)
            || !PathsEqual(
                provenance.Oracle.SourcePath!,
                resolvedEntry.SourcePath)
            || !StringComparer.Ordinal.Equals(
                provenance.Oracle.SourceSha256,
                descriptor.SourceSha256)
            || !RegistryIdentityMatches(
                provenance.Oracle.BuildRecipe,
                descriptor.BuildRecipe)
            || !PathsEqual(
                provenance.Oracle.BuildRecipePath!,
                resolvedEntry.BuildRecipePath)
            || !StringComparer.Ordinal.Equals(
                provenance.Oracle.BuildRecipeSha256,
                descriptor.BuildRecipeSha256))
        {
            return "legacy-oracle-source-provenance-mismatch";
        }

        if (!StringComparer.Ordinal.Equals(
                provenance.Toolchain!.LazarusVersion,
                reference.Toolchain.LazarusVersion)
            || !StringComparer.Ordinal.Equals(
                provenance.Toolchain.FpcVersion,
                reference.Toolchain.FpcVersion)
            || !StringComparer.Ordinal.Equals(
                provenance.Toolchain.TargetCpu,
                reference.Toolchain.TargetCpu)
            || !StringComparer.Ordinal.Equals(
                provenance.Toolchain.TargetOs,
                reference.Toolchain.TargetOs)
            || !StringComparer.Ordinal.Equals(
                provenance.Toolchain.CompilerSha256,
                reference.Toolchain.CompilerSha256)
            || !StringComparer.Ordinal.Equals(
                provenance.Toolchain.BackendCompilerSha256,
                reference.Toolchain.BackendCompilerSha256)
            || !StringComparer.Ordinal.Equals(
                provenance.Toolchain.LazbuildSha256,
                reference.Toolchain.LazbuildSha256)
            || provenance.Toolchain.Fingerprint!.SchemaVersion
                != reference.Toolchain.Fingerprint.SchemaVersion
            || !StringComparer.Ordinal.Equals(
                provenance.Toolchain.Fingerprint.Canonicalization,
                reference.Toolchain.Fingerprint.Canonicalization)
            || !provenance.Toolchain.Fingerprint.Roots!
                .SequenceEqual(
                    reference.Toolchain.Fingerprint.Roots,
                    StringComparer.Ordinal)
            || !StringComparer.Ordinal.Equals(
                provenance.Toolchain.Fingerprint.AggregateSha256,
                reference.Toolchain.Fingerprint.AggregateSha256)
            || provenance.Toolchain.Fingerprint.FileCount
                != reference.Toolchain.Fingerprint.FileCount
            || provenance.Toolchain.Fingerprint.ByteCount
                != reference.Toolchain.Fingerprint.ByteCount)
        {
            return "legacy-toolchain-provenance-mismatch";
        }

        if (!ValidateToolchainPaths(provenance.Toolchain))
        {
            return "legacy-toolchain-provenance-mismatch";
        }

        return null;
    }

    private static bool ValidateToolchainPaths(
        OracleToolchainProvenance toolchain)
    {
        try
        {
            string toolchainRoot = Path.GetFullPath(toolchain.Root!);
            if (!Path.IsPathFullyQualified(toolchain.Root!))
            {
                return false;
            }

            string compilerDirectory = Path.Combine(
                toolchainRoot,
                "fpc",
                "3.2.2",
                "bin",
                "x86_64-win64");
            string expectedCompiler = Path.Combine(
                compilerDirectory,
                "fpc.exe");
            string expectedBackendCompiler = Path.Combine(
                compilerDirectory,
                "ppcx64.exe");
            string expectedLazbuild = Path.Combine(
                toolchainRoot,
                "lazbuild.exe");
            return Directory.Exists(toolchainRoot)
                && PathsEqual(
                    toolchain.Compiler!,
                    expectedCompiler)
                && PathsEqual(
                    toolchain.BackendCompiler!,
                    expectedBackendCompiler)
                && PathsEqual(
                    toolchain.Lazbuild!,
                    expectedLazbuild);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }
    }

    private static async Task<string?> ValidateArtifactHashesAsync(
        LegacyOracleProvenance provenance,
        LegacyReferenceDefinition reference,
        string oraclePath,
        CancellationToken cancellationToken)
    {
        string? failure = await ValidateHashAsync(
            reference.DefinitionPath,
            provenance.Reference!.DefinitionSha256!,
            "legacy-reference-hash-mismatch",
            cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        failure = await ValidateHashAsync(
            reference.BundlePath,
            reference.BundleSha256,
            "legacy-reference-bundle-hash-mismatch",
            cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        failure = await ValidateToolchainHashesAsync(
            provenance.Toolchain!,
            reference.Toolchain,
            cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        if (provenance.Build!.Script is not null)
        {
            failure = await ValidateHashAsync(
                provenance.Build.Script,
                provenance.Build.ScriptSha256!,
                "legacy-build-script-hash-mismatch",
                cancellationToken);
            if (failure is not null)
            {
                return failure;
            }
        }

        string manifestPath = Path.Combine(
            RepositoryPaths.Root,
            "tests",
            "parity",
            "parity-manifest.json");
        if (!PathsEqual(
                provenance.Manifest!.Path!,
                manifestPath))
        {
            return "legacy-manifest-provenance-mismatch";
        }

        failure = await ValidateHashAsync(
            manifestPath,
            provenance.Manifest.Sha256!,
            "legacy-manifest-hash-mismatch",
            cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        FileInfo oracleFile = new(oraclePath);
        if (oracleFile.Length != provenance.Oracle!.Length)
        {
            return "legacy-oracle-length-mismatch";
        }

        return await ValidateHashAsync(
            oraclePath,
            provenance.Oracle.ExecutableSha256!,
            "legacy-oracle-hash-mismatch",
            cancellationToken);
    }

    private static async Task<string?> ValidateToolchainHashesAsync(
        OracleToolchainProvenance provenance,
        LegacyReferenceToolchain reference,
        CancellationToken cancellationToken)
    {
        string? failure = await ValidateHashAsync(
            provenance.Compiler!,
            reference.CompilerSha256,
            "legacy-compiler-hash-mismatch",
            cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        failure = await ValidateHashAsync(
            provenance.BackendCompiler!,
            reference.BackendCompilerSha256,
            "legacy-backend-compiler-hash-mismatch",
            cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        return await ValidateHashAsync(
            provenance.Lazbuild!,
            reference.LazbuildSha256,
            "legacy-lazbuild-hash-mismatch",
            cancellationToken);
    }

    private static async Task<string?> ValidateHashAsync(
        string path,
        string expectedHash,
        string failureCode,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return failureCode;
        }

        string actualHash = await ComputeSha256Async(
            path,
            cancellationToken);
        return StringComparer.Ordinal.Equals(
                actualHash,
                expectedHash)
            ? null
            : failureCode;
    }

    private static bool ValidateBuildProvenance(
        LegacyOracleProvenance provenance,
        LegacyReferenceDefinition reference,
        LegacyOracleDescriptor descriptor,
        ResolvedLegacyOracleEntry resolvedEntry,
        string oraclePath,
        string legacyRoot)
    {
        try
        {
            OracleBuildProvenance build = provenance.Build!;
            OracleBuildInvocationProvenance invocation =
                build.Invocation!;
            OracleToolchainProvenance toolchain =
                provenance.Toolchain!;
            string toolchainRoot = Path.GetFullPath(toolchain.Root!);
            string executableOutputRoot =
                Path.GetDirectoryName(oraclePath)!;
            string buildRoot =
                Directory.GetParent(executableOutputRoot)?.FullName
                ?? throw new InvalidDataException(
                    "Legacy oracle executable has no build root.");
            LegacyOracleBuildRecipe? recipe =
                DeserializeStrict<LegacyOracleBuildRecipe>(
                    File.ReadAllBytes(
                        resolvedEntry.BuildRecipePath));
            if (!ValidateBuildRecipeIdentity(
                    recipe,
                    reference,
                    descriptor)
                || recipe!.Invocation?.Arguments is not
                { Count: > 0 } argumentTemplates
                || argumentTemplates.Any(
                    String.IsNullOrWhiteSpace))
            {
                return false;
            }

            string unitOutput =
                Path.Combine(buildRoot, "units");
            IReadOnlyDictionary<string, string> replacements =
                new Dictionary<string, string>(
                    StringComparer.Ordinal)
                {
                    ["{toolchainRoot}"] = toolchainRoot,
                    ["{legacyRoot}"] = legacyRoot,
                    ["{unitOutput}"] = unitOutput,
                    ["{executableOutput}"] = executableOutputRoot,
                    ["{executable}"] = oraclePath,
                    ["{source}"] = resolvedEntry.SourcePath,
                };
            string expectedCompiler = ExpandBuildRecipeValue(
                recipe.Invocation.Compiler!,
                replacements);
            string[] expectedArguments = argumentTemplates
                .Select(
                    argument => ExpandBuildRecipeValue(
                        argument!,
                        replacements))
                .ToArray();
            string[] expectedOptions = expectedArguments
                .Where(
                    argument => !argument.StartsWith(
                            "-Fu",
                            StringComparison.Ordinal)
                        && !argument.StartsWith(
                            "-FD",
                            StringComparison.Ordinal)
                        && !argument.StartsWith(
                            "-Fl",
                            StringComparison.Ordinal)
                        && !argument.StartsWith(
                            "-FU",
                            StringComparison.Ordinal)
                        && !argument.StartsWith(
                            "-FE",
                            StringComparison.Ordinal)
                        && !argument.StartsWith(
                            "-o",
                            StringComparison.Ordinal)
                        && !PathsEqual(
                            argument,
                            resolvedEntry.SourcePath))
                .ToArray();
            string[] expectedUnitSearchPaths =
                ExtractBuildPaths(expectedArguments, "-Fu");
            string[] expectedToolSearchPaths =
                ExtractBuildPaths(expectedArguments, "-FD");
            string[] expectedLibrarySearchPaths =
                ExtractBuildPaths(expectedArguments, "-Fl");

            return PathsEqual(
                    invocation.Compiler!,
                    expectedCompiler)
                && PathsEqual(
                    invocation.Compiler!,
                    toolchain.Compiler!)
                && invocation.Options!.SequenceEqual(
                    expectedOptions,
                    StringComparer.Ordinal)
                && PathSequencesEqual(
                    invocation.UnitSearchPaths,
                    expectedUnitSearchPaths)
                && PathSequencesEqual(
                    invocation.ToolSearchPaths,
                    expectedToolSearchPaths)
                && PathSequencesEqual(
                    invocation.LibrarySearchPaths,
                    expectedLibrarySearchPaths)
                && PathsEqual(
                    invocation.UnitOutputPath!,
                    unitOutput)
                && PathsEqual(
                    invocation.ExecutableOutputPath!,
                    executableOutputRoot)
                && PathsEqual(
                    invocation.OutputExecutable!,
                    oraclePath)
                && PathsEqual(
                    invocation.Source!,
                    resolvedEntry.SourcePath)
                && build.Arguments!.SequenceEqual(
                    expectedArguments,
                    StringComparer.Ordinal);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or IOException
                or InvalidDataException
                or JsonException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }
    }

    private static bool ValidateBuildRecipeIdentity(
        LegacyOracleBuildRecipe? recipe,
        LegacyReferenceDefinition reference,
        LegacyOracleDescriptor descriptor)
    {
        LegacyOracleBuildRecipeSourceClosure? closure =
            recipe?.SourceClosure;
        return recipe?.SchemaVersion == 1
            && StringComparer.Ordinal.Equals(
                recipe.AdapterId,
                descriptor.AdapterId)
            && StringComparer.Ordinal.Equals(
                recipe.VersionId,
                descriptor.VersionId)
            && closure is not null
            && StringComparer.Ordinal.Equals(
                closure.OracleSource,
                descriptor.Source)
            && StringComparer.Ordinal.Equals(
                closure.OracleSourceSha256,
                descriptor.SourceSha256)
            && StringComparer.Ordinal.Equals(
                closure.LegacyRevision,
                reference.Revision)
            && StringComparer.Ordinal.Equals(
                closure.LegacyTree,
                reference.Tree)
            && StringComparer.Ordinal.Equals(
                closure.LegacyBundleSha256,
                reference.BundleSha256)
            && StringComparer.Ordinal.Equals(
                closure.ToolchainFingerprintSha256,
                reference.Toolchain.Fingerprint.AggregateSha256)
            && !String.IsNullOrWhiteSpace(
                recipe.Invocation?.Compiler);
    }

    private static string ExpandBuildRecipeValue(
        string value,
        IReadOnlyDictionary<string, string> replacements)
    {
        string result = value;
        foreach ((string placeholder, string replacement) in replacements)
        {
            result = result.Replace(
                placeholder,
                replacement,
                StringComparison.Ordinal);
        }

        if (result.Contains('{', StringComparison.Ordinal)
            || result.Contains('}', StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Legacy oracle build recipe has an unknown placeholder.");
        }

        return result;
    }

    private static string[] ExtractBuildPaths(
        IReadOnlyList<string> arguments,
        string prefix)
    {
        return
        [
            .. arguments
                .Where(
                    argument => argument.StartsWith(
                        prefix,
                        StringComparison.Ordinal))
                .Select(argument => argument[prefix.Length..]),
        ];
    }

    private static bool PathSequencesEqual(
        IReadOnlyList<string?>? actual,
        string[] expected)
    {
        if (actual is null || actual.Count != expected.Length)
        {
            return false;
        }

        for (int index = 0; index < expected.Length; index++)
        {
            if (actual[index] is not { } actualPath
                || !PathsEqual(actualPath, expected[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidateStringSequence(
        IReadOnlyList<string?>? values)
    {
        return values is { Count: > 0 }
            && values.All(value => !String.IsNullOrWhiteSpace(value));
    }

    private static bool ValidateFingerprintRoots(
        IReadOnlyList<string>? roots)
    {
        if (roots is not { Count: > 0 })
        {
            return false;
        }

        HashSet<string> uniqueRoots =
            new(StringComparer.Ordinal);
        foreach (string root in roots)
        {
            if (String.IsNullOrWhiteSpace(root)
                || Path.IsPathFullyQualified(root)
                || root.Contains('\\', StringComparison.Ordinal)
                || root.Split('/').Any(
                    segment => segment is "" or "." or "..")
                || !uniqueRoots.Add(root))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLowercaseSha256(string? value)
    {
        return ParityCanonicalJson.IsLowercaseSha256(value);
    }

    private static bool IsGitObjectId(string? value)
    {
        return value is { Length: 40 }
            && value.All(
                character =>
                    character is >= '0' and <= '9'
                    or >= 'a' and <= 'f'
                    or >= 'A' and <= 'F');
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            string normalizedLeft = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(left));
            string normalizedRight = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(right));
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return String.Equals(
                normalizedLeft,
                normalizedRight,
                comparison);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static string ComputeSha256(ReadOnlySpan<byte> value)
    {
        return Convert.ToHexStringLower(SHA256.HashData(value));
    }

    private static T? DeserializeStrict<T>(
        ReadOnlySpan<byte> utf8Json)
    {
        byte[] bytes = utf8Json.ToArray();
        using JsonDocument document = JsonDocument.Parse(bytes);
        _ = ParityCanonicalJson.SerializeToUtf8Bytes(
            document.RootElement);
        return JsonSerializer.Deserialize<T>(
            bytes,
            StrictJsonOptions);
    }

    private static string GetReferenceDefinitionPath()
    {
        return Path.Combine(
            RepositoryPaths.Root,
            "tests",
            "parity",
            "legacy-reference.json");
    }

    private static bool ObservationBindingMatches(
        OracleObservation observation,
        ParityScenario scenario,
        LegacyOracleDescriptor descriptor)
    {
        return StringComparer.Ordinal.Equals(
                observation.Scenario,
                scenario.Id)
            && StringComparer.Ordinal.Equals(
                observation.AdapterId,
                descriptor.AdapterId)
            && StringComparer.Ordinal.Equals(
                observation.VersionId,
                descriptor.VersionId)
            && StringComparer.Ordinal.Equals(
                observation.Source,
                descriptor.Source)
            && StringComparer.Ordinal.Equals(
                observation.SourceSha256,
                descriptor.SourceSha256)
            && StringComparer.Ordinal.Equals(
                observation.BuildRecipe,
                descriptor.BuildRecipe)
            && StringComparer.Ordinal.Equals(
                observation.BuildRecipeSha256,
                descriptor.BuildRecipeSha256)
            && StringComparer.Ordinal.Equals(
                observation.CaseDefinitionSha256,
                scenario.CaseDefinitionSha256)
            && StringComparer.Ordinal.Equals(
                observation.InputSha256,
                scenario.InputSha256);
    }

    private static LegacyOracleResultBinding CreateResultBinding(
        LegacyOracleDescriptor descriptor,
        ResolvedLegacyOracleEntry resolved)
    {
        return new LegacyOracleResultBinding(
            descriptor.AdapterId,
            descriptor.VersionId,
            descriptor.Source,
            descriptor.SourceSha256,
            descriptor.BuildRecipe,
            descriptor.BuildRecipeSha256,
            resolved.RegistrySha256,
            resolved.Entry.ExecutableSha256!,
            resolved.Entry.Provenance!,
            resolved.Entry.ProvenanceSha256!);
    }

    private static ParityObservation Failure(
        string failureCode,
        string evidenceSource,
        LegacyOracleResultBinding? legacyOracle = null)
    {
        return new ParityObservation(
            ParityTargetOutcome.Failed,
            [],
            failureCode,
            evidenceSource,
            legacyOracle);
    }

    private sealed record OracleObservation(
        [property: JsonPropertyName("scenario")] string? Scenario,
        [property: JsonPropertyName("adapterId")] string? AdapterId,
        [property: JsonPropertyName("versionId")] string? VersionId,
        [property: JsonPropertyName("source")] string? Source,
        [property: JsonPropertyName("sourceSha256")] string? SourceSha256,
        [property: JsonPropertyName("buildRecipe")] string? BuildRecipe,
        [property: JsonPropertyName("buildRecipeSha256")]
        string? BuildRecipeSha256,
        [property: JsonPropertyName("caseDefinitionSha256")]
        string? CaseDefinitionSha256,
        [property: JsonPropertyName("inputSha256")] string? InputSha256,
        [property: JsonPropertyName("values")] IReadOnlyList<string>? Values);
}

internal sealed record LegacyOracleConfiguration(
    string? RegistryPath,
    string? RegistrySha256,
    string? LegacyRoot)
{
    public static LegacyOracleConfiguration FromEnvironment()
    {
        return new LegacyOracleConfiguration(
            Environment.GetEnvironmentVariable(
                "MORSE_RUNNER_LEGACY_ORACLE_REGISTRY"),
            Environment.GetEnvironmentVariable(
                "MORSE_RUNNER_LEGACY_ORACLE_REGISTRY_SHA256"),
            Environment.GetEnvironmentVariable(
                "MORSE_RUNNER_LEGACY_ROOT"));
    }
}

internal sealed record LegacyOracleInvocation(
    string ScenarioId,
    string CaseDefinitionSha256,
    string InputSha256,
    string CanonicalInputJson,
    string ExecutableSha256,
    LegacyOracleDescriptor Descriptor);

internal interface ILegacyOracleProcessRunner
{
    Task<LegacyOracleProcessResult> ExecuteAsync(
        string oraclePath,
        string legacyRoot,
        LegacyOracleInvocation invocation,
        CancellationToken cancellationToken);
}

internal sealed record LegacyOracleProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal sealed class LegacyOracleProcessRunner : ILegacyOracleProcessRunner
{
    public async Task<LegacyOracleProcessResult> ExecuteAsync(
        string oraclePath,
        string legacyRoot,
        LegacyOracleInvocation invocation,
        CancellationToken cancellationToken)
    {
        byte[] inputBytes = Encoding.UTF8.GetBytes(
            invocation.CanonicalInputJson);
        if (!StringComparer.Ordinal.Equals(
                ParityCanonicalJson.ComputeSha256(inputBytes),
                invocation.InputSha256))
        {
            throw new InvalidOperationException(
                "Legacy oracle input digest is stale.");
        }

        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"mrx-legacy-oracle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        string inputPath = Path.Combine(
            temporaryDirectory,
            invocation.InputSha256 + ".json");
        string privateOraclePath = Path.Combine(
            temporaryDirectory,
            OperatingSystem.IsWindows()
                ? "LegacyOracle.exe"
                : "LegacyOracle");

        try
        {
            byte[] executableBytes = await File.ReadAllBytesAsync(
                oraclePath,
                cancellationToken);
            if (!StringComparer.Ordinal.Equals(
                    ParityCanonicalJson.ComputeSha256(
                        executableBytes),
                    invocation.ExecutableSha256))
            {
                throw new InvalidDataException(
                    "Legacy oracle executable digest is stale.");
            }

            await File.WriteAllBytesAsync(
                privateOraclePath,
                executableBytes,
                cancellationToken);
            MakePrivateOracleReadOnly(
                oraclePath,
                privateOraclePath);
            if (!StringComparer.Ordinal.Equals(
                    await ComputePrivateOracleHashAsync(
                        privateOraclePath,
                        cancellationToken),
                    invocation.ExecutableSha256))
            {
                throw new InvalidDataException(
                    "Private legacy oracle copy digest is invalid.");
            }

            await File.WriteAllBytesAsync(
                inputPath,
                inputBytes,
                cancellationToken);
            ProcessStartInfo startInfo = new(privateOraclePath)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(
                Path.TrimEndingDirectorySeparator(legacyRoot)
                + Path.DirectorySeparatorChar);
            startInfo.ArgumentList.Add(invocation.ScenarioId);
            startInfo.ArgumentList.Add(
                invocation.Descriptor.AdapterId);
            startInfo.ArgumentList.Add(
                invocation.Descriptor.VersionId);
            startInfo.ArgumentList.Add(
                invocation.Descriptor.Source);
            startInfo.ArgumentList.Add(
                invocation.Descriptor.SourceSha256);
            startInfo.ArgumentList.Add(
                invocation.Descriptor.BuildRecipe);
            startInfo.ArgumentList.Add(
                invocation.Descriptor.BuildRecipeSha256);
            startInfo.ArgumentList.Add(
                invocation.CaseDefinitionSha256);
            startInfo.ArgumentList.Add(invocation.InputSha256);
            startInfo.ArgumentList.Add(inputPath);

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    $"Could not start legacy oracle '{privateOraclePath}'.");
            Task<string> outputTask =
                process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask =
                process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (!StringComparer.Ordinal.Equals(
                    await ComputePrivateOracleHashAsync(
                        privateOraclePath,
                        cancellationToken),
                    invocation.ExecutableSha256))
            {
                throw new InvalidDataException(
                    "Private legacy oracle copy changed during execution.");
            }

            return new LegacyOracleProcessResult(
                process.ExitCode,
                await outputTask,
                await errorTask);
        }
        finally
        {
            if (File.Exists(privateOraclePath))
            {
                File.SetAttributes(
                    privateOraclePath,
                    FileAttributes.Normal);
                File.Delete(privateOraclePath);
            }

            if (File.Exists(inputPath))
            {
                File.Delete(inputPath);
            }

            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory);
            }
        }
    }

    private static void MakePrivateOracleReadOnly(
        string sourcePath,
        string privateOraclePath)
    {
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(
                privateOraclePath,
                FileAttributes.ReadOnly);
            return;
        }

        UnixFileMode mode = File.GetUnixFileMode(sourcePath);
        mode &= ~(UnixFileMode.UserWrite
            | UnixFileMode.GroupWrite
            | UnixFileMode.OtherWrite);
        File.SetUnixFileMode(privateOraclePath, mode);
    }

    private static async Task<string> ComputePrivateOracleHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(
            stream,
            cancellationToken);
        return Convert.ToHexStringLower(hash);
    }
}

internal sealed record ResolvedLegacyOracleEntry(
    LegacyOracleRegistryEntry Entry,
    string ExecutablePath,
    string ProvenancePath,
    string SourcePath,
    string BuildRecipePath,
    string RegistryPath,
    string RegistrySha256);
