using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Infrastructure;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatCeEncodingTranslationTarget : IParityTarget
{
    internal const string ParityId = "settings.ce-encoding-translation";
    internal const string FunctionalDivergenceCode =
        "settings-ce-encoding-translation-mismatch";
    internal const string EvidenceSource =
        "LegacySettingsImporter over a fixed CE INI vector";

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

        _ = CeEncodingTranslationInput.Parse(scenario);
        LegacyIniDocument source = LegacyIniDocument.Parse(
            """
            [Station]
            Pitch=6
            BandWidth=4
            SerialNR=2
            SelfMonVolume=-99
            CqWpxExchange=5NN 007
            Qsk=1
            SaveWav=1
            [Contest]
            SimContest=9
            DefaultRunMode=3
            Duration=47
            CompetitionDuration=99
            [System]
            BufSize=4
            ShowCallsignInfo=0
            [Settings]
            WpmStepRate=0
            RitStepIncr=700
            SingleCallStartDelay=3000
            [Band]
            Qsb=1
            Qrm=0
            Qrn=1
            Flutter=0
            Lids=1
            """);
        SettingsDocument document = LegacySettingsImporter.Import(source);
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
        "translation"
        + "|Station.Pitch=" + Get(values, "Station.Pitch")
        + "|Station.BandWidth=" + Get(values, "Station.BandWidth")
        + "|Contest.SimContest=" + Get(values, "Contest.SimContest")
        + "|Contest.DefaultRunMode="
        + Get(values, "Contest.DefaultRunMode")
        + "|Station.SerialNR=" + Get(values, "Station.SerialNR")
        + "|System.BufSize=" + Get(values, "System.BufSize")
        + "|Contest.Duration=" + Get(values, "Contest.Duration")
        + "|Contest.CompetitionDuration="
        + Get(values, "Contest.CompetitionDuration")
        + "|Station.SelfMonVolume="
        + Get(values, "Station.SelfMonVolume")
        + "|Settings.WpmStepRate=" + Get(values, "Settings.WpmStepRate")
        + "|Settings.RitStepIncr=" + Get(values, "Settings.RitStepIncr")
        + "|Settings.SingleCallStartDelay="
        + Get(values, "Settings.SingleCallStartDelay")
        + "|Station.CqWpxExchange="
        + Get(values, "Station.CqWpxExchange")
        + "|Station.Qsk=" + Get(values, "Station.Qsk")
        + "|Station.SaveWav=" + Get(values, "Station.SaveWav")
        + "|Band.Qsb=" + Get(values, "Band.Qsb")
        + "|Band.Qrm=" + Get(values, "Band.Qrm")
        + "|Band.Qrn=" + Get(values, "Band.Qrn")
        + "|Band.Flutter=" + Get(values, "Band.Flutter")
        + "|Band.Lids=" + Get(values, "Band.Lids")
        + "|System.ShowCallsignInfo="
        + Get(values, "System.ShowCallsignInfo");

    private static string Get(
        IReadOnlyDictionary<string, string> values,
        string key) =>
        values.TryGetValue(key, out string? value) ? value : "<missing>";
}

internal sealed record CeEncodingTranslationInput(int Seed)
{
    public static CeEncodingTranslationInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames = ["scenario", "seed"];
        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var result = new CeEncodingTranslationInput(
            input.GetProperty("seed").GetInt32());
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal)
            || input.GetProperty("scenario").GetString()
                != XPlatCeEncodingTranslationTarget.ParityId
            || result != new CeEncodingTranslationInput(12_345)
            || scenario.ExpectedValues.Count != 1)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' fixed vector is invalid.");
        }

        return result;
    }
}
