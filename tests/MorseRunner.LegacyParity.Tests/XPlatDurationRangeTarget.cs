using System.Globalization;
using System.Text.Json;
using MorseRunner.App.ViewModels;
using MorseRunner.Client;
using MorseRunner.Domain;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatDurationRangeTarget : IParityTarget
{
    internal const string ParityId = "settings.duration-full-range";
    internal const string FunctionalDivergenceCode =
        "settings-duration-full-range-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.App.ViewModels.MainWindowViewModel.DurationMinutes";

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

        DurationRangeInput input = DurationRangeInput.Parse(scenario);
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());
        int minimum = MainWindowViewModel.MinimumDurationMinutes;
        int maximum = MainWindowViewModel.MaximumDurationMinutes;
        viewModel.DurationMinutes = input.InitialValue;
        int initial = viewModel.DurationMinutes;
        viewModel.DurationMinutes = input.ArbitraryValue;
        int arbitrary = viewModel.DurationMinutes;
        viewModel.DurationMinutes = input.UpperValue;
        int upper = viewModel.DurationMinutes;
        viewModel.DurationMinutes = input.RequestBelow;
        int low = viewModel.DurationMinutes;
        viewModel.DurationMinutes = input.RequestAbove;
        int high = viewModel.DurationMinutes;
        string[] values =
        [
            $"duration-control|min={Format(minimum)}|max={Format(maximum)}"
                + $"|initial={Format(initial)}",
            $"duration-arbitrary|request={Format(input.ArbitraryValue)}"
                + $"|result={Format(arbitrary)}",
            $"duration-upper|request={Format(input.UpperValue)}"
                + $"|result={Format(upper)}",
            $"duration-low-clamp|request={Format(input.RequestBelow)}"
                + $"|result={Format(low)}",
            $"duration-high-clamp|request={Format(input.RequestAbove)}"
                + $"|result={Format(high)}",
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

internal sealed record DurationRangeInput(
    int ArbitraryValue,
    int InitialValue,
    int MaximumValue,
    int MinimumValue,
    int RequestAbove,
    int RequestBelow,
    int UpperValue)
{
    public static DurationRangeInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames =
        [
            "arbitraryValue",
            "initialValue",
            "maximumValue",
            "minimumValue",
            "requestAbove",
            "requestBelow",
            "scenario",
            "upperValue",
        ];
        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var result = new DurationRangeInput(
            input.GetProperty("arbitraryValue").GetInt32(),
            input.GetProperty("initialValue").GetInt32(),
            input.GetProperty("maximumValue").GetInt32(),
            input.GetProperty("minimumValue").GetInt32(),
            input.GetProperty("requestAbove").GetInt32(),
            input.GetProperty("requestBelow").GetInt32(),
            input.GetProperty("upperValue").GetInt32());
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal)
            || input.GetProperty("scenario").GetString()
                != XPlatDurationRangeTarget.ParityId
            || result != new DurationRangeInput(17, 30, 240, 1, 241, 0, 240)
            || scenario.ExpectedValues.Count != 5)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' fixed vector is invalid.");
        }

        return result;
    }
}
