using System.Text.Json;
using System.Text.Json.Serialization;

namespace MorseRunner.LegacyParity.Tests;

public sealed class OfflineLegacyFixtureTarget(string fixturePath) : IParityTarget
{
    public async Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        string absoluteFixturePath = Path.Combine(
            RepositoryPaths.Root,
            fixturePath);

        if (!File.Exists(absoluteFixturePath))
        {
            return new ParityObservation(
                ParityTargetOutcome.Failed,
                [],
                "legacy-fixture-not-found",
                absoluteFixturePath);
        }

        OracleFixture? fixture;
        try
        {
            await using FileStream stream = File.OpenRead(absoluteFixturePath);
            fixture = await JsonSerializer.DeserializeAsync<OracleFixture>(
                stream,
                cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return new ParityObservation(
                ParityTargetOutcome.Failed,
                [],
                "legacy-fixture-invalid",
                absoluteFixturePath);
        }

        if (fixture is null)
        {
            return new ParityObservation(
                ParityTargetOutcome.Failed,
                [],
                "legacy-fixture-invalid",
                absoluteFixturePath);
        }

        bool matches = StringComparer.Ordinal.Equals(
                fixture.Revision,
                LegacyOracleProvenance.PinnedLegacyRevision)
            && StringComparer.Ordinal.Equals(fixture.ParityId, scenario.Id)
            && fixture.Values.SequenceEqual(
                scenario.ExpectedValues,
                StringComparer.Ordinal);

        return new ParityObservation(
            matches ? ParityTargetOutcome.Passed : ParityTargetOutcome.Failed,
            fixture.Values,
            matches ? null : "legacy-fixture-mismatch",
            absoluteFixturePath);
    }

    private sealed record OracleFixture(
        [property: JsonPropertyName("revision")] string Revision,
        [property: JsonPropertyName("parityId")] string ParityId,
        [property: JsonPropertyName("values")] IReadOnlyList<string> Values);
}
