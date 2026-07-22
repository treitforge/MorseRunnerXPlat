using MorseRunner.Audio;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.Engine.Tests;

public sealed class CompetitionSettingsTests
{
    [Fact]
    public void WpxUsesCompetitionDurationAndForcesConditionsOn()
    {
        SessionSettings submitted = CreateSettings("scWpx", "rmWpx") with
        {
            Qsb = false,
            Qrm = false,
            Qrn = false,
            Flutter = false,
            Lids = false,
        };

        SessionSettings effective =
            MorseRunnerEngine.NormalizeCompetitionSettings(submitted);

        Assert.Equal(DurationBlocks(17), effective.DurationBlocks);
        Assert.Equal(7, effective.Activity);
        Assert.Equal(500, effective.BandwidthHz);
        Assert.True(effective.Qsb);
        Assert.True(effective.Qrm);
        Assert.True(effective.Qrn);
        Assert.True(effective.Flutter);
        Assert.True(effective.Lids);
    }

    [Fact]
    public void HstUsesCompetitionDurationAndForcesModeSettings()
    {
        SessionSettings submitted = CreateSettings("scHst", "rmHst") with
        {
            Qsb = true,
            Qrm = true,
            Qrn = true,
            Flutter = true,
            Lids = true,
        };

        SessionSettings effective =
            MorseRunnerEngine.NormalizeCompetitionSettings(submitted);

        Assert.Equal(DurationBlocks(17), effective.DurationBlocks);
        Assert.Equal(4, effective.Activity);
        Assert.Equal(600, effective.BandwidthHz);
        Assert.False(effective.Qsb);
        Assert.False(effective.Qrm);
        Assert.False(effective.Qrn);
        Assert.False(effective.Flutter);
        Assert.False(effective.Lids);
    }

    [Fact]
    public void NoncompetitionModePreservesSubmittedSettings()
    {
        SessionSettings submitted = CreateSettings("scWpx", "rmPileup");

        SessionSettings effective =
            MorseRunnerEngine.NormalizeCompetitionSettings(submitted);

        Assert.Same(submitted, effective);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public async Task SessionRejectsCompetitionDurationOutsideCeRange(
        int competitionDurationMinutes)
    {
        await using var engine = new MorseRunnerEngine(
            _ => new NullAudioSink());
        SessionSettings settings = CreateSettings("scWpx", "rmWpx") with
        {
            CompetitionDurationMinutes = competitionDurationMinutes,
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => engine.CreateSessionAsync(settings, CancellationToken.None));
    }

    private static SessionSettings CreateSettings(
        string contestId,
        string runModeId) =>
        new(
            12_345,
            new ContestId(contestId),
            new RunModeId(runModeId),
            DurationBlocks(30))
        {
            Activity = 7,
            BandwidthHz = 500,
            CompetitionDurationMinutes = 17,
            SerialNumberRange = SerialNumberRangeMode.StartOfContest,
        };

    private static long DurationBlocks(int minutes) =>
        checked((long)Math.Ceiling(
            minutes
            * 60d
            * CompatibilityProfile.SampleRate
            / CompatibilityProfile.BlockSize));
}
