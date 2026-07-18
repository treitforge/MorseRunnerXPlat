using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using MorseRunner.Client;
using MorseRunner.Domain;

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
    private SessionId? _sessionId;
    private Task? _subscriptionTask;
    private string[]? _lastFrameLines;
    private int _lastFrameWidth;
    private int _lastFrameHeight;
    private long _lastRenderTimestamp;
    private bool _quit;
    private volatile bool _dirty = true;

    public TuiApplication(IMorseRunnerClient client, bool isHosted)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        State = new TuiState
        {
            IsHosted = isHosted,
        };
    }

    public TuiState State { get; }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetime.Token);
        Console.OutputEncoding = Encoding.UTF8;
        Console.CancelKeyPress += Cancel;
        bool ansi = SupportsAnsi();
        if (ansi)
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

                if (WindowSizeChanged(ansi))
                {
                    _dirty = true;
                }

                if (_dirty && RenderIntervalElapsed())
                {
                    Draw(ansi);
                    _dirty = false;
                }

                if (Console.KeyAvailable)
                {
                    ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                    await HandleAsync(TuiKeyRouter.Map(key), linked.Token);
                    Draw(ansi);
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
                if (ansi)
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
            case TuiActionKind.LogQso:
                await LogQsoAsync(cancellationToken);
                break;
            case TuiActionKind.Wipe:
                State.ClearEntry();
                State.Status = "Entry fields cleared.";
                break;
            case TuiActionKind.Abort:
                await SendAsync(OperatorIntent.Abort, cancellationToken);
                break;
            case TuiActionKind.RitUp:
                await AdjustAsync(RadioControl.Rit, 10, cancellationToken);
                break;
            case TuiActionKind.RitDown:
                await AdjustAsync(RadioControl.Rit, -10, cancellationToken);
                break;
            case TuiActionKind.BandwidthUp:
                await AdjustAsync(RadioControl.Bandwidth, 50, cancellationToken);
                break;
            case TuiActionKind.BandwidthDown:
                await AdjustAsync(RadioControl.Bandwidth, -50, cancellationToken);
                break;
            case TuiActionKind.SpeedUp:
                await AdjustAsync(RadioControl.Speed, 1, cancellationToken);
                break;
            case TuiActionKind.SpeedDown:
                await AdjustAsync(RadioControl.Speed, -1, cancellationToken);
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
            case TuiActionKind.ToggleHelp:
                State.ShowHelp = !State.ShowHelp;
                break;
            case TuiActionKind.Quit:
                _quit = true;
                break;
            case TuiActionKind.None:
            default:
                break;
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
            Qsk = State.Qsk,
            Qsb = State.Qsb,
            Qrm = State.Qrm,
            Qrn = State.Qrn,
            Flutter = State.Flutter,
            Lids = State.Lids,
            MonitorLevelDb = 0d,
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
        await RefreshSnapshotAsync(cancellationToken);
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
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
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

    private async Task LogQsoAsync(CancellationToken cancellationToken)
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
        await SendAsync(OperatorIntent.ThankYou, cancellationToken);
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

    private void Draw(bool ansi)
    {
        (int width, int height) = GetViewportSize(ansi);
        string frame = TuiRenderer.Render(
            State,
            width,
            height,
            useColor: ansi);
        if (ansi)
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

    private static bool SupportsAnsi()
    {
        string? term = Environment.GetEnvironmentVariable("TERM");
        return !String.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase);
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
