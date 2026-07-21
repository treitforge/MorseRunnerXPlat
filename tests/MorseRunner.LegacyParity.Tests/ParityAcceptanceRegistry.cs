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
            [XPlatRuntimeRitChangeTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatRuntimeRitChangeTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatRuntimeRitChangeTarget),
                    XPlatRuntimeRitChangeTarget.FunctionalDivergenceCode,
                    static scenario =>
                        _ = RuntimeRitChangeInput.Parse(scenario),
                    static () => new LegacyOracleTarget(),
                    static () => new XPlatRuntimeRitChangeTarget()),
            [XPlatRuntimeBandwidthChangeTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatRuntimeBandwidthChangeTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatRuntimeBandwidthChangeTarget),
                    XPlatRuntimeBandwidthChangeTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = RuntimeBandwidthChangeInput.Parse(scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatRuntimeBandwidthChangeTarget()),
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
            [XPlatQrmFirstTriggeredStationTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatQrmFirstTriggeredStationTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatQrmFirstTriggeredStationTarget),
                    XPlatQrmFirstTriggeredStationTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = QrmFirstTriggeredStationInput.Parse(
                            scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatQrmFirstTriggeredStationTarget()),
            [XPlatQrmCallerCollisionTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatQrmCallerCollisionTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatQrmCallerCollisionTarget),
                    XPlatQrmCallerCollisionTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = QrmCallerCollisionInput.Parse(scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatQrmCallerCollisionTarget()),
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
            [XPlatQrnBackgroundSparseImpulsesTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatQrnBackgroundSparseImpulsesTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatQrnBackgroundSparseImpulsesTarget),
                    XPlatQrnBackgroundSparseImpulsesTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = QrnBackgroundSparseImpulsesInput.Parse(
                            scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatQrnBackgroundSparseImpulsesTarget()),
            [XPlatQrnBurstStationLifecycleTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatQrnBurstStationLifecycleTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatQrnBurstStationLifecycleTarget),
                    XPlatQrnBurstStationLifecycleTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = QrnBurstStationLifecycleInput.Parse(
                            scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatQrnBurstStationLifecycleTarget()),
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
            [XPlatQsbRuntimeToggleTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatQsbRuntimeToggleTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatQsbRuntimeToggleTarget),
                    XPlatQsbRuntimeToggleTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = QsbRuntimeToggleInput.Parse(scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatQsbRuntimeToggleTarget()),
            [XPlatStartSilentEmptyEnterCqTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatStartSilentEmptyEnterCqTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatStartSilentEmptyEnterCqTarget),
                    XPlatStartSilentEmptyEnterCqTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = StartSilentEmptyEnterCqInput.Parse(scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatStartSilentEmptyEnterCqTarget()),
            [XPlatContestOperatorMessagesTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatContestOperatorMessagesTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatContestOperatorMessagesTarget),
                    XPlatContestOperatorMessagesTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = ContestOperatorMessagesInput.Parse(scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatContestOperatorMessagesTarget()),
            [XPlatCwtRemoteExchangeFormatTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatCwtRemoteExchangeFormatTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatCwtRemoteExchangeFormatTarget),
                    XPlatCwtRemoteExchangeFormatTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = CwtRemoteExchangeFormatInput.Parse(scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatCwtRemoteExchangeFormatTarget()),
            [XPlatDefaultTwoFieldRemoteExchangeFormatTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatDefaultTwoFieldRemoteExchangeFormatTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatDefaultTwoFieldRemoteExchangeFormatTarget),
                    XPlatDefaultTwoFieldRemoteExchangeFormatTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = DefaultTwoFieldRemoteExchangeFormatInput.Parse(
                            scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatDefaultTwoFieldRemoteExchangeFormatTarget()),
            [XPlatFullCutNumericRemoteExchangeFormatTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatFullCutNumericRemoteExchangeFormatTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatFullCutNumericRemoteExchangeFormatTarget),
                    XPlatFullCutNumericRemoteExchangeFormatTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = FullCutNumericRemoteExchangeFormatInput.Parse(
                            scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatFullCutNumericRemoteExchangeFormatTarget()),
            [XPlatArrlDxHighR1PowerRemoteExchangeFormatTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatArrlDxHighR1PowerRemoteExchangeFormatTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatArrlDxHighR1PowerRemoteExchangeFormatTarget),
                    XPlatArrlDxHighR1PowerRemoteExchangeFormatTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = ArrlDxHighR1PowerRemoteExchangeFormatInput.Parse(
                            scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatArrlDxHighR1PowerRemoteExchangeFormatTarget()),
            [XPlatCqwwRandomConsumptionRemoteExchangeFormatTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatCqwwRandomConsumptionRemoteExchangeFormatTarget
                        .ParityId,
                    "LegacyOracleTarget",
                    nameof(
                        XPlatCqwwRandomConsumptionRemoteExchangeFormatTarget),
                    XPlatCqwwRandomConsumptionRemoteExchangeFormatTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = CqwwRandomConsumptionRemoteExchangeFormatInput
                            .Parse(scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatCqwwRandomConsumptionRemoteExchangeFormatTarget()),
            [XPlatJarlRandomCutRemoteExchangeFormatTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatJarlRandomCutRemoteExchangeFormatTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatJarlRandomCutRemoteExchangeFormatTarget),
                    XPlatJarlRandomCutRemoteExchangeFormatTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = JarlRandomCutRemoteExchangeFormatInput.Parse(
                            scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatJarlRandomCutRemoteExchangeFormatTarget()),
            [XPlatRareRstErrorRemoteExchangeFormatTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatRareRstErrorRemoteExchangeFormatTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatRareRstErrorRemoteExchangeFormatTarget),
                    XPlatRareRstErrorRemoteExchangeFormatTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = RareRstErrorRemoteExchangeFormatInput.Parse(
                            scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatRareRstErrorRemoteExchangeFormatTarget()),
            [XPlatLidSerialCorrectionRemoteExchangeFormatTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatLidSerialCorrectionRemoteExchangeFormatTarget
                        .ParityId,
                    "LegacyOracleTarget",
                    nameof(
                        XPlatLidSerialCorrectionRemoteExchangeFormatTarget),
                    XPlatLidSerialCorrectionRemoteExchangeFormatTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = LidSerialCorrectionRemoteExchangeFormatInput.Parse(
                            scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatLidSerialCorrectionRemoteExchangeFormatTarget()),
            [XPlatFieldDayRemoteExchangeFormatTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatFieldDayRemoteExchangeFormatTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatFieldDayRemoteExchangeFormatTarget),
                    XPlatFieldDayRemoteExchangeFormatTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = FieldDayRemoteExchangeFormatInput.Parse(
                            scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatFieldDayRemoteExchangeFormatTarget()),
            [XPlatHstRemoteExchangeFormatTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatHstRemoteExchangeFormatTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatHstRemoteExchangeFormatTarget),
                    XPlatHstRemoteExchangeFormatTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = HstRemoteExchangeFormatInput.Parse(scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatHstRemoteExchangeFormatTarget()),
            [XPlatNaqpRemoteExchangeFormatTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatNaqpRemoteExchangeFormatTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatNaqpRemoteExchangeFormatTarget),
                    XPlatNaqpRemoteExchangeFormatTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = NaqpRemoteExchangeFormatInput.Parse(scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatNaqpRemoteExchangeFormatTarget()),
            [XPlatSstRemoteExchangeFormatTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatSstRemoteExchangeFormatTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatSstRemoteExchangeFormatTarget),
                    XPlatSstRemoteExchangeFormatTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = SstRemoteExchangeFormatInput.Parse(scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatSstRemoteExchangeFormatTarget()),
            [XPlatSweepstakesRemoteExchangeFormatTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatSweepstakesRemoteExchangeFormatTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatSweepstakesRemoteExchangeFormatTarget),
                    XPlatSweepstakesRemoteExchangeFormatTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = SweepstakesRemoteExchangeFormatInput.Parse(
                            scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatSweepstakesRemoteExchangeFormatTarget()),
            [XPlatWpxMidContestRemoteExchangeFormatTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatWpxMidContestRemoteExchangeFormatTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatWpxMidContestRemoteExchangeFormatTarget),
                    XPlatWpxMidContestRemoteExchangeFormatTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = WpxMidContestRemoteExchangeFormatInput.Parse(
                            scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatWpxMidContestRemoteExchangeFormatTarget()),
            [XPlatWpxCustomRangeRemoteExchangeFormatTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatWpxCustomRangeRemoteExchangeFormatTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatWpxCustomRangeRemoteExchangeFormatTarget),
                    XPlatWpxCustomRangeRemoteExchangeFormatTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = WpxCustomRangeRemoteExchangeFormatInput.Parse(
                            scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatWpxCustomRangeRemoteExchangeFormatTarget()),
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
            [XPlatReceiverHissSharedRandomCheckpointTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatReceiverHissSharedRandomCheckpointTarget
                        .ParityId,
                    "LegacyOracleTarget",
                    nameof(
                        XPlatReceiverHissSharedRandomCheckpointTarget),
                    XPlatReceiverHissSharedRandomCheckpointTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = ReceiverHissSharedRandomCheckpointInput
                            .Parse(scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new
                            XPlatReceiverHissSharedRandomCheckpointTarget()),
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
            [XPlatQskReceiverDuckingTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatQskReceiverDuckingTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatQskReceiverDuckingTarget),
                    XPlatQskReceiverDuckingTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = QskReceiverDuckingInput.Parse(scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatQskReceiverDuckingTarget()),
            [XPlatRuntimeQskChangeTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatRuntimeQskChangeTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatRuntimeQskChangeTarget),
                    XPlatRuntimeQskChangeTarget.FunctionalDivergenceCode,
                    static scenario =>
                        _ = RuntimeQskChangeInput.Parse(scenario),
                    static () => new LegacyOracleTarget(),
                    static () => new XPlatRuntimeQskChangeTarget()),
            [XPlatOperatorMonitorMuteTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatOperatorMonitorMuteTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatOperatorMonitorMuteTarget),
                    XPlatOperatorMonitorMuteTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = OperatorMonitorMuteInput.Parse(scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatOperatorMonitorMuteTarget()),
            [XPlatOperatorMonitorRuntimeChangeTarget.ParityId] =
                new ParityAcceptanceRegistration(
                    XPlatOperatorMonitorRuntimeChangeTarget.ParityId,
                    "LegacyOracleTarget",
                    nameof(XPlatOperatorMonitorRuntimeChangeTarget),
                    XPlatOperatorMonitorRuntimeChangeTarget
                        .FunctionalDivergenceCode,
                    static scenario =>
                        _ = OperatorMonitorRuntimeChangeInput.Parse(
                            scenario),
                    static () => new LegacyOracleTarget(),
                    static () =>
                        new XPlatOperatorMonitorRuntimeChangeTarget()),
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
