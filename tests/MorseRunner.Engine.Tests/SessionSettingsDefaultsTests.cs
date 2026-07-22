using MorseRunner.Domain;

namespace MorseRunner.Engine.Tests;

public sealed class SessionSettingsDefaultsTests
{
    [Fact]
    public void CleanProfileMatchesCeDefaults()
    {
        SessionSettings settings = SessionSettings.CreateDefault(12_345);

        Assert.Equal("VE3NEA", settings.StationCall);
        Assert.Empty(settings.OperatorExchange);
        Assert.Equal(25, settings.WordsPerMinute);
        Assert.Equal(450, settings.PitchHz);
        Assert.Equal(550, settings.BandwidthHz);
        Assert.Equal(2, settings.Activity);
        Assert.Equal(38_760, settings.DurationBlocks);
        Assert.Equal(60, settings.CompetitionDurationMinutes);
        Assert.Equal("scWpx", settings.ContestId.Value);
        Assert.Equal("rmPileup", settings.RunModeId.Value);
        Assert.Equal(SerialNumberRangeMode.StartOfContest,
            settings.SerialNumberRange);
        Assert.Equal(0, settings.ReceiveSpeedBelowWpm);
        Assert.Equal(0, settings.ReceiveSpeedAboveWpm);
        Assert.Equal(3, settings.StationIdRate);
        Assert.Equal(0d, settings.MonitorLevelDb);
        Assert.Empty(settings.HstOperatorName);
        Assert.False(settings.Qsk);
        Assert.False(settings.Qsb);
        Assert.False(settings.Qrm);
        Assert.False(settings.Qrn);
        Assert.False(settings.Flutter);
        Assert.False(settings.Lids);
    }
}
