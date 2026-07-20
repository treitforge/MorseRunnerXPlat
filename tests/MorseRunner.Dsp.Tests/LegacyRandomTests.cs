namespace MorseRunner.Dsp.Tests;

public sealed class LegacyRandomTests
{
    [Fact]
    public void ZeroBoundReturnsZeroAndConsumesOneDraw()
    {
        var random = new LegacyRandom(12_345);

        int value = random.Next(0);

        Assert.Equal(0, value);
        Assert.Equal(
            0x3F63_E12EU,
            BitConverter.SingleToUInt32Bits(random.NextSingle()));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(-2, -1)]
    public void NegativeBoundUsesFpcIncrementAndConsumesOneDraw(
        int bound,
        int expected)
    {
        var random = new LegacyRandom(12_345);

        Assert.Equal(expected, random.Next(bound));
        Assert.Equal(
            0x3F63_E12EU,
            BitConverter.SingleToUInt32Bits(random.NextSingle()));
    }
}
