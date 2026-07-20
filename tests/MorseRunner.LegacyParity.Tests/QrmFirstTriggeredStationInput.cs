using System.Collections.Immutable;
using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

internal sealed record QrmFirstTriggeredStationInput(
    int SampleRate,
    int BlockSize,
    int Seed,
    int BandwidthHz,
    int PitchHz,
    int StartupRequestCount,
    int ComparedBlockCount,
    int QrmTriggerRandomOrdinal,
    int CleanTerminalRandomOrdinal,
    int QrmTerminalRandomOrdinal,
    int CallCatalogCount,
    int SelectedCallIndex,
    string MasterDataSha256,
    string StationCall,
    IReadOnlyList<int> ProbeSampleIndexes)
{
    internal const int ExpectedValueCount = 10;

    private const string ExpectedMasterDataSha256 =
        "acf37090e7c9c0f2146a2b08608295cb243c8bfe649a421d1c528a59656097aa";

    private static readonly int[] ExpectedProbeSampleIndexes =
    [
        0,
        1,
        2,
        148,
        149,
        150,
        255,
        310,
        384,
        509,
        510,
        511,
    ];

    public static QrmFirstTriggeredStationInput Parse(
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
                "callCatalogCount",
                "cleanTerminalRandomOrdinal",
                "comparedBlockCount",
                "masterDataSha256",
                "pitchHz",
                "probeSampleIndexes",
                "qrmTerminalRandomOrdinal",
                "qrmTriggerRandomOrdinal",
                "sampleRate",
                "scenario",
                "seed",
                "selectedCallIndex",
                "startupRequestCount",
                "stationCall",
            ],
            scenario.Id);

        JsonElement scenarioElement = input.GetProperty("scenario");
        if (scenarioElement.ValueKind != JsonValueKind.String
            || !StringComparer.Ordinal.Equals(
                scenarioElement.GetString(),
                XPlatQrmFirstTriggeredStationTarget.ParityId))
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
        int qrmTriggerRandomOrdinal = RequireInt32(
            input,
            "qrmTriggerRandomOrdinal",
            scenario.Id);
        int cleanTerminalRandomOrdinal = RequireInt32(
            input,
            "cleanTerminalRandomOrdinal",
            scenario.Id);
        int qrmTerminalRandomOrdinal = RequireInt32(
            input,
            "qrmTerminalRandomOrdinal",
            scenario.Id);
        int callCatalogCount = RequireInt32(
            input,
            "callCatalogCount",
            scenario.Id);
        int selectedCallIndex = RequireInt32(
            input,
            "selectedCallIndex",
            scenario.Id);
        string masterDataSha256 = RequireString(
            input,
            "masterDataSha256",
            scenario.Id);
        string stationCall = RequireString(
            input,
            "stationCall",
            scenario.Id);
        int[] probeSampleIndexes = RequireInt32Array(
            input,
            "probeSampleIndexes",
            scenario.Id);

        if (sampleRate != 11_025
            || blockSize != 512
            || seed != 1_843
            || bandwidthHz != 500
            || pitchHz != 600
            || startupRequestCount != 5
            || comparedBlockCount != 1
            || qrmTriggerRandomOrdinal != 1_024
            || cleanTerminalRandomOrdinal != 1_024
            || qrmTerminalRandomOrdinal != 1_033
            || callCatalogCount != 46_039
            || selectedCallIndex != 23_903
            || !StringComparer.Ordinal.Equals(
                masterDataSha256,
                ExpectedMasterDataSha256)
            || !StringComparer.Ordinal.Equals(stationCall, "W7SST")
            || !probeSampleIndexes.SequenceEqual(
                ExpectedProbeSampleIndexes)
            || scenario.ExpectedValues.Count != ExpectedValueCount)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' positive QRM input is "
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
            qrmTriggerRandomOrdinal,
            cleanTerminalRandomOrdinal,
            qrmTerminalRandomOrdinal,
            callCatalogCount,
            selectedCallIndex,
            masterDataSha256,
            stationCall,
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
        if (value.ValueKind != JsonValueKind.String
            || String.IsNullOrEmpty(value.GetString()))
        {
            throw new InvalidDataException(
                $"Parity case '{scenarioId}' {propertyName} is not "
                + "a nonempty string.");
        }

        return value.GetString()!;
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
