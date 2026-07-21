using System.Globalization;
using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Infrastructure;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatLegacyNRDigitsMigrationTarget : IParityTarget
{
    internal const string ParityId = "settings.legacy-nrdigits-migration";
    internal const string FunctionalDivergenceCode =
        "settings-legacy-nrdigits-migration-mismatch";
    internal const string EvidenceSource =
        "LegacySettingsImporter over every CE NRDigits branch";

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

        _ = LegacyNRDigitsMigrationInput.Parse(scenario);
        string[] values = Enumerable.Range(0, 6)
            .Select(Observe)
            .ToArray();
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

    private static string Observe(int input)
    {
        SettingsDocument document = LegacySettingsImporter.Import(
            LegacyIniDocument.Parse(
                "[Station]\nNRDigits="
                + input.ToString(CultureInfo.InvariantCulture)));
        bool legacyKeyExists = document.Values.ContainsKey("Station.NRDigits");
        string serial = document.Values.TryGetValue(
            "Station.SerialNR",
            out string? value)
                ? value
                : "<missing>";
        return "legacy-nrdigits"
            + "|input=" + input.ToString(CultureInfo.InvariantCulture)
            + "|legacy-key-exists=" + legacyKeyExists
            + "|serial=" + serial;
    }
}

internal sealed record LegacyNRDigitsMigrationInput(int Seed)
{
    public static LegacyNRDigitsMigrationInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames = ["scenario", "seed"];
        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var result = new LegacyNRDigitsMigrationInput(
            input.GetProperty("seed").GetInt32());
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal)
            || input.GetProperty("scenario").GetString()
                != XPlatLegacyNRDigitsMigrationTarget.ParityId
            || result != new LegacyNRDigitsMigrationInput(12_345)
            || scenario.ExpectedValues.Count != 6)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' fixed vector is invalid.");
        }

        return result;
    }
}
