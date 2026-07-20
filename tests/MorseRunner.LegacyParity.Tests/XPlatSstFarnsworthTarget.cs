using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MorseRunner.Domain;
using MorseRunner.Dsp;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatSstFarnsworthTarget : IParityTarget
{
    internal const string ParityId =
        "audio.sst-farnsworth-envelope-timing";
    internal const string FunctionalDivergenceCode =
        "audio-sst-farnsworth-timing-mismatch";

    public Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!StringComparer.Ordinal.Equals(scenario.Id, ParityId))
        {
            return Task.FromResult(
                new ParityObservation(
                    ParityTargetOutcome.Failed,
                    [],
                    DomainErrorCodes.UnsupportedCapability,
                    "MorseRunner.Dsp.MorseKeyer"));
        }

        SstFarnsworthTimingInput input =
            SstFarnsworthTimingInput.Parse(scenario);
        string[] values = Observe(input);
        bool matches = values.SequenceEqual(
            scenario.ExpectedValues,
            StringComparer.Ordinal);
        return Task.FromResult(
            new ParityObservation(
                matches
                    ? ParityTargetOutcome.Passed
                    : ParityTargetOutcome.Failed,
                values,
                matches ? null : FunctionalDivergenceCode,
                "MorseRunner.Dsp.MorseKeyer"));
    }

    private static string[] Observe(SstFarnsworthTimingInput input)
    {
        var values = new List<string>(
            1 + (5 * input.Messages.Count))
        {
            "configuration"
            + $"|sample-rate={Format(input.SampleRate)}"
            + $"|block-size={Format(input.BlockSize)}"
            + $"|amplitude={Format(input.Amplitude)}"
            + "|sending-wpm="
            + Format(input.SendingWordsPerMinute)
            + "|character-wpm="
            + Format(input.CharacterWordsPerMinute),
        };

        for (int index = 0; index < input.Messages.Count; index++)
        {
            string message = input.Messages[index];
            var keyer = new MorseKeyer(
                input.SampleRate,
                input.BlockSize);
            keyer.SetWordsPerMinute(
                input.SendingWordsPerMinute,
                input.CharacterWordsPerMinute);
            float[] envelope = keyer.CreateEnvelope(
                MorseKeyer.Encode(message));
            for (int sampleIndex = 0;
                 sampleIndex < envelope.Length;
                 sampleIndex++)
            {
                envelope[sampleIndex] *= input.Amplitude;
            }

            values.Add($"message[{Format(index)}]={message}");
            values.Add(
                $"timing[{Format(index)}]"
                + "|sending-wpm="
                + Format(keyer.SendingWordsPerMinute)
                + "|character-wpm="
                + Format(keyer.CharacterWordsPerMinute)
                + $"|amplitude={Format(input.Amplitude)}");
            values.Add(
                $"true-length[{Format(index)}]="
                + Format(keyer.TrueEnvelopeLength));
            values.Add(
                $"padded-length[{Format(index)}]="
                + Format(envelope.Length));
            values.Add(
                $"float-sha256[{Format(index)}]="
                + ComputeRawSingleSha256(envelope));
        }

        return [.. values];
    }

    private static string ComputeRawSingleSha256(
        ReadOnlySpan<float> samples)
    {
        if (!BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException(
                "CE raw Single parity hashing requires little-endian "
                + "sample storage.");
        }

        return Convert.ToHexStringLower(
            SHA256.HashData(MemoryMarshal.AsBytes(samples)));
    }

    private static string Format(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
