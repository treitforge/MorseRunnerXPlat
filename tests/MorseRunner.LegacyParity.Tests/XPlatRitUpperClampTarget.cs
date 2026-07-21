using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatRitUpperClampTarget : IParityTarget
{
    internal const string ParityId =
        "audio.rit-upper-clamp-extra-click-second-caller-block-seed-12345";
    internal const string FunctionalDivergenceCode =
        "audio-RIT-upper-clamp-mismatch";
    internal const string EvidenceSource =
        XPlatRuntimeRitChangeTarget.EvidenceSource;

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
                MorseRunner.Domain.DomainErrorCodes.UnsupportedCapability,
                EvidenceSource);
        }

        RitUpperClampInput input = RitUpperClampInput.Parse(scenario);
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
        RitUpperClampInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        XPlatRuntimeRitChangeTarget.EnsureLittleEndianSingleStorage();
        RuntimeRitChangeInput captureInput = input.ToCaptureInput();
        XPlatRuntimeRitChangeTarget.CapturedRun fixedRit =
            await XPlatRuntimeRitChangeTarget.CaptureAsync(
                captureInput,
                input.FixedClickCount,
                cancellationToken);
        XPlatRuntimeRitChangeTarget.CapturedRun clampedRit =
            await XPlatRuntimeRitChangeTarget.CaptureAsync(
                captureInput,
                input.ClampedClickCount,
                cancellationToken);
        return Normalize(input, fixedRit, clampedRit);
    }

    internal static string[] Normalize(
        RitUpperClampInput input,
        XPlatRuntimeRitChangeTarget.CapturedRun fixedRit,
        XPlatRuntimeRitChangeTarget.CapturedRun clampedRit)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(fixedRit);
        ArgumentNullException.ThrowIfNull(clampedRit);
        RuntimeRitChangeInput captureInput = input.ToCaptureInput();
        XPlatRuntimeRitChangeTarget.ValidateRun(
            captureInput,
            fixedRit,
            "fixed-upper-RIT");
        XPlatRuntimeRitChangeTarget.ValidateRun(
            captureInput,
            clampedRit,
            "extra-click-RIT");

        int firstDivergence = XPlatRuntimeRitChangeTarget.FirstDivergence(
            fixedRit.Blocks[1].Samples,
            clampedRit.Blocks[1].Samples);
        var values = new List<string>(RitUpperClampInput.ExpectedValueCount)
        {
            "configuration"
            + "|run-mode=rmStop"
            + $"|seed={Format(input.Seed)}"
            + $"|sample-rate={Format(input.SampleRate)}"
            + $"|block-size={Format(input.BlockSize)}"
            + "|startup-requests="
            + Format(input.StartupRequestCount)
            + "|absolute-blocks=6,7"
            + "|rit-step-hz=" + Format(input.RitStepHz)
            + "|fixed-clicks=" + Format(input.FixedClickCount)
            + "|clamped-clicks=" + Format(input.ClampedClickCount)
            + "|upper-bound-hz=" + Format(input.UpperBoundHz)
            + "|fixed-result-hz="
            + Format(fixedRit.Snapshot.RitOffsetHz)
            + "|clamped-result-hz="
            + Format(clampedRit.Snapshot.RitOffsetHz)
            + "|change-before-absolute-block=7"
            + "|handler=TMainForm.Panel8MouseDown"
            + "|qrn=false|qrm=false|qsb=false|flutter=false|qsk=false|lids=false",
            "remote-station"
            + "|class=TScriptedStation"
            + "|pitch-offset-hz=" + Format(input.RemotePitchHz)
            + "|amplitude=" + Format(input.RemoteAmplitude)
            + "|message=" + input.MessageText
            + "|rendered-samples=" + Format(input.BlockSize * 2)
            + "|probe-sample-indexes="
            + String.Join(',', input.ProbeSampleIndexes.Select(Format)),
        };
        XPlatRuntimeRitChangeTarget.AddBlockStatistics(
            values,
            "rit-before-upper-bound-block[0]",
            fixedRit.Blocks[0].Samples,
            input.ProbeSampleIndexes);
        XPlatRuntimeRitChangeTarget.AddBlockStatistics(
            values,
            "rit-plus-500-block[1]",
            fixedRit.Blocks[1].Samples,
            input.ProbeSampleIndexes);
        XPlatRuntimeRitChangeTarget.AddBlockStatistics(
            values,
            "rit-extra-click-clamped-block[1]",
            clampedRit.Blocks[1].Samples,
            input.ProbeSampleIndexes);
        values.Add(
            "comparison"
            + "|exact-equal=" + Format(firstDivergence < 0)
            + "|first-divergence=" + Format(firstDivergence)
            + "|rit-plus-500-float-sha256="
            + XPlatRuntimeRitChangeTarget.ComputeRawSingleSha256(
                fixedRit.Blocks[1].Samples)
            + "|rit-extra-click-float-sha256="
            + XPlatRuntimeRitChangeTarget.ComputeRawSingleSha256(
                clampedRit.Blocks[1].Samples));
        values.Add(
            "terminal-random"
            + "|ordinal=" + Format(input.TerminalRandomOrdinal)
            + "|rit-plus-500-single-bits="
            + XPlatRuntimeRitChangeTarget.SingleBits(
                fixedRit.TerminalRandom)
            + "|rit-extra-click-single-bits="
            + XPlatRuntimeRitChangeTarget.SingleBits(
                clampedRit.TerminalRandom));

        if (values.Count != RitUpperClampInput.ExpectedValueCount)
        {
            throw new InvalidDataException(
                "The RIT upper-clamp capture emitted an invalid row "
                + "count.");
        }

        return [.. values];
    }

    private static string Format(int value) =>
        XPlatRuntimeRitChangeTarget.Format(value);

    private static string Format(bool value) =>
        XPlatRuntimeRitChangeTarget.Format(value);
}

internal sealed record RitUpperClampInput(
    int SampleRate,
    int BlockSize,
    int Seed,
    int StartupRequestCount,
    int RitStepHz,
    int UpperBoundHz,
    int FixedClickCount,
    int ClampedClickCount,
    int RemotePitchHz,
    int RemoteAmplitude,
    string MessageText,
    int TerminalRandomOrdinal,
    IReadOnlyList<int> ProbeSampleIndexes)
{
    internal const int ExpectedValueCount = 7;

    public static RitUpperClampInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames =
        [
            "blockSize",
            "clampedClickCount",
            "fixedClickCount",
            "messageText",
            "probeSampleIndexes",
            "remoteAmplitude",
            "remotePitchHz",
            "ritStepHz",
            "sampleRate",
            "scenario",
            "seed",
            "startupRequestCount",
            "terminalRandomOrdinal",
            "upperBoundHz",
        ];
        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' input has unsupported fields.");
        }

        int[] probes = input
            .GetProperty("probeSampleIndexes")
            .EnumerateArray()
            .Select(value => value.GetInt32())
            .ToArray();
        var result = new RitUpperClampInput(
            input.GetProperty("sampleRate").GetInt32(),
            input.GetProperty("blockSize").GetInt32(),
            input.GetProperty("seed").GetInt32(),
            input.GetProperty("startupRequestCount").GetInt32(),
            input.GetProperty("ritStepHz").GetInt32(),
            input.GetProperty("upperBoundHz").GetInt32(),
            input.GetProperty("fixedClickCount").GetInt32(),
            input.GetProperty("clampedClickCount").GetInt32(),
            input.GetProperty("remotePitchHz").GetInt32(),
            input.GetProperty("remoteAmplitude").GetInt32(),
            RequireString(input, "messageText", scenario.Id),
            input.GetProperty("terminalRandomOrdinal").GetInt32(),
            probes);
        string discriminator = RequireString(input, "scenario", scenario.Id);
        int[] expectedProbes =
            [0, 1, 2, 148, 149, 150, 255, 310, 384, 509, 510, 511];
        if (discriminator != XPlatRitUpperClampTarget.ParityId
            || result.SampleRate != 11_025
            || result.BlockSize != 512
            || result.Seed != 12_345
            || result.StartupRequestCount != 5
            || result.RitStepHz != 50
            || result.UpperBoundHz != 500
            || result.FixedClickCount != 10
            || result.ClampedClickCount != 11
            || result.RemotePitchHz != 360
            || result.RemoteAmplitude != 18_000
            || result.MessageText != "TEST TEST"
            || result.TerminalRandomOrdinal != 2_048
            || !probes.SequenceEqual(expectedProbes)
            || scenario.ExpectedValues.Count != ExpectedValueCount)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' fixed vector is invalid.");
        }

        return result;
    }

    internal RuntimeRitChangeInput ToCaptureInput() =>
        new(
            SampleRate,
            BlockSize,
            Seed,
            StartupRequestCount,
            RitStepHz,
            RemotePitchHz,
            RemoteAmplitude,
            MessageText,
            TerminalRandomOrdinal,
            ProbeSampleIndexes);

    private static string RequireString(
        JsonElement input,
        string propertyName,
        string scenarioId)
    {
        JsonElement value = input.GetProperty(propertyName);
        string? result = value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
        return !String.IsNullOrEmpty(result)
            ? result
            : throw new InvalidDataException(
                $"Parity case '{scenarioId}' {propertyName} is invalid.");
    }
}
