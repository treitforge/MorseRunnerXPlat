using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using MorseRunner.Client;
using MorseRunner.Domain;

namespace MorseRunner.App.ViewModels;

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
    private readonly IMorseRunnerClient _client;
    private readonly string _contestName = ContestCatalog.All[0].DisplayName;
    private readonly string _seed = "12345";
    private SessionId? _sessionId;
    private SessionState _sessionState = SessionState.Created;
    private string _selectedRunMode = "rmPileup";
    private string _status = "Ready for a deterministic practice session.";
    private long _simulationBlock;
    private string _elapsed = "00:00.000";
    private string _lastCaller = "Waiting";
    private string _lastSent = "None";
    private string _callEntry = string.Empty;
    private string _rstEntry = "5NN";
    private string _exchange1Entry = string.Empty;
    private string _exchange2Entry = string.Empty;
    private string _stationCall = "W7SST";
    private int _wordsPerMinute = 30;
    private int _pitchHz = 600;
    private int _bandwidthHz = 500;
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
    private int _disposed;

    public MainWindowViewModel(IMorseRunnerClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        StartCommand = new AsyncCommand(
            () => StartAsync("rmPileup"),
            CanStart);
        StartSingleCommand = new AsyncCommand(
            () => StartAsync("rmSingle"),
            CanStart);
        StartWpxCommand = new AsyncCommand(
            () => StartAsync("rmWpx"),
            CanStart);
        StartHstCommand = new AsyncCommand(
            () => StartAsync("rmHst"),
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
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<ScoreSummaryEventArgs>? ShowScoreRequested;

    public string ContestName => _contestName;

    public string RunMode => _selectedRunMode[2..];

    public string Seed => _seed;

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
        set => SetField(ref _stationCall, value);
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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_sessionId is SessionId sessionId)
        {
            await _client.CloseSessionAsync(sessionId, CancellationToken.None);
        }

        await _client.DisposeAsync();
    }

    private bool CanStart() =>
        _sessionId is null || _sessionState == SessionState.Ready;

    private AsyncCommand CreateIntentCommand(OperatorIntent intent) =>
        new(
            () => SendIntentAsync(intent),
            () => _sessionState == SessionState.Running);

    private AsyncCommand CreateRadioCommand(RadioControl control, int delta) =>
        new(
            () => AdjustRadioAsync(control, delta),
            () => _sessionState is SessionState.Running or SessionState.Paused);

    private async Task StartAsync(string runMode)
    {
        try
        {
            _selectedRunMode = runMode;
            OnPropertyChanged(nameof(RunMode));
            if (_sessionId is null)
            {
                var settings = new SessionSettings(
                    Seed: 12_345,
                    ContestCatalog.All[0].Id,
                    new RunModeId(runMode),
                    DurationBlocks: 0)
                {
                    StationCall = StationCall,
                    WordsPerMinute = WordsPerMinute,
                    PitchHz = PitchHz,
                    BandwidthHz = BandwidthHz,
                };
                SessionHandle handle = await _client.CreateSessionAsync(
                    settings,
                    CancellationToken.None);
                _sessionId = handle.SessionId;
                _sessionState = handle.State;
            }

            CommandResult result = await ExecuteAsync(
                new StartSessionCommand(
                    RequestId.New(),
                    _sessionId.Value,
                    DesktopClientId));
            Status = result.Accepted
                ? $"{RunMode} session running. Use F1 through F8 to send."
                : result.Message ?? "Start was rejected.";
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            Status = exception.Message;
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
            ? $"Committed four blocks at boundary {result.AppliedBlock}."
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
            ? $"Logged {CallEntry.ToUpperInvariant()}."
            : result.Message ?? "QSO logging was rejected.";
        if (result.Accepted)
        {
            await WipeAsync();
        }

        await RefreshAsync();
    }

    private Task WipeAsync()
    {
        CallEntry = string.Empty;
        RstEntry = "5NN";
        Exchange1Entry = string.Empty;
        Exchange2Entry = string.Empty;
        Status = "Entry fields cleared.";
        return Task.CompletedTask;
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

    private Task<CommandResult> ExecuteAsync(SessionCommand command) =>
        _client.ExecuteAsync(command, CancellationToken.None);

    private async Task RefreshAsync()
    {
        if (_sessionId is not SessionId sessionId)
        {
            return;
        }

        SessionSnapshot snapshot = await _client.GetSnapshotAsync(
            sessionId,
            CancellationToken.None);
        _sessionState = snapshot.State;
        SimulationBlock = snapshot.SimulationBlock;
        Elapsed = snapshot.ElapsedSimulationTime.ToString(
            @"mm\:ss\.fff",
            CultureInfo.InvariantCulture);
        LastCaller = snapshot.LastCaller ?? "Waiting";
        LastSent = snapshot.LastOperatorMessage ?? "None";
        QsoCount = snapshot.QsoCount;
        Score = snapshot.Score;
        RaiseCommandStateChanged();
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

    private void SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged(string? propertyName)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
