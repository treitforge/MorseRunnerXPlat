using System.Text.Json;
using System.Text.Json.Nodes;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatQrmFirstTriggeredStationTargetTests
{
    private static readonly int[] MutatedProbeIndexes =
    [
        1, 0, 2, 148, 149, 150, 255, 310, 384, 509, 510, 511,
    ];

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void PinnedCeFixtureCapturesFirstTriggeredStation()
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                XPlatQrmFirstTriggeredStationTarget.ParityId);
        IReadOnlyList<string> values =
            definition.Scenario.ExpectedValues;

        Assert.Equal(
            QrmFirstTriggeredStationInput.ExpectedValueCount,
            values.Count);
        Assert.Equal(
            "station|count=1|class=TQrmStation|state=stSending"
                + "|my-call=LU5MT|his-call=W7SST"
                + "|r1-single-bits=3f03301e"
                + "|amplitude=19583.306640625"
                + "|amplitude-single-bits=4698fe9d"
                + "|pitch-offset-hz=-124|wpm-s=31|wpm-c=31",
            values[3]);
        Assert.Equal(
            "message|set=[msgQrl2]|text=QRL?   QRL?"
                + "|envelope-samples=53248|envelope-blocks=104"
                + "|send-position=512|remaining-blocks=103",
            values[4]);
        Assert.Contains(
            "|float-sha256="
                + "3ba44162f2959aeeaa6599059e97033d9"
                + "42258e3a2e0dc33cbb07defbb12a4c5",
            values[6],
            StringComparison.Ordinal);
        Assert.Contains(
            "|float-sha256="
                + "72f7618e7e055db7fefd472c47f04880"
                + "46087905d5810b1e1aa97b88187f643d",
            values[7],
            StringComparison.Ordinal);
        Assert.Contains(
            "|first-divergence=310|",
            values[8],
            StringComparison.Ordinal);
        Assert.Equal(
            "b08ba365056aba1885437b23426f6b5c"
                + "af400ec1267420e628f82ecade3d8e06",
            ParityObservedValuesDigest.Compute(values));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void SourceOrderReplayPinsEagerQrmConstructor()
    {
        QrmFirstTriggeredStationInput input = ValidInput();

        XPlatQrmFirstTriggeredStationTarget.ConstructorReplay replay =
            XPlatQrmFirstTriggeredStationTarget.ReplayDecisions(input);

        Assert.Equal(1_024, replay.TriggerOrdinal);
        Assert.Equal(
            0x38e1_bf40u,
            BitConverter.SingleToUInt32Bits(replay.TriggerValue));
        Assert.Equal(
            [
                0x38e1_bf40u,
                0x3f03_301eu,
                0x3eac_999cu,
                0x3f04_e9ecu,
                0x3f15_5543u,
                0x3e29_3bc8u,
                0x3f2d_adc6u,
                0x3da2_d42cu,
                0x3e99_41cdu,
                0x3f51_9e01u,
            ],
            replay.Sequence.Select(BitConverter.SingleToUInt32Bits));
        Assert.Equal(1_025, replay.R1Ordinal);
        Assert.Equal(2, replay.Patience);
        Assert.Equal(23_903, replay.CallIndex);
        Assert.Equal(
            0x4698_fe9du,
            BitConverter.SingleToUInt32Bits(replay.Amplitude));
        Assert.Equal(-124, replay.PitchOffsetHz);
        Assert.Equal(31, replay.WordsPerMinute);
        Assert.Equal(2, replay.MessageChoice);
        Assert.Equal(1_033, replay.TerminalOrdinal);
        Assert.Equal(
            0x3f51_9e01u,
            BitConverter.SingleToUInt32Bits(replay.TerminalRandom));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task CurrentProductionIsRedAtInternalStationRow()
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                XPlatQrmFirstTriggeredStationTarget.ParityId);

        ParityObservation observation =
            await new XPlatQrmFirstTriggeredStationTarget()
                .ExecuteAsync(
                    definition.Scenario,
                    TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            XPlatQrmFirstTriggeredStationTarget
                .FunctionalDivergenceCode,
            observation.FailureCode);
        Assert.Equal(
            XPlatQrmFirstTriggeredStationTarget.EvidenceSource,
            observation.EvidenceSource);
        Assert.Equal(
            3,
            FirstMismatch(
                definition.Scenario.ExpectedValues,
                observation.Values));
        Assert.Equal(
            definition.Scenario.ExpectedValues.Take(3),
            observation.Values.Take(3),
            StringComparer.Ordinal);
        Assert.StartsWith(
            "station|count=0|class=none|state=none|",
            observation.Values[3],
            StringComparison.Ordinal);
        Assert.Equal(
            definition.Scenario.ExpectedValues[6],
            observation.Values[6]);
        Assert.EndsWith(
            "|exact-equal=true|first-divergence=-1"
                + "|clean-float-sha256="
                + "3ba44162f2959aeeaa6599059e97033d9"
                + "42258e3a2e0dc33cbb07defbb12a4c5"
                + "|qrm-float-sha256="
                + "3ba44162f2959aeeaa6599059e97033d9"
                + "42258e3a2e0dc33cbb07defbb12a4c5"
                + "|station-counts=0,0"
                + "|pick-station-calls=0,0|get-call-calls=0,0",
            observation.Values[8],
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task ProductionCaptureIsDeterministicAndWellFramed()
    {
        QrmFirstTriggeredStationInput input = ValidInput();

        string[] first =
            await XPlatQrmFirstTriggeredStationTarget.ObserveAsync(
                input,
                TestContext.Current.CancellationToken);
        string[] second =
            await XPlatQrmFirstTriggeredStationTarget.ObserveAsync(
                input,
                TestContext.Current.CancellationToken);

        Assert.Equal(first, second, StringComparer.Ordinal);
        Assert.Equal(
            QrmFirstTriggeredStationInput.ExpectedValueCount,
            first.Length);
        Assert.Contains(
            "|clean-single-bits=38e1bf40",
            first[9],
            StringComparison.Ordinal);
        Assert.EndsWith(
            "|qrm-single-bits=38e1bf40",
            first[9],
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("seed")]
    [InlineData("scenario")]
    [InlineData("catalog-index")]
    [InlineData("probe-order")]
    [InlineData("expected-count")]
    [Trait("Category", "ParityInfrastructure")]
    public void InputContractFailsClosed(string mutation)
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                XPlatQrmFirstTriggeredStationTarget.ParityId);
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
                input["seed"] = 1_844;
                break;
            case "scenario":
                input["scenario"] = "wrong";
                break;
            case "catalog-index":
                input["selectedCallIndex"] = 23_904;
                break;
            case "probe-order":
                input["probeSampleIndexes"] =
                    JsonSerializer.SerializeToNode(
                        MutatedProbeIndexes);
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
                    XPlatQrmFirstTriggeredStationTarget.ParityId,
                    "simulation.legacy-effects",
                    expected,
                    JsonSerializer.SerializeToElement(input));
                QrmFirstTriggeredStationInput.Parse(scenario);
            });
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task UnsupportedScenarioDoesNotAttemptCapture()
    {
        ParityObservation observation =
            await new XPlatQrmFirstTriggeredStationTarget()
                .ExecuteAsync(
                    new ParityScenario(
                        "audio.some-other-case",
                        "simulation.legacy-effects",
                        []),
                    TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            MorseRunner.Domain.DomainErrorCodes.UnsupportedCapability,
            observation.FailureCode);
        Assert.Empty(observation.Values);
    }

    private static int FirstMismatch(
        IReadOnlyList<string> expected,
        IReadOnlyList<string> actual)
    {
        int count = Math.Min(expected.Count, actual.Count);
        for (int index = 0; index < count; index++)
        {
            if (!StringComparer.Ordinal.Equals(
                    expected[index],
                    actual[index]))
            {
                return index;
            }
        }

        return expected.Count == actual.Count ? -1 : count;
    }

    private static QrmFirstTriggeredStationInput ValidInput()
    {
        return new(
            SampleRate: 11_025,
            BlockSize: 512,
            Seed: 1_843,
            BandwidthHz: 500,
            PitchHz: 600,
            StartupRequestCount: 5,
            ComparedBlockCount: 1,
            QrmTriggerRandomOrdinal: 1_024,
            CleanTerminalRandomOrdinal: 1_024,
            QrmTerminalRandomOrdinal: 1_033,
            CallCatalogCount: 46_039,
            SelectedCallIndex: 23_903,
            MasterDataSha256:
                "acf37090e7c9c0f2146a2b08608295cb243c8bfe649a421d"
                + "1c528a59656097aa",
            StationCall: "W7SST",
            ProbeSampleIndexes:
                [0, 1, 2, 148, 149, 150, 255, 310, 384, 509, 510, 511]);
    }
}
