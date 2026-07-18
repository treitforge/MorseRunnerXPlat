namespace MorseRunner.LegacyParity.Tests;

public interface IParityTarget
{
    Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken);
}

public sealed record ParityScenario(
    string Id,
    string Capability,
    IReadOnlyList<string> ExpectedValues);

public sealed record ParityObservation(
    ParityTargetOutcome Outcome,
    IReadOnlyList<string> Values,
    string? FailureCode,
    string EvidenceSource);

public enum ParityTargetOutcome
{
    Passed,
    Failed,
}

public enum ParityAssessment
{
    BothGreen,
    LegacyGreenXPlatRed,
    LegacyFailure,
}

public static class ParityAssessmentClassifier
{
    public static ParityAssessment Classify(
        ParityObservation legacy,
        ParityObservation xplat)
    {
        if (legacy.Outcome != ParityTargetOutcome.Passed)
        {
            return ParityAssessment.LegacyFailure;
        }

        return xplat.Outcome == ParityTargetOutcome.Passed
            ? ParityAssessment.BothGreen
            : ParityAssessment.LegacyGreenXPlatRed;
    }
}
