using System.Globalization;
using System.Text.Json;
using MorseRunner.Audio;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatHstStartConstraintsTarget : IParityTarget
{
    internal const string ParityId = "session.hst-invalid-start-settings";
    internal const string FunctionalDivergenceCode =
        "session-hst-invalid-start-settings-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Engine.MorseRunnerEngine.ValidateSettings";

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

        HstStartConstraintsInput input = HstStartConstraintsInput.Parse(
            scenario);
        bool wrongContestAccepted = await AttemptCreateAsync(
            input.Seed,
            new ContestId(input.WrongContestId),
            SerialNumberRangeMode.StartOfContest,
            cancellationToken);
        bool wrongSerialAccepted = await AttemptCreateAsync(
            input.Seed,
            new ContestId(input.CorrectContestId),
            SerialNumberRangeMode.MidContest,
            cancellationToken);
        string[] values =
        [
            "hst-start-attempt"
            + "|seed=" + Format(input.Seed)
            + "|contest=" + input.WrongContestId
            + "|serial=" + input.RequiredSerialMode
            + "|requested=rmHst"
            + "|result=" + (wrongContestAccepted ? "rmHst" : "rmStop")
            + "|accepted=" + Format(wrongContestAccepted),
            "hst-start-attempt"
            + "|seed=" + Format(input.Seed)
            + "|contest=" + input.CorrectContestId
            + "|serial=" + input.WrongSerialMode
            + "|requested=rmHst"
            + "|result=" + (wrongSerialAccepted ? "rmHst" : "rmStop")
            + "|accepted=" + Format(wrongSerialAccepted),
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

    private static async Task<bool> AttemptCreateAsync(
        int seed,
        ContestId contestId,
        SerialNumberRangeMode serialNumberRange,
        CancellationToken cancellationToken)
    {
        await using var engine = new MorseRunnerEngine(
            _ => new NullAudioSink());
        try
        {
            SessionHandle handle = await engine.CreateSessionAsync(
                new SessionSettings(
                    seed,
                    contestId,
                    new RunModeId("rmHst"),
                    DurationBlocks: 0)
                {
                    SerialNumberRange = serialNumberRange,
                },
                cancellationToken);
            await engine.CloseSessionAsync(
                handle.SessionId,
                cancellationToken);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string Format(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string Format(bool value) =>
        value.ToString().ToLowerInvariant();
}

internal sealed record HstStartConstraintsInput(
    string CorrectContestId,
    string RequiredSerialMode,
    int Seed,
    string WrongContestId,
    string WrongSerialMode)
{
    public static HstStartConstraintsInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames =
        [
            "correctContestId",
            "requiredSerialMode",
            "scenario",
            "seed",
            "wrongContestId",
            "wrongSerialMode",
        ];
        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var result = new HstStartConstraintsInput(
            input.GetProperty("correctContestId").GetString()
                ?? string.Empty,
            input.GetProperty("requiredSerialMode").GetString()
                ?? string.Empty,
            input.GetProperty("seed").GetInt32(),
            input.GetProperty("wrongContestId").GetString()
                ?? string.Empty,
            input.GetProperty("wrongSerialMode").GetString()
                ?? string.Empty);
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal)
            || input.GetProperty("scenario").GetString()
                != XPlatHstStartConstraintsTarget.ParityId
            || result != new HstStartConstraintsInput(
                "scHst",
                "snStartContest",
                12_345,
                "scWpx",
                "snMidContest")
            || scenario.ExpectedValues.Count != 2)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' fixed vector is invalid.");
        }

        return result;
    }
}
