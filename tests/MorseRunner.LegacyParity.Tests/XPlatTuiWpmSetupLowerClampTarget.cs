using System.Globalization;
using System.Text.Json;
using MorseRunner.Client;
using MorseRunner.Domain;
using MorseRunner.Tui;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatTuiWpmSetupLowerClampTarget : IParityTarget
{
    internal const string ParityId =
        "ux.tui-wpm-setup-lower-clamp-decrement-from-10-seed-12345";
    internal const string FunctionalDivergenceCode =
        "ux-TUI-WPM-setup-lower-clamp-mismatch";
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

        TuiWpmSetupLowerClampInput input =
            TuiWpmSetupLowerClampInput.Parse(scenario);
        await using InProcessMorseRunnerClient client =
            InProcessMorseRunnerClient.CreateDefault();
        using var application = new TuiApplication(client, isHosted: false);
        application.State.View = TuiView.Settings;
        application.State.SettingsIndex = 1;
        application.State.WordsPerMinute = input.InitialWpm;
        await application.InitializeAsync(cancellationToken);
        int before = application.State.WordsPerMinute;
        await application.HandleAsync(
            new(TuiActionKind.DecreaseSetting),
            cancellationToken);
        int after = application.State.WordsPerMinute;
        string[] values =
        [
            "configuration"
            + "|seed=" + Format(input.Seed)
            + "|initial-wpm=" + Format(input.InitialWpm)
            + "|settings-decrement-wpm=" + Format(input.SettingsDecrementWpm)
            + "|control-min-wpm=" + Format(input.ExpectedMinimumWpm)
            + "|control-max-wpm=" + Format(input.ExpectedMaximumWpm)
            + "|handler=TMainForm.SetWpm",
            "wpm-before|wpm=" + Format(before),
            "wpm-after-settings-decrement"
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

internal sealed record TuiWpmSetupLowerClampInput(
    int Seed,
    int ExpectedAfterWpm,
    int ExpectedMaximumWpm,
    int ExpectedMinimumWpm,
    int InitialWpm,
    int SettingsDecrementWpm)
{
    public static TuiWpmSetupLowerClampInput Parse(ParityScenario scenario)
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
            "settingsDecrementWpm",
        ];
        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var result = new TuiWpmSetupLowerClampInput(
            input.GetProperty("seed").GetInt32(),
            input.GetProperty("expectedAfterWpm").GetInt32(),
            input.GetProperty("expectedMaximumWpm").GetInt32(),
            input.GetProperty("expectedMinimumWpm").GetInt32(),
            input.GetProperty("initialWpm").GetInt32(),
            input.GetProperty("settingsDecrementWpm").GetInt32());
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal)
            || input.GetProperty("scenario").GetString()
                != XPlatTuiWpmSetupLowerClampTarget.ParityId
            || result.Seed != 12_345
            || result.ExpectedAfterWpm != 10
            || result.ExpectedMaximumWpm != 120
            || result.ExpectedMinimumWpm != 10
            || result.InitialWpm != 10
            || result.SettingsDecrementWpm != 1
            || scenario.ExpectedValues.Count != 3)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' fixed vector is invalid.");
        }

        return result;
    }
}
