using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Infrastructure;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatDataOperationsTarget : IParityTarget
{
    private static readonly HashSet<string> CoveredOperations =
    [
        "FileExists",
        "LoadFromFile",
        "MessageDlg",
        "raise",
        "SaveToFile",
        "ShowMessage",
        "TFileStream.Create",
        "TIniFile.Create",
    ];

    public Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = new List<string>();
        if (scenario.Id == "data.files-and-operational-paths")
        {
            var catalog = new PackagedDataCatalog();
            foreach (string expected in scenario.ExpectedValues)
            {
                string[] parts = expected.Split('|', count: 3);
                using JsonDocument payload = JsonDocument.Parse(parts[2]);
                bool covered = parts[0].StartsWith(
                    "legacy.data.reference.",
                    StringComparison.Ordinal)
                    ? ReferenceIsCovered(catalog, payload.RootElement)
                    : OperationIsCovered(payload.RootElement);
                if (covered)
                {
                    values.Add(expected);
                }
            }
        }

        bool matches = values.SequenceEqual(
            scenario.ExpectedValues,
            StringComparer.Ordinal);
        return Task.FromResult(
            new ParityObservation(
                matches ? ParityTargetOutcome.Passed : ParityTargetOutcome.Failed,
                values,
                matches ? null : DomainErrorCodes.UnsupportedCapability,
                "MorseRunner.Infrastructure"));
    }

    private static bool ReferenceIsCovered(
        PackagedDataCatalog catalog,
        JsonElement payload)
    {
        string kind = payload.GetProperty("kind").GetString()!;
        if (kind == "generated")
        {
            string reference = payload.GetProperty("reference").GetString()!;
            return reference is ".ini" or ".wav" or "HstResults.txt";
        }

        string asset = payload.GetProperty("asset").GetString()!;
        PackagedDataFile actual = catalog.Describe(asset);
        return actual.Length == payload.GetProperty("bytes").GetInt64()
            && string.Equals(
                actual.Sha256,
                payload.GetProperty("sha256").GetString(),
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool OperationIsCovered(JsonElement payload)
    {
        string operation = payload.GetProperty("operation").GetString()!;
        return CoveredOperations.Contains(operation);
    }
}
