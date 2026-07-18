using System.Globalization;
using MorseRunner.Domain;
using MorseRunner.Dsp;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatDspTarget : IParityTarget
{
    public Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string[] values = scenario.Id == "audio-dsp.legacy-processing"
            ? ObserveDsp()
            : [];
        bool matches = values.SequenceEqual(
            scenario.ExpectedValues,
            StringComparer.Ordinal);
        return Task.FromResult(
            new ParityObservation(
                matches ? ParityTargetOutcome.Passed : ParityTargetOutcome.Failed,
                values,
                matches ? null : DomainErrorCodes.UnsupportedCapability,
                "MorseRunner.Dsp"));
    }

    private static string[] ObserveDsp()
    {
        var values = new List<string>();
        var keyer = new MorseKeyer(sampleRate: 11_025, blockSize: 512);
        keyer.SetWordsPerMinute(30);
        string morse = MorseKeyer.Encode("CQ TEST");
        values.Add($"morse={morse}");
        float[] envelope = keyer.CreateEnvelope(morse);
        values.Add($"envelope-length={envelope.Length}");
        values.Add($"true-envelope-length={keyer.TrueEnvelopeLength}");
        for (int index = 0; index < 8; index++)
        {
            values.Add(
                $"envelope[{index * 32}]={Format(envelope[index * 32])}");
        }

        float[] input = [1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f];
        var mixed = new ComplexSample[input.Length];
        var mixer = new DownMixer
        {
            SampleRate = 8_000,
            Frequency = 1_000,
        };
        mixer.Mix(input, mixed);
        for (int index = 0; index < mixed.Length; index++)
        {
            values.Add(
                $"downmix[{index}]={Format(mixed[index].Real)},{Format(mixed[index].Imaginary)}");
        }

        var average = new QuickAverage
        {
            Points = 4,
            Passes = 2,
        };
        for (int index = 0; index < 12; index++)
        {
            values.Add(
                $"quick-average[{index}]={Format(average.Filter(index + 1))}");
        }

        return [.. values];
    }

    private static string Format(float value)
    {
        float normalized = value == 0f ? 0f : value;
        return normalized.ToString("F9", CultureInfo.InvariantCulture);
    }
}
