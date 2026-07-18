using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MorseRunner.Domain;
using MorseRunner.Dsp;

namespace MorseRunner.Engine;

internal sealed class EngineSession : IAsyncDisposable
{
    private const int CommandCapacity = 256;
    private const int SubscriberCapacity = 64;
    private const int EventHistoryCapacity = 256;
    private readonly Guid _engineEpoch;
    private readonly SessionId _sessionId;
    private readonly SessionSettings _settings;
    private readonly IAudioSink _audioSink;
    private readonly SeededRandomSource _random;
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
    private readonly MorseToneRenderer _toneRenderer;
    private readonly LegacyRandom _effectRandom;
    private readonly QsbProcessor? _qsbProcessor;
    private readonly float _monitorGain;
    private readonly bool _automaticTiming;
    private readonly TimeSpan _blockPeriod;
    private readonly Task _worker;
    private SessionSnapshot _snapshot;
    private SessionState _state = SessionState.Created;
    private long _revision;
    private long _simulationBlock;
    private long _renderedSamples;
    private long _eventSequence;
    private string? _lastCaller;
    private string? _lastOperatorMessage;
    private int _currentWordsPerMinute;
    private int _currentBandwidthHz;
    private int _ritOffsetHz;
    private int _qsoCount;
    private int _score;
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
        _effectRandom = new(unchecked(settings.Seed ^ 0x51B5_4A32));
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
            await Task.Delay(_blockPeriod, _lifetime.Token);
            if (_commands.Reader.TryRead(out pending))
            {
                return await ProcessWorkItemAsync(pending);
            }

            await RenderBlocksAsync(1);
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

            _toneRenderer.Render(_renderBuffer);
            ApplyAudioEffects();
            await _audioSink.WriteAsync(
                _renderBuffer,
                _simulationBlock,
                _lifetime.Token);
            _simulationBlock++;
            _renderedSamples += CompatibilityProfile.BlockSize;
            _revision++;

            int callerInterval = Math.Max(2, 13 - _settings.Activity);
            if (_simulationBlock % callerInterval == 0)
            {
                _lastCaller = $"DX{_random.Next(1000):000}";
                if (_settings.Lids && _random.Next(4) == 0)
                {
                    _lastCaller += "?";
                }
                PublishEvent(SessionEventKind.CallerJoined, _lastCaller);
            }

            if (_settings.DurationBlocks > 0
                && _simulationBlock >= _settings.DurationBlocks)
            {
                _state = SessionState.Completed;
                PublishEvent(SessionEventKind.Completed, null);
                await _audioSink.CompleteAsync(_lifetime.Token);
                break;
            }
        }
    }

    private void ApplyAudioEffects()
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

            _renderBuffer[index] = Math.Clamp(
                sample * _monitorGain,
                -1F,
                1F);
        }
    }

    private async Task<CommandResult> ApplyAudioRecoveryAsync(
        RecoverAudioCommand command)
    {
        if (_state != SessionState.Paused)
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
        _revision++;
        return AcceptedResult();
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

        bool isCqWpx = _settings.ContestId == new ContestId("scWpx");
        string normalizedCall = CqWpxContestRules.NormalizeCall(command.Call);
        bool duplicate = false;
        string prefix = string.Empty;
        if (isCqWpx)
        {
            ContestValidation validation =
                CqWpxContestRules.ValidateReceivedQso(
                    command.Call,
                    command.Rst,
                    command.Exchange1);
            if (!validation.IsValid)
            {
                return RejectedResult(
                    DomainErrorCodes.InvalidSetting,
                    validation.Error);
            }

            duplicate = !_workedCalls.Add(normalizedCall);
            prefix = CallsignParser.ExtractPrefix(normalizedCall);
            if (!duplicate)
            {
                _verifiedPoints += CqWpxContestRules.PointsPerQso;
                _verifiedMultipliers.Add(prefix);
                _score = _verifiedPoints * _verifiedMultipliers.Count;
            }
        }
        else
        {
            if (normalizedCall.Length < 3)
            {
                return RejectedResult(
                    DomainErrorCodes.InvalidSetting,
                    "A callsign must contain at least three characters.");
            }

            _score++;
        }

        _qsoCount++;
        _lastLoggedCall = normalizedCall;
        _lastCaller = _lastLoggedCall;
        int rst = CqWpxContestRules.ParseRst(command.Rst);
        int serialNumber =
            CqWpxContestRules.ParseSerialNumber(command.Exchange1);
        Qso qso = new()
        {
            Timestamp = DateTimeOffset.UnixEpoch
                + TimeSpan.FromSeconds(
                    (double)_renderedSamples / CompatibilityProfile.SampleRate),
            Call = _lastLoggedCall,
            TrueCall = _lastLoggedCall,
            RawCallsign = command.Call,
            Rst = rst,
            TrueRst = rst,
            Number = serialNumber,
            TrueNumber = serialNumber,
            Exchange1 = command.Exchange1,
            TrueExchange1 = command.Exchange1,
            Exchange2 = command.Exchange2,
            TrueExchange2 = command.Exchange2,
            Prefix = prefix,
            Multiplier = prefix,
            Points = CqWpxContestRules.PointsPerQso,
            IsDuplicate = duplicate,
            ExchangeError = duplicate
                ? LogError.Duplicate
                : LogError.None,
            ErrorText = duplicate ? "DUP" : "   ",
        };
        Qso[] current = _completedQsos;
        Qso[] next = new Qso[current.Length + 1];
        Array.Copy(current, next, current.Length);
        next[^1] = qso;
        Volatile.Write(ref _completedQsos, next);
        _revision++;
        return AcceptedResult();
    }

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
        _state = SessionState.Closed;
        _revision++;
        PublishEvent(SessionEventKind.Closed, null);
        PublishSnapshot();
        completion.TrySetResult(true);
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

    private CommandResult AcceptedResult()
    {
        return new(
            Accepted: true,
            ErrorCode: null,
            Message: null,
            AppliedRevision: _revision,
            AppliedBlock: _simulationBlock);
    }

    private CommandResult RejectedResult(string errorCode, string message)
    {
        return new(
            Accepted: false,
            errorCode,
            message,
            _revision,
            _simulationBlock);
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
        return new(
            _engineEpoch,
            _sessionId,
            _state,
            _revision,
            _simulationBlock,
            _renderedSamples,
            TimeSpan.FromSeconds(
                (double)_renderedSamples / CompatibilityProfile.SampleRate),
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
            _lastLoggedCall);
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
