using System.Globalization;
using System.Reflection;
using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatDynamicExchangeTypeLocalityTarget : IParityTarget
{
    internal const string ParityId =
        "contest.dynamic-exchange-type-locality-matrix";
    internal const string FunctionalDivergenceCode =
        "contest-dynamic-exchange-type-locality-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Engine.ContestQsoRules.ResolveExchangeTypes";

    private static readonly MethodInfo? ResolveMethod =
        typeof(ContestQsoRules).GetMethod(
            "ResolveExchangeTypes",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

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

        DynamicExchangeTypeLocalityInput input =
            DynamicExchangeTypeLocalityInput.Parse(scenario);
        string[] values = input.Vectors
            .Select(ObserveVector)
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

    private static string ObserveVector(
        DynamicExchangeTypeLocalityVector vector,
        int ordinal)
    {
        ContestId contestId = new(vector.ContestId);
        ExchangeTypes types = ResolveMethod is null
            ? ResolveBaseline(contestId, vector.RequestedMsgType)
            : (ExchangeTypes)(ResolveMethod.Invoke(
                null,
                [
                    contestId,
                    vector.HomeCall,
                    vector.StationKind == "skDxStation",
                    vector.RequestedMsgType == "mtRecvMsg",
                    vector.StationCall,
                    vector.RemoteCall,
                ]) ?? throw new InvalidOperationException(
                    "Dynamic exchange type resolution returned no value."));
        return "vector|ordinal="
            + ordinal.ToString(CultureInfo.InvariantCulture)
            + "|contest=" + vector.ContestId
            + "|homeCall=" + vector.HomeCall
            + "|stationKind=" + vector.StationKind
            + "|requestedMsgType=" + vector.RequestedMsgType
            + "|stationCall=" + vector.StationCall
            + "|remoteCall=" + vector.RemoteCall
            + "|exch1=" + ToCeName(types.First)
            + "|exch2=" + ToCeName(types.Second);
    }

    private static ExchangeTypes ResolveBaseline(
        ContestId contestId,
        string requestedMsgType)
    {
        ContestRules rules = ContestRulesCatalog.Get(contestId);
        return requestedMsgType == "mtRecvMsg"
            ? rules.BaselineReceivedExchangeTypes
            : rules.BaselineSentExchangeTypes;
    }

    private static string ToCeName(ExchangeType1 type) => type switch
    {
        ExchangeType1.Rst => "etRST",
        ExchangeType1.OperatorName => "etOpName",
        _ => throw new InvalidDataException(
            $"Unsupported first exchange type '{type}'."),
    };

    private static string ToCeName(ExchangeType2 type) => type switch
    {
        ExchangeType2.StateProvince => "etStateProv",
        ExchangeType2.Power => "etPower",
        ExchangeType2.NaqpSecondField => "etNaQpExch2",
        ExchangeType2.NaqpNonNorthAmericaSecondField =>
            "etNaQpNonNaExch2",
        _ => throw new InvalidDataException(
            $"Unsupported second exchange type '{type}'."),
    };
}

internal sealed record DynamicExchangeTypeLocalityVector(
    string ContestId,
    string HomeCall,
    string StationKind,
    string RequestedMsgType,
    string StationCall,
    string RemoteCall);

internal sealed record DynamicExchangeTypeLocalityInput(
    IReadOnlyList<DynamicExchangeTypeLocalityVector> Vectors)
{
    private static readonly string[] ExactVectorProperties =
    [
        "contestId",
        "homeCall",
        "remoteCall",
        "requestedMsgType",
        "stationCall",
        "stationKind",
    ];

    public static DynamicExchangeTypeLocalityInput Parse(
        ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        RequireExactProperties(input, ["scenario", "vectors"], scenario.Id);
        if (input.GetProperty("scenario").GetString()
                != XPlatDynamicExchangeTypeLocalityTarget.ParityId
            || input.GetProperty("vectors").ValueKind
                != JsonValueKind.Array)
        {
            throw Invalid(scenario.Id);
        }

        var vectors = new List<DynamicExchangeTypeLocalityVector>();
        foreach (JsonElement value in input.GetProperty("vectors")
                     .EnumerateArray())
        {
            RequireExactProperties(value, ExactVectorProperties, scenario.Id);
            var vector = new DynamicExchangeTypeLocalityVector(
                RequireString(value, "contestId", scenario.Id),
                RequireString(value, "homeCall", scenario.Id),
                RequireString(value, "stationKind", scenario.Id),
                RequireString(value, "requestedMsgType", scenario.Id),
                RequireString(value, "stationCall", scenario.Id),
                RequireString(value, "remoteCall", scenario.Id));
            if (vector.ContestId is not ("scArrlDx" or "scNaQp")
                || vector.StationKind is not (
                    "skMyStation" or "skDxStation")
                || vector.RequestedMsgType is not (
                    "mtSendMsg" or "mtRecvMsg"))
            {
                throw Invalid(scenario.Id);
            }

            vectors.Add(vector);
        }

        if (vectors.Count != 12
            || scenario.ExpectedValues.Count != vectors.Count)
        {
            throw Invalid(scenario.Id);
        }

        for (int index = 0; index < vectors.Count; index++)
        {
            DynamicExchangeTypeLocalityVector vector = vectors[index];
            string prefix = "vector|ordinal="
                + index.ToString(CultureInfo.InvariantCulture)
                + "|contest=" + vector.ContestId
                + "|homeCall=" + vector.HomeCall
                + "|stationKind=" + vector.StationKind
                + "|requestedMsgType=" + vector.RequestedMsgType
                + "|stationCall=" + vector.StationCall
                + "|remoteCall=" + vector.RemoteCall + "|";
            if (!scenario.ExpectedValues[index].StartsWith(
                    prefix,
                    StringComparison.Ordinal))
            {
                throw Invalid(scenario.Id);
            }
        }

        return new(vectors);
    }

    private static string RequireString(
        JsonElement value,
        string propertyName,
        string scenarioId)
    {
        JsonElement property = value.GetProperty(propertyName);
        return property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : throw Invalid(scenarioId);
    }

    private static void RequireExactProperties(
        JsonElement input,
        IReadOnlyList<string> expectedNames,
        string scenarioId)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(scenarioId);
        }

        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal))
        {
            throw Invalid(scenarioId);
        }
    }

    private static InvalidDataException Invalid(string scenarioId) =>
        new($"Parity case '{scenarioId}' fixed vector is invalid.");
}
