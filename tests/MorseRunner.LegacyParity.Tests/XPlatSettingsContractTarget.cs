using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Infrastructure;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatSettingsContractTarget : IParityTarget
{
    public Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = new List<string>();
        var covered = new HashSet<(string Section, string Key)>();
        if (scenario.Id == "configuration.persisted-settings")
        {
            foreach (string expected in scenario.ExpectedValues)
            {
                string[] parts = expected.Split('|', count: 3);
                using JsonDocument payload = JsonDocument.Parse(parts[2]);
                string section =
                    payload.RootElement.GetProperty("section").GetString()!;
                string key = payload.RootElement.GetProperty("key").GetString()!;
                if (!LegacySettingSchema.TryGet(
                        section,
                        key,
                        out LegacySettingDescriptor? descriptor)
                    || !OperationsMatch(
                        descriptor!,
                        payload.RootElement.GetProperty("operations")))
                {
                    continue;
                }

                covered.Add((descriptor!.Section, descriptor.Key));
                values.Add(expected);
            }
        }

        bool matches = covered.Count == LegacySettingSchema.All.Count
            && values.SequenceEqual(
                scenario.ExpectedValues,
                StringComparer.Ordinal);
        return Task.FromResult(
            new ParityObservation(
                matches ? ParityTargetOutcome.Passed : ParityTargetOutcome.Failed,
                values,
                matches ? null : DomainErrorCodes.UnsupportedCapability,
                "MorseRunner.Infrastructure"));
    }

    private static bool OperationsMatch(
        LegacySettingDescriptor descriptor,
        JsonElement expected)
    {
        var operations = expected
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToHashSet(StringComparer.Ordinal);
        return operations.Contains("read")
                == descriptor.Operations.HasFlag(LegacySettingOperation.Read)
            && operations.Contains("write")
                == descriptor.Operations.HasFlag(LegacySettingOperation.Write)
            && operations.Contains("delete")
                == descriptor.Operations.HasFlag(LegacySettingOperation.Delete)
            && operations.Contains("exists")
                == descriptor.Operations.HasFlag(LegacySettingOperation.Exists);
    }
}
