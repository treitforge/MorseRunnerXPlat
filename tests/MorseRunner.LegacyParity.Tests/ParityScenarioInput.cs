using System.Collections.Immutable;
using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

internal static class ParityScenarioInput
{
    public static JsonElement CloneAndValidateDiscriminator(
        string scenarioId,
        JsonElement input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioId);
        if (input.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"Parity scenario '{scenarioId}' input is not an object.");
        }

        JsonProperty[] discriminators = input
            .EnumerateObject()
            .Where(
                property => StringComparer.Ordinal.Equals(
                    property.Name,
                    "scenario"))
            .ToArray();
        if (discriminators.Length != 1
            || discriminators[0].Value.ValueKind
                != JsonValueKind.String
            || !StringComparer.Ordinal.Equals(
                discriminators[0].Value.GetString(),
                scenarioId))
        {
            throw new InvalidDataException(
                $"Parity scenario '{scenarioId}' input discriminator "
                + "does not match.");
        }

        JsonElement clone = input.Clone();
        _ = ParityCanonicalJson.SerializeToUtf8Bytes(clone);
        return clone;
    }

    public static JsonElement CreateDiscriminatorOnly(
        string scenarioId)
    {
        return JsonSerializer.SerializeToElement(
            new Dictionary<string, string>(
                StringComparer.Ordinal)
            {
                ["scenario"] = scenarioId,
            });
    }
}

internal sealed record ContestExchangeShapesInput(
    IReadOnlyList<string> ContestIds)
{
    public static ContestExchangeShapesInput Parse(
        ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        RequireExactProperties(
            input,
            ["contestIds", "scenario"],
            scenario.Id);
        JsonElement contestIdsElement =
            input.GetProperty("contestIds");
        if (contestIdsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' contestIds is not an array.");
        }

        string[] contestIds = contestIdsElement
            .EnumerateArray()
            .Select(
                value => value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null)
            .Where(value => !String.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
        if (contestIds.Length == 0
            || contestIds.Length
                != contestIdsElement.GetArrayLength()
            || contestIds.Distinct(StringComparer.Ordinal).Count()
                != contestIds.Length
            || contestIds.Length
                != scenario.ExpectedValues.Count)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' contest input is invalid.");
        }

        for (int index = 0; index < contestIds.Length; index++)
        {
            if (!scenario.ExpectedValues[index].StartsWith(
                    contestIds[index] + "|",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Parity case '{scenario.Id}' contest input order "
                    + "does not match the fixture rows.");
            }
        }

        return new ContestExchangeShapesInput(
            contestIds.ToImmutableArray());
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
                $"Parity case '{scenarioId}' input has unsupported fields.");
        }
    }
}
