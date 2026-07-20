using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatQrnBackgroundSparseImpulsesTargetTests
{
    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task CurrentProductionMatchesPinnedQrnBackgroundVector()
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                XPlatQrnBackgroundSparseImpulsesTarget.ParityId);

        ParityObservation observation =
            await new XPlatQrnBackgroundSparseImpulsesTarget()
                .ExecuteAsync(
                    definition.Scenario,
                    TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, observation.Outcome);
        Assert.Null(observation.FailureCode);
        Assert.Equal(
            XPlatQrnBackgroundSparseImpulsesTarget.EvidenceSource,
            observation.EvidenceSource);
        Assert.Equal(
            definition.Scenario.ExpectedValues,
            observation.Values,
            StringComparer.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void PinnedCeFixtureHasExactQrnRows()
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                XPlatQrnBackgroundSparseImpulsesTarget.ParityId);
        IReadOnlyList<string> values =
            definition.Scenario.ExpectedValues;

        Assert.Equal(
            QrnBackgroundSparseImpulsesInput.ExpectedValueCount,
            values.Count);
        Assert.Equal(
            "qrn-background-decisions"
                + "|replacement-indexes=92,248,323,482,488,507"
                + "|trigger-ordinals=1116,1273,1349,1509,1516,1536"
                + "|trigger-values=0.006017120,0.003656835,"
                + "0.003212208,0.009843730,0.004131156,0.009783321"
                + "|trigger-single-bits=3bc52b42,3b6fa783,"
                + "3b5283e8,3c214799,3b875ea5,3c204a38"
                + "|replacement-value-ordinals="
                + "1117,1274,1350,1510,1517,1537"
                + "|replacement-random-values=0.908031952,"
                + "0.598242246,0.955448369,0.252086627,"
                + "0.718064227,0.062282774"
                + "|replacement-random-single-bits=3f6874c8,"
                + "3f192667,3f749844,3e811180,3f37d30f,3d7f1c39"
                + "|replacement-sample-bits=480f72e0,470a2735,"
                + "48201e5a,c7ae5068,47995390,c819e28d"
                + "|burst-trigger-ordinal=1542"
                + "|burst-trigger-value=0.486590252"
                + "|burst-trigger-single-bits=3ef9225c"
                + "|burst-created=false",
            values[1]);
        Assert.Equal(
            "clean-block[0]|sample-count=512"
                + "|probe-bits=00000000,00000000,00000000,"
                + "00000000,00000000,00000000,b864f231,"
                + "b85bf67c,bb3d3b74,3c8e8f56,bd1d420e,"
                + "bd18929b,3b26ef5d"
                + "|float-sha256="
                + "6b468ab13ccc1accb6ec587b8a51d27c"
                + "a23eb80b20bce034106e547ad3565378",
            values[2]);
        Assert.Equal(
            "qrn-block[0]|sample-count=512"
                + "|probe-bits=00000000,00000000,00000000,"
                + "00000000,00000000,00000000,b84ad349,"
                + "b842ddf4,bb27a47b,3c7cfe47,bd0ba556,"
                + "bd082d00,3b151132"
                + "|float-sha256="
                + "16375b39a2a153bc44f33449bad640084"
                + "f8dbed67c6b8bb9ccb3dec094be5435",
            values[3]);
        Assert.Equal(
            "station-counts|clean=0|qrn=0|burst-created=false",
            values[4]);
        Assert.Equal(
            "terminal-random-sentinels"
                + "|clean-next-ordinal=1024"
                + "|clean-value=0.173840821"
                + "|clean-single-bits=3e320354"
                + "|qrn-next-ordinal=1543"
                + "|qrn-value=0.762713313"
                + "|qrn-single-bits=3f43412e",
            values[5]);
        Assert.Equal(
            "output-difference[0]"
                + "|clean-float-sha256="
                + "6b468ab13ccc1accb6ec587b8a51d27c"
                + "a23eb80b20bce034106e547ad3565378"
                + "|qrn-float-sha256="
                + "16375b39a2a153bc44f33449bad640084"
                + "f8dbed67c6b8bb9ccb3dec094be5435"
                + "|exact-equal=false|first-divergence=310",
            values[6]);
        Assert.Equal(
            "91f28ad40a7bb8f11cf6ca8ab819873"
                + "656239e28e30b0cdda09e4cd8302b86e8",
            ParityObservedValuesDigest.Compute(values));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void SourceOrderReplayMatchesPinnedDecisionRow()
    {
        QrnBackgroundSparseImpulsesInput input = ValidInput();
        XPlatQrnBackgroundSparseImpulsesTarget.QrnDecisionReplay replay =
            XPlatQrnBackgroundSparseImpulsesTarget.ReplayDecisions(
                input);
        var zeros = new float[input.BlockSize];
        var block =
            new XPlatQrnBackgroundSparseImpulsesTarget
                .CapturedAudioBlock(0, zeros);
        var clean =
            new XPlatQrnBackgroundSparseImpulsesTarget.CapturedRun(
                [block],
                0,
                BitConverter.UInt32BitsToSingle(0x3e32_0354));
        var qrn =
            new XPlatQrnBackgroundSparseImpulsesTarget.CapturedRun(
                [
                    new(
                        0,
                        new float[input.BlockSize]),
                ],
                0,
                BitConverter.UInt32BitsToSingle(0x3f43_412e));

        string[] values =
            XPlatQrnBackgroundSparseImpulsesTarget.Normalize(
                input,
                replay,
                clean,
                qrn);

        Assert.Contains(
            "|replacement-indexes=92,248,323,482,488,507|",
            values[1],
            StringComparison.Ordinal);
        Assert.Contains(
            "|burst-trigger-ordinal=1542"
                + "|burst-trigger-value=0.486590252"
                + "|burst-trigger-single-bits=3ef9225c"
                + "|burst-created=false",
            values[1],
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task PublicEngineCaptureIsDeterministicAndWellFramed()
    {
        QrnBackgroundSparseImpulsesInput input = ValidInput();

        string[] first =
            await XPlatQrnBackgroundSparseImpulsesTarget.ObserveAsync(
                input,
                TestContext.Current.CancellationToken);
        string[] second =
            await XPlatQrnBackgroundSparseImpulsesTarget.ObserveAsync(
                input,
                TestContext.Current.CancellationToken);

        Assert.Equal(first, second, StringComparer.Ordinal);
        Assert.Equal(
            QrnBackgroundSparseImpulsesInput.ExpectedValueCount,
            first.Length);
        Assert.StartsWith(
            "clean-block[0]|sample-count=512|",
            first[2],
            StringComparison.Ordinal);
        Assert.StartsWith(
            "qrn-block[0]|sample-count=512|",
            first[3],
            StringComparison.Ordinal);
        Assert.Equal(
            "station-counts|clean=0|qrn=0|burst-created=false",
            first[4]);
        Assert.Contains(
            "|clean-single-bits=3e320354|",
            first[5],
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task ExactObservationPassesAndMutationIsFunctionalRed()
    {
        QrnBackgroundSparseImpulsesInput input = ValidInput();
        string[] values =
            await XPlatQrnBackgroundSparseImpulsesTarget.ObserveAsync(
                input,
                TestContext.Current.CancellationToken);

        ParityObservation passed =
            await new XPlatQrnBackgroundSparseImpulsesTarget()
                .ExecuteAsync(
                    Scenario(values),
                    TestContext.Current.CancellationToken);
        string[] mutated = [.. values];
        mutated[^1] += "-changed";
        ParityObservation failed =
            await new XPlatQrnBackgroundSparseImpulsesTarget()
                .ExecuteAsync(
                    Scenario(mutated),
                    TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, passed.Outcome);
        Assert.Null(passed.FailureCode);
        Assert.Equal(values, passed.Values, StringComparer.Ordinal);
        Assert.Equal(ParityTargetOutcome.Failed, failed.Outcome);
        Assert.Equal(
            XPlatQrnBackgroundSparseImpulsesTarget
                .FunctionalDivergenceCode,
            failed.FailureCode);
        Assert.Equal(values, failed.Values, StringComparer.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task UnsupportedScenarioDoesNotAttemptCapture()
    {
        ParityObservation observation =
            await new XPlatQrnBackgroundSparseImpulsesTarget()
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
            () => XPlatQrnBackgroundSparseImpulsesTarget.ObserveAsync(
                ValidInput(),
                cancellation.Token));
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("scenario")]
    [InlineData("probe-order")]
    [InlineData("replacement")]
    [InlineData("trigger")]
    [InlineData("replacement-ordinal")]
    [InlineData("burst-ordinal")]
    [InlineData("clean-terminal")]
    [InlineData("qrn-terminal")]
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
                    QrnBackgroundSparseImpulsesInput.ExpectedValueCount)
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
            case "probe-order":
                input["probeSampleIndexes"] =
                    new[] { 0, 92, 91, 93, 248, 309, 310, 311,
                        323, 482, 488, 507, 511 };
                break;
            case "replacement":
                input["replacementSampleIndexes"] =
                    new[] { 91, 248, 323, 482, 488, 507 };
                break;
            case "trigger":
                input["triggerRandomOrdinals"] =
                    new[] { 1_115, 1_273, 1_349, 1_509, 1_516, 1_536 };
                break;
            case "replacement-ordinal":
                input["replacementRandomOrdinals"] =
                    new[] { 1_116, 1_274, 1_350, 1_510, 1_517, 1_537 };
                break;
            case "burst-ordinal":
                input["burstTriggerRandomOrdinal"] = 1_541;
                break;
            case "clean-terminal":
                input["cleanTerminalRandomOrdinal"] = 1_023;
                break;
            case "qrn-terminal":
                input["qrnTerminalRandomOrdinal"] = 1_542;
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
                    XPlatQrnBackgroundSparseImpulsesTarget.ParityId,
                    "simulation.legacy-effects",
                    expected,
                    serializedInput);
                QrnBackgroundSparseImpulsesInput.Parse(scenario);
            });
    }

    [Theory]
    [InlineData("nan")]
    [InlineData("range")]
    [InlineData("block-number")]
    [InlineData("block-count")]
    [InlineData("station-count")]
    [InlineData("terminal-nan")]
    [InlineData("terminal-range")]
    [Trait("Category", "ParityInfrastructure")]
    public void NormalizerRejectsInvalidCapture(string mutation)
    {
        QrnBackgroundSparseImpulsesInput input = ValidInput();
        var replay =
            XPlatQrnBackgroundSparseImpulsesTarget.ReplayDecisions(
                input);
        var capture =
            new XPlatQrnBackgroundSparseImpulsesTarget.CapturedRun(
                [
                    new(
                        0,
                        new float[input.BlockSize]),
                ],
                0,
                0.25f);
        switch (mutation)
        {
            case "nan":
                capture.Blocks[0].Samples[0] = float.NaN;
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
            case "terminal-nan":
                capture = capture with
                {
                    TerminalRandom = float.NaN,
                };
                break;
            case "terminal-range":
                capture = capture with
                {
                    TerminalRandom = 1f,
                };
                break;
            default:
                throw new InvalidOperationException();
        }

        Assert.Throws<InvalidDataException>(
            () => XPlatQrnBackgroundSparseImpulsesTarget.Normalize(
                input,
                replay,
                capture,
                new(
                    [
                        new(
                            0,
                            new float[input.BlockSize]),
                    ],
                    0,
                    0.25f)));
    }

    private static QrnBackgroundSparseImpulsesInput ValidInput()
    {
        return new(
            SampleRate: 11_025,
            BlockSize: 512,
            Seed: 12_345,
            BandwidthHz: 500,
            PitchHz: 600,
            StartupRequestCount: 5,
            ComparedBlockCount: 1,
            ProbeSampleIndexes:
                [0, 91, 92, 93, 248, 309, 310, 311, 323, 482, 488,
                    507, 511],
            ReplacementSampleIndexes:
                [92, 248, 323, 482, 488, 507],
            TriggerRandomOrdinals:
                [1_116, 1_273, 1_349, 1_509, 1_516, 1_536],
            ReplacementRandomOrdinals:
                [1_117, 1_274, 1_350, 1_510, 1_517, 1_537],
            BurstTriggerRandomOrdinal: 1_542,
            CleanTerminalRandomOrdinal: 1_024,
            QrnTerminalRandomOrdinal: 1_543);
    }

    private static ParityScenario Scenario(
        IReadOnlyList<string> expectedValues)
    {
        return new(
            XPlatQrnBackgroundSparseImpulsesTarget.ParityId,
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
                XPlatQrnBackgroundSparseImpulsesTarget.ParityId,
            ["sampleRate"] = 11_025,
            ["blockSize"] = 512,
            ["seed"] = 12_345,
            ["bandwidthHz"] = 500,
            ["pitchHz"] = 600,
            ["startupRequestCount"] = 5,
            ["comparedBlockCount"] = 1,
            ["probeSampleIndexes"] =
                new[]
                {
                    0, 91, 92, 93, 248, 309, 310, 311, 323, 482,
                    488, 507, 511,
                },
            ["replacementSampleIndexes"] =
                new[] { 92, 248, 323, 482, 488, 507 },
            ["triggerRandomOrdinals"] =
                new[] { 1_116, 1_273, 1_349, 1_509, 1_516, 1_536 },
            ["replacementRandomOrdinals"] =
                new[] { 1_117, 1_274, 1_350, 1_510, 1_517, 1_537 },
            ["burstTriggerRandomOrdinal"] = 1_542,
            ["cleanTerminalRandomOrdinal"] = 1_024,
            ["qrnTerminalRandomOrdinal"] = 1_543,
        };
    }
}
