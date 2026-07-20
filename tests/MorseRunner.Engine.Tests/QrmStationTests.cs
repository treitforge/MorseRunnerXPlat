using MorseRunner.Domain;
using MorseRunner.Dsp;
using MorseRunner.Engine;

namespace MorseRunner.Engine.Tests;

public sealed class QrmStationTests
{
    private const int BlockSize = CompatibilityProfile.BlockSize;

    [Fact]
    public void Seed1843MatchesTheCeEagerConstructorAndFirstBlock()
    {
        var random = new LegacyRandom(1_843);
        for (int ordinal = 0; ordinal < 1_024; ordinal++)
        {
            _ = random.NextDouble();
        }

        double trigger = random.NextDouble();
        Assert.True(trigger < 0.0002d);
        Assert.Equal(
            0x38E1_BF40U,
            BitConverter.SingleToUInt32Bits((float)trigger));

        StationReferenceCatalog catalog =
            StationReferenceCatalog.Load(
                new ContestId("scWpx"),
                "W7SST");
        var station = new QrmStation(
            CreateProfile(new ContestId("scWpx")));
        station.Activate(
            random,
            new LegacyRandomEffects(random),
            catalog,
            new ContestId("scWpx"),
            new RunModeId("rmStop"),
            "W7SST");

        Assert.True(station.IsActive);
        Assert.True(station.IsSending);
        Assert.Equal(
            0x3F03_301EU,
            BitConverter.SingleToUInt32Bits(station.R1));
        Assert.Equal(2, station.Patience);
        Assert.Equal("LU5MT", station.MyCall);
        Assert.Equal("W7SST", station.HisCall);
        Assert.Equal(
            0x4698_FE9DU,
            BitConverter.SingleToUInt32Bits(station.Amplitude));
        Assert.Equal(-124, station.PitchOffsetHz);
        Assert.Equal(31, station.SendingWordsPerMinute);
        Assert.Equal(31, station.CharacterWordsPerMinute);
        Assert.Equal("[msgQrl2]", station.MessageSet);
        Assert.Equal("QRL?   QRL?", station.MessageText);
        Assert.Equal(53_248, station.EnvelopeSampleCount);
        Assert.Equal(0, station.SendPosition);
        Assert.Equal(104, station.RemainingBlockCount);
        Assert.Equal(1, station.TransmissionCount);

        var envelope = new float[BlockSize];
        var receiverReal = new float[BlockSize];
        var receiverImaginary = new float[BlockSize];
        station.MixNextBlock(
            envelope,
            receiverReal,
            receiverImaginary,
            ritOffsetHz: 0,
            ritPhase: 0f);

        Assert.Equal(BlockSize, station.SendPosition);
        Assert.Equal(103, station.RemainingBlockCount);
        Assert.True(station.IsSending);
        Assert.False(station.Tick(
            new LegacyRandomEffects(random),
            new ContestId("scWpx")));
        Assert.Equal(
            0x3F51_9E01U,
            BitConverter.SingleToUInt32Bits(random.NextSingle()));
    }

    [Fact]
    public void RetryUsesExactSilentBlockCountAndThenReleases()
    {
        var random = new LegacyRandom(1_843);
        for (int ordinal = 0; ordinal <= 1_024; ordinal++)
        {
            _ = random.NextDouble();
        }

        StationReferenceCatalog catalog =
            StationReferenceCatalog.Load(
                new ContestId("scWpx"),
                "W7SST");
        var effects = new LegacyRandomEffects(random);
        var station = new QrmStation(
            CreateProfile(new ContestId("scWpx")));
        station.Activate(
            random,
            effects,
            catalog,
            new ContestId("scWpx"),
            new RunModeId("rmStop"),
            "W7SST");

        var envelope = new float[BlockSize];
        var receiverReal = new float[BlockSize];
        var receiverImaginary = new float[BlockSize];
        bool releaseRequested = false;
        int firstTransmissionBlocks = 0;
        while (station.IsSending)
        {
            station.MixNextBlock(
                envelope,
                receiverReal,
                receiverImaginary,
                ritOffsetHz: 0,
                ritPhase: 0f);
            firstTransmissionBlocks++;
            releaseRequested = station.Tick(
                effects,
                new ContestId("scWpx"));
        }

        Assert.False(releaseRequested);
        Assert.Equal(104, firstTransmissionBlocks);
        Assert.Equal(1, station.Patience);
        Assert.InRange(station.TimeoutBlocks, 43, 129);
        int silentBlocks = station.TimeoutBlocks;

        for (int block = 0; block < silentBlocks; block++)
        {
            Assert.False(station.Tick(
                effects,
                new ContestId("scWpx")));
            Assert.Equal(
                block == silentBlocks - 1,
                station.IsSending);
        }

        Assert.Equal("[msgLongCQ]", station.MessageSet);
        Assert.Equal(
            "CQ CQ TEST LU5MT LU5MT TEST",
            station.MessageText);
        Assert.Equal(2, station.TransmissionCount);

        int retryBlocks = 0;
        while (!releaseRequested)
        {
            station.MixNextBlock(
                envelope,
                receiverReal,
                receiverImaginary,
                ritOffsetHz: 0,
                ritPhase: 0f);
            retryBlocks++;
            releaseRequested = station.Tick(
                effects,
                new ContestId("scWpx"));
        }

        Assert.True(retryBlocks > 0);
        Assert.False(station.IsSending);
        Assert.Equal(0, station.Patience);
        station.Release();
        Assert.False(station.IsActive);
        Assert.Null(station.MyCall);
        Assert.Null(station.MessageText);
    }

    [Fact]
    public void PoolBoundIsFiniteAndIncludesEveryCatalogCall()
    {
        var contestId = new ContestId("scWpx");
        StationReferenceCatalog catalog =
            StationReferenceCatalog.Load(contestId, "W7SST");

        int maximumConcurrent =
            QrmStation.CalculateMaximumConcurrentStations(
                CreateProfile(contestId),
                catalog,
                contestId,
                "W7SST");

        Assert.Equal(2_396, maximumConcurrent);
        Assert.Equal(46_039, catalog.QrmCallsignCount);
    }

    private static LegacyMorseKeyingProfile CreateProfile(
        ContestId contestId) =>
        new(
            CompatibilityProfile.SampleRate,
            CompatibilityProfile.BlockSize,
            contestId.Value == "scSst"
                ? LegacyMorseKeyingMode.SstFarnsworth
                : LegacyMorseKeyingMode.Standard);
}
