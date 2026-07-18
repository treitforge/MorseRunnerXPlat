using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MorseRunner.LegacyParity.Tests;

public sealed class LegacyOracleTarget(string fixturePath) : IParityTarget
{
    public async Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        string? configured = Environment.GetEnvironmentVariable(
            "MORSE_RUNNER_LEGACY_ORACLE");
        string defaultOraclePath = Path.Combine(
            RepositoryPaths.Root,
            "artifacts",
            "legacy-oracle",
            "LegacyOracle.exe");
        string oraclePath = String.IsNullOrWhiteSpace(configured)
            ? defaultOraclePath
            : Path.GetFullPath(configured);

        if (File.Exists(oraclePath))
        {
            return await ExecuteOracleAsync(
                oraclePath,
                scenario,
                cancellationToken);
        }

        string absoluteFixturePath = Path.Combine(
            RepositoryPaths.Root,
            fixturePath);
        await using FileStream stream = File.OpenRead(absoluteFixturePath);
        OracleFixture fixture = (await JsonSerializer.DeserializeAsync<OracleFixture>(
            stream,
            cancellationToken: cancellationToken))!;
        bool matches = StringComparer.Ordinal.Equals(
                fixture.Revision,
                "55bbd019c29d8cf693184ea420a17a253f16fe1e")
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

    private static async Task<ParityObservation> ExecuteOracleAsync(
        string oraclePath,
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        string legacyRoot = RepositoryPaths.LegacyRoot
            + Path.DirectorySeparatorChar;
        ProcessStartInfo startInfo = new(oraclePath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(legacyRoot);
        startInfo.ArgumentList.Add(scenario.Id);

        using Process process = Process.Start(startInfo)!;
        string output = await process.StandardOutput.ReadToEndAsync(
            cancellationToken);
        string error = await process.StandardError.ReadToEndAsync(
            cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            return new ParityObservation(
                ParityTargetOutcome.Failed,
                [],
                "legacy-oracle-failed",
                $"{oraclePath}: {error.Trim()}");
        }

        OracleObservation observation =
            JsonSerializer.Deserialize<OracleObservation>(output)!;
        bool matches = StringComparer.Ordinal.Equals(
                observation.Scenario,
                scenario.Id)
            && observation.Values.SequenceEqual(
                scenario.ExpectedValues,
                StringComparer.Ordinal);
        return new ParityObservation(
            matches ? ParityTargetOutcome.Passed : ParityTargetOutcome.Failed,
            observation.Values,
            matches ? null : "legacy-observation-mismatch",
            oraclePath);
    }

    private sealed record OracleFixture(
        [property: JsonPropertyName("revision")] string Revision,
        [property: JsonPropertyName("parityId")] string ParityId,
        [property: JsonPropertyName("values")] IReadOnlyList<string> Values);

    private sealed record OracleObservation(
        [property: JsonPropertyName("scenario")] string Scenario,
        [property: JsonPropertyName("values")] IReadOnlyList<string> Values);
}
