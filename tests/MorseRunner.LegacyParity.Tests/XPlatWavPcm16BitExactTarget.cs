using System.Globalization;
using System.Text.Json;
using MorseRunner.Audio;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatWavPcm16BitExactTarget : IParityTarget
{
    internal const string ParityId = "audio.wav-pcm16-bit-exact";
    internal const string FunctionalDivergenceCode =
        "audio-wav-pcm16-bit-exact-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Audio.WavAudioSink";

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

        WavPcm16BitExactInput input =
            WavPcm16BitExactInput.Parse(scenario);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"morse-runner-parity-{Guid.NewGuid():N}.wav");
        string[] values;
        try
        {
            await using var sink = new WavAudioSink(path);
            await sink.InitializeAsync(
                SessionId.New(),
                new AudioStreamFormat(
                    input.SampleRate,
                    Channels: 1,
                    BlockSize: input.PcmSamples.Count),
                cancellationToken);
            await sink.WriteAsync(
                input.PcmSamples
                    .Select(value => value / (float)Int16.MaxValue)
                    .ToArray(),
                simulationBlock: 0,
                cancellationToken);
            await sink.CompleteAsync(cancellationToken);

            byte[] bytes = await File.ReadAllBytesAsync(
                path,
                cancellationToken);
            values =
            [
                "wav|sampleRate="
                + input.SampleRate.ToString(CultureInfo.InvariantCulture)
                + "|sampleCount="
                + input.PcmSamples.Count.ToString(
                    CultureInfo.InvariantCulture)
                + "|bytes="
                + Convert.ToHexStringLower(bytes),
            ];
        }
        finally
        {
            File.Delete(path);
        }

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
}

internal sealed record WavPcm16BitExactInput(
    int SampleRate,
    IReadOnlyList<int> PcmSamples)
{
    public static WavPcm16BitExactInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        RequireExactProperties(
            input,
            ["scenario", "sampleRate", "pcmSamples"],
            scenario.Id);
        if (input.GetProperty("scenario").GetString()
                != XPlatWavPcm16BitExactTarget.ParityId
            || !input.GetProperty("sampleRate").TryGetInt32(
                out int sampleRate)
            || sampleRate <= 0
            || input.GetProperty("pcmSamples").ValueKind
                != JsonValueKind.Array)
        {
            throw Invalid(scenario.Id);
        }

        int[] pcmSamples = input.GetProperty("pcmSamples")
            .EnumerateArray()
            .Select(value => value.GetInt32())
            .ToArray();
        if (pcmSamples.Length == 0
            || pcmSamples.Any(value => value is < -32767 or > 32767)
            || scenario.ExpectedValues.Count != 1)
        {
            throw Invalid(scenario.Id);
        }

        string prefix = "wav|sampleRate="
            + sampleRate.ToString(CultureInfo.InvariantCulture)
            + "|sampleCount="
            + pcmSamples.Length.ToString(CultureInfo.InvariantCulture)
            + "|bytes=";
        if (!scenario.ExpectedValues[0].StartsWith(
                prefix,
                StringComparison.Ordinal)
            || scenario.ExpectedValues[0].Length <= prefix.Length)
        {
            throw Invalid(scenario.Id);
        }

        return new(sampleRate, pcmSamples);
    }

    private static void RequireExactProperties(
        JsonElement input,
        IReadOnlyList<string> expectedNames,
        string scenarioId)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(scenarioId);
        }

        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal))
        {
            throw Invalid(scenarioId);
        }
    }

    private static InvalidDataException Invalid(string scenarioId) =>
        new($"Parity case '{scenarioId}' fixed vector is invalid.");
}
