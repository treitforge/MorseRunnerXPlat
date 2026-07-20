using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

internal sealed record LegacyAudioV3CandidateObservation(
    string Scenario,
    IReadOnlyList<string> Values,
    string ValuesSha256,
    string EmittedJsonLineSha256);

internal static class LegacyAudioV3CandidateTarget
{
    private const string AdapterId = "LegacyOracleTarget";
    private const string CandidateDirectory =
        "tests/parity/legacy-oracle/v3";
    private const string ExpectedDxccListSha256 =
        "94ad79465eb8cd8df91861f5cd8d67064f8c3ba39a9774a737eb5f53d0b51049";
    private const string ExpectedLegacyBundle =
        "tests/parity/legacy-reference.bundle";
    private const string ExpectedLegacyBundleSha256 =
        "1d9fcafb3adb0227aba360bc1884b5c32d2c1e8210448e646a4104f142b07772";
    private const string ExpectedLegacyReferenceDefinition =
        "tests/parity/legacy-reference.json";
    private const string ExpectedLegacyReferenceDefinitionSha256 =
        "663adf3bf230161abb923cf8b6651d394af1b99eab05efeafb696cb29992da23";
    private const string ExpectedLegacyRevision =
        "55bbd019c29d8cf693184ea420a17a253f16fe1e";
    private const string ExpectedLegacyTree =
        "a44212bfee5b1eebfd0129459d476736775adf36";
    private const string VersionId = "legacy-oracle-v3";

    public static string? FindLocalExecutable()
    {
        string? configured = Environment.GetEnvironmentVariable(
            "MORSE_RUNNER_V3_CANDIDATE_EXE");
        if (!String.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        string defaultPath = Path.Combine(
            RepositoryPaths.Root,
            "artifacts",
            "legacy-oracle-v3-candidate",
            "release-nogui-a",
            "LegacyOracleV3.exe");
        return File.Exists(defaultPath)
            ? defaultPath
            : null;
    }

    public static async Task<LegacyAudioV3CandidateObservation> ExecuteAsync(
        string executablePath,
        string legacyRoot,
        string scenario,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenario);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "The v3 candidate recipe currently targets win64.");
        }

        CandidateBinding binding = LoadAndValidateBinding(scenario);
        string fullExecutablePath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullExecutablePath))
        {
            throw new FileNotFoundException(
                "The v3 candidate executable was not found.",
                fullExecutablePath);
        }

        string executableSha256 = FileSha256(fullExecutablePath);
        if (!binding.AllowedExecutableSha256.Contains(
                executableSha256,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "The v3 candidate executable is not bound by the "
                + "build recipe.");
        }

        string fullLegacyRoot = Path.GetFullPath(legacyRoot);
        if (!Directory.Exists(fullLegacyRoot))
        {
            throw new DirectoryNotFoundException(
                $"The legacy root does not exist: {fullLegacyRoot}");
        }

        fullLegacyRoot = Path.TrimEndingDirectorySeparator(
                fullLegacyRoot)
            + Path.DirectorySeparatorChar;
        ValidateLegacyMaterialization(
            binding,
            fullLegacyRoot);
        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"morse-runner-v3-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            string contentAddressedInput = Path.Combine(
                temporaryDirectory,
                $"{binding.InputSha256}.json");
            File.Copy(
                binding.InputPath,
                contentAddressedInput,
                overwrite: false);

            var startInfo = new ProcessStartInfo
            {
                FileName = fullExecutablePath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add(fullLegacyRoot);
            startInfo.ArgumentList.Add(scenario);
            startInfo.ArgumentList.Add(AdapterId);
            startInfo.ArgumentList.Add(VersionId);
            startInfo.ArgumentList.Add(binding.Source);
            startInfo.ArgumentList.Add(binding.SourceSha256);
            startInfo.ArgumentList.Add(binding.BuildRecipe);
            startInfo.ArgumentList.Add(binding.BuildRecipeSha256);
            startInfo.ArgumentList.Add(binding.CaseContractsSha256);
            startInfo.ArgumentList.Add(binding.InputSha256);
            startInfo.ArgumentList.Add(contentAddressedInput);
            startInfo.ArgumentList.Add(binding.SourcePath);
            startInfo.ArgumentList.Add(binding.BuildRecipePath);
            startInfo.ArgumentList.Add(binding.CaseContractsPath);
            startInfo.ArgumentList.Add(binding.DescriptorPath);

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "The v3 candidate process did not start.");
            Task<string> standardOutput =
                process.StandardOutput.ReadToEndAsync(
                    cancellationToken);
            Task<string> standardError =
                process.StandardError.ReadToEndAsync(
                    cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                throw;
            }

            string output = await standardOutput;
            string error = await standardError;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"The v3 candidate exited with "
                    + $"{process.ExitCode}: {error.Trim()}");
            }

            if (!String.IsNullOrWhiteSpace(error))
            {
                throw new InvalidDataException(
                    $"The successful v3 candidate wrote stderr: "
                    + error.Trim());
            }

            return ParseAndValidate(
                output,
                binding);
        }
        finally
        {
            Directory.Delete(
                temporaryDirectory,
                recursive: true);
        }
    }

    internal static LegacyAudioV3CandidateObservation ParseAndValidate(
        string output,
        CandidateBinding binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        ArgumentNullException.ThrowIfNull(binding);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;
        RequireExactProperties(
            root,
            "scenario",
            "adapterId",
            "versionId",
            "source",
            "sourceSha256",
            "buildRecipe",
            "buildRecipeSha256",
            "caseDefinitionSha256",
            "inputSha256",
            "values");
        RequireEqual(root, "scenario", binding.Scenario);
        RequireEqual(root, "adapterId", AdapterId);
        RequireEqual(root, "versionId", VersionId);
        RequireEqual(root, "source", binding.Source);
        RequireEqual(
            root,
            "sourceSha256",
            binding.SourceSha256);
        RequireEqual(
            root,
            "buildRecipe",
            binding.BuildRecipe);
        RequireEqual(
            root,
            "buildRecipeSha256",
            binding.BuildRecipeSha256);
        RequireEqual(
            root,
            "caseDefinitionSha256",
            binding.CaseContractsSha256);
        RequireEqual(
            root,
            "inputSha256",
            binding.InputSha256);

        JsonElement valuesElement = root.GetProperty("values");
        if (valuesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "The v3 candidate values property is not an array.");
        }

        string[] values = valuesElement
            .EnumerateArray()
            .Select(
                value =>
                    value.ValueKind == JsonValueKind.String
                        ? value.GetString()!
                        : throw new InvalidDataException(
                            "A v3 candidate value is not a string."))
            .ToArray();
        if (values.Length == 0)
        {
            throw new InvalidDataException(
                "The v3 candidate returned no observed values.");
        }

        string emittedLine = output.TrimEnd('\r', '\n');
        return new(
            binding.Scenario,
            values,
            ParityObservedValuesDigest.Compute(values),
            Sha256(Encoding.UTF8.GetBytes(emittedLine)));
    }

    internal static CandidateBinding LoadAndValidateBinding(
        string scenario)
    {
        string descriptorPath = CandidateFile(
            "adapter-descriptor.json");
        string contractsPath = CandidateFile(
            "case-contracts.json");
        string capturePath = CandidateFile(
            "captured-checkpoints.json");
        string requirementsPath = CandidateFile(
            "integration-requirements.json");
        using JsonDocument descriptor =
            JsonDocument.Parse(File.ReadAllBytes(descriptorPath));
        using JsonDocument contracts =
            JsonDocument.Parse(File.ReadAllBytes(contractsPath));
        using JsonDocument capture =
            JsonDocument.Parse(File.ReadAllBytes(capturePath));
        using JsonDocument requirements =
            JsonDocument.Parse(File.ReadAllBytes(requirementsPath));
        JsonElement descriptorRoot = descriptor.RootElement;
        RequireExactProperties(
            descriptorRoot,
            "adapterId",
            "versionId",
            "source",
            "sourceSha256",
            "buildRecipe",
            "buildRecipeSha256",
            "caseDefinition",
            "caseDefinitionSha256",
            "legacyReferenceDefinition",
            "legacyReferenceDefinitionSha256",
            "legacyBundle",
            "legacyBundleSha256",
            "legacyRevision",
            "legacyTree");
        RequireEqual(descriptorRoot, "adapterId", AdapterId);
        RequireEqual(descriptorRoot, "versionId", VersionId);
        string source = RequireString(descriptorRoot, "source");
        string sourceSha256 =
            RequireSha256(descriptorRoot, "sourceSha256");
        string buildRecipe =
            RequireString(descriptorRoot, "buildRecipe");
        string buildRecipeSha256 =
            RequireSha256(descriptorRoot, "buildRecipeSha256");
        string caseDefinition = RequireString(
            descriptorRoot,
            "caseDefinition");
        string caseContractsSha256 = RequireSha256(
            descriptorRoot,
            "caseDefinitionSha256");
        string legacyReferenceDefinition = RequireString(
            descriptorRoot,
            "legacyReferenceDefinition");
        string legacyReferenceDefinitionSha256 = RequireSha256(
            descriptorRoot,
            "legacyReferenceDefinitionSha256");
        string legacyBundle = RequireString(
            descriptorRoot,
            "legacyBundle");
        string legacyBundleSha256 = RequireSha256(
            descriptorRoot,
            "legacyBundleSha256");
        string legacyRevision = RequireString(
            descriptorRoot,
            "legacyRevision");
        string legacyTree = RequireString(
            descriptorRoot,
            "legacyTree");
        if (caseDefinition
                != $"{CandidateDirectory}/case-contracts.json"
            || legacyReferenceDefinition
                != ExpectedLegacyReferenceDefinition
            || legacyReferenceDefinitionSha256
                != ExpectedLegacyReferenceDefinitionSha256
            || legacyBundle != ExpectedLegacyBundle
            || legacyBundleSha256 != ExpectedLegacyBundleSha256
            || legacyRevision != ExpectedLegacyRevision
            || legacyTree != ExpectedLegacyTree)
        {
            throw new InvalidDataException(
                "The v3 candidate pinned legacy identity differs.");
        }

        string sourcePath = RepositoryFile(source);
        if (FileSha256(sourcePath) != sourceSha256)
        {
            throw new InvalidDataException(
                "The v3 candidate source hash does not match.");
        }

        string recipePath = RepositoryFile(buildRecipe);
        if (FileSha256(recipePath) != buildRecipeSha256)
        {
            throw new InvalidDataException(
                "The v3 candidate build recipe hash does not match.");
        }

        if (!String.Equals(
                RepositoryFile(caseDefinition),
                contractsPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The v3 candidate case definition identity does "
                + "not resolve to the canonical artifact.");
        }

        if (FileSha256(contractsPath) != caseContractsSha256)
        {
            throw new InvalidDataException(
                "The v3 candidate case definition hash does not "
                + "match.");
        }

        string legacyReferenceDefinitionPath = RepositoryFile(
            legacyReferenceDefinition);
        string legacyBundlePath = RepositoryFile(legacyBundle);
        using JsonDocument recipe =
            JsonDocument.Parse(File.ReadAllBytes(recipePath));
        JsonElement sourceClosure = recipe.RootElement.GetProperty(
            "sourceClosure");
        RequireEqual(
            sourceClosure,
            "oracleSource",
            source);
        RequireEqual(
            sourceClosure,
            "oracleSourceSha256",
            sourceSha256);
        RequireEqual(
            sourceClosure,
            "legacyRevision",
            legacyRevision);
        RequireEqual(
            sourceClosure,
            "legacyTree",
            legacyTree);
        RequireEqual(
            sourceClosure,
            "legacyBundleSha256",
            legacyBundleSha256);
        JsonElement recipeReference = sourceClosure.GetProperty(
            "legacyReferenceDefinition");
        RequireExactProperties(
            recipeReference,
            "path",
            "sha256");
        RequireEqual(
            recipeReference,
            "path",
            legacyReferenceDefinition);
        RequireEqual(
            recipeReference,
            "sha256",
            legacyReferenceDefinitionSha256);
        LegacyMaterializationFile[] legacyMaterializationFiles =
            ReadLegacyMaterializationFiles(sourceClosure);
        if (legacyMaterializationFiles.Length != 52
            || legacyMaterializationFiles
                .Select(file => file.Identity)
                .Distinct(StringComparer.Ordinal)
                .Count() != legacyMaterializationFiles.Length)
        {
            throw new InvalidDataException(
                "The v3 materialized legacy closure is incomplete "
                + "or duplicated.");
        }

        LegacyMaterializationFile dxcc = legacyMaterializationFiles
            .Single(
                file => file.Identity == "DXCC.LIST");
        if (dxcc.Sha256 != ExpectedDxccListSha256)
        {
            throw new InvalidDataException(
                "The v3 DXCC.LIST binding differs.");
        }

        JsonElement contractsRoot = contracts.RootElement;
        if (RequireString(
                contractsRoot,
                "certificationStatus")
            != "unactivated-noncertifying-ce-runtime-candidate"
            || contractsRoot.GetProperty("manifestActivation")
                .GetBoolean()
            || contractsRoot.GetProperty(
                    "externalDescriptorBuildSupported")
                .GetBoolean())
        {
            throw new InvalidDataException(
                "The v3 candidate is not marked unactivated.");
        }

        JsonElement requirementsRoot = requirements.RootElement;
        if (requirementsRoot.GetProperty("manifestActivation")
                .GetBoolean()
            || requirementsRoot.GetProperty("registryActivation")
                .GetBoolean()
            || requirementsRoot.GetProperty(
                    "externalDescriptorBuildSupported")
                .GetBoolean())
        {
            throw new InvalidDataException(
                "The v3 integration requirements permit activation.");
        }

        JsonElement captureBinding = capture.RootElement
            .GetProperty("binding");
        RequireEqual(
            captureBinding,
            "sourceSha256",
            sourceSha256);
        RequireEqual(
            captureBinding,
            "buildRecipeSha256",
            buildRecipeSha256);
        RequireEqual(
            captureBinding,
            "adapterDescriptorSha256",
            FileSha256(descriptorPath));
        RequireEqual(
            captureBinding,
            "caseContractsSha256",
            caseContractsSha256);
        RequireEqual(
            captureBinding,
            "integrationRequirementsSha256",
            FileSha256(requirementsPath));
        RequireEqual(
            captureBinding,
            "legacyReferenceDefinitionSha256",
            legacyReferenceDefinitionSha256);
        RequireEqual(
            captureBinding,
            "legacyBundleSha256",
            legacyBundleSha256);
        RequireEqual(
            captureBinding,
            "legacyRevision",
            legacyRevision);
        RequireEqual(
            captureBinding,
            "legacyTree",
            legacyTree);

        JsonElement caseContract = contractsRoot
            .GetProperty("cases")
            .EnumerateArray()
            .Single(
                candidate =>
                    RequireString(candidate, "id") == scenario);
        JsonElement capturedCase = capture.RootElement
            .GetProperty("cases")
            .EnumerateArray()
            .Single(
                candidate =>
                    RequireString(candidate, "id") == scenario);
        string inputIdentity = RequireString(
            caseContract,
            "input");
        string inputPath = RepositoryFile(inputIdentity);
        string inputSha256 = RequireSha256(
            caseContract,
            "inputSha256");
        if (FileSha256(inputPath) != inputSha256)
        {
            throw new InvalidDataException(
                "The v3 candidate input hash does not match.");
        }

        RequireEqual(
            capturedCase,
            "inputSha256",
            inputSha256);
        JsonElement reproducibility = recipe.RootElement
            .GetProperty("reproducibility");
        string primaryExecutableSha256 = RequireSha256(
            reproducibility,
            "primaryExecutableSha256");
        string independentPrimaryRebuildSha256 = RequireSha256(
            reproducibility,
            "independentPrimaryRebuildSha256");
        string win32ExecutableSha256 = RequireSha256(
            reproducibility,
            "win32ExecutableSha256");
        string independentWin32RebuildSha256 = RequireSha256(
            reproducibility,
            "independentWin32RebuildSha256");
        if (primaryExecutableSha256
                != independentPrimaryRebuildSha256
            || win32ExecutableSha256
                != independentWin32RebuildSha256)
        {
            throw new InvalidDataException(
                "The v3 candidate executable rebuild hashes differ.");
        }

        JsonElement capturedBuilds = capture.RootElement.GetProperty(
            "builds");
        RequireEqual(
            capturedBuilds,
            "primaryNoguiSha256",
            primaryExecutableSha256);
        RequireEqual(
            capturedBuilds,
            "independentNoguiRebuildSha256",
            independentPrimaryRebuildSha256);
        RequireEqual(
            capturedBuilds,
            "win32CrossCheckSha256",
            win32ExecutableSha256);
        RequireEqual(
            capturedBuilds,
            "independentWin32RebuildSha256",
            independentWin32RebuildSha256);
        if (!capturedBuilds
                .GetProperty("primaryBuildsByteIdentical")
                .GetBoolean()
            || !capturedBuilds
                .GetProperty("win32BuildsByteIdentical")
                .GetBoolean())
        {
            throw new InvalidDataException(
                "The v3 candidate rebuild identity flag differs.");
        }

        string[] allowedExecutableSha256 =
        [
            primaryExecutableSha256,
            win32ExecutableSha256,
        ];

        var binding = new CandidateBinding(
            scenario,
            source,
            sourcePath,
            sourceSha256,
            buildRecipe,
            recipePath,
            buildRecipeSha256,
            contractsPath,
            caseContractsSha256,
            descriptorPath,
            legacyReferenceDefinition,
            legacyReferenceDefinitionPath,
            legacyReferenceDefinitionSha256,
            legacyBundle,
            legacyBundlePath,
            legacyBundleSha256,
            legacyRevision,
            legacyTree,
            legacyMaterializationFiles,
            inputPath,
            inputSha256,
            allowedExecutableSha256,
            capturedCase.GetProperty("valueCount").GetInt32(),
            RequireSha256(capturedCase, "valuesSha256"),
            RequireSha256(
                capturedCase,
                "emittedJsonLineSha256"));
        ValidatePinnedLegacyReferenceArtifacts(
            binding,
            legacyReferenceDefinitionPath,
            legacyBundlePath);
        return binding;
    }

    internal static void ValidatePinnedLegacyReferenceArtifacts(
        CandidateBinding binding,
        string definitionPath,
        string bundlePath)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);
        if (FileSha256(definitionPath)
            != binding.LegacyReferenceDefinitionSha256)
        {
            throw new InvalidDataException(
                "The pinned legacy reference definition hash "
                + "differs.");
        }

        if (FileSha256(bundlePath) != binding.LegacyBundleSha256)
        {
            throw new InvalidDataException(
                "The pinned legacy bundle hash differs.");
        }

        using JsonDocument definition = JsonDocument.Parse(
            File.ReadAllBytes(definitionPath));
        JsonElement root = definition.RootElement;
        RequireExactProperties(
            root,
            "schemaVersion",
            "repository",
            "publicBaseRevision",
            "revision",
            "tree",
            "bundle",
            "bundleSha256",
            "toolchain");
        if (root.GetProperty("schemaVersion").ValueKind
                != JsonValueKind.Number
            || root.GetProperty("schemaVersion").GetInt32() != 1)
        {
            throw new InvalidDataException(
                "The pinned legacy reference schema differs.");
        }

        RequireEqual(
            root,
            "revision",
            binding.LegacyRevision);
        RequireEqual(
            root,
            "tree",
            binding.LegacyTree);
        RequireEqual(
            root,
            "bundle",
            binding.LegacyBundle);
        RequireEqual(
            root,
            "bundleSha256",
            binding.LegacyBundleSha256);
    }

    internal static void ValidateLegacyMaterialization(
        CandidateBinding binding,
        string legacyRoot)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyRoot);
        string fullRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(legacyRoot));
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException(
                $"The legacy root does not exist: {fullRoot}");
        }

        foreach (LegacyMaterializationFile file
                 in binding.LegacyMaterializationFiles)
        {
            string path = ResolveLegacyFile(
                fullRoot,
                file.Identity);
            if (FileSha256(path) != file.Sha256)
            {
                throw new InvalidDataException(
                    $"The materialized legacy file "
                    + $"'{file.Identity}' hash differs.");
            }
        }
    }

    private static LegacyMaterializationFile[]
        ReadLegacyMaterializationFiles(JsonElement sourceClosure)
    {
        List<LegacyMaterializationFile> result =
        [
            .. ReadLegacyMaterializationFiles(
                sourceClosure,
                "executedLegacyUnits",
                25),
            .. ReadLegacyMaterializationFiles(
                sourceClosure,
                "compiledReferenceOnlyLegacyUnits",
                24),
            .. ReadLegacyMaterializationFiles(
                sourceClosure,
                "compiledReferenceOnlyLegacyResources",
                2),
            .. ReadLegacyMaterializationFiles(
                sourceClosure,
                "runtimeData",
                1),
        ];
        return [.. result];
    }

    private static IEnumerable<LegacyMaterializationFile>
        ReadLegacyMaterializationFiles(
            JsonElement sourceClosure,
            string propertyName,
            int expectedCount)
    {
        JsonElement value = sourceClosure.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() != expectedCount)
        {
            throw new InvalidDataException(
                $"The v3 {propertyName} closure shape differs.");
        }

        foreach (JsonElement item in value.EnumerateArray())
        {
            RequireExactProperties(item, "path", "sha256");
            string identity = RequireString(item, "path");
            if (!IsSafeRelativeIdentity(identity))
            {
                throw new InvalidDataException(
                    $"The v3 legacy file identity "
                    + $"'{identity}' is unsafe.");
            }

            yield return new(
                identity,
                RequireSha256(item, "sha256"));
        }
    }

    private static string ResolveLegacyFile(
        string fullRoot,
        string identity)
    {
        if (!IsSafeRelativeIdentity(identity))
        {
            throw new InvalidDataException(
                "A materialized legacy file identity is unsafe.");
        }

        string path = Path.GetFullPath(
            Path.Combine(
                fullRoot,
                identity.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));
        if (!path.StartsWith(
                fullRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
            || !File.Exists(path))
        {
            throw new InvalidDataException(
                $"The materialized legacy file "
                + $"'{identity}' is missing or outside its root.");
        }

        return path;
    }

    private static bool IsSafeRelativeIdentity(string identity)
    {
        return !String.IsNullOrWhiteSpace(identity)
            && !Path.IsPathRooted(identity)
            && !identity.Contains('\\')
            && identity
                .Split(
                    '/',
                    StringSplitOptions.RemoveEmptyEntries)
                .All(
                    segment =>
                        segment is not "." and not ".."
                        && !segment.Contains(':'));
    }

    private static string CandidateFile(string fileName)
    {
        return RepositoryFile(
            $"{CandidateDirectory}/{fileName}");
    }

    private static string RepositoryFile(string identity)
    {
        if (Path.IsPathRooted(identity))
        {
            throw new InvalidDataException(
                "A v3 candidate identity is rooted.");
        }

        string repositoryRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(RepositoryPaths.Root));
        string path = Path.GetFullPath(
            Path.Combine(
                repositoryRoot,
                identity.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));
        if (!path.StartsWith(
                repositoryRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
            || !File.Exists(path))
        {
            throw new InvalidDataException(
                "A v3 candidate artifact identity is invalid.");
        }

        return path;
    }

    private static void RequireExactProperties(
        JsonElement element,
        params string[] expectedNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "A v3 candidate JSON value is not an object.");
        }

        string[] actualNames = element
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        if (actualNames.Length != expectedNames.Length
            || actualNames.Distinct(StringComparer.Ordinal).Count()
                != actualNames.Length
            || !actualNames
                .OrderBy(name => name, StringComparer.Ordinal)
                .SequenceEqual(
                    expectedNames.OrderBy(
                        name => name,
                        StringComparer.Ordinal),
                    StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "A v3 candidate JSON object has unexpected fields.");
        }
    }

    private static void RequireEqual(
        JsonElement element,
        string propertyName,
        string expected)
    {
        string actual = RequireString(element, propertyName);
        if (actual != expected)
        {
            throw new InvalidDataException(
                $"The v3 candidate {propertyName} binding differs.");
        }
    }

    private static string RequireString(
        JsonElement element,
        string propertyName)
    {
        JsonElement property = element.GetProperty(propertyName);
        if (property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                $"The v3 candidate {propertyName} is not a string.");
        }

        return property.GetString()
            ?? throw new InvalidDataException(
                $"The v3 candidate {propertyName} is null.");
    }

    private static string RequireSha256(
        JsonElement element,
        string propertyName)
    {
        string value = RequireString(element, propertyName);
        if (!ParityCanonicalJson.IsLowercaseSha256(value))
        {
            throw new InvalidDataException(
                $"The v3 candidate {propertyName} is not a "
                + "lowercase SHA-256.");
        }

        return value;
    }

    private static string FileSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Sha256(stream);
    }

    private static string Sha256(Stream stream)
    {
        return Convert.ToHexStringLower(
            SHA256.HashData(stream));
    }

    private static string Sha256(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexStringLower(
            SHA256.HashData(bytes));
    }

    internal sealed record CandidateBinding(
        string Scenario,
        string Source,
        string SourcePath,
        string SourceSha256,
        string BuildRecipe,
        string BuildRecipePath,
        string BuildRecipeSha256,
        string CaseContractsPath,
        string CaseContractsSha256,
        string DescriptorPath,
        string LegacyReferenceDefinition,
        string LegacyReferenceDefinitionPath,
        string LegacyReferenceDefinitionSha256,
        string LegacyBundle,
        string LegacyBundlePath,
        string LegacyBundleSha256,
        string LegacyRevision,
        string LegacyTree,
        IReadOnlyList<LegacyMaterializationFile>
            LegacyMaterializationFiles,
        string InputPath,
        string InputSha256,
        IReadOnlyList<string> AllowedExecutableSha256,
        int ExpectedValueCount,
        string ExpectedValuesSha256,
        string ExpectedEmittedJsonLineSha256);

    internal sealed record LegacyMaterializationFile(
        string Identity,
        string Sha256);
}
