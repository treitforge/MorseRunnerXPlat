using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatReceiverHissSharedRandomCheckpointTargetTests
{
    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task
        CurrentProductionFailsAtSharedRandomCheckpointWithPinnedCode()
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                XPlatReceiverHissSharedRandomCheckpointTarget.ParityId);

        ParityObservation observation =
            await new XPlatReceiverHissSharedRandomCheckpointTarget()
                .ExecuteAsync(
                    definition.Scenario,
                    TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            XPlatReceiverHissSharedRandomCheckpointTarget
                .FunctionalDivergenceCode,
            observation.FailureCode);
        Assert.Equal(
            XPlatReceiverHissSharedRandomCheckpointTarget.EvidenceSource,
            observation.EvidenceSource);
        Assert.Equal(
            definition.Scenario.ExpectedValues.Take(2),
            observation.Values.Take(2),
            StringComparer.Ordinal);
        Assert.Equal(
            2,
            FindFirstDivergence(
                definition.Scenario.ExpectedValues,
                observation.Values));
        Assert.Equal(
            "shared-random-checkpoint"
                + "|draw-count-before-checkpoint=1024"
                + "|ordinal=1024|single-bits=3f6dfb52",
            observation.Values[2]);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void PinnedCeFixtureHasExactSharedRandomRows()
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                XPlatReceiverHissSharedRandomCheckpointTarget.ParityId);

        Assert.Equal(
            ReceiverHissSharedRandomCheckpointInput.ExpectedValueCount,
            definition.Scenario.ExpectedValues.Count);
        Assert.Equal(
            "configuration|sample-rate=11025|block-size=512"
                + "|seed=12345|bandwidth-hz=500|pitch-hz=600"
                + "|startup-request-count=5"
                + "|complete-block-count=1"
                + "|hiss-random-draw-count=1024"
                + "|random-checkpoint-ordinal=1024"
                + "|probe-sample-indexes=0,1,2,3,310,511"
                + "|run-mode=rmStop|qsb=false|flutter=false"
                + "|qrm=false|qrn=false|qsk=false|lids=false"
                + "|operator-transmission=false"
                + "|normal-dx-stations=false"
                + "|normalization=ce-single-div-32768-clamp-unit",
            definition.Scenario.ExpectedValues[0]);
        Assert.Equal(
            "receiver-block[0]|sample-count=512"
                + "|probe-bits=00000000,00000000,00000000,"
                + "00000000,b864f231,3b26ef5d"
                + "|float-sha256="
                + "6b468ab13ccc1accb6ec587b8a51d27c"
                + "a23eb80b20bce034106e547ad3565378",
            definition.Scenario.ExpectedValues[1]);
        Assert.Equal(
            "shared-random-checkpoint"
                + "|draw-count-before-checkpoint=1024"
                + "|ordinal=1024|single-bits=3e320354",
            definition.Scenario.ExpectedValues[2]);
        Assert.Equal(
            "6af5b9552bd37181add531e082c979823"
                + "9fa16df8e5fb3805a57717fc1391060",
            ParityObservedValuesDigest.Compute(
                definition.Scenario.ExpectedValues));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task PublicEngineCaptureIsDeterministicAndWellFramed()
    {
        ReceiverHissSharedRandomCheckpointInput input = ValidInput();

        string[] first =
            await XPlatReceiverHissSharedRandomCheckpointTarget
                .ObserveAsync(
                    input,
                    TestContext.Current.CancellationToken);
        string[] second =
            await XPlatReceiverHissSharedRandomCheckpointTarget
                .ObserveAsync(
                    input,
                    TestContext.Current.CancellationToken);

        Assert.Equal(first, second, StringComparer.Ordinal);
        Assert.Equal(
            ReceiverHissSharedRandomCheckpointInput.ExpectedValueCount,
            first.Length);
        Assert.Equal(
            "aeb163bf9f1e789ec08402edab2afc0"
                + "cfec497442afa3f36770b169617a266c2",
            ParityObservedValuesDigest.Compute(first));
        Assert.Equal(
            "6b468ab13ccc1accb6ec587b8a51d27c"
                + "a23eb80b20bce034106e547ad3565378",
            ExtractHash(first[1]));
        Assert.Equal(
            "shared-random-checkpoint"
                + "|draw-count-before-checkpoint=1024"
                + "|ordinal=1024|single-bits=3f6dfb52",
            first[2]);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void NormalizerPreservesAudioAndCheckpointBits()
    {
        ReceiverHissSharedRandomCheckpointInput input = ValidInput();
        var capture =
            new XPlatReceiverHissSharedRandomCheckpointTarget.CapturedRun(
                [
                    new(
                        0,
                        new float[input.BlockSize]),
                ],
                ActiveStationCount: 0,
                BitConverter.UInt32BitsToSingle(0x3e32_0354));

        string[] values =
            XPlatReceiverHissSharedRandomCheckpointTarget.Normalize(
                input,
                capture);

        Assert.EndsWith(
            "|single-bits=3e320354",
            values[2],
            StringComparison.Ordinal);
        Assert.EndsWith(
            "|float-sha256="
                + "e5a00aa9991ac8a5ee3109844d84a555"
                + "83bd20572ad3ffcd42792f3c36b183ad",
            values[1],
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task ExactObservationPassesAndMutationIsFunctionalRed()
    {
        ReceiverHissSharedRandomCheckpointInput input = ValidInput();
        string[] values =
            await XPlatReceiverHissSharedRandomCheckpointTarget
                .ObserveAsync(
                    input,
                    TestContext.Current.CancellationToken);

        ParityObservation passed =
            await new XPlatReceiverHissSharedRandomCheckpointTarget()
                .ExecuteAsync(
                    Scenario(values),
                    TestContext.Current.CancellationToken);
        string[] mutated = [.. values];
        mutated[^1] += "-changed";
        ParityObservation failed =
            await new XPlatReceiverHissSharedRandomCheckpointTarget()
                .ExecuteAsync(
                    Scenario(mutated),
                    TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, passed.Outcome);
        Assert.Null(passed.FailureCode);
        Assert.Equal(values, passed.Values, StringComparer.Ordinal);
        Assert.Equal(ParityTargetOutcome.Failed, failed.Outcome);
        Assert.Equal(
            XPlatReceiverHissSharedRandomCheckpointTarget
                .FunctionalDivergenceCode,
            failed.FailureCode);
        Assert.Equal(values, failed.Values, StringComparer.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task UnsupportedScenarioDoesNotAttemptCapture()
    {
        ParityObservation observation =
            await new XPlatReceiverHissSharedRandomCheckpointTarget()
                .ExecuteAsync(
                    new ParityScenario(
                        "audio.some-other-case",
                        "audio.dsp",
                        []),
                    TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            MorseRunner.Domain.DomainErrorCodes.UnsupportedCapability,
            observation.FailureCode);
        Assert.Empty(observation.Values);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task CancellationIsPropagatedBeforeEngineCreation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => XPlatReceiverHissSharedRandomCheckpointTarget
                .ObserveAsync(
                    ValidInput(),
                    cancellation.Token));
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("scenario")]
    [InlineData("sample-rate")]
    [InlineData("startup-count")]
    [InlineData("block-count")]
    [InlineData("draw-count")]
    [InlineData("ordinal")]
    [InlineData("probe-order")]
    [InlineData("expected-count")]
    [InlineData("non-object")]
    [Trait("Category", "ParityInfrastructure")]
    public void InputContractRejectsUnsupportedMutations(
        string mutation)
    {
        Dictionary<string, object> input = ValidInputDocument();
        IReadOnlyList<string> expected =
            Enumerable.Repeat(
                    "placeholder",
                    ReceiverHissSharedRandomCheckpointInput
                        .ExpectedValueCount)
                .ToArray();
        bool useNonObjectInput = false;
        switch (mutation)
        {
            case "extra":
                input["unsupported"] = true;
                break;
            case "scenario":
                input["scenario"] = "audio.some-other-case";
                break;
            case "sample-rate":
                input["sampleRate"] = 48_000;
                break;
            case "startup-count":
                input["startupRequestCount"] = 4;
                break;
            case "block-count":
                input["completeBlockCount"] = 2;
                break;
            case "draw-count":
                input["hissRandomDrawCount"] = 1_023;
                break;
            case "ordinal":
                input["randomCheckpointOrdinal"] = 1_023;
                break;
            case "probe-order":
                input["probeSampleIndexes"] =
                    new[] { 1, 0, 2, 3, 310, 511 };
                break;
            case "expected-count":
                expected = expected.Skip(1).ToArray();
                break;
            case "non-object":
                useNonObjectInput = true;
                break;
            default:
                throw new InvalidOperationException();
        }

        JsonElement serializedInput = useNonObjectInput
            ? JsonSerializer.SerializeToElement("not-an-object")
            : JsonSerializer.SerializeToElement(input);

        Assert.Throws<InvalidDataException>(
            () =>
            {
                ParityScenario scenario = new(
                    XPlatReceiverHissSharedRandomCheckpointTarget
                        .ParityId,
                    "audio.dsp",
                    expected,
                    serializedInput);
                ReceiverHissSharedRandomCheckpointInput.Parse(scenario);
            });
    }

    [Theory]
    [InlineData("nan")]
    [InlineData("infinity")]
    [InlineData("range")]
    [InlineData("block-number")]
    [InlineData("block-count")]
    [InlineData("station-count")]
    [InlineData("checkpoint-nan")]
    [InlineData("checkpoint-range")]
    [Trait("Category", "ParityInfrastructure")]
    public void NormalizerRejectsInvalidCapture(string mutation)
    {
        ReceiverHissSharedRandomCheckpointInput input = ValidInput();
        var capture =
            new XPlatReceiverHissSharedRandomCheckpointTarget.CapturedRun(
                [
                    new(
                        0,
                        new float[input.BlockSize]),
                ],
                ActiveStationCount: 0,
                RandomCheckpoint: 0.25f);
        switch (mutation)
        {
            case "nan":
                capture.Blocks[0].Samples[0] = float.NaN;
                break;
            case "infinity":
                capture.Blocks[0].Samples[0] =
                    float.PositiveInfinity;
                break;
            case "range":
                capture.Blocks[0].Samples[0] = 1.01f;
                break;
            case "block-number":
                capture.Blocks[0] = capture.Blocks[0] with
                {
                    SimulationBlock = 4,
                };
                break;
            case "block-count":
                capture = capture with
                {
                    Blocks = [],
                };
                break;
            case "station-count":
                capture = capture with
                {
                    ActiveStationCount = 1,
                };
                break;
            case "checkpoint-nan":
                capture = capture with
                {
                    RandomCheckpoint = float.NaN,
                };
                break;
            case "checkpoint-range":
                capture = capture with
                {
                    RandomCheckpoint = 1f,
                };
                break;
            default:
                throw new InvalidOperationException();
        }

        Assert.Throws<InvalidDataException>(
            () => XPlatReceiverHissSharedRandomCheckpointTarget
                .Normalize(
                    input,
                    capture));
    }

    private static int FindFirstDivergence(
        IReadOnlyList<string> expected,
        IReadOnlyList<string> actual)
    {
        int commonCount = Math.Min(expected.Count, actual.Count);
        for (int index = 0; index < commonCount; index++)
        {
            if (!StringComparer.Ordinal.Equals(
                    expected[index],
                    actual[index]))
            {
                return index;
            }
        }

        return expected.Count == actual.Count ? -1 : commonCount;
    }

    private static string ExtractHash(string row)
    {
        const string marker = "|float-sha256=";
        int markerIndex = row.LastIndexOf(
            marker,
            StringComparison.Ordinal);
        Assert.True(markerIndex >= 0);
        return row[(markerIndex + marker.Length)..];
    }

    private static ReceiverHissSharedRandomCheckpointInput ValidInput()
    {
        return new(
            SampleRate: 11_025,
            BlockSize: 512,
            Seed: 12_345,
            BandwidthHz: 500,
            PitchHz: 600,
            StartupRequestCount: 5,
            CompleteBlockCount: 1,
            HissRandomDrawCount: 1_024,
            RandomCheckpointOrdinal: 1_024,
            ProbeSampleIndexes: [0, 1, 2, 3, 310, 511]);
    }

    private static ParityScenario Scenario(
        IReadOnlyList<string> expectedValues)
    {
        return new(
            XPlatReceiverHissSharedRandomCheckpointTarget.ParityId,
            "simulation.legacy-effects",
            expectedValues,
            JsonSerializer.SerializeToElement(
                ValidInputDocument()));
    }

    private static Dictionary<string, object> ValidInputDocument()
    {
        return new(StringComparer.Ordinal)
        {
            ["scenario"] =
                XPlatReceiverHissSharedRandomCheckpointTarget.ParityId,
            ["sampleRate"] = 11_025,
            ["blockSize"] = 512,
            ["seed"] = 12_345,
            ["bandwidthHz"] = 500,
            ["pitchHz"] = 600,
            ["startupRequestCount"] = 5,
            ["completeBlockCount"] = 1,
            ["hissRandomDrawCount"] = 1_024,
            ["randomCheckpointOrdinal"] = 1_024,
            ["probeSampleIndexes"] =
                new[] { 0, 1, 2, 3, 310, 511 },
        };
    }
}
