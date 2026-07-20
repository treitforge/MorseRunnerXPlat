using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

internal sealed record QrmCallerCollisionInput(
    int SampleRate,
    int BlockSize,
    int Seed,
    int RetryLimit,
    int CollisionChecks,
    int AcceptedAttempt,
    string StationCall,
    string CollisionCall)
{
    internal const int ExpectedValueCount = 16;

    public static QrmCallerCollisionInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        if (input.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' input is not an object.");
        }

        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        string[] expectedNames =
        [
            "acceptedAttempt",
            "blockSize",
            "collisionCall",
            "collisionChecks",
            "retryLimit",
            "sampleRate",
            "scenario",
            "seed",
            "stationCall",
        ];
        if (!actualNames.SequenceEqual(
                expectedNames,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' input has unsupported "
                + "fields.");
        }

        string discriminator = RequireString(
            input,
            "scenario",
            scenario.Id);
        int sampleRate = RequireInt32(input, "sampleRate", scenario.Id);
        int blockSize = RequireInt32(input, "blockSize", scenario.Id);
        int seed = RequireInt32(input, "seed", scenario.Id);
        int retryLimit = RequireInt32(input, "retryLimit", scenario.Id);
        int collisionChecks = RequireInt32(
            input,
            "collisionChecks",
            scenario.Id);
        int acceptedAttempt = RequireInt32(
            input,
            "acceptedAttempt",
            scenario.Id);
        string stationCall = RequireString(
            input,
            "stationCall",
            scenario.Id);
        string collisionCall = RequireString(
            input,
            "collisionCall",
            scenario.Id);
        if (!StringComparer.Ordinal.Equals(
                discriminator,
                XPlatQrmCallerCollisionTarget.ParityId)
            || sampleRate != 11_025
            || blockSize != 512
            || seed != 24_680
            || retryLimit != 10
            || collisionChecks != 9
            || acceptedAttempt != 10
            || !StringComparer.Ordinal.Equals(stationCall, "W7SST")
            || !StringComparer.Ordinal.Equals(collisionCall, "K1ABC")
            || scenario.ExpectedValues.Count != ExpectedValueCount)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' caller collision input "
                + "is invalid.");
        }

        return new(
            sampleRate,
            blockSize,
            seed,
            retryLimit,
            collisionChecks,
            acceptedAttempt,
            stationCall,
            collisionCall);
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
        if (value.ValueKind != JsonValueKind.String
            || String.IsNullOrEmpty(value.GetString()))
        {
            throw new InvalidDataException(
                $"Parity case '{scenarioId}' {propertyName} is not "
                + "a nonempty string.");
        }

        return value.GetString()!;
    }
}
