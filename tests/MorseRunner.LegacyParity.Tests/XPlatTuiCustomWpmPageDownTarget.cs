using System.Globalization;
using System.Text.Json;
using MorseRunner.Client;
using MorseRunner.Domain;
using MorseRunner.Infrastructure;
using MorseRunner.Tui;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatTuiCustomWpmPageDownTarget : IParityTarget
{
    internal const string ParityId =
        "ux.tui-wpm-custom-page-down-command-step-7-wpm-seed-12345";
    internal const string FunctionalDivergenceCode =
        "ux-TUI-WPM-custom-page-down-step-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Infrastructure.SettingsStore.LoadAsync"
        + "+MorseRunner.Tui.TuiApplication.HandleAsync"
        + "+MorseRunner.Client.InProcessMorseRunnerClient"
        + "+MorseRunner.Engine.EngineSession.ApplyRadioControl";

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

        TuiCustomWpmPageDownInput input = TuiCustomWpmPageDownInput.Parse(scenario);
        string root = Path.Combine(
            Path.GetTempPath(),
            $"MorseRunnerXPlat-parity-{Guid.NewGuid():N}");
        try
        {
            var paths = new ApplicationPaths(root);
            paths.EnsureWritableDirectories();
            var settingsStore = new SettingsStore(
                Path.Combine(paths.Settings, "settings.json"));
            await settingsStore.SaveAsync(
                new SettingsDocument(
                    SettingsDocument.CurrentSchemaVersion,
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["Settings.WpmStepRate"] =
                            Format(input.CustomWpmStep),
                    }),
                cancellationToken);
            await using InProcessMorseRunnerClient client =
                InProcessMorseRunnerClient.CreateDefault();
            using var application = new TuiApplication(
                client,
                isHosted: false,
                paths);
            application.State.WordsPerMinute = input.InitialWpm;
            await application.InitializeAsync(cancellationToken);
            await application.HandleAsync(
                new(TuiActionKind.StartSingle),
                cancellationToken);
            int before = application.State.Snapshot?.CurrentWordsPerMinute
                ?? throw new InvalidOperationException(
                    "TUI session did not publish its initial snapshot.");
            await application.HandleAsync(
                new(TuiActionKind.SpeedDown),
                cancellationToken);
            int after = application.State.Snapshot?.CurrentWordsPerMinute
                ?? throw new InvalidOperationException(
                    "TUI session did not publish its adjusted snapshot.");
            string[] values =
            [
                "configuration"
                + "|run-mode=" + input.RunModeId
                + "|seed=" + Format(input.Seed)
                + "|initial-wpm=" + Format(input.InitialWpm)
                + "|custom-step-wpm=" + Format(input.CustomWpmStep)
                + "|action=page-down"
                + "|handler=TMainForm.FormKeyDown",
                "wpm-before|wpm=" + Format(before),
                "wpm-after-custom-page-down"
                + "|wpm=" + Format(after)
                + "|delta-wpm=" + Format(after - before),
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

    private static string Format(int value) =>
        value.ToString(CultureInfo.InvariantCulture);
}

internal sealed record TuiCustomWpmPageDownInput(
    int Seed,
    int CustomWpmStep,
    int ExpectedAfterWpm,
    int InitialWpm,
    string RunModeId)
{
    public static TuiCustomWpmPageDownInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames =
        [
            "customWpmStep",
            "expectedAfterWpm",
            "initialWpm",
            "runModeId",
            "scenario",
            "seed",
        ];
        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var result = new TuiCustomWpmPageDownInput(
            input.GetProperty("seed").GetInt32(),
            input.GetProperty("customWpmStep").GetInt32(),
            input.GetProperty("expectedAfterWpm").GetInt32(),
            input.GetProperty("initialWpm").GetInt32(),
            input.GetProperty("runModeId").GetString() ?? string.Empty);
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal)
            || input.GetProperty("scenario").GetString()
                != XPlatTuiCustomWpmPageDownTarget.ParityId
            || result.Seed != 12_345
            || result.CustomWpmStep != 7
            || result.ExpectedAfterWpm != 23
            || result.InitialWpm != 30
            || result.RunModeId != "rmSingle"
            || scenario.ExpectedValues.Count != 3)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' fixed vector is invalid.");
        }

        return result;
    }
}


