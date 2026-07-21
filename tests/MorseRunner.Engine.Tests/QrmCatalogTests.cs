using MorseRunner.Domain;
using MorseRunner.Dsp;

namespace MorseRunner.Engine.Tests;

public sealed class QrmCatalogTests
{
    [Theory]
    [InlineData("scAllJa", "JR4ERC/4", "34H")]
    [InlineData("scAcag", "JA8SBJ", "0102L")]
    public void JarlTruthUsesRstThenStoredLocationPower(
        string contestId,
        string expectedCall,
        string expectedExchange2)
    {
        ContestId id = new(contestId);

        StationIdentity station = StationReferenceCatalog
            .Load(id)
            .Pick(new LegacyRandom(12_345), id, serialNumber: 1);

        Assert.Equal(expectedCall, station.Callsign);
        Assert.Equal("599", station.Exchange1);
        Assert.Equal(expectedExchange2, station.Exchange2);
    }

    [Fact]
    public void WpxCallOnlySelectionMatchesThePinnedCeSeedVector()
    {
        StationReferenceCatalog catalog =
            StationReferenceCatalog.Load(new ContestId("scWpx"));
        var random = new LegacyRandom(1_843);
        for (int ordinal = 0; ordinal < 1_027; ordinal++)
        {
            _ = random.NextSingle();
        }

        Assert.Equal(46_039, catalog.QrmCallsignCount);
        Assert.Equal(46_040, catalog.QrmEnvelopeBoundCallsignCount);
        Assert.Equal("LU5MT", catalog.GetQrmCallsignAt(23_903));
        Assert.Equal(
            "P29SX",
            catalog.GetQrmEnvelopeBoundCallsignAt(46_039));
        Assert.Equal(
            "LU5MT",
            catalog.PickCallsignForQrm(
                random,
                new RunModeId("rmStop")));
        Assert.Equal(46_039, catalog.QrmCallsignCount);
    }

    [Fact]
    public void HstSelectionDeletesOnlyInHstRunMode()
    {
        StationReferenceCatalog stopped =
            StationReferenceCatalog.Load(new ContestId("scHst"));
        int stoppedCount = stopped.QrmCallsignCount;

        _ = stopped.PickCallsignForQrm(
            new LegacyRandom(81),
            new RunModeId("rmStop"));

        Assert.Equal(stoppedCount, stopped.QrmCallsignCount);

        StationReferenceCatalog competing =
            StationReferenceCatalog.Load(new ContestId("scHst"));
        int expectedIndex = new LegacyRandom(81).Next(
            competing.QrmCallsignCount);
        string expectedCall = competing.GetQrmCallsignAt(expectedIndex);

        string selected = competing.PickCallsignForQrm(
            new LegacyRandom(81),
            new RunModeId("rmHst"));

        Assert.Equal(expectedCall, selected);
        Assert.Equal(stoppedCount - 1, competing.QrmCallsignCount);
        Assert.DoesNotContain(
            selected,
            Enumerable.Range(0, competing.QrmCallsignCount)
                .Select(competing.GetQrmCallsignAt));

        StationReferenceCatalog wpx =
            StationReferenceCatalog.Load(new ContestId("scWpx"));
        int wpxCount = wpx.QrmCallsignCount;
        _ = wpx.PickCallsignForQrm(
            new LegacyRandom(81),
            new RunModeId("rmHst"));
        Assert.Equal(wpxCount - 1, wpx.QrmCallsignCount);

        StationReferenceCatalog cwt =
            StationReferenceCatalog.Load(new ContestId("scCwt"));
        int cwtCount = cwt.QrmCallsignCount;
        _ = cwt.PickCallsignForQrm(
            new LegacyRandom(81),
            new RunModeId("rmHst"));
        Assert.Equal(cwtCount, cwt.QrmCallsignCount);
    }

    [Fact]
    public void ExhaustedHstCatalogReturnsCeFallbackWithoutRandomDraw()
    {
        StationReferenceCatalog catalog =
            StationReferenceCatalog.Load(new ContestId("scHst"));
        var actualRandom = new LegacyRandom(704);
        var expectedRandom = new LegacyRandom(704);

        for (int remaining = catalog.QrmCallsignCount;
             remaining > 0;
             remaining--)
        {
            _ = expectedRandom.Next(remaining);
            _ = catalog.PickCallsignForQrm(
                actualRandom,
                new RunModeId("rmHst"));
        }

        Assert.Equal(0, catalog.QrmCallsignCount);
        Assert.Equal(1, catalog.QrmEnvelopeBoundCallsignCount);
        Assert.Equal(
            "P29SX",
            catalog.GetQrmEnvelopeBoundCallsignAt(0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => catalog.GetQrmEnvelopeBoundCallsignAt(1));
        Assert.Equal(
            "P29SX",
            catalog.PickCallsignForQrm(
                actualRandom,
                new RunModeId("rmHst")));
        Assert.Equal(
            BitConverter.SingleToUInt32Bits(expectedRandom.NextSingle()),
            BitConverter.SingleToUInt32Bits(actualRandom.NextSingle()));
    }

    [Fact]
    public void ArrlDxLoadUsesTheCeHomeSideCatalogPartition()
    {
        StationReferenceCatalog local =
            StationReferenceCatalog.Load(
                new ContestId("scArrlDx"),
                "W7SST");
        StationReferenceCatalog dx =
            StationReferenceCatalog.Load(
                new ContestId("scArrlDx"),
                "F6ABC");

        Assert.Equal(3_781, local.QrmCallsignCount);
        Assert.Equal(4_576, dx.QrmCallsignCount);
    }

    [Fact]
    public void SideDependentCatalogRequiresHomeCallBeforeDrawing()
    {
        foreach (string contestId in new[] { "scArrlDx", "scNaQp" })
        {
            StationReferenceCatalog catalog =
                StationReferenceCatalog.Load(new ContestId(contestId));
            var actual = new LegacyRandom(6_031);
            var expected = new LegacyRandom(6_031);

            Assert.Throws<InvalidOperationException>(
                () => catalog.PickCallsignForQrm(
                    actual,
                    new RunModeId("rmStop")));
            Assert.Equal(
                BitConverter.SingleToUInt32Bits(expected.NextSingle()),
                BitConverter.SingleToUInt32Bits(actual.NextSingle()));
        }
    }

    [Fact]
    public void ArrlDxCallOnlySelectionDeletesAnInvalidDxccCallAndRetries()
    {
        StationReferenceCatalog catalog =
            StationReferenceCatalog.Load(
                new ContestId("scArrlDx"),
                "W7SST");
        var dxcc = new ContestDxccDatabase();
        const int seed = 649;
        int initialCount = catalog.QrmCallsignCount;
        var replay = new LegacyRandom(seed);
        int rejectedIndex = replay.Next(initialCount);
        string rejectedCall =
            catalog.GetQrmCallsignAt(rejectedIndex);
        int selectedIndex = replay.Next(initialCount - 1);
        StationReferenceCatalog warmup =
            StationReferenceCatalog.Load(
                new ContestId("scArrlDx"),
                "W7SST");
        _ = warmup.PickCallsignForQrm(
            new LegacyRandom(seed),
            new RunModeId("rmStop"));
        var actual = new LegacyRandom(seed);

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        string selected = catalog.PickCallsignForQrm(
            actual,
            new RunModeId("rmStop"));
        uint terminalBits =
            BitConverter.SingleToUInt32Bits(actual.NextSingle());
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(3_781, initialCount);
        Assert.Equal(219, rejectedIndex);
        Assert.Equal("D1DX", rejectedCall);
        Assert.False(dxcc.TryFind(rejectedCall, out _));
        Assert.Equal(3_450, selectedIndex);
        Assert.Equal("UC7A", selected);
        Assert.Equal("UC7A", catalog.GetQrmCallsignAt(selectedIndex));
        Assert.Equal(3_780, catalog.QrmCallsignCount);
        Assert.Equal(0x3F65_6F23U, terminalBits);
        Assert.Equal(
            terminalBits,
            BitConverter.SingleToUInt32Bits(replay.NextSingle()));
        Assert.True(dxcc.TryFind(selected, out _));
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void NaqpLoadAndDxSelectionMatchCeRemovalRules()
    {
        StationReferenceCatalog catalog =
            StationReferenceCatalog.Load(
                new ContestId("scNaQp"),
                "F6ABC");
        var dxcc = new ContestDxccDatabase();
        const int seed = 319;
        int initialCount = catalog.QrmCallsignCount;
        var replay = new LegacyRandom(seed);
        int rejectedIndex = replay.Next(initialCount);
        string rejectedCall =
            catalog.GetQrmCallsignAt(rejectedIndex);
        int selectedIndex = replay.Next(initialCount - 1);
        StationReferenceCatalog warmup =
            StationReferenceCatalog.Load(
                new ContestId("scNaQp"),
                "F6ABC");
        _ = warmup.PickCallsignForQrm(
            new LegacyRandom(seed),
            new RunModeId("rmStop"));
        var actual = new LegacyRandom(seed);

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        string selected = catalog.PickCallsignForQrm(
            actual,
            new RunModeId("rmStop"));
        uint terminalBits =
            BitConverter.SingleToUInt32Bits(actual.NextSingle());
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(9_272, initialCount);
        Assert.Equal(9_257, rejectedIndex);
        Assert.Equal("SV2AEL", rejectedCall);
        Assert.False(IsNaqpLocal(dxcc, rejectedCall));
        Assert.Equal(656, selectedIndex);
        Assert.Equal("AK2G", selected);
        Assert.Equal("AK2G", catalog.GetQrmCallsignAt(selectedIndex));
        Assert.Equal(9_271, catalog.QrmCallsignCount);
        Assert.Equal(0x3F6A_6367U, terminalBits);
        Assert.Equal(
            terminalBits,
            BitConverter.SingleToUInt32Bits(replay.NextSingle()));
        Assert.True(IsNaqpLocal(dxcc, selected));
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void NaqpLocalHomeRetainsTheSameValidDxCall()
    {
        StationReferenceCatalog catalog =
            StationReferenceCatalog.Load(
                new ContestId("scNaQp"),
                "W7SST");
        var random = new LegacyRandom(319);

        Assert.Equal("SV2AEL", catalog.GetQrmCallsignAt(9_257));
        Assert.Equal(
            "SV2AEL",
            catalog.PickCallsignForQrm(
                random,
                new RunModeId("rmStop")));
        Assert.Equal(9_272, catalog.QrmCallsignCount);
        Assert.Equal(
            0x3D90_F1D7U,
            BitConverter.SingleToUInt32Bits(random.NextSingle()));
    }

    [Fact]
    public void NaqpQrmSelectionRepairsLocalMissingStateForLaterCallers()
    {
        ContestId contestId = new("scNaQp");
        StationReferenceCatalog catalog =
            StationReferenceCatalog.Load(contestId, "W7SST");
        const int seed = 5_133;
        const int callerSeed = 14_729;
        StationReferenceCatalog warmup =
            StationReferenceCatalog.Load(contestId, "W7SST");
        _ = warmup.PickCallsignForQrm(
            new LegacyRandom(seed),
            new RunModeId("rmStop"));
        var qrmRandom = new LegacyRandom(seed);

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        string selected = catalog.PickCallsignForQrm(
            qrmRandom,
            new RunModeId("rmStop"));
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.Equal("6Y5PW", selected);
        Assert.Equal(
            0x3F15_50F9U,
            BitConverter.SingleToUInt32Bits(qrmRandom.NextSingle()));
        Assert.Equal(0, allocated);

        StationIdentity caller = catalog.Pick(
            new LegacyRandom(callerSeed),
            contestId,
            serialNumber: 1);

        Assert.Equal("6Y5PW", caller.Callsign);
        Assert.Equal("6Y", caller.Exchange2);
    }

    [Fact]
    public void MessageChoicesPreserveCeSetsSpacesAndSegmentBoundaries()
    {
        ContestId contest = new("scWpx");

        AssertDescriptor(
            ContestQrmMessageCatalog.CreateInitial(
                contest,
                0,
                "LU5MT",
                "W7SST"),
            QrmMessageKind.Qrl,
            "[msgQrl]",
            ["QRL?"]);
        AssertDescriptor(
            ContestQrmMessageCatalog.CreateInitial(
                contest,
                1,
                "LU5MT",
                "W7SST"),
            QrmMessageKind.QrlTwice,
            "[msgQrl2]",
            ["QRL?", "   ", "QRL?"]);
        AssertDescriptor(
            ContestQrmMessageCatalog.CreateInitial(
                contest,
                2,
                "LU5MT",
                "W7SST"),
            QrmMessageKind.QrlTwice,
            "[msgQrl2]",
            ["QRL?", "   ", "QRL?"]);
        foreach (int choice in new[] { 3, 4, 5 })
        {
            AssertDescriptor(
                ContestQrmMessageCatalog.CreateInitial(
                    contest,
                    choice,
                    "LU5MT",
                    "W7SST"),
                QrmMessageKind.LongCq,
                "[msgLongCQ]",
                ["CQ CQ TEST ", "LU5MT", " ", "LU5MT", " TEST"]);
        }

        AssertDescriptor(
            ContestQrmMessageCatalog.CreateInitial(
                contest,
                6,
                "LU5MT",
                "W7SST"),
            QrmMessageKind.Qsy,
            "[msqQsy]",
            ["W7SST", "  QSY QSY"]);
    }

    [Fact]
    public void LongCqUsesEveryCeContestFamily()
    {
        AssertLongCq(
            "scFieldDay",
            ["CQ CQ FD ", "N3BJY", " ", "N3BJY", " FD"]);
        AssertLongCq(
            "scArrlSS",
            ["CQ CQ SS ", "N3BJY", " ", "N3BJY", " SS"]);
        AssertLongCq(
            "scCwt",
            ["CQ CQ CWT ", "N3BJY", " ", "N3BJY"]);
        AssertLongCq(
            "scSst",
            ["CQ CQ SST ", "N3BJY", " ", "N3BJY"]);

        string[] specialized =
            ["scFieldDay", "scArrlSS", "scCwt", "scSst"];
        foreach (ContestDefinition definition in ContestCatalog.All)
        {
            if (specialized.Contains(
                    definition.Id.Value,
                    StringComparer.Ordinal))
            {
                continue;
            }

            AssertLongCq(
                definition.Id.Value,
                ["CQ CQ TEST ", "N3BJY", " ", "N3BJY", " TEST"]);
        }
    }

    [Fact]
    public void MessageCatalogRejectsAnImpossibleRandomChoice()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ContestQrmMessageCatalog.CreateInitial(
                new ContestId("scWpx"),
                7,
                "LU5MT",
                "W7SST"));
    }

    [Fact]
    public void DescriptorConstructionAndIndexedCatalogScanAllocateNothing()
    {
        ContestId contestId = new("scWpx");
        StationReferenceCatalog catalog =
            StationReferenceCatalog.Load(contestId);
        var random = new LegacyRandom(1_843);
        _ = ContestQrmMessageCatalog.CreateInitial(
            contestId,
            2,
            "LU5MT",
            "W7SST");
        _ = catalog.PickCallsignForQrm(
            random,
            new RunModeId("rmStop"));

        int checksum = 0;
        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 4_096; iteration++)
        {
            ContestQrmMessageDescriptor descriptor =
                ContestQrmMessageCatalog.CreateInitial(
                    contestId,
                    iteration % 7,
                    "LU5MT",
                    "W7SST");
            checksum += descriptor.CharacterCount;
            for (int index = 0;
                 index < descriptor.SegmentCount;
                 index++)
            {
                checksum += descriptor.GetSegment(index).Length;
            }
        }

        for (int index = 0;
             index < catalog.QrmEnvelopeBoundCallsignCount;
             index++)
        {
            checksum += catalog
                .GetQrmEnvelopeBoundCallsignAt(index)
                .Length;
        }

        for (int iteration = 0; iteration < 4_096; iteration++)
        {
            checksum += catalog.PickCallsignForQrm(
                random,
                new RunModeId("rmStop")).Length;
        }

        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.True(checksum > 0);
        Assert.Equal(0, allocated);
    }

    private static bool IsNaqpLocal(
        ContestDxccDatabase dxcc,
        string call)
    {
        return dxcc.TryFind(call, out ContestDxccRecord? record)
            && record is not null
            && (record.Continent == "NA" || record.Entity == "Hawaii");
    }

    private static void AssertLongCq(
        string contestId,
        string[] expectedSegments)
    {
        AssertDescriptor(
            ContestQrmMessageCatalog.CreateLongCq(
                new ContestId(contestId),
                "N3BJY"),
            QrmMessageKind.LongCq,
            "[msgLongCQ]",
            expectedSegments);
    }

    private static void AssertDescriptor(
        ContestQrmMessageDescriptor descriptor,
        QrmMessageKind expectedKind,
        string expectedSet,
        string[] expectedSegments)
    {
        Assert.Equal(expectedKind, descriptor.Kind);
        Assert.Equal(expectedSet, descriptor.MessageSet);
        Assert.Equal(expectedSegments.Length, descriptor.SegmentCount);
        Assert.Equal(
            expectedSegments,
            Enumerable.Range(0, descriptor.SegmentCount)
                .Select(descriptor.GetSegment));
        string expectedText = String.Concat(expectedSegments);
        Assert.Equal(expectedText.Length, descriptor.CharacterCount);
        Assert.Equal(
            expectedText,
            descriptor.MaterializeForObservation());
    }
}
