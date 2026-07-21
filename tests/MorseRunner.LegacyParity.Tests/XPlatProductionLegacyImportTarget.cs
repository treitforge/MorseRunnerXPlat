using System.Globalization;
using System.Text.Json;
using MorseRunner.App.ViewModels;
using MorseRunner.Client;
using MorseRunner.Domain;
using MorseRunner.Infrastructure;
using MorseRunner.Tui;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatProductionLegacyImportTarget : IParityTarget
{
    internal const string ParityId = "settings.production-legacy-import";
    internal const string FunctionalDivergenceCode =
        "settings-production-legacy-import-mismatch";
    internal const string EvidenceSource =
        "Production Avalonia and TUI startup settings paths";

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

        _ = ProductionLegacyImportInput.Parse(scenario);
        string root = Path.Combine(
            Path.GetTempPath(),
            "MorseRunnerXPlat.ProductionLegacyImport",
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        try
        {
            string[] values =
            [
                await ObserveAvaloniaAsync(root + "-app", cancellationToken),
                await ObserveTuiAsync(root + "-tui", cancellationToken),
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
            foreach (string path in new[] { root + "-app", root + "-tui" })
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
        }
    }

    private static async Task<string> ObserveAvaloniaAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var paths = new ApplicationPaths(root);
        paths.EnsureWritableDirectories();
        await WriteLegacyIniAsync(paths.Root, cancellationToken);
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault(),
            settingsStore: new SettingsStore(
                Path.Combine(paths.Settings, "settings.json"),
                paths.LegacySettingsImport));
        await viewModel.InitializeAsync();
        return Format("avalonia", viewModel.StationCall, viewModel.PitchHz);
    }

    private static async Task<string> ObserveTuiAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var paths = new ApplicationPaths(root);
        paths.EnsureWritableDirectories();
        await WriteLegacyIniAsync(paths.Root, cancellationToken);
        await using InProcessMorseRunnerClient client =
            InProcessMorseRunnerClient.CreateDefault();
        using var application = new TuiApplication(
            client,
            isHosted: false,
            paths);
        await application.InitializeAsync(cancellationToken);
        return Format(
            "tui",
            application.State.StationCall,
            application.State.PitchHz);
    }

    private static Task WriteLegacyIniAsync(
        string root,
        CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(
            Path.Combine(root, "MorseRunner.ini"),
            "[Station]" + Environment.NewLine
                + "Call=K7ABC" + Environment.NewLine
                + "Pitch=6" + Environment.NewLine,
            cancellationToken);

    private static string Format(
        string surface,
        string stationCall,
        int pitchHz) =>
        "startup-import"
        + "|surface=" + surface
        + "|station-call=" + stationCall
        + "|pitch-hz=" + pitchHz.ToString(CultureInfo.InvariantCulture);
}

internal sealed record ProductionLegacyImportInput(int Seed)
{
    public static ProductionLegacyImportInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames = ["scenario", "seed"];
        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var result = new ProductionLegacyImportInput(
            input.GetProperty("seed").GetInt32());
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal)
            || input.GetProperty("scenario").GetString()
                != XPlatProductionLegacyImportTarget.ParityId
            || result != new ProductionLegacyImportInput(12_345)
            || scenario.ExpectedValues.Count != 2)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' fixed vector is invalid.");
        }

        return result;
    }
}
