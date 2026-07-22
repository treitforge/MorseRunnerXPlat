using MorseRunner.Domain;
using MorseRunner.Dsp;
using MorseRunner.Engine;

namespace MorseRunner.Engine.Tests;

public sealed class SimulatedStationAudioTests
{
    [Fact]
    public void CallerMixUsesCeSinglePrecisionBfoAndRitFormula()
    {
        const int PitchOffsetHz = 360;
        const int RitOffsetHz = 50;
        const float RitPhase = 0.75f;
        const float Amplitude = 18_000f;
        SimulatedStation station = CreateStation(
            PitchOffsetHz,
            Amplitude);
        var actualReal = new float[SimulationAudioProfile.BlockSize];
        var actualImaginary = new float[SimulationAudioProfile.BlockSize];
        var envelope = new float[SimulationAudioProfile.BlockSize];

        station.RenderBlock(
            actualReal,
            actualImaginary,
            qsbEnabled: false,
            RitOffsetHz,
            RitPhase,
            envelopeObservation: envelope);

        var expectedEnvelope = new float[envelope.Length];
        for (int index = 0; index < envelope.Length; index++)
        {
            expectedEnvelope[index] = envelope[index] * Amplitude;
        }

        var expectedReal = new float[SimulationAudioProfile.BlockSize];
        var expectedImaginary = new float[SimulationAudioProfile.BlockSize];
        var mixer = new StationMixer(SimulationAudioProfile.SampleRate);
        mixer.BeginTransmission(PitchOffsetHz);
        mixer.MixBlock(
            expectedEnvelope,
            expectedReal,
            expectedImaginary,
            RitOffsetHz,
            RitPhase);

        Assert.Equal(expectedReal, actualReal);
        Assert.Equal(expectedImaginary, actualImaginary);
    }

    [Fact]
    public void RuntimeRitChangeAffectsOnlyTheChangedCallerMix()
    {
        SimulatedStation fixedRit = CreateStation(360, 18_000f);
        SimulatedStation changedRit = CreateStation(360, 18_000f);
        var fixedReal = new float[SimulationAudioProfile.BlockSize];
        var fixedImaginary = new float[SimulationAudioProfile.BlockSize];
        var changedReal = new float[SimulationAudioProfile.BlockSize];
        var changedImaginary = new float[SimulationAudioProfile.BlockSize];

        fixedRit.RenderBlock(
            fixedReal,
            fixedImaginary,
            qsbEnabled: false,
            ritOffsetHz: 0,
            ritPhase: 0f);
        changedRit.RenderBlock(
            changedReal,
            changedImaginary,
            qsbEnabled: false,
            ritOffsetHz: 0,
            ritPhase: 0f);
        Assert.Equal(fixedReal, changedReal);
        Assert.Equal(fixedImaginary, changedImaginary);

        Array.Clear(fixedReal);
        Array.Clear(fixedImaginary);
        Array.Clear(changedReal);
        Array.Clear(changedImaginary);
        fixedRit.RenderBlock(
            fixedReal,
            fixedImaginary,
            qsbEnabled: false,
            ritOffsetHz: 0,
            ritPhase: 0f);
        changedRit.RenderBlock(
            changedReal,
            changedImaginary,
            qsbEnabled: false,
            ritOffsetHz: 50,
            ritPhase: 0f);

        Assert.False(fixedReal.SequenceEqual(changedReal));
        Assert.False(fixedImaginary.SequenceEqual(changedImaginary));
    }

    private static SimulatedStation CreateStation(
        int pitchOffsetHz,
        float amplitude) =>
        SimulatedStation.CreateScripted(
            new("N0CALL", "599", 1, string.Empty, string.Empty),
            wordsPerMinute: 30,
            pitchOffsetHz,
            amplitude,
            OperatorRunMode.Stop,
            message: "TEST TEST");
}
