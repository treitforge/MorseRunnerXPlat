using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatQsbNoStationNoiseInvarianceTargetTests
{
    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task PublicEngineCaptureMatchesPinnedCeFixture()
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                XPlatQsbNoStationNoiseInvarianceTarget.ParityId);

        ParityObservation observation =
            await new XPlatQsbNoStationNoiseInvarianceTarget()
                .ExecuteAsync(
                    definition.Scenario,
                    TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, observation.Outcome);
        Assert.Null(observation.FailureCode);
        Assert.Equal(
            XPlatQsbNoStationNoiseInvarianceTarget.EvidenceSource,
            observation.EvidenceSource);
        Assert.Equal(
            definition.Scenario.ExpectedValues,
            observation.Values,
            StringComparer.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void PinnedCeFixtureHasExactQsbInvariantRows()
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                XPlatQsbNoStationNoiseInvarianceTarget.ParityId);

        Assert.Equal(
            QsbNoStationNoiseInvarianceInput.ExpectedValueCount,
            definition.Scenario.ExpectedValues.Count);
        Assert.Equal(
            "configuration|sample-rate=11025|block-size=512"
                + "|seed=12345|bandwidth-hz=500|pitch-hz=600"
                + "|startup-request-count=5"
                + "|compared-block-count=2"
                + "|probe-sample-indexes=0,1,2,3,310,511"
                + "|fresh-runs=clean,qsb|station-count=0"
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
                    "qsb-block",
                    StringComparison.Ordinal),
            definition.Scenario.ExpectedValues[3]);
        Assert.Equal(
            "aggregate-invariance|sample-count=1024"
                + "|clean-float-sha256="
                + "19b2cc5f9d3e60b5efbd77cddaf62e91"
                + "030c1d351153341ec8484b93aaa5694d"
                + "|qsb-float-sha256="
                + "19b2cc5f9d3e60b5efbd77cddaf62e91"
                + "030c1d351153341ec8484b93aaa5694d"
                + "|exact-equal=true",
            definition.Scenario.ExpectedValues[^1]);
        Assert.Equal(
            "99e62b99e4a17575a73f9fff75600c75"
                + "30b87dcb28634930d5198ceb2fe709b5",
            ParityObservedValuesDigest.Compute(
                definition.Scenario.ExpectedValues));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task PublicEngineCaptureIsDeterministicAndWellFramed()
    {
        QsbNoStationNoiseInvarianceInput input = ValidInput();

        string[] first =
            await XPlatQsbNoStationNoiseInvarianceTarget.ObserveAsync(
                input,
                TestContext.Current.CancellationToken);
        string[] second =
            await XPlatQsbNoStationNoiseInvarianceTarget.ObserveAsync(
                input,
                TestContext.Current.CancellationToken);

        Assert.Equal(first, second, StringComparer.Ordinal);
        Assert.Equal(
            QsbNoStationNoiseInvarianceInput.ExpectedValueCount,
            first.Length);
        Assert.Equal(
            "99e62b99e4a17575a73f9fff75600c75"
                + "30b87dcb28634930d5198ceb2fe709b5",
            ParityObservedValuesDigest.Compute(first));
        Assert.Equal(
            "6b468ab13ccc1accb6ec587b8a51d27c"
                + "a23eb80b20bce034106e547ad3565378",
            ExtractHash(first[1]));
        Assert.Equal(ExtractHash(first[1]), ExtractHash(first[3]));
        Assert.EndsWith(
            "|exact-equal=true",
            first[5],
            StringComparison.Ordinal);
        Assert.EndsWith(
            "|exact-equal=true",
            first[^1],
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void EqualAndMutatedCapturesDriveExactInvariance()
    {
        QsbNoStationNoiseInvarianceInput input = ValidInput();
        XPlatQsbNoStationNoiseInvarianceTarget.CapturedAudioBlock[]
            clean = CreateZeroBlocks(input);
        XPlatQsbNoStationNoiseInvarianceTarget.CapturedAudioBlock[]
            qsb = CreateZeroBlocks(input);

        string[] equal =
            XPlatQsbNoStationNoiseInvarianceTarget.Normalize(
                input,
                clean,
                qsb);
        qsb[0].Samples[310] =
            BitConverter.UInt32BitsToSingle(0x3d00_0000);
        string[] changed =
            XPlatQsbNoStationNoiseInvarianceTarget.Normalize(
                input,
                clean,
                qsb);

        Assert.EndsWith(
            "|exact-equal=true",
            equal[5],
            StringComparison.Ordinal);
        Assert.EndsWith(
            "|exact-equal=true",
            equal[^1],
            StringComparison.Ordinal);
        Assert.EndsWith(
            "|exact-equal=false",
            changed[5],
            StringComparison.Ordinal);
        Assert.EndsWith(
            "|exact-equal=false",
            changed[^1],
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task ExactObservationPassesAndMutationIsFunctionalRed()
    {
        QsbNoStationNoiseInvarianceInput input = ValidInput();
        string[] values =
            await XPlatQsbNoStationNoiseInvarianceTarget.ObserveAsync(
                input,
                TestContext.Current.CancellationToken);

        ParityObservation passed =
            await new XPlatQsbNoStationNoiseInvarianceTarget()
                .ExecuteAsync(
                    Scenario(values),
                    TestContext.Current.CancellationToken);
        string[] mutated = [.. values];
        mutated[^1] += "-changed";
        ParityObservation failed =
            await new XPlatQsbNoStationNoiseInvarianceTarget()
                .ExecuteAsync(
                    Scenario(mutated),
                    TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, passed.Outcome);
        Assert.Null(passed.FailureCode);
        Assert.Equal(values, passed.Values, StringComparer.Ordinal);
        Assert.Equal(ParityTargetOutcome.Failed, failed.Outcome);
        Assert.Equal(
            XPlatQsbNoStationNoiseInvarianceTarget
                .FunctionalDivergenceCode,
            failed.FailureCode);
        Assert.Equal(values, failed.Values, StringComparer.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task UnsupportedScenarioDoesNotAttemptCapture()
    {
        ParityObservation observation =
            await new XPlatQsbNoStationNoiseInvarianceTarget()
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
            () => XPlatQsbNoStationNoiseInvarianceTarget.ObserveAsync(
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
                    QsbNoStationNoiseInvarianceInput.ExpectedValueCount)
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
                    XPlatQsbNoStationNoiseInvarianceTarget.ParityId,
                    "audio.dsp",
                    expected,
                    serializedInput);
                QsbNoStationNoiseInvarianceInput.Parse(scenario);
            });
    }

    [Theory]
    [InlineData("nan")]
    [InlineData("infinity")]
    [InlineData("range")]
    [InlineData("block-number")]
    [InlineData("block-count")]
    [Trait("Category", "ParityInfrastructure")]
    public void NormalizerRejectsInvalidCapture(string mutation)
    {
        QsbNoStationNoiseInvarianceInput input = ValidInput();
        XPlatQsbNoStationNoiseInvarianceTarget.CapturedAudioBlock[]
            clean = CreateZeroBlocks(input);
        XPlatQsbNoStationNoiseInvarianceTarget.CapturedAudioBlock[]
            qsb = CreateZeroBlocks(input);
        switch (mutation)
        {
            case "nan":
                clean[0].Samples[0] = float.NaN;
                break;
            case "infinity":
                clean[0].Samples[0] = float.PositiveInfinity;
                break;
            case "range":
                clean[0].Samples[0] = 1.01f;
                break;
            case "block-number":
                clean[1] = clean[1] with
                {
                    SimulationBlock = 4,
                };
                break;
            case "block-count":
                clean = [clean[0]];
                break;
            default:
                throw new InvalidOperationException();
        }

        Assert.Throws<InvalidDataException>(
            () => XPlatQsbNoStationNoiseInvarianceTarget.Normalize(
                input,
                clean,
                qsb));
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
        XPlatQsbNoStationNoiseInvarianceTarget.CapturedAudioBlock[]
        CreateZeroBlocks(
            QsbNoStationNoiseInvarianceInput input)
    {
        return Enumerable.Range(0, input.ComparedBlockCount)
            .Select(
                index =>
                    new XPlatQsbNoStationNoiseInvarianceTarget
                        .CapturedAudioBlock(
                            index,
                            new float[input.BlockSize]))
            .ToArray();
    }

    private static QsbNoStationNoiseInvarianceInput ValidInput()
    {
        return new(
            SampleRate: 11_025,
            BlockSize: 512,
            Seed: 12_345,
            BandwidthHz: 500,
            PitchHz: 600,
            StartupRequestCount: 5,
            ComparedBlockCount: 2,
            ProbeSampleIndexes: [0, 1, 2, 3, 310, 511]);
    }

    private static ParityScenario Scenario(
        IReadOnlyList<string> expectedValues)
    {
        return new(
            XPlatQsbNoStationNoiseInvarianceTarget.ParityId,
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
                XPlatQsbNoStationNoiseInvarianceTarget.ParityId,
            ["sampleRate"] = 11_025,
            ["blockSize"] = 512,
            ["seed"] = 12_345,
            ["bandwidthHz"] = 500,
            ["pitchHz"] = 600,
            ["startupRequestCount"] = 5,
            ["comparedBlockCount"] = 2,
            ["probeSampleIndexes"] =
                new[] { 0, 1, 2, 3, 310, 511 },
        };
    }
}
