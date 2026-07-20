using MorseRunner.Dsp;

namespace MorseRunner.LegacyParity.Tests;

public sealed class LegacyAutomaticGainControlTests
{
    [Fact]
    public void ConstantInputMatchesCeSinglePrecisionVector()
    {
        var agc = new LegacyAutomaticGainControl(
            attackSamples: 1,
            holdSamples: 1);
        float[] output = new float[5];

        agc.Process([1f, 1f, 1f, 1f, 1f], output);

        Assert.Equal(
            [
                0U,
                0U,
                0x3F697052U,
                0x3F697052U,
                0x3F697052U,
            ],
            output
                .Select(BitConverter.SingleToUInt32Bits)
                .ToArray());
    }
}
