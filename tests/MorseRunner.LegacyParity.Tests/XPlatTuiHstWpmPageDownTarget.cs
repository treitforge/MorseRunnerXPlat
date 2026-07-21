using System.Globalization;
using System.Text.Json;
using MorseRunner.Client;
using MorseRunner.Domain;
using MorseRunner.Tui;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatTuiHstWpmPageDownTarget : IParityTarget
{
    internal const string ParityId =
        "ux.tui-wpm-hst-page-down-rounds-33-to-30-seed-12345";
    internal const string FunctionalDivergenceCode =
        "ux-TUI-WPM-HST-page-down-rounding-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Tui.TuiApplication.HandleAsync"
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

        TuiHstWpmPageDownInput input = TuiHstWpmPageDownInput.Parse(scenario);
        await using InProcessMorseRunnerClient client =
            InProcessMorseRunnerClient.CreateDefault();
        using var application = new TuiApplication(client, isHosted: false);
        application.State.WordsPerMinute = input.InitialWpm;
        application.State.ContestIndex = ContestCatalog.All
            .Select((contest, index) => (contest, index))
            .Single(item => item.contest.Id.Value == "scHst")
            .index;
        await application.InitializeAsync(cancellationToken);
        await application.HandleAsync(
            new(TuiActionKind.StartHst),
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
            + "|rounding-interval-wpm=5"
            + "|action=page-down"
            + "|handler=TMainForm.FormKeyDown",
            "wpm-before|wpm=" + Format(before),
            "wpm-after-hst-page-down"
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

internal sealed record TuiHstWpmPageDownInput(
    int Seed,
    int ExpectedAfterWpm,
    int InitialWpm,
    string RunModeId)
{
    public static TuiHstWpmPageDownInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames =
        [
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
        var result = new TuiHstWpmPageDownInput(
            input.GetProperty("seed").GetInt32(),
            input.GetProperty("expectedAfterWpm").GetInt32(),
            input.GetProperty("initialWpm").GetInt32(),
            input.GetProperty("runModeId").GetString() ?? string.Empty);
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal)
            || input.GetProperty("scenario").GetString()
                != XPlatTuiHstWpmPageDownTarget.ParityId
            || result.Seed != 12_345
            || result.ExpectedAfterWpm != 30
            || result.InitialWpm != 33
            || result.RunModeId != "rmHst"
            || scenario.ExpectedValues.Count != 3)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' fixed vector is invalid.");
        }

        return result;
    }
}
