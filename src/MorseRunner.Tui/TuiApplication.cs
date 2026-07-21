using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using MorseRunner.Client;
using MorseRunner.Domain;
using MorseRunner.Infrastructure;

namespace MorseRunner.Tui;

public sealed class TuiApplication : IDisposable
{
    private static readonly TimeSpan SnapshotRenderInterval =
        TimeSpan.FromMilliseconds(100);
    private static readonly ClientId TuiClientId = new("tui");
    private readonly IMorseRunnerClient _client;
    private readonly Channel<SessionSnapshot> _snapshots =
        Channel.CreateBounded<SessionSnapshot>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            });
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SettingsStore? _settingsStore;
    private readonly HighScoreStore? _highScoreStore;
    private readonly TuiRecordingPreference? _recordingPreference;
    private readonly string? _resultsDirectory;
    private readonly Func<string, Task> _artifactLauncher;
    private readonly bool _forceNoColor;
    private SessionId? _sessionId;
    private Task? _subscriptionTask;
    private SessionId? _recordedResultSessionId;
    private string[]? _lastFrameLines;
    private int _lastFrameWidth;
    private int _lastFrameHeight;
    private long _lastRenderTimestamp;
    private bool _quit;
    private volatile bool _dirty = true;

    public TuiApplication(
        IMorseRunnerClient client,
        bool isHosted,
        ApplicationPaths? paths = null,
        TuiRecordingPreference? recordingPreference = null,
        Func<string, Task>? artifactLauncher = null,
        bool forceNoColor = false)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _recordingPreference = recordingPreference
            ?? (paths is null ? null : new TuiRecordingPreference(paths));
        _settingsStore = paths is null
            ? null
            : new SettingsStore(Path.Combine(paths.Settings, "settings.json"));
        _highScoreStore = paths is null
            ? null
            : new HighScoreStore(
                Path.Combine(paths.Results, "high-scores.json"));
        _resultsDirectory = paths?.Results;
        _artifactLauncher = artifactLauncher ?? OpenArtifactAsync;
        _forceNoColor = forceNoColor;
        State = new TuiState
        {
            IsHosted = isHosted,
            ConnectionStatus = isHosted
                ? "Connecting to authenticated local host."
                : "Local in-process engine.",
        };
    }

    public TuiState State { get; }

    public void Dispose()
    {
        _lifetime.Cancel();
        _highScoreStore?.Dispose();
        _lifetime.Dispose();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_settingsStore is not null)
        {
            SettingsLoadResult result = await _settingsStore.LoadAsync(
                cancellationToken);
            ApplyPreferences(result.Document.Values);
            if (!String.IsNullOrWhiteSpace(result.Diagnostic))
            {
                State.Status = result.Diagnostic;
            }
        }

        if (_recordingPreference is not null)
        {
            _recordingPreference.Enabled =
                State.RecordingEnabled && !State.IsHosted;
            State.LastRecordingPath = _recordingPreference.DiscoverLatest();
        }

        try
        {
            EngineInfo info = await _client.GetEngineInfoAsync(cancellationToken);
            State.EngineDiagnostic =
                $"{info.DisplayName} {info.DiagnosticVersion} | "
                + $"{(info.IsInProcess ? "in-process" : "hosted")} | "
                + $"{String.Join(", ", info.Capabilities)}";
            State.ConnectionStatus = State.IsHosted
                ? "Connected to authenticated local host."
                : "Local in-process engine ready.";
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            State.ConnectionStatus =
                $"Connection failed: {exception.Message}";
            State.Status = State.ConnectionStatus;
        }
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetime.Token);
        Console.OutputEncoding = Encoding.UTF8;
        Console.CancelKeyPress += Cancel;
        TerminalCapabilities capabilities =
            TerminalCapabilities.Detect(_forceNoColor);
        if (capabilities.UseAnsi)
        {
            Console.Write("\u001b[?1049h\u001b[?25l");
        }

        try
        {
            while (!_quit && !linked.IsCancellationRequested)
            {
                while (_snapshots.Reader.TryRead(out SessionSnapshot? snapshot))
                {
                    State.Snapshot = snapshot;
                    _dirty = true;
                }

                if (WindowSizeChanged(capabilities.UseAnsi))
                {
                    _dirty = true;
                }

                if (_dirty && RenderIntervalElapsed())
                {
                    Draw(capabilities);
                    _dirty = false;
                }

                if (Console.KeyAvailable)
                {
                    ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                    await HandleAsync(TuiKeyRouter.Map(key), linked.Token);
                    Draw(capabilities);
                    _dirty = false;
                    continue;
                }

                await Task.Delay(25, linked.Token);
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        finally
        {
            Console.CancelKeyPress -= Cancel;
            try
            {
                await CloseSessionAsync(CancellationToken.None);
            }
            finally
            {
                if (capabilities.UseAnsi)
                {
                    Console.Write("\u001b[?25h\u001b[?1049l");
                }
                else
                {
                    Console.Clear();
                }
            }
        }

        return 0;
    }

    public async Task HandleAsync(
        TuiAction action,
        CancellationToken cancellationToken)
    {
        try
        {
            await HandleCoreAsync(action, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            State.ConnectionStatus = State.IsHosted
                ? $"Hosted connection error: {exception.Message}"
                : State.ConnectionStatus;
            State.Status = $"Command failed: {exception.Message}";
            _dirty = true;
        }
    }

    private async Task HandleCoreAsync(
        TuiAction action,
        CancellationToken cancellationToken)
    {
        if (State.View == TuiView.Settings
            && await HandleSettingsActionAsync(action, cancellationToken))
        {
            return;
        }

        if (State.View != TuiView.Operator
            && action.Kind == TuiActionKind.Abort)
        {
            State.View = TuiView.Operator;
            return;
        }

        if (State.View is TuiView.Results
                or TuiView.Diagnostics
                or TuiView.Help
            && !IsViewAction(action.Kind))
        {
            State.Status =
                "Press Escape or the current view shortcut to return.";
            return;
        }

        switch (action.Kind)
        {
            case TuiActionKind.InsertCharacter:
                InsertCharacter(action.Character);
                break;
            case TuiActionKind.Backspace:
                Backspace();
                break;
            case TuiActionKind.NextField:
                State.ActiveField = (State.ActiveField + 1) % 4;
                break;
            case TuiActionKind.PreviousField:
                State.ActiveField = (State.ActiveField + 3) % 4;
                break;
            case TuiActionKind.StartPileup:
                await StartAsync(0, cancellationToken);
                break;
            case TuiActionKind.StartSingle:
                await StartAsync(1, cancellationToken);
                break;
            case TuiActionKind.StartWpx:
                await StartAsync(2, cancellationToken);
                break;
            case TuiActionKind.StartHst:
                await StartAsync(3, cancellationToken);
                break;
            case TuiActionKind.Stop:
                await ExecuteStateCommandAsync(
                    sessionId => new StopSessionCommand(
                        RequestId.New(),
                        sessionId,
                        TuiClientId),
                    "Session completed.",
                    cancellationToken);
                if (State.Snapshot?.State == SessionState.Completed)
                {
                    await RefreshResultAsync(
                        recordHighScore: true,
                        cancellationToken);
                }
                break;
            case TuiActionKind.Pause:
                await ExecuteStateCommandAsync(
                    sessionId => new PauseSessionCommand(
                        RequestId.New(),
                        sessionId,
                        TuiClientId),
                    "Session paused.",
                    cancellationToken);
                break;
            case TuiActionKind.Resume:
                await ExecuteStateCommandAsync(
                    sessionId => new ResumeSessionCommand(
                        RequestId.New(),
                        sessionId,
                        TuiClientId),
                    "Session resumed.",
                    cancellationToken);
                break;
            case TuiActionKind.SendCq:
                await SendAsync(OperatorIntent.Cq, cancellationToken);
                break;
            case TuiActionKind.SendExchange:
                await SendAsync(OperatorIntent.Exchange, cancellationToken);
                break;
            case TuiActionKind.SendThankYou:
                await SendAsync(OperatorIntent.ThankYou, cancellationToken);
                break;
            case TuiActionKind.SendMyCall:
                await SendAsync(OperatorIntent.MyCall, cancellationToken);
                break;
            case TuiActionKind.SendHisCall:
                await SendAsync(OperatorIntent.HisCall, cancellationToken);
                break;
            case TuiActionKind.SendBefore:
                await SendAsync(OperatorIntent.Before, cancellationToken);
                break;
            case TuiActionKind.SendQuestion:
                await SendAsync(OperatorIntent.Question, cancellationToken);
                break;
            case TuiActionKind.SendNil:
                await SendAsync(OperatorIntent.Nil, cancellationToken);
                break;
            case TuiActionKind.SendNumberQuestion:
                await SendAsync(OperatorIntent.NumberQuestion, cancellationToken);
                break;
            case TuiActionKind.SendCallAndExchange:
                await SendAsync(OperatorIntent.HisCall, cancellationToken);
                await SendAsync(OperatorIntent.Exchange, cancellationToken);
                break;
            case TuiActionKind.EnterSendMessage:
                await EnterSendMessageAsync(cancellationToken);
                break;
            case TuiActionKind.SaveQso:
                await LogQsoAsync(
                    sendThankYou: false,
                    cancellationToken);
                break;
            case TuiActionKind.LogQso:
                await LogQsoAsync(
                    sendThankYou: true,
                    cancellationToken);
                break;
            case TuiActionKind.Wipe:
                State.ClearEntry();
                State.Status = "Entry fields cleared.";
                break;
            case TuiActionKind.Abort:
                await SendAsync(OperatorIntent.Abort, cancellationToken);
                break;
            case TuiActionKind.RitUp:
                await AdjustAsync(RadioControl.Rit, 50, cancellationToken);
                break;
            case TuiActionKind.RitDown:
                await AdjustAsync(RadioControl.Rit, -50, cancellationToken);
                break;
            case TuiActionKind.BandwidthUp:
                await AdjustAsync(RadioControl.Bandwidth, 50, cancellationToken);
                break;
            case TuiActionKind.BandwidthDown:
                await AdjustAsync(RadioControl.Bandwidth, -50, cancellationToken);
                break;
            case TuiActionKind.SpeedUp:
                await AdjustAsync(
                    RadioControl.Speed,
                    GetSpeedUpDelta(),
                    cancellationToken);
                break;
            case TuiActionKind.SpeedDown:
                await AdjustAsync(RadioControl.Speed, -2, cancellationToken);
                break;
            case TuiActionKind.NextContest:
                ChangeSetup(
                    () => State.ContestIndex =
                        (State.ContestIndex + 1) % ContestCatalog.All.Count);
                break;
            case TuiActionKind.PreviousContest:
                ChangeSetup(
                    () => State.ContestIndex =
                        (State.ContestIndex + ContestCatalog.All.Count - 1)
                        % ContestCatalog.All.Count);
                break;
            case TuiActionKind.NextRunMode:
                ChangeSetup(
                    () => State.RunModeIndex =
                        (State.RunModeIndex + 1) % TuiState.RunModes.Count);
                break;
            case TuiActionKind.PreviousRunMode:
                ChangeSetup(
                    () => State.RunModeIndex =
                        (State.RunModeIndex + TuiState.RunModes.Count - 1)
                        % TuiState.RunModes.Count);
                break;
            case TuiActionKind.NextDuration:
                ChangeSetup(
                    () => State.DurationIndex =
                        (State.DurationIndex + 1)
                        % TuiState.DurationMinutesValues.Count);
                break;
            case TuiActionKind.ToggleQsk:
                ChangeSetup(() => State.Qsk = !State.Qsk);
                break;
            case TuiActionKind.ToggleQsb:
                ChangeSetup(() => State.Qsb = !State.Qsb);
                break;
            case TuiActionKind.ToggleQrm:
                ChangeSetup(() => State.Qrm = !State.Qrm);
                break;
            case TuiActionKind.ToggleQrn:
                ChangeSetup(() => State.Qrn = !State.Qrn);
                break;
            case TuiActionKind.ToggleFlutter:
                ChangeSetup(() => State.Flutter = !State.Flutter);
                break;
            case TuiActionKind.ToggleLids:
                ChangeSetup(() => State.Lids = !State.Lids);
                break;
            case TuiActionKind.ToggleSettings:
                State.View = State.View == TuiView.Settings
                    ? TuiView.Operator
                    : TuiView.Settings;
                break;
            case TuiActionKind.ToggleResults:
                State.View = State.View == TuiView.Results
                    ? TuiView.Operator
                    : TuiView.Results;
                await RefreshResultAsync(
                    recordHighScore: false,
                    cancellationToken);
                break;
            case TuiActionKind.ToggleDiagnostics:
                State.View = State.View == TuiView.Diagnostics
                    ? TuiView.Operator
                    : TuiView.Diagnostics;
                await RefreshSnapshotAsync(cancellationToken);
                break;
            case TuiActionKind.ToggleRecording:
                ToggleRecording();
                break;
            case TuiActionKind.ExportJson:
                await ExportResultAsync(
                    ResultExportFormat.Json,
                    cancellationToken);
                break;
            case TuiActionKind.ExportCabrillo:
                await ExportResultAsync(
                    ResultExportFormat.Cabrillo,
                    cancellationToken);
                break;
            case TuiActionKind.OpenRecording:
                await OpenRecordingAsync();
                break;
            case TuiActionKind.IncreaseSetting:
            case TuiActionKind.DecreaseSetting:
                break;
            case TuiActionKind.ToggleHelp:
                State.View = State.View == TuiView.Help
                    ? TuiView.Operator
                    : TuiView.Help;
                break;
            case TuiActionKind.Quit:
                _quit = true;
                break;
            case TuiActionKind.None:
            default:
                break;
        }

        if (ShouldPersist(action.Kind))
        {
            await SavePreferencesAsync(cancellationToken);
        }
    }

    private async Task StartAsync(
        int runModeIndex,
        CancellationToken cancellationToken)
    {
        SessionState? currentState = State.Snapshot?.State;
        if (currentState is SessionState.Running or SessionState.Paused)
        {
            State.Status = "Stop the active session before starting another.";
            return;
        }

        await CloseSessionAsync(cancellationToken);
        State.RunModeIndex = runModeIndex;
        State.Qsos = [];
        var settings = new SessionSettings(
            State.Seed,
            State.Contest.Id,
            State.RunMode,
            DurationBlocks(State.DurationMinutes))
        {
            StationCall = State.StationCall,
            WordsPerMinute = State.WordsPerMinute,
            PitchHz = State.PitchHz,
            BandwidthHz = State.BandwidthHz,
            Activity = State.Activity,
            Qsk = State.Qsk,
            Qsb = State.Qsb,
            Qrm = State.Qrm,
            Qrn = State.Qrn,
            Flutter = State.Flutter,
            Lids = State.Lids,
            MonitorLevelDb = State.MonitorLevelDb,
            ReceiveSpeedBelowWpm = State.ReceiveSpeedBelowWpm,
            ReceiveSpeedAboveWpm = State.ReceiveSpeedAboveWpm,
            SerialNumberRange = State.SerialNumberRange,
            CustomSerialNumberMinimum =
                State.CustomSerialNumberMinimum,
            CustomSerialNumberExclusiveMaximum =
                State.CustomSerialNumberExclusiveMaximum,
            CustomSerialNumberMinimumDigits =
                State.CustomSerialNumberMinimumDigits,
            CustomSerialNumberMaximumDigits =
                State.CustomSerialNumberMaximumDigits,
            HstOperatorName = State.HstOperatorName,
        };
        SessionHandle handle = await _client.CreateSessionAsync(
            settings,
            cancellationToken);
        _sessionId = handle.SessionId;
        _subscriptionTask = ObserveAsync(
            handle.SessionId,
            _lifetime.Token);
        CommandResult result = await _client.ExecuteAsync(
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                TuiClientId),
            cancellationToken);
        State.Status = result.Accepted
            ? $"{RunModeName(State.RunMode)} running."
            : result.Message ?? "Start rejected.";
        State.ConnectionStatus = State.IsHosted
            ? "Connected to authenticated local host."
            : "Local in-process engine ready.";
        await RefreshSnapshotAsync(cancellationToken);
        await SavePreferencesAsync(cancellationToken);
    }

    private async Task ObserveAsync(
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
                    _snapshots.Writer.TryWrite(snapshot);
                    State.ConnectionStatus = State.IsHosted
                        ? "Connected to authenticated local host."
                        : State.ConnectionStatus;
                    if (snapshot.State == SessionState.Completed
                        && _recordedResultSessionId != snapshot.SessionId)
                    {
                        _recordedResultSessionId = snapshot.SessionId;
                        await RefreshResultAsync(
                            recordHighScore: true,
                            cancellationToken);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            State.ConnectionStatus = State.IsHosted
                ? $"Disconnected after reconnect attempt: {exception.Message}"
                : State.ConnectionStatus;
            State.Status = $"Live update stopped: {exception.Message}";
            _dirty = true;
        }
    }

    private async Task SendAsync(
        OperatorIntent intent,
        CancellationToken cancellationToken)
    {
        if (_sessionId is not SessionId sessionId)
        {
            State.Status = "Start a session before sending.";
            return;
        }

        CommandResult result = await _client.ExecuteAsync(
            new SendOperatorIntentCommand(
                RequestId.New(),
                sessionId,
                TuiClientId,
                intent,
                State.Call,
                State.Rst,
                State.Exchange1,
                State.Exchange2),
            cancellationToken);
        State.Status = result.Accepted
            ? $"Sent {intent}."
            : result.Message ?? "Send rejected.";
        await RefreshSnapshotAsync(cancellationToken);
    }

    private async Task LogQsoAsync(
        bool sendThankYou,
        CancellationToken cancellationToken)
    {
        if (_sessionId is not SessionId sessionId)
        {
            State.Status = "Start a session before logging.";
            return;
        }

        if (State.Call.Length < 3)
        {
            State.Status = "Enter a callsign before logging.";
            return;
        }

        string call = State.Call.ToUpperInvariant();
        if (sendThankYou)
        {
            await SendAsync(OperatorIntent.ThankYou, cancellationToken);
        }

        CommandResult result = await _client.ExecuteAsync(
            new LogQsoCommand(
                RequestId.New(),
                sessionId,
                TuiClientId,
                State.Call,
                State.Rst,
                State.Exchange1,
                State.Exchange2),
            cancellationToken);
        State.Status = result.Accepted
            ? $"Logged {call}."
            : result.Message ?? "Log rejected.";
        if (result.Accepted)
        {
            State.Qsos = await _client.ListCompletedQsosAsync(
                sessionId,
                cancellationToken);
            if (State.Qsos[^1].IsDuplicate)
            {
                State.Status = $"Logged {call} as a duplicate.";
            }

            State.ClearEntry();
        }

        await RefreshSnapshotAsync(cancellationToken);
    }

    private async Task EnterSendMessageAsync(
        CancellationToken cancellationToken)
    {
        if (_sessionId is not SessionId sessionId)
        {
            State.Status = "Start a session before sending.";
            return;
        }

        CommandResult result = await _client.ExecuteAsync(
            new TriggerEnterSendMessageCommand(
                RequestId.New(),
                sessionId,
                TuiClientId,
                new(
                    State.Call,
                    State.Rst,
                    State.Exchange1,
                    State.Exchange2)),
            cancellationToken);
        EnterSendMessageResult? enter = result.EnterSendMessage;
        if (enter is null)
        {
            State.Status = result.Message ?? "Enter Sends Message rejected.";
            await RefreshSnapshotAsync(cancellationToken);
            return;
        }

        string loggedCall = State.Call.ToUpperInvariant();
        State.Status = enter.Outcome switch
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
        State.ActiveField = (int)enter.FocusTarget;
        if (enter.ClearEntry)
        {
            State.Qsos = await _client.ListCompletedQsosAsync(
                sessionId,
                cancellationToken);
            if (State.Qsos[^1].IsDuplicate)
            {
                State.Status = $"Logged {loggedCall} as a duplicate.";
            }

            State.ClearEntry();
        }

        await RefreshSnapshotAsync(cancellationToken);
    }

    private async Task AdjustAsync(
        RadioControl control,
        int delta,
        CancellationToken cancellationToken)
    {
        if (_sessionId is not SessionId sessionId)
        {
            State.Status = "Start a session before adjusting the radio.";
            return;
        }

        CommandResult result = await _client.ExecuteAsync(
            new AdjustRadioControlCommand(
                RequestId.New(),
                sessionId,
                TuiClientId,
                control,
                delta),
            cancellationToken);
        State.Status = result.Accepted
            ? $"Adjusted {control}."
            : result.Message ?? "Adjustment rejected.";
        await RefreshSnapshotAsync(cancellationToken);
    }

    private int GetSpeedUpDelta()
    {
        SessionSnapshot? snapshot = State.Snapshot;
        return snapshot?.RunModeId.Value == "rmHst"
            ? ((snapshot.CurrentWordsPerMinute / 5) * 5 + 5)
                - snapshot.CurrentWordsPerMinute
            : 2;
    }

    private async Task ExecuteStateCommandAsync(
        Func<SessionId, SessionCommand> createCommand,
        string success,
        CancellationToken cancellationToken)
    {
        if (_sessionId is not SessionId sessionId)
        {
            State.Status = "No active session.";
            return;
        }

        CommandResult result = await _client.ExecuteAsync(
            createCommand(sessionId),
            cancellationToken);
        State.Status = result.Accepted
            ? success
            : result.Message ?? "Command rejected.";
        await RefreshSnapshotAsync(cancellationToken);
    }

    private async Task<bool> HandleSettingsActionAsync(
        TuiAction action,
        CancellationToken cancellationToken)
    {
        switch (action.Kind)
        {
            case TuiActionKind.ToggleSettings:
            case TuiActionKind.Abort:
                State.View = TuiView.Operator;
                State.Status = "Advanced settings saved.";
                await SavePreferencesAsync(cancellationToken);
                return true;
            case TuiActionKind.NextField:
            case TuiActionKind.RitDown:
                State.SettingsIndex = (State.SettingsIndex + 1) % 21;
                return true;
            case TuiActionKind.PreviousField:
            case TuiActionKind.RitUp:
                State.SettingsIndex = (State.SettingsIndex + 20) % 21;
                return true;
            case TuiActionKind.IncreaseSetting:
            case TuiActionKind.EnterSendMessage:
                AdjustCurrentSetting(1);
                await SavePreferencesAsync(cancellationToken);
                return true;
            case TuiActionKind.DecreaseSetting:
                AdjustCurrentSetting(-1);
                await SavePreferencesAsync(cancellationToken);
                return true;
            case TuiActionKind.SpeedUp:
                AdjustCurrentSetting(10);
                await SavePreferencesAsync(cancellationToken);
                return true;
            case TuiActionKind.SpeedDown:
                AdjustCurrentSetting(-10);
                await SavePreferencesAsync(cancellationToken);
                return true;
            case TuiActionKind.InsertCharacter:
                EditCurrentText(action.Character);
                await SavePreferencesAsync(cancellationToken);
                return true;
            case TuiActionKind.Backspace:
                BackspaceCurrentText();
                await SavePreferencesAsync(cancellationToken);
                return true;
            case TuiActionKind.Wipe:
                ClearCurrentText();
                await SavePreferencesAsync(cancellationToken);
                return true;
            case TuiActionKind.ToggleRecording:
                ToggleRecording();
                await SavePreferencesAsync(cancellationToken);
                return true;
            default:
                return false;
        }
    }

    private void AdjustCurrentSetting(int direction)
    {
        static int Step(int value, int delta, int minimum, int maximum) =>
            Math.Clamp(value + delta, minimum, maximum);

        switch (State.SettingsIndex)
        {
            case 1:
                State.WordsPerMinute = Step(
                    State.WordsPerMinute,
                    direction,
                    10,
                    120);
                break;
            case 2:
                State.PitchHz = Step(
                    State.PitchHz,
                    direction * 10,
                    100,
                    2_000);
                break;
            case 3:
                State.BandwidthHz = Step(
                    State.BandwidthHz,
                    direction * 50,
                    50,
                    5_000);
                break;
            case 4:
                State.Activity = Step(State.Activity, direction, 1, 9);
                break;
            case 5:
                State.MonitorLevelDb = Math.Clamp(
                    State.MonitorLevelDb + direction,
                    -60d,
                    12d);
                break;
            case 6:
                State.ReceiveSpeedBelowWpm = NextReceiveOffset(
                    State.ReceiveSpeedBelowWpm,
                    Math.Sign(direction));
                break;
            case 7:
                State.ReceiveSpeedAboveWpm = NextReceiveOffset(
                    State.ReceiveSpeedAboveWpm,
                    Math.Sign(direction));
                break;
            case 8:
                State.SerialNumberRange = (SerialNumberRangeMode)
                    Mod(
                        (int)State.SerialNumberRange + Math.Sign(direction),
                        Enum.GetValues<SerialNumberRangeMode>().Length);
                break;
            case 9:
                State.CustomSerialNumberMinimum = Step(
                    State.CustomSerialNumberMinimum,
                    direction,
                    1,
                    State.CustomSerialNumberExclusiveMaximum - 1);
                State.CustomSerialNumberMinimumDigits = Math.Max(
                    State.CustomSerialNumberMinimumDigits,
                    DecimalDigitCount(State.CustomSerialNumberMinimum));
                break;
            case 10:
                State.CustomSerialNumberExclusiveMaximum = Step(
                    State.CustomSerialNumberExclusiveMaximum,
                    direction,
                    State.CustomSerialNumberMinimum + 1,
                    9_999);
                State.CustomSerialNumberMaximumDigits = Math.Max(
                    State.CustomSerialNumberMaximumDigits,
                    DecimalDigitCount(
                        State.CustomSerialNumberExclusiveMaximum));
                break;
            case 11:
                State.CustomSerialNumberMinimumDigits = Step(
                    State.CustomSerialNumberMinimumDigits,
                    direction,
                    DecimalDigitCount(State.CustomSerialNumberMinimum),
                    4);
                break;
            case 12:
                State.CustomSerialNumberMaximumDigits = Step(
                    State.CustomSerialNumberMaximumDigits,
                    direction,
                    DecimalDigitCount(
                        State.CustomSerialNumberExclusiveMaximum),
                    4);
                break;
            case 14:
                State.Qsk = !State.Qsk;
                break;
            case 15:
                State.Qsb = !State.Qsb;
                break;
            case 16:
                State.Qrm = !State.Qrm;
                break;
            case 17:
                State.Qrn = !State.Qrn;
                break;
            case 18:
                State.Flutter = !State.Flutter;
                break;
            case 19:
                State.Lids = !State.Lids;
                break;
            case 20:
                ToggleRecording();
                break;
        }

        State.Status = "Advanced setting updated.";
    }

    private void EditCurrentText(char character)
    {
        character = Char.ToUpperInvariant(character);
        if (State.SettingsIndex == 0
            && State.StationCall.Length < 18
            && (Char.IsAsciiLetterOrDigit(character) || character == '/'))
        {
            State.StationCall += character;
        }
        else if (State.SettingsIndex == 13
            && State.HstOperatorName.Length < 32
            && !Char.IsControl(character))
        {
            State.HstOperatorName += character;
        }
    }

    private void BackspaceCurrentText()
    {
        if (State.SettingsIndex == 0 && State.StationCall.Length > 0)
        {
            State.StationCall = State.StationCall[..^1];
        }
        else if (State.SettingsIndex == 13
            && State.HstOperatorName.Length > 0)
        {
            State.HstOperatorName = State.HstOperatorName[..^1];
        }
    }

    private void ClearCurrentText()
    {
        if (State.SettingsIndex == 0)
        {
            State.StationCall = string.Empty;
        }
        else if (State.SettingsIndex == 13)
        {
            State.HstOperatorName = string.Empty;
        }
    }

    private void ToggleRecording()
    {
        if (State.IsHosted)
        {
            State.Status =
                "Hosted recording is controlled by the engine host.";
            return;
        }

        if (_recordingPreference is null)
        {
            State.Status = "Recording storage is unavailable.";
            return;
        }

        State.RecordingEnabled = !State.RecordingEnabled;
        _recordingPreference.Enabled = State.RecordingEnabled;
        State.Status = State.RecordingEnabled
            ? "Buffered WAV recording enabled for the next session."
            : "WAV recording disabled.";
    }

    private async Task RefreshResultAsync(
        bool recordHighScore,
        CancellationToken cancellationToken)
    {
        if (_sessionId is not SessionId sessionId
            || State.Snapshot?.State != SessionState.Completed)
        {
            return;
        }

        State.Result = await _client.GetResultAsync(
            sessionId,
            cancellationToken);
        if (_highScoreStore is not null)
        {
            State.PersonalHighScore = recordHighScore
                ? await _highScoreStore.RecordAsync(
                    State.Result,
                    State.HstOperatorName,
                    cancellationToken)
                : await _highScoreStore.GetAsync(
                    State.Result.ContestId,
                    cancellationToken);
        }

        if (_recordingPreference is not null)
        {
            State.LastRecordingPath =
                _recordingPreference.DiscoverLatest();
        }
    }

    private async Task ExportResultAsync(
        ResultExportFormat format,
        CancellationToken cancellationToken)
    {
        await RefreshResultAsync(
            recordHighScore: false,
            cancellationToken);
        if (State.Result is null
            || _sessionId is not SessionId sessionId
            || String.IsNullOrWhiteSpace(_resultsDirectory))
        {
            State.Status =
                "Complete a session before exporting its result.";
            return;
        }

        IReadOnlyList<Qso> qsos =
            await _client.ListCompletedQsosAsync(
                sessionId,
                cancellationToken);
        ResultExportArtifact artifact = ResultExporter.Create(
            State.Result,
            qsos,
            format,
            State.HstOperatorName);
        State.LastExportPath = await ResultExporter.SaveAtomicAsync(
            _resultsDirectory,
            artifact,
            cancellationToken);
        State.View = TuiView.Results;
        State.Status =
            $"Exported {format} result to {State.LastExportPath}.";
    }

    private async Task OpenRecordingAsync()
    {
        string? path = _recordingPreference?.DiscoverLatest()
            ?? State.LastRecordingPath;
        if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            State.Status = "No completed WAV recording was found.";
            return;
        }

        State.LastRecordingPath = path;
        await _artifactLauncher(path);
        State.Status = $"Opened recording {path}.";
    }

    public async Task SavePreferencesAsync(
        CancellationToken cancellationToken)
    {
        if (_settingsStore is null)
        {
            return;
        }

        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Station.Call"] = State.StationCall,
            ["Station.Wpm"] = State.WordsPerMinute.ToString(
                CultureInfo.InvariantCulture),
            ["Station.Pitch"] = State.PitchHz.ToString(
                CultureInfo.InvariantCulture),
            ["Station.BandWidth"] = State.BandwidthHz.ToString(
                CultureInfo.InvariantCulture),
            ["Station.SelfMonVolume"] = State.MonitorLevelDb.ToString(
                CultureInfo.InvariantCulture),
            ["Station.CWMinRxSpeed"] =
                State.ReceiveSpeedBelowWpm.ToString(
                    CultureInfo.InvariantCulture),
            ["Station.CWMaxRxSpeed"] =
                State.ReceiveSpeedAboveWpm.ToString(
                    CultureInfo.InvariantCulture),
            ["Station.SerialNR"] = ((int)State.SerialNumberRange).ToString(
                CultureInfo.InvariantCulture),
            ["Station.SerialNrCustomMinimum"] =
                State.CustomSerialNumberMinimum.ToString(
                    CultureInfo.InvariantCulture),
            ["Station.SerialNrCustomMaximum"] =
                State.CustomSerialNumberExclusiveMaximum.ToString(
                    CultureInfo.InvariantCulture),
            ["Station.SerialNrCustomRange"] = string.Create(
                CultureInfo.InvariantCulture,
                $"{State.CustomSerialNumberMinimum.ToString($"D{State.CustomSerialNumberMinimumDigits}", CultureInfo.InvariantCulture)}-{State.CustomSerialNumberExclusiveMaximum.ToString($"D{State.CustomSerialNumberMaximumDigits}", CultureInfo.InvariantCulture)}"),
            ["Station.Name"] = State.HstOperatorName,
            ["Station.Qsk"] = State.Qsk.ToString(
                CultureInfo.InvariantCulture),
            ["Station.SaveWav"] = State.RecordingEnabled.ToString(
                CultureInfo.InvariantCulture),
            ["Band.Activity"] = State.Activity.ToString(
                CultureInfo.InvariantCulture),
            ["Band.Qsb"] = State.Qsb.ToString(
                CultureInfo.InvariantCulture),
            ["Band.Qrm"] = State.Qrm.ToString(
                CultureInfo.InvariantCulture),
            ["Band.Qrn"] = State.Qrn.ToString(
                CultureInfo.InvariantCulture),
            ["Band.Flutter"] = State.Flutter.ToString(
                CultureInfo.InvariantCulture),
            ["Band.Lids"] = State.Lids.ToString(
                CultureInfo.InvariantCulture),
            ["Contest.SimContest"] = State.Contest.Id.Value,
            ["Contest.DefaultRunMode"] = State.RunMode.Value,
            ["Contest.Duration"] = State.DurationMinutes.ToString(
                CultureInfo.InvariantCulture),
        };
        await _settingsStore.SaveAsync(
            new(SettingsDocument.CurrentSchemaVersion, values),
            cancellationToken);
    }

    private async Task RefreshSnapshotAsync(CancellationToken cancellationToken)
    {
        if (_sessionId is SessionId sessionId)
        {
            State.Snapshot = await _client.GetSnapshotAsync(
                sessionId,
                cancellationToken);
        }
    }

    private async Task CloseSessionAsync(CancellationToken cancellationToken)
    {
        if (_sessionId is not SessionId sessionId)
        {
            return;
        }

        await _client.CloseSessionAsync(sessionId, cancellationToken);
        _sessionId = null;
        if (_subscriptionTask is not null)
        {
            await _subscriptionTask.WaitAsync(
                TimeSpan.FromSeconds(2),
                cancellationToken);
            _subscriptionTask = null;
        }
    }

    private void ChangeSetup(Action change)
    {
        if (State.Snapshot?.State is SessionState.Running or SessionState.Paused)
        {
            State.Status = "Stop the session before changing setup.";
            return;
        }

        change();
        State.Status =
            $"{State.Contest.DisplayName}, {RunModeName(State.RunMode)}, "
            + (State.DurationMinutes == 0
                ? "unlimited."
                : $"{State.DurationMinutes} minutes.");
    }

    private void InsertCharacter(char character)
    {
        character = Char.ToUpperInvariant(character);
        string current = State.Fields[State.ActiveField];
        if (current.Length >= 18)
        {
            return;
        }

        switch (State.ActiveField)
        {
            case 0 when Char.IsAsciiLetterOrDigit(character)
                || character is '/' or '?':
                State.Call += character;
                break;
            case 1:
                if (State.Rst == "5NN")
                {
                    State.Rst = string.Empty;
                }
                character = character switch
                {
                    'A' => '1',
                    'E' => '5',
                    'N' => '9',
                    _ => character,
                };
                if (Char.IsAsciiDigit(character))
                {
                    State.Rst += character;
                }
                break;
            case 2 when Char.IsAsciiLetterOrDigit(character):
                State.Exchange1 += character;
                break;
            case 3 when Char.IsAsciiLetterOrDigit(character)
                || character is '/' or ' ':
                State.Exchange2 += character switch
                {
                    'A' => '1',
                    'N' => '9',
                    'O' or 'T' => '0',
                    _ => character,
                };
                break;
        }
    }

    private void Backspace()
    {
        switch (State.ActiveField)
        {
            case 0 when State.Call.Length > 0:
                State.Call = State.Call[..^1];
                break;
            case 1 when State.Rst.Length > 0:
                State.Rst = State.Rst[..^1];
                break;
            case 2 when State.Exchange1.Length > 0:
                State.Exchange1 = State.Exchange1[..^1];
                break;
            case 3 when State.Exchange2.Length > 0:
                State.Exchange2 = State.Exchange2[..^1];
                break;
        }
    }

    private void Draw(TerminalCapabilities capabilities)
    {
        (int width, int height) = GetViewportSize(capabilities.UseAnsi);
        string frame = TuiRenderer.Render(
            State,
            width,
            height,
            useColor: capabilities.UseColor);
        if (capabilities.UseAnsi)
        {
            WriteAnsiFrame(frame, width, height);
        }
        else
        {
            Console.Clear();
            Console.Write(frame);
            _lastFrameWidth = width;
            _lastFrameHeight = height;
        }

        _lastRenderTimestamp = Stopwatch.GetTimestamp();
    }

    private void WriteAnsiFrame(string frame, int width, int height)
    {
        string[] lines = frame.Split(Environment.NewLine);
        bool fullRepaint = _lastFrameLines is null
            || _lastFrameWidth != width
            || _lastFrameHeight != height
            || _lastFrameLines.Length != lines.Length;
        var output = new StringBuilder(frame.Length + 128);
        output.Append("\u001b[?2026h");
        if (fullRepaint)
        {
            output.Append("\u001b[2J\u001b[H");
            output.Append(frame);
            output.Append("\u001b[0m\u001b[J");
        }
        else
        {
            for (int row = 0; row < lines.Length; row++)
            {
                if (String.Equals(
                    _lastFrameLines![row],
                    lines[row],
                    StringComparison.Ordinal))
                {
                    continue;
                }

                output.Append("\u001b[");
                output.Append(row + 1);
                output.Append(";1H");
                output.Append(lines[row]);
                output.Append("\u001b[0m\u001b[K");
            }
        }

        output.Append("\u001b[?2026l");
        Console.Write(output);
        _lastFrameLines = lines;
        _lastFrameWidth = width;
        _lastFrameHeight = height;
    }

    private bool WindowSizeChanged(bool ansi)
    {
        (int width, int height) = GetViewportSize(ansi);
        return width != _lastFrameWidth || height != _lastFrameHeight;
    }

    private bool RenderIntervalElapsed() =>
        _lastRenderTimestamp == 0
        || Stopwatch.GetElapsedTime(_lastRenderTimestamp)
            >= SnapshotRenderInterval;

    private static (int Width, int Height) GetViewportSize(bool ansi)
    {
        try
        {
            int width = Console.WindowWidth;
            if (ansi && width > 1)
            {
                width--;
            }

            return (Math.Max(1, width), Math.Max(1, Console.WindowHeight));
        }
        catch (IOException)
        {
            return (100, 28);
        }
    }

    private void Cancel(object? sender, ConsoleCancelEventArgs args)
    {
        args.Cancel = true;
        _quit = true;
        _lifetime.Cancel();
    }

    private void ApplyPreferences(
        IReadOnlyDictionary<string, string> values)
    {
        State.StationCall = Get(values, "Station.Call", State.StationCall);
        State.WordsPerMinute = GetInt(
            values,
            "Station.Wpm",
            State.WordsPerMinute);
        State.PitchHz = GetInt(values, "Station.Pitch", State.PitchHz);
        State.BandwidthHz = GetInt(
            values,
            "Station.BandWidth",
            State.BandwidthHz);
        State.MonitorLevelDb = GetDouble(
            values,
            "Station.SelfMonVolume",
            State.MonitorLevelDb);
        State.ReceiveSpeedBelowWpm = GetInt(
            values,
            "Station.CWMinRxSpeed",
            State.ReceiveSpeedBelowWpm);
        State.ReceiveSpeedAboveWpm = GetInt(
            values,
            "Station.CWMaxRxSpeed",
            State.ReceiveSpeedAboveWpm);
        State.SerialNumberRange = (SerialNumberRangeMode)Math.Clamp(
            GetInt(
                values,
                "Station.SerialNR",
                (int)State.SerialNumberRange),
            0,
            Enum.GetValues<SerialNumberRangeMode>().Length - 1);
        State.CustomSerialNumberMinimum = GetInt(
            values,
            "Station.SerialNrCustomMinimum",
            State.CustomSerialNumberMinimum);
        State.CustomSerialNumberExclusiveMaximum = GetInt(
            values,
            "Station.SerialNrCustomMaximum",
            State.CustomSerialNumberExclusiveMaximum);
        if (TryParseSerialNumberRange(
                Get(
                    values,
                    "Station.SerialNrCustomRange",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{State.CustomSerialNumberMinimum.ToString($"D{State.CustomSerialNumberMinimumDigits}", CultureInfo.InvariantCulture)}-{State.CustomSerialNumberExclusiveMaximum.ToString($"D{State.CustomSerialNumberMaximumDigits}", CultureInfo.InvariantCulture)}")),
                out int minimum,
                out int maximum,
                out int minimumDigits,
                out int maximumDigits))
        {
            State.CustomSerialNumberMinimum = minimum;
            State.CustomSerialNumberExclusiveMaximum = maximum;
            State.CustomSerialNumberMinimumDigits = minimumDigits;
            State.CustomSerialNumberMaximumDigits = maximumDigits;
        }

        if (State.CustomSerialNumberMinimum < 1
            || State.CustomSerialNumberExclusiveMaximum
                <= State.CustomSerialNumberMinimum
            || State.CustomSerialNumberExclusiveMaximum > 9_999)
        {
            State.CustomSerialNumberMinimum = 1;
            State.CustomSerialNumberExclusiveMaximum = 99;
            State.CustomSerialNumberMinimumDigits = 2;
            State.CustomSerialNumberMaximumDigits = 2;
        }

        State.HstOperatorName = Get(
            values,
            "Station.Name",
            State.HstOperatorName);
        State.Qsk = GetBool(values, "Station.Qsk", State.Qsk);
        State.RecordingEnabled = GetBool(
            values,
            "Station.SaveWav",
            State.RecordingEnabled);
        State.Activity = GetInt(values, "Band.Activity", State.Activity);
        State.Qsb = GetBool(values, "Band.Qsb", State.Qsb);
        State.Qrm = GetBool(values, "Band.Qrm", State.Qrm);
        State.Qrn = GetBool(values, "Band.Qrn", State.Qrn);
        State.Flutter = GetBool(
            values,
            "Band.Flutter",
            State.Flutter);
        State.Lids = GetBool(values, "Band.Lids", State.Lids);

        string contestId = Get(
            values,
            "Contest.SimContest",
            State.Contest.Id.Value);
        int contestIndex = ContestCatalog.All
            .Select((contest, index) => (contest, index))
            .FirstOrDefault(
                item => item.contest.Id.Value == contestId)
            .index;
        if (ContestCatalog.All[contestIndex].Id.Value == contestId)
        {
            State.ContestIndex = contestIndex;
        }

        string runMode = Get(
            values,
            "Contest.DefaultRunMode",
            State.RunMode.Value);
        int runModeIndex = TuiState.RunModes
            .Select((mode, index) => (mode, index))
            .FirstOrDefault(item => item.mode.Value == runMode)
            .index;
        if (TuiState.RunModes[runModeIndex].Value == runMode)
        {
            State.RunModeIndex = runModeIndex;
        }

        int duration = GetInt(
            values,
            "Contest.Duration",
            State.DurationMinutes);
        int durationIndex = -1;
        for (int index = 0;
            index < TuiState.DurationMinutesValues.Count;
            index++)
        {
            if (TuiState.DurationMinutesValues[index] == duration)
            {
                durationIndex = index;
                break;
            }
        }
        if (durationIndex >= 0)
        {
            State.DurationIndex = durationIndex;
        }
    }

    private static bool ShouldPersist(TuiActionKind action) =>
        action is TuiActionKind.NextContest
            or TuiActionKind.PreviousContest
            or TuiActionKind.NextRunMode
            or TuiActionKind.PreviousRunMode
            or TuiActionKind.NextDuration
            or TuiActionKind.ToggleQsk
            or TuiActionKind.ToggleQsb
            or TuiActionKind.ToggleQrm
            or TuiActionKind.ToggleQrn
            or TuiActionKind.ToggleFlutter
            or TuiActionKind.ToggleLids
            or TuiActionKind.ToggleRecording
            or TuiActionKind.Quit;

    private static bool IsViewAction(TuiActionKind action) =>
        action is TuiActionKind.ToggleSettings
            or TuiActionKind.ToggleResults
            or TuiActionKind.ToggleDiagnostics
            or TuiActionKind.ToggleRecording
            or TuiActionKind.ExportJson
            or TuiActionKind.ExportCabrillo
            or TuiActionKind.OpenRecording
            or TuiActionKind.ToggleHelp
            or TuiActionKind.Quit;

    private static int NextReceiveOffset(int current, int direction)
    {
        int[] values = [0, 1, 2, 4, 6, 8, 10];
        int index = Array.IndexOf(values, current);
        index = index < 0 ? 0 : index;
        return values[Mod(index + direction, values.Length)];
    }

    private static int Mod(int value, int modulus) =>
        ((value % modulus) + modulus) % modulus;

    private static int DecimalDigitCount(int value) =>
        value switch
        {
            >= 1_000 => 4,
            >= 100 => 3,
            >= 10 => 2,
            _ => 1,
        };

    private static bool TryParseSerialNumberRange(
        string value,
        out int minimum,
        out int maximum,
        out int minimumDigits,
        out int maximumDigits)
    {
        string[] parts = value.Split('-', 2);
        minimum = 0;
        maximum = 0;
        bool valid = parts.Length == 2
            && Int32.TryParse(
                parts[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out minimum)
            && Int32.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out maximum)
            && minimum >= 1
            && maximum > minimum
            && maximum <= 9_999
            && parts[0].Length <= 4
            && parts[1].Length <= 4;
        minimumDigits = valid ? parts[0].Length : 0;
        maximumDigits = valid ? parts[1].Length : 0;
        return valid;
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

    private static Task OpenArtifactAsync(string path)
    {
        _ = Process.Start(
            new ProcessStartInfo(path)
            {
                UseShellExecute = true,
            });
        return Task.CompletedTask;
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

    private static string RunModeName(RunModeId runMode) =>
        runMode.Value switch
        {
            "rmPileup" => "Pile-Up",
            "rmSingle" => "Single Calls",
            "rmWpx" => "WPX Competition",
            "rmHst" => "HST Competition",
            _ => runMode.Value,
        };
}
