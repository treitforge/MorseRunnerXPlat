using System.Globalization;
using System.Text.Json;
using MorseRunner.Client;
using MorseRunner.Domain;
using MorseRunner.Tui;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatTuiRitUpStepTarget : IParityTarget
{
    internal const string ParityId =
        "ux.tui-rit-default-up-command-step-50-hz-seed-12345";
    internal const string FunctionalDivergenceCode =
        "ux-TUI-RIT-default-up-step-mismatch";
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

        TuiRitUpStepInput input = TuiRitUpStepInput.Parse(scenario);
        await using InProcessMorseRunnerClient client =
            InProcessMorseRunnerClient.CreateDefault();
        using var application = new TuiApplication(client, isHosted: false);
        await application.InitializeAsync(cancellationToken);
        await application.HandleAsync(
            new(TuiActionKind.StartSingle),
            cancellationToken);
        int before = application.State.Snapshot?.RitOffsetHz
            ?? throw new InvalidOperationException(
                "TUI session did not publish its initial snapshot.");
        await application.HandleAsync(
            new(TuiActionKind.RitUp),
            cancellationToken);
        int after = application.State.Snapshot?.RitOffsetHz
            ?? throw new InvalidOperationException(
                "TUI session did not publish its adjusted snapshot.");
        string[] values =
        [
            "configuration"
            + "|run-mode=" + input.RunModeId
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

internal sealed record TuiRitUpStepInput(
    int Seed,
    int DefaultRitStepHz,
    int ExpectedAfterHz,
    string RunModeId)
{
    public static TuiRitUpStepInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames =
        [
            "defaultRitStepHz",
            "expectedAfterHz",
            "runModeId",
            "scenario",
            "seed",
        ];
        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var result = new TuiRitUpStepInput(
            input.GetProperty("seed").GetInt32(),
            input.GetProperty("defaultRitStepHz").GetInt32(),
            input.GetProperty("expectedAfterHz").GetInt32(),
            input.GetProperty("runModeId").GetString() ?? string.Empty);
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal)
            || input.GetProperty("scenario").GetString()
                != XPlatTuiRitUpStepTarget.ParityId
            || result.Seed != 12_345
            || result.DefaultRitStepHz != 50
            || result.ExpectedAfterHz != 50
            || result.RunModeId != "rmSingle"
            || scenario.ExpectedValues.Count != 3)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' fixed vector is invalid.");
        }

        return result;
    }
}
