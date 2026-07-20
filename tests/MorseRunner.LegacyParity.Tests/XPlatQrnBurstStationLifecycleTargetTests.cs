using System.Text.Json;
using System.Text.Json.Nodes;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatQrnBurstStationLifecycleTargetTests
{
    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void PinnedCeFixtureCapturesBurstLifecycleAndAudio()
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                XPlatQrnBurstStationLifecycleTarget.ParityId);
        IReadOnlyList<string> values =
            definition.Scenario.ExpectedValues;

        Assert.Equal(
            QrnBurstStationLifecycleInput.ExpectedValueCount,
            values.Count);
        Assert.Equal(
            "station-lifecycle"
                + "|after-block1-count=1"
                + "|after-block1-class=TQrnStation"
                + "|after-block1-state=stSending"
                + "|after-block1-envelope-samples=1024"
                + "|after-block2-count=0",
            values[3]);
        Assert.Equal(
            "qrn-block[0]|sample-count=512"
                + "|probe-bits=00000000,00000000,00000000,"
                + "00000000,3cb5c57e,3b79a5de,b91a2574,"
                + "bbac638e,3cd95db3,bc207385"
                + "|float-sha256="
                + "41096894caafa0890ea1f5d18545aa14"
                + "b644ed1e9f20aff31f9f8fcec75d960e",
            values[4]);
        Assert.Equal(
            "qrn-block[1]|sample-count=512"
                + "|probe-bits=3b12f9d7,be0e5e8f,bdf56141,"
                + "bdae544c,bc9e1282,bc303a21,ba80878d,"
                + "bc8ba2e0,bc8edea3,bb3e6fb9,bbd51650,"
                + "bb9c961e,ba992db7,3ce55712,3c97227d,"
                + "3bed16f0,3d330c61,3d4c8375,3d405e54,"
                + "bc71723d"
                + "|float-sha256="
                + "44ae49f68a99e7688200685231b9cbf6"
                + "c84414ae8a6b1b4736b78cce1d7d4637",
            values[6]);
        Assert.Equal(
            "terminal-random-sentinels"
                + "|one-block-next-ordinal=2576"
                + "|one-block-value=0.846895993"
                + "|one-block-single-bits=3f58ce2d"
                + "|two-block-next-ordinal=4117"
                + "|two-block-value=0.312355429"
                + "|two-block-single-bits=3e9fed0d",
            values[7]);
        Assert.Equal(
            "two-block-output|sample-count=1024"
                + "|float-sha256="
                + "bec466358e35bf3720c074c9fee0ea8e"
                + "7ef8f656164b6dd48d5758da66c41f61",
            values[8]);
        Assert.Equal(
            "68bca62538b923e7a58610a533b1ff99"
                + "9cd2cf1b4b4c84392cda0e398b274646",
            ParityObservedValuesDigest.Compute(values));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void SourceOrderReplayPinsEagerBurstConstructor()
    {
        QrnBurstStationLifecycleInput input = ValidInput();

        XPlatQrnBurstStationLifecycleTarget.BurstReplay replay =
            XPlatQrnBurstStationLifecycleTarget.ReplayDecisions(input);

        Assert.True(replay.Block1Background.BurstCreated);
        Assert.Equal(
            [154, 210, 245, 284, 324, 341, 424, 493],
            replay.Block1Background.ReplacementSampleIndexes);
        Assert.Equal(1_544, replay.Block1Background.BurstTriggerOrdinal);
        Assert.Equal(1_545, replay.Constructor.DurationRandomOrdinal);
        Assert.Equal(2, replay.Constructor.DurationBlocks);
        Assert.Equal(1_024, replay.Constructor.DurationSamples);
        Assert.Equal(1_546, replay.Constructor.AmplitudeRandomOrdinal);
        Assert.Equal(
            0x48e9_76cfu,
            BitConverter.SingleToUInt32Bits(
                replay.Constructor.Amplitude));
        Assert.Equal(
            [359, 411, 848, 907, 990],
            replay.Constructor.ReplacementSampleIndexes);
        Assert.Equal(
            [
                0x47c4_6069u,
                0x4849_d6e3u,
                0xc7e5_b614u,
                0x476e_2c1cu,
                0xc75e_86f2u,
            ],
            replay.Constructor.ReplacementSamples
                .Select(BitConverter.SingleToUInt32Bits));
        Assert.False(replay.Block2Background.BurstCreated);
        Assert.Equal(4_116, replay.Block2Background.BurstTriggerOrdinal);
        Assert.Equal(
            0x3f58_ce2du,
            BitConverter.SingleToUInt32Bits(
                replay.Block1TerminalRandom));
        Assert.Equal(
            0x3e9f_ed0du,
            BitConverter.SingleToUInt32Bits(
                replay.Block2TerminalRandom));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task CurrentProductionMatchesPinnedBurstBehavior()
    {
        ParityCertificationCase burstDefinition =
            ParityCertificationCase.LoadForInspection(
                XPlatQrnBurstStationLifecycleTarget.ParityId);
        ParityCertificationCase sparseDefinition =
            ParityCertificationCase.LoadForInspection(
                XPlatQrnBackgroundSparseImpulsesTarget.ParityId);

        ParityObservation burst =
            await new XPlatQrnBurstStationLifecycleTarget()
                .ExecuteAsync(
                    burstDefinition.Scenario,
                    TestContext.Current.CancellationToken);
        ParityObservation sparse =
            await new XPlatQrnBackgroundSparseImpulsesTarget()
                .ExecuteAsync(
                    sparseDefinition.Scenario,
                    TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, burst.Outcome);
        Assert.Null(burst.FailureCode);
        Assert.Equal(
            XPlatQrnBurstStationLifecycleTarget.EvidenceSource,
            burst.EvidenceSource);
        Assert.Equal(
            burstDefinition.Scenario.ExpectedValues,
            burst.Values,
            StringComparer.Ordinal);
        Assert.Equal(ParityTargetOutcome.Passed, sparse.Outcome);
        Assert.Null(sparse.FailureCode);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task ProductionCaptureIsDeterministicAndWellFramed()
    {
        QrnBurstStationLifecycleInput input = ValidInput();

        string[] first =
            await XPlatQrnBurstStationLifecycleTarget.ObserveAsync(
                input,
                TestContext.Current.CancellationToken);
        string[] second =
            await XPlatQrnBurstStationLifecycleTarget.ObserveAsync(
                input,
                TestContext.Current.CancellationToken);

        Assert.Equal(first, second, StringComparer.Ordinal);
        Assert.Equal(
            QrnBurstStationLifecycleInput.ExpectedValueCount,
            first.Length);
        Assert.StartsWith(
            "qrn-block[0]|sample-count=512|",
            first[4],
            StringComparison.Ordinal);
        Assert.StartsWith(
            "qrn-block[1]|sample-count=512|",
            first[6],
            StringComparison.Ordinal);
        Assert.Contains(
            "|after-block1-count=1|",
            first[3],
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("seed")]
    [InlineData("scenario")]
    [InlineData("expected-count")]
    [Trait("Category", "ParityInfrastructure")]
    public void InputContractFailsClosed(string mutation)
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                XPlatQrnBurstStationLifecycleTarget.ParityId);
        JsonObject input = JsonNode.Parse(
            definition.Scenario.Input.GetRawText())!.AsObject();
        IReadOnlyList<string> expected =
            definition.Scenario.ExpectedValues;
        switch (mutation)
        {
            case "extra":
                input["unsupported"] = true;
                break;
            case "seed":
                input["seed"] = 1_904;
                break;
            case "scenario":
                input["scenario"] = "wrong";
                break;
            case "expected-count":
                expected = expected.Skip(1).ToArray();
                break;
            default:
                throw new InvalidOperationException();
        }

        Assert.Throws<InvalidDataException>(
            () =>
            {
                ParityScenario scenario = new(
                    XPlatQrnBurstStationLifecycleTarget.ParityId,
                    "simulation.legacy-effects",
                    expected,
                    JsonSerializer.SerializeToElement(input));
                QrnBurstStationLifecycleInput.Parse(scenario);
            });
    }

    private static QrnBurstStationLifecycleInput ValidInput()
    {
        return new(
            SampleRate: 11_025,
            BlockSize: 512,
            Seed: 1_903,
            BandwidthHz: 500,
            PitchHz: 600,
            StartupRequestCount: 5,
            ComparedBlockCount: 2,
            Block1ProbeSampleIndexes:
                [0, 153, 154, 155, 359, 371, 372, 373, 411, 511],
            Block2ProbeSampleIndexes:
                [
                    0, 64, 65, 66, 116, 117, 118, 239, 240, 241,
                    335, 336, 337, 395, 396, 397, 478, 479, 480,
                    511,
                ],
            Block1BackgroundReplacementIndexes:
                [154, 210, 245, 284, 324, 341, 424, 493],
            Block1BackgroundTriggerRandomOrdinals:
                [
                    1_178, 1_235, 1_271, 1_311, 1_352, 1_370,
                    1_454, 1_524,
                ],
            Block1BackgroundReplacementRandomOrdinals:
                [
                    1_179, 1_236, 1_272, 1_312, 1_353, 1_371,
                    1_455, 1_525,
                ],
            Block1BurstTriggerRandomOrdinal: 1_544,
            DurationRandomOrdinal: 1_545,
            AmplitudeRandomOrdinal: 1_546,
            DurationBlocks: 2,
            DurationSamples: 1_024,
            EnvelopeReplacementIndexes:
                [359, 411, 848, 907, 990],
            EnvelopeTriggerRandomOrdinals:
                [1_906, 1_959, 2_397, 2_457, 2_541],
            EnvelopeReplacementRandomOrdinals:
                [1_907, 1_960, 2_398, 2_458, 2_542],
            Block1TerminalRandomOrdinal: 2_576,
            Block2BackgroundReplacementIndexes:
                [22, 146, 233, 297],
            Block2BackgroundTriggerRandomOrdinals:
                [3_622, 3_747, 3_835, 3_900],
            Block2BackgroundReplacementRandomOrdinals:
                [3_623, 3_748, 3_836, 3_901],
            Block2BurstTriggerRandomOrdinal: 4_116,
            Block2TerminalRandomOrdinal: 4_117);
    }
}
