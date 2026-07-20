using System.Collections.Immutable;
using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

internal sealed record RandomPrimitivesInput(
    int Seed,
    int ProbeCount,
    int SequenceCount,
    IReadOnlyList<int> IntegerBounds,
    int GaussianMeanMilli,
    int GaussianLimitMilli,
    int RayleighMeanMilli,
    int PoissonMeanMilli)
{
    internal const int ExpectedValueCount = 80;

    private static readonly int[] ExpectedIntegerBounds =
    [
        0,
        1,
        2,
        3,
        10,
        1_000,
        65_536,
        Int32.MaxValue,
    ];

    public static RandomPrimitivesInput Parse(
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
                "gaussianLimitMilli",
                "gaussianMeanMilli",
                "integerBounds",
                "poissonMeanMilli",
                "probeCount",
                "rayleighMeanMilli",
                "scenario",
                "seed",
                "sequenceCount",
            ],
            scenario.Id);

        JsonElement scenarioElement = input.GetProperty("scenario");
        if (scenarioElement.ValueKind != JsonValueKind.String
            || !StringComparer.Ordinal.Equals(
                scenarioElement.GetString(),
                XPlatRandomPrimitivesTarget.ParityId))
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' scenario discriminator "
                + "is invalid.");
        }

        int seed = RequireInt32(input, "seed", scenario.Id);
        int probeCount = RequireInt32(
            input,
            "probeCount",
            scenario.Id);
        int sequenceCount = RequireInt32(
            input,
            "sequenceCount",
            scenario.Id);
        JsonElement integerBoundsElement =
            input.GetProperty("integerBounds");
        if (integerBoundsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' integerBounds is not "
                + "an array.");
        }

        int[] integerBounds = integerBoundsElement
            .EnumerateArray()
            .Select(
                value => value.ValueKind == JsonValueKind.Number
                    && value.TryGetInt32(out int result)
                        ? result
                        : throw new InvalidDataException(
                            $"Parity case '{scenario.Id}' "
                            + "integerBounds contains a non-Int32 "
                            + "value."))
            .ToArray();
        int gaussianMeanMilli = RequireInt32(
            input,
            "gaussianMeanMilli",
            scenario.Id);
        int gaussianLimitMilli = RequireInt32(
            input,
            "gaussianLimitMilli",
            scenario.Id);
        int rayleighMeanMilli = RequireInt32(
            input,
            "rayleighMeanMilli",
            scenario.Id);
        int poissonMeanMilli = RequireInt32(
            input,
            "poissonMeanMilli",
            scenario.Id);

        if (seed != 12_345
            || probeCount != 8
            || sequenceCount != 4_096
            || !integerBounds.SequenceEqual(ExpectedIntegerBounds)
            || gaussianMeanMilli != 5_100
            || gaussianLimitMilli != 1_300
            || rayleighMeanMilli != 2_700
            || poissonMeanMilli != 3_300
            || scenario.ExpectedValues.Count != ExpectedValueCount)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' random primitive input "
                + "is invalid.");
        }

        return new(
            seed,
            probeCount,
            sequenceCount,
            integerBounds.ToImmutableArray(),
            gaussianMeanMilli,
            gaussianLimitMilli,
            rayleighMeanMilli,
            poissonMeanMilli);
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
