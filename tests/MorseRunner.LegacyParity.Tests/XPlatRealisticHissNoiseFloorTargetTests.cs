using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatRealisticHissNoiseFloorTargetTests
{
    [Fact]
    public async Task PublicEngineCaptureIsDeterministicAndWellFramed()
    {
        RealisticHissNoiseFloorInput input = ValidInput();

        string[] first =
            await XPlatRealisticHissNoiseFloorTarget.ObserveAsync(
                input,
                TestContext.Current.CancellationToken);
        string[] second =
            await XPlatRealisticHissNoiseFloorTarget.ObserveAsync(
                input,
                TestContext.Current.CancellationToken);

        Assert.Equal(first, second);
        Assert.Equal(
            RealisticHissNoiseFloorInput.ExpectedValueCount,
            first.Length);
        Assert.Equal(
            "configuration|sample-rate=11025|block-size=512"
            + "|seed=12345|bandwidth-hz=500|pitch-hz=600"
            + "|total-blocks=12"
            + "|probe-sample-indexes=0,1,2,3,310,511"
            + "|ce-startup-requests-discarded=5"
            + "|normalization=ce-single-div-32768-clamp-unit",
            first[0]);
        for (int index = 0; index < input.TotalBlocks; index++)
        {
            Assert.Matches(
                "^block\\[" + index
                + "\\]\\|sample-count=512"
                + "\\|probe-bits=(?:[0-9a-f]{8},){5}"
                + "[0-9a-f]{8}"
                + "\\|float-sha256=[0-9a-f]{64}$",
                first[index + 1]);
        }

        Assert.Matches(
            "^aggregate\\|sample-count=6144"
            + "\\|peak=0\\.[0-9]{9}"
            + "\\|rms=0\\.[0-9]{9}"
            + "\\|float-sha256=[0-9a-f]{64}$",
            first[^1]);
        Assert.DoesNotContain(
            "|peak=0.000000000|",
            first[^1],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExactObservationPassesAndMutationIsFunctionalRed()
    {
        RealisticHissNoiseFloorInput input = ValidInput();
        string[] values =
            await XPlatRealisticHissNoiseFloorTarget.ObserveAsync(
                input,
                TestContext.Current.CancellationToken);
        ParityScenario passingScenario = Scenario(values);

        ParityObservation passed =
            await new XPlatRealisticHissNoiseFloorTarget()
                .ExecuteAsync(
                    passingScenario,
                    TestContext.Current.CancellationToken);
        string[] mutated = [.. values];
        mutated[^1] += "-changed";
        ParityObservation failed =
            await new XPlatRealisticHissNoiseFloorTarget()
                .ExecuteAsync(
                    Scenario(mutated),
                    TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, passed.Outcome);
        Assert.Null(passed.FailureCode);
        Assert.Equal(values, passed.Values);
        Assert.Equal(
            XPlatRealisticHissNoiseFloorTarget.EvidenceSource,
            passed.EvidenceSource);
        Assert.Equal(ParityTargetOutcome.Failed, failed.Outcome);
        Assert.Equal(
            XPlatRealisticHissNoiseFloorTarget.FunctionalDivergenceCode,
            failed.FailureCode);
        Assert.Equal(values, failed.Values);
    }

    [Fact]
    public async Task UnsupportedScenarioDoesNotAttemptCapture()
    {
        ParityObservation observation =
            await new XPlatRealisticHissNoiseFloorTarget()
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
    public async Task CancellationIsPropagatedBeforeEngineCreation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => XPlatRealisticHissNoiseFloorTarget.ObserveAsync(
                ValidInput(),
                cancellation.Token));
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("scenario")]
    [InlineData("sample-rate")]
    [InlineData("probe-order")]
    [InlineData("expected-count")]
    [InlineData("non-object")]
    public void InputContractRejectsUnsupportedMutations(
        string mutation)
    {
        Dictionary<string, object> input = ValidInputDocument();
        IReadOnlyList<string> expected =
            Enumerable.Repeat(
                    "placeholder",
                    RealisticHissNoiseFloorInput.ExpectedValueCount)
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
                    XPlatRealisticHissNoiseFloorTarget.ParityId,
                    "audio.dsp",
                    expected,
                    serializedInput);
                RealisticHissNoiseFloorInput.Parse(scenario);
            });
    }

    [Fact]
    public void NormalizerRejectsInvalidSampleAndFraming()
    {
        RealisticHissNoiseFloorInput input = ValidInput();
        XPlatRealisticHissNoiseFloorTarget.CapturedAudioBlock[] blocks =
            Enumerable.Range(0, input.TotalBlocks)
                .Select(
                    index =>
                        new XPlatRealisticHissNoiseFloorTarget
                            .CapturedAudioBlock(
                                index,
                                new float[input.BlockSize]))
                .ToArray();
        blocks[0].Samples[0] = float.NaN;

        Assert.Throws<InvalidDataException>(
            () => XPlatRealisticHissNoiseFloorTarget.Normalize(
                input,
                blocks));

        blocks[0].Samples[0] = 0f;
        blocks[1] = blocks[1] with
        {
            SimulationBlock = 4,
        };
        Assert.Throws<InvalidDataException>(
            () => XPlatRealisticHissNoiseFloorTarget.Normalize(
                input,
                blocks));
    }

    private static RealisticHissNoiseFloorInput ValidInput()
    {
        return new(
            SampleRate: 11_025,
            BlockSize: 512,
            Seed: 12_345,
            BandwidthHz: 500,
            PitchHz: 600,
            TotalBlocks: 12,
            ProbeSampleIndexes: [0, 1, 2, 3, 310, 511]);
    }

    private static ParityScenario Scenario(
        IReadOnlyList<string> expectedValues)
    {
        return new(
            XPlatRealisticHissNoiseFloorTarget.ParityId,
            "audio.dsp",
            expectedValues,
            JsonSerializer.SerializeToElement(
                ValidInputDocument()));
    }

    private static Dictionary<string, object> ValidInputDocument()
    {
        return new(StringComparer.Ordinal)
        {
            ["scenario"] =
                XPlatRealisticHissNoiseFloorTarget.ParityId,
            ["sampleRate"] = 11_025,
            ["blockSize"] = 512,
            ["seed"] = 12_345,
            ["bandwidthHz"] = 500,
            ["pitchHz"] = 600,
            ["totalBlocks"] = 12,
            ["probeSampleIndexes"] =
                new[] { 0, 1, 2, 3, 310, 511 },
        };
    }
}
