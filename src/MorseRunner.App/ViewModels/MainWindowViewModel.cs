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
    string Contest,
    string Elapsed) : EventArgs
{
    public int Score { get; } = Score;

    public int QsoCount { get; } = QsoCount;

    public string Contest { get; } = Contest;

    public string Elapsed { get; } = Elapsed;
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

    private readonly IMorseRunnerClient _client;
    private readonly RecordingPreference? _recordingPreference;
    private readonly SettingsStore? _settingsStore;
    private readonly SynchronizationContext? _uiContext;
    private readonly CancellationTokenSource _lifetime = new();
    private SessionId? _sessionId;
    private Task? _subscriptionTask;
    private SessionSnapshot? _pendingSnapshot;
    private int _snapshotDispatchScheduled;
    private SessionState _sessionState = SessionState.Ready;
    private ContestOption _selectedContest;
    private RunModeOption _selectedRunMode = RunModeOptions[0];
    private DurationOption _selectedDuration = DurationOptions[0];
    private string _status = "Ready. Configure a contest and press F9.";
    private long _simulationBlock;
    private string _elapsed = "00:00.000";
    private string _lastCaller = "Waiting";
    private string _callerState = "Idle";
    private int _activeCallerCount;
    private string _lastSent = "None";
    private string _callEntry = string.Empty;
    private string _rstEntry = "5NN";
    private string _exchange1Entry = string.Empty;
    private string _exchange2Entry = string.Empty;
    private string _stationCall = "W7SST";
    private int _seed = 12_345;
    private int _wordsPerMinute = 30;
    private int _pitchHz = 600;
    private int _bandwidthHz = 500;
    private int _ritOffsetHz;
    private int _activity = 5;
    private double _monitorLevel = -15d;
    private bool _qsk;
    private bool _qsb;
    private bool _qrm;
    private bool _qrn;
    private bool _flutter;
    private bool _lids;
    private int _qsoCount;
    private int _score;
    private string _audioHealth = "Ready";
    private int _disposed;
    private int _initialized;

    public MainWindowViewModel(
        IMorseRunnerClient client,
        SynchronizationContext? uiContext = null,
        RecordingPreference? recordingPreference = null,
        SettingsStore? settingsStore = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _uiContext = uiContext ?? SynchronizationContext.Current;
        _recordingPreference = recordingPreference;
        _settingsStore = settingsStore;
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
        CompleteQsoCommand = new AsyncCommand(
            CompleteQsoAsync,
            () => _sessionState == SessionState.Running);
        SendCallAndExchangeCommand = new AsyncCommand(
            SendCallAndExchangeAsync,
            () => _sessionState == SessionState.Running);
        RitUpCommand = CreateRadioCommand(RadioControl.Rit, 10);
        RitDownCommand = CreateRadioCommand(RadioControl.Rit, -10);
        BandwidthUpCommand = CreateRadioCommand(RadioControl.Bandwidth, 50);
        BandwidthDownCommand = CreateRadioCommand(RadioControl.Bandwidth, -50);
        SpeedUpCommand = CreateRadioCommand(RadioControl.Speed, 1);
        SpeedDownCommand = CreateRadioCommand(RadioControl.Speed, -1);
        ShowScoreCommand = new AsyncCommand(ShowScoreAsync);
        PlayRecordingCommand = new AsyncCommand(
            PlayRecordingAsync,
            () => CanPlayRecording);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<ScoreSummaryEventArgs>? ShowScoreRequested;

    public IReadOnlyList<ContestOption> Contests { get; }

    public IReadOnlyList<RunModeOption> RunModes { get; }

    public IReadOnlyList<DurationOption> Durations { get; }

    public ObservableCollection<QsoLogEntryViewModel> QsoLog { get; } = [];

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

    public string ContestName => SelectedContest.DisplayName;

    public string RunMode => SelectedRunMode.DisplayName;

    public string SessionStateLabel => _sessionState.ToString();

    public bool IsSetupEnabled =>
        _sessionState is SessionState.Ready
            or SessionState.Created
            or SessionState.Completed;

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
        set => SetField(ref _wordsPerMinute, Math.Clamp(value, 10, 100));
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
        private set => SetField(ref _qsoCount, value);
    }

    public int Score
    {
        get => _score;
        private set => SetField(ref _score, value);
    }

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

    public AsyncCommand CompleteQsoCommand { get; }

    public AsyncCommand SendCallAndExchangeCommand { get; }

    public AsyncCommand RitUpCommand { get; }

    public AsyncCommand RitDownCommand { get; }

    public AsyncCommand BandwidthUpCommand { get; }

    public AsyncCommand BandwidthDownCommand { get; }

    public AsyncCommand SpeedUpCommand { get; }

    public AsyncCommand SpeedDownCommand { get; }

    public AsyncCommand ShowScoreCommand { get; }

    public AsyncCommand PlayRecordingCommand { get; }

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
            };
            SessionHandle handle = await _client.CreateSessionAsync(
                settings,
                _lifetime.Token);
            _sessionId = handle.SessionId;
            _sessionState = handle.State;
            BeginSubscription(handle.SessionId);

            CommandResult result = await ExecuteAsync(
                new StartSessionCommand(
                    RequestId.New(),
                    handle.SessionId,
                    DesktopClientId));
            Status = result.Accepted
                ? $"{RunMode} running. F1 through F8 send, Enter logs."
                : result.Message ?? "Start was rejected.";
            await RefreshAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Status = exception.Message;
        }
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

    private async Task SendCallAndExchangeAsync()
    {
        await SendIntentAsync(OperatorIntent.HisCall);
        await SendIntentAsync(OperatorIntent.Exchange);
    }

    private async Task CompleteQsoAsync()
    {
        if (CallEntry.Length < 3)
        {
            Status = "Enter a callsign before logging the QSO.";
            return;
        }

        string loggedCall = CallEntry.ToUpperInvariant();
        await SendIntentAsync(OperatorIntent.ThankYou);
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
        RstEntry = "5NN";
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
    }

    private Task ShowScoreAsync()
    {
        ShowScoreRequested?.Invoke(
            this,
            new ScoreSummaryEventArgs(Score, QsoCount, ContestName, Elapsed));
        return Task.CompletedTask;
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
        QueueSnapshot(snapshot);
    }

    private void ApplySnapshot(SessionSnapshot snapshot)
    {
        if (_sessionId != snapshot.SessionId)
        {
            return;
        }

        _sessionState = snapshot.State;
        OnPropertyChanged(nameof(SessionStateLabel));
        OnPropertyChanged(nameof(IsSetupEnabled));
        SimulationBlock = snapshot.SimulationBlock;
        Elapsed = snapshot.ElapsedSimulationTime.ToString(
            @"mm\:ss\.fff",
            CultureInfo.InvariantCulture);
        LastCaller = snapshot.LastCaller ?? "Waiting";
        CallerState = FormatOperatorState(snapshot.ActiveOperatorState);
        ActiveCallerCount = snapshot.ActiveStations?.Count ?? 0;
        LastSent = snapshot.LastOperatorMessage ?? "None";
        QsoCount = snapshot.QsoCount;
        Score = snapshot.Score;
        WordsPerMinute = snapshot.CurrentWordsPerMinute;
        BandwidthHz = snapshot.CurrentBandwidthHz;
        RitOffsetHz = snapshot.RitOffsetHz;
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

    private void DrainSnapshots()
    {
        while (true)
        {
            SessionSnapshot? snapshot =
                Interlocked.Exchange(ref _pendingSnapshot, null);
            if (snapshot is not null)
            {
                ApplySnapshot(snapshot);
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
            CompleteQsoCommand,
            SendCallAndExchangeCommand,
            RitUpCommand,
            RitDownCommand,
            BandwidthUpCommand,
            BandwidthDownCommand,
            SpeedUpCommand,
            SpeedDownCommand,
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

    private static bool GetBool(
        IReadOnlyDictionary<string, string> values,
        string key,
        bool fallback) =>
        values.TryGetValue(key, out string? value)
        && Boolean.TryParse(value, out bool parsed)
            ? parsed
            : fallback;
}
