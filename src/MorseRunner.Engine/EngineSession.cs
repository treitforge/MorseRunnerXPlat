using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MorseRunner.Domain;
using MorseRunner.Dsp;

namespace MorseRunner.Engine;

internal sealed class EngineSession : IAsyncDisposable
{
    private static readonly (int Begin, int Count)[] SerialNumberBins =
    [
        (0, 344),
        (10, 188),
        (20, 178),
        (30, 164),
        (40, 156),
        (50, 179),
        (60, 149),
        (70, 126),
        (80, 118),
        (90, 124),
        (100, 957),
        (200, 628),
        (300, 368),
        (400, 257),
        (500, 239),
        (600, 150),
        (700, 129),
        (800, 100),
        (900, 65),
        (1_000, 79),
        (1_100, 59),
        (1_200, 47),
        (1_300, 44),
        (1_400, 26),
        (1_500, 28),
        (1_600, 36),
        (1_700, 23),
        (1_800, 25),
        (1_900, 23),
        (2_000, 17),
        (2_100, 24),
        (2_200, 16),
        (2_300, 15),
        (2_400, 7),
        (2_500, 11),
        (2_600, 6),
        (2_700, 11),
        (2_800, 4),
        (2_900, 5),
        (3_000, 6),
        (3_100, 1),
        (3_200, 4),
        (3_300, 6),
        (3_400, 3),
        (3_500, 1),
        (3_600, 2),
        (3_700, 3),
        (3_800, 0),
        (3_900, 1),
        (4_000, 2),
        (4_100, 0),
        (4_200, 0),
        (4_300, 0),
        (4_400, 0),
        (4_500, 0),
        (4_600, 1),
        (4_700, 0),
        (4_800, 0),
        (4_900, 0),
        (5_000, UInt16.MaxValue),
    ];

    private const int CommandCapacity = 256;
    private const int SubscriberCapacity = 64;
    private const int EventHistoryCapacity = 256;
    private readonly Guid _engineEpoch;
    private readonly SessionId _sessionId;
    private readonly SessionSettings _settings;
    private readonly IAudioSink _audioSink;
    private readonly LegacyRandom _random;
    private readonly StationReferenceCatalog _stationCatalog;
    private readonly List<SimulatedStation> _stations = [];
    private readonly Channel<WorkItem> _commands;
    private readonly Dictionary<RequestId, RequestRecord> _requests = [];
    private readonly List<Subscriber> _subscribers = [];
    private readonly Queue<SessionEvent> _eventHistory = [];
    private readonly object _subscriberGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource<SessionHandle> _initialized =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly float[] _renderBuffer =
        new float[CompatibilityProfile.BlockSize];
    private readonly float[] _operatorBuffer =
        new float[CompatibilityProfile.BlockSize];
    private readonly float[] _receiverReal =
        new float[CompatibilityProfile.BlockSize];
    private readonly float[] _receiverImaginary =
        new float[CompatibilityProfile.BlockSize];
    private readonly MorseToneRenderer _toneRenderer;
    private readonly LegacyReceiverPipeline _receiverPipeline;
    private readonly LegacyRandom _effectRandom;
    private readonly QsbProcessor? _qsbProcessor;
    private readonly float _monitorGain;
    private readonly bool _automaticTiming;
    private readonly TimeSpan _blockPeriod;
    private readonly AutomaticBlockClock _automaticBlockClock = new(
        CompatibilityProfile.SampleRate,
        CompatibilityProfile.BlockSize);
    private readonly Task _worker;
    private PeriodicTimer? _automaticTimer;
    private SessionSnapshot _snapshot;
    private SessionState _state = SessionState.Created;
    private long _revision;
    private long _simulationBlock;
    private long _renderedSamples;
    private long _eventSequence;
    private string? _lastCaller;
    private string? _lastOperatorMessage;
    private string? _esmSentCall;
    private bool _esmExchangeSent;
    private int _currentWordsPerMinute;
    private int _currentBandwidthHz;
    private int _ritOffsetHz;
    private int _qsoCount;
    private int _score;
    private int _nextStationSerial = 1;
    private bool _hasCreatedStation;
    private int _verifiedPoints;
    private readonly HashSet<string> _workedCalls =
        new(StringComparer.Ordinal);
    private readonly SortedSet<string> _verifiedMultipliers =
        new(StringComparer.Ordinal);
    private Qso[] _completedQsos = [];
    private string? _lastLoggedCall;
    private float _qrmPhase;
    private float _flutterPhase;
    private int _disposed;

    public EngineSession(
        Guid engineEpoch,
        SessionId sessionId,
        SessionSettings settings,
        IAudioSink audioSink,
        MorseRunnerEngineOptions options)
    {
        _engineEpoch = engineEpoch;
        _sessionId = sessionId;
        _settings = settings;
        _audioSink = audioSink;
        _automaticTiming = options.AutomaticTiming;
        _blockPeriod = options.BlockPeriod;
        _random = new(settings.Seed);
        _stationCatalog = StationReferenceCatalog.Load(settings.ContestId);
        _effectRandom = new(settings.Seed);
        _qsbProcessor = settings.Qsb
            ? new QsbProcessor(
                new LegacyRandomEffects(
                    new LegacyRandom(unchecked(settings.Seed ^ 0x71C3_90EF))))
            : null;
        _monitorGain = MathF.Pow(10F, (float)settings.MonitorLevelDb / 20F);
        _currentWordsPerMinute = settings.WordsPerMinute;
        _currentBandwidthHz = settings.BandwidthHz;
        _toneRenderer = new(
            CompatibilityProfile.SampleRate,
            CompatibilityProfile.BlockSize,
            settings.WordsPerMinute,
            settings.PitchHz);
        _receiverPipeline = new(
            CompatibilityProfile.SampleRate,
            CompatibilityProfile.BlockSize,
            settings.BandwidthHz,
            settings.PitchHz,
            CompatibilityProfile.AudioStartupRequestCount);
        _commands = Channel.CreateBounded<WorkItem>(
            new BoundedChannelOptions(CommandCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });
        _snapshot = CreateSnapshot();
        _worker = RunAsync();
    }

    public SessionSnapshot Snapshot
    {
        get
        {
            SessionSnapshot snapshot = Volatile.Read(ref _snapshot);
            if (_audioSink is not IAudioSinkMetricsSource source)
            {
                return snapshot;
            }

            AudioSinkMetrics metrics = source.GetMetrics();
            return snapshot with
            {
                AudioQueuedBlocks = metrics.QueuedBlocks,
                AudioUnderrunCount = metrics.UnderrunCount,
                AudioDroppedBlockCount = metrics.DroppedBlockCount,
                AudioOutputHealthy = metrics.IsHealthy,
            };
        }
    }

    public IReadOnlyList<Qso> CompletedQsos =>
        Volatile.Read(ref _completedQsos);

    public async Task<SessionHandle> InitializeAsync(
        CancellationToken cancellationToken)
    {
        return await _initialized.Task.WaitAsync(cancellationToken);
    }

    public async Task<CommandResult> ExecuteAsync(
        SessionCommand command,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<CommandResult> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        RequestRecord record = new(command, completion);
        lock (_requests)
        {
            if (_requests.TryGetValue(
                    command.RequestId,
                    out RequestRecord? existing))
            {
                if (existing.Command != command)
                {
                    return new(
                        Accepted: false,
                        DomainErrorCodes.DuplicateRequestConflict,
                        "The request ID was already used with a different command.",
                        Snapshot.Revision,
                        Snapshot.SimulationBlock);
                }

                completion = existing.Completion;
            }
            else
            {
                _requests.Add(command.RequestId, record);
                if (!_commands.Writer.TryWrite(new CommandWorkItem(record)))
                {
                    _requests.Remove(command.RequestId);
                    return new(
                        Accepted: false,
                        DomainErrorCodes.CommandQueueFull,
                        "The session command queue is full.",
                        Snapshot.Revision,
                        Snapshot.SimulationBlock);
                }
            }
        }

        return await completion.Task.WaitAsync(cancellationToken);
    }

    public async IAsyncEnumerable<SessionUpdate> SubscribeAsync(
        SessionSubscription subscription,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Channel<SessionUpdate> channel = Channel.CreateBounded<SessionUpdate>(
            new BoundedChannelOptions(SubscriberCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            });
        var subscriber = new Subscriber(channel);
        lock (_subscriberGate)
        {
            long earliestSequence = _eventHistory.Count == 0
                ? _eventSequence + 1
                : _eventHistory.Peek().Sequence;
            bool cannotResume = subscription.AfterSequence > _eventSequence
                || (subscription.AfterSequence > 0
                    && subscription.AfterSequence < earliestSequence - 1);
            if (cannotResume)
            {
                subscriber.MarkResyncRequired();
                channel.Writer.TryComplete();
            }
            else
            {
                foreach (SessionEvent sessionEvent in _eventHistory)
                {
                    if (sessionEvent.Sequence > subscription.AfterSequence
                        && !channel.Writer.TryWrite(
                            SessionUpdate.FromEvent(sessionEvent)))
                    {
                        subscriber.MarkResyncRequired();
                        channel.Writer.TryComplete();
                        break;
                    }
                }

                if (!subscriber.ResyncRequired)
                {
                    if (subscription.AfterSequence == 0)
                    {
                        channel.Writer.TryWrite(
                            SessionUpdate.FromSnapshot(Snapshot));
                    }

                    _subscribers.Add(subscriber);
                }
            }
        }

        try
        {
            while (await channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (channel.Reader.TryRead(out SessionUpdate? update))
                {
                    yield return update;
                }
            }

            if (subscriber.ResyncRequired)
            {
                yield return CreateResyncUpdate();
            }
        }
        finally
        {
            lock (_subscriberGate)
            {
                _subscribers.Remove(subscriber);
            }
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_commands.Writer.TryWrite(new CloseWorkItem(completion)))
        {
            throw new InvalidOperationException("Could not enqueue session close.");
        }

        await completion.Task.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _commands.Writer.TryComplete();
        _lifetime.Cancel();
        StopAutomaticTimer();
        try
        {
            await _worker;
        }
        catch (OperationCanceledException)
        {
        }

        await _audioSink.DisposeAsync();
        _lifetime.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            PublishEvent(SessionEventKind.Created, null);
            await _audioSink.InitializeAsync(
                _sessionId,
                AudioStreamFormat.Compatibility,
                _lifetime.Token);
            _state = SessionState.Ready;
            _revision++;
            PublishEvent(SessionEventKind.Ready, null);
            PublishSnapshot();
            _initialized.SetResult(
                new(_sessionId, _engineEpoch, _state, _revision));

            while (await ProcessNextWorkItemOrBlockAsync())
            {
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            _initialized.TrySetCanceled(_lifetime.Token);
        }
        catch (Exception exception)
        {
            _state = SessionState.Faulted;
            _revision++;
            Volatile.Write(
                ref _snapshot,
                CreateSnapshot(exception.Message));
            _initialized.TrySetException(exception);
            FailPendingCommands(exception);
        }
        finally
        {
            StopAutomaticTimer();
            CompleteSubscribers();
        }
    }

    private async Task<bool> ProcessNextWorkItemOrBlockAsync()
    {
        if (_commands.Reader.TryRead(out WorkItem? pending))
        {
            return await ProcessWorkItemAsync(pending);
        }

        if (_automaticTiming && _state == SessionState.Running)
        {
            PeriodicTimer timer = _automaticTimer
                ?? throw new InvalidOperationException(
                    "Automatic timing is running without a block timer.");
            await timer.WaitForNextTickAsync(_lifetime.Token);
            if (_commands.Reader.TryRead(out pending))
            {
                return await ProcessWorkItemAsync(pending);
            }

            int blocksDue = _automaticBlockClock.GetDueBlockCount(
                _simulationBlock);
            if (blocksDue > 0)
            {
                await RenderBlocksAsync(blocksDue);
            }
            PublishSnapshot();
            return true;
        }

        try
        {
            WorkItem workItem = await _commands.Reader.ReadAsync(_lifetime.Token);
            return await ProcessWorkItemAsync(workItem);
        }
        catch (ChannelClosedException)
        {
            return false;
        }
    }

    private async Task<bool> ProcessWorkItemAsync(WorkItem workItem)
    {
        switch (workItem)
        {
            case CommandWorkItem command:
                await ApplyCommandAsync(command.Record);
                return true;
            case CloseWorkItem close:
                ApplyClose(close.Completion);
                return false;
            default:
                throw new InvalidOperationException(
                    $"Unknown work item '{workItem.GetType().Name}'.");
        }
    }

    private async Task ApplyCommandAsync(RequestRecord request)
    {
        SessionCommand command = request.Command;
        if (command.ExpectedRevision is long expected
            && expected != _revision)
        {
            Reject(
                request,
                DomainErrorCodes.StaleRevision,
                $"Expected revision {expected}, current revision is {_revision}.");
            return;
        }

        long appliedBlock = _simulationBlock;
        CommandResult result = command switch
        {
            StartSessionCommand => ApplyStart(),
            PauseSessionCommand => ApplyPause(),
            ResumeSessionCommand => ApplyResume(),
            StopSessionCommand => await ApplyStopAsync(),
            RecoverAudioCommand recover => await ApplyAudioRecoveryAsync(recover),
            AdvanceSimulationCommand advance => await ApplyAdvanceAsync(advance),
            SendOperatorIntentCommand intent => ApplyOperatorIntent(intent),
            TriggerEnterSendMessageCommand enter =>
                ApplyEnterSendMessage(enter),
            AdjustRadioControlCommand control => ApplyRadioControl(control),
            LogQsoCommand qso => ApplyLogQso(qso),
            ExpireControlLeaseCommand expired => ApplyControlExpired(expired),
            _ => RejectedResult(
                DomainErrorCodes.UnsupportedCapability,
                $"Command '{command.GetType().Name}' is unsupported."),
        };

        if (result.Accepted)
        {
            PublishEvent(
                SessionEventKind.CommandApplied,
                command.GetType().Name);
            PublishSnapshot();
            result = result with
            {
                AppliedRevision = result.AppliedRevision,
                AppliedBlock = appliedBlock,
            };
        }
        else
        {
            PublishEvent(
                SessionEventKind.CommandRejected,
                result.ErrorCode);
        }

        request.Completion.TrySetResult(result);
    }

    private CommandResult ApplyStart()
    {
        if (_state != SessionState.Ready)
        {
            return InvalidState("start");
        }

        _state = SessionState.Running;
        StartAutomaticTimer();
        _toneRenderer.LoadMessage("CQ TEST");
        _revision++;
        PublishEvent(SessionEventKind.Started, null);
        return AcceptedResult();
    }

    private CommandResult ApplyPause()
    {
        if (_state != SessionState.Running)
        {
            return InvalidState("pause");
        }

        _state = SessionState.Paused;
        StopAutomaticTimer();
        _revision++;
        PublishEvent(SessionEventKind.Paused, null);
        return AcceptedResult();
    }

    private CommandResult ApplyResume()
    {
        if (_state != SessionState.Paused)
        {
            return InvalidState("resume");
        }

        _state = SessionState.Running;
        StartAutomaticTimer();
        _revision++;
        PublishEvent(SessionEventKind.Resumed, null);
        return AcceptedResult();
    }

    private async Task<CommandResult> ApplyStopAsync()
    {
        if (_state is not (SessionState.Running or SessionState.Paused))
        {
            return InvalidState("stop");
        }

        _state = SessionState.Stopping;
        StopAutomaticTimer();
        _revision++;
        PublishEvent(SessionEventKind.Stopping, null);
        await _audioSink.CompleteAsync(_lifetime.Token);
        _state = SessionState.Completed;
        _revision++;
        PublishEvent(SessionEventKind.Completed, null);
        return AcceptedResult();
    }

    private async Task<CommandResult> ApplyAdvanceAsync(
        AdvanceSimulationCommand command)
    {
        if (_state != SessionState.Running)
        {
            return InvalidState("advance simulation");
        }

        if (command.BlockCount <= 0)
        {
            return RejectedResult(
                DomainErrorCodes.InvalidSetting,
                "Block count must be positive.");
        }

        long appliedRevision = ++_revision;
        await RenderBlocksAsync(command.BlockCount);

        return new(
            Accepted: true,
            ErrorCode: null,
            Message: null,
            AppliedRevision: appliedRevision,
            AppliedBlock: _simulationBlock);
    }

    private async Task RenderBlocksAsync(int blockCount)
    {
        for (int index = 0; index < blockCount; index++)
        {
            if (_audioSink is IAudioSinkMetricsSource metricsSource
                && !metricsSource.GetMetrics().IsHealthy)
            {
                _state = SessionState.Paused;
                _revision++;
                PublishEvent(
                    SessionEventKind.AudioDeviceFailed,
                    DomainErrorCodes.AudioDeviceUnavailable);
                break;
            }

            bool operatorIsSending = _toneRenderer.HasPendingAudio;
            _toneRenderer.Render(_operatorBuffer);
            PrepareReceiverInput();
            for (int stationIndex = 0;
                 stationIndex < _stations.Count;
                 stationIndex++)
            {
                SimulatedStation station = _stations[stationIndex];
                StationState priorStationState = station.State;
                station.AdvanceBlock(
                    _receiverReal,
                    _receiverImaginary,
                    mixOutput: _settings.Qsk || !operatorIsSending);
                if (priorStationState != StationState.Sending
                    && station.State == StationState.Sending)
                {
                    PublishEvent(
                        SessionEventKind.StationReplyStarted,
                        station.Identity.Callsign
                        + "|"
                        + station.LastReplyText);
                }
                else if (priorStationState == StationState.Sending
                    && station.State == StationState.Listening)
                {
                    PublishEvent(
                        SessionEventKind.StationReplyCompleted,
                        station.Identity.Callsign);
                }
            }
            _receiverPipeline.Process(
                _receiverReal,
                _receiverImaginary,
                _renderBuffer);
            ApplyAudioEffects(operatorIsSending);
            await _audioSink.WriteAsync(
                _renderBuffer,
                _simulationBlock,
                _lifetime.Token);
            _simulationBlock++;
            _renderedSamples += CompatibilityProfile.BlockSize;
            _revision++;

            RemoveFailedStations();
            AddCallersAtCurrentBlock();

            if (_settings.DurationBlocks > 0
                && _simulationBlock >= _settings.DurationBlocks)
            {
                _state = SessionState.Completed;
                StopAutomaticTimer();
                PublishEvent(SessionEventKind.Completed, null);
                await _audioSink.CompleteAsync(_lifetime.Token);
                break;
            }
        }
    }

    private void PrepareReceiverInput()
    {
        const double noiseAmplitude = 18_000d;
        for (int index = 0; index < _receiverReal.Length; index++)
        {
            _receiverReal[index] = (float)(
                noiseAmplitude * (_effectRandom.NextDouble() - 0.5d));
            _receiverImaginary[index] = (float)(
                noiseAmplitude * (_effectRandom.NextDouble() - 0.5d));
        }
    }

    private void ApplyAudioEffects(bool operatorIsSending)
    {
        _qsbProcessor?.Apply(_renderBuffer);
        float qrmIncrement =
            2F * MathF.PI * (_settings.PitchHz + 170F)
            / CompatibilityProfile.SampleRate;
        float flutterIncrement =
            2F * MathF.PI * 11F / CompatibilityProfile.SampleRate;
        for (int index = 0; index < _renderBuffer.Length; index++)
        {
            float sample = _renderBuffer[index];
            if (_settings.Qrm)
            {
                sample += 0.06F * MathF.Sin(_qrmPhase);
                _qrmPhase += qrmIncrement;
                if (_qrmPhase >= 2F * MathF.PI)
                {
                    _qrmPhase -= 2F * MathF.PI;
                }
            }

            if (_settings.Qrn)
            {
                sample += (_effectRandom.NextSingle() * 2F - 1F) * 0.035F;
            }

            if (_settings.Flutter)
            {
                sample *= 0.86F + (0.14F * MathF.Sin(_flutterPhase));
                _flutterPhase += flutterIncrement;
                if (_flutterPhase >= 2F * MathF.PI)
                {
                    _flutterPhase -= 2F * MathF.PI;
                }
            }

            float localMonitor =
                _operatorBuffer[index] * _monitorGain;
            _renderBuffer[index] = Math.Clamp(
                operatorIsSending
                    ? (_settings.Qsk
                        ? sample + localMonitor
                        : localMonitor)
                    : sample,
                -1f,
                1f);
        }
    }

    private async Task<CommandResult> ApplyAudioRecoveryAsync(
        RecoverAudioCommand command)
    {
        if (_state is not (SessionState.Ready or SessionState.Paused))
        {
            return InvalidState("recover audio");
        }

        if (_audioSink is not IRecoverableAudioSink recoverable)
        {
            return RejectedResult(
                DomainErrorCodes.UnsupportedCapability,
                "The configured audio sink does not support recovery.");
        }

        try
        {
            await recoverable.RecoverAsync(
                command.DeviceName,
                _lifetime.Token);
        }
        catch (Exception exception)
        {
            return RejectedResult(
                DomainErrorCodes.AudioDeviceUnavailable,
                exception.Message);
        }

        _revision++;
        PublishEvent(SessionEventKind.AudioDeviceRecovered, command.DeviceName);
        return AcceptedResult();
    }

    private CommandResult ApplyOperatorIntent(
        SendOperatorIntentCommand command)
    {
        if (_state != SessionState.Running)
        {
            return InvalidState("send an operator message");
        }

        string message = command.Intent switch
        {
            OperatorIntent.Cq => "CQ TEST",
            OperatorIntent.Exchange => JoinMessage(
                command.Rst,
                command.Exchange1,
                command.Exchange2),
            OperatorIntent.ThankYou => "TU",
            OperatorIntent.MyCall => _settings.StationCall,
            OperatorIntent.HisCall => command.Call,
            OperatorIntent.Before => "QSO B4",
            OperatorIntent.Question => "?",
            OperatorIntent.Nil => "NIL",
            OperatorIntent.NumberQuestion => "NR?",
            OperatorIntent.Abort => string.Empty,
            _ => throw new InvalidOperationException(
                $"Unknown operator intent '{command.Intent}'."),
        };
        _toneRenderer.LoadMessage(message);
        _lastOperatorMessage = message;
        ApplyIntentToStations(command);
        if (command.Intent == OperatorIntent.HisCall)
        {
            _esmSentCall = command.Call;
        }
        else if (command.Intent == OperatorIntent.Exchange)
        {
            _esmExchangeSent = true;
        }

        _revision++;
        return AcceptedResult();
    }

    private CommandResult ApplyEnterSendMessage(
        TriggerEnterSendMessageCommand command)
    {
        if (_state != SessionState.Running)
        {
            return InvalidState("trigger Enter Sends Message");
        }

        QsoEntrySnapshot entry = command.Entry;
        if (entry.Call.Length == 0)
        {
            _esmSentCall = null;
            _esmExchangeSent = false;
            SendEsmMessages(
                command,
                [(OperatorIntent.Cq, "CQ TEST")]);
            _revision++;
            return AcceptedResult(
                CreateEnterResult(
                    EnterSendMessageOutcome.SendCq,
                    ["CQ TEST"],
                    EntryFocusTarget.Call));
        }

        bool callWasSent = StringComparer.Ordinal.Equals(
            _esmSentCall,
            entry.Call);
        bool exchangeWasSent = _esmExchangeSent;
        bool validCall = !entry.Call.Contains('?')
            && CqWpxContestRules.NormalizeCall(entry.Call).Length >= 3;
        ReceivedEntryState received =
            ContestQsoRules.GetReceivedEntryState(
                _settings.ContestId,
                entry);
        List<(OperatorIntent Intent, string Message)> messages = [];

        if (!callWasSent
            || (!exchangeWasSent && !received.SecondPresent))
        {
            messages.Add((OperatorIntent.HisCall, entry.Call));
        }

        if (!exchangeWasSent && validCall)
        {
            messages.Add(
                (
                    OperatorIntent.Exchange,
                    ContestQsoRules.ComposeOwnExchange(
                        _settings.ContestId,
                        _settings.StationCall,
                        _qsoCount + 1)
                ));
        }

        if (exchangeWasSent && !received.IsComplete)
        {
            messages.Add((OperatorIntent.Question, "?"));
        }

        if (received.IsComplete && (callWasSent || exchangeWasSent))
        {
            LogQsoCommand logCommand = new(
                command.RequestId,
                command.SessionId,
                command.ClientId,
                entry.Call,
                entry.Rst,
                entry.Exchange1,
                entry.Exchange2,
                command.ExpectedRevision);
            ContestQsoEvaluation evaluation = ContestQsoRules.EvaluateReceived(
                _settings.ContestId,
                _settings.StationCall,
                logCommand);
            if (!validCall || !evaluation.Validation.IsValid)
            {
                string error = validCall
                    ? evaluation.Validation.Error
                    : "Invalid callsign";
                return RejectedResult(
                    DomainErrorCodes.InvalidSetting,
                    error,
                    CreateEnterResult(
                        EnterSendMessageOutcome.RejectEntry,
                        [],
                        FocusForValidationError(error, received),
                        selectQuestionMark:
                            entry.Call.Contains('?')));
            }

            messages.Add((OperatorIntent.ThankYou, "TU"));
            SendEsmMessages(command, messages);
            CommandResult logged = ApplyLogQsoCore(logCommand, evaluation);
            return logged with
            {
                EnterSendMessage = CreateEnterResult(
                    EnterSendMessageOutcome.CompleteAndLogQso,
                    messages.Select(item => item.Message).ToArray(),
                    EntryFocusTarget.Call,
                    clearEntry: true),
            };
        }

        SendEsmMessages(command, messages);
        _revision++;
        EnterSendMessageOutcome outcome = messages.Any(
            item => item.Intent == OperatorIntent.Question)
                ? EnterSendMessageOutcome.RequestExchangeRepeat
                : messages.Any(
                    item => item.Intent == OperatorIntent.Exchange)
                    ? EnterSendMessageOutcome.SendCallAndExchange
                    : EnterSendMessageOutcome.SendEnteredCall;
        EntryFocusTarget focusTarget = entry.Call.Contains('?')
            ? EntryFocusTarget.Call
            : _settings.ContestId.Value == "scWpx"
                ? EntryFocusTarget.Exchange1
                : validCall
                    ? received.MissingFocusTarget
                    : EntryFocusTarget.Call;
        return AcceptedResult(
            CreateEnterResult(
                outcome,
                messages.Select(item => item.Message).ToArray(),
                focusTarget,
                selectQuestionMark:
                    entry.Call.Contains('?')));
    }

    private void SendEsmMessages(
        TriggerEnterSendMessageCommand command,
        List<(OperatorIntent Intent, string Message)> messages)
    {
        if (messages.Count == 0)
        {
            return;
        }

        string combined = String.Join(
            ' ',
            messages.Select(item => item.Message));
        _toneRenderer.LoadMessage(combined);
        _lastOperatorMessage = combined;
        foreach ((OperatorIntent intent, _) in messages)
        {
            var intentCommand = new SendOperatorIntentCommand(
                command.RequestId,
                command.SessionId,
                command.ClientId,
                intent,
                command.Entry.Call,
                command.Entry.Rst,
                command.Entry.Exchange1,
                command.Entry.Exchange2,
                command.ExpectedRevision);
            ApplyIntentToStations(intentCommand);
            if (intent == OperatorIntent.HisCall)
            {
                _esmSentCall = command.Entry.Call;
            }
            else if (intent == OperatorIntent.Exchange)
            {
                _esmExchangeSent = true;
            }
        }
    }

    private static EntryFocusTarget FocusForValidationError(
        string error,
        ReceivedEntryState received)
    {
        if (error.Contains("callsign", StringComparison.OrdinalIgnoreCase))
        {
            return EntryFocusTarget.Call;
        }

        if (error.Contains("RST", StringComparison.OrdinalIgnoreCase))
        {
            return EntryFocusTarget.Rst;
        }

        return received.MissingFocusTarget;
    }

    private static EnterSendMessageResult CreateEnterResult(
        EnterSendMessageOutcome outcome,
        IReadOnlyList<string> sentMessages,
        EntryFocusTarget focusTarget,
        bool selectQuestionMark = false,
        bool clearEntry = false) =>
        new(
            outcome,
            sentMessages,
            focusTarget,
            selectQuestionMark,
            clearEntry);

    private void ApplyIntentToStations(SendOperatorIntentCommand command)
    {
        if (_stations.Count == 0)
        {
            return;
        }

        StationMessage stationMessage = command.Intent switch
        {
            OperatorIntent.Cq => StationMessage.Cq,
            OperatorIntent.Exchange => StationMessage.Number,
            OperatorIntent.ThankYou => StationMessage.ThankYou,
            OperatorIntent.MyCall => StationMessage.MyCall,
            OperatorIntent.HisCall => StationMessage.HisCall,
            OperatorIntent.Before => StationMessage.Before,
            OperatorIntent.Question => StationMessage.Question,
            OperatorIntent.Nil => StationMessage.Nil,
            OperatorIntent.NumberQuestion => StationMessage.Question,
            OperatorIntent.Abort => StationMessage.None,
            _ => throw new InvalidOperationException(
                $"Unknown operator intent '{command.Intent}'."),
        };
        if (stationMessage != StationMessage.None)
        {
            int bestConfidence = 1;
            if (stationMessage == StationMessage.HisCall)
            {
                foreach (SimulatedStation station in _stations)
                {
                    station.Operator.MatchCall(command.Call);
                    bestConfidence = Math.Max(
                        bestConfidence,
                        station.Operator.CallConfidence);
                }
            }

            foreach (SimulatedStation station in _stations.ToArray())
            {
                station.ReceiveOperatorStarted();
                station.ReceiveOperatorFinished(
                    stationMessage,
                    command.Call,
                    bestConfidence,
                    allowLidErrors: true);
            }
        }
    }

    private void AddCallersAtCurrentBlock()
    {
        OperatorRunMode runMode = ToOperatorRunMode(_settings.RunModeId);
        int activeCount = _stations.Count(
            station => !station.IsComplete);
        int maximumCallers = runMode switch
        {
            OperatorRunMode.Stop => 0,
            OperatorRunMode.SingleCall => 1,
            _ => Math.Max(1, _settings.Activity),
        };
        if (activeCount >= maximumCallers)
        {
            return;
        }

        int callerInterval = Math.Max(2, 13 - _settings.Activity);
        int callersToAdd = runMode == OperatorRunMode.Hst
            ? maximumCallers - activeCount
            : _simulationBlock % callerInterval == 0
                ? 1
                : 0;
        for (int index = 0; index < callersToAdd; index++)
        {
            AddCaller(runMode);
        }
    }

    private void AddCaller(OperatorRunMode runMode)
    {
        StationIdentity? identity = null;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            int serialNumber = CreateStationSerialNumber();
            StationIdentity candidate = _stationCatalog.Pick(
                _random,
                _settings.ContestId,
                serialNumber);
            if (_stations.All(
                    station => !station.Identity.Callsign.Equals(
                        candidate.Callsign,
                        StringComparison.Ordinal)))
            {
                identity = candidate;
                break;
            }
        }

        if (identity is null)
        {
            return;
        }

        int wordsPerMinute = CreateStationWordsPerMinute(runMode);
        int pitchRange = runMode == OperatorRunMode.SingleCall ? 50 : 300;
        int pitchOffset = _random.Next((pitchRange * 2) + 1) - pitchRange;
        SimulatedStation station = SimulatedStation.CreateReadyCaller(
            identity,
            wordsPerMinute,
            pitchOffset,
            _random,
            runMode,
            _settings.Lids,
            sweepstakes: _settings.ContestId.Value == "scArrlSS");
        _stations.Add(station);
        _hasCreatedStation = true;
        _lastCaller = identity.Callsign;
        PublishEvent(SessionEventKind.CallerJoined, identity.Callsign);
    }

    private int CreateStationWordsPerMinute(OperatorRunMode runMode)
    {
        if (runMode == OperatorRunMode.Hst)
        {
            return _settings.WordsPerMinute;
        }

        if (_settings.ReceiveSpeedBelowWpm == -1
            && _settings.ReceiveSpeedAboveWpm == -1)
        {
            return Math.Max(
                10,
                (int)Math.Round(
                    _settings.WordsPerMinute
                    * 0.5d
                    * (1d + _random.NextDouble()),
                    MidpointRounding.ToEven));
        }

        int minimum = Math.Max(
            10,
            _settings.WordsPerMinute - _settings.ReceiveSpeedBelowWpm);
        int maximum =
            _settings.WordsPerMinute + _settings.ReceiveSpeedAboveWpm;
        if (minimum >= maximum)
        {
            return minimum;
        }

        return (int)Math.Round(
            minimum + ((maximum - minimum) * _random.NextDouble()),
            MidpointRounding.ToEven);
    }

    private int CreateStationSerialNumber()
    {
        int serialNumber = _settings.SerialNumberRange switch
        {
            SerialNumberRangeMode.StartOfContest => _nextStationSerial,
            SerialNumberRangeMode.MidContest =>
                CreateDistributedSerialNumber(firstBin: 5, lastBin: 13),
            SerialNumberRangeMode.EndOfContest =>
                CreateDistributedSerialNumber(firstBin: 14, lastBin: 58),
            SerialNumberRangeMode.Custom =>
                _settings.CustomSerialNumberMinimum
                + _random.Next(
                    _settings.CustomSerialNumberExclusiveMaximum
                    - _settings.CustomSerialNumberMinimum),
            _ => throw new InvalidOperationException(
                $"Unknown serial-number range '{_settings.SerialNumberRange}'."),
        };
        _nextStationSerial++;
        return serialNumber;
    }

    private int CreateDistributedSerialNumber(int firstBin, int lastBin)
    {
        int total = 0;
        for (int index = firstBin; index <= lastBin; index++)
        {
            total += SerialNumberBins[index].Count;
        }

        int selectedCount = _random.Next(total) + 1;
        int cumulative = 0;
        for (int index = firstBin; index <= lastBin; index++)
        {
            cumulative += SerialNumberBins[index].Count;
            if (selectedCount <= cumulative)
            {
                int begin = SerialNumberBins[index].Begin;
                int width = SerialNumberBins[index + 1].Begin - begin;
                return begin + _random.Next(width);
            }
        }

        throw new InvalidOperationException(
            "The serial-number distribution did not select a bin.");
    }

    private void RemoveFailedStations()
    {
        for (int index = _stations.Count - 1; index >= 0; index--)
        {
            if (_stations[index].Operator.State != OperatorState.Failed)
            {
                continue;
            }

            SimulatedStation station = _stations[index];
            _stations.RemoveAt(index);
            PublishEvent(
                SessionEventKind.CallerLeft,
                station.Identity.Callsign + "|failed");
        }
    }

    private static OperatorRunMode ToOperatorRunMode(RunModeId runModeId)
    {
        return runModeId.Value switch
        {
            "rmStop" => OperatorRunMode.Stop,
            "rmPileup" => OperatorRunMode.Pileup,
            "rmSingle" => OperatorRunMode.SingleCall,
            "rmWpx" => OperatorRunMode.Wpx,
            "rmHst" => OperatorRunMode.Hst,
            _ => throw new InvalidOperationException(
                $"Unknown run mode '{runModeId.Value}'."),
        };
    }

    private CommandResult ApplyRadioControl(
        AdjustRadioControlCommand command)
    {
        if (_state is not (SessionState.Running or SessionState.Paused))
        {
            return InvalidState("adjust a radio control");
        }

        switch (command.Control)
        {
            case RadioControl.Rit:
                _ritOffsetHz = Math.Clamp(
                    _ritOffsetHz + command.Delta,
                    -2_000,
                    2_000);
                break;
            case RadioControl.Bandwidth:
                _currentBandwidthHz = Math.Clamp(
                    _currentBandwidthHz + command.Delta,
                    100,
                    1_000);
                break;
            case RadioControl.Speed:
                _currentWordsPerMinute = Math.Clamp(
                    _currentWordsPerMinute + command.Delta,
                    10,
                    100);
                _toneRenderer.SetWordsPerMinute(_currentWordsPerMinute);
                break;
            default:
                return RejectedResult(
                    DomainErrorCodes.InvalidSetting,
                    $"Unknown radio control '{command.Control}'.");
        }

        _revision++;
        return AcceptedResult();
    }

    private CommandResult ApplyLogQso(LogQsoCommand command)
    {
        if (_state != SessionState.Running)
        {
            return InvalidState("log a QSO");
        }

        ContestQsoEvaluation evaluation = ContestQsoRules.EvaluateReceived(
            _settings.ContestId,
            _settings.StationCall,
            command);
        if (!evaluation.Validation.IsValid)
        {
            return RejectedResult(
                DomainErrorCodes.InvalidSetting,
                evaluation.Validation.Error);
        }

        return ApplyLogQsoCore(command, evaluation);
    }

    private CommandResult ApplyLogQsoCore(
        LogQsoCommand command,
        ContestQsoEvaluation evaluation)
    {
        SimulatedStation? completedStation = _stations
            .Where(
                station => station.Operator.State == OperatorState.Done)
            .OrderByDescending(
                station =>
                {
                    station.Operator.MatchCall(evaluation.Call);
                    return station.Operator.CallConfidence;
                })
            .FirstOrDefault();
        LogError exchangeError = DetermineLogError(
            evaluation,
            command,
            completedStation);
        bool duplicate = !_workedCalls.Add(evaluation.Call);
        if (duplicate && exchangeError == LogError.None)
        {
            exchangeError = LogError.Duplicate;
        }

        if (!duplicate && exchangeError == LogError.None)
        {
            _verifiedPoints += evaluation.Points;
            if (evaluation.UsesAdditiveScore)
            {
                _score = _verifiedPoints;
            }
            else
            {
                foreach (string multiplier in
                         evaluation.Multiplier.Split(';'))
                {
                    _verifiedMultipliers.Add(multiplier);
                }

                _score = _verifiedPoints * _verifiedMultipliers.Count;
            }
        }

        _qsoCount++;
        _lastLoggedCall = evaluation.Call;
        _lastCaller = _lastLoggedCall;
        StationIdentity? truth = completedStation?.Identity;
        bool directLogScenario = !_hasCreatedStation;
        Qso qso = new()
        {
            Timestamp = DateTimeOffset.UnixEpoch
                + TimeSpan.FromSeconds(
                    (double)_renderedSamples / CompatibilityProfile.SampleRate),
            Call = _lastLoggedCall,
            TrueCall = truth?.Callsign
                ?? (directLogScenario ? evaluation.Call : string.Empty),
            RawCallsign = command.Call,
            Rst = evaluation.Rst,
            TrueRst = truth is null
                ? (directLogScenario ? evaluation.Rst : 0)
                : ParseRst(truth.Rst),
            Number = evaluation.Number,
            TrueNumber = truth?.Number
                ?? (directLogScenario ? evaluation.Number : 0),
            Precedence = evaluation.Precedence,
            TruePrecedence = truth?.Precedence
                ?? (directLogScenario ? evaluation.Precedence : string.Empty),
            Check = evaluation.Check,
            TrueCheck = truth?.Check
                ?? (directLogScenario ? evaluation.Check : 0),
            Section = evaluation.Section,
            TrueSection = truth?.Section
                ?? (directLogScenario ? evaluation.Section : string.Empty),
            Exchange1 = command.Exchange1,
            TrueExchange1 = truth?.Exchange1
                ?? (directLogScenario ? command.Exchange1 : string.Empty),
            Exchange2 = command.Exchange2,
            TrueExchange2 = truth?.Exchange2
                ?? (directLogScenario ? command.Exchange2 : string.Empty),
            Prefix = evaluation.Prefix,
            Multiplier = evaluation.Multiplier,
            Points = exchangeError is LogError.None or LogError.Duplicate
                ? evaluation.Points
                : 0,
            IsDuplicate = duplicate,
            ExchangeError = exchangeError,
            ErrorText = ErrorText(exchangeError),
        };
        Qso[] current = _completedQsos;
        Qso[] next = new Qso[current.Length + 1];
        Array.Copy(current, next, current.Length);
        next[^1] = qso;
        Volatile.Write(ref _completedQsos, next);
        if (completedStation is not null)
        {
            _stations.Remove(completedStation);
        }
        _esmSentCall = null;
        _esmExchangeSent = false;
        _revision++;
        PublishEvent(
            SessionEventKind.QsoLogged,
            qso.Call + "|" + qso.ErrorText.Trim());
        return AcceptedResult();
    }

    private LogError DetermineLogError(
        ContestQsoEvaluation evaluation,
        LogQsoCommand command,
        SimulatedStation? station)
    {
        if (station is null)
        {
            return _hasCreatedStation ? LogError.Nil : LogError.None;
        }

        StationIdentity truth = station.Identity;
        if (!evaluation.Call.Equals(
                truth.Callsign,
                StringComparison.Ordinal))
        {
            return LogError.Call;
        }

        if (evaluation.Rst != ParseRst(truth.Rst))
        {
            return LogError.Rst;
        }

        return _settings.ContestId.Value switch
        {
            "scWpx" or "scHst"
                when evaluation.Number != truth.Number => LogError.Number,
            "scCwt" or "scSst"
                when !Equivalent(command.Exchange1, truth.Exchange1) =>
                    LogError.Name,
            "scCwt" or "scSst"
                when !Equivalent(command.Exchange2, truth.Exchange2) =>
                    LogError.Number,
            "scFieldDay"
                when !Equivalent(command.Exchange1, truth.Exchange1) =>
                    LogError.Class,
            "scFieldDay" or "scArrlSS"
                when !Equivalent(command.Exchange2, truth.Exchange2) =>
                    LogError.Section,
            "scCQWW"
                when !Equivalent(command.Exchange2, truth.Exchange2) =>
                    LogError.Zone,
            "scArrlDx"
                when !Equivalent(command.Exchange2, truth.Exchange2) =>
                    LogError.Power,
            "scNaQp"
                when !Equivalent(command.Exchange1, truth.Exchange1) =>
                    LogError.Name,
            "scNaQp"
                when !Equivalent(command.Exchange2, truth.Exchange2) =>
                    LogError.State,
            "scAllJa" or "scAcag" or "scIaruHf"
                when !Equivalent(command.Exchange2, truth.Exchange2) =>
                    LogError.Error,
            _ => LogError.None,
        };
    }

    private static bool Equivalent(string left, string right)
    {
        string normalizedLeft = NormalizeExchange(left);
        string normalizedRight = NormalizeExchange(right);
        return normalizedLeft.Equals(
            normalizedRight,
            StringComparison.Ordinal);
    }

    private static string NormalizeExchange(string value)
    {
        string normalized = value
            .Trim()
            .ToUpperInvariant()
            .Replace('N', '9')
            .Replace('T', '0')
            .Replace('O', '0');
        return int.TryParse(normalized, out int number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : normalized;
    }

    private static int ParseRst(string? value)
    {
        string normalized = NormalizeExchange(value ?? string.Empty);
        return int.TryParse(normalized, out int rst) ? rst : 0;
    }

    private static string ErrorText(LogError error) =>
        error switch
        {
            LogError.None => "   ",
            LogError.Nil => "NIL",
            LogError.Duplicate => "DUP",
            LogError.Call => "CALL",
            LogError.Rst => "RST",
            LogError.Number => "NR",
            LogError.Name => "NAME",
            LogError.Class => "CLASS",
            LogError.Section => "SECT",
            LogError.Zone => "ZONE",
            LogError.State => "STATE",
            LogError.Power => "PWR",
            _ => "ERROR",
        };

    private CommandResult ApplyControlExpired(
        ExpireControlLeaseCommand command)
    {
        if (_state == SessionState.Running)
        {
            _state = SessionState.Paused;
        }

        _revision++;
        PublishEvent(SessionEventKind.ControlExpired, command.ClientId.Value);
        return AcceptedResult();
    }

    private static string JoinMessage(params string[] parts) =>
        string.Join(
            ' ',
            parts.Where(part => !string.IsNullOrWhiteSpace(part)));

    private void ApplyClose(TaskCompletionSource<bool> completion)
    {
        StopAutomaticTimer();
        _state = SessionState.Closed;
        _revision++;
        PublishEvent(SessionEventKind.Closed, null);
        PublishSnapshot();
        completion.TrySetResult(true);
    }

    private void StartAutomaticTimer()
    {
        if (!_automaticTiming)
        {
            return;
        }

        StopAutomaticTimer();
        _automaticTimer = new PeriodicTimer(_blockPeriod);
        _automaticBlockClock.Start(_simulationBlock);
    }

    private void StopAutomaticTimer()
    {
        _automaticTimer?.Dispose();
        _automaticTimer = null;
    }

    private void Reject(
        RequestRecord request,
        string errorCode,
        string message)
    {
        PublishEvent(SessionEventKind.CommandRejected, errorCode);
        request.Completion.TrySetResult(RejectedResult(errorCode, message));
    }

    private CommandResult InvalidState(string action)
    {
        return RejectedResult(
            DomainErrorCodes.InvalidSessionState,
            $"Cannot {action} while the session is {_state}.");
    }

    private CommandResult AcceptedResult(
        EnterSendMessageResult? enterSendMessage = null)
    {
        return new(
            Accepted: true,
            ErrorCode: null,
            Message: null,
            AppliedRevision: _revision,
            AppliedBlock: _simulationBlock,
            enterSendMessage);
    }

    private CommandResult RejectedResult(
        string errorCode,
        string message,
        EnterSendMessageResult? enterSendMessage = null)
    {
        return new(
            Accepted: false,
            errorCode,
            message,
            _revision,
            _simulationBlock,
            enterSendMessage);
    }

    private void PublishEvent(SessionEventKind kind, string? detail)
    {
        SessionEvent sessionEvent = new(
            _engineEpoch,
            _sessionId,
            ++_eventSequence,
            _revision,
            _simulationBlock,
            kind,
            detail);
        lock (_subscriberGate)
        {
            _eventHistory.Enqueue(sessionEvent);
            while (_eventHistory.Count > EventHistoryCapacity)
            {
                _eventHistory.Dequeue();
            }
        }

        Publish(SessionUpdate.FromEvent(sessionEvent));
    }

    private void PublishSnapshot()
    {
        SessionSnapshot snapshot = CreateSnapshot();
        Volatile.Write(ref _snapshot, snapshot);
        Publish(SessionUpdate.FromSnapshot(snapshot));
    }

    private void Publish(SessionUpdate update)
    {
        lock (_subscriberGate)
        {
            for (int index = _subscribers.Count - 1; index >= 0; index--)
            {
                Subscriber subscriber = _subscribers[index];
                if (!subscriber.Channel.Writer.TryWrite(update))
                {
                    subscriber.MarkResyncRequired();
                    subscriber.Channel.Writer.TryComplete();
                    _subscribers.RemoveAt(index);
                }
            }
        }
    }

    private SessionUpdate CreateResyncUpdate() =>
        SessionUpdate.FromEvent(
            new(
                _engineEpoch,
                _sessionId,
                _eventSequence,
                _revision,
                _simulationBlock,
                SessionEventKind.ResyncRequired,
                DomainErrorCodes.ResyncRequired));

    private SessionSnapshot CreateSnapshot(string? error = null)
    {
        AudioSinkMetrics metrics = _audioSink is IAudioSinkMetricsSource source
            ? source.GetMetrics()
            : new(
                QueuedBlocks: 0,
                UnderrunCount: 0,
                DroppedBlockCount: 0,
                IsHealthy: true);
        TimeSpan elapsed = TimeSpan.FromSeconds(
            (double)_renderedSamples / CompatibilityProfile.SampleRate);
        ActiveStationSnapshot[] activeStations = _stations
            .Select(station => station.CreateSnapshot())
            .ToArray();
        OperatorState? activeOperatorState = _stations
            .LastOrDefault(
                station => station.Identity.Callsign.Equals(
                    _lastCaller,
                    StringComparison.Ordinal))
            ?.Operator.State
            ?? _stations.LastOrDefault()?.Operator.State;
        return new(
            _engineEpoch,
            _sessionId,
            _state,
            _revision,
            _simulationBlock,
            _renderedSamples,
            elapsed,
            _settings.Seed,
            _settings.ContestId,
            _settings.RunModeId,
            _lastCaller,
            _qsoCount,
            _score,
            error,
            metrics.QueuedBlocks,
            metrics.UnderrunCount,
            metrics.DroppedBlockCount,
            metrics.IsHealthy,
            _lastOperatorMessage,
            _currentWordsPerMinute,
            _currentBandwidthHz,
            _ritOffsetHz,
            _lastLoggedCall,
            activeOperatorState,
            QsoRateCalculator.Calculate(_completedQsos, elapsed),
            activeStations);
    }

    private void FailPendingCommands(Exception exception)
    {
        lock (_requests)
        {
            foreach (RequestRecord request in _requests.Values)
            {
                request.Completion.TrySetException(exception);
            }
        }
    }

    private void CompleteSubscribers()
    {
        lock (_subscriberGate)
        {
            foreach (Subscriber subscriber in _subscribers)
            {
                subscriber.Channel.Writer.TryComplete();
            }

            _subscribers.Clear();
        }
    }

    private abstract record WorkItem;

    private sealed record CommandWorkItem(RequestRecord Record) : WorkItem;

    private sealed record CloseWorkItem(
        TaskCompletionSource<bool> Completion) : WorkItem;

    private sealed record RequestRecord(
        SessionCommand Command,
        TaskCompletionSource<CommandResult> Completion);

    private sealed class Subscriber(Channel<SessionUpdate> channel)
    {
        private int _resyncRequired;

        public Channel<SessionUpdate> Channel { get; } = channel;

        public bool ResyncRequired =>
            Volatile.Read(ref _resyncRequired) != 0;

        public void MarkResyncRequired()
        {
            Interlocked.Exchange(ref _resyncRequired, 1);
        }
    }
}
