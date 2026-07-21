using System.Globalization;
using System.Text.Json;
using MorseRunner.App.ViewModels;
using MorseRunner.Client;
using MorseRunner.Domain;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatAvaloniaRitUpStepTarget : IParityTarget
{
    internal const string ParityId =
        "ux.rit-default-up-command-step-50-hz-seed-12345";
    internal const string FunctionalDivergenceCode =
        "ux-RIT-default-up-step-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.App.ViewModels.MainWindowViewModel.RitUpCommand"
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

        AvaloniaRitUpStepInput input = AvaloniaRitUpStepInput.Parse(scenario);
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());
        viewModel.Seed = input.Seed;
        await viewModel.StartCommand.ExecuteAsync(null);
        int before = viewModel.RitOffsetHz;
        await viewModel.RitUpCommand.ExecuteAsync(null);
        int after = viewModel.RitOffsetHz;
        string[] values =
        [
            "configuration"
            + "|run-mode=rmStop"
            + "|seed=" + Format(input.Seed)
            + "|default-step-hz=" + Format(input.DefaultRitStepHz)
            + "|action=up"
            + "|handler=TMainForm.Panel8MouseDown",
            "rit-before|hz=" + Format(before),
            "rit-after-default-up"
            + "|hz=" + Format(after)
            + "|delta-hz=" + Format(after - before),
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

internal sealed record AvaloniaRitUpStepInput(
    int Seed,
    int DefaultRitStepHz,
    int ExpectedAfterHz)
{
    public static AvaloniaRitUpStepInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames =
        [
            "defaultRitStepHz",
            "expectedAfterHz",
            "scenario",
            "seed",
        ];
        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var result = new AvaloniaRitUpStepInput(
            input.GetProperty("seed").GetInt32(),
            input.GetProperty("defaultRitStepHz").GetInt32(),
            input.GetProperty("expectedAfterHz").GetInt32());
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal)
            || input.GetProperty("scenario").GetString()
                != XPlatAvaloniaRitUpStepTarget.ParityId
            || result.Seed != 12_345
            || result.DefaultRitStepHz != 50
            || result.ExpectedAfterHz != 50
            || scenario.ExpectedValues.Count != 3)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' fixed vector is invalid.");
        }

        return result;
    }
}
