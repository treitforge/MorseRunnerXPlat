using System.Buffers.Binary;
using System.Globalization;
using MorseRunner.Audio;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatAudioAdapterTarget : IParityTarget
{
    public async Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        if (scenario.Id != "audio.legacy-adapters")
        {
            return new(
                ParityTargetOutcome.Failed,
                [],
                DomainErrorCodes.UnsupportedCapability,
                "MorseRunner.Audio");
        }

        string path = Path.Combine(
            Path.GetTempPath(),
            $"morse-runner-parity-{Guid.NewGuid():N}.wav");
        try
        {
            var samples = new float[16];
            for (int index = 0; index < samples.Length; index++)
            {
                samples[index] = ((index - 8) * 1_024f) / 32_768f;
            }

            await using (var sink = new WavAudioSink(path))
            {
                await sink.InitializeAsync(
                    SessionId.New(),
                    AudioStreamFormat.Compatibility,
                    cancellationToken);
                await sink.WriteAsync(
                    samples,
                    simulationBlock: 0,
                    cancellationToken);
                await sink.CompleteAsync(cancellationToken);
            }

            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            string[] values = Observe(bytes, samples.Length);
            bool matches = values.SequenceEqual(
                scenario.ExpectedValues,
                StringComparer.Ordinal);
            return new(
                matches ? ParityTargetOutcome.Passed : ParityTargetOutcome.Failed,
                values,
                matches ? null : DomainErrorCodes.UnsupportedCapability,
                "MorseRunner.Audio.WavAudioSink");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string[] Observe(byte[] bytes, int sampleCount)
    {
        var values = new List<string>
        {
            $"written-bytes={bytes.Length}",
            $"sample-count={sampleCount}",
            $"current-sample={sampleCount}",
        };
        for (int index = 0; index < sampleCount; index++)
        {
            short sample = BinaryPrimitives.ReadInt16LittleEndian(
                bytes.AsSpan(44 + (index * 2), 2));
            values.Add(
                $"sample[{index}]={sample.ToString("F9", CultureInfo.InvariantCulture)}");
        }

        return [.. values];
    }
}
