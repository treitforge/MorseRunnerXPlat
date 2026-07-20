using System.Globalization;
using MorseRunner.Domain;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatQsbRuntimeToggleTarget : IParityTarget
{
    internal const string ParityId =
        "audio.qsb-runtime-toggle-active-station-seed-12345";
    internal const string FunctionalDivergenceCode =
        "audio-qsb-runtime-toggle-active-station-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Engine.SimulatedStation"
        + "+MorseRunner.Domain.SessionSettings.Qsb"
        + "+MorseRunner.Domain.AdjustRadioControlCommand";

    public Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!StringComparer.Ordinal.Equals(scenario.Id, ParityId))
        {
            return Task.FromResult(
                new ParityObservation(
                    ParityTargetOutcome.Failed,
                    [],
                    DomainErrorCodes.UnsupportedCapability,
                    EvidenceSource));
        }

        QsbRuntimeToggleInput input =
            QsbRuntimeToggleInput.Parse(scenario);
        string configuration =
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
            + "|sample-format=raw-ce-single-envelope";
        string[] values =
        [
            configuration,
            "runtime-qsb-toggle"
            + "|supported=false"
            + "|reason=session-settings-immutable",
        ];
        return Task.FromResult(
            new ParityObservation(
                ParityTargetOutcome.Failed,
                values,
                FunctionalDivergenceCode,
                EvidenceSource));
    }

    private static string Format(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
