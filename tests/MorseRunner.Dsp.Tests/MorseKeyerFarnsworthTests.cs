using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MorseRunner.Dsp;

namespace MorseRunner.Dsp.Tests;

public sealed class MorseKeyerFarnsworthTests
{
    [Theory]
    [InlineData(
        "PARIS TEST",
        ".--. .- .-. .. ...  - . ... -~",
        71_363,
        71_680,
        "8d24e5cd0f054a2d846a01120bf57fa4ca6c341937c1fc93c834fa5851fb1546")]
    [InlineData(
        "K1ABC 599 123",
        "-.- .---- .- -... -.-.  ..... ----. ----.  .---- ..--- ...--~",
        136_971,
        137_216,
        "8e8c1b424dcd8925c46bebb8051d11d9cf36c57fe4950d89592a80f3ea914a9e")]
    public void SstFarnsworthEnvelopeMatchesCeVector(
        string message,
        string expectedEncoding,
        int expectedTrueLength,
        int expectedPaddedLength,
        string expectedScaledHash)
    {
        string encoded = MorseKeyer.Encode(message);
        var keyer = new MorseKeyer(sampleRate: 11_025, blockSize: 512);
        keyer.SetWordsPerMinute(
            sendingWordsPerMinute: 15,
            characterWordsPerMinute: 25);

        float[] envelope = keyer.CreateEnvelope(encoded);

        Assert.Equal(expectedEncoding, encoded);
        Assert.Equal(expectedTrueLength, keyer.TrueEnvelopeLength);
        Assert.Equal(expectedPaddedLength, envelope.Length);
        Assert.Equal(
            expectedScaledHash,
            ScaleAndHash(envelope, amplitude: 300_000f));
    }

    [Theory]
    [InlineData(".", 1_102)]
    [InlineData("-", 2_204)]
    [InlineData(".^", 5_318)]
    [InlineData("._", 8_496)]
    [InlineData(".~", 6_907)]
    [InlineData("._ ", 16_441)]
    public void MessageMarkersUseExactCharacterAndAdjustedDurations(
        string encoded,
        int expectedLength)
    {
        var keyer = new MorseKeyer(sampleRate: 11_025, blockSize: 1);
        keyer.SetWordsPerMinute(
            sendingWordsPerMinute: 15,
            characterWordsPerMinute: 25);

        float[] envelope = keyer.CreateEnvelope(encoded);

        Assert.Equal(expectedLength, keyer.TrueEnvelopeLength);
        Assert.Equal(expectedLength, envelope.Length);
    }

    [Fact]
    public void EqualCharacterSpeedPreservesNonFarnsworthEnvelope()
    {
        string encoded = MorseKeyer.Encode("CQ TEST");
        var standardKeyer =
            new MorseKeyer(sampleRate: 11_025, blockSize: 512);
        var equalSpeedKeyer =
            new MorseKeyer(sampleRate: 11_025, blockSize: 512);
        standardKeyer.SetWordsPerMinute(sendingWordsPerMinute: 30);
        equalSpeedKeyer.SetWordsPerMinute(
            sendingWordsPerMinute: 30,
            characterWordsPerMinute: 30);

        float[] standard = standardKeyer.CreateEnvelope(encoded);
        float[] equalSpeed = equalSpeedKeyer.CreateEnvelope(encoded);

        Assert.Equal(
            standardKeyer.TrueEnvelopeLength,
            equalSpeedKeyer.TrueEnvelopeLength);
        Assert.Equal(standard, equalSpeed);
    }

    [Fact]
    public void CharacterSpeedBelowSendingSpeedFallsBackToSendingSpeed()
    {
        string encoded = MorseKeyer.Encode("PARIS TEST");
        var standardKeyer =
            new MorseKeyer(sampleRate: 11_025, blockSize: 512);
        var slowerCharacterKeyer =
            new MorseKeyer(sampleRate: 11_025, blockSize: 512);
        standardKeyer.SetWordsPerMinute(sendingWordsPerMinute: 25);
        slowerCharacterKeyer.SetWordsPerMinute(
            sendingWordsPerMinute: 25,
            characterWordsPerMinute: 15);

        float[] standard = standardKeyer.CreateEnvelope(encoded);
        float[] slowerCharacter =
            slowerCharacterKeyer.CreateEnvelope(encoded);

        Assert.Equal(
            standardKeyer.TrueEnvelopeLength,
            slowerCharacterKeyer.TrueEnvelopeLength);
        Assert.Equal(standard, slowerCharacter);
    }

    [Fact]
    public void DelayExpressionRoundsToSingleAfterTheRightHandSide()
    {
        var keyer = new MorseKeyer(sampleRate: 44_100, blockSize: 1);
        keyer.SetWordsPerMinute(
            sendingWordsPerMinute: 14,
            characterWordsPerMinute: 14);

        float[] envelope = keyer.CreateEnvelope(MorseKeyer.Encode("EE"));

        Assert.Equal(35_442, keyer.TrueEnvelopeLength);
        Assert.Equal(35_442, envelope.Length);
    }

    [Fact]
    public void HighCharacterRateOverlapsTheDefaultRampsLikeCe()
    {
        var keyer = new MorseKeyer(sampleRate: 11_025, blockSize: 1);
        keyer.SetWordsPerMinute(
            sendingWordsPerMinute: 15,
            characterWordsPerMinute: 100);

        float[] envelope = keyer.CreateEnvelope(MorseKeyer.Encode("E"));

        Assert.Equal(9_510, keyer.TrueEnvelopeLength);
        Assert.Equal(9_510, envelope.Length);
        Assert.Equal(
            "4e5467d9e5f5b5c5e1c6d214db048833"
            + "486d4fa6a5a9551baf67a89514b911bb",
            Hash(envelope));
    }

    [Theory]
    [InlineData(" ", " ", 7_945)]
    [InlineData("__", "  ", 15_890)]
    [InlineData(" E ", " ._", 16_441)]
    public void CharacterSpeedTextEncodingPreservesWhitespace(
        string text,
        string expectedEncoding,
        int expectedLength)
    {
        var keyer = new MorseKeyer(sampleRate: 11_025, blockSize: 1);
        keyer.SetWordsPerMinute(15, 25);

        string encoded = keyer.EncodeText(text);
        float[] envelope = keyer.CreateEnvelope(encoded);

        Assert.Equal(expectedEncoding, encoded);
        Assert.Equal(expectedLength, keyer.TrueEnvelopeLength);
        Assert.Equal(expectedLength, envelope.Length);
    }

    [Theory]
    [InlineData("E ", ". ~", "._", 8_496)]
    [InlineData("E  ", ".  ~", "._ ", 16_441)]
    public void TrailingSpacesReplaceTheSyntheticMessageMarker(
        string message,
        string expectedBaseEncoding,
        string explicitMarkers,
        int expectedLength)
    {
        string encoded = MorseKeyer.Encode(message);
        var encodedKeyer =
            new MorseKeyer(sampleRate: 11_025, blockSize: 1);
        var markerKeyer =
            new MorseKeyer(sampleRate: 11_025, blockSize: 1);
        encodedKeyer.SetWordsPerMinute(15, 25);
        markerKeyer.SetWordsPerMinute(15, 25);

        float[] fromBaseEncoding =
            encodedKeyer.CreateEnvelope(encoded);
        float[] fromExplicitMarkers =
            markerKeyer.CreateEnvelope(explicitMarkers);

        Assert.Equal(expectedBaseEncoding, encoded);
        Assert.Equal(expectedLength, encodedKeyer.TrueEnvelopeLength);
        Assert.Equal(fromExplicitMarkers, fromBaseEncoding);
    }

    [Fact]
    public void LeadingSpacesRemainRawAdjustedSpaces()
    {
        string encoded = MorseKeyer.Encode(" E");
        var keyer = new MorseKeyer(sampleRate: 11_025, blockSize: 1);
        keyer.SetWordsPerMinute(15, 25);

        float[] envelope = keyer.CreateEnvelope(encoded);

        Assert.Equal(" .~", encoded);
        Assert.Equal(14_852, keyer.TrueEnvelopeLength);
        Assert.Equal(14_852, envelope.Length);
    }

    [Fact]
    public void AlreadyEncodedPieceSeparatorsRemainRawAdjustedSpaces()
    {
        var keyer = new MorseKeyer(sampleRate: 11_025, blockSize: 1);
        keyer.SetWordsPerMinute(15, 25);

        float[] envelope = keyer.CreateEnvelope(".~ .~");

        Assert.Equal(21_759, keyer.TrueEnvelopeLength);
        Assert.Equal(21_759, envelope.Length);
    }

    [Fact]
    public void MultipleSpacesBecomeRawAdjustedInterwordSpaces()
    {
        const int expectedExtraSpaceSamples = 5 * 1_589;
        string singleSpaceEncoding = MorseKeyer.Encode("E E");
        string multipleSpaceEncoding = MorseKeyer.Encode("E  E");
        var singleSpaceKeyer =
            new MorseKeyer(sampleRate: 11_025, blockSize: 1);
        var multipleSpaceKeyer =
            new MorseKeyer(sampleRate: 11_025, blockSize: 1);
        var markerKeyer =
            new MorseKeyer(sampleRate: 11_025, blockSize: 1);
        singleSpaceKeyer.SetWordsPerMinute(15, 25);
        multipleSpaceKeyer.SetWordsPerMinute(15, 25);
        markerKeyer.SetWordsPerMinute(15, 25);

        float[] singleSpace =
            singleSpaceKeyer.CreateEnvelope(singleSpaceEncoding);
        float[] multipleSpaces =
            multipleSpaceKeyer.CreateEnvelope(multipleSpaceEncoding);
        float[] explicitMarkers = markerKeyer.CreateEnvelope("._ .~");

        Assert.Equal(".  .~", singleSpaceEncoding);
        Assert.Equal(".   .~", multipleSpaceEncoding);
        Assert.Equal(15_403, singleSpaceKeyer.TrueEnvelopeLength);
        Assert.Equal(23_348, multipleSpaceKeyer.TrueEnvelopeLength);
        Assert.Equal(
            expectedExtraSpaceSamples,
            multipleSpaceKeyer.TrueEnvelopeLength
            - singleSpaceKeyer.TrueEnvelopeLength);
        Assert.Equal(explicitMarkers, multipleSpaces);
    }

    private static string ScaleAndHash(
        Span<float> envelope,
        float amplitude)
    {
        for (int index = 0; index < envelope.Length; index++)
        {
            envelope[index] *= amplitude;
        }

        return Hash(envelope);
    }

    private static string Hash(ReadOnlySpan<float> envelope)
    {
        return Convert.ToHexStringLower(
            SHA256.HashData(MemoryMarshal.AsBytes(envelope)));
    }
}
