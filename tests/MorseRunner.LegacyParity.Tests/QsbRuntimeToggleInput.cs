using System.Collections.Immutable;
using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

internal sealed record QsbRuntimeToggleInput(
    int SampleRate,
    int BlockSize,
    int Seed,
    int ComparedBlockCount,
    int ToggleAfterBlockCount,
    string MessageText,
    IReadOnlyList<int> ProbeSampleIndexes)
{
    internal const int ExpectedValueCount = 15;

    private static readonly int[] ExpectedProbeSampleIndexes =
    [
        0,
        1,
        2,
        3,
        310,
        511,
    ];

    public static QsbRuntimeToggleInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        if (input.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' input is not an object.");
        }

        string[] actualProperties = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        string[] expectedProperties =
        [
            "blockSize",
            "comparedBlockCount",
            "messageText",
            "probeSampleIndexes",
            "sampleRate",
            "scenario",
            "seed",
            "toggleAfterBlockCount",
        ];
        if (!actualProperties.SequenceEqual(
                expectedProperties,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' input has unsupported "
                + "fields.");
        }

        string? discriminator = RequireString(
            input,
            "scenario",
            scenario.Id);
        int sampleRate = RequireInt32(input, "sampleRate", scenario.Id);
        int blockSize = RequireInt32(input, "blockSize", scenario.Id);
        int seed = RequireInt32(input, "seed", scenario.Id);
        int comparedBlockCount = RequireInt32(
            input,
            "comparedBlockCount",
            scenario.Id);
        int toggleAfterBlockCount = RequireInt32(
            input,
            "toggleAfterBlockCount",
            scenario.Id);
        string messageText = RequireString(
            input,
            "messageText",
            scenario.Id);
        JsonElement probes = input.GetProperty("probeSampleIndexes");
        if (probes.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' probeSampleIndexes is "
                + "not an array.");
        }

        int[] probeSampleIndexes = probes
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
        if (!StringComparer.Ordinal.Equals(
                discriminator,
                XPlatQsbRuntimeToggleTarget.ParityId)
            || sampleRate != 11_025
            || blockSize != 512
            || seed != 12_345
            || comparedBlockCount != 4
            || toggleAfterBlockCount != 2
            || !StringComparer.Ordinal.Equals(
                messageText,
                "K1ABC K1ABC K1ABC K1ABC "
                + "K1ABC K1ABC K1ABC K1ABC")
            || !probeSampleIndexes.SequenceEqual(
                ExpectedProbeSampleIndexes)
            || scenario.ExpectedValues.Count != ExpectedValueCount)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' QSB runtime-toggle input "
                + "is invalid.");
        }

        return new(
            sampleRate,
            blockSize,
            seed,
            comparedBlockCount,
            toggleAfterBlockCount,
            messageText,
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

    private static string RequireString(
        JsonElement input,
        string propertyName,
        string scenarioId)
    {
        JsonElement value = input.GetProperty(propertyName);
        string? result = value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
        if (String.IsNullOrEmpty(result))
        {
            throw new InvalidDataException(
                $"Parity case '{scenarioId}' {propertyName} is not "
                + "a nonempty string.");
        }

        return result;
    }
}
