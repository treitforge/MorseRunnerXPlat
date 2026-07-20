using System.Text.Json;
using System.Text.Json.Serialization;

namespace MorseRunner.LegacyParity.Tests;

internal static class ParityRunRecorder
{
    public const string EnvironmentVariableName =
        "MORSE_RUNNER_PARITY_RESULTS";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        UnmappedMemberHandling =
            JsonUnmappedMemberHandling.Disallow,
    };

    public static async Task RecordAsync(
        ParityCertificationCase definition,
        ParityTargetKind target,
        ParityObservation observation,
        bool adapterCompleted,
        CancellationToken cancellationToken)
    {
        string acceptanceTestName =
            LegacyOracleParityTests.GetCurrentAcceptanceTestName();
        await RecordAsync(
            Environment.GetEnvironmentVariable(EnvironmentVariableName),
            definition,
            target,
            observation,
            adapterCompleted,
            cancellationToken,
            acceptanceTestName);
    }

    internal static async Task RecordAsync(
        string? configuredPath,
        ParityCertificationCase definition,
        ParityTargetKind target,
        ParityObservation observation,
        bool adapterCompleted,
        CancellationToken cancellationToken,
        string? acceptanceTestName = null)
    {
        await RecordForPlatformAsync(
            configuredPath,
            definition,
            target,
            observation,
            adapterCompleted,
            ParityRunEnvironment.Capture().Platform,
            cancellationToken,
            acceptanceTestName);
    }

    internal static async Task RecordForPlatformAsync(
        string? configuredPath,
        ParityCertificationCase definition,
        ParityTargetKind target,
        ParityObservation observation,
        bool adapterCompleted,
        string certificationPlatform,
        CancellationToken cancellationToken,
        string? acceptanceTestName = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            certificationPlatform);
        string expectedAcceptanceTestName =
            $"parity:{definition.Id}()";
        acceptanceTestName ??= expectedAcceptanceTestName;
        if (!StringComparer.Ordinal.Equals(
                acceptanceTestName,
                expectedAcceptanceTestName))
        {
            throw new InvalidOperationException(
                $"Parity case '{definition.Id}' executed under unexpected "
                + $"test identity '{acceptanceTestName}'.");
        }

        ParityAcceptanceRegistration registration =
            ParityAcceptanceRegistry.Get(definition.Id);
        registration.ValidateManifestBinding(definition);
        ParityAcceptanceRegistry.EnsureApplicable(
            definition,
            certificationPlatform);
        string[] selectedParityIds = ParityAcceptanceRegistry
            .ParseSelectedIds(
                Environment.GetEnvironmentVariable(
                    ParityAcceptanceRegistry
                        .SelectedCaseIdsEnvironmentVariable),
                certificationPlatform)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (!selectedParityIds.Contains(
                definition.Id,
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Parity case '{definition.Id}' is not selected for "
                + "this certification run.");
        }

        ParityCertificationCase currentDefinition =
            ParityCertificationCase.LoadForInspection(
                definition.Id);
        if (!definition.HasSameExecutionDefinition(
                currentDefinition))
        {
            throw new InvalidDataException(
                $"Parity case '{definition.Id}' changed before recording.");
        }

        if (!ValidateObservationTargetBinding(
                observation,
                target,
                definition))
        {
            throw new InvalidDataException(
                $"Parity case '{definition.Id}' target attestation "
                + "is missing or invalid.");
        }

        if (String.IsNullOrWhiteSpace(configuredPath))
        {
            return;
        }

        string resultPath = Path.GetFullPath(configuredPath);
        string? directory = Path.GetDirectoryName(resultPath);
        if (!String.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        ParityRunContext runContext =
            await ParityRunContextProvider.CaptureAsync(
                target,
                cancellationToken);
        if (!StringComparer.Ordinal.Equals(
                runContext.Platform,
                certificationPlatform))
        {
            throw new InvalidOperationException(
                "Parity certification platform does not match "
                + "the captured run context.");
        }

        await Gate.WaitAsync(cancellationToken);
        string? temporaryPath = null;
        try
        {
            Dictionary<string, ParityRunResult> results =
                await LoadResultsAsync(
                    resultPath,
                    target,
                    runContext,
                    currentDefinition,
                    selectedParityIds,
                    cancellationToken);
            int executionCount = results.TryGetValue(
                definition.Id,
                out ParityRunResult? prior)
                ? checked(prior.ExecutionCount + 1)
                : 1;
            string[] observedValues = [.. observation.Values];
            ParityResultClassification classification =
                Classify(
                    definition,
                    registration,
                    target,
                    observation,
                    adapterCompleted);
            results[definition.Id] = new ParityRunResult(
                definition.Id,
                acceptanceTestName,
                Format(target),
                registration.AdapterId(target),
                definition.CaseDefinitionSha256,
                definition.FixtureSha256,
                classification.Outcome,
                classification.FailureCode,
                observation.EvidenceSource,
                observedValues,
                observedValues.Length,
                ParityObservedValuesDigest.Compute(observedValues),
                classification.Outcome == "passed"
                    ? null
                    : FindFirstDivergence(
                        definition.Scenario.ExpectedValues,
                        observedValues),
                target == ParityTargetKind.Legacy
                    ? observation.LegacyOracle
                    : null,
                executionCount);

            ParityRunDocument document = new(
                SchemaVersion: 1,
                Target: Format(target),
                RunContext: runContext,
                ExpectedParityIds: selectedParityIds,
                Results: results.Values
                    .OrderBy(result => result.ParityId, StringComparer.Ordinal)
                    .ToArray());
            temporaryPath =
                $"{resultPath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, resultPath, overwrite: true);
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                File.Delete(temporaryPath);
            }

            Gate.Release();
        }
    }

    private static async Task<Dictionary<string, ParityRunResult>>
        LoadResultsAsync(
            string resultPath,
            ParityTargetKind target,
            ParityRunContext runContext,
            ParityCertificationCase currentDefinition,
            IReadOnlyList<string> expectedParityIds,
            CancellationToken cancellationToken)
    {
        if (!File.Exists(resultPath))
        {
            return new Dictionary<string, ParityRunResult>(
                StringComparer.Ordinal);
        }

        ParityRunDocument? document;
        try
        {
            byte[] resultBytes = await File.ReadAllBytesAsync(
                resultPath,
                cancellationToken);
            using (JsonDocument jsonDocument =
                   JsonDocument.Parse(resultBytes))
            {
                _ = ParityCanonicalJson.SerializeToUtf8Bytes(
                    jsonDocument.RootElement);
            }

            document =
                JsonSerializer.Deserialize<ParityRunDocument>(
                    resultBytes,
                    SerializerOptions);
        }
        catch (Exception exception)
            when (exception is JsonException
                or InvalidDataException)
        {
            throw new InvalidDataException(
                $"Parity result file '{resultPath}' is invalid.",
                exception);
        }

        if (document is null
            || document.SchemaVersion != 1
            || document.RunContext is null
            || document.ExpectedParityIds is null
            || document.Results is null)
        {
            throw new InvalidDataException(
                $"Parity result file '{resultPath}' is invalid.");
        }

        if (!StringComparer.Ordinal.Equals(
                document.Target,
                Format(target)))
        {
            throw new InvalidDataException(
                $"Parity result file '{resultPath}' contains target "
                + $"'{document.Target}', not '{target}'.");
        }

        if (document.RunContext != runContext)
        {
            throw new InvalidDataException(
                $"Parity result file '{resultPath}' changed run context.");
        }

        if (!document.ExpectedParityIds.SequenceEqual(
                expectedParityIds,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Parity result file '{resultPath}' has a stale expected ID set.");
        }

        if (document.Results.Any(
                result => !expectedParityIds.Contains(
                    result.ParityId,
                    StringComparer.Ordinal)))
        {
            throw new InvalidDataException(
                $"Parity result file '{resultPath}' contains a "
                + "non-applicable result.");
        }

        foreach (ParityRunResult result in document.Results)
        {
            try
            {
                ParityCertificationCase definition =
                    StringComparer.Ordinal.Equals(
                        result.ParityId,
                        currentDefinition.Id)
                        ? currentDefinition
                        : ParityCertificationCase.LoadForInspection(
                            result.ParityId);
                if (!ValidateResultIntegrity(
                        result,
                        target,
                        definition,
                        runContext.Platform))
                {
                    throw new InvalidDataException(
                        $"Parity result '{result.ParityId}' is invalid.");
                }
            }
            catch (Exception exception)
                when (exception is ArgumentException
                    or InvalidOperationException
                    or InvalidDataException
                    or IOException
                    or JsonException)
            {
                throw new InvalidDataException(
                    $"Parity result file '{resultPath}' contains "
                    + "an invalid result.",
                    exception);
            }
        }

        return document.Results.ToDictionary(
            result => result.ParityId,
            StringComparer.Ordinal);
    }

    private static string Format(ParityTargetKind target)
    {
        return target.ToString().ToLowerInvariant();
    }

    private static ParityResultClassification Classify(
        ParityCertificationCase definition,
        ParityAcceptanceRegistration registration,
        ParityTargetKind target,
        ParityObservation observation,
        bool adapterCompleted)
    {
        bool valuesMatch =
            observation.Values.SequenceEqual(
                definition.Scenario.ExpectedValues,
                StringComparer.Ordinal);
        if (adapterCompleted
            && observation.Outcome == ParityTargetOutcome.Passed
            && observation.FailureCode is null
            && valuesMatch)
        {
            return new ParityResultClassification("passed", null);
        }

        if (adapterCompleted
            && observation.Outcome == ParityTargetOutcome.Failed
            && !valuesMatch
            && registration.IsFunctionalDivergence(
                target,
                observation.FailureCode))
        {
            return new ParityResultClassification(
                "functional-divergence",
                observation.FailureCode);
        }

        string failureCode = observation.FailureCode
            ?? (adapterCompleted
                ? "parity-observation-invalid"
                : "parity-adapter-did-not-complete");
        if (registration.IsProductDivergenceCode(
                failureCode))
        {
            failureCode =
                "parity-functional-divergence-without-value-difference";
        }

        return new ParityResultClassification(
            "not-runnable",
            failureCode);
    }

    private static bool ValidateResultIntegrity(
        ParityRunResult result,
        ParityTargetKind target,
        ParityCertificationCase definition,
        string certificationPlatform)
    {
        ParityAcceptanceRegistration registration;
        try
        {
            registration =
                ParityAcceptanceRegistry.Get(result.ParityId);
            registration.ValidateManifestBinding(definition);
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                or InvalidDataException)
        {
            return false;
        }

        ParityFirstDivergence? expectedDivergence =
            StringComparer.Ordinal.Equals(result.Outcome, "passed")
                ? null
                : FindFirstDivergence(
                    definition.Scenario.ExpectedValues,
                    result.ObservedValues ?? []);
        bool outcomeIsValid = result.Outcome switch
        {
            "passed" =>
                result.FailureCode is null
                && result.ObservedValues is not null
                && result.ObservedValues.SequenceEqual(
                    definition.Scenario.ExpectedValues,
                    StringComparer.Ordinal),
            "functional-divergence" =>
                registration.IsFunctionalDivergence(
                    target,
                    result.FailureCode)
                && expectedDivergence is not null,
            "not-runnable" =>
                !String.IsNullOrWhiteSpace(result.FailureCode)
                && !registration.IsProductDivergenceCode(
                    result.FailureCode),
            _ => false,
        };

        return StringComparer.Ordinal.Equals(
                result.Target,
                Format(target))
            && StringComparer.Ordinal.Equals(
                result.AcceptanceTestName,
                $"parity:{result.ParityId}()")
            && definition.Platforms.Contains(
                certificationPlatform,
                StringComparer.Ordinal)
            && StringComparer.Ordinal.Equals(
                result.Adapter,
                registration.AdapterId(target))
            && StringComparer.Ordinal.Equals(
                result.CaseDefinitionSha256,
                definition.CaseDefinitionSha256)
            && StringComparer.Ordinal.Equals(
                result.FixtureSha256,
                definition.FixtureSha256)
            && IsLowercaseSha256(result.CaseDefinitionSha256)
            && IsLowercaseSha256(result.FixtureSha256)
            && outcomeIsValid
            && ValidateLegacyResultBinding(
                result,
                target,
                definition)
            && EvidenceSourceMatches(
                result.EvidenceSource,
                target,
                definition)
            && result.ExecutionCount > 0
            && result.ObservedValues is not null
            && result.ObservedValues.All(value => value is not null)
            && result.ObservedValueCount
                == result.ObservedValues.Count
            && IsLowercaseSha256(result.ObservedValuesSha256)
            && StringComparer.Ordinal.Equals(
                result.ObservedValuesSha256,
                ParityObservedValuesDigest.Compute(
                    result.ObservedValues))
            && result.FirstDivergence == expectedDivergence;
    }

    private static bool ValidateLegacyResultBinding(
        ParityRunResult result,
        ParityTargetKind target,
        ParityCertificationCase definition)
    {
        if (target == ParityTargetKind.XPlat)
        {
            return result.LegacyOracle is null;
        }

        LegacyOracleResultBinding? binding =
            result.LegacyOracle;
        return binding is not null
            && LegacyResultBindingMatches(
                binding,
                definition);
    }

    private static bool ValidateObservationTargetBinding(
        ParityObservation observation,
        ParityTargetKind target,
        ParityCertificationCase definition)
    {
        return target == ParityTargetKind.XPlat
            ? observation.LegacyOracle is null
                && !String.IsNullOrWhiteSpace(
                    observation.EvidenceSource)
            : observation.LegacyOracle is not null
                && LegacyResultBindingMatches(
                    observation.LegacyOracle,
                    definition)
                && EvidenceSourceMatches(
                    observation.EvidenceSource,
                    target,
                    definition);
    }

    private static bool EvidenceSourceMatches(
        string? evidenceSource,
        ParityTargetKind target,
        ParityCertificationCase definition)
    {
        if (target == ParityTargetKind.XPlat)
        {
            return !String.IsNullOrWhiteSpace(evidenceSource);
        }

        return definition.Scenario.LegacyOracle is
        { } descriptor
            && StringComparer.Ordinal.Equals(
                evidenceSource,
                descriptor.Source);
    }

    private static bool LegacyResultBindingMatches(
        LegacyOracleResultBinding binding,
        ParityCertificationCase definition)
    {
        LegacyOracleDescriptor? descriptor =
            definition.Scenario.LegacyOracle;
        return descriptor is not null
            && StringComparer.Ordinal.Equals(
                binding.AdapterId,
                descriptor.AdapterId)
            && StringComparer.Ordinal.Equals(
                binding.VersionId,
                descriptor.VersionId)
            && StringComparer.Ordinal.Equals(
                binding.Source,
                descriptor.Source)
            && StringComparer.Ordinal.Equals(
                binding.SourceSha256,
                descriptor.SourceSha256)
            && StringComparer.Ordinal.Equals(
                binding.BuildRecipe,
                descriptor.BuildRecipe)
            && StringComparer.Ordinal.Equals(
                binding.BuildRecipeSha256,
                descriptor.BuildRecipeSha256)
            && ParityCanonicalJson.IsLowercaseSha256(
                binding.RegistrySha256)
            && ParityCanonicalJson.IsLowercaseSha256(
                binding.ExecutableSha256)
            && IsSafeLegacyArtifactIdentity(
                binding.Provenance)
            && ParityCanonicalJson.IsLowercaseSha256(
                binding.ProvenanceSha256);
    }

    private static bool IsSafeLegacyArtifactIdentity(
        string identity)
    {
        return !String.IsNullOrWhiteSpace(identity)
            && !Path.IsPathFullyQualified(identity)
            && !identity.Contains('\\', StringComparison.Ordinal)
            && identity.StartsWith(
                "artifacts/legacy-oracle/",
                StringComparison.Ordinal)
            && !identity
                .Split('/')
                .Any(segment => segment is "" or "." or "..");
    }

    private static ParityFirstDivergence? FindFirstDivergence(
        IReadOnlyList<string> expected,
        IReadOnlyList<string> actual)
    {
        int count = Math.Max(expected.Count, actual.Count);
        for (int index = 0; index < count; index++)
        {
            string? expectedValue =
                index < expected.Count ? expected[index] : null;
            string? actualValue =
                index < actual.Count ? actual[index] : null;
            if (!StringComparer.Ordinal.Equals(
                    expectedValue,
                    actualValue))
            {
                return new ParityFirstDivergence(
                    index,
                    expectedValue,
                    actualValue);
            }
        }

        return null;
    }

    private static bool IsLowercaseSha256(string? value)
    {
        return value is { Length: 64 }
            && value.All(
                character =>
                    character is >= '0' and <= '9'
                    or >= 'a' and <= 'f');
    }

    private sealed record ParityRunDocument(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("target")] string Target,
        [property: JsonPropertyName("runContext")]
        ParityRunContext? RunContext,
        [property: JsonPropertyName("expectedParityIds")]
        IReadOnlyList<string>? ExpectedParityIds,
        [property: JsonPropertyName("results")]
        IReadOnlyList<ParityRunResult>? Results);

    private sealed record ParityRunResult(
        [property: JsonPropertyName("parityId")] string ParityId,
        [property: JsonPropertyName("acceptanceTestName")]
        string AcceptanceTestName,
        [property: JsonPropertyName("target")] string Target,
        [property: JsonPropertyName("adapter")] string Adapter,
        [property: JsonPropertyName("caseDefinitionSha256")]
        string CaseDefinitionSha256,
        [property: JsonPropertyName("fixtureSha256")]
        string FixtureSha256,
        [property: JsonPropertyName("outcome")] string Outcome,
        [property: JsonPropertyName("failureCode")] string? FailureCode,
        [property: JsonPropertyName("evidenceSource")] string EvidenceSource,
        [property: JsonPropertyName("observedValues")]
        IReadOnlyList<string>? ObservedValues,
        [property: JsonPropertyName("observedValueCount")]
        int ObservedValueCount,
        [property: JsonPropertyName("observedValuesSha256")]
        string ObservedValuesSha256,
        [property: JsonPropertyName("firstDivergence")]
        ParityFirstDivergence? FirstDivergence,
        [property: JsonPropertyName("legacyOracle")]
        LegacyOracleResultBinding? LegacyOracle,
        [property: JsonPropertyName("executionCount")] int ExecutionCount);

    private sealed record ParityFirstDivergence(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("expected")] string? Expected,
        [property: JsonPropertyName("actual")] string? Actual);

    private sealed record ParityResultClassification(
        string Outcome,
        string? FailureCode);
}
