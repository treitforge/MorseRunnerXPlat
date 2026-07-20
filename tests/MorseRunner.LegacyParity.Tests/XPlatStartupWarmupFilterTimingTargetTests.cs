using System.Collections.Immutable;
using System.Text.Json;
using MorseRunner.Audio;
using MorseRunner.Domain;
using MorseRunner.Dsp;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatStartupWarmupFilterTimingTargetTests
{
    private const int FirstFullRowIndex = 8;

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task
        CurrentXPlatRowsMatchCePhysicalFramingAndFilterPhase()
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                XPlatStartupWarmupFilterTimingTarget.ParityId);

        ParityObservation observation =
            await new XPlatStartupWarmupFilterTimingTarget()
                .ExecuteAsync(
                    definition.Scenario,
                    TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, observation.Outcome);
        Assert.Null(observation.FailureCode);
        Assert.Equal(
            XPlatStartupWarmupFilterTimingTarget.EvidenceSource,
            observation.EvidenceSource);
        Assert.Contains(
            "PhysicalAudioSink.PrepareDeviceFreeDiagnostics",
            observation.EvidenceSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "PhysicalAudioSink.FillDeviceFreeDiagnostics",
            observation.EvidenceSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "PhysicalAudioSink.FillPlaybackCore",
            observation.EvidenceSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "PhysicalAudioPlaybackCoordinator"
                + ".PresentSynchronousStartupPrefill",
            observation.EvidenceSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "PhysicalAudioPlaybackCoordinator"
                + ".PresentCompletionDrivenStartup",
            observation.EvidenceSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "PhysicalAudioPlaybackCoordinator.FillInterleaved",
            observation.EvidenceSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "MorseRunner.Audio.PhysicalAudioSink.GetDiagnostics",
            observation.EvidenceSource,
            StringComparison.Ordinal);
        Assert.Equal(
            StartupWarmupFilterTimingInput.ExpectedValueCount,
            observation.Values.Count);
        Assert.Equal(
            -1,
            FindFirstDivergence(
                definition.Scenario.ExpectedValues,
                observation.Values));
        Assert.Equal(
            "prefill|request-count=4|absolute-requests=1-4"
                + "|sample-counts=1,1,1,1",
            definition.Scenario.ExpectedValues[1]);
        Assert.Equal(
            definition.Scenario.ExpectedValues[1],
            observation.Values[1]);
        Assert.Equal(
            "warmup|request-count=5|all-one-zero-single=true"
                + "|first-full-absolute-request=6"
                + "|first-full-block-number=6",
            observation.Values[2]);
        Assert.Equal(
            definition.Scenario.ExpectedValues[3],
            observation.Values[3]);
        Assert.Equal(
            definition.Scenario.ExpectedValues
                .Skip(FirstFullRowIndex)
                .Take(16),
            observation.Values
                .Skip(FirstFullRowIndex)
                .Take(16),
            StringComparer.Ordinal);
        Assert.EndsWith(
            "|float-sha256="
                + "5c1ac8a4e8b722df949ace13e7cf1689"
                + "c1e200061f0af23e6a382c00057ddf9c",
            definition.Scenario.ExpectedValues[
                FirstFullRowIndex + 15],
            StringComparison.Ordinal);
        Assert.StartsWith(
            "aggregate|sample-count=8197|",
            observation.Values[^1],
            StringComparison.Ordinal);
        Assert.Equal(
            "1ee1a92146433ae17d508856803b69785"
                + "759d2de39f9c80a1126d46496b7d08e",
            ParityObservedValuesDigest.Compute(observation.Values));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task
        FreshPhysicalSinkReportsNoUnexecutedStartupFraming()
    {
        await using var sink = new PhysicalAudioSink();

        PhysicalAudioSinkDiagnostics diagnostics =
            sink.GetDiagnostics();

        Assert.Equal(
            PhysicalAudioSinkState.Created,
            diagnostics.State);
        Assert.False(diagnostics.StartupFraming.IsDefault);
        Assert.Empty(diagnostics.StartupFraming);
        Assert.Equal(0, diagnostics.CallbackCount);
        Assert.Equal(-1, diagnostics.LastSimulationBlock);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task CurrentProductionObservationMatchesPinnedCeDigest()
    {
        string[] rows =
            await XPlatStartupWarmupFilterTimingTarget.ObserveAsync(
                ValidInput(),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            "prefill|request-count=4|absolute-requests=1-4"
                + "|sample-counts=1,1,1,1",
            rows[1]);
        Assert.Equal(
            "1ee1a92146433ae17d508856803b69785"
                + "759d2de39f9c80a1126d46496b7d08e",
            ParityObservedValuesDigest.Compute(rows));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void
        InjectedStartupFramingChangesRowsAndAggregateFromSamplePayload()
    {
        StartupWarmupFilterTimingInput input = ValidInput();
        XPlatStartupWarmupFilterTimingTarget.CapturedAudioBlock[] blocks =
            Enumerable.Range(0, input.FullBlockCount)
                .Select(
                    index =>
                        new XPlatStartupWarmupFilterTimingTarget
                            .CapturedAudioBlock(
                                index,
                                new float[input.BlockSize]))
                .ToArray();
        ImmutableArray<PhysicalAudioSinkStartupFrame> framing =
            CreateCeStartupFraming(input);
        framing = framing.SetItem(
            framing.Length - 1,
            framing[^1] with
            {
                Samples = ImmutableArray.Create(0.25f),
            });

        string[] emptyRows =
            XPlatStartupWarmupFilterTimingTarget.Normalize(
                input,
                ImmutableArray<PhysicalAudioSinkStartupFrame>.Empty,
                blocks);
        string[] framedRows =
            XPlatStartupWarmupFilterTimingTarget.Normalize(
                input,
                framing,
                blocks);

        Assert.Equal(
            "prefill|request-count=4|absolute-requests=1-4"
                + "|sample-counts=1,1,1,1",
            framedRows[1]);
        Assert.Equal(
            "warmup|request-count=5|all-one-zero-single=false"
                + "|first-full-absolute-request=6"
                + "|first-full-block-number=6",
            framedRows[2]);
        Assert.Equal(
            "startup[4]|absolute-request=5|sample-count=1"
                + "|bits=3e800000|float-sha256="
                + XPlatStartupWarmupFilterTimingTarget
                    .ComputeRawSingleSha256([0.25f]),
            framedRows[7]);
        Assert.StartsWith(
            "aggregate|sample-count=8192|peak=0.000000000",
            emptyRows[^1],
            StringComparison.Ordinal);
        Assert.StartsWith(
            "aggregate|sample-count=8197|peak=0.250000000",
            framedRows[^1],
            StringComparison.Ordinal);
        Assert.NotEqual(emptyRows[^1], framedRows[^1]);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task
        PublicEngineCaptureMatchesCeReceiverPhase()
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                XPlatStartupWarmupFilterTimingTarget.ParityId);
        StartupWarmupFilterTimingInput input =
            StartupWarmupFilterTimingInput.Parse(definition.Scenario);
        XPlatStartupWarmupFilterTimingTarget.CapturedAudioBlock[]
            currentBlocks =
                await XPlatStartupWarmupFilterTimingTarget.CaptureAsync(
                    input,
                    TestContext.Current.CancellationToken);
        XPlatStartupWarmupFilterTimingTarget.CapturedAudioBlock[]
            cePhaseBlocks = RenderReceiverBlocks(
                input,
                input.StartupRequestCount);
        string[] cePhaseRows =
            XPlatStartupWarmupFilterTimingTarget.Normalize(
                input,
                CreateCeStartupFraming(input),
                cePhaseBlocks);

        Assert.Equal(
            definition.Scenario.ExpectedValues,
            cePhaseRows,
            StringComparer.Ordinal);
        for (int blockIndex = 0;
             blockIndex < input.FullBlockCount;
             blockIndex++)
        {
            Assert.Equal(
                currentBlocks[blockIndex].Samples,
                cePhaseBlocks[blockIndex].Samples);
        }

        Assert.Equal(
            "5c1ac8a4e8b722df949ace13e7cf1689"
                + "c1e200061f0af23e6a382c00057ddf9c",
            XPlatStartupWarmupFilterTimingTarget
                .ComputeRawSingleSha256(
                    currentBlocks[^1].Samples));
        Assert.Equal(
            "5c1ac8a4e8b722df949ace13e7cf1689"
                + "c1e200061f0af23e6a382c00057ddf9c",
            XPlatStartupWarmupFilterTimingTarget
                .ComputeRawSingleSha256(
                    cePhaseBlocks[^1].Samples));
        Assert.Equal(
            -1,
            FindFirstSampleDivergence(
                currentBlocks[^1].Samples,
                cePhaseBlocks[^1].Samples));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task PublicEngineCaptureIsDeterministicAndWellFramed()
    {
        StartupWarmupFilterTimingInput input = ValidInput();

        XPlatStartupWarmupFilterTimingTarget.CapturedAudioBlock[] first =
            await XPlatStartupWarmupFilterTimingTarget.CaptureAsync(
                input,
                TestContext.Current.CancellationToken);
        XPlatStartupWarmupFilterTimingTarget.CapturedAudioBlock[] second =
            await XPlatStartupWarmupFilterTimingTarget.CaptureAsync(
                input,
                TestContext.Current.CancellationToken);
        string[] firstRows =
            XPlatStartupWarmupFilterTimingTarget.Normalize(
                input,
                CreateCeStartupFraming(input),
                first);
        string[] secondRows =
            XPlatStartupWarmupFilterTimingTarget.Normalize(
                input,
                CreateCeStartupFraming(input),
                second);

        Assert.Equal(firstRows, secondRows, StringComparer.Ordinal);
        Assert.Equal(
            StartupWarmupFilterTimingInput.ExpectedValueCount,
            firstRows.Length);
        Assert.Equal(
            Enumerable.Range(0, input.FullBlockCount)
                .Select(index => (long)index),
            first.Select(block => block.SimulationBlock));
        Assert.All(
            first,
            block => Assert.Equal(
                input.BlockSize,
                block.Samples.Length));
        Assert.Equal(
            "configuration|sample-rate=11025|block-size=512"
                + "|seed=12345|bandwidth-hz=500|pitch-hz=600"
                + "|prefill-request-count=4"
                + "|startup-request-count=5"
                + "|full-block-count=16"
                + "|first-full-absolute-request=6"
                + "|probe-sample-indexes=0,1,2,3,310,511"
                + "|seed-reset-after-startup=false"
                + "|normalization=ce-single-div-32768-clamp-unit",
            firstRows[0]);
        for (int blockIndex = 0;
             blockIndex < input.FullBlockCount;
             blockIndex++)
        {
            int absoluteRequest =
                input.StartupRequestCount + blockIndex + 1;
            Assert.Matches(
                "^full-block\\[" + blockIndex
                + "\\]\\|absolute-block=" + absoluteRequest
                + "\\|absolute-request=" + absoluteRequest
                + "\\|sample-count=512"
                + "\\|swap-after="
                + (absoluteRequest % 10 == 0 ? "true" : "false")
                + "\\|probe-bits=(?:[0-9a-f]{8},){5}"
                + "[0-9a-f]{8}"
                + "\\|float-sha256=[0-9a-f]{64}$",
                firstRows[FirstFullRowIndex + blockIndex]);
        }
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task TargetPassesOnlyWhenObservedRowsMatch()
    {
        StartupWarmupFilterTimingInput input = ValidInput();
        string[] values =
            await XPlatStartupWarmupFilterTimingTarget.ObserveAsync(
                input,
                TestContext.Current.CancellationToken);
        ParityScenario passingScenario = Scenario(values);

        ParityObservation passed =
            await new XPlatStartupWarmupFilterTimingTarget()
                .ExecuteAsync(
                    passingScenario,
                    TestContext.Current.CancellationToken);
        string[] mutated = [.. values];
        mutated[^1] += "-changed";
        ParityObservation failed =
            await new XPlatStartupWarmupFilterTimingTarget()
                .ExecuteAsync(
                    Scenario(mutated),
                    TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, passed.Outcome);
        Assert.Null(passed.FailureCode);
        Assert.Equal(values, passed.Values, StringComparer.Ordinal);
        Assert.Equal(ParityTargetOutcome.Failed, failed.Outcome);
        Assert.Equal(
            XPlatStartupWarmupFilterTimingTarget
                .FunctionalDivergenceCode,
            failed.FailureCode);
        Assert.Equal(values, failed.Values, StringComparer.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task UnsupportedScenarioFailsWithoutCapture()
    {
        ParityObservation observation =
            await new XPlatStartupWarmupFilterTimingTarget()
                .ExecuteAsync(
                    new ParityScenario(
                        "audio.some-other-case",
                        "audio.dsp",
                        []),
                    TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            DomainErrorCodes.UnsupportedCapability,
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
            () => XPlatStartupWarmupFilterTimingTarget.CaptureAsync(
                ValidInput(),
                cancellation.Token));
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("scenario")]
    [InlineData("sample-rate")]
    [InlineData("prefill-count")]
    [InlineData("startup-count")]
    [InlineData("full-count")]
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
                    StartupWarmupFilterTimingInput.ExpectedValueCount)
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
            case "prefill-count":
                input["prefillRequestCount"] = 8;
                break;
            case "startup-count":
                input["startupRequestCount"] = 4;
                break;
            case "full-count":
                input["fullBlockCount"] = 15;
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
                    XPlatStartupWarmupFilterTimingTarget.ParityId,
                    "audio.dsp",
                    expected,
                    serializedInput);
                StartupWarmupFilterTimingInput.Parse(scenario);
            });
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void NormalizerRejectsInvalidSampleAndFraming()
    {
        StartupWarmupFilterTimingInput input = ValidInput();
        XPlatStartupWarmupFilterTimingTarget.CapturedAudioBlock[] blocks =
            Enumerable.Range(0, input.FullBlockCount)
                .Select(
                    index =>
                        new XPlatStartupWarmupFilterTimingTarget
                            .CapturedAudioBlock(
                                index,
                                new float[input.BlockSize]))
                .ToArray();
        blocks[0].Samples[0] = float.NaN;

        Assert.Throws<InvalidDataException>(
            () => XPlatStartupWarmupFilterTimingTarget.Normalize(
                input,
                ImmutableArray<PhysicalAudioSinkStartupFrame>.Empty,
                blocks));

        blocks[0].Samples[0] = 0f;
        blocks[1] = blocks[1] with
        {
            SimulationBlock = 4,
        };
        Assert.Throws<InvalidDataException>(
            () => XPlatStartupWarmupFilterTimingTarget.Normalize(
                input,
                ImmutableArray<PhysicalAudioSinkStartupFrame>.Empty,
                blocks));
    }

    private static
        ImmutableArray<PhysicalAudioSinkStartupFrame>
        CreateCeStartupFraming(
            StartupWarmupFilterTimingInput input)
    {
        return Enumerable.Range(0, input.StartupRequestCount)
            .Select(
                index => new PhysicalAudioSinkStartupFrame(
                    index + 1,
                    index < input.PrefillRequestCount,
                    ImmutableArray.Create(0f)))
            .ToImmutableArray();
    }

    private static
        XPlatStartupWarmupFilterTimingTarget.CapturedAudioBlock[]
        RenderReceiverBlocks(
            StartupWarmupFilterTimingInput input,
            int initialBlockNumber)
    {
        var pipeline = new LegacyReceiverPipeline(
            input.SampleRate,
            input.BlockSize,
            input.BandwidthHz,
            input.PitchHz,
            initialBlockNumber);

        var random = new LegacyRandom(input.Seed);
        var real = new float[input.BlockSize];
        var imaginary = new float[input.BlockSize];
        var blocks =
            new XPlatStartupWarmupFilterTimingTarget
                .CapturedAudioBlock[input.FullBlockCount];
        for (int blockIndex = 0;
             blockIndex < input.FullBlockCount;
             blockIndex++)
        {
            for (int sampleIndex = 0;
                 sampleIndex < input.BlockSize;
                 sampleIndex++)
            {
                real[sampleIndex] = (float)(
                    18_000d * (random.NextDouble() - 0.5d));
                imaginary[sampleIndex] = (float)(
                    18_000d * (random.NextDouble() - 0.5d));
            }

            var output = new float[input.BlockSize];
            pipeline.Process(real, imaginary, output);
            blocks[blockIndex] = new(blockIndex, output);
        }

        return blocks;
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

    private static int FindFirstSampleDivergence(
        float[] first,
        float[] second)
    {
        Assert.Equal(first.Length, second.Length);
        for (int index = 0; index < first.Length; index++)
        {
            if (BitConverter.SingleToUInt32Bits(first[index])
                != BitConverter.SingleToUInt32Bits(second[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static StartupWarmupFilterTimingInput ValidInput()
    {
        return new(
            SampleRate: 11_025,
            BlockSize: 512,
            Seed: 12_345,
            BandwidthHz: 500,
            PitchHz: 600,
            PrefillRequestCount: 4,
            StartupRequestCount: 5,
            FullBlockCount: 16,
            ProbeSampleIndexes: [0, 1, 2, 3, 310, 511]);
    }

    private static ParityScenario Scenario(
        IReadOnlyList<string> expectedValues)
    {
        return new(
            XPlatStartupWarmupFilterTimingTarget.ParityId,
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
                XPlatStartupWarmupFilterTimingTarget.ParityId,
            ["sampleRate"] = 11_025,
            ["blockSize"] = 512,
            ["seed"] = 12_345,
            ["bandwidthHz"] = 500,
            ["pitchHz"] = 600,
            ["prefillRequestCount"] = 4,
            ["startupRequestCount"] = 5,
            ["fullBlockCount"] = 16,
            ["probeSampleIndexes"] =
                new[] { 0, 1, 2, 3, 310, 511 },
        };
    }
}
