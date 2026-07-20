using System.Collections.Immutable;
using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

internal sealed record StartupWarmupFilterTimingInput(
    int SampleRate,
    int BlockSize,
    int Seed,
    int BandwidthHz,
    int PitchHz,
    int PrefillRequestCount,
    int StartupRequestCount,
    int FullBlockCount,
    IReadOnlyList<int> ProbeSampleIndexes)
{
    internal const int ExpectedValueCount = 25;

    private static readonly int[] ExpectedProbeSampleIndexes =
    [
        0,
        1,
        2,
        3,
        310,
        511,
    ];

    public static StartupWarmupFilterTimingInput Parse(
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
                "fullBlockCount",
                "pitchHz",
                "prefillRequestCount",
                "probeSampleIndexes",
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
                XPlatStartupWarmupFilterTimingTarget.ParityId))
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' scenario discriminator "
                + "is invalid.");
        }

        int sampleRate = RequireInt32(
            input,
            "sampleRate",
            scenario.Id);
        int blockSize = RequireInt32(
            input,
            "blockSize",
            scenario.Id);
        int seed = RequireInt32(
            input,
            "seed",
            scenario.Id);
        int bandwidthHz = RequireInt32(
            input,
            "bandwidthHz",
            scenario.Id);
        int pitchHz = RequireInt32(
            input,
            "pitchHz",
            scenario.Id);
        int prefillRequestCount = RequireInt32(
            input,
            "prefillRequestCount",
            scenario.Id);
        int startupRequestCount = RequireInt32(
            input,
            "startupRequestCount",
            scenario.Id);
        int fullBlockCount = RequireInt32(
            input,
            "fullBlockCount",
            scenario.Id);
        JsonElement probesElement =
            input.GetProperty("probeSampleIndexes");
        if (probesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' probeSampleIndexes is "
                + "not an array.");
        }

        int[] probeSampleIndexes = probesElement
            .EnumerateArray()
            .Select(
                value => value.ValueKind == JsonValueKind.Number
                    && value.TryGetInt32(out int result)
                        ? result
                        : throw new InvalidDataException(
                            $"Parity case '{scenario.Id}' "
                            + "probeSampleIndexes contains a non-Int32 "
                            + "value."))
            .ToArray();
        if (sampleRate != 11_025
            || blockSize != 512
            || seed != 12_345
            || bandwidthHz != 500
            || pitchHz != 600
            || prefillRequestCount != 4
            || startupRequestCount != 5
            || fullBlockCount != 16
            || !probeSampleIndexes.SequenceEqual(
                ExpectedProbeSampleIndexes)
            || scenario.ExpectedValues.Count != ExpectedValueCount)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' startup timing input is "
                + "invalid.");
        }

        return new(
            sampleRate,
            blockSize,
            seed,
            bandwidthHz,
            pitchHz,
            prefillRequestCount,
            startupRequestCount,
            fullBlockCount,
            probeSampleIndexes.ToImmutableArray());
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
