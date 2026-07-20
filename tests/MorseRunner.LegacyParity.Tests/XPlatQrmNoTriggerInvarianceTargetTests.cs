using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatQrmNoTriggerInvarianceTargetTests
{
    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task PublicEngineCaptureMatchesPinnedCeFixture()
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                XPlatQrmNoTriggerInvarianceTarget.ParityId);

        ParityObservation observation =
            await new XPlatQrmNoTriggerInvarianceTarget()
                .ExecuteAsync(
                    definition.Scenario,
                    TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, observation.Outcome);
        Assert.Null(observation.FailureCode);
        Assert.Equal(
            XPlatQrmNoTriggerInvarianceTarget.EvidenceSource,
            observation.EvidenceSource);
        Assert.Equal(
            definition.Scenario.ExpectedValues,
            observation.Values,
            StringComparer.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void PinnedCeFixtureHasExactQrmInvariantRows()
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                XPlatQrmNoTriggerInvarianceTarget.ParityId);

        Assert.Equal(
            QrmNoTriggerInvarianceInput.ExpectedValueCount,
            definition.Scenario.ExpectedValues.Count);
        Assert.Equal(
            "configuration|sample-rate=11025|block-size=512"
                + "|seed=12345|bandwidth-hz=500|pitch-hz=600"
                + "|startup-request-count=5"
                + "|compared-block-count=1"
                + "|probe-sample-indexes=0,1,2,3,310,511"
                + "|fresh-runs=clean,qrm|run-mode=rmStop"
                + "|qsb=false|flutter=false|qrn=false|qsk=false"
                + "|lids=false|operator-transmission=false"
                + "|normal-dx-stations=false"
                + "|normalization=ce-single-div-32768-clamp-unit",
            definition.Scenario.ExpectedValues[0]);
        Assert.Equal(
            "clean-block[0]|sample-count=512"
                + "|probe-bits=00000000,00000000,00000000,"
                + "00000000,b864f231,3b26ef5d"
                + "|float-sha256="
                + "6b468ab13ccc1accb6ec587b8a51d27c"
                + "a23eb80b20bce034106e547ad3565378",
            definition.Scenario.ExpectedValues[1]);
        Assert.Equal(
            definition.Scenario.ExpectedValues[1]
                .Replace(
                    "clean-block",
                    "qrm-block",
                    StringComparison.Ordinal),
            definition.Scenario.ExpectedValues[2]);
        Assert.Equal(
            "station-counts|clean=0|qrm=0",
            definition.Scenario.ExpectedValues[3]);
        Assert.Equal(
            "output-invariance[0]"
                + "|clean-float-sha256="
                + "6b468ab13ccc1accb6ec587b8a51d27c"
                + "a23eb80b20bce034106e547ad3565378"
                + "|qrm-float-sha256="
                + "6b468ab13ccc1accb6ec587b8a51d27c"
                + "a23eb80b20bce034106e547ad3565378"
                + "|exact-equal=true",
            definition.Scenario.ExpectedValues[4]);
        Assert.Equal(
            "174b52c82a01661240a302c8a890d6ad"
                + "705738c66281076a482f30bb30fbe68f",
            ParityObservedValuesDigest.Compute(
                definition.Scenario.ExpectedValues));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task PublicEngineCaptureIsDeterministicAndWellFramed()
    {
        QrmNoTriggerInvarianceInput input = ValidInput();

        string[] first =
            await XPlatQrmNoTriggerInvarianceTarget.ObserveAsync(
                input,
                TestContext.Current.CancellationToken);
        string[] second =
            await XPlatQrmNoTriggerInvarianceTarget.ObserveAsync(
                input,
                TestContext.Current.CancellationToken);

        Assert.Equal(first, second, StringComparer.Ordinal);
        Assert.Equal(
            QrmNoTriggerInvarianceInput.ExpectedValueCount,
            first.Length);
        Assert.Equal(
            "174b52c82a01661240a302c8a890d6ad"
                + "705738c66281076a482f30bb30fbe68f",
            ParityObservedValuesDigest.Compute(first));
        Assert.Equal(
            "6b468ab13ccc1accb6ec587b8a51d27c"
                + "a23eb80b20bce034106e547ad3565378",
            ExtractHash(first[1]));
        Assert.Equal(
            "6b468ab13ccc1accb6ec587b8a51d27c"
                + "a23eb80b20bce034106e547ad3565378",
            ExtractHash(first[2]));
        Assert.Equal(ExtractHash(first[1]), ExtractHash(first[2]));
        Assert.Equal("station-counts|clean=0|qrm=0", first[3]);
        Assert.EndsWith(
            "|exact-equal=true",
            first[4],
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void EqualAndMutatedCapturesDriveExactInvariance()
    {
        QrmNoTriggerInvarianceInput input = ValidInput();
        XPlatQrmNoTriggerInvarianceTarget.CapturedRun clean =
            CreateZeroRun(input);
        XPlatQrmNoTriggerInvarianceTarget.CapturedRun qrm =
            CreateZeroRun(input);

        string[] equal =
            XPlatQrmNoTriggerInvarianceTarget.Normalize(
                input,
                clean,
                qrm);
        qrm.Blocks[0].Samples[310] =
            BitConverter.UInt32BitsToSingle(0x3d00_0000);
        string[] changed =
            XPlatQrmNoTriggerInvarianceTarget.Normalize(
                input,
                clean,
                qrm);

        Assert.EndsWith(
            "|exact-equal=true",
            equal[4],
            StringComparison.Ordinal);
        Assert.EndsWith(
            "|exact-equal=false",
            changed[4],
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task ExactObservationPassesAndMutationIsFunctionalRed()
    {
        QrmNoTriggerInvarianceInput input = ValidInput();
        string[] values =
            await XPlatQrmNoTriggerInvarianceTarget.ObserveAsync(
                input,
                TestContext.Current.CancellationToken);

        ParityObservation passed =
            await new XPlatQrmNoTriggerInvarianceTarget()
                .ExecuteAsync(
                    Scenario(values),
                    TestContext.Current.CancellationToken);
        string[] mutated = [.. values];
        mutated[^1] += "-changed";
        ParityObservation failed =
            await new XPlatQrmNoTriggerInvarianceTarget()
                .ExecuteAsync(
                    Scenario(mutated),
                    TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, passed.Outcome);
        Assert.Null(passed.FailureCode);
        Assert.Equal(values, passed.Values, StringComparer.Ordinal);
        Assert.Equal(ParityTargetOutcome.Failed, failed.Outcome);
        Assert.Equal(
            XPlatQrmNoTriggerInvarianceTarget
                .FunctionalDivergenceCode,
            failed.FailureCode);
        Assert.Equal(values, failed.Values, StringComparer.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task UnsupportedScenarioDoesNotAttemptCapture()
    {
        ParityObservation observation =
            await new XPlatQrmNoTriggerInvarianceTarget()
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
            () => XPlatQrmNoTriggerInvarianceTarget.ObserveAsync(
                ValidInput(),
                cancellation.Token));
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("scenario")]
    [InlineData("sample-rate")]
    [InlineData("startup-count")]
    [InlineData("block-count")]
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
                    QrmNoTriggerInvarianceInput.ExpectedValueCount)
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
                input["comparedBlockCount"] = 3;
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
                    XPlatQrmNoTriggerInvarianceTarget.ParityId,
                    "audio.dsp",
                    expected,
                    serializedInput);
                QrmNoTriggerInvarianceInput.Parse(scenario);
            });
    }

    [Theory]
    [InlineData("nan")]
    [InlineData("infinity")]
    [InlineData("range")]
    [InlineData("block-number")]
    [InlineData("block-count")]
    [InlineData("station-count")]
    [Trait("Category", "ParityInfrastructure")]
    public void NormalizerRejectsInvalidCapture(string mutation)
    {
        QrmNoTriggerInvarianceInput input = ValidInput();
        XPlatQrmNoTriggerInvarianceTarget.CapturedRun clean =
            CreateZeroRun(input);
        XPlatQrmNoTriggerInvarianceTarget.CapturedRun qrm =
            CreateZeroRun(input);
        switch (mutation)
        {
            case "nan":
                clean.Blocks[0].Samples[0] = float.NaN;
                break;
            case "infinity":
                clean.Blocks[0].Samples[0] = float.PositiveInfinity;
                break;
            case "range":
                clean.Blocks[0].Samples[0] = 1.01f;
                break;
            case "block-number":
                clean.Blocks[0] = clean.Blocks[0] with
                {
                    SimulationBlock = 4,
                };
                break;
            case "block-count":
                clean = clean with
                {
                    Blocks = [],
                };
                break;
            case "station-count":
                clean = clean with
                {
                    ActiveStationCount = 1,
                };
                break;
            default:
                throw new InvalidOperationException();
        }

        Assert.Throws<InvalidDataException>(
            () => XPlatQrmNoTriggerInvarianceTarget.Normalize(
                input,
                clean,
                qrm));
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

    private static
        XPlatQrmNoTriggerInvarianceTarget.CapturedRun
        CreateZeroRun(
            QrmNoTriggerInvarianceInput input)
    {
        return new(
            Enumerable.Range(0, input.ComparedBlockCount)
                .Select(
                    index =>
                        new XPlatQrmNoTriggerInvarianceTarget
                            .CapturedAudioBlock(
                                index,
                                new float[input.BlockSize]))
                .ToArray(),
            ActiveStationCount: 0);
    }

    private static QrmNoTriggerInvarianceInput ValidInput()
    {
        return new(
            SampleRate: 11_025,
            BlockSize: 512,
            Seed: 12_345,
            BandwidthHz: 500,
            PitchHz: 600,
            StartupRequestCount: 5,
            ComparedBlockCount: 1,
            ProbeSampleIndexes: [0, 1, 2, 3, 310, 511]);
    }

    private static ParityScenario Scenario(
        IReadOnlyList<string> expectedValues)
    {
        return new(
            XPlatQrmNoTriggerInvarianceTarget.ParityId,
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
                XPlatQrmNoTriggerInvarianceTarget.ParityId,
            ["sampleRate"] = 11_025,
            ["blockSize"] = 512,
            ["seed"] = 12_345,
            ["bandwidthHz"] = 500,
            ["pitchHz"] = 600,
            ["startupRequestCount"] = 5,
            ["comparedBlockCount"] = 1,
            ["probeSampleIndexes"] =
                new[] { 0, 1, 2, 3, 310, 511 },
        };
    }
}
