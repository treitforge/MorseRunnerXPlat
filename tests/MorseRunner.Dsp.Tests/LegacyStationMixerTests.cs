using System.Runtime.CompilerServices;
using MorseRunner.Dsp;

namespace MorseRunner.Dsp.Tests;

public sealed class LegacyStationMixerTests
{
    private const int SampleRate = 11_025;

    [Fact]
    public void NegativePitchAndRitMatchFpcSingleVector()
    {
        float[] envelope =
        [
            0f,
            0.00003125f,
            1f,
            19.5f,
            19_583.306640625f,
            321.125f,
            0.5f,
            0f,
        ];
        float[] real =
        [
            0.25f,
            -0.5f,
            1.25f,
            -2.5f,
            12.75f,
            -20.125f,
            0f,
            -0.000125f,
        ];
        float[] imaginary =
        [
            -0.75f,
            0.125f,
            2.5f,
            -1.5f,
            -8.25f,
            17.75f,
            0.25f,
            0.00025f,
        ];
        uint[] expectedRealBits =
        [
            0x3e800000,
            0xbefffd56,
            0x3fe7e0c7,
            0x40d25a47,
            0x45dead0c,
            0x427906ce,
            0x3d9628bf,
            0xb903126f,
        ];
        uint[] expectedImaginaryBits =
        [
            0xbf400000,
            0x3e000638,
            0x4054f4db,
            0x417c2a94,
            0x468e7b4c,
            0x43a41064,
            0x3f3e9dc7,
            0x3983126f,
        ];
        // These bits come from the same expressions compiled by
        // FPC 3.2.2 for x86_64-win64.
        var mixer = new LegacyStationMixer(SampleRate);

        mixer.BeginTransmission(pitchOffsetHz: -124);
        mixer.MixBlock(
            envelope,
            real,
            imaginary,
            ritHz: 73,
            ritPhase: 0.75f);

        Assert.Equal(expectedRealBits, ToBits(real));
        Assert.Equal(expectedImaginaryBits, ToBits(imaginary));
        Assert.Equal(0xbf10ba67u, Bits(mixer.BfoPhase));
        Assert.Equal(0xbd90ba66u, Bits(mixer.BfoPhaseIncrement));
    }

    [Fact]
    public void PositiveBfoWrapAndRitAdvanceMatchFpcSingleVector()
    {
        var envelope = new float[512];
        var real = new float[512];
        var imaginary = new float[512];
        var mixer = new LegacyStationMixer(SampleRate);

        mixer.BeginTransmission(pitchOffsetHz: 600);
        mixer.MixBlock(
            envelope,
            real,
            imaginary,
            ritHz: 0,
            ritPhase: 0f);
        float ritPhase = LegacyStationMixer.AdvanceRitPhase(
            ritPhase: 0.75f,
            blockSize: 512,
            ritHz: 73,
            sampleRate: SampleRate);

        Assert.Equal(0x40adb4aau, Bits(mixer.BfoPhase));
        Assert.Equal(0x3eaf1308u, Bits(mixer.BfoPhaseIncrement));
        Assert.Equal(0x404cdfbbu, Bits(ritPhase));
    }

    [Fact]
    public void BeginTransmissionResetsOscillatorPhase()
    {
        var envelope = new float[8];
        var real = new float[8];
        var imaginary = new float[8];
        var mixer = new LegacyStationMixer(SampleRate);
        mixer.BeginTransmission(pitchOffsetHz: 600);
        mixer.MixBlock(
            envelope,
            real,
            imaginary,
            ritHz: 0,
            ritPhase: 0f);

        mixer.BeginTransmission(pitchOffsetHz: -124);

        Assert.Equal(0u, Bits(mixer.BfoPhase));
        Assert.Equal(0xbd90ba66u, Bits(mixer.BfoPhaseIncrement));
    }

    [Fact]
    public void StationMixingAllocatesNoManagedMemory()
    {
        var envelope = new float[512];
        var real = new float[512];
        var imaginary = new float[512];
        var mixer = new LegacyStationMixer(SampleRate);
        mixer.BeginTransmission(pitchOffsetHz: -124);

        MixRepeatedly(mixer, envelope, real, imaginary);
        long before = GC.GetAllocatedBytesForCurrentThread();
        MixRepeatedly(mixer, envelope, real, imaginary);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void MixRepeatedly(
        LegacyStationMixer mixer,
        float[] envelope,
        float[] real,
        float[] imaginary)
    {
        for (int iteration = 0; iteration < 64; iteration++)
        {
            mixer.MixBlock(
                envelope,
                real,
                imaginary,
                ritHz: 0,
                ritPhase: 0f);
        }
    }

    private static uint[] ToBits(float[] values)
    {
        var result = new uint[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            result[index] = Bits(values[index]);
        }

        return result;
    }

    private static uint Bits(float value) =>
        BitConverter.SingleToUInt32Bits(value);
}
