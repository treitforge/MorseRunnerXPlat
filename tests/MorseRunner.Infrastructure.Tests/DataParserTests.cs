using MorseRunner.Domain;

namespace MorseRunner.Infrastructure.Tests;

public sealed class DataParserTests
{
    [Theory]
    [InlineData("W7SST", "W7SST", "W7", "W7SST")]
    [InlineData("F6/W7SST", "W7SST", "F6", "F6")]
    [InlineData("W7SST/P", "W7SST", "W7", "W7SST")]
    [InlineData("RC2FX", "RC2FX", "RC2", "RC2FX")]
    public void CallsignUtilitiesMatchApplicationRules(
        string input,
        string callsign,
        string wpxPrefix,
        string dxccPrefix)
    {
        Assert.Equal(callsign, CallsignParser.ExtractCallsign(input));
        Assert.Equal(wpxPrefix, CallsignParser.ExtractPrefix(input));
        Assert.Equal(
            dxccPrefix,
            CallsignParser.ExtractPrefix(input, deleteTrailingLetters: false));
    }

    [Theory]
    [InlineData("W7SST", "United States of America", "NA", "6,7,8", "3,4,5")]
    [InlineData("F6/W7SST", "France", "EU", "27", "14")]
    [InlineData("RC2FX", "Kaliningrad", "EU", "29", "15")]
    [InlineData("KG4AA", "Guantanamo Bay", "NA", "11", "08")]
    [InlineData("KG4ABC", "United States of America", "NA", "6,7,8", "3,4,5")]
    [InlineData("CE9/W7SST", "Antarctica", "AN", "67,69-74", "12,13,29,30,32,38,39")]
    public void DxccLookupMatchesLongestPrefixAndSpecialCases(
        string callsign,
        string entity,
        string continent,
        string itu,
        string cq)
    {
        var database = new DxccDatabase();

        Assert.True(database.TryFind(callsign, out DxccRecord? actual));
        Assert.Equal(new DxccRecord(entity, continent, itu, cq), actual);
    }

    [Fact]
    public void SweepstakesParserHandlesCompleteAndIncompleteExchanges()
    {
        SweepstakesParseResult own =
            SweepstakesExchangeParser.ParseOwn("123 A 72 OR");
        SweepstakesParseResult complete =
            SweepstakesExchangeParser.ParseEntered(
                "W7SST",
                "123 A W7SST 72 OR");
        SweepstakesParseResult rotation =
            SweepstakesExchangeParser.ParseEntered(
                "K1ABC",
                "11 22 33 ID 44 55");

        Assert.True(own.IsValid);
        Assert.Equal(123, own.Exchange.SerialNumber);
        Assert.Equal("A", own.Exchange.Precedence);
        Assert.Equal("72", own.Exchange.Check);
        Assert.Equal("OR", own.Exchange.Section);
        Assert.Equal("123A W7SST 72 OR", complete.Exchange.Summary);
        Assert.True(complete.IsValid);
        Assert.Equal("44 K1ABC 55 ID", rotation.Exchange.Summary);
        Assert.Equal("Missing/Invalid Precedence", rotation.Error);
    }
}
