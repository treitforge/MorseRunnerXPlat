using System.Globalization;
using System.Text.Json;
using MorseRunner.Client;
using MorseRunner.Domain;
using MorseRunner.Tui;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatTuiWpmSetupUpperRangeTarget : IParityTarget
{
    internal const string ParityId =
        "ux.tui-wpm-setup-upper-range-increment-from-100-seed-12345";
    internal const string FunctionalDivergenceCode =
        "ux-TUI-WPM-setup-upper-range-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Tui.TuiApplication.HandleSettingsActionAsync"
        + "+MorseRunner.Tui.TuiApplication.AdjustCurrentSetting";

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

        TuiWpmSetupUpperRangeInput input =
            TuiWpmSetupUpperRangeInput.Parse(scenario);
        await using InProcessMorseRunnerClient client =
            InProcessMorseRunnerClient.CreateDefault();
        using var application = new TuiApplication(client, isHosted: false);
        application.State.View = TuiView.Settings;
        application.State.SettingsIndex = 1;
        application.State.WordsPerMinute = input.InitialWpm;
        await application.InitializeAsync(cancellationToken);
        int before = application.State.WordsPerMinute;
        await application.HandleAsync(
            new(TuiActionKind.IncreaseSetting),
            cancellationToken);
        int after = application.State.WordsPerMinute;
        string[] values =
        [
            "configuration"
            + "|seed=" + Format(input.Seed)
            + "|initial-wpm=" + Format(input.InitialWpm)
            + "|settings-increment-wpm=" + Format(input.SettingsIncrementWpm)
            + "|control-min-wpm=" + Format(input.ExpectedMinimumWpm)
            + "|control-max-wpm=" + Format(input.ExpectedMaximumWpm)
            + "|handler=TMainForm.SetWpm",
            "wpm-before|wpm=" + Format(before),
            "wpm-after-settings-increment"
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

internal sealed record TuiWpmSetupUpperRangeInput(
    int Seed,
    int ExpectedAfterWpm,
    int ExpectedMaximumWpm,
    int ExpectedMinimumWpm,
    int InitialWpm,
    int SettingsIncrementWpm)
{
    public static TuiWpmSetupUpperRangeInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames =
        [
            "expectedAfterWpm",
            "expectedMaximumWpm",
            "expectedMinimumWpm",
            "initialWpm",
            "scenario",
            "seed",
            "settingsIncrementWpm",
        ];
        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var result = new TuiWpmSetupUpperRangeInput(
            input.GetProperty("seed").GetInt32(),
            input.GetProperty("expectedAfterWpm").GetInt32(),
            input.GetProperty("expectedMaximumWpm").GetInt32(),
            input.GetProperty("expectedMinimumWpm").GetInt32(),
            input.GetProperty("initialWpm").GetInt32(),
            input.GetProperty("settingsIncrementWpm").GetInt32());
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal)
            || input.GetProperty("scenario").GetString()
                != XPlatTuiWpmSetupUpperRangeTarget.ParityId
            || result.Seed != 12_345
            || result.ExpectedAfterWpm != 101
            || result.ExpectedMaximumWpm != 120
            || result.ExpectedMinimumWpm != 10
            || result.InitialWpm != 100
            || result.SettingsIncrementWpm != 1
            || scenario.ExpectedValues.Count != 3)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' fixed vector is invalid.");
        }

        return result;
    }
}
