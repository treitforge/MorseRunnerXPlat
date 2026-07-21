using System.Globalization;
using System.Text.Json;
using MorseRunner.Audio;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatCompetitionForcedSettingsTarget : IParityTarget
{
    internal const string ParityId =
        "session.competition-forced-settings";
    internal const string FunctionalDivergenceCode =
        "session-competition-forced-settings-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Engine.MorseRunnerEngine.CreateSessionAsync";

    public async Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!StringComparer.Ordinal.Equals(scenario.Id, ParityId))
        {
            return new(
                ParityTargetOutcome.Failed,
                [],
                DomainErrorCodes.UnsupportedCapability,
                EvidenceSource);
        }

        CompetitionForcedSettingsInput input =
            CompetitionForcedSettingsInput.Parse(scenario);
        SessionSettings wpx = CreateSettings(
            input,
            "scWpx",
            "rmWpx",
            initialConditions: false);
        SessionSettings hst = CreateSettings(
            input,
            "scHst",
            "rmHst",
            initialConditions: true);

        await AssertSessionConsumesInputAsync(wpx, cancellationToken);
        await AssertSessionConsumesInputAsync(hst, cancellationToken);

        string[] values =
        [
            FormatSettings(wpx),
            FormatSettings(hst),
        ];
        bool matches = values.SequenceEqual(
            scenario.ExpectedValues,
            StringComparer.Ordinal);
        return new(
            matches
                ? ParityTargetOutcome.Passed
                : ParityTargetOutcome.Failed,
            values,
            matches ? null : FunctionalDivergenceCode,
            EvidenceSource);
    }

    private static SessionSettings CreateSettings(
        CompetitionForcedSettingsInput input,
        string contestId,
        string runModeId,
        bool initialConditions) =>
        new(
            input.Seed,
            new ContestId(contestId),
            new RunModeId(runModeId),
            DurationBlocks(input.InitialDurationMinutes))
        {
            Activity = input.InitialActivity,
            BandwidthHz = input.InitialBandwidthHz,
            Qsb = initialConditions,
            Qrm = initialConditions,
            Qrn = initialConditions,
            Flutter = initialConditions,
            Lids = initialConditions,
            SerialNumberRange = SerialNumberRangeMode.StartOfContest,
        };

    private static async Task AssertSessionConsumesInputAsync(
        SessionSettings settings,
        CancellationToken cancellationToken)
    {
        await using var engine = new MorseRunnerEngine(
            _ => new NullAudioSink());
        SessionHandle handle = await engine.CreateSessionAsync(
            settings,
            cancellationToken);
        SessionSnapshot snapshot = engine.GetSnapshot(handle.SessionId);
        if (snapshot.CurrentBandwidthHz != settings.BandwidthHz
            || snapshot.QsbEnabled != settings.Qsb)
        {
            throw new InvalidOperationException(
                "The XPlat session did not consume its submitted settings.");
        }

        await engine.CloseSessionAsync(handle.SessionId, cancellationToken);
    }

    private static string FormatSettings(SessionSettings settings) =>
        "competition-settings"
        + "|mode=" + settings.RunModeId.Value
        + "|duration-minutes=" + Format(DurationMinutes(
            settings.DurationBlocks))
        + "|activity=" + Format(settings.Activity)
        + "|bandwidth-hz=" + Format(settings.BandwidthHz)
        + "|qsb=" + Format(settings.Qsb)
        + "|qrm=" + Format(settings.Qrm)
        + "|qrn=" + Format(settings.Qrn)
        + "|flutter=" + Format(settings.Flutter)
        + "|lids=" + Format(settings.Lids);

    private static long DurationBlocks(int minutes) =>
        checked((long)Math.Ceiling(
            minutes
            * 60d
            * CompatibilityProfile.SampleRate
            / CompatibilityProfile.BlockSize));

    private static int DurationMinutes(long blocks) =>
        checked((int)Math.Round(
            blocks
            * CompatibilityProfile.BlockSize
            / (60d * CompatibilityProfile.SampleRate),
            MidpointRounding.AwayFromZero));

    private static string Format(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string Format(bool value) =>
        value.ToString().ToLowerInvariant();
}

internal sealed record CompetitionForcedSettingsInput(
    int CompetitionDurationMinutes,
    int InitialActivity,
    int InitialBandwidthHz,
    int InitialDurationMinutes,
    int Seed)
{
    public static CompetitionForcedSettingsInput Parse(
        ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames =
        [
            "competitionDurationMinutes",
            "initialActivity",
            "initialBandwidthHz",
            "initialDurationMinutes",
            "scenario",
            "seed",
        ];
        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var result = new CompetitionForcedSettingsInput(
            input.GetProperty("competitionDurationMinutes").GetInt32(),
            input.GetProperty("initialActivity").GetInt32(),
            input.GetProperty("initialBandwidthHz").GetInt32(),
            input.GetProperty("initialDurationMinutes").GetInt32(),
            input.GetProperty("seed").GetInt32());
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal)
            || input.GetProperty("scenario").GetString()
                != XPlatCompetitionForcedSettingsTarget.ParityId
            || result != new CompetitionForcedSettingsInput(
                17,
                7,
                500,
                30,
                12_345)
            || scenario.ExpectedValues.Count != 2)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' fixed vector is invalid.");
        }

        return result;
    }
}
