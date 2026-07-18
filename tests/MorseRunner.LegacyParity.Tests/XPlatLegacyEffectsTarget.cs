using System.Globalization;
using MorseRunner.Domain;
using MorseRunner.Dsp;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatLegacyEffectsTarget : IParityTarget
{
    public Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string[] values = scenario.Id == "simulation.legacy-effects"
            ? Observe()
            : [];
        bool matches = values.SequenceEqual(
            scenario.ExpectedValues,
            StringComparer.Ordinal);
        return Task.FromResult(
            new ParityObservation(
                matches ? ParityTargetOutcome.Passed : ParityTargetOutcome.Failed,
                values,
                matches ? null : DomainErrorCodes.UnsupportedCapability,
                "MorseRunner.Dsp"));
    }

    private static string[] Observe()
    {
        var values = new List<string>();
        AddVector(values, "uniform", effects => effects.Uniform());
        AddVector(values, "ushaped", effects => effects.UShaped());
        AddVector(values, "normal", effects => effects.Normal());
        AddVector(values, "rayleigh", effects => effects.Rayleigh(2.5f));
        AddVector(
            values,
            "gauss-limited",
            effects => effects.GaussianLimited(5f, 1.5f));

        var poisson = CreateEffects();
        for (int index = 0; index < 8; index++)
        {
            values.Add($"poisson[{index}]={poisson.Poisson(3.25f)}");
        }

        values.Add(
            $"seconds-to-blocks={LegacyRandomEffects.SecondsToBlocks(1.25f)}");
        values.Add(
            $"blocks-to-seconds={Format(LegacyRandomEffects.BlocksToSeconds(12f))}");

        var samples = new float[512];
        Array.Fill(samples, 1f);
        var qsb = new QsbProcessor(CreateEffects())
        {
            Level = 0.75f,
            Bandwidth = 0.5f,
        };
        qsb.Apply(samples);
        double sum = 0d;
        foreach (float sample in samples)
        {
            sum += sample;
        }

        for (int index = 0; index < 8; index++)
        {
            values.Add($"qsb[{index * 64}]={Format(samples[index * 64])}");
        }

        values.Add($"qsb-sum={Format(sum)}");
        return [.. values];
    }

    private static void AddVector(
        List<string> values,
        string name,
        Func<LegacyRandomEffects, float> sample)
    {
        LegacyRandomEffects effects = CreateEffects();
        for (int index = 0; index < 8; index++)
        {
            values.Add($"{name}[{index}]={Format(sample(effects))}");
        }
    }

    private static LegacyRandomEffects CreateEffects() =>
        new(new LegacyRandom(12_345));

    private static string Format(float value) =>
        value.ToString("F9", CultureInfo.InvariantCulture);

    private static string Format(double value) =>
        value.ToString("F9", CultureInfo.InvariantCulture);
}
