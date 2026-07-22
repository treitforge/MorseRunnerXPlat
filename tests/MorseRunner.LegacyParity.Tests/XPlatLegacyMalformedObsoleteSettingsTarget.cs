using System.Globalization;
using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Infrastructure;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatLegacyMalformedObsoleteSettingsTarget : IParityTarget
{
    internal const string ParityId = "settings.legacy-malformed-obsolete";
    internal const string FunctionalDivergenceCode =
        "settings-legacy-malformed-obsolete-mismatch";
    internal const string EvidenceSource =
        "LegacySettingsImporter over malformed CE primitive settings";

    public Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!StringComparer.Ordinal.Equals(scenario.Id, ParityId))
        {
            return Task.FromResult(
                new ParityObservation(
                    ParityTargetOutcome.Failed,
                    [],
                    DomainErrorCodes.UnsupportedCapability,
                    EvidenceSource));
        }

        _ = LegacyMalformedObsoleteSettingsInput.Parse(scenario);
        SettingsDocument document = LegacySettingsImporter.Import(
            LegacyIniDocument.Parse(
                """
                [Station]
                cwopsnum=9999
                Pitch=not-a-value
                BandWidth=not-a-value
                Wpm=not-a-value
                Qsk=not-a-value
                SerialNR=not-a-value
                SelfMonVolume=not-a-value
                SaveWav=not-a-value
                [Contest]
                SimContest=not-a-value
                DefaultRunMode=not-a-value
                Duration=not-a-value
                CompetitionDuration=not-a-value
                [System]
                BufSize=not-a-value
                ShowCallsignInfo=not-a-value
                [Settings]
                FarnsworthCharacterRate=not-a-value
                WpmStepRate=not-a-value
                RitStepIncr=not-a-value
                SingleCallStartDelay=not-a-value
                [Band]
                Activity=not-a-value
                Qsb=not-a-value
                Qrm=not-a-value
                Qrn=not-a-value
                Flutter=not-a-value
                Lids=not-a-value
                """));
        string[] values = [Format(document.Values)];
        bool matches = values.SequenceEqual(
            scenario.ExpectedValues,
            StringComparer.Ordinal);
        return Task.FromResult(
            new ParityObservation(
                matches
                    ? ParityTargetOutcome.Passed
                    : ParityTargetOutcome.Failed,
                values,
                matches ? null : FunctionalDivergenceCode,
                EvidenceSource));
    }

    private static string Format(IReadOnlyDictionary<string, string> values) =>
        "malformed-settings"
        + "|cwopsnum-exists=" + values.ContainsKey("Station.cwopsnum")
        + "|pitch-hz=" + Get(values, "Station.Pitch")
        + "|bandwidth-hz=" + Get(values, "Station.BandWidth")
        + "|wpm=" + Get(values, "Station.Wpm")
        + "|qsk=" + Get(values, "Station.Qsk")
        + "|serial=" + Get(values, "Station.SerialNR")
        + "|monitor-db=" + Get(values, "Station.SelfMonVolume")
        + "|save-wav=" + Get(values, "Station.SaveWav")
        + "|contest=" + ContestOrdinal(Get(values, "Contest.SimContest"))
        + "|run-mode="
        + RunModeOrdinal(Get(values, "Contest.DefaultRunMode"))
        + "|duration=" + Get(values, "Contest.Duration")
        + "|competition-duration="
        + Get(values, "Contest.CompetitionDuration")
        + "|buffer-samples=" + Get(values, "System.BufSize")
        + "|show-call-info=" + Get(values, "System.ShowCallsignInfo")
        + "|farnsworth="
        + Get(values, "Settings.FarnsworthCharacterRate")
        + "|wpm-step=" + Get(values, "Settings.WpmStepRate")
        + "|rit-step=" + Get(values, "Settings.RitStepIncr")
        + "|single-delay="
        + Get(values, "Settings.SingleCallStartDelay")
        + "|activity=" + Get(values, "Band.Activity")
        + "|qsb=" + Get(values, "Band.Qsb")
        + "|qrm=" + Get(values, "Band.Qrm")
        + "|qrn=" + Get(values, "Band.Qrn")
        + "|flutter=" + Get(values, "Band.Flutter")
        + "|lids=" + Get(values, "Band.Lids");

    private static string Get(
        IReadOnlyDictionary<string, string> values,
        string key) =>
        values.TryGetValue(key, out string? value) ? value : "<missing>";

    private static string ContestOrdinal(string value)
    {
        int index = ContestCatalog.All
            .Select((contest, ordinal) => (contest, ordinal))
            .Where(item => StringComparer.Ordinal.Equals(
                item.contest.Id.Value,
                value))
            .Select(item => item.ordinal)
            .DefaultIfEmpty(-1)
            .Single();
        return index < 0
            ? "<invalid>"
            : index.ToString(CultureInfo.InvariantCulture);
    }

    private static string RunModeOrdinal(string value)
    {
        int index = RunModeCatalog.All
            .Select((mode, ordinal) => (mode, ordinal))
            .Where(item => StringComparer.Ordinal.Equals(item.mode.Value, value))
            .Select(item => item.ordinal)
            .DefaultIfEmpty(-1)
            .Single();
        return index < 0
            ? "<invalid>"
            : index.ToString(CultureInfo.InvariantCulture);
    }
}

internal sealed record LegacyMalformedObsoleteSettingsInput(int Seed)
{
    public static LegacyMalformedObsoleteSettingsInput Parse(
        ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames = ["scenario", "seed"];
        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var result = new LegacyMalformedObsoleteSettingsInput(
            input.GetProperty("seed").GetInt32());
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal)
            || input.GetProperty("scenario").GetString()
                != XPlatLegacyMalformedObsoleteSettingsTarget.ParityId
            || result != new LegacyMalformedObsoleteSettingsInput(12_345)
            || scenario.ExpectedValues.Count != 1)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' fixed vector is invalid.");
        }

        return result;
    }
}
