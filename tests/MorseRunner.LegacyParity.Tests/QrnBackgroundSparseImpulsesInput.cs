using System.Collections.Immutable;
using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

internal sealed record QrnBackgroundSparseImpulsesInput(
    int SampleRate,
    int BlockSize,
    int Seed,
    int BandwidthHz,
    int PitchHz,
    int StartupRequestCount,
    int ComparedBlockCount,
    IReadOnlyList<int> ProbeSampleIndexes,
    IReadOnlyList<int> ReplacementSampleIndexes,
    IReadOnlyList<int> TriggerRandomOrdinals,
    IReadOnlyList<int> ReplacementRandomOrdinals,
    int BurstTriggerRandomOrdinal,
    int CleanTerminalRandomOrdinal,
    int QrnTerminalRandomOrdinal)
{
    internal const int ExpectedValueCount = 7;

    private static readonly int[] ExpectedProbeSampleIndexes =
    [
        0,
        91,
        92,
        93,
        248,
        309,
        310,
        311,
        323,
        482,
        488,
        507,
        511,
    ];

    private static readonly int[] ExpectedReplacementSampleIndexes =
    [
        92,
        248,
        323,
        482,
        488,
        507,
    ];

    private static readonly int[] ExpectedTriggerRandomOrdinals =
    [
        1_116,
        1_273,
        1_349,
        1_509,
        1_516,
        1_536,
    ];

    private static readonly int[] ExpectedReplacementRandomOrdinals =
    [
        1_117,
        1_274,
        1_350,
        1_510,
        1_517,
        1_537,
    ];

    public static QrnBackgroundSparseImpulsesInput Parse(
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
                "bandwidthHz",
                "blockSize",
                "burstTriggerRandomOrdinal",
                "cleanTerminalRandomOrdinal",
                "comparedBlockCount",
                "pitchHz",
                "probeSampleIndexes",
                "qrnTerminalRandomOrdinal",
                "replacementRandomOrdinals",
                "replacementSampleIndexes",
                "sampleRate",
                "scenario",
                "seed",
                "startupRequestCount",
                "triggerRandomOrdinals",
            ],
            scenario.Id);

        JsonElement scenarioElement = input.GetProperty("scenario");
        if (scenarioElement.ValueKind != JsonValueKind.String
            || !StringComparer.Ordinal.Equals(
                scenarioElement.GetString(),
                XPlatQrnBackgroundSparseImpulsesTarget.ParityId))
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
        int burstTriggerRandomOrdinal = RequireInt32(
            input,
            "burstTriggerRandomOrdinal",
            scenario.Id);
        int cleanTerminalRandomOrdinal = RequireInt32(
            input,
            "cleanTerminalRandomOrdinal",
            scenario.Id);
        int qrnTerminalRandomOrdinal = RequireInt32(
            input,
            "qrnTerminalRandomOrdinal",
            scenario.Id);
        int[] probeSampleIndexes = RequireInt32Array(
            input,
            "probeSampleIndexes",
            scenario.Id);
        int[] replacementSampleIndexes = RequireInt32Array(
            input,
            "replacementSampleIndexes",
            scenario.Id);
        int[] triggerRandomOrdinals = RequireInt32Array(
            input,
            "triggerRandomOrdinals",
            scenario.Id);
        int[] replacementRandomOrdinals = RequireInt32Array(
            input,
            "replacementRandomOrdinals",
            scenario.Id);

        if (sampleRate != 11_025
            || blockSize != 512
            || seed != 12_345
            || bandwidthHz != 500
            || pitchHz != 600
            || startupRequestCount != 5
            || comparedBlockCount != 1
            || burstTriggerRandomOrdinal != 1_542
            || cleanTerminalRandomOrdinal != 1_024
            || qrnTerminalRandomOrdinal != 1_543
            || !probeSampleIndexes.SequenceEqual(
                ExpectedProbeSampleIndexes)
            || !replacementSampleIndexes.SequenceEqual(
                ExpectedReplacementSampleIndexes)
            || !triggerRandomOrdinals.SequenceEqual(
                ExpectedTriggerRandomOrdinals)
            || !replacementRandomOrdinals.SequenceEqual(
                ExpectedReplacementRandomOrdinals)
            || scenario.ExpectedValues.Count != ExpectedValueCount)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' QRN background input is "
                + "invalid.");
        }

        return new(
            sampleRate,
            blockSize,
            seed,
            bandwidthHz,
            pitchHz,
            startupRequestCount,
            comparedBlockCount,
            probeSampleIndexes.ToImmutableArray(),
            replacementSampleIndexes.ToImmutableArray(),
            triggerRandomOrdinals.ToImmutableArray(),
            replacementRandomOrdinals.ToImmutableArray(),
            burstTriggerRandomOrdinal,
            cleanTerminalRandomOrdinal,
            qrnTerminalRandomOrdinal);
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
