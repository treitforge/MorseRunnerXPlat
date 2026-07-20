using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

public static class ParityAcceptanceRegistry
{
    public const string SelectedCaseIdsEnvironmentVariable =
        "MORSE_RUNNER_PARITY_CASE_IDS";

    private static readonly IReadOnlyDictionary<
        string,
        ParityAcceptanceRegistration> Registrations =
        new Dictionary<string, ParityAcceptanceRegistration>(
            StringComparer.Ordinal)
        {
            [XPlatRandomPrimitivesTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatRandomPrimitivesTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatRandomPrimitivesTarget),
                    XPlatRandomPrimitivesTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = RandomPrimitivesInput.Parse(scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatRandomPrimitivesTarget()),
            [XPlatFlutterNoStationNoiseInvarianceTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatFlutterNoStationNoiseInvarianceTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatFlutterNoStationNoiseInvarianceTarget),
                    XPlatFlutterNoStationNoiseInvarianceTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = FlutterNoStationNoiseInvarianceInput.Parse(
                            scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatFlutterNoStationNoiseInvarianceTarget()),
            [XPlatQrmNoTriggerInvarianceTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatQrmNoTriggerInvarianceTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatQrmNoTriggerInvarianceTarget),
                    XPlatQrmNoTriggerInvarianceTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = QrmNoTriggerInvarianceInput.Parse(scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatQrmNoTriggerInvarianceTarget()),
            [XPlatQsbNoStationNoiseInvarianceTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatQsbNoStationNoiseInvarianceTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatQsbNoStationNoiseInvarianceTarget),
                    XPlatQsbNoStationNoiseInvarianceTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = QsbNoStationNoiseInvarianceInput.Parse(
                            scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatQsbNoStationNoiseInvarianceTarget()),
            [XPlatRealisticHissNoiseFloorTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatRealisticHissNoiseFloorTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatRealisticHissNoiseFloorTarget),
                    XPlatRealisticHissNoiseFloorTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = RealisticHissNoiseFloorInput.Parse(scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatRealisticHissNoiseFloorTarget()),
            [XPlatSstFarnsworthTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatSstFarnsworthTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatSstFarnsworthTarget),
                    XPlatSstFarnsworthTarget.FunctionalDivergenceCode,
                    static scenario =>
                        _ = SstFarnsworthTimingInput.Parse(scenario),
                    static () => new LegacyOracleTarget(),
                    static () => new XPlatSstFarnsworthTarget()),
            [XPlatStartupWarmupFilterTimingTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatStartupWarmupFilterTimingTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatStartupWarmupFilterTimingTarget),
                    XPlatStartupWarmupFilterTimingTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = StartupWarmupFilterTimingInput.Parse(
                            scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatStartupWarmupFilterTimingTarget()),
            ["contest.exchange-shapes"] =
                new ParityAcceptanceRegistration(
                    "contest.exchange-shapes",
                    "LegacyOracleTarget",
                    "XPlatContestRulesTarget",
                    "contest-exchange-shape-mismatch",
                    static scenario =>
                        _ = ContestExchangeShapesInput.Parse(scenario),
                    static () => new LegacyOracleTarget(),
                    static () => new XPlatContestRulesTarget()),
            [XPlatEnterEsmTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatEnterEsmTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatEnterEsmTarget),
                    XPlatEnterEsmTarget.FunctionalDivergenceCode,
                    static scenario =>
                        _ = EnterEsmScenarioInput.Parse(scenario),
                    static () => new LegacyOracleTarget(),
                    static () => new XPlatEnterEsmTarget()),
        };

    public static IReadOnlyList<string> AllIds { get; } =
        Registrations.Keys
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> ActiveIds =>
        ApplicableIdsFor(
            ParityRunEnvironment.Capture().Platform);

    internal static IReadOnlyList<string> SelectedIdsForCurrentRun()
    {
        return ParseSelectedIds(
            Environment.GetEnvironmentVariable(
                SelectedCaseIdsEnvironmentVariable),
            ParityRunEnvironment.Capture().Platform);
    }

    internal static IReadOnlyList<string> ParseSelectedIds(
        string? configuredIds,
        string platform)
    {
        IReadOnlyList<string> applicableIds =
            ApplicableIdsFor(platform);
        if (configuredIds is null)
        {
            return applicableIds;
        }

        try
        {
            using JsonDocument document =
                JsonDocument.Parse(configuredIds);
            if (document.RootElement.ValueKind
                    != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "Parity case selection must be a JSON array.");
            }

            string[] selectedIds = document.RootElement
                .EnumerateArray()
                .Select(
                    element =>
                        element.ValueKind == JsonValueKind.String
                            ? element.GetString()
                            : null)
                .Select(
                    id => !String.IsNullOrWhiteSpace(id)
                        ? id
                        : throw new InvalidDataException(
                            "Parity case selection contains an "
                            + "invalid ID."))
                .ToArray()!;
            if (selectedIds.Length == 0
                || selectedIds
                    .Distinct(StringComparer.Ordinal)
                    .Count() != selectedIds.Length
                || selectedIds.Any(
                    id => !applicableIds.Contains(
                        id,
                        StringComparer.Ordinal)))
            {
                throw new InvalidDataException(
                    "Parity case selection must contain unique, active, "
                    + $"applicable IDs for platform '{platform}'.");
            }

            return applicableIds
                .Where(
                    id => selectedIds.Contains(
                        id,
                        StringComparer.Ordinal))
                .ToArray();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Parity case selection is not valid JSON.",
                exception);
        }
    }

    internal static IReadOnlyList<string> ApplicableIdsFor(
        string platform)
    {
        return
        [
            .. AllIds.Where(
                id => ParityCertificationCase
                    .LoadForInspection(id)
                    .Platforms
                    .Contains(platform, StringComparer.Ordinal)),
        ];
    }

    internal static void EnsureApplicable(
        ParityCertificationCase definition,
        string platform)
    {
        if (!definition.Platforms.Contains(
                platform,
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Parity case '{definition.Id}' is not applicable to "
                + $"platform '{platform}'.");
        }
    }

    internal static ParityAcceptanceRegistration Get(string parityId)
    {
        return Registrations.TryGetValue(
            parityId,
            out ParityAcceptanceRegistration? registration)
            ? registration
            : throw new InvalidOperationException(
                $"Parity ID '{parityId}' is not registered.");
    }
}

internal sealed record ParityAcceptanceRegistration(
    string Id,
    string LegacyAdapterId,
    string XPlatAdapterId,
    string XPlatFunctionalDivergenceCode,
    Action<ParityScenario> ValidateInput,
    Func<IParityTarget> CreateLegacy,
    Func<IParityTarget> CreateXPlat)
{
    public void ValidateManifestBinding(
        ParityCertificationCase definition)
    {
        string[] expectedAdapters =
        [
            LegacyAdapterId,
            XPlatAdapterId,
        ];
        if (!definition.TargetAdapters.SequenceEqual(
                expectedAdapters,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Parity case '{Id}' targetAdapters do not match "
                + "the executable registry.");
        }

        if (!StringComparer.Ordinal.Equals(
                definition.FunctionalDivergenceCode,
                XPlatFunctionalDivergenceCode))
        {
            throw new InvalidDataException(
                $"Parity case '{Id}' functional divergence code does "
                + "not match the executable registry.");
        }

        if (!StringComparer.Ordinal.Equals(
                definition.Scenario.LegacyOracle?.AdapterId,
                LegacyAdapterId))
        {
            throw new InvalidDataException(
                $"Parity case '{Id}' legacyOracle adapter does not match "
                + "the executable registry.");
        }

        ValidateInput(definition.Scenario);
    }

    public string AdapterId(ParityTargetKind target)
    {
        return target switch
        {
            ParityTargetKind.Legacy => LegacyAdapterId,
            ParityTargetKind.XPlat => XPlatAdapterId,
            _ => throw new ArgumentOutOfRangeException(
                nameof(target),
                target,
                null),
        };
    }

    public Func<IParityTarget> CreateTarget(ParityTargetKind target)
    {
        return target switch
        {
            ParityTargetKind.Legacy => CreateLegacy,
            ParityTargetKind.XPlat => CreateXPlat,
            _ => throw new ArgumentOutOfRangeException(
                nameof(target),
                target,
                null),
        };
    }

    public bool IsFunctionalDivergence(
        ParityTargetKind target,
        string? failureCode)
    {
        return target == ParityTargetKind.XPlat
            && StringComparer.Ordinal.Equals(
                failureCode,
                XPlatFunctionalDivergenceCode);
    }

    public bool IsProductDivergenceCode(string? failureCode)
    {
        return StringComparer.Ordinal.Equals(
            failureCode,
            XPlatFunctionalDivergenceCode);
    }
}
