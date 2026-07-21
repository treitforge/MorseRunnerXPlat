using System.Globalization;
using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatInvalidOwnExchangeMessagesTarget : IParityTarget
{
    internal const string ParityId =
        "contest.invalid-own-exchange-messages-ce-order";
    internal const string FunctionalDivergenceCode =
        "contest-invalid-own-exchange-message-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Engine.ContestQsoRules.ValidateOwnExchange";

    public Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!StringComparer.Ordinal.Equals(scenario.Id, ParityId))
        {
            return Task.FromResult(
                new ParityObservation(
                    ParityTargetOutcome.Failed,
                    [],
                    DomainErrorCodes.UnsupportedCapability,
                    EvidenceSource));
        }

        _ = InvalidOwnExchangeMessagesInput.Parse(scenario);
        string[] values = ContestCatalog.All
            .Select(ObserveInvalidExchange)
            .ToArray();
        bool matches = values.SequenceEqual(
            scenario.ExpectedValues,
            StringComparer.Ordinal);
        return Task.FromResult(
            new ParityObservation(
                matches
                    ? ParityTargetOutcome.Passed
                    : ParityTargetOutcome.Failed,
                values,
                matches ? null : FunctionalDivergenceCode,
                EvidenceSource));
    }

    private static string ObserveInvalidExchange(
        ContestDefinition definition,
        int ordinal)
    {
        ContestValidation validation = ContestRulesCatalog
            .Get(definition.Id)
            .ValidateMyExchange(string.Empty);
        return "contest|ordinal="
            + ordinal.ToString(CultureInfo.InvariantCulture)
            + "|id=" + definition.Id.Value
            + "|valid=" + validation.IsValid.ToString().ToLowerInvariant()
            + "|error=" + validation.Error;
    }
}

internal sealed record InvalidOwnExchangeMessagesInput
{
    public static InvalidOwnExchangeMessagesInput Parse(
        ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!actualNames.SequenceEqual(["scenario"], StringComparer.Ordinal)
            || input.GetProperty("scenario").GetString()
                != XPlatInvalidOwnExchangeMessagesTarget.ParityId
            || scenario.ExpectedValues.Count != 12)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' fixed vector is invalid.");
        }

        return new InvalidOwnExchangeMessagesInput();
    }
}
