using System.Globalization;
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

            bool operatorIsSending = _toneRenderer.HasPendingAudio;
            _toneRenderer.Render(_renderBuffer);
            for (int stationIndex = 0;
                 stationIndex < _stations.Count;
                 stationIndex++)
            {
                SimulatedStation station = _stations[stationIndex];
                StationState priorStationState = station.State;
                station.AdvanceBlock(
                    _renderBuffer,
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
            ApplyAudioEffects();
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
        ApplyIntentToStations(command);
        _revision++;
        return AcceptedResult();
    }

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
            StationIdentity candidate = _stationCatalog.Pick(
                _random,
                _settings.ContestId,
                _nextStationSerial++);
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

        int wordsPerMinute = runMode == OperatorRunMode.Hst
            ? _settings.WordsPerMinute
            : Math.Max(
                10,
                (int)Math.Round(
                    _settings.WordsPerMinute
                    * 0.5d
                    * (1d + _random.NextDouble()),
                    MidpointRounding.ToEven));
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
