using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MorseRunner.Audio;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatQsbRuntimeToggleTarget : IParityTarget
{
    internal const string ParityId =
        "audio.qsb-runtime-toggle-active-station-seed-12345";
    internal const string FunctionalDivergenceCode =
        "audio-qsb-runtime-toggle-active-station-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Engine.MorseRunnerEngine"
        + "+MorseRunner.Engine.EngineSession"
        + ".ObserveQsbRuntimeForParityAsync"
        + "+MorseRunner.Engine.SimulatedStation.RenderBlock"
        + "+MorseRunner.Domain.SetRadioConditionCommand"
        + "+MorseRunner.Dsp.QsbProcessor";

    public async Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!StringComparer.Ordinal.Equals(scenario.Id, ParityId))
        {
            return new(
                ParityTargetOutcome.Failed,
                [],
                DomainErrorCodes.UnsupportedCapability,
                EvidenceSource);
        }

        QsbRuntimeToggleInput input =
            QsbRuntimeToggleInput.Parse(scenario);
        string[] values = await ObserveAsync(input, cancellationToken);
        bool matches = values.SequenceEqual(
            scenario.ExpectedValues,
            StringComparer.Ordinal);
        return new(
            matches
                ? ParityTargetOutcome.Passed
                : ParityTargetOutcome.Failed,
            values,
            matches ? null : FunctionalDivergenceCode,
            EvidenceSource);
    }

    internal static async Task<string[]> ObserveAsync(
        QsbRuntimeToggleInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureLittleEndianSingleStorage();

        QsbRuntimeParityObservation disabled = await CaptureAsync(
            input,
            runtimeToggle: false,
            cancellationToken);
        QsbRuntimeParityObservation runtimeToggle = await CaptureAsync(
            input,
            runtimeToggle: true,
            cancellationToken);
        return Normalize(input, disabled, runtimeToggle);
    }

    internal static async Task<QsbRuntimeParityObservation> CaptureAsync(
        QsbRuntimeToggleInput input,
        bool runtimeToggle,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        await using var engine = new MorseRunnerEngine(
            _ => new NullAudioSink(),
            new MorseRunnerEngineOptions
            {
                AutomaticTiming = false,
            });
        SessionSettings settings = new(
            input.Seed,
            new ContestId("scWpx"),
            new RunModeId("rmStop"),
            DurationBlocks: 0)
        {
            StationCall = "W7SST",
            WordsPerMinute = 30,
            PitchHz = 600,
            BandwidthHz = 500,
            Activity = 1,
            ReceiveSpeedBelowWpm = 0,
            ReceiveSpeedAboveWpm = 0,
            Qsk = false,
            Qsb = false,
            Qrm = false,
            Qrn = false,
            Flutter = false,
            Lids = false,
        };
        SessionHandle handle = await engine.CreateSessionAsync(
            settings,
            cancellationToken);
        return await engine.ObserveQsbRuntimeForParityAsync(
            handle.SessionId,
            handle.Revision,
            expectedSimulationBlock: 0,
            stationCall: "K1ABC",
            input.MessageText,
            input.ComparedBlockCount,
            input.ToggleAfterBlockCount,
            runtimeToggle,
            cancellationToken);
    }

    internal static string[] Normalize(
        QsbRuntimeToggleInput input,
        QsbRuntimeParityObservation disabled,
        QsbRuntimeParityObservation runtimeToggle)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(disabled);
        ArgumentNullException.ThrowIfNull(runtimeToggle);
        ValidateCapture(input, disabled, "disabled");
        ValidateCapture(input, runtimeToggle, "runtime-toggle");

        var values = new List<string>(
            QsbRuntimeToggleInput.ExpectedValueCount)
        {
            "configuration"
            + $"|sample-rate={Format(input.SampleRate)}"
            + $"|block-size={Format(input.BlockSize)}"
            + $"|seed={Format(input.Seed)}"
            + "|compared-block-count="
            + Format(input.ComparedBlockCount)
            + "|toggle-after-block-count="
            + Format(input.ToggleAfterBlockCount)
            + "|probe-sample-indexes="
            + String.Join(
                ',',
                input.ProbeSampleIndexes.Select(Format))
            + "|fresh-runs=disabled,runtime-toggle"
            + "|station-count=1"
            + "|station-call=K1ABC"
            + $"|message={input.MessageText}"
            + "|sample-format=raw-ce-single-envelope",
        };

        AddBlockRows(values, "disabled", input, disabled.Blocks);
        AddBlockRows(
            values,
            "runtime-toggle",
            input,
            runtimeToggle.Blocks);
        for (int blockIndex = 0;
             blockIndex < input.ComparedBlockCount;
             blockIndex++)
        {
            string disabledHash =
                ComputeRawSingleSha256(disabled.Blocks[blockIndex]);
            string runtimeToggleHash =
                ComputeRawSingleSha256(runtimeToggle.Blocks[blockIndex]);
            values.Add(
                $"runtime-transition[{Format(blockIndex)}]"
                + "|qsb-enabled="
                + Format(blockIndex >= input.ToggleAfterBlockCount)
                + $"|disabled-float-sha256={disabledHash}"
                + "|runtime-toggle-float-sha256="
                + runtimeToggleHash
                + "|exact-equal="
                + Format(
                    SamplesEqual(
                        disabled.Blocks[blockIndex],
                        runtimeToggle.Blocks[blockIndex])));
        }

        float[] disabledAggregate =
            CreateAggregate(input, disabled.Blocks);
        float[] runtimeToggleAggregate =
            CreateAggregate(input, runtimeToggle.Blocks);
        values.Add(
            "aggregate-transition"
            + $"|sample-count={Format(disabledAggregate.Length)}"
            + "|disabled-float-sha256="
            + ComputeRawSingleSha256(disabledAggregate)
            + "|runtime-toggle-float-sha256="
            + ComputeRawSingleSha256(runtimeToggleAggregate)
            + "|exact-equal="
            + Format(
                SamplesEqual(
                    disabledAggregate,
                    runtimeToggleAggregate)));
        values.Add(
            "terminal-random"
            + $"|disabled-value={Format(disabled.TerminalRandom)}"
            + "|disabled-single-bits="
            + SingleBits(disabled.TerminalRandom)
            + "|runtime-toggle-value="
            + Format(runtimeToggle.TerminalRandom)
            + "|runtime-toggle-single-bits="
            + SingleBits(runtimeToggle.TerminalRandom));

        if (values.Count != QsbRuntimeToggleInput.ExpectedValueCount)
        {
            throw new InvalidDataException(
                "The QSB runtime-toggle capture emitted an invalid "
                + "row count.");
        }

        return [.. values];
    }

    private static void AddBlockRows(
        List<string> values,
        string run,
        QsbRuntimeToggleInput input,
        IReadOnlyList<float[]> blocks)
    {
        for (int blockIndex = 0;
             blockIndex < input.ComparedBlockCount;
             blockIndex++)
        {
            float[] samples = blocks[blockIndex];
            string probeBits = String.Join(
                ',',
                input.ProbeSampleIndexes.Select(
                    sampleIndex =>
                        SingleBits(samples[sampleIndex])));
            values.Add(
                $"{run}-block[{Format(blockIndex)}]"
                + $"|sample-count={Format(samples.Length)}"
                + $"|probe-bits={probeBits}"
                + "|float-sha256="
                + ComputeRawSingleSha256(samples));
        }
    }

    private static void ValidateCapture(
        QsbRuntimeToggleInput input,
        QsbRuntimeParityObservation observation,
        string run)
    {
        if (observation.Blocks.Count != input.ComparedBlockCount
            || !float.IsFinite(observation.TerminalRandom))
        {
            throw new InvalidDataException(
                $"The {run} QSB runtime capture is invalid.");
        }

        for (int blockIndex = 0;
             blockIndex < observation.Blocks.Count;
             blockIndex++)
        {
            float[] block = observation.Blocks[blockIndex];
            if (block.Length != input.BlockSize
                || block.Any(sample => !float.IsFinite(sample)))
            {
                throw new InvalidDataException(
                    $"The {run} QSB runtime block {blockIndex} is "
                    + "invalid.");
            }
        }
    }

    private static float[] CreateAggregate(
        QsbRuntimeToggleInput input,
        IReadOnlyList<float[]> blocks)
    {
        var aggregate =
            new float[input.ComparedBlockCount * input.BlockSize];
        for (int blockIndex = 0;
             blockIndex < blocks.Count;
             blockIndex++)
        {
            blocks[blockIndex].CopyTo(
                aggregate,
                blockIndex * input.BlockSize);
        }

        return aggregate;
    }

    private static bool SamplesEqual(
        float[] first,
        float[] second)
    {
        if (first.Length != second.Length)
        {
            return false;
        }

        for (int index = 0; index < first.Length; index++)
        {
            if (BitConverter.SingleToUInt32Bits(first[index])
                != BitConverter.SingleToUInt32Bits(second[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static string ComputeRawSingleSha256(
        ReadOnlySpan<float> samples)
    {
        return Convert.ToHexStringLower(
            SHA256.HashData(MemoryMarshal.AsBytes(samples)));
    }

    private static string SingleBits(float value)
    {
        return BitConverter.SingleToUInt32Bits(value).ToString(
            "x8",
            CultureInfo.InvariantCulture);
    }

    private static string Format(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Format(bool value)
    {
        return value ? "true" : "false";
    }

    private static string Format(float value)
    {
        return ((double)value).ToString(
            "0.000000000",
            CultureInfo.InvariantCulture);
    }

    private static void EnsureLittleEndianSingleStorage()
    {
        if (!BitConverter.IsLittleEndian
            || Marshal.SizeOf<float>() != sizeof(uint))
        {
            throw new PlatformNotSupportedException(
                "CE raw Single parity hashing requires 32-bit "
                + "little-endian sample storage.");
        }
    }
}
