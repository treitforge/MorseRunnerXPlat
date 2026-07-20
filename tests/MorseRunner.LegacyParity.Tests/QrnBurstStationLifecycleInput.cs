using System.Collections.Immutable;
using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

internal sealed record QrnBurstStationLifecycleInput(
    int SampleRate,
    int BlockSize,
    int Seed,
    int BandwidthHz,
    int PitchHz,
    int StartupRequestCount,
    int ComparedBlockCount,
    IReadOnlyList<int> Block1ProbeSampleIndexes,
    IReadOnlyList<int> Block2ProbeSampleIndexes,
    IReadOnlyList<int> Block1BackgroundReplacementIndexes,
    IReadOnlyList<int> Block1BackgroundTriggerRandomOrdinals,
    IReadOnlyList<int> Block1BackgroundReplacementRandomOrdinals,
    int Block1BurstTriggerRandomOrdinal,
    int DurationRandomOrdinal,
    int AmplitudeRandomOrdinal,
    int DurationBlocks,
    int DurationSamples,
    IReadOnlyList<int> EnvelopeReplacementIndexes,
    IReadOnlyList<int> EnvelopeTriggerRandomOrdinals,
    IReadOnlyList<int> EnvelopeReplacementRandomOrdinals,
    int Block1TerminalRandomOrdinal,
    IReadOnlyList<int> Block2BackgroundReplacementIndexes,
    IReadOnlyList<int> Block2BackgroundTriggerRandomOrdinals,
    IReadOnlyList<int> Block2BackgroundReplacementRandomOrdinals,
    int Block2BurstTriggerRandomOrdinal,
    int Block2TerminalRandomOrdinal)
{
    internal const int ExpectedValueCount = 9;

    private static readonly int[] ExpectedBlock1ProbeSampleIndexes =
    [
        0,
        153,
        154,
        155,
        359,
        371,
        372,
        373,
        411,
        511,
    ];

    private static readonly int[] ExpectedBlock2ProbeSampleIndexes =
    [
        0,
        64,
        65,
        66,
        116,
        117,
        118,
        239,
        240,
        241,
        335,
        336,
        337,
        395,
        396,
        397,
        478,
        479,
        480,
        511,
    ];

    private static readonly int[]
        ExpectedBlock1BackgroundReplacementIndexes =
        [
            154,
            210,
            245,
            284,
            324,
            341,
            424,
            493,
        ];

    private static readonly int[]
        ExpectedBlock1BackgroundTriggerRandomOrdinals =
        [
            1_178,
            1_235,
            1_271,
            1_311,
            1_352,
            1_370,
            1_454,
            1_524,
        ];

    private static readonly int[]
        ExpectedBlock1BackgroundReplacementRandomOrdinals =
        [
            1_179,
            1_236,
            1_272,
            1_312,
            1_353,
            1_371,
            1_455,
            1_525,
        ];

    private static readonly int[] ExpectedEnvelopeReplacementIndexes =
    [
        359,
        411,
        848,
        907,
        990,
    ];

    private static readonly int[] ExpectedEnvelopeTriggerRandomOrdinals =
    [
        1_906,
        1_959,
        2_397,
        2_457,
        2_541,
    ];

    private static readonly int[]
        ExpectedEnvelopeReplacementRandomOrdinals =
        [
            1_907,
            1_960,
            2_398,
            2_458,
            2_542,
        ];

    private static readonly int[]
        ExpectedBlock2BackgroundReplacementIndexes =
        [
            22,
            146,
            233,
            297,
        ];

    private static readonly int[]
        ExpectedBlock2BackgroundTriggerRandomOrdinals =
        [
            3_622,
            3_747,
            3_835,
            3_900,
        ];

    private static readonly int[]
        ExpectedBlock2BackgroundReplacementRandomOrdinals =
        [
            3_623,
            3_748,
            3_836,
            3_901,
        ];

    public static QrnBurstStationLifecycleInput Parse(
        ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        if (input.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' input is not an object.");
        }

        RequireExactProperties(
            input,
            [
                "amplitudeRandomOrdinal",
                "bandwidthHz",
                "block1BackgroundReplacementIndexes",
                "block1BackgroundReplacementRandomOrdinals",
                "block1BackgroundTriggerRandomOrdinals",
                "block1BurstTriggerRandomOrdinal",
                "block1ProbeSampleIndexes",
                "block1TerminalRandomOrdinal",
                "block2BackgroundReplacementIndexes",
                "block2BackgroundReplacementRandomOrdinals",
                "block2BackgroundTriggerRandomOrdinals",
                "block2BurstTriggerRandomOrdinal",
                "block2ProbeSampleIndexes",
                "block2TerminalRandomOrdinal",
                "blockSize",
                "comparedBlockCount",
                "durationBlocks",
                "durationRandomOrdinal",
                "durationSamples",
                "envelopeReplacementIndexes",
                "envelopeReplacementRandomOrdinals",
                "envelopeTriggerRandomOrdinals",
                "pitchHz",
                "sampleRate",
                "scenario",
                "seed",
                "startupRequestCount",
            ],
            scenario.Id);

        JsonElement scenarioElement = input.GetProperty("scenario");
        if (scenarioElement.ValueKind != JsonValueKind.String
            || !StringComparer.Ordinal.Equals(
                scenarioElement.GetString(),
                XPlatQrnBurstStationLifecycleTarget.ParityId))
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' scenario discriminator "
                + "is invalid.");
        }

        int sampleRate = RequireInt32(input, "sampleRate", scenario.Id);
        int blockSize = RequireInt32(input, "blockSize", scenario.Id);
        int seed = RequireInt32(input, "seed", scenario.Id);
        int bandwidthHz = RequireInt32(
            input,
            "bandwidthHz",
            scenario.Id);
        int pitchHz = RequireInt32(input, "pitchHz", scenario.Id);
        int startupRequestCount = RequireInt32(
            input,
            "startupRequestCount",
            scenario.Id);
        int comparedBlockCount = RequireInt32(
            input,
            "comparedBlockCount",
            scenario.Id);
        int[] block1ProbeSampleIndexes = RequireInt32Array(
            input,
            "block1ProbeSampleIndexes",
            scenario.Id);
        int[] block2ProbeSampleIndexes = RequireInt32Array(
            input,
            "block2ProbeSampleIndexes",
            scenario.Id);
        int[] block1BackgroundReplacementIndexes = RequireInt32Array(
            input,
            "block1BackgroundReplacementIndexes",
            scenario.Id);
        int[] block1BackgroundTriggerRandomOrdinals =
            RequireInt32Array(
                input,
                "block1BackgroundTriggerRandomOrdinals",
                scenario.Id);
        int[] block1BackgroundReplacementRandomOrdinals =
            RequireInt32Array(
                input,
                "block1BackgroundReplacementRandomOrdinals",
                scenario.Id);
        int block1BurstTriggerRandomOrdinal = RequireInt32(
            input,
            "block1BurstTriggerRandomOrdinal",
            scenario.Id);
        int durationRandomOrdinal = RequireInt32(
            input,
            "durationRandomOrdinal",
            scenario.Id);
        int amplitudeRandomOrdinal = RequireInt32(
            input,
            "amplitudeRandomOrdinal",
            scenario.Id);
        int durationBlocks = RequireInt32(
            input,
            "durationBlocks",
            scenario.Id);
        int durationSamples = RequireInt32(
            input,
            "durationSamples",
            scenario.Id);
        int[] envelopeReplacementIndexes = RequireInt32Array(
            input,
            "envelopeReplacementIndexes",
            scenario.Id);
        int[] envelopeTriggerRandomOrdinals = RequireInt32Array(
            input,
            "envelopeTriggerRandomOrdinals",
            scenario.Id);
        int[] envelopeReplacementRandomOrdinals = RequireInt32Array(
            input,
            "envelopeReplacementRandomOrdinals",
            scenario.Id);
        int block1TerminalRandomOrdinal = RequireInt32(
            input,
            "block1TerminalRandomOrdinal",
            scenario.Id);
        int[] block2BackgroundReplacementIndexes = RequireInt32Array(
            input,
            "block2BackgroundReplacementIndexes",
            scenario.Id);
        int[] block2BackgroundTriggerRandomOrdinals =
            RequireInt32Array(
                input,
                "block2BackgroundTriggerRandomOrdinals",
                scenario.Id);
        int[] block2BackgroundReplacementRandomOrdinals =
            RequireInt32Array(
                input,
                "block2BackgroundReplacementRandomOrdinals",
                scenario.Id);
        int block2BurstTriggerRandomOrdinal = RequireInt32(
            input,
            "block2BurstTriggerRandomOrdinal",
            scenario.Id);
        int block2TerminalRandomOrdinal = RequireInt32(
            input,
            "block2TerminalRandomOrdinal",
            scenario.Id);

        if (sampleRate != 11_025
            || blockSize != 512
            || seed != 1_903
            || bandwidthHz != 500
            || pitchHz != 600
            || startupRequestCount != 5
            || comparedBlockCount != 2
            || !block1ProbeSampleIndexes.SequenceEqual(
                ExpectedBlock1ProbeSampleIndexes)
            || !block2ProbeSampleIndexes.SequenceEqual(
                ExpectedBlock2ProbeSampleIndexes)
            || !block1BackgroundReplacementIndexes.SequenceEqual(
                ExpectedBlock1BackgroundReplacementIndexes)
            || !block1BackgroundTriggerRandomOrdinals.SequenceEqual(
                ExpectedBlock1BackgroundTriggerRandomOrdinals)
            || !block1BackgroundReplacementRandomOrdinals
                .SequenceEqual(
                    ExpectedBlock1BackgroundReplacementRandomOrdinals)
            || block1BurstTriggerRandomOrdinal != 1_544
            || durationRandomOrdinal != 1_545
            || amplitudeRandomOrdinal != 1_546
            || durationBlocks != 2
            || durationSamples != 1_024
            || !envelopeReplacementIndexes.SequenceEqual(
                ExpectedEnvelopeReplacementIndexes)
            || !envelopeTriggerRandomOrdinals.SequenceEqual(
                ExpectedEnvelopeTriggerRandomOrdinals)
            || !envelopeReplacementRandomOrdinals.SequenceEqual(
                ExpectedEnvelopeReplacementRandomOrdinals)
            || block1TerminalRandomOrdinal != 2_576
            || !block2BackgroundReplacementIndexes.SequenceEqual(
                ExpectedBlock2BackgroundReplacementIndexes)
            || !block2BackgroundTriggerRandomOrdinals.SequenceEqual(
                ExpectedBlock2BackgroundTriggerRandomOrdinals)
            || !block2BackgroundReplacementRandomOrdinals
                .SequenceEqual(
                    ExpectedBlock2BackgroundReplacementRandomOrdinals)
            || block2BurstTriggerRandomOrdinal != 4_116
            || block2TerminalRandomOrdinal != 4_117
            || scenario.ExpectedValues.Count != ExpectedValueCount)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' QRN burst lifecycle "
                + "input is invalid.");
        }

        return new(
            sampleRate,
            blockSize,
            seed,
            bandwidthHz,
            pitchHz,
            startupRequestCount,
            comparedBlockCount,
            block1ProbeSampleIndexes.ToImmutableArray(),
            block2ProbeSampleIndexes.ToImmutableArray(),
            block1BackgroundReplacementIndexes.ToImmutableArray(),
            block1BackgroundTriggerRandomOrdinals.ToImmutableArray(),
            block1BackgroundReplacementRandomOrdinals
                .ToImmutableArray(),
            block1BurstTriggerRandomOrdinal,
            durationRandomOrdinal,
            amplitudeRandomOrdinal,
            durationBlocks,
            durationSamples,
            envelopeReplacementIndexes.ToImmutableArray(),
            envelopeTriggerRandomOrdinals.ToImmutableArray(),
            envelopeReplacementRandomOrdinals.ToImmutableArray(),
            block1TerminalRandomOrdinal,
            block2BackgroundReplacementIndexes.ToImmutableArray(),
            block2BackgroundTriggerRandomOrdinals.ToImmutableArray(),
            block2BackgroundReplacementRandomOrdinals
                .ToImmutableArray(),
            block2BurstTriggerRandomOrdinal,
            block2TerminalRandomOrdinal);
    }

    private static int RequireInt32(
        JsonElement input,
        string propertyName,
        string scenarioId)
    {
        JsonElement value = input.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out int result))
        {
            throw new InvalidDataException(
                $"Parity case '{scenarioId}' {propertyName} is not "
                + "an Int32.");
        }

        return result;
    }

    private static int[] RequireInt32Array(
        JsonElement input,
        string propertyName,
        string scenarioId)
    {
        JsonElement value = input.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"Parity case '{scenarioId}' {propertyName} is not "
                + "an array.");
        }

        return value
            .EnumerateArray()
            .Select(
                item => item.ValueKind == JsonValueKind.Number
                    && item.TryGetInt32(out int result)
                        ? result
                        : throw new InvalidDataException(
                            $"Parity case '{scenarioId}' "
                            + $"{propertyName} contains a non-Int32 "
                            + "value."))
            .ToArray();
    }

    private static void RequireExactProperties(
        JsonElement input,
        IReadOnlyList<string> expectedPropertyNames,
        string scenarioId)
    {
        string[] actualPropertyNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!actualPropertyNames.SequenceEqual(
                expectedPropertyNames,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Parity case '{scenarioId}' input has unsupported "
                + "fields.");
        }
    }
}
