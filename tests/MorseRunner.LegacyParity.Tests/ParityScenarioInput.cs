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

internal sealed record SstFarnsworthTimingInput(
    int SampleRate,
    int BlockSize,
    int Amplitude,
    int SendingWordsPerMinute,
    int CharacterWordsPerMinute,
    IReadOnlyList<string> Messages)
{
    private static readonly string[] ExpectedMessages =
    [
        "PARIS TEST",
        "K1ABC 599 123",
    ];

    public static SstFarnsworthTimingInput Parse(
        ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        RequireExactProperties(
            input,
            [
                "amplitude",
                "blockSize",
                "characterWpm",
                "messages",
                "sampleRate",
                "scenario",
                "sendingWpm",
            ],
            scenario.Id);

        int sampleRate = RequireInt32(
            input,
            "sampleRate",
            scenario.Id);
        int blockSize = RequireInt32(
            input,
            "blockSize",
            scenario.Id);
        int amplitude = RequireInt32(
            input,
            "amplitude",
            scenario.Id);
        int sendingWordsPerMinute = RequireInt32(
            input,
            "sendingWpm",
            scenario.Id);
        int characterWordsPerMinute = RequireInt32(
            input,
            "characterWpm",
            scenario.Id);
        JsonElement messagesElement = input.GetProperty("messages");
        if (messagesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' messages is not an array.");
        }

        string[] messages = messagesElement
            .EnumerateArray()
            .Select(
                value => value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null)
            .Select(
                value => value
                    ?? throw new InvalidDataException(
                        $"Parity case '{scenario.Id}' messages contains "
                        + "a non-string value."))
            .ToArray();
        if (sampleRate != 11_025
            || blockSize != 512
            || amplitude != 300_000
            || sendingWordsPerMinute != 15
            || characterWordsPerMinute != 25
            || !messages.SequenceEqual(
                ExpectedMessages,
                StringComparer.Ordinal)
            || scenario.ExpectedValues.Count
                != 1 + (5 * messages.Length))
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' SST timing input is invalid.");
        }

        return new SstFarnsworthTimingInput(
            sampleRate,
            blockSize,
            amplitude,
            sendingWordsPerMinute,
            characterWordsPerMinute,
            messages.ToImmutableArray());
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
                $"Parity case '{scenarioId}' input has unsupported fields.");
        }
    }
}
