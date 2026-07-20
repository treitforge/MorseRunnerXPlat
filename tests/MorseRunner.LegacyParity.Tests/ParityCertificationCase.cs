using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

internal sealed record ParityCertificationCase(
    ParityScenario Scenario,
    string CaseDefinitionSha256,
    string FixtureSha256,
    string FixturePath,
    IReadOnlyList<string> ObligationIds,
    IReadOnlyList<string> TargetAdapters,
    IReadOnlyList<string> Platforms,
    string FunctionalDivergenceCode)
{
    public const string CaseDefinitionCanonicalization =
        "utf8-compact-json-recursive-ordinal-keys-unescaped-nonascii-v1";

    private static readonly string[] SemanticFieldNames =
    [
        "assertions",
        "behavior",
        "capabilityId",
        "fixture",
        "id",
        "input",
        "legacyOracle",
        "legacySources",
        "legacySurfaceSelectors",
        "obligationIds",
        "platforms",
        "preconditions",
        "targetAdapters",
    ];

    private static readonly string[] AllowedCaseFieldNames =
    [
        "assertions",
        "behavior",
        "capabilityId",
        "evidence",
        "failureCode",
        "firstGreenCommit",
        "fixture",
        "id",
        "input",
        "legacyOracle",
        "legacySources",
        "legacySurfaceSelectors",
        "legacyTestStatus",
        "obligationIds",
        "platforms",
        "preconditions",
        "status",
        "targetAdapters",
        "xplatTestStatus",
    ];

    public string Id => Scenario.Id;

    internal bool HasSameExecutionDefinition(
        ParityCertificationCase other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return StringComparer.Ordinal.Equals(Id, other.Id)
            && StringComparer.Ordinal.Equals(
                Scenario.Capability,
                other.Scenario.Capability)
            && Scenario.ExpectedValues.SequenceEqual(
                other.Scenario.ExpectedValues,
                StringComparer.Ordinal)
            && StringComparer.Ordinal.Equals(
                Scenario.CanonicalInputJson,
                other.Scenario.CanonicalInputJson)
            && StringComparer.Ordinal.Equals(
                Scenario.InputSha256,
                other.Scenario.InputSha256)
            && Scenario.LegacyOracle
                == other.Scenario.LegacyOracle
            && StringComparer.Ordinal.Equals(
                CaseDefinitionSha256,
                other.CaseDefinitionSha256)
            && StringComparer.Ordinal.Equals(
                FixtureSha256,
                other.FixtureSha256)
            && StringComparer.Ordinal.Equals(
                FixturePath,
                other.FixturePath)
            && ObligationIds.SequenceEqual(
                other.ObligationIds,
                StringComparer.Ordinal)
            && TargetAdapters.SequenceEqual(
                other.TargetAdapters,
                StringComparer.Ordinal)
            && Platforms.SequenceEqual(
                other.Platforms,
                StringComparer.Ordinal)
            && StringComparer.Ordinal.Equals(
                FunctionalDivergenceCode,
                other.FunctionalDivergenceCode);
    }

    public static ParityCertificationCase Load(string parityId)
    {
        string manifestPath = Path.Combine(
            RepositoryPaths.Root,
            "tests",
            "parity",
            "parity-manifest.json");
        return Load(
            parityId,
            manifestPath,
            RepositoryPaths.Root,
            ParityRunEnvironment.Capture().Platform);
    }

    internal static ParityCertificationCase LoadForInspection(
        string parityId)
    {
        string manifestPath = Path.Combine(
            RepositoryPaths.Root,
            "tests",
            "parity",
            "parity-manifest.json");
        return Load(
            parityId,
            manifestPath,
            RepositoryPaths.Root,
            requiredPlatform: null);
    }

    internal static ParityCertificationCase Load(
        string parityId,
        string manifestPath,
        string repositoryRoot,
        string? requiredPlatform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parityId);
        byte[] manifestBytes = File.ReadAllBytes(
            Path.GetFullPath(manifestPath));
        using JsonDocument manifest = JsonDocument.Parse(manifestBytes);
        if (manifest.RootElement.GetProperty(
                "schemaVersion").GetInt32() != 3)
        {
            throw new InvalidDataException(
                "Parity manifest schema version is not 3.");
        }

        JsonElement[] matchingCases = manifest.RootElement
            .GetProperty("cases")
            .EnumerateArray()
            .Where(
                item => StringComparer.Ordinal.Equals(
                    item.GetProperty("id").GetString(),
                    parityId))
            .ToArray();
        if (matchingCases.Length != 1)
        {
            throw new InvalidDataException(
                $"Parity case '{parityId}' must occur exactly once.");
        }

        JsonElement caseElement = matchingCases[0];
        ValidateCaseShape(caseElement, parityId);
        string capabilityId = RequireString(
            caseElement,
            "capabilityId",
            parityId);
        string fixtureRelativePath = RequireString(
            caseElement,
            "fixture",
            parityId);
        string[] obligationIds = RequireStringArray(
            caseElement,
            "obligationIds",
            parityId);
        if (obligationIds.Length != 1)
        {
            throw new InvalidDataException(
                $"Parity case '{parityId}' must bind one obligation.");
        }
        string[] targetAdapters = RequireStringArray(
            caseElement,
            "targetAdapters",
            parityId);
        string[] platforms = RequireStringArray(
            caseElement,
            "platforms",
            parityId);
        ValidatePlatforms(platforms, parityId);
        JsonElement input = caseElement
            .GetProperty("input")
            .Clone();
        LegacyOracleDescriptor legacyOracle =
            LoadLegacyOracleDescriptor(
                caseElement.GetProperty("legacyOracle"),
                parityId,
                repositoryRoot);
        JsonElement assertions =
            caseElement.GetProperty("assertions");
        string functionalDivergenceCode = RequireString(
            assertions,
            "functionalDivergenceCode",
            parityId);

        string fixturePath = ResolveFixturePath(
            fixtureRelativePath,
            repositoryRoot);
        byte[] fixtureBytes = File.ReadAllBytes(fixturePath);
        if (fixtureBytes.Contains((byte)'\r'))
        {
            throw new InvalidDataException(
                $"Parity fixture '{fixtureRelativePath}' is not LF-stable.");
        }

        string[] expectedValues = LoadFixtureValues(
            fixtureBytes,
            parityId);
        if (!StringComparer.Ordinal.Equals(
                assertions.GetProperty(
                    "fixtureComparison").GetString(),
                "exact")
            || assertions.GetProperty(
                "observedValueCount").GetInt32()
                != expectedValues.Length)
        {
            throw new InvalidDataException(
                $"Parity case '{parityId}' assertions do not match its fixture.");
        }

        string expectedValuesSha256 = RequireString(
            assertions,
            "observedValuesSha256",
            parityId);
        if (!IsLowercaseSha256(expectedValuesSha256)
            || !StringComparer.Ordinal.Equals(
                expectedValuesSha256,
                ParityObservedValuesDigest.Compute(expectedValues)))
        {
            throw new InvalidDataException(
                $"Parity case '{parityId}' observed-values hash is stale.");
        }

        string caseDefinitionSha256 =
            ComputeCaseDefinitionSha256(caseElement);
        ParityCertificationCase definition = new(
            new ParityScenario(
                parityId,
                capabilityId,
                expectedValues,
                input,
                caseDefinitionSha256,
                legacyOracle),
            caseDefinitionSha256,
            ComputeSha256(fixtureBytes),
            fixturePath,
            obligationIds,
            targetAdapters,
            platforms,
            functionalDivergenceCode);
        ParityAcceptanceRegistry.Get(parityId)
            .ValidateManifestBinding(definition);
        if (requiredPlatform is not null
            && !platforms.Contains(
                requiredPlatform,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Parity case '{parityId}' does not declare platform "
                + $"'{requiredPlatform}'.");
        }

        return definition;
    }

    internal static string ComputeCaseDefinitionSha256(
        JsonElement caseElement)
    {
        return ParityCanonicalJson.ComputeProjectionSha256(
            caseElement,
            SemanticFieldNames);
    }

    internal static void ValidateCaseShape(
        JsonElement caseElement,
        string parityId)
    {
        ValidateExactProperties(
            caseElement,
            AllowedCaseFieldNames,
            $"Parity case '{parityId}'");
        _ = ComputeCaseDefinitionSha256(caseElement);
    }

    private static string[] LoadFixtureValues(
        byte[] fixtureBytes,
        string parityId)
    {
        using JsonDocument fixture = JsonDocument.Parse(fixtureBytes);
        JsonElement root = fixture.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 2
            || !StringComparer.Ordinal.Equals(
                root.GetProperty("revision").GetString(),
                LegacyOracleProvenance.PinnedLegacyRevision)
            || !StringComparer.Ordinal.Equals(
                root.GetProperty("tree").GetString(),
                LegacyOracleProvenance.PinnedLegacyTree)
            || !StringComparer.Ordinal.Equals(
                root.GetProperty("parityId").GetString(),
                parityId))
        {
            throw new InvalidDataException(
                $"Parity fixture for '{parityId}' has stale identity.");
        }

        JsonElement values = root.GetProperty("values");
        if (values.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"Parity fixture for '{parityId}' has no value array.");
        }

        List<string> result = [];
        foreach (JsonElement value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException(
                    $"Parity fixture for '{parityId}' has a non-string value.");
            }

            result.Add(value.GetString()!);
        }

        return [.. result];
    }

    private static string ResolveFixturePath(
        string relativePath,
        string repositoryRoot)
    {
        if (Path.IsPathFullyQualified(relativePath)
            || relativePath
                .Split(['/', '\\'])
                .Any(segment => segment == ".."))
        {
            throw new InvalidDataException(
                $"Parity fixture path '{relativePath}' is unsafe.");
        }

        string fixtureRoot = Path.GetFullPath(
            Path.Combine(
                repositoryRoot,
                "tests",
                "parity",
                "fixtures",
                "legacy"));
        string resolved = Path.GetFullPath(
            Path.Combine(repositoryRoot, relativePath));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!resolved.StartsWith(
                fixtureRoot
                + Path.DirectorySeparatorChar,
                comparison))
        {
            throw new InvalidDataException(
                $"Parity fixture path '{relativePath}' is outside fixtures.");
        }

        return resolved;
    }

    private static string RequireString(
        JsonElement owner,
        string propertyName,
        string parityId)
    {
        JsonElement value = owner.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.String
            || String.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException(
                $"Parity case '{parityId}' has invalid {propertyName}.");
        }

        return value.GetString()!;
    }

    private static string[] RequireStringArray(
        JsonElement owner,
        string propertyName,
        string parityId)
    {
        JsonElement value = owner.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"Parity case '{parityId}' has invalid {propertyName}.");
        }

        string[] result = value
            .EnumerateArray()
            .Select(
                item => item.ValueKind == JsonValueKind.String
                    ? item.GetString()
                    : null)
            .Where(item => !String.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();
        if (result.Length != value.GetArrayLength())
        {
            throw new InvalidDataException(
                $"Parity case '{parityId}' has invalid {propertyName}.");
        }

        return result;
    }

    private static void ValidatePlatforms(
        string[] platforms,
        string parityId)
    {
        string[] supportedPlatforms =
        [
            "windows",
            "linux",
            "macos",
        ];
        if (platforms.Length == 0
            || platforms.Distinct(StringComparer.Ordinal).Count()
                != platforms.Length
            || platforms.Any(
                platform => !supportedPlatforms.Contains(
                    platform,
                    StringComparer.Ordinal)))
        {
            throw new InvalidDataException(
                $"Parity case '{parityId}' platforms are invalid.");
        }
    }

    private static LegacyOracleDescriptor LoadLegacyOracleDescriptor(
        JsonElement value,
        string parityId,
        string repositoryRoot)
    {
        ValidateExactProperties(
            value,
            [
                "adapterId",
                "buildRecipe",
                "buildRecipeSha256",
                "source",
                "sourceSha256",
                "versionId",
            ],
            $"Parity case '{parityId}' legacyOracle");
        LegacyOracleDescriptor descriptor = new(
            RequireString(value, "adapterId", parityId),
            RequireString(value, "versionId", parityId),
            RequireString(value, "source", parityId),
            RequireString(value, "sourceSha256", parityId),
            RequireString(value, "buildRecipe", parityId),
            RequireString(value, "buildRecipeSha256", parityId));
        if (!ParityCanonicalJson.IsLowercaseSha256(
                descriptor.SourceSha256)
            || !ParityCanonicalJson.IsLowercaseSha256(
                descriptor.BuildRecipeSha256))
        {
            throw new InvalidDataException(
                $"Parity case '{parityId}' legacyOracle hashes are invalid.");
        }

        ValidateLegacyOracleVersionBinding(descriptor, parityId);
        string sourcePath = ResolveLegacyOraclePath(
            descriptor.Source,
            repositoryRoot);
        string buildRecipePath = ResolveLegacyOraclePath(
            descriptor.BuildRecipe,
            repositoryRoot);
        if (!File.Exists(sourcePath)
            || !File.Exists(buildRecipePath)
            || !StringComparer.Ordinal.Equals(
                ParityCanonicalJson.ComputeSha256(
                    File.ReadAllBytes(sourcePath)),
                descriptor.SourceSha256)
            || !StringComparer.Ordinal.Equals(
                ParityCanonicalJson.ComputeSha256(
                    File.ReadAllBytes(buildRecipePath)),
                descriptor.BuildRecipeSha256))
        {
            throw new InvalidDataException(
                $"Parity case '{parityId}' legacyOracle descriptor is stale.");
        }

        return descriptor;
    }

    internal static void ValidateLegacyOracleVersionBinding(
        LegacyOracleDescriptor descriptor,
        string parityId)
    {
        int marker = descriptor.VersionId.LastIndexOf(
            "-v",
            StringComparison.Ordinal);
        ReadOnlySpan<char> name = marker > 0
            ? descriptor.VersionId.AsSpan(0, marker)
            : [];
        ReadOnlySpan<char> version = marker > 0
            ? descriptor.VersionId.AsSpan(marker + 2)
            : [];
        bool validName = IsValidLegacyOracleVersionName(name);
        bool validVersion = IsValidLegacyOracleVersionNumber(version);
        if (!validName || !validVersion)
        {
            throw new InvalidDataException(
                $"Parity case '{parityId}' legacyOracle versionId is invalid.");
        }

        string expectedPrefix =
            $"tests/parity/legacy-oracle/v{version.ToString()}/";
        if (!IsCanonicalLegacyOracleIdentity(descriptor.Source)
            || !IsCanonicalLegacyOracleIdentity(
                descriptor.BuildRecipe)
            || !descriptor.Source.StartsWith(
                expectedPrefix,
                StringComparison.Ordinal)
            || !descriptor.BuildRecipe.StartsWith(
                expectedPrefix,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Parity case '{parityId}' legacyOracle paths do not match "
                + $"version '{descriptor.VersionId}'.");
        }
    }

    private static bool IsCanonicalLegacyOracleIdentity(
        string identity)
    {
        return !Path.IsPathFullyQualified(identity)
            && !identity.Contains('\\', StringComparison.Ordinal)
            && !identity
                .Split('/')
                .Any(segment => segment is "" or "." or "..");
    }

    private static bool IsLowercaseAsciiLetterOrDigit(char value) =>
        value is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static bool IsValidLegacyOracleVersionName(
        ReadOnlySpan<char> value)
    {
        if (value.Length == 0
            || !IsLowercaseAsciiLetterOrDigit(value[0]))
        {
            return false;
        }

        foreach (char character in value[1..])
        {
            if (!IsLowercaseAsciiLetterOrDigit(character)
                && character is not ('.' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidLegacyOracleVersionNumber(
        ReadOnlySpan<char> value)
    {
        if (value.Length == 0
            || value[0] is < '1' or > '9')
        {
            return false;
        }

        foreach (char character in value[1..])
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static string ResolveLegacyOraclePath(
        string relativePath,
        string repositoryRoot)
    {
        if (Path.IsPathFullyQualified(relativePath)
            || relativePath.Contains('\\', StringComparison.Ordinal)
            || relativePath
                .Split('/')
                .Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException(
                $"Legacy oracle path '{relativePath}' is unsafe.");
        }

        string oracleRoot = Path.GetFullPath(
            Path.Combine(
                repositoryRoot,
                "tests",
                "parity",
                "legacy-oracle"));
        string resolved = Path.GetFullPath(
            Path.Combine(repositoryRoot, relativePath));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!resolved.StartsWith(
                oracleRoot + Path.DirectorySeparatorChar,
                comparison))
        {
            throw new InvalidDataException(
                $"Legacy oracle path '{relativePath}' is outside its root.");
        }

        return resolved;
    }

    private static void ValidateExactProperties(
        JsonElement owner,
        IReadOnlyList<string> expectedPropertyNames,
        string description)
    {
        if (owner.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"{description} is not an object.");
        }

        string[] actualPropertyNames = owner
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!actualPropertyNames.SequenceEqual(
                expectedPropertyNames,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"{description} has unsupported fields.");
        }
    }

    private static string ComputeSha256(ReadOnlySpan<byte> contents)
    {
        return ParityCanonicalJson.ComputeSha256(contents);
    }

    private static bool IsLowercaseSha256(string value)
    {
        return ParityCanonicalJson.IsLowercaseSha256(value);
    }
}
