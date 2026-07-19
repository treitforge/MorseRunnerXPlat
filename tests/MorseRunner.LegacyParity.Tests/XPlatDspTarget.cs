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
        bool matches = ValuesEquivalent(values, scenario.ExpectedValues);
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

        ObserveReceiver(values);
        return [.. values];
    }

    private static void ObserveReceiver(List<string> values)
    {
        var receiver = new LegacyReceiverPipeline(
            sampleRate: 11_025,
            blockSize: 512,
            bandwidthHz: 500,
            requestedCarrierHz: 600);
        var keyer = new MorseKeyer(sampleRate: 11_025, blockSize: 512);
        keyer.SetWordsPerMinute(30);
        float[] envelope = keyer.CreateEnvelope(
            MorseKeyer.Encode("CQ TEST"));
        var real = new float[512];
        var imaginary = new float[512];
        var output = new float[512];
        int[] sampleIndexes = [0, 128, 256, 511];
        double peak = 0d;
        double sumSquares = 0d;
        double carrierCosine = 0d;
        double carrierSine = 0d;
        double requestedCarrierCosine = 0d;
        double requestedCarrierSine = 0d;
        values.Add(
            $"receiver-effective-carrier={Format(receiver.EffectiveCarrierHz)}");
        for (int block = 0; block < 12; block++)
        {
            for (int index = 0; index < 512; index++)
            {
                int globalSample = (block * 512) + index;
                real[index] = (float)(
                    9_000d * Math.Cos(
                        2d * Math.PI * 37d * globalSample / 11_025d));
                imaginary[index] = (float)(
                    -9_000d * Math.Sin(
                        2d * Math.PI * 37d * globalSample / 11_025d));
                if (globalSample < envelope.Length)
                {
                    real[index] += 300_000f * envelope[globalSample];
                    imaginary[index] += 300_000f * envelope[globalSample];
                }
            }

            receiver.ProcessPcm16(real, imaginary, output);
            double blockPeak = 0d;
            for (int index = 0; index < output.Length; index++)
            {
                int globalSample = (block * 512) + index;
                double normalized = output[index] / 32_768d;
                peak = Math.Max(peak, Math.Abs(normalized));
                blockPeak = Math.Max(blockPeak, Math.Abs(normalized));
                sumSquares += normalized * normalized;
                AccumulateCarrier(
                    normalized,
                    globalSample,
                    receiver.EffectiveCarrierHz,
                    ref carrierCosine,
                    ref carrierSine);
                AccumulateCarrier(
                    normalized,
                    globalSample,
                    600f,
                    ref requestedCarrierCosine,
                    ref requestedCarrierSine);
            }

            if (block is 0 or 5 or 11)
            {
                values.Add(
                    $"receiver-agc-peak[{block}]={Format(blockPeak)}");
                foreach (int index in sampleIndexes)
                {
                    values.Add(
                        $"receiver[{block},{index}]="
                        + Format(output[index] / 32_768d));
                }
            }
        }

        values.Add($"receiver-peak={Format(peak)}");
        values.Add(
            $"receiver-active-rms={Format(Math.Sqrt(sumSquares / (12 * 512)))}");
        values.Add(
            "receiver-effective-carrier-correlation="
            + Format(CorrelationMagnitude(carrierCosine, carrierSine)));
        values.Add(
            "receiver-requested-carrier-correlation="
            + Format(
                CorrelationMagnitude(
                    requestedCarrierCosine,
                    requestedCarrierSine)));
    }

    private static void AccumulateCarrier(
        double sample,
        int sampleIndex,
        float requestedCarrierHz,
        ref double cosine,
        ref double sine)
    {
        double phase =
            2d * Math.PI * requestedCarrierHz * sampleIndex / 11_025d;
        cosine += sample * Math.Cos(phase);
        sine += sample * Math.Sin(phase);
    }

    private static double CorrelationMagnitude(double cosine, double sine)
    {
        return 2d * Math.Sqrt((cosine * cosine) + (sine * sine))
            / (12 * 512);
    }

    private static string Format(float value)
    {
        float normalized = value == 0f ? 0f : value;
        return normalized.ToString("F9", CultureInfo.InvariantCulture);
    }

    private static string Format(double value)
    {
        double normalized = value == 0d ? 0d : value;
        return normalized.ToString("F9", CultureInfo.InvariantCulture);
    }

    public static bool ValuesEquivalent(
        IReadOnlyList<string> actual,
        IReadOnlyList<string> expected)
    {
        if (actual.Count != expected.Count)
        {
            return false;
        }

        for (int index = 0; index < actual.Count; index++)
        {
            if (String.Equals(
                    actual[index],
                    expected[index],
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (!actual[index].StartsWith("receiver", StringComparison.Ordinal)
                || !TryReadNumericValue(actual[index], out double actualValue)
                || !TryReadNumericValue(expected[index], out double expectedValue)
                || Math.Abs(actualValue - expectedValue) > 0.000001d)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadNumericValue(
        string value,
        out double numericValue)
    {
        numericValue = 0d;
        int separator = value.LastIndexOf('=');
        return separator >= 0
            && double.TryParse(
                value.AsSpan(separator + 1),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out numericValue);
    }
}
