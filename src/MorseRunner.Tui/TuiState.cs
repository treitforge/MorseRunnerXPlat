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
    private readonly Dictionary<ContestId, string> _operatorExchanges = [];

    public int Seed { get; set; }

    public int ContestIndex { get; set; }

    public int RunModeIndex { get; set; }

    public int DurationIndex { get; set; } = 30;

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

    public string StationCall { get; set; } = "VE3NEA";

    public string OperatorExchange
    {
        get => _operatorExchanges.TryGetValue(
            Contest.Id,
            out string? value)
                ? value
                : Contest.ExchangeDefault;
        set => SetOperatorExchange(Contest.Id, value);
    }

    public void SetOperatorExchange(ContestId contestId, string value) =>
        _operatorExchanges[contestId] = value.Trim().ToUpperInvariant();

    public string GetOperatorExchange(ContestId contestId) =>
        _operatorExchanges.TryGetValue(contestId, out string? value)
            ? value
            : ContestCatalog.Get(contestId).ExchangeDefault;

    public int WordsPerMinute { get; set; } = 25;

    public int PitchHz { get; set; } = 450;

    public int BandwidthHz { get; set; } = 550;

    public int Activity { get; set; } = 2;

    public int CompetitionDurationMinutes { get; set; } = 60;

    public double MonitorLevelDb { get; set; }

    public int ReceiveSpeedBelowWpm { get; set; }

    public int ReceiveSpeedAboveWpm { get; set; }

    public SerialNumberRangeMode SerialNumberRange { get; set; }

    public int CustomSerialNumberMinimum { get; set; } = 1;

    public int CustomSerialNumberExclusiveMaximum { get; set; } = 99;

    public int CustomSerialNumberMinimumDigits { get; set; } = 2;

    public int CustomSerialNumberMaximumDigits { get; set; } = 2;

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

    public bool UsesRstEntry => String.Equals(
        Contest.ExchangeType1,
        "etRST",
        StringComparison.Ordinal);

    public IReadOnlyList<int> VisibleEntryFields => UsesRstEntry
        ? [0, 1, 2, 3]
        : [0, 2, 3];

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
        Enumerable.Range(0, 241).ToArray();

    public void ClearEntry()
    {
        Call = string.Empty;
        Rst = "5NN";
        Exchange1 = string.Empty;
        Exchange2 = string.Empty;
        ActiveField = 0;
    }

    public void MoveActiveField(int direction)
    {
        IReadOnlyList<int> fields = VisibleEntryFields;
        int currentIndex = -1;
        for (int index = 0; index < fields.Count; index++)
        {
            if (fields[index] == ActiveField)
            {
                currentIndex = index;
                break;
            }
        }
        if (currentIndex < 0)
        {
            ActiveField = fields[0];
            return;
        }

        ActiveField = fields[(currentIndex + direction + fields.Count) % fields.Count];
    }

    public void NormalizeActiveField()
    {
        if (!VisibleEntryFields.Contains(ActiveField))
        {
            ActiveField = VisibleEntryFields[0];
        }
    }
}
