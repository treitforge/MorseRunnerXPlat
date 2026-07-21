using System.Globalization;
using System.Text.Json;
using MorseRunner.App.ViewModels;
using MorseRunner.Client;
using MorseRunner.Domain;
using MorseRunner.Infrastructure;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatAvaloniaCustomWpmPageDownTarget : IParityTarget
{
    internal const string ParityId =
        "ux.wpm-custom-page-down-command-step-7-wpm-seed-12345";
    internal const string FunctionalDivergenceCode =
        "ux-WPM-custom-page-down-step-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Infrastructure.SettingsStore.LoadAsync"
        + "+MorseRunner.App.ViewModels.MainWindowViewModel.SpeedDownCommand"
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

        AvaloniaCustomWpmPageDownInput input =
            AvaloniaCustomWpmPageDownInput.Parse(scenario);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"MorseRunnerXPlat-parity-{Guid.NewGuid():N}.json");
        try
        {
            var settingsStore = new SettingsStore(path);
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
            await using var viewModel = new MainWindowViewModel(
                InProcessMorseRunnerClient.CreateDefault(),
                settingsStore: settingsStore);
            await viewModel.InitializeAsync();
            viewModel.Seed = input.Seed;
            viewModel.WordsPerMinute = input.InitialWpm;
            await viewModel.StartSingleCommand.ExecuteAsync(null);
            int before = viewModel.WordsPerMinute;
            await viewModel.SpeedDownCommand.ExecuteAsync(null);
            int after = viewModel.WordsPerMinute;
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
            File.Delete(path);
        }
    }

    private static string Format(int value) =>
        value.ToString(CultureInfo.InvariantCulture);
}

internal sealed record AvaloniaCustomWpmPageDownInput(
    int Seed,
    int CustomWpmStep,
    int ExpectedAfterWpm,
    int InitialWpm,
    string RunModeId)
{
    public static AvaloniaCustomWpmPageDownInput Parse(ParityScenario scenario)
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
        var result = new AvaloniaCustomWpmPageDownInput(
            input.GetProperty("seed").GetInt32(),
            input.GetProperty("customWpmStep").GetInt32(),
            input.GetProperty("expectedAfterWpm").GetInt32(),
            input.GetProperty("initialWpm").GetInt32(),
            input.GetProperty("runModeId").GetString() ?? string.Empty);
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal)
            || input.GetProperty("scenario").GetString()
                != XPlatAvaloniaCustomWpmPageDownTarget.ParityId
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


