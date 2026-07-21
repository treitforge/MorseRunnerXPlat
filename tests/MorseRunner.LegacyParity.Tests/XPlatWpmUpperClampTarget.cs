using System.Globalization;
using System.Text.Json;
using MorseRunner.App.ViewModels;
using MorseRunner.Client;
using MorseRunner.Domain;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatWpmUpperClampTarget : IParityTarget
{
    internal const string ParityId =
        "ux.wpm-upper-clamp-extra-page-up-from-118-seed-12345";
    internal const string FunctionalDivergenceCode =
        "ux-WPM-upper-clamp-mismatch";
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

        WpmUpperClampInput input = WpmUpperClampInput.Parse(scenario);
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault())
        {
            Seed = input.Seed,
            WordsPerMinute = input.InitialWpm,
        };
        await viewModel.StartSingleCommand.ExecuteAsync(null);
        int before = viewModel.WordsPerMinute;
        await viewModel.SpeedUpCommand.ExecuteAsync(null);
        int afterFirst = viewModel.WordsPerMinute;
        await viewModel.SpeedUpCommand.ExecuteAsync(null);
        int afterExtra = viewModel.WordsPerMinute;
        string[] values =
        [
            "configuration"
            + "|run-mode=" + input.RunModeId
            + "|seed=" + Format(input.Seed)
            + "|initial-wpm=" + Format(input.InitialWpm)
            + "|default-step-wpm=" + Format(input.DefaultWpmStep)
            + "|page-up-count=" + Format(input.PageUpCount)
            + "|handler=TMainForm.FormKeyDown",
            "wpm-before|wpm=" + Format(before),
            "wpm-after-first-page-up"
            + "|wpm=" + Format(afterFirst)
            + "|delta-wpm=" + Format(afterFirst - before),
            "wpm-after-extra-page-up"
            + "|wpm=" + Format(afterExtra)
            + "|delta-wpm=" + Format(afterExtra - before),
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

internal sealed record WpmUpperClampInput(
    int Seed,
    int DefaultWpmStep,
    int ExpectedClampWpm,
    int InitialWpm,
    int PageUpCount,
    string RunModeId)
{
    public static WpmUpperClampInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames =
        [
            "defaultWpmStep",
            "expectedClampWpm",
            "initialWpm",
            "pageUpCount",
            "runModeId",
            "scenario",
            "seed",
        ];
        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var result = new WpmUpperClampInput(
            input.GetProperty("seed").GetInt32(),
            input.GetProperty("defaultWpmStep").GetInt32(),
            input.GetProperty("expectedClampWpm").GetInt32(),
            input.GetProperty("initialWpm").GetInt32(),
            input.GetProperty("pageUpCount").GetInt32(),
            input.GetProperty("runModeId").GetString() ?? string.Empty);
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal)
            || input.GetProperty("scenario").GetString()
                != XPlatWpmUpperClampTarget.ParityId
            || result.Seed != 12_345
            || result.DefaultWpmStep != 2
            || result.ExpectedClampWpm != 120
            || result.InitialWpm != 118
            || result.PageUpCount != 2
            || result.RunModeId != "rmSingle"
            || scenario.ExpectedValues.Count != 4)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' fixed vector is invalid.");
        }

        return result;
    }
}
