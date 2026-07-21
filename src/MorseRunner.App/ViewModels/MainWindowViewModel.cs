using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using MorseRunner.Client;
using MorseRunner.Domain;
using MorseRunner.Infrastructure;

namespace MorseRunner.App.ViewModels;

public sealed record ContestOption(ContestId Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record RunModeOption(RunModeId Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record DurationOption(int Minutes, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record SerialNumberRangeOption(
    SerialNumberRangeMode Mode,
    string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record QsoLogEntryViewModel(
    string Time,
    string Call,
    string Rst,
    string Exchange,
    int Points,
    bool IsDuplicate)
{
    public string Result =>
        IsDuplicate
            ? "DUP"
            : Points.ToString(CultureInfo.InvariantCulture);
}

public sealed class ScoreSummaryEventArgs(
    int Score,
    int QsoCount,
    int QsoRatePerHour,
    int HighScore,
    string Contest,
    string Elapsed) : EventArgs
{
    public int Score { get; } = Score;

    public int QsoCount { get; } = QsoCount;

    public int QsoRatePerHour { get; } = QsoRatePerHour;

    public int HighScore { get; } = HighScore;

    public string Contest { get; } = Contest;

    public string Elapsed { get; } = Elapsed;
}

public sealed class EntryFocusRequestedEventArgs(
    EntryFocusTarget target,
    bool selectQuestionMark) : EventArgs
{
    public EntryFocusTarget Target { get; } = target;

    public bool SelectQuestionMark { get; } = selectQuestionMark;
}

public sealed class MainWindowViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly ClientId DesktopClientId = new("avalonia-desktop");
    private static readonly IReadOnlyList<RunModeOption> RunModeOptions =
    [
        new(new("rmPileup"), "Pile-Up"),
        new(new("rmSingle"), "Single Calls"),
        new(new("rmWpx"), "WPX Competition"),
        new(new("rmHst"), "HST Competition"),
    ];
    private static readonly IReadOnlyList<DurationOption> DurationOptions =
    [
        new(0, "Unlimited"),
        new(5, "5 minutes"),
        new(10, "10 minutes"),
        new(15, "15 minutes"),
        new(30, "30 minutes"),
        new(60, "60 minutes"),
        new(90, "90 minutes"),
        new(120, "120 minutes"),
    ];
    private static readonly IReadOnlyList<SerialNumberRangeOption>
        SerialNumberRangeOptions =
        [
            new(SerialNumberRangeMode.StartOfContest, "Start of contest"),
            new(SerialNumberRangeMode.MidContest, "Mid contest (50-500)"),
            new(SerialNumberRangeMode.EndOfContest, "End contest (500-5000)"),
            new(SerialNumberRangeMode.Custom, "Custom range"),
        ];

    private readonly IMorseRunnerClient _client;
    private readonly RecordingPreference? _recordingPreference;
    private readonly SettingsStore? _settingsStore;
    private readonly HighScoreStore? _highScoreStore;
    private readonly string? _resultsDirectory;
    private readonly DxccDatabase _dxccDatabase;
    private readonly SynchronizationContext? _uiContext;
    private readonly object _snapshotApplicationGate = new();
    private readonly SemaphoreSlim _monitorLevelUpdateGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private SessionId? _sessionId;
    private SessionId? _recordedResultSessionId;
    private Task? _subscriptionTask;
    private SessionSnapshot? _pendingSnapshot;
    private int _snapshotDispatchScheduled;
    private long _lastAppliedSnapshotRevision = -1;
    private SessionState _sessionState = SessionState.Ready;
    private ContestOption _selectedContest;
    private RunModeOption _selectedRunMode = RunModeOptions[0];
    private DurationOption _selectedDuration = DurationOptions[0];
    private SerialNumberRangeOption _selectedSerialNumberRange =
        SerialNumberRangeOptions[0];
    private string _status = "Ready. Configure a contest and press F9.";
    private long _simulationBlock;
    private string _elapsed = "00:00.000";
    private string _lastCaller = "Waiting";
    private string _callerState = "Idle";
    private int _activeCallerCount;
    private string _lastSent = "None";
    private string _callEntry = string.Empty;
    private string _rstEntry = string.Empty;
    private string _exchange1Entry = string.Empty;
    private string _exchange2Entry = string.Empty;
    private string _stationCall = "W7SST";
    private int _seed = 12_345;
    private int _wordsPerMinute = 30;
    private int _pitchHz = 600;
    private int _bandwidthHz = 500;
    private int _ritOffsetHz;
    private int _activity = 5;
    private double _monitorLevel;
    private double _appliedMonitorLevelDb;
    private int _receiveSpeedBelowWpm;
    private int _receiveSpeedAboveWpm;
    private int _customSerialNumberMinimum = 1;
    private int _customSerialNumberExclusiveMaximum = 99;
    private int _customSerialNumberMinimumDigits = 2;
    private int _customSerialNumberMaximumDigits = 2;
    private string _hstOperatorName = string.Empty;
    private bool _showCallsignInformation = true;
    private string _callsignInformation = "No caller selected";
    private AudioOutputDevice? _selectedAudioOutputDevice;
    private string _audioDeviceStatus = "Audio devices have not been loaded.";
    private bool _qsk;
    private bool _qsb;
    private bool _qrm;
    private bool _qrn;
    private bool _flutter;
    private bool _lids;
    private int _qsoCount;
    private int _score;
    private int _qsoRatePerHour;
    private int _highScore;
    private string? _lastExportPath;
    private string _audioHealth = "Ready";
    private int _disposed;
    private int _initialized;

    public MainWindowViewModel(
        IMorseRunnerClient client,
        SynchronizationContext? uiContext = null,
        RecordingPreference? recordingPreference = null,
        SettingsStore? settingsStore = null,
        HighScoreStore? highScoreStore = null,
        string? resultsDirectory = null,
        DxccDatabase? dxccDatabase = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _uiContext = uiContext ?? SynchronizationContext.Current;
        _recordingPreference = recordingPreference;
        _settingsStore = settingsStore;
        _highScoreStore = highScoreStore;
        _resultsDirectory = resultsDirectory;
        _dxccDatabase = dxccDatabase ?? new DxccDatabase();
        Contests = ContestCatalog.All
            .Select(contest => new ContestOption(contest.Id, contest.DisplayName))
            .ToArray();
        RunModes = RunModeOptions;
        Durations = DurationOptions;
        _selectedContest = Contests[0];

        StartCommand = new AsyncCommand(
            () => StartAsync(SelectedRunMode),
            CanStart);
        StartSingleCommand = new AsyncCommand(
            () => StartAsync(RunModes[1]),
            CanStart);
        StartWpxCommand = new AsyncCommand(
            () => StartAsync(RunModes[2]),
            CanStart);
        StartHstCommand = new AsyncCommand(
            () => StartAsync(RunModes[3]),
            CanStart);
        AdvanceCommand = new AsyncCommand(
            AdvanceAsync,
            () => _sessionState == SessionState.Running);
        PauseCommand = new AsyncCommand(
            PauseAsync,
            () => _sessionState == SessionState.Running);
        ResumeCommand = new AsyncCommand(
            ResumeAsync,
            () => _sessionState == SessionState.Paused);
        StopCommand = new AsyncCommand(
            StopAsync,
            () => _sessionState is SessionState.Running or SessionState.Paused);
        SendCqCommand = CreateIntentCommand(OperatorIntent.Cq);
        SendExchangeCommand = CreateIntentCommand(OperatorIntent.Exchange);
        SendThankYouCommand = CreateIntentCommand(OperatorIntent.ThankYou);
        SendMyCallCommand = CreateIntentCommand(OperatorIntent.MyCall);
        SendHisCallCommand = CreateIntentCommand(OperatorIntent.HisCall);
        SendBeforeCommand = CreateIntentCommand(OperatorIntent.Before);
        SendQuestionCommand = CreateIntentCommand(OperatorIntent.Question);
        SendNilCommand = CreateIntentCommand(OperatorIntent.Nil);
        SendNumberQuestionCommand =
            CreateIntentCommand(OperatorIntent.NumberQuestion);
        AbortCommand = CreateIntentCommand(OperatorIntent.Abort);
        WipeCommand = new AsyncCommand(WipeAsync);
        EnterSendMessageCommand = new AsyncCommand(
            EnterSendMessageAsync,
            () => _sessionState == SessionState.Running);
        CompleteQsoCommand = new AsyncCommand(
            CompleteQsoAsync,
            () => _sessionState == SessionState.Running);
        LogQsoOnlyCommand = new AsyncCommand(
            LogQsoOnlyAsync,
            () => _sessionState == SessionState.Running);
        SendCallAndExchangeCommand = new AsyncCommand(
            SendCallAndExchangeAsync,
            () => _sessionState == SessionState.Running);
        RitUpCommand = CreateRadioCommand(RadioControl.Rit, 50);
        RitDownCommand = CreateRadioCommand(RadioControl.Rit, -50);
        BandwidthUpCommand = CreateRadioCommand(RadioControl.Bandwidth, 50);
        BandwidthDownCommand = CreateRadioCommand(RadioControl.Bandwidth, -50);
        SpeedUpCommand = new AsyncCommand(
            AdjustSpeedUpAsync,
            () => _sessionState is SessionState.Running or SessionState.Paused);
        SpeedDownCommand = CreateRadioCommand(RadioControl.Speed, -2);
        ShowScoreCommand = new AsyncCommand(ShowScoreAsync);
        ExportJsonCommand = new AsyncCommand(
            () => ExportResultAsync(ResultExportFormat.Json),
            CanExportResult);
        ExportCabrilloCommand = new AsyncCommand(
            () => ExportResultAsync(ResultExportFormat.Cabrillo),
            CanExportResult);
        OpenResultsFolderCommand = new AsyncCommand(
            OpenResultsFolderAsync,
            () => _resultsDirectory is not null
                && Directory.Exists(_resultsDirectory));
        PlayRecordingCommand = new AsyncCommand(
            PlayRecordingAsync,
            () => CanPlayRecording);
        RefreshAudioDevicesCommand =
            new AsyncCommand(RefreshAudioDevicesAsync);
        RecoverAudioCommand = new AsyncCommand(
            RecoverAudioAsync,
            () => _sessionId is not null
                && _selectedAudioOutputDevice is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<ScoreSummaryEventArgs>? ShowScoreRequested;

    public event EventHandler<EntryFocusRequestedEventArgs>?
        EntryFocusRequested;

    public IReadOnlyList<ContestOption> Contests { get; }

    public IReadOnlyList<RunModeOption> RunModes { get; }

    public IReadOnlyList<DurationOption> Durations { get; }

    public IReadOnlyList<SerialNumberRangeOption> SerialNumberRanges { get; } =
        SerialNumberRangeOptions;

    public ObservableCollection<QsoLogEntryViewModel> QsoLog { get; } = [];

    public ObservableCollection<AudioOutputDevice> AudioOutputDevices { get; } =
        [];

    public ContestOption SelectedContest
    {
        get => _selectedContest;
        set
        {
            if (value is not null && SetField(ref _selectedContest, value))
            {
                OnPropertyChanged(nameof(ContestName));
            }
        }
    }

    public RunModeOption SelectedRunMode
    {
        get => _selectedRunMode;
        set
        {
            if (value is not null && SetField(ref _selectedRunMode, value))
            {
                OnPropertyChanged(nameof(RunMode));
            }
        }
    }

    public DurationOption SelectedDuration
    {
        get => _selectedDuration;
        set
        {
            if (value is not null)
            {
                SetField(ref _selectedDuration, value);
            }
        }
    }

    public SerialNumberRangeOption SelectedSerialNumberRange
    {
        get => _selectedSerialNumberRange;
        set
        {
            if (value is not null
                && SetField(ref _selectedSerialNumberRange, value))
            {
                OnPropertyChanged(nameof(IsCustomSerialNumberRange));
            }
        }
    }

    public bool IsCustomSerialNumberRange =>
        SelectedSerialNumberRange.Mode == SerialNumberRangeMode.Custom;

    public string ContestName => SelectedContest.DisplayName;

    public string RunMode => SelectedRunMode.DisplayName;

    public string SessionStateLabel => _sessionState.ToString();

    public bool IsSetupEnabled =>
        _sessionState is SessionState.Ready
            or SessionState.Created
            or SessionState.Completed;

    public bool IsMonitorLevelEnabled =>
        IsSetupEnabled
        || _sessionState is SessionState.Running or SessionState.Paused;

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public long SimulationBlock
    {
        get => _simulationBlock;
        private set => SetField(ref _simulationBlock, value);
    }

    public string Elapsed
    {
        get => _elapsed;
        private set => SetField(ref _elapsed, value);
    }

    public string LastCaller
    {
        get => _lastCaller;
        private set => SetField(ref _lastCaller, value);
    }

    public string CallerState
    {
        get => _callerState;
        private set => SetField(ref _callerState, value);
    }

    public int ActiveCallerCount
    {
        get => _activeCallerCount;
        private set => SetField(ref _activeCallerCount, value);
    }

    public string LastSent
    {
        get => _lastSent;
        private set => SetField(ref _lastSent, value);
    }

    public string CallEntry
    {
        get => _callEntry;
        set => SetField(ref _callEntry, value);
    }

    public string RstEntry
    {
        get => _rstEntry;
        set => SetField(ref _rstEntry, value);
    }

    public string Exchange1Entry
    {
        get => _exchange1Entry;
        set => SetField(ref _exchange1Entry, value);
    }

    public string Exchange2Entry
    {
        get => _exchange2Entry;
        set => SetField(ref _exchange2Entry, value);
    }

    public string StationCall
    {
        get => _stationCall;
        set => SetField(ref _stationCall, value.ToUpperInvariant());
    }

    public int Seed
    {
        get => _seed;
        set => SetField(ref _seed, Math.Max(0, value));
    }

    public int WordsPerMinute
    {
        get => _wordsPerMinute;
        set => SetField(ref _wordsPerMinute, Math.Clamp(value, 10, 120));
    }

    public int PitchHz
    {
        get => _pitchHz;
        set => SetField(ref _pitchHz, Math.Clamp(value, 300, 900));
    }

    public int BandwidthHz
    {
        get => _bandwidthHz;
        set => SetField(ref _bandwidthHz, Math.Clamp(value, 100, 600));
    }

    public int RitOffsetHz
    {
        get => _ritOffsetHz;
        private set => SetField(ref _ritOffsetHz, value);
    }

    public int Activity
    {
        get => _activity;
        set => SetField(ref _activity, Math.Clamp(value, 1, 9));
    }

    public double MonitorLevel
    {
        get => _monitorLevel;
        set => SetField(ref _monitorLevel, Math.Clamp(value, -60d, 0d));
    }

    public async Task SetMonitorLevelAsync(double requestedLevelDb)
    {
        double requested = Math.Round(
            Math.Clamp(requestedLevelDb, -60d, 0d),
            MidpointRounding.AwayFromZero);
        MonitorLevel = requested;
        if (_sessionState is not (SessionState.Running or SessionState.Paused))
        {
            return;
        }

        try
        {
            await _monitorLevelUpdateGate.WaitAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }

        try
        {
            if (_sessionState is not (
                    SessionState.Running or SessionState.Paused))
            {
                return;
            }

            int delta = checked((int)(requested - _appliedMonitorLevelDb));
            if (delta == 0)
            {
                return;
            }

            CommandResult result = await ExecuteAsync(
                new AdjustRadioControlCommand(
                    RequestId.New(),
                    _sessionId!.Value,
                    DesktopClientId,
                    RadioControl.MonitorLevel,
                    delta));
            Status = result.Accepted
                ? $"Adjusted monitor level to {requested:+0;-0;0} dB."
                : result.Message ?? "Monitor level adjustment was rejected.";
            await RefreshAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Status = $"Monitor level adjustment failed: {exception.Message}";
        }
        finally
        {
            _monitorLevelUpdateGate.Release();
        }
    }

    public int ReceiveSpeedBelowWpm
    {
        get => _receiveSpeedBelowWpm;
        set => SetField(ref _receiveSpeedBelowWpm, Math.Clamp(value, 0, 10));
    }

    public int ReceiveSpeedAboveWpm
    {
        get => _receiveSpeedAboveWpm;
        set => SetField(ref _receiveSpeedAboveWpm, Math.Clamp(value, 0, 10));
    }

    public int CustomSerialNumberMinimum
    {
        get => _customSerialNumberMinimum;
        set
        {
            if (SetField(
                    ref _customSerialNumberMinimum,
                    Math.Clamp(value, 1, 9_998)))
            {
                CustomSerialNumberMinimumDigits = Math.Max(
                    CustomSerialNumberMinimumDigits,
                    DecimalDigitCount(_customSerialNumberMinimum));
            }
        }
    }

    public int CustomSerialNumberExclusiveMaximum
    {
        get => _customSerialNumberExclusiveMaximum;
        set
        {
            if (SetField(
                    ref _customSerialNumberExclusiveMaximum,
                    Math.Clamp(value, 2, 9_999)))
            {
                CustomSerialNumberMaximumDigits = Math.Max(
                    CustomSerialNumberMaximumDigits,
                    DecimalDigitCount(_customSerialNumberExclusiveMaximum));
            }
        }
    }

    public int CustomSerialNumberMinimumDigits
    {
        get => _customSerialNumberMinimumDigits;
        set => SetField(
            ref _customSerialNumberMinimumDigits,
            Math.Clamp(
                value,
                DecimalDigitCount(CustomSerialNumberMinimum),
                4));
    }

    public int CustomSerialNumberMaximumDigits
    {
        get => _customSerialNumberMaximumDigits;
        set => SetField(
            ref _customSerialNumberMaximumDigits,
            Math.Clamp(
                value,
                DecimalDigitCount(CustomSerialNumberExclusiveMaximum),
                4));
    }

    public string HstOperatorName
    {
        get => _hstOperatorName;
        set => SetField(
            ref _hstOperatorName,
            value.ToUpperInvariant());
    }

    public bool ShowCallsignInformation
    {
        get => _showCallsignInformation;
        set
        {
            if (SetField(ref _showCallsignInformation, value))
            {
                OnPropertyChanged(nameof(CallsignInformation));
            }
        }
    }

    public string CallsignInformation =>
        ShowCallsignInformation ? _callsignInformation : "Callsign information hidden";

    public AudioOutputDevice? SelectedAudioOutputDevice
    {
        get => _selectedAudioOutputDevice;
        set
        {
            if (SetField(ref _selectedAudioOutputDevice, value))
            {
                RecoverAudioCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string AudioDeviceStatus
    {
        get => _audioDeviceStatus;
        private set => SetField(ref _audioDeviceStatus, value);
    }

    public bool Qsk
    {
        get => _qsk;
        set => SetField(ref _qsk, value);
    }

    public bool Qsb
    {
        get => _qsb;
        set => SetField(ref _qsb, value);
    }

    public bool Qrm
    {
        get => _qrm;
        set => SetField(ref _qrm, value);
    }

    public bool Qrn
    {
        get => _qrn;
        set => SetField(ref _qrn, value);
    }

    public bool Flutter
    {
        get => _flutter;
        set => SetField(ref _flutter, value);
    }

    public bool Lids
    {
        get => _lids;
        set => SetField(ref _lids, value);
    }

    public int QsoCount
    {
        get => _qsoCount;
        private set
        {
            if (SetField(ref _qsoCount, value))
            {
                OnPropertyChanged(nameof(QsoEmptyMessage));
            }
        }
    }

    public string QsoEmptyMessage =>
        QsoCount == 0
            ? "No contacts yet. Start a session and copy the next caller."
            : string.Empty;

    public int Score
    {
        get => _score;
        private set => SetField(ref _score, value);
    }

    public int QsoRatePerHour
    {
        get => _qsoRatePerHour;
        private set => SetField(ref _qsoRatePerHour, value);
    }

    public int HighScore
    {
        get => _highScore;
        private set => SetField(ref _highScore, value);
    }

    public string LastExportPath =>
        _lastExportPath ?? "No result has been exported.";

    public string AudioHealth
    {
        get => _audioHealth;
        private set => SetField(ref _audioHealth, value);
    }

    public bool AudioRecordingEnabled
    {
        get => _recordingPreference?.Enabled ?? false;
        set
        {
            if (_recordingPreference is null
                || _recordingPreference.Enabled == value)
            {
                return;
            }

            _recordingPreference.Enabled = value;
            OnPropertyChanged(nameof(AudioRecordingEnabled));
            Status = value
                ? "The next session will be recorded as a WAV file."
                : "Audio recording disabled.";
        }
    }

    public bool CanPlayRecording =>
        _recordingPreference?.LastPath is string path
        && File.Exists(path);

    public AsyncCommand StartCommand { get; }

    public AsyncCommand StartSingleCommand { get; }

    public AsyncCommand StartWpxCommand { get; }

    public AsyncCommand StartHstCommand { get; }

    public AsyncCommand AdvanceCommand { get; }

    public AsyncCommand PauseCommand { get; }

    public AsyncCommand ResumeCommand { get; }

    public AsyncCommand StopCommand { get; }

    public AsyncCommand SendCqCommand { get; }

    public AsyncCommand SendExchangeCommand { get; }

    public AsyncCommand SendThankYouCommand { get; }

    public AsyncCommand SendMyCallCommand { get; }

    public AsyncCommand SendHisCallCommand { get; }

    public AsyncCommand SendBeforeCommand { get; }

    public AsyncCommand SendQuestionCommand { get; }

    public AsyncCommand SendNilCommand { get; }

    public AsyncCommand SendNumberQuestionCommand { get; }

    public AsyncCommand AbortCommand { get; }

    public AsyncCommand WipeCommand { get; }

    public AsyncCommand EnterSendMessageCommand { get; }

    public AsyncCommand CompleteQsoCommand { get; }

    public AsyncCommand LogQsoOnlyCommand { get; }

    public AsyncCommand SendCallAndExchangeCommand { get; }

    public AsyncCommand RitUpCommand { get; }

    public AsyncCommand RitDownCommand { get; }

    public AsyncCommand BandwidthUpCommand { get; }

    public AsyncCommand BandwidthDownCommand { get; }

    public AsyncCommand SpeedUpCommand { get; }

    public AsyncCommand SpeedDownCommand { get; }

    public AsyncCommand ShowScoreCommand { get; }

    public AsyncCommand ExportJsonCommand { get; }

    public AsyncCommand ExportCabrilloCommand { get; }

    public AsyncCommand OpenResultsFolderCommand { get; }

    public AsyncCommand PlayRecordingCommand { get; }

    public AsyncCommand RefreshAudioDevicesCommand { get; }

    public AsyncCommand RecoverAudioCommand { get; }

    public async Task InitializeAsync()
    {
        if (_settingsStore is null
            || Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        SettingsLoadResult result = await _settingsStore.LoadAsync(
            _lifetime.Token);
        IReadOnlyDictionary<string, string> values = result.Document.Values;
        StationCall = Get(values, "Station.Call", StationCall);
        WordsPerMinute = GetInt(values, "Station.Wpm", WordsPerMinute);
        PitchHz = GetInt(values, "Station.Pitch", PitchHz);
        BandwidthHz = GetInt(values, "Station.BandWidth", BandwidthHz);
        Activity = GetInt(values, "Band.Activity", Activity);
        MonitorLevel = GetDouble(
            values,
            "Station.SelfMonVolume",
            MonitorLevel);
        ReceiveSpeedBelowWpm = GetInt(
            values,
            "Station.CWMinRxSpeed",
            ReceiveSpeedBelowWpm);
        ReceiveSpeedAboveWpm = GetInt(
            values,
            "Station.CWMaxRxSpeed",
            ReceiveSpeedAboveWpm);
        (
            int importedMinimum,
            int importedMaximum,
            int importedMinimumDigits,
            int importedMaximumDigits) =
            ParseSerialNumberRange(
                Get(values, "Station.SerialNrCustomRange", "01-99"),
                CustomSerialNumberMinimum,
                CustomSerialNumberExclusiveMaximum,
                CustomSerialNumberMinimumDigits,
                CustomSerialNumberMaximumDigits);
        CustomSerialNumberMinimum = GetInt(
            values,
            "Station.SerialNrCustomMinimum",
            importedMinimum);
        CustomSerialNumberExclusiveMaximum = GetInt(
            values,
            "Station.SerialNrCustomMaximum",
            importedMaximum);
        CustomSerialNumberMinimumDigits = importedMinimumDigits;
        CustomSerialNumberMaximumDigits = importedMaximumDigits;
        HstOperatorName = Get(
            values,
            "Station.Name",
            HstOperatorName);
        ShowCallsignInformation = GetBool(
            values,
            "System.ShowCallsignInfo",
            ShowCallsignInformation);
        Qsk = GetBool(values, "Station.Qsk", Qsk);
        Qsb = GetBool(values, "Band.Qsb", Qsb);
        Qrm = GetBool(values, "Band.Qrm", Qrm);
        Qrn = GetBool(values, "Band.Qrn", Qrn);
        Flutter = GetBool(values, "Band.Flutter", Flutter);
        Lids = GetBool(values, "Band.Lids", Lids);
        AudioRecordingEnabled = GetBool(
            values,
            "Station.SaveWav",
            AudioRecordingEnabled);

        string contestId = Get(
            values,
            "Contest.SimContest",
            SelectedContest.Id.Value);
        SelectedContest = Contests.FirstOrDefault(
                option => option.Id.Value == contestId)
            ?? SelectedContest;
        string runModeId = Get(
            values,
            "Contest.DefaultRunMode",
            SelectedRunMode.Id.Value);
        SelectedRunMode = RunModes.FirstOrDefault(
                option => option.Id.Value == runModeId)
            ?? SelectedRunMode;
        int duration = GetInt(
            values,
            "Contest.Duration",
            SelectedDuration.Minutes);
        SelectedDuration = Durations.FirstOrDefault(
                option => option.Minutes == duration)
            ?? SelectedDuration;
        int serialNumberRange = GetInt(
            values,
            "Station.SerialNR",
            (int)SelectedSerialNumberRange.Mode);
        SelectedSerialNumberRange = SerialNumberRanges.FirstOrDefault(
                option => (int)option.Mode == serialNumberRange)
            ?? SelectedSerialNumberRange;
        await RefreshAudioDevicesAsync(
            Get(values, "Station.AudioOutputDevice", string.Empty));
        Status = result.Recovered
            ? result.Diagnostic ?? "Settings recovered with defaults."
            : "Ready. Configure a contest and press F9.";
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _lifetime.CancelAsync();
        if (_subscriptionTask is not null)
        {
            try
            {
                await _subscriptionTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_sessionId is SessionId sessionId)
        {
            await _client.CloseSessionAsync(sessionId, CancellationToken.None);
        }

        await SaveSettingsAsync();
        _highScoreStore?.Dispose();
        _lifetime.Dispose();
        await _client.DisposeAsync();
    }

    private bool CanStart() =>
        _sessionState is SessionState.Ready
            or SessionState.Created
            or SessionState.Completed;

    private AsyncCommand CreateIntentCommand(OperatorIntent intent) =>
        new(
            () => SendIntentAsync(intent),
            () => _sessionState == SessionState.Running);

    private AsyncCommand CreateRadioCommand(RadioControl control, int delta) =>
        new(
            () => AdjustRadioAsync(control, delta),
            () => _sessionState is SessionState.Running or SessionState.Paused);

    private async Task StartAsync(RunModeOption runMode)
    {
        try
        {
            SelectedRunMode = runMode;
            if (_sessionId is not null)
            {
                await CloseCurrentSessionAsync();
            }

            var settings = new SessionSettings(
                Seed,
                SelectedContest.Id,
                SelectedRunMode.Id,
                DurationBlocks(SelectedDuration.Minutes))
            {
                StationCall = StationCall,
                WordsPerMinute = WordsPerMinute,
                PitchHz = PitchHz,
                BandwidthHz = BandwidthHz,
                Activity = Activity,
                Qsk = Qsk,
                Qsb = Qsb,
                Qrm = Qrm,
                Qrn = Qrn,
                Flutter = Flutter,
                Lids = Lids,
                MonitorLevelDb = MonitorLevel,
                ReceiveSpeedBelowWpm = ReceiveSpeedBelowWpm,
                ReceiveSpeedAboveWpm = ReceiveSpeedAboveWpm,
                SerialNumberRange = SelectedSerialNumberRange.Mode,
                CustomSerialNumberMinimum = CustomSerialNumberMinimum,
                CustomSerialNumberExclusiveMaximum =
                    CustomSerialNumberExclusiveMaximum,
                CustomSerialNumberMinimumDigits =
                    CustomSerialNumberMinimumDigits,
                CustomSerialNumberMaximumDigits =
                    CustomSerialNumberMaximumDigits,
                HstOperatorName = HstOperatorName.Trim(),
                AudioOutputDeviceName = SelectedAudioOutputDevice?.Name,
            };
            SessionHandle handle = await _client.CreateSessionAsync(
                settings,
                _lifetime.Token);
            _sessionId = handle.SessionId;
            _recordedResultSessionId = null;
            _lastAppliedSnapshotRevision = -1;
            _sessionState = handle.State;
            BeginSubscription(handle.SessionId);

            if (SelectedAudioOutputDevice is not null)
            {
                CommandResult audioResult = await ExecuteAsync(
                    new RecoverAudioCommand(
                        RequestId.New(),
                        handle.SessionId,
                        DesktopClientId,
                        SelectedAudioOutputDevice.Name));
                if (!audioResult.Accepted
                    && audioResult.ErrorCode
                        != DomainErrorCodes.UnsupportedCapability)
                {
                    Status = audioResult.Message
                        ?? "The selected audio device is unavailable.";
                    return;
                }
            }

            CommandResult result = await ExecuteAsync(
                new StartSessionCommand(
                    RequestId.New(),
                    handle.SessionId,
                    DesktopClientId));
            Status = result.Accepted
                ? $"{RunMode} running. F1 through F8 send, Enter uses ESM."
                : result.Message ?? "Start was rejected.";
            await RefreshAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Status = exception.Message;
        }
    }

    private Task RefreshAudioDevicesAsync() =>
        RefreshAudioDevicesAsync(SelectedAudioOutputDevice?.Name);

    private async Task RefreshAudioDevicesAsync(string? preferredDeviceName)
    {
        AudioDeviceStatus = "Loading audio devices...";
        try
        {
            IReadOnlyList<AudioOutputDevice> devices =
                await _client.GetAudioOutputDevicesAsync(_lifetime.Token);
            Dispatch(
                () =>
                {
                    AudioOutputDevices.Clear();
                    foreach (AudioOutputDevice device in devices)
                    {
                        AudioOutputDevices.Add(device);
                    }

                    SelectedAudioOutputDevice = devices.FirstOrDefault(
                            device => String.Equals(
                                device.Name,
                                preferredDeviceName,
                                StringComparison.Ordinal))
                        ?? devices.FirstOrDefault(device => device.IsDefault)
                        ?? (devices.Count == 0 ? null : devices[0]);
                    AudioDeviceStatus = devices.Count == 0
                        ? "No playback devices were found."
                        : $"{devices.Count} playback device(s) available.";
                });
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            AudioDeviceStatus =
                $"Audio device discovery failed: {exception.Message}";
        }
    }

    private async Task RecoverAudioAsync()
    {
        if (_sessionId is not SessionId sessionId
            || SelectedAudioOutputDevice is not AudioOutputDevice device)
        {
            AudioDeviceStatus =
                "Choose an audio device after creating a session.";
            return;
        }

        bool resume = _sessionState == SessionState.Running;
        if (resume)
        {
            CommandResult pause = await ExecuteAsync(
                new PauseSessionCommand(
                    RequestId.New(),
                    sessionId,
                    DesktopClientId));
            if (!pause.Accepted)
            {
                AudioDeviceStatus = pause.Message ?? "Could not pause audio.";
                return;
            }
        }

        CommandResult result = await ExecuteAsync(
            new RecoverAudioCommand(
                RequestId.New(),
                sessionId,
                DesktopClientId,
                device.Name));
        AudioDeviceStatus = result.Accepted
            ? $"Using {device.Name}."
            : result.Message ?? "Audio recovery failed.";

        if (resume && result.Accepted)
        {
            await ExecuteAsync(
                new ResumeSessionCommand(
                    RequestId.New(),
                    sessionId,
                    DesktopClientId));
        }

        await RefreshAsync();
    }

    private static long DurationBlocks(int minutes)
    {
        if (minutes == 0)
        {
            return 0;
        }

        return checked(
            (long)Math.Ceiling(
                minutes
                * 60d
                * CompatibilityProfile.SampleRate
                / CompatibilityProfile.BlockSize));
    }

    private void BeginSubscription(SessionId sessionId)
    {
        _subscriptionTask = ObserveSessionAsync(sessionId, _lifetime.Token);
    }

    private async Task ObserveSessionAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (SessionUpdate update in _client.SubscribeAsync(
                new SessionSubscription(sessionId),
                cancellationToken))
            {
                if (update.Snapshot is SessionSnapshot snapshot)
                {
                    QueueSnapshot(snapshot);
                    if (snapshot.State == SessionState.Completed
                        && _recordedResultSessionId != snapshot.SessionId)
                    {
                        _recordedResultSessionId = snapshot.SessionId;
                        await RefreshResultSummaryAsync(recordHighScore: true);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Dispatch(() => Status = $"Live update stopped: {exception.Message}");
        }
    }

    private async Task SendIntentAsync(OperatorIntent intent)
    {
        CommandResult result = await ExecuteAsync(
            new SendOperatorIntentCommand(
                RequestId.New(),
                _sessionId!.Value,
                DesktopClientId,
                intent,
                CallEntry,
                RstEntry,
                Exchange1Entry,
                Exchange2Entry));
        Status = result.Accepted
            ? $"Sent {intent} at block {result.AppliedBlock}."
            : result.Message ?? "Message was rejected.";
        await RefreshAsync();
    }

    private async Task AdvanceAsync()
    {
        CommandResult result = await ExecuteAsync(
            new AdvanceSimulationCommand(
                RequestId.New(),
                _sessionId!.Value,
                DesktopClientId,
                BlockCount: 4));
        Status = result.Accepted
            ? $"Advanced four diagnostic blocks at {result.AppliedBlock}."
            : result.Message ?? "Advance was rejected.";
        await RefreshAsync();
    }

    private async Task AdjustRadioAsync(RadioControl control, int delta)
    {
        CommandResult result = await ExecuteAsync(
            new AdjustRadioControlCommand(
                RequestId.New(),
                _sessionId!.Value,
                DesktopClientId,
                control,
                delta));
        Status = result.Accepted
            ? $"Adjusted {control}."
            : result.Message ?? "Control adjustment was rejected.";
        await RefreshAsync();
    }

    private Task AdjustSpeedUpAsync()
    {
        int delta = SelectedRunMode.Id.Value == "rmHst"
            ? ((WordsPerMinute / 5) * 5 + 5) - WordsPerMinute
            : 2;
        return AdjustRadioAsync(RadioControl.Speed, delta);
    }

    private async Task SendCallAndExchangeAsync()
    {
        await SendIntentAsync(OperatorIntent.HisCall);
        await SendIntentAsync(OperatorIntent.Exchange);
    }

    private async Task EnterSendMessageAsync()
    {
        CommandResult result = await ExecuteAsync(
            new TriggerEnterSendMessageCommand(
                RequestId.New(),
                _sessionId!.Value,
                DesktopClientId,
                new(
                    CallEntry,
                    RstEntry,
                    Exchange1Entry,
                    Exchange2Entry)));
        EnterSendMessageResult? enter = result.EnterSendMessage;
        if (enter is null)
        {
            Status = result.Message ?? "Enter Sends Message was rejected.";
            await RefreshAsync();
            return;
        }

        if (CallEntry.Length > 0
            && RstEntry.Length == 0
            && result.Accepted
            && enter.SentMessages.Count > 0
            && String.Equals(
                ContestCatalog.Get(SelectedContest.Id).ExchangeType1,
                "etRST",
                StringComparison.Ordinal))
        {
            RstEntry = "599";
        }

        string loggedCall = CallEntry.ToUpperInvariant();
        Status = enter.Outcome switch
        {
            EnterSendMessageOutcome.SendCq => "Sent CQ.",
            EnterSendMessageOutcome.SendEnteredCall =>
                "Sent entered callsign.",
            EnterSendMessageOutcome.SendCallAndExchange =>
                "Sent callsign and exchange.",
            EnterSendMessageOutcome.RequestExchangeRepeat =>
                "Requested an exchange repeat.",
            EnterSendMessageOutcome.CompleteAndLogQso =>
                $"Logged {loggedCall}.",
            EnterSendMessageOutcome.RejectEntry =>
                result.Message ?? "The QSO entry is invalid.",
            _ => throw new InvalidOperationException(
                $"Unknown ESM outcome '{enter.Outcome}'."),
        };

        if (enter.ClearEntry)
        {
            ClearEntryFields();
            Qso? loggedQso = await RefreshQsoLogAsync();
            if (loggedQso?.IsDuplicate == true)
            {
                Status = $"Logged {loggedCall} as a duplicate.";
            }
        }

        EntryFocusRequested?.Invoke(
            this,
            new(enter.FocusTarget, enter.SelectQuestionMark));
        await RefreshAsync();
    }

    private async Task CompleteQsoAsync()
    {
        await LogQsoAsync(sendThankYou: true);
    }

    private async Task LogQsoOnlyAsync()
    {
        await LogQsoAsync(sendThankYou: false);
    }

    private async Task LogQsoAsync(bool sendThankYou)
    {
        if (CallEntry.Length < 3)
        {
            Status = "Enter a callsign before logging the QSO.";
            return;
        }

        string loggedCall = CallEntry.ToUpperInvariant();
        if (sendThankYou)
        {
            await SendIntentAsync(OperatorIntent.ThankYou);
        }

        CommandResult result = await ExecuteAsync(
            new LogQsoCommand(
                RequestId.New(),
                _sessionId!.Value,
                DesktopClientId,
                CallEntry,
                RstEntry,
                Exchange1Entry,
                Exchange2Entry));
        Status = result.Accepted
            ? $"Logged {loggedCall}."
            : result.Message ?? "QSO logging was rejected.";
        if (result.Accepted)
        {
            ClearEntryFields();
            Qso? loggedQso = await RefreshQsoLogAsync();
            if (loggedQso?.IsDuplicate == true)
            {
                Status = $"Logged {loggedCall} as a duplicate.";
            }
        }

        await RefreshAsync();
    }

    private Task WipeAsync()
    {
        ClearEntryFields();
        Status = "Entry fields cleared.";
        return Task.CompletedTask;
    }

    private void ClearEntryFields()
    {
        CallEntry = string.Empty;
        RstEntry = string.Empty;
        Exchange1Entry = string.Empty;
        Exchange2Entry = string.Empty;
    }

    private async Task PauseAsync()
    {
        CommandResult result = await ExecuteAsync(
            new PauseSessionCommand(
                RequestId.New(),
                _sessionId!.Value,
                DesktopClientId));
        Status = result.Accepted ? "Session paused." : result.Message!;
        await RefreshAsync();
    }

    private async Task ResumeAsync()
    {
        CommandResult result = await ExecuteAsync(
            new ResumeSessionCommand(
                RequestId.New(),
                _sessionId!.Value,
                DesktopClientId));
        Status = result.Accepted ? "Session resumed." : result.Message!;
        await RefreshAsync();
    }

    private async Task StopAsync()
    {
        CommandResult result = await ExecuteAsync(
            new StopSessionCommand(
                RequestId.New(),
                _sessionId!.Value,
                DesktopClientId));
        Status = result.Accepted ? "Session completed." : result.Message!;
        await RefreshAsync();
        if (result.Accepted)
        {
            _recordedResultSessionId = _sessionId;
            await RefreshResultSummaryAsync(recordHighScore: true);
        }
    }

    private async Task ShowScoreAsync()
    {
        await RefreshResultSummaryAsync(recordHighScore: false);
        ShowScoreRequested?.Invoke(
            this,
            new ScoreSummaryEventArgs(
                Score,
                QsoCount,
                QsoRatePerHour,
                HighScore,
                ContestName,
                Elapsed));
    }

    private bool CanExportResult() =>
        _sessionId is not null && _resultsDirectory is not null;

    private async Task ExportResultAsync(ResultExportFormat format)
    {
        if (_sessionId is not SessionId sessionId
            || _resultsDirectory is null)
        {
            Status = "Start a session before exporting results.";
            return;
        }

        try
        {
            SessionResult result = await _client.GetResultAsync(
                sessionId,
                _lifetime.Token);
            IReadOnlyList<Qso> qsos =
                await _client.ListCompletedQsosAsync(
                    sessionId,
                    _lifetime.Token);
            ResultExportArtifact artifact = ResultExporter.Create(
                result,
                qsos,
                format,
                HstOperatorName);
            _lastExportPath = await ResultExporter.SaveAtomicAsync(
                _resultsDirectory,
                artifact,
                _lifetime.Token);
            OnPropertyChanged(nameof(LastExportPath));
            OpenResultsFolderCommand.RaiseCanExecuteChanged();
            Status = $"Exported {Path.GetFileName(_lastExportPath)}.";
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            Status = $"Result export failed: {exception.Message}";
        }
    }

    private Task OpenResultsFolderAsync()
    {
        if (_resultsDirectory is null)
        {
            return Task.CompletedTask;
        }

        Directory.CreateDirectory(_resultsDirectory);
        Process.Start(
            new ProcessStartInfo(_resultsDirectory)
            {
                UseShellExecute = true,
            });
        return Task.CompletedTask;
    }

    private async Task RefreshResultSummaryAsync(bool recordHighScore)
    {
        if (_sessionId is not SessionId sessionId)
        {
            return;
        }

        try
        {
            SessionResult result = await _client.GetResultAsync(
                sessionId,
                _lifetime.Token);
            Score = result.Score;
            QsoCount = result.QsoCount;
            QsoRatePerHour = result.QsoRatePerHour;
            if (_highScoreStore is not null)
            {
                ContestHighScore? highScore = recordHighScore
                    ? await _highScoreStore.RecordAsync(
                        result,
                        HstOperatorName,
                        _lifetime.Token)
                    : await _highScoreStore.GetAsync(
                        result.ContestId,
                        _lifetime.Token);
                HighScore = highScore?.Score ?? result.Score;
            }
            else
            {
                HighScore = Math.Max(HighScore, result.Score);
            }
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            Status = $"Could not load result details: {exception.Message}";
        }
    }

    private Task PlayRecordingAsync()
    {
        string? path = _recordingPreference?.LastPath;
        if (path is null || !File.Exists(path))
        {
            Status = "No completed recording is available.";
            return Task.CompletedTask;
        }

        Process.Start(
            new ProcessStartInfo(path)
            {
                UseShellExecute = true,
            });
        Status = $"Opened {Path.GetFileName(path)}.";
        return Task.CompletedTask;
    }

    private Task<CommandResult> ExecuteAsync(SessionCommand command) =>
        _client.ExecuteAsync(command, _lifetime.Token);

    private async Task RefreshAsync()
    {
        if (_sessionId is not SessionId sessionId)
        {
            return;
        }

        SessionSnapshot snapshot = await _client.GetSnapshotAsync(
            sessionId,
            _lifetime.Token);
        await QueueSnapshotAsync(snapshot);
    }

    private void ApplySnapshot(SessionSnapshot snapshot)
    {
        if (_sessionId != snapshot.SessionId)
        {
            return;
        }

        if (snapshot.Revision < _lastAppliedSnapshotRevision)
        {
            return;
        }

        _lastAppliedSnapshotRevision = snapshot.Revision;
        _sessionState = snapshot.State;
        OnPropertyChanged(nameof(SessionStateLabel));
        OnPropertyChanged(nameof(IsSetupEnabled));
        OnPropertyChanged(nameof(IsMonitorLevelEnabled));
        SimulationBlock = snapshot.SimulationBlock;
        Elapsed = snapshot.ElapsedSimulationTime.ToString(
            @"mm\:ss\.fff",
            CultureInfo.InvariantCulture);
        LastCaller = snapshot.LastCaller ?? "Waiting";
        IReadOnlyList<ActiveStationSnapshot>? activeStations =
            snapshot.ActiveStations;
        ActiveStationSnapshot? selectedStation = activeStations?
            .FirstOrDefault(
                station => station.Callsign.Equals(
                    snapshot.LastCaller,
                    StringComparison.Ordinal))
            ?? (activeStations is null || activeStations.Count == 0
                ? null
                : activeStations[activeStations.Count - 1]);
        _callsignInformation = FormatCallsignInformation(selectedStation);
        OnPropertyChanged(nameof(CallsignInformation));
        CallerState = FormatOperatorState(snapshot.ActiveOperatorState);
        ActiveCallerCount = snapshot.ActiveStations?.Count ?? 0;
        LastSent = snapshot.LastOperatorMessage ?? "None";
        QsoCount = snapshot.QsoCount;
        Score = snapshot.Score;
        QsoRatePerHour = snapshot.QsoRatePerHour;
        WordsPerMinute = snapshot.CurrentWordsPerMinute;
        BandwidthHz = snapshot.CurrentBandwidthHz;
        RitOffsetHz = snapshot.RitOffsetHz;
        _appliedMonitorLevelDb = snapshot.CurrentMonitorLevelDb;
        MonitorLevel = snapshot.CurrentMonitorLevelDb;
        AudioHealth = snapshot.AudioOutputHealthy
            ? $"Healthy, {snapshot.AudioQueuedBlocks} blocks queued"
            : $"Needs attention, {snapshot.AudioUnderrunCount} underruns";
        RaiseCommandStateChanged();
        OnPropertyChanged(nameof(CanPlayRecording));
        PlayRecordingCommand.RaiseCanExecuteChanged();
    }

    private static string FormatOperatorState(OperatorState? state) =>
        state switch
        {
            null => "Idle",
            OperatorState.NeedPreviousEnd => "Waiting for prior QSO",
            OperatorState.NeedQso => "Calling",
            OperatorState.NeedNumber => "Waiting for exchange",
            OperatorState.NeedCall => "Needs call correction",
            OperatorState.NeedCallAndNumber => "Needs call and exchange",
            OperatorState.NeedEnd => "Waiting for TU",
            OperatorState.Done => "Complete",
            OperatorState.Failed => "Gone",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };

    private string FormatCallsignInformation(ActiveStationSnapshot? station)
    {
        if (station is null)
        {
            return "No caller selected";
        }

        string radio = string.Create(
            CultureInfo.InvariantCulture,
            $"{station.WordsPerMinute} WPM | "
            + $"{station.PitchOffsetHz:+0;-0;0} Hz");
        return _dxccDatabase.TryFind(station.Callsign, out DxccRecord? record)
            && record is not null
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{station.Callsign} | {record.Entity} | "
                + $"{record.Continent} | CQ {record.CqZones} | {radio}")
            : $"{station.Callsign} | {radio}";
    }

    private void QueueSnapshot(SessionSnapshot snapshot)
    {
        Volatile.Write(ref _pendingSnapshot, snapshot);
        if (Interlocked.CompareExchange(
                ref _snapshotDispatchScheduled,
                1,
                0) == 0)
        {
            Dispatch(DrainSnapshots);
        }
    }

    private Task QueueSnapshotAsync(SessionSnapshot snapshot)
    {
        if (_uiContext is null)
        {
            ApplySnapshotSynchronized(snapshot);
            return Task.CompletedTask;
        }

        if (ReferenceEquals(SynchronizationContext.Current, _uiContext))
        {
            QueueSnapshot(snapshot);
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatch(
            () =>
            {
                try
                {
                    QueueSnapshot(snapshot);
                    completion.SetResult(true);
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            });
        return completion.Task;
    }

    private void ApplySnapshotSynchronized(SessionSnapshot snapshot)
    {
        lock (_snapshotApplicationGate)
        {
            ApplySnapshot(snapshot);
        }
    }

    private void DrainSnapshots()
    {
        while (true)
        {
            SessionSnapshot? snapshot =
                Interlocked.Exchange(ref _pendingSnapshot, null);
            if (snapshot is not null)
            {
                ApplySnapshotSynchronized(snapshot);
            }

            Volatile.Write(ref _snapshotDispatchScheduled, 0);
            if (Volatile.Read(ref _pendingSnapshot) is null
                || Interlocked.CompareExchange(
                    ref _snapshotDispatchScheduled,
                    1,
                    0) != 0)
            {
                return;
            }
        }
    }

    private async Task<Qso?> RefreshQsoLogAsync()
    {
        if (_sessionId is not SessionId sessionId)
        {
            return null;
        }

        IReadOnlyList<Qso> qsos = await _client.ListCompletedQsosAsync(
            sessionId,
            _lifetime.Token);
        Dispatch(
            () =>
            {
                QsoLog.Clear();
                foreach (Qso qso in qsos.Reverse())
                {
                    QsoLog.Add(
                        new(
                            qso.Timestamp.ToString(
                                "HH:mm:ss",
                                CultureInfo.InvariantCulture),
                            qso.Call,
                            qso.Rst.ToString(CultureInfo.InvariantCulture),
                            string.Join(
                                ' ',
                                new[] { qso.Exchange1, qso.Exchange2 }
                                    .Where(value => value.Length > 0)),
                            qso.Points,
                            qso.IsDuplicate));
                }
            });
        return qsos.Count == 0 ? null : qsos[qsos.Count - 1];
    }

    private async Task CloseCurrentSessionAsync()
    {
        if (_sessionId is not SessionId sessionId)
        {
            return;
        }

        await _client.CloseSessionAsync(sessionId, _lifetime.Token);
        _sessionId = null;
        QsoLog.Clear();
        RaiseCommandStateChanged();
    }

    private void Dispatch(Action action)
    {
        if (_uiContext is null
            || ReferenceEquals(SynchronizationContext.Current, _uiContext))
        {
            action();
            return;
        }

        _uiContext.Post(static state => ((Action)state!).Invoke(), action);
    }

    private void RaiseCommandStateChanged()
    {
        foreach (AsyncCommand command in new[]
        {
            StartCommand,
            StartSingleCommand,
            StartWpxCommand,
            StartHstCommand,
            AdvanceCommand,
            PauseCommand,
            ResumeCommand,
            StopCommand,
            SendCqCommand,
            SendExchangeCommand,
            SendThankYouCommand,
            SendMyCallCommand,
            SendHisCallCommand,
            SendBeforeCommand,
            SendQuestionCommand,
            SendNilCommand,
            SendNumberQuestionCommand,
            AbortCommand,
            EnterSendMessageCommand,
            CompleteQsoCommand,
            LogQsoOnlyCommand,
            SendCallAndExchangeCommand,
            RitUpCommand,
            RitDownCommand,
            BandwidthUpCommand,
            BandwidthDownCommand,
            SpeedUpCommand,
            SpeedDownCommand,
            RecoverAudioCommand,
            ExportJsonCommand,
            ExportCabrilloCommand,
        })
        {
            command.RaiseCanExecuteChanged();
        }
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }

    private async Task SaveSettingsAsync()
    {
        if (_settingsStore is null)
        {
            return;
        }

        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Station.Call"] = StationCall,
            ["Station.Wpm"] = WordsPerMinute.ToString(
                CultureInfo.InvariantCulture),
            ["Station.Pitch"] = PitchHz.ToString(CultureInfo.InvariantCulture),
            ["Station.BandWidth"] = BandwidthHz.ToString(
                CultureInfo.InvariantCulture),
            ["Station.SelfMonVolume"] = MonitorLevel.ToString(
                CultureInfo.InvariantCulture),
            ["Station.CWMinRxSpeed"] = ReceiveSpeedBelowWpm.ToString(
                CultureInfo.InvariantCulture),
            ["Station.CWMaxRxSpeed"] = ReceiveSpeedAboveWpm.ToString(
                CultureInfo.InvariantCulture),
            ["Station.SerialNR"] = ((int)SelectedSerialNumberRange.Mode).ToString(
                CultureInfo.InvariantCulture),
            ["Station.SerialNrCustomMinimum"] =
                CustomSerialNumberMinimum.ToString(
                    CultureInfo.InvariantCulture),
            ["Station.SerialNrCustomMaximum"] =
                CustomSerialNumberExclusiveMaximum.ToString(
                    CultureInfo.InvariantCulture),
            ["Station.SerialNrCustomRange"] = string.Create(
                CultureInfo.InvariantCulture,
                $"{CustomSerialNumberMinimum.ToString($"D{CustomSerialNumberMinimumDigits}", CultureInfo.InvariantCulture)}-{CustomSerialNumberExclusiveMaximum.ToString($"D{CustomSerialNumberMaximumDigits}", CultureInfo.InvariantCulture)}"),
            ["Station.Name"] = HstOperatorName.Trim(),
            ["Station.AudioOutputDevice"] =
                SelectedAudioOutputDevice?.Name ?? string.Empty,
            ["Station.Qsk"] = Qsk.ToString(CultureInfo.InvariantCulture),
            ["Station.SaveWav"] = AudioRecordingEnabled.ToString(
                CultureInfo.InvariantCulture),
            ["Band.Activity"] = Activity.ToString(CultureInfo.InvariantCulture),
            ["Band.Qsb"] = Qsb.ToString(CultureInfo.InvariantCulture),
            ["Band.Qrm"] = Qrm.ToString(CultureInfo.InvariantCulture),
            ["Band.Qrn"] = Qrn.ToString(CultureInfo.InvariantCulture),
            ["Band.Flutter"] = Flutter.ToString(CultureInfo.InvariantCulture),
            ["Band.Lids"] = Lids.ToString(CultureInfo.InvariantCulture),
            ["Contest.SimContest"] = SelectedContest.Id.Value,
            ["Contest.DefaultRunMode"] = SelectedRunMode.Id.Value,
            ["Contest.Duration"] = SelectedDuration.Minutes.ToString(
                CultureInfo.InvariantCulture),
            ["System.ShowCallsignInfo"] = ShowCallsignInformation.ToString(
                CultureInfo.InvariantCulture),
        };
        await _settingsStore.SaveAsync(
            new(
                SettingsDocument.CurrentSchemaVersion,
                values),
            CancellationToken.None);
    }

    private static string Get(
        IReadOnlyDictionary<string, string> values,
        string key,
        string fallback) =>
        values.TryGetValue(key, out string? value) ? value : fallback;

    private static int GetInt(
        IReadOnlyDictionary<string, string> values,
        string key,
        int fallback) =>
        values.TryGetValue(key, out string? value)
        && Int32.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int parsed)
            ? parsed
            : fallback;

    private static double GetDouble(
        IReadOnlyDictionary<string, string> values,
        string key,
        double fallback) =>
        values.TryGetValue(key, out string? value)
        && Double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double parsed)
            ? parsed
            : fallback;

    private static (
        int Minimum,
        int ExclusiveMaximum,
        int MinimumDigits,
        int MaximumDigits) ParseSerialNumberRange(
        string value,
        int fallbackMinimum,
        int fallbackMaximum,
        int fallbackMinimumDigits,
        int fallbackMaximumDigits)
    {
        string[] parts = value.Split('-', 2);
        return parts.Length == 2
            && Int32.TryParse(
                parts[0],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int minimum)
            && Int32.TryParse(
                parts[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int maximum)
            && minimum >= 1
            && maximum > minimum
            && maximum <= 9_999
            && parts[0].Length <= 4
            && parts[1].Length <= 4
                ? (minimum, maximum, parts[0].Length, parts[1].Length)
                : (
                    fallbackMinimum,
                    fallbackMaximum,
                    fallbackMinimumDigits,
                    fallbackMaximumDigits);
    }

    private static int DecimalDigitCount(int value) =>
        value switch
        {
            >= 1_000 => 4,
            >= 100 => 3,
            >= 10 => 2,
            _ => 1,
        };

    private static bool GetBool(
        IReadOnlyDictionary<string, string> values,
        string key,
        bool fallback) =>
        values.TryGetValue(key, out string? value)
        && Boolean.TryParse(value, out bool parsed)
            ? parsed
            : fallback;
}
