using System.Globalization;
using System.Text.Json;
using MorseRunner.App.ViewModels;
using MorseRunner.Client;
using MorseRunner.Domain;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatAvaloniaWpmStepTarget : IParityTarget
{
    internal const string ParityId =
        "ux.wpm-default-page-up-command-step-2-wpm-seed-12345";
    internal const string FunctionalDivergenceCode =
        "ux-WPM-default-page-up-step-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.App.ViewModels.MainWindowViewModel.SpeedUpCommand"
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

        AvaloniaWpmStepInput input = AvaloniaWpmStepInput.Parse(scenario);
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());
        viewModel.Seed = input.Seed;
        viewModel.WordsPerMinute = input.InitialWpm;
        await viewModel.StartSingleCommand.ExecuteAsync(null);
        int before = viewModel.WordsPerMinute;
        await viewModel.SpeedUpCommand.ExecuteAsync(null);
        int after = viewModel.WordsPerMinute;
        string[] values =
        [
            "configuration"
            + "|run-mode=" + input.RunModeId
            + "|seed=" + Format(input.Seed)
            + "|initial-wpm=" + Format(input.InitialWpm)
            + "|default-step-wpm=" + Format(input.DefaultWpmStep)
            + "|action=page-up"
            + "|handler=TMainForm.FormKeyDown",
            "wpm-before|wpm=" + Format(before),
            "wpm-after-default-page-up"
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

    private static string Format(int value) =>
        value.ToString(CultureInfo.InvariantCulture);
}

internal sealed record AvaloniaWpmStepInput(
    int Seed,
    int DefaultWpmStep,
    int ExpectedAfterWpm,
    int InitialWpm,
    string RunModeId)
{
    public static AvaloniaWpmStepInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames =
        [
            "defaultWpmStep",
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
        var result = new AvaloniaWpmStepInput(
            input.GetProperty("seed").GetInt32(),
            input.GetProperty("defaultWpmStep").GetInt32(),
            input.GetProperty("expectedAfterWpm").GetInt32(),
            input.GetProperty("initialWpm").GetInt32(),
            input.GetProperty("runModeId").GetString() ?? string.Empty);
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal)
            || input.GetProperty("scenario").GetString()
                != XPlatAvaloniaWpmStepTarget.ParityId
            || result.Seed != 12_345
            || result.DefaultWpmStep != 2
            || result.ExpectedAfterWpm != 32
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
