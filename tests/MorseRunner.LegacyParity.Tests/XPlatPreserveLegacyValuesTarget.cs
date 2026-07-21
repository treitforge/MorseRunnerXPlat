using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Infrastructure;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatPreserveLegacyValuesTarget : IParityTarget
{
    internal const string ParityId = "settings.preserve-legacy-values";
    internal const string FunctionalDivergenceCode =
        "settings-preserve-legacy-values-mismatch";
    internal const string EvidenceSource =
        "LegacySettingsImporter plus production SettingsStore save and reload";

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

        _ = PreserveLegacyValuesInput.Parse(scenario);
        string root = Path.Combine(
            Path.GetTempPath(),
            "MorseRunnerXPlat.PreserveLegacyValues",
            Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "settings.json");
        try
        {
            LegacyIniDocument source = LegacyIniDocument.Parse(
                """
                [Future]
                Mystery=keep-me
                [Station]
                CallsFromKeyer=1
                Call=K7ABC
                """);
            SettingsDocument imported = LegacySettingsImporter.Import(source);
            var store = new SettingsStore(path);
            await store.SaveAsync(imported, cancellationToken);
            await store.SaveAsync(
                new SettingsDocument(
                    SettingsDocument.CurrentSchemaVersion,
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["Station.Call"] = "K7XYZ",
                    }),
                cancellationToken);
            SettingsLoadResult restarted = await store.LoadAsync(
                cancellationToken);
            string[] values =
            [
                "preservation"
                + "|unknown="
                + Get(restarted.Document.Values, "Legacy.Future.Mystery")
                + "|unconsumed="
                + Get(restarted.Document.Values, "Station.CallsFromKeyer")
                + "|call="
                + Get(restarted.Document.Values, "Station.Call"),
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
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string Get(
        IReadOnlyDictionary<string, string> values,
        string key) =>
        values.TryGetValue(key, out string? value) ? value : "<missing>";
}

internal sealed record PreserveLegacyValuesInput(int Seed)
{
    public static PreserveLegacyValuesInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames = ["scenario", "seed"];
        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var result = new PreserveLegacyValuesInput(
            input.GetProperty("seed").GetInt32());
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal)
            || input.GetProperty("scenario").GetString()
                != XPlatPreserveLegacyValuesTarget.ParityId
            || result != new PreserveLegacyValuesInput(12_345)
            || scenario.ExpectedValues.Count != 1)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' fixed vector is invalid.");
        }

        return result;
    }
}
