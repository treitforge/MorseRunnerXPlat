using MorseRunner.Domain;
using MorseRunner.Infrastructure;

namespace MorseRunner.Tui;

public enum TuiView
{
    Operator,
    Settings,
    Results,
    Diagnostics,
    Help,
}

public sealed class TuiState
{
    public int Seed { get; init; } = 12_345;

    public int ContestIndex { get; set; }

    public int RunModeIndex { get; set; }

    public int DurationIndex { get; set; }

    public int ActiveField { get; set; }

    public string Call { get; set; } = string.Empty;

    public string Rst { get; set; } = "5NN";

    public string Exchange1 { get; set; } = string.Empty;

    public string Exchange2 { get; set; } = string.Empty;

    public string Status { get; set; } =
        "Ready. F9 starts a pile-up session. Press ? for help.";

    public bool Qsk { get; set; }

    public bool Qsb { get; set; }

    public bool Qrm { get; set; }

    public bool Qrn { get; set; }

    public bool Flutter { get; set; }

    public bool Lids { get; set; }

    public bool IsHosted { get; init; }

    public TuiView View { get; set; }

    public int SettingsIndex { get; set; }

    public string StationCall { get; set; } = "W7SST";

    public int WordsPerMinute { get; set; } = 30;

    public int PitchHz { get; set; } = 600;

    public int BandwidthHz { get; set; } = 500;

    public int Activity { get; set; } = 5;

    public double MonitorLevelDb { get; set; }

    public int ReceiveSpeedBelowWpm { get; set; }

    public int ReceiveSpeedAboveWpm { get; set; }

    public SerialNumberRangeMode SerialNumberRange { get; set; }

    public int CustomSerialNumberMinimum { get; set; } = 1;

    public int CustomSerialNumberExclusiveMaximum { get; set; } = 99;

    public string HstOperatorName { get; set; } = string.Empty;

    public bool RecordingEnabled { get; set; }

    public string? LastRecordingPath { get; set; }

    public SessionResult? Result { get; set; }

    public ContestHighScore? PersonalHighScore { get; set; }

    public string? LastExportPath { get; set; }

    public string ConnectionStatus { get; set; } = "Local in-process engine.";

    public string EngineDiagnostic { get; set; } = "Engine information not loaded.";

    public SessionSnapshot? Snapshot { get; set; }

    public IReadOnlyList<Qso> Qsos { get; set; } = [];

    public string[] Fields =>
        [Call, Rst, Exchange1, Exchange2];

    public ContestDefinition Contest => ContestCatalog.All[ContestIndex];

    public RunModeId RunMode => RunModes[RunModeIndex];

    public int DurationMinutes => DurationMinutesValues[DurationIndex];

    public static IReadOnlyList<RunModeId> RunModes { get; } =
    [
        new("rmPileup"),
        new("rmSingle"),
        new("rmWpx"),
        new("rmHst"),
    ];

    public static IReadOnlyList<int> DurationMinutesValues { get; } =
        [0, 5, 10, 15, 30, 60, 90, 120];

    public void ClearEntry()
    {
        Call = string.Empty;
        Rst = "5NN";
        Exchange1 = string.Empty;
        Exchange2 = string.Empty;
        ActiveField = 0;
    }
}
