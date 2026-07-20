using System.Text.Json;
using System.Text.Json.Nodes;
using MorseRunner.Domain;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatEnterEsmTargetTests
{
    private static readonly string[] LegacyExpectedRows =
    [
        "configuration|scenario=ux.enter-esm-partial-call-message-selection-live"
        + "|contest=scWpx|run-mode=rmPileup|station=W7SST|seed=12345"
        + "|action-count=6",
        "action[0]|id=empty|input=|messages=cq|focus=call"
        + "|question-start=-1|question-length=0|call=|rst=|exchange1="
        + "|call-retained=true|qso-count=0",
        "action[1]|id=short-partial|input=K1|messages=his-call:K1"
        + "|focus=exchange1|question-start=-1|question-length=0|call=K1"
        + "|rst=599|exchange1=|call-retained=true|qso-count=0",
        "action[2]|id=uncertain|input=K1A?|messages=his-call:K1A?"
        + "|focus=call|question-start=3|question-length=1|call=K1A?"
        + "|rst=599|exchange1=|call-retained=true|qso-count=0",
        "action[3]|id=corrected|input=K1ABC"
        + "|messages=his-call:K1ABC,exchange|focus=exchange1"
        + "|question-start=-1|question-length=0|call=K1ABC|rst=599"
        + "|exchange1=|call-retained=true|qso-count=0",
        "action[4]|id=same-call-repeat|input=K1ABC|messages=question"
        + "|focus=exchange1|question-start=-1|question-length=0"
        + "|call=K1ABC|rst=599|exchange1=|call-retained=true"
        + "|qso-count=0",
        "action[5]|id=complete|input=K2XYZ"
        + "|messages=his-call:K2XYZ,exchange|focus=exchange1"
        + "|question-start=-1|question-length=0|call=K2XYZ|rst=599"
        + "|exchange1=|call-retained=true|qso-count=0",
    ];

    private static readonly string[] CurrentXPlatRows =
    [
        "configuration|scenario=ux.enter-esm-partial-call-message-selection-live"
        + "|contest=scWpx|run-mode=rmPileup|station=W7SST|seed=12345"
        + "|action-count=6",
        "action[0]|id=empty|input=|messages=cq|focus=call"
        + "|question-start=-1|question-length=0|call=|rst=5NN"
        + "|exchange1=|call-retained=true|qso-count=0",
        "action[1]|id=short-partial|input=K1|messages=his-call:K1"
        + "|focus=call|question-start=-1|question-length=0|call=K1"
        + "|rst=5NN|exchange1=|call-retained=true|qso-count=0",
        "action[2]|id=uncertain|input=K1A?|messages=his-call:K1A?"
        + "|focus=call|question-start=3|question-length=1|call=K1A?"
        + "|rst=5NN|exchange1=|call-retained=true|qso-count=0",
        "action[3]|id=corrected|input=K1ABC"
        + "|messages=his-call:K1ABC,exchange|focus=exchange1"
        + "|question-start=-1|question-length=0|call=K1ABC|rst=5NN"
        + "|exchange1=|call-retained=true|qso-count=0",
        "action[4]|id=same-call-repeat|input=K1ABC|messages=question"
        + "|focus=exchange1|question-start=-1|question-length=0"
        + "|call=K1ABC|rst=5NN|exchange1=|call-retained=true"
        + "|qso-count=0",
        "action[5]|id=complete|input=K2XYZ"
        + "|messages=his-call:K2XYZ,exchange|focus=exchange1"
        + "|question-start=-1|question-length=0|call=K2XYZ|rst=5NN"
        + "|exchange1=|call-retained=true|qso-count=0",
    ];

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task CurrentXPlatRowsPinFirstFunctionalDivergence()
    {
        ParityScenario scenario = CreateScenario(LegacyExpectedRows);

        ParityObservation observation =
            await new XPlatEnterEsmTarget().ExecuteAsync(
                scenario,
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            XPlatEnterEsmTarget.FunctionalDivergenceCode,
            observation.FailureCode);
        Assert.Equal(
            XPlatEnterEsmTarget.EvidenceSource,
            observation.EvidenceSource);
        Assert.Equal(
            CurrentXPlatRows,
            observation.Values,
            StringComparer.Ordinal);
        Assert.Equal(
            1,
            FindFirstDivergence(
                LegacyExpectedRows,
                observation.Values));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task TargetPassesWheneverObservedRowsEqualExpectedRows()
    {
        ParityScenario scenario = CreateScenario(CurrentXPlatRows);

        ParityObservation observation =
            await new XPlatEnterEsmTarget().ExecuteAsync(
                scenario,
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, observation.Outcome);
        Assert.Null(observation.FailureCode);
        Assert.Equal(
            CurrentXPlatRows,
            observation.Values,
            StringComparer.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task RealEnterRouteCapturesFocusedTextBoxAndSelection()
    {
        ParityObservation observation =
            await new XPlatEnterEsmTarget().ExecuteAsync(
                CreateScenario(CurrentXPlatRows),
                TestContext.Current.CancellationToken);

        Assert.Contains(
            "Avalonia.Headless.MainWindow.KeyPress",
            observation.EvidenceSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "QueuedSemanticMessages",
            observation.EvidenceSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Renderer",
            observation.EvidenceSource,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "Audio",
            observation.EvidenceSource,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "|focus=call|question-start=3|question-length=1|",
            observation.Values[3],
            StringComparison.Ordinal);
        Assert.Contains(
            "|focus=exchange1|question-start=-1|question-length=0|",
            observation.Values[4],
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void MessageNormalizationFailsClosedForImpossibleResults()
    {
        var empty = new EnterEsmActionInput(
            "empty",
            Reset: true,
            Call: String.Empty);
        var corrected = new EnterEsmActionInput(
            "corrected",
            Reset: false,
            Call: "K1ABC");

        Assert.Throws<InvalidOperationException>(
            () => XPlatEnterEsmTarget.NormalizeMessages(
                empty,
                new CommandResult(
                    Accepted: false,
                    ErrorCode: DomainErrorCodes.InvalidSessionState,
                    Message: "Rejected",
                    AppliedRevision: 0,
                    AppliedBlock: 0)));
        Assert.Throws<InvalidOperationException>(
            () => XPlatEnterEsmTarget.NormalizeMessages(
                empty,
                new CommandResult(
                    Accepted: true,
                    ErrorCode: null,
                    Message: null,
                    AppliedRevision: 1,
                    AppliedBlock: 0)));
        Assert.Throws<InvalidOperationException>(
            () => XPlatEnterEsmTarget.NormalizeMessages(
                corrected,
                AcceptedEnter(
                    new EnterSendMessageResult(
                        EnterSendMessageOutcome.SendCallAndExchange,
                        ["K1ABC", "WRONG"],
                        EntryFocusTarget.Exchange1,
                        SelectQuestionMark: false,
                        ClearEntry: false))));
        Assert.Throws<InvalidOperationException>(
            () => XPlatEnterEsmTarget.NormalizeMessages(
                empty,
                AcceptedEnter(
                    new EnterSendMessageResult(
                        EnterSendMessageOutcome.SendCq,
                        [String.Empty],
                        EntryFocusTarget.Call,
                        SelectQuestionMark: false,
                        ClearEntry: false))));
        Assert.Equal(
            "cq",
            XPlatEnterEsmTarget.NormalizeMessages(
                empty,
                AcceptedEnter(
                    new EnterSendMessageResult(
                        EnterSendMessageOutcome.SendCq,
                        ["CQ WPX"],
                        EntryFocusTarget.Call,
                        SelectQuestionMark: false,
                        ClearEntry: false))));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void ParserAcceptsOnlyThePinnedOrderedVector()
    {
        EnterEsmScenarioInput input =
            EnterEsmScenarioInput.Parse(
                CreateScenario(LegacyExpectedRows));

        Assert.Equal(XPlatEnterEsmTarget.ParityId, input.Scenario);
        Assert.Equal("scWpx", input.ContestId);
        Assert.Equal("rmPileup", input.RunModeId);
        Assert.Equal("W7SST", input.StationCall);
        Assert.Equal(12_345, input.Seed);
        Assert.Equal(
            [
                new EnterEsmActionInput(
                    "empty",
                    Reset: true,
                    Call: String.Empty),
                new EnterEsmActionInput(
                    "short-partial",
                    Reset: true,
                    Call: "K1"),
                new EnterEsmActionInput(
                    "uncertain",
                    Reset: true,
                    Call: "K1A?"),
                new EnterEsmActionInput(
                    "corrected",
                    Reset: false,
                    Call: "K1ABC"),
                new EnterEsmActionInput(
                    "same-call-repeat",
                    Reset: false,
                    Call: "K1ABC"),
                new EnterEsmActionInput(
                    "complete",
                    Reset: true,
                    Call: "K2XYZ"),
            ],
            input.Actions);
    }

    [Theory]
    [Trait("Category", "ParityInfrastructure")]
    [InlineData("extra-top-level")]
    [InlineData("missing-top-level")]
    [InlineData("wrong-top-level-type")]
    [InlineData("wrong-seed-type")]
    [InlineData("wrong-action-count")]
    [InlineData("wrong-action-order")]
    [InlineData("wrong-action-reset")]
    [InlineData("wrong-action-call")]
    [InlineData("extra-action-field")]
    [InlineData("non-object-action")]
    [InlineData("wrong-expected-row-count")]
    public void ParserFailsClosedForAnyVectorShapeChange(string mutation)
    {
        JsonObject input = CreateValidInputNode();
        IReadOnlyList<string> expectedRows = LegacyExpectedRows;
        JsonArray actions = input["actions"]!.AsArray();
        switch (mutation)
        {
            case "extra-top-level":
                input["unsupported"] = true;
                break;
            case "missing-top-level":
                input.Remove("stationCall");
                break;
            case "wrong-top-level-type":
                input["contestId"] = 7;
                break;
            case "wrong-seed-type":
                input["seed"] = "12345";
                break;
            case "wrong-action-count":
                actions.RemoveAt(actions.Count - 1);
                break;
            case "wrong-action-order":
                JsonNode? first = actions[0]?.DeepClone();
                actions[0] = actions[1]?.DeepClone();
                actions[1] = first;
                break;
            case "wrong-action-reset":
                actions[3]!["reset"] = true;
                break;
            case "wrong-action-call":
                actions[2]!["call"] = "K1ZZ?";
                break;
            case "extra-action-field":
                actions[0]!["unsupported"] = true;
                break;
            case "non-object-action":
                actions[0] = "empty";
                break;
            case "wrong-expected-row-count":
                expectedRows = LegacyExpectedRows[..^1];
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(mutation),
                    mutation,
                    null);
        }

        ParityScenario scenario = CreateScenario(expectedRows, input);

        Assert.Throws<InvalidDataException>(
            () => EnterEsmScenarioInput.Parse(scenario));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task UnsupportedScenarioDoesNotStartAnXPlatRuntime()
    {
        var scenario = new ParityScenario(
            "ux.some-other-scenario",
            "ux",
            []);

        ParityObservation observation =
            await new XPlatEnterEsmTarget().ExecuteAsync(
                scenario,
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Empty(observation.Values);
        Assert.Equal(
            MorseRunner.Domain.DomainErrorCodes.UnsupportedCapability,
            observation.FailureCode);
    }

    private static int FindFirstDivergence(
        string[] expected,
        IReadOnlyList<string> actual)
    {
        int commonCount = Math.Min(expected.Length, actual.Count);
        for (int index = 0; index < commonCount; index++)
        {
            if (!StringComparer.Ordinal.Equals(
                    expected[index],
                    actual[index]))
            {
                return index;
            }
        }

        return expected.Length == actual.Count ? -1 : commonCount;
    }

    private static ParityScenario CreateScenario(
        IReadOnlyList<string> expectedRows,
        JsonObject? input = null)
    {
        input ??= CreateValidInputNode();
        using JsonDocument document = JsonDocument.Parse(
            input.ToJsonString());
        return new ParityScenario(
            XPlatEnterEsmTarget.ParityId,
            "ux.keyboard-workflows",
            expectedRows,
            document.RootElement);
    }

    private static JsonObject CreateValidInputNode()
    {
        return JsonNode.Parse(
            """
            {
              "scenario": "ux.enter-esm-partial-call-message-selection-live",
              "contestId": "scWpx",
              "runModeId": "rmPileup",
              "stationCall": "W7SST",
              "seed": 12345,
              "actions": [
                {"id": "empty", "reset": true, "call": ""},
                {"id": "short-partial", "reset": true, "call": "K1"},
                {"id": "uncertain", "reset": true, "call": "K1A?"},
                {"id": "corrected", "reset": false, "call": "K1ABC"},
                {
                  "id": "same-call-repeat",
                  "reset": false,
                  "call": "K1ABC"
                },
                {"id": "complete", "reset": true, "call": "K2XYZ"}
              ]
            }
            """)!.AsObject();
    }

    private static CommandResult AcceptedEnter(
        EnterSendMessageResult enter)
    {
        return new CommandResult(
            Accepted: true,
            ErrorCode: null,
            Message: null,
            AppliedRevision: 1,
            AppliedBlock: 0,
            EnterSendMessage: enter);
    }
}
