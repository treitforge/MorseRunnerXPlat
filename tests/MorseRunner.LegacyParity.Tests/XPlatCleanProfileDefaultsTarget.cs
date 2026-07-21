using System.Globalization;
using System.Text.Json;
using MorseRunner.App.ViewModels;
using MorseRunner.Client;
using MorseRunner.Domain;
using MorseRunner.Tui;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatCleanProfileDefaultsTarget : IParityTarget
{
    internal const string ParityId = "settings.clean-profile-ce-defaults";
    internal const string FunctionalDivergenceCode =
        "settings-clean-profile-ce-defaults-mismatch";
    internal const string EvidenceSource =
        "SessionSettings.CreateDefault and clean Avalonia/TUI state";

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

        CleanProfileDefaultsInput input =
            CleanProfileDefaultsInput.Parse(scenario);
        SessionSettings domain = SessionSettings.CreateDefault(input.Seed);
        await using var viewModel = new MainWindowViewModel(
            InProcessMorseRunnerClient.CreateDefault());
        var tui = new TuiState { Seed = input.Seed };

        string[] values =
        [
            Format(
                "domain",
                domain.StationCall,
                domain.HstOperatorName,
                domain.WordsPerMinute,
                domain.PitchHz,
                domain.BandwidthHz,
                domain.Activity,
                DurationMinutes(domain.DurationBlocks),
                domain.CompetitionDurationMinutes,
                domain.ContestId.Value,
                domain.RunModeId.Value,
                domain.SerialNumberRange,
                domain.ReceiveSpeedBelowWpm,
                domain.ReceiveSpeedAboveWpm,
                domain.StationIdRate,
                domain.MonitorLevelDb,
                domain.Qsk,
                domain.Qsb,
                domain.Qrm,
                domain.Qrn,
                domain.Flutter,
                domain.Lids),
            Format(
                "avalonia",
                viewModel.StationCall,
                viewModel.HstOperatorName,
                viewModel.WordsPerMinute,
                viewModel.PitchHz,
                viewModel.BandwidthHz,
                viewModel.Activity,
                viewModel.DurationMinutes,
                viewModel.CompetitionDurationMinutes,
                viewModel.SelectedContest.Id.Value,
                viewModel.SelectedRunMode.Id.Value,
                viewModel.SelectedSerialNumberRange.Mode,
                viewModel.ReceiveSpeedBelowWpm,
                viewModel.ReceiveSpeedAboveWpm,
                3,
                viewModel.MonitorLevel,
                viewModel.Qsk,
                viewModel.Qsb,
                viewModel.Qrm,
                viewModel.Qrn,
                viewModel.Flutter,
                viewModel.Lids),
            Format(
                "tui",
                tui.StationCall,
                tui.HstOperatorName,
                tui.WordsPerMinute,
                tui.PitchHz,
                tui.BandwidthHz,
                tui.Activity,
                tui.DurationMinutes,
                tui.CompetitionDurationMinutes,
                tui.Contest.Id.Value,
                tui.RunMode.Value,
                tui.SerialNumberRange,
                tui.ReceiveSpeedBelowWpm,
                tui.ReceiveSpeedAboveWpm,
                3,
                tui.MonitorLevelDb,
                tui.Qsk,
                tui.Qsb,
                tui.Qrm,
                tui.Qrn,
                tui.Flutter,
                tui.Lids),
        ];
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

    private static string Format(
        string surface,
        string stationCall,
        string hstName,
        int wordsPerMinute,
        int pitchHz,
        int bandwidthHz,
        int activity,
        int durationMinutes,
        int competitionDurationMinutes,
        string contestId,
        string runModeId,
        SerialNumberRangeMode serialNumberRange,
        int receiveSpeedBelowWpm,
        int receiveSpeedAboveWpm,
        int stationIdRate,
        double monitorLevelDb,
        bool qsk,
        bool qsb,
        bool qrm,
        bool qrn,
        bool flutter,
        bool lids) =>
        "clean-profile-defaults"
        + "|surface=" + surface
        + "|station-call=" + stationCall
        + "|hst-name=" + hstName
        + "|wpm=" + Format(wordsPerMinute)
        + "|pitch-hz=" + Format(pitchHz)
        + "|bandwidth-hz=" + Format(bandwidthHz)
        + "|activity=" + Format(activity)
        + "|duration-minutes=" + Format(durationMinutes)
        + "|competition-duration-minutes="
        + Format(competitionDurationMinutes)
        + "|contest=" + contestId
        + "|default-run-mode=" + runModeId
        + "|serial=" + Format(serialNumberRange)
        + "|rx-below-wpm=" + Format(receiveSpeedBelowWpm)
        + "|rx-above-wpm=" + Format(receiveSpeedAboveWpm)
        + "|station-id-rate=" + Format(stationIdRate)
        + "|monitor-db=" + monitorLevelDb.ToString(
            "0.#########",
            CultureInfo.InvariantCulture)
        + "|qsk=" + Format(qsk)
        + "|qsb=" + Format(qsb)
        + "|qrm=" + Format(qrm)
        + "|qrn=" + Format(qrn)
        + "|flutter=" + Format(flutter)
        + "|lids=" + Format(lids);

    private static int DurationMinutes(long blocks) =>
        blocks == 0
            ? 0
            : checked((int)Math.Round(
                blocks
                * CompatibilityProfile.BlockSize
                / (60d * CompatibilityProfile.SampleRate),
                MidpointRounding.AwayFromZero));

    private static string Format(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string Format(bool value) =>
        value.ToString().ToLowerInvariant();

    private static string Format(SerialNumberRangeMode value) =>
        value switch
        {
            SerialNumberRangeMode.StartOfContest => "snStartContest",
            SerialNumberRangeMode.MidContest => "snMidContest",
            SerialNumberRangeMode.EndOfContest => "snEndContest",
            SerialNumberRangeMode.Custom => "snCustomRange",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
}

internal sealed record CleanProfileDefaultsInput(int Seed)
{
    public static CleanProfileDefaultsInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] expectedNames = ["scenario", "seed"];
        string[] actualNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var result = new CleanProfileDefaultsInput(
            input.GetProperty("seed").GetInt32());
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal)
            || input.GetProperty("scenario").GetString()
                != XPlatCleanProfileDefaultsTarget.ParityId
            || result != new CleanProfileDefaultsInput(12_345)
            || scenario.ExpectedValues.Count != 3)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' fixed vector is invalid.");
        }

        return result;
    }
}
