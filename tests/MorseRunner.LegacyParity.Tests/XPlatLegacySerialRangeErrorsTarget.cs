using System.Globalization;
using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Infrastructure;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatLegacySerialRangeErrorsTarget : IParityTarget
{
    internal const string ParityId = "settings.legacy-serial-range-errors";
    internal const string FunctionalDivergenceCode =
        "settings-legacy-serial-range-errors-mismatch";
    internal const string EvidenceSource =
        "Production SettingsStore legacy import diagnostics";

    public async Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!StringComparer.Ordinal.Equals(scenario.Id, ParityId))
        {
            return new ParityObservation(
                ParityTargetOutcome.Failed,
                [],
                DomainErrorCodes.UnsupportedCapability,
                EvidenceSource);
        }

        _ = LegacySerialRangeErrorsInput.Parse(scenario);
        string root = Path.Combine(
            Path.GetTempPath(),
            "MorseRunnerXPlat.SerialRangeErrors",
            Guid.NewGuid().ToString("N"));
        string settingsPath = Path.Combine(root, "settings.json");
        string legacyPath = Path.Combine(root, "MorseRunner.ini");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                legacyPath,
                """
                [Station]
                SerialNrMidContest=bad
                SerialNrEndContest=10000-10001
                SerialNrCustomRange=99-1
                """,
                cancellationToken);
            SettingsLoadResult result = await new SettingsStore(
                settingsPath,
                legacyPath).LoadAsync(cancellationToken);
            var values = new List<string>();
            foreach (string diagnostic in ExtractCeDiagnostics(
                         result.Diagnostic))
            {
                values.Add($"error[{values.Count}]={diagnostic}");
            }

            string mid = Get(
                result.Document.Values,
                "Station.SerialNrMidContest");
            string end = Get(
                result.Document.Values,
                "Station.SerialNrEndContest");
            string custom = Get(
                result.Document.Values,
                "Station.SerialNrCustomRange");
            values.Add(
                "retained-ranges"
                + "|mid=" + mid
                + "|end=" + end
                + "|custom=" + custom);
            values.Add(
                "custom-caption="
                + (IsValidCeRange(custom)
                    ? $"Custom Range ({custom})..."
                    : "Custom Range..."));
            bool matches = values.SequenceEqual(
                scenario.ExpectedValues,
                StringComparer.Ordinal);
            return new ParityObservation(
                matches
                    ? ParityTargetOutcome.Passed
                    : ParityTargetOutcome.Failed,
                values,
                matches ? null : FunctionalDivergenceCode,
                EvidenceSource);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static IEnumerable<string> ExtractCeDiagnostics(
        string? diagnostic) =>
        String.IsNullOrEmpty(diagnostic)
            ? []
            : diagnostic.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(value => value.StartsWith(
                    "Error while reading MorseRunner.ini file.",
                    StringComparison.Ordinal));

    private static string Get(
        IReadOnlyDictionary<string, string> values,
        string key) =>
        values.TryGetValue(key, out string? value) ? value : "<missing>";

    private static bool IsValidCeRange(string value)
    {
        string[] parts = value.Split('-');
        return parts.Length == 2
            && value.Count(character => character == '-') == 1
            && Int32.TryParse(
                parts[0],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int minimum)
            && Int32.TryParse(
                parts[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int maximum)
            && minimum > 0
            && minimum <= maximum
            && maximum <= 9_999;
    }
}

internal sealed record LegacySerialRangeErrorsInput(int Seed)
{
    public static LegacySerialRangeErrorsInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames = ["scenario", "seed"];
        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var result = new LegacySerialRangeErrorsInput(
            input.GetProperty("seed").GetInt32());
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal)
            || input.GetProperty("scenario").GetString()
                != XPlatLegacySerialRangeErrorsTarget.ParityId
            || result != new LegacySerialRangeErrorsInput(12_345)
            || scenario.ExpectedValues.Count != 5)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' fixed vector is invalid.");
        }

        return result;
    }
}
