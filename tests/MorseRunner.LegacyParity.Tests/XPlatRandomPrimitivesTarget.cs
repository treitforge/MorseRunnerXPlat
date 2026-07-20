using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MorseRunner.Domain;
using MorseRunner.Dsp;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatRandomPrimitivesTarget : IParityTarget
{
    internal const string ParityId =
        "audio.deterministic-random-primitives-seed-12345";
    internal const string FunctionalDivergenceCode =
        "audio-deterministic-random-primitives-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Dsp.LegacyRandom"
        + "+MorseRunner.Dsp.LegacyRandomEffects";

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
                    EvidenceSource));
        }

        RandomPrimitivesInput input =
            RandomPrimitivesInput.Parse(scenario);
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
                EvidenceSource));
    }

    internal static string[] Observe(RandomPrimitivesInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureLittleEndianSingleStorage();

        var values = new List<string>(
            RandomPrimitivesInput.ExpectedValueCount)
        {
            "configuration"
            + $"|seed={Format(input.Seed)}"
            + $"|probe-count={Format(input.ProbeCount)}"
            + $"|sequence-count={Format(input.SequenceCount)}"
            + "|gaussian-mean-milli="
            + Format(input.GaussianMeanMilli)
            + "|gaussian-limit-milli="
            + Format(input.GaussianLimitMilli)
            + "|rayleigh-mean-milli="
            + Format(input.RayleighMeanMilli)
            + "|poisson-mean-milli="
            + Format(input.PoissonMeanMilli)
            + "|integer-bound-count="
            + Format(input.IntegerBounds.Count)
            + "|binary32=ieee-754-little-endian"
            + "|reset=randseed-per-group",
        };

        LegacyRandom random = new(input.Seed);
        var singleSequence = new float[input.SequenceCount];
        for (int index = 0; index < input.SequenceCount; index++)
        {
            singleSequence[index] = random.NextSingle();
            if (index < input.ProbeCount)
            {
                AddFloatValue(
                    values,
                    "raw-random-single",
                    index,
                    singleSequence[index]);
            }
        }

        AddFloatSequenceHash(
            values,
            "raw-random-single",
            singleSequence);
        AddRawSentinel(values, "raw-random-single", random);

        random = new LegacyRandom(input.Seed);
        for (int index = 0;
             index < input.IntegerBounds.Count;
             index++)
        {
            int bound = input.IntegerBounds[index];
            try
            {
                values.Add(
                    $"raw-random-int[{Format(index)}]"
                    + $"|bound={Format(bound)}"
                    + $"|value={Format(random.Next(bound))}");
            }
            catch (ArgumentOutOfRangeException)
            {
                values.Add(
                    $"raw-random-int[{Format(index)}]"
                    + $"|bound={Format(bound)}"
                    + "|error=ArgumentOutOfRangeException");
            }
        }

        AddRawSentinel(values, "raw-random-int", random);

        AddFloatGroup(
            values,
            input,
            "rnd-uniform",
            effects => effects.Uniform());
        AddFloatGroup(
            values,
            input,
            "rnd-ushaped",
            effects => effects.UShaped());
        AddFloatGroup(
            values,
            input,
            "rnd-normal",
            effects => effects.Normal());
        AddFloatGroup(
            values,
            input,
            "rnd-gauss-lim",
            effects => effects.GaussianLimited(
                input.GaussianMeanMilli / 1_000f,
                input.GaussianLimitMilli / 1_000f));
        AddFloatGroup(
            values,
            input,
            "rnd-rayleigh",
            effects => effects.Rayleigh(
                input.RayleighMeanMilli / 1_000f));

        random = new LegacyRandom(input.Seed);
        var poissonEffects = new LegacyRandomEffects(random);
        var integerSequence = new int[input.SequenceCount];
        for (int index = 0; index < input.SequenceCount; index++)
        {
            integerSequence[index] = poissonEffects.Poisson(
                input.PoissonMeanMilli / 1_000f);
            if (index < input.ProbeCount)
            {
                values.Add(
                    $"rnd-poisson[{Format(index)}]"
                    + "|value="
                    + Format(integerSequence[index]));
            }
        }

        values.Add(
            "rnd-poisson"
            + $"|sequence-count={Format(input.SequenceCount)}"
            + "|int32-sha256="
            + ComputeRawSha256(integerSequence.AsSpan()));
        AddRawSentinel(values, "rnd-poisson", random);
        if (values.Count != RandomPrimitivesInput.ExpectedValueCount)
        {
            throw new InvalidDataException(
                "The random primitive capture emitted an invalid row "
                + "count.");
        }

        return [.. values];
    }

    private static void AddFloatGroup(
        List<string> values,
        RandomPrimitivesInput input,
        string groupName,
        Func<LegacyRandomEffects, float> sample)
    {
        var random = new LegacyRandom(input.Seed);
        var effects = new LegacyRandomEffects(random);
        var sequence = new float[input.SequenceCount];
        for (int index = 0; index < input.SequenceCount; index++)
        {
            sequence[index] = sample(effects);
            if (index < input.ProbeCount)
            {
                AddFloatValue(
                    values,
                    groupName,
                    index,
                    sequence[index]);
            }
        }

        AddFloatSequenceHash(values, groupName, sequence);
        AddRawSentinel(values, groupName, random);
    }

    private static void AddFloatSequenceHash(
        List<string> values,
        string groupName,
        float[] sequence)
    {
        values.Add(
            groupName
            + $"|sequence-count={Format(sequence.Length)}"
            + "|float-sha256="
            + ComputeRawSha256(sequence.AsSpan()));
    }

    private static void AddFloatValue(
        List<string> values,
        string groupName,
        int index,
        float value)
    {
        values.Add(
            $"{groupName}[{Format(index)}]"
            + "|bits="
            + BitConverter.SingleToUInt32Bits(value).ToString(
                "x8",
                CultureInfo.InvariantCulture));
    }

    private static void AddRawSentinel(
        List<string> values,
        string groupName,
        LegacyRandom random)
    {
        values.Add(
            groupName
            + "|next-raw-single-bits="
            + BitConverter.SingleToUInt32Bits(
                random.NextSingle()).ToString(
                    "x8",
                    CultureInfo.InvariantCulture));
    }

    private static string Format(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string ComputeRawSha256<T>(
        ReadOnlySpan<T> values)
        where T : unmanaged
    {
        return Convert.ToHexStringLower(
            SHA256.HashData(MemoryMarshal.AsBytes(values)));
    }

    private static void EnsureLittleEndianSingleStorage()
    {
        if (!BitConverter.IsLittleEndian
            || sizeof(float) != sizeof(uint)
            || BitConverter.SingleToUInt32Bits(1f) != 0x3F80_0000U)
        {
            throw new PlatformNotSupportedException(
                "CE random primitive parity requires little-endian "
                + "IEEE-754 binary32 storage.");
        }
    }
}
