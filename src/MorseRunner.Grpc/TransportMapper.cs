using Google.Protobuf.WellKnownTypes;
using Contract = MorseRunner.Contracts.V1;
using Domain = MorseRunner.Domain;

namespace MorseRunner.Grpc;

public static class TransportMapper
{
    public static Contract.EngineInfoMessage ToTransport(
        Domain.EngineInfo value,
        bool isInProcess)
    {
        var message = new Contract.EngineInfoMessage
        {
            EngineId = value.EngineId.ToString(),
            DisplayName = value.DisplayName,
            SemanticVersion = value.SemanticVersion,
            EngineEpoch = value.EngineEpoch.ToString("D"),
            IsInProcess = isInProcess,
            MinimumContractVersion = value.MinimumContractVersion,
            MaximumContractVersion = value.MaximumContractVersion,
            DiagnosticVersion = value.DiagnosticVersion,
        };
        message.Capabilities.Add(value.Capabilities);
        return message;
    }

    public static Domain.EngineInfo ToDomain(Contract.EngineInfoMessage value) =>
        new(
            new Domain.EngineId(ParseGuid(value.EngineId, nameof(value.EngineId))),
            value.DisplayName,
            value.SemanticVersion,
            ParseGuid(value.EngineEpoch, nameof(value.EngineEpoch)),
            value.Capabilities.ToArray(),
            value.IsInProcess)
        {
            MinimumContractVersion = value.MinimumContractVersion,
            MaximumContractVersion = value.MaximumContractVersion,
            DiagnosticVersion = value.DiagnosticVersion,
        };

    public static Contract.ContestDefinitionMessage ToTransport(
        Domain.ContestDefinition value) =>
        new()
        {
            Id = value.Id.Value,
            Key = value.Key,
            DisplayName = value.DisplayName,
            ExchangeType1 = value.ExchangeType1,
            ExchangeType2 = value.ExchangeType2,
            ExchangeFieldEditable = value.ExchangeFieldEditable,
            ExchangeDefault = value.ExchangeDefault,
        };

    public static Contract.SessionSettingsMessage ToTransport(
        Domain.SessionSettings value) =>
        new()
        {
            Seed = value.Seed,
            ContestId = value.ContestId.Value,
            RunModeId = value.RunModeId.Value,
            DurationBlocks = value.DurationBlocks,
            StationCall = value.StationCall,
            WordsPerMinute = value.WordsPerMinute,
            PitchHz = value.PitchHz,
            BandwidthHz = value.BandwidthHz,
            Activity = value.Activity,
            StationIdRate = value.StationIdRate,
            Qsk = value.Qsk,
            Qsb = value.Qsb,
            Qrm = value.Qrm,
            Qrn = value.Qrn,
            Flutter = value.Flutter,
            Lids = value.Lids,
            MonitorLevelDb = value.MonitorLevelDb,
            ReceiveSpeedBelowWpm = value.ReceiveSpeedBelowWpm,
            ReceiveSpeedAboveWpm = value.ReceiveSpeedAboveWpm,
            SerialNumberRange = ToTransport(value.SerialNumberRange),
            CustomSerialNumberMinimum = value.CustomSerialNumberMinimum,
            CustomSerialNumberExclusiveMaximum =
                value.CustomSerialNumberExclusiveMaximum,
            CustomSerialNumberMinimumDigits =
                value.CustomSerialNumberMinimumDigits,
            CustomSerialNumberMaximumDigits =
                value.CustomSerialNumberMaximumDigits,
            HstOperatorName = value.HstOperatorName,
            AudioOutputDeviceName = value.AudioOutputDeviceName ?? string.Empty,
        };

    public static Domain.SessionSettings ToDomain(
        Contract.SessionSettingsMessage value) =>
        new(
            value.Seed,
            new Domain.ContestId(value.ContestId),
            new Domain.RunModeId(value.RunModeId),
            value.DurationBlocks)
        {
            StationCall = value.StationCall,
            WordsPerMinute = value.WordsPerMinute,
            PitchHz = value.PitchHz,
            BandwidthHz = value.BandwidthHz,
            Activity = value.HasActivity ? value.Activity : 5,
            StationIdRate = value.HasStationIdRate
                ? value.StationIdRate
                : 3,
            Qsk = value.Qsk,
            Qsb = value.Qsb,
            Qrm = value.Qrm,
            Qrn = value.Qrn,
            Flutter = value.Flutter,
            Lids = value.Lids,
            MonitorLevelDb = value.HasMonitorLevelDb
                ? value.MonitorLevelDb
                : -15d,
            ReceiveSpeedBelowWpm = value.HasReceiveSpeedBelowWpm
                ? value.ReceiveSpeedBelowWpm
                : -1,
            ReceiveSpeedAboveWpm = value.HasReceiveSpeedAboveWpm
                ? value.ReceiveSpeedAboveWpm
                : -1,
            SerialNumberRange = ToDomain(value.SerialNumberRange),
            CustomSerialNumberMinimum = value.HasCustomSerialNumberMinimum
                ? value.CustomSerialNumberMinimum
                : 1,
            CustomSerialNumberExclusiveMaximum =
                value.HasCustomSerialNumberExclusiveMaximum
                ? value.CustomSerialNumberExclusiveMaximum
                : 99,
            CustomSerialNumberMinimumDigits =
                value.HasCustomSerialNumberMinimumDigits
                ? value.CustomSerialNumberMinimumDigits
                : 2,
            CustomSerialNumberMaximumDigits =
                value.HasCustomSerialNumberMaximumDigits
                ? value.CustomSerialNumberMaximumDigits
                : 2,
            HstOperatorName = value.HstOperatorName,
            AudioOutputDeviceName = EmptyToNull(value.AudioOutputDeviceName),
        };

    public static Contract.SessionHandleMessage ToTransport(
        Domain.SessionHandle value) =>
        new()
        {
            SessionId = value.SessionId.ToString(),
            EngineEpoch = value.EngineEpoch.ToString("D"),
            State = ToTransport(value.State),
            Revision = value.Revision,
        };

    public static Domain.SessionHandle ToDomain(
        Contract.SessionHandleMessage value) =>
        new(
            ParseSessionId(value.SessionId),
            ParseGuid(value.EngineEpoch, nameof(value.EngineEpoch)),
            ToDomain(value.State),
            value.Revision);

    public static Contract.CommandResultMessage ToTransport(
        Domain.CommandResult value)
    {
        var message = new Contract.CommandResultMessage
        {
            Accepted = value.Accepted,
            ErrorCode = value.ErrorCode ?? String.Empty,
            Message = value.Message ?? String.Empty,
            AppliedRevision = value.AppliedRevision,
            AppliedBlock = value.AppliedBlock,
        };
        if (value.EnterSendMessage is Domain.EnterSendMessageResult enter)
        {
            message.EnterSendMessage = new()
            {
                Outcome = ToTransport(enter.Outcome),
                FocusTarget = ToTransport(enter.FocusTarget),
                SelectQuestionMark = enter.SelectQuestionMark,
                ClearEntry = enter.ClearEntry,
            };
            message.EnterSendMessage.SentMessages.Add(enter.SentMessages);
        }

        return message;
    }

    public static Domain.CommandResult ToDomain(
        Contract.CommandResultMessage value) =>
        new(
            value.Accepted,
            EmptyToNull(value.ErrorCode),
            EmptyToNull(value.Message),
            value.AppliedRevision,
            value.AppliedBlock,
            value.EnterSendMessage is null
                ? null
                : new Domain.EnterSendMessageResult(
                    ToDomain(value.EnterSendMessage.Outcome),
                    value.EnterSendMessage.SentMessages.ToArray(),
                    ToDomain(value.EnterSendMessage.FocusTarget),
                    value.EnterSendMessage.SelectQuestionMark,
                    value.EnterSendMessage.ClearEntry));

    public static Contract.CommandEnvelope ToTransport(
        Domain.SessionCommand value)
    {
        var message = new Contract.CommandEnvelope
        {
            RequestId = value.RequestId.ToString(),
            SessionId = value.SessionId.ToString(),
            ClientId = value.ClientId.Value,
        };
        if (value.ExpectedRevision is long revision)
        {
            message.ExpectedRevision = revision;
        }

        switch (value)
        {
            case Domain.StartSessionCommand:
                message.Start = new();
                break;
            case Domain.PauseSessionCommand:
                message.Pause = new();
                break;
            case Domain.ResumeSessionCommand:
                message.Resume = new();
                break;
            case Domain.StopSessionCommand:
                message.Stop = new();
                break;
            case Domain.AdvanceSimulationCommand advance:
                message.Advance = new() { BlockCount = advance.BlockCount };
                break;
            case Domain.RecoverAudioCommand recover:
                message.RecoverAudio = new()
                {
                    DeviceName = recover.DeviceName ?? String.Empty,
                };
                break;
            case Domain.SendOperatorIntentCommand intent:
                message.OperatorIntent = new()
                {
                    Intent = ToTransport(intent.Intent),
                    Call = intent.Call,
                    Rst = intent.Rst,
                    Exchange1 = intent.Exchange1,
                    Exchange2 = intent.Exchange2,
                };
                break;
            case Domain.TriggerEnterSendMessageCommand enter:
                message.EnterSendMessage = new()
                {
                    Entry = new()
                    {
                        Call = enter.Entry.Call,
                        Rst = enter.Entry.Rst,
                        Exchange1 = enter.Entry.Exchange1,
                        Exchange2 = enter.Entry.Exchange2,
                    },
                };
                break;
            case Domain.AdjustRadioControlCommand control:
                message.RadioControl = new()
                {
                    Control = ToTransport(control.Control),
                    Delta = control.Delta,
                };
                break;
            case Domain.SetRadioConditionCommand condition:
                message.SetRadioCondition = new()
                {
                    Condition = ToTransport(condition.Condition),
                    Enabled = condition.Enabled,
                };
                break;
            case Domain.LogQsoCommand qso:
                message.LogQso = new()
                {
                    Call = qso.Call,
                    Rst = qso.Rst,
                    Exchange1 = qso.Exchange1,
                    Exchange2 = qso.Exchange2,
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    $"Unsupported command type '{value.GetType().Name}'.");
        }

        return message;
    }

    public static Domain.SessionCommand ToDomain(
        Contract.CommandEnvelope value)
    {
        Domain.RequestId requestId = new(
            ParseGuid(value.RequestId, nameof(value.RequestId)));
        Domain.SessionId sessionId = ParseSessionId(value.SessionId);
        Domain.ClientId clientId = new(value.ClientId);
        long? revision = value.HasExpectedRevision
            ? value.ExpectedRevision
            : null;

        return value.PayloadCase switch
        {
            Contract.CommandEnvelope.PayloadOneofCase.Start =>
                new Domain.StartSessionCommand(
                    requestId,
                    sessionId,
                    clientId,
                    revision),
            Contract.CommandEnvelope.PayloadOneofCase.Pause =>
                new Domain.PauseSessionCommand(
                    requestId,
                    sessionId,
                    clientId,
                    revision),
            Contract.CommandEnvelope.PayloadOneofCase.Resume =>
                new Domain.ResumeSessionCommand(
                    requestId,
                    sessionId,
                    clientId,
                    revision),
            Contract.CommandEnvelope.PayloadOneofCase.Stop =>
                new Domain.StopSessionCommand(
                    requestId,
                    sessionId,
                    clientId,
                    revision),
            Contract.CommandEnvelope.PayloadOneofCase.Advance =>
                new Domain.AdvanceSimulationCommand(
                    requestId,
                    sessionId,
                    clientId,
                    value.Advance.BlockCount,
                    revision),
            Contract.CommandEnvelope.PayloadOneofCase.RecoverAudio =>
                new Domain.RecoverAudioCommand(
                    requestId,
                    sessionId,
                    clientId,
                    EmptyToNull(value.RecoverAudio.DeviceName),
                    revision),
            Contract.CommandEnvelope.PayloadOneofCase.OperatorIntent =>
                new Domain.SendOperatorIntentCommand(
                    requestId,
                    sessionId,
                    clientId,
                    ToDomain(value.OperatorIntent.Intent),
                    value.OperatorIntent.Call,
                    value.OperatorIntent.Rst,
                    value.OperatorIntent.Exchange1,
                    value.OperatorIntent.Exchange2,
                    revision),
            Contract.CommandEnvelope.PayloadOneofCase.EnterSendMessage =>
                new Domain.TriggerEnterSendMessageCommand(
                    requestId,
                    sessionId,
                    clientId,
                    new Domain.QsoEntrySnapshot(
                        value.EnterSendMessage.Entry.Call,
                        value.EnterSendMessage.Entry.Rst,
                        value.EnterSendMessage.Entry.Exchange1,
                        value.EnterSendMessage.Entry.Exchange2),
                    revision),
            Contract.CommandEnvelope.PayloadOneofCase.RadioControl =>
                new Domain.AdjustRadioControlCommand(
                    requestId,
                    sessionId,
                    clientId,
                    ToDomain(value.RadioControl.Control),
                    value.RadioControl.Delta,
                    revision),
            Contract.CommandEnvelope.PayloadOneofCase.SetRadioCondition =>
                new Domain.SetRadioConditionCommand(
                    requestId,
                    sessionId,
                    clientId,
                    ToDomain(value.SetRadioCondition.Condition),
                    value.SetRadioCondition.Enabled,
                    revision),
            Contract.CommandEnvelope.PayloadOneofCase.LogQso =>
                new Domain.LogQsoCommand(
                    requestId,
                    sessionId,
                    clientId,
                    value.LogQso.Call,
                    value.LogQso.Rst,
                    value.LogQso.Exchange1,
                    value.LogQso.Exchange2,
                    revision),
            _ => throw new ArgumentException(
                "A supported command payload is required.",
                nameof(value)),
        };
    }

    public static Contract.SessionSnapshotMessage ToTransport(
        Domain.SessionSnapshot value,
        Domain.ControlLeaseSummary? lease = null)
    {
        var message = new Contract.SessionSnapshotMessage
        {
            EngineEpoch = value.EngineEpoch.ToString("D"),
            SessionId = value.SessionId.ToString(),
            State = ToTransport(value.State),
            Revision = value.Revision,
            SimulationBlock = value.SimulationBlock,
            RenderedSamples = value.RenderedSamples,
            ElapsedTicks = value.ElapsedSimulationTime.Ticks,
            Seed = value.Seed,
            ContestId = value.ContestId.Value,
            RunModeId = value.RunModeId.Value,
            LastCaller = value.LastCaller ?? String.Empty,
            QsoCount = value.QsoCount,
            Score = value.Score,
            LastError = value.LastError ?? String.Empty,
            AudioQueuedBlocks = value.AudioQueuedBlocks,
            AudioUnderrunCount = value.AudioUnderrunCount,
            AudioDroppedBlockCount = value.AudioDroppedBlockCount,
            AudioOutputHealthy = value.AudioOutputHealthy,
            LastOperatorMessage = value.LastOperatorMessage ?? String.Empty,
            CurrentWordsPerMinute = value.CurrentWordsPerMinute,
            CurrentBandwidthHz = value.CurrentBandwidthHz,
            RitOffsetHz = value.RitOffsetHz,
            LastLoggedCall = value.LastLoggedCall ?? String.Empty,
            ActiveOperatorState = ToTransport(value.ActiveOperatorState),
            QsoRatePerHour = value.QsoRatePerHour,
            QsbEnabled = value.QsbEnabled,
            CurrentMonitorLevelDb = value.CurrentMonitorLevelDb,
        };
        if (lease is not null)
        {
            message.ControlLease = new()
            {
                OwningClientId = lease.OwningClientId.Value,
                ExpiresAt = Timestamp.FromDateTimeOffset(lease.ExpiresAt),
                Revision = lease.Revision,
            };
        }

        message.ActiveStations.AddRange(
            (value.ActiveStations ?? [])
            .Select(ToTransport));
        return message;
    }

    public static Domain.SessionSnapshot ToDomain(
        Contract.SessionSnapshotMessage value) =>
        new(
            ParseGuid(value.EngineEpoch, nameof(value.EngineEpoch)),
            ParseSessionId(value.SessionId),
            ToDomain(value.State),
            value.Revision,
            value.SimulationBlock,
            value.RenderedSamples,
            TimeSpan.FromTicks(value.ElapsedTicks),
            value.Seed,
            new Domain.ContestId(value.ContestId),
            new Domain.RunModeId(value.RunModeId),
            EmptyToNull(value.LastCaller),
            value.QsoCount,
            value.Score,
            EmptyToNull(value.LastError),
            value.AudioQueuedBlocks,
            value.AudioUnderrunCount,
            value.AudioDroppedBlockCount,
            value.AudioOutputHealthy,
            EmptyToNull(value.LastOperatorMessage),
            value.CurrentWordsPerMinute,
            value.CurrentBandwidthHz,
            value.RitOffsetHz,
            EmptyToNull(value.LastLoggedCall),
            ToDomain(value.ActiveOperatorState),
            value.QsoRatePerHour,
            value.ActiveStations.Select(ToDomain).ToArray(),
            value.QsbEnabled,
            value.CurrentMonitorLevelDb);

    private static Contract.ActiveStationMessage ToTransport(
        Domain.ActiveStationSnapshot value) =>
        new()
        {
            Callsign = value.Callsign,
            StationState = ToTransport(value.StationState),
            OperatorState = ToTransport(value.OperatorState),
            Patience = value.Patience,
            RepeatCount = value.RepeatCount,
            WordsPerMinute = value.WordsPerMinute,
            PitchOffsetHz = value.PitchOffsetHz,
            TrueRst = value.TrueRst,
            TrueExchange1 = value.TrueExchange1,
            TrueExchange2 = value.TrueExchange2,
            LastReply = value.LastReply ?? String.Empty,
        };

    private static Domain.ActiveStationSnapshot ToDomain(
        Contract.ActiveStationMessage value) =>
        new(
            value.Callsign,
            ToDomain(value.StationState),
            ToDomain(value.OperatorState)
                ?? throw new ArgumentException(
                    "Active station operator state is required.",
                    nameof(value)),
            value.Patience,
            value.RepeatCount,
            value.WordsPerMinute,
            value.PitchOffsetHz,
            value.TrueRst,
            value.TrueExchange1,
            value.TrueExchange2,
            EmptyToNull(value.LastReply));

    private static Contract.StationStateMessage ToTransport(
        Domain.StationState value) =>
        value switch
        {
            Domain.StationState.Listening =>
                Contract.StationStateMessage.Listening,
            Domain.StationState.Copying =>
                Contract.StationStateMessage.Copying,
            Domain.StationState.PreparingToSend =>
                Contract.StationStateMessage.PreparingToSend,
            Domain.StationState.Sending =>
                Contract.StationStateMessage.Sending,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    private static Domain.StationState ToDomain(
        Contract.StationStateMessage value) =>
        value switch
        {
            Contract.StationStateMessage.Listening =>
                Domain.StationState.Listening,
            Contract.StationStateMessage.Copying =>
                Domain.StationState.Copying,
            Contract.StationStateMessage.PreparingToSend =>
                Domain.StationState.PreparingToSend,
            Contract.StationStateMessage.Sending =>
                Domain.StationState.Sending,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    private static Contract.OperatorStateMessage ToTransport(
        Domain.OperatorState? value) =>
        value switch
        {
            null => Contract.OperatorStateMessage.Unspecified,
            Domain.OperatorState.NeedPreviousEnd =>
                Contract.OperatorStateMessage.NeedPreviousEnd,
            Domain.OperatorState.NeedQso =>
                Contract.OperatorStateMessage.NeedQso,
            Domain.OperatorState.NeedNumber =>
                Contract.OperatorStateMessage.NeedNumber,
            Domain.OperatorState.NeedCall =>
                Contract.OperatorStateMessage.NeedCall,
            Domain.OperatorState.NeedCallAndNumber =>
                Contract.OperatorStateMessage.NeedCallAndNumber,
            Domain.OperatorState.NeedEnd =>
                Contract.OperatorStateMessage.NeedEnd,
            Domain.OperatorState.Done =>
                Contract.OperatorStateMessage.Done,
            Domain.OperatorState.Failed =>
                Contract.OperatorStateMessage.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
        };

    private static Domain.OperatorState? ToDomain(
        Contract.OperatorStateMessage value) =>
        value switch
        {
            Contract.OperatorStateMessage.Unspecified => null,
            Contract.OperatorStateMessage.NeedPreviousEnd =>
                Domain.OperatorState.NeedPreviousEnd,
            Contract.OperatorStateMessage.NeedQso =>
                Domain.OperatorState.NeedQso,
            Contract.OperatorStateMessage.NeedNumber =>
                Domain.OperatorState.NeedNumber,
            Contract.OperatorStateMessage.NeedCall =>
                Domain.OperatorState.NeedCall,
            Contract.OperatorStateMessage.NeedCallAndNumber =>
                Domain.OperatorState.NeedCallAndNumber,
            Contract.OperatorStateMessage.NeedEnd =>
                Domain.OperatorState.NeedEnd,
            Contract.OperatorStateMessage.Done =>
                Domain.OperatorState.Done,
            Contract.OperatorStateMessage.Failed =>
                Domain.OperatorState.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
        };

    public static Contract.SessionEventMessage ToTransport(
        Domain.SessionEvent value) =>
        new()
        {
            EngineEpoch = value.EngineEpoch.ToString("D"),
            SessionId = value.SessionId.ToString(),
            Sequence = value.Sequence,
            Revision = value.Revision,
            SimulationBlock = value.SimulationBlock,
            Kind = ToTransport(value.Kind),
            Detail = value.Detail ?? String.Empty,
        };

    public static Domain.SessionEvent ToDomain(
        Contract.SessionEventMessage value) =>
        new(
            ParseGuid(value.EngineEpoch, nameof(value.EngineEpoch)),
            ParseSessionId(value.SessionId),
            value.Sequence,
            value.Revision,
            value.SimulationBlock,
            ToDomain(value.Kind),
            EmptyToNull(value.Detail));

    public static Contract.SessionUpdateMessage ToTransport(
        Domain.SessionUpdate value,
        Domain.ControlLeaseSummary? lease = null)
    {
        return value.Event is not null
            ? new() { Event = ToTransport(value.Event) }
            : value.Snapshot is not null
                ? new() { Snapshot = ToTransport(value.Snapshot, lease) }
                : throw new ArgumentException(
                    "A session update requires an event or snapshot.",
                    nameof(value));
    }

    public static Domain.SessionUpdate ToDomain(
        Contract.SessionUpdateMessage value) =>
        value.PayloadCase switch
        {
            Contract.SessionUpdateMessage.PayloadOneofCase.Event =>
                Domain.SessionUpdate.FromEvent(ToDomain(value.Event)),
            Contract.SessionUpdateMessage.PayloadOneofCase.Snapshot =>
                Domain.SessionUpdate.FromSnapshot(ToDomain(value.Snapshot)),
            _ => throw new ArgumentException(
                "A session update payload is required.",
                nameof(value)),
        };

    public static Contract.ControlLeaseMessage ToTransport(
        Domain.ControlLease value) =>
        new()
        {
            Token = value.Token,
            SessionId = value.SessionId.ToString(),
            OwningClientId = value.OwningClientId.Value,
            IssuedAt = Timestamp.FromDateTimeOffset(value.IssuedAt),
            ExpiresAt = Timestamp.FromDateTimeOffset(value.ExpiresAt),
            Revision = value.Revision,
        };

    public static Domain.ControlLease ToDomain(
        Contract.ControlLeaseMessage value) =>
        new(
            value.Token,
            ParseSessionId(value.SessionId),
            new Domain.ClientId(value.OwningClientId),
            value.IssuedAt.ToDateTimeOffset(),
            value.ExpiresAt.ToDateTimeOffset(),
            value.Revision);

    public static Contract.QsoMessage ToTransport(Domain.Qso value) =>
        new()
        {
            Timestamp = Timestamp.FromDateTimeOffset(value.Timestamp),
            Call = value.Call,
            Rst = value.Rst,
            Exchange1 = value.Exchange1,
            Exchange2 = value.Exchange2,
            Points = value.Points,
            Multiplier = value.Multiplier,
            IsDuplicate = value.IsDuplicate,
            ErrorText = value.ErrorText,
            Number = value.Number,
            Prefix = value.Prefix,
            ExchangeError = ToTransport(value.ExchangeError),
            TrueCall = value.TrueCall,
            RawCallsign = value.RawCallsign,
            TrueRst = value.TrueRst,
            TrueNumber = value.TrueNumber,
            Precedence = value.Precedence,
            TruePrecedence = value.TruePrecedence,
            Check = value.Check,
            TrueCheck = value.TrueCheck,
            Section = value.Section,
            TrueSection = value.TrueSection,
            TrueExchange1 = value.TrueExchange1,
            TrueExchange2 = value.TrueExchange2,
            TrueWpm = value.TrueWpm,
            Exchange1Error = ToTransport(value.Exchange1Error),
            Exchange1SecondaryError =
                ToTransport(value.Exchange1SecondaryError),
            Exchange2Error = ToTransport(value.Exchange2Error),
            Exchange2SecondaryError =
                ToTransport(value.Exchange2SecondaryError),
            ColumnErrorFlags = value.ColumnErrorFlags,
        };

    public static Domain.Qso ToDomain(Contract.QsoMessage value) =>
        new()
        {
            Timestamp = value.Timestamp.ToDateTimeOffset(),
            Call = value.Call,
            TrueCall = value.TrueCall,
            RawCallsign = value.RawCallsign,
            Rst = value.Rst,
            TrueRst = value.TrueRst,
            Number = value.Number,
            TrueNumber = value.TrueNumber,
            Precedence = value.Precedence,
            TruePrecedence = value.TruePrecedence,
            Check = value.Check,
            TrueCheck = value.TrueCheck,
            Section = value.Section,
            TrueSection = value.TrueSection,
            Exchange1 = value.Exchange1,
            TrueExchange1 = value.TrueExchange1,
            Exchange2 = value.Exchange2,
            TrueExchange2 = value.TrueExchange2,
            TrueWpm = value.TrueWpm,
            Prefix = value.Prefix,
            Points = value.Points,
            Multiplier = value.Multiplier,
            IsDuplicate = value.IsDuplicate,
            ExchangeError = ToDomain(value.ExchangeError),
            Exchange1Error = ToDomain(value.Exchange1Error),
            Exchange1SecondaryError =
                ToDomain(value.Exchange1SecondaryError),
            Exchange2Error = ToDomain(value.Exchange2Error),
            Exchange2SecondaryError =
                ToDomain(value.Exchange2SecondaryError),
            ErrorText = value.ErrorText,
            ColumnErrorFlags = value.ColumnErrorFlags,
        };

    public static Contract.SessionResultMessage ToTransport(
        Domain.SessionResult value) =>
        new()
        {
            SessionId = value.SessionId.ToString(),
            ContestId = value.ContestId.Value,
            QsoCount = value.QsoCount,
            Score = value.Score,
            ElapsedTicks = value.ElapsedSimulationTime.Ticks,
            State = ToTransport(value.State),
            QsoRatePerHour = value.QsoRatePerHour,
        };

    public static Domain.SessionResult ToDomain(
        Contract.SessionResultMessage value) =>
        new(
            ParseSessionId(value.SessionId),
            new Domain.ContestId(value.ContestId),
            value.QsoCount,
            value.Score,
            TimeSpan.FromTicks(value.ElapsedTicks),
            ToDomain(value.State),
            value.QsoRatePerHour);

    public static Domain.SessionId ParseSessionId(string value) =>
        new(ParseGuid(value, "session_id"));

    private static Guid ParseGuid(string value, string fieldName)
    {
        return Guid.TryParse(value, out Guid parsed)
            ? parsed
            : throw new ArgumentException(
                $"Field '{fieldName}' must be a GUID.");
    }

    private static Contract.SessionStateMessage ToTransport(
        Domain.SessionState value) =>
        value switch
        {
            Domain.SessionState.Created =>
                Contract.SessionStateMessage.Created,
            Domain.SessionState.Ready =>
                Contract.SessionStateMessage.Ready,
            Domain.SessionState.Running =>
                Contract.SessionStateMessage.Running,
            Domain.SessionState.Paused =>
                Contract.SessionStateMessage.Paused,
            Domain.SessionState.Stopping =>
                Contract.SessionStateMessage.Stopping,
            Domain.SessionState.Completed =>
                Contract.SessionStateMessage.Completed,
            Domain.SessionState.Faulted =>
                Contract.SessionStateMessage.Faulted,
            Domain.SessionState.Closed =>
                Contract.SessionStateMessage.Closed,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    private static Domain.SessionState ToDomain(
        Contract.SessionStateMessage value) =>
        value switch
        {
            Contract.SessionStateMessage.Created =>
                Domain.SessionState.Created,
            Contract.SessionStateMessage.Ready =>
                Domain.SessionState.Ready,
            Contract.SessionStateMessage.Running =>
                Domain.SessionState.Running,
            Contract.SessionStateMessage.Paused =>
                Domain.SessionState.Paused,
            Contract.SessionStateMessage.Stopping =>
                Domain.SessionState.Stopping,
            Contract.SessionStateMessage.Completed =>
                Domain.SessionState.Completed,
            Contract.SessionStateMessage.Faulted =>
                Domain.SessionState.Faulted,
            Contract.SessionStateMessage.Closed =>
                Domain.SessionState.Closed,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    private static Contract.SerialNumberRangeModeMessage ToTransport(
        Domain.SerialNumberRangeMode value) =>
        value switch
        {
            Domain.SerialNumberRangeMode.StartOfContest =>
                Contract.SerialNumberRangeModeMessage.StartOfContest,
            Domain.SerialNumberRangeMode.MidContest =>
                Contract.SerialNumberRangeModeMessage.MidContest,
            Domain.SerialNumberRangeMode.EndOfContest =>
                Contract.SerialNumberRangeModeMessage.EndOfContest,
            Domain.SerialNumberRangeMode.Custom =>
                Contract.SerialNumberRangeModeMessage.Custom,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    private static Domain.SerialNumberRangeMode ToDomain(
        Contract.SerialNumberRangeModeMessage value) =>
        value switch
        {
            Contract.SerialNumberRangeModeMessage.Unspecified
                or Contract.SerialNumberRangeModeMessage.StartOfContest =>
                Domain.SerialNumberRangeMode.StartOfContest,
            Contract.SerialNumberRangeModeMessage.MidContest =>
                Domain.SerialNumberRangeMode.MidContest,
            Contract.SerialNumberRangeModeMessage.EndOfContest =>
                Domain.SerialNumberRangeMode.EndOfContest,
            Contract.SerialNumberRangeModeMessage.Custom =>
                Domain.SerialNumberRangeMode.Custom,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    private static Contract.OperatorIntentMessage ToTransport(
        Domain.OperatorIntent value) =>
        (Contract.OperatorIntentMessage)((int)value + 1);

    private static Domain.OperatorIntent ToDomain(
        Contract.OperatorIntentMessage value)
    {
        int numeric = (int)value - 1;
        return System.Enum.IsDefined(typeof(Domain.OperatorIntent), numeric)
            ? (Domain.OperatorIntent)numeric
            : throw new ArgumentOutOfRangeException(nameof(value));
    }

    private static Contract.EnterSendMessageOutcomeMessage ToTransport(
        Domain.EnterSendMessageOutcome value) =>
        (Contract.EnterSendMessageOutcomeMessage)((int)value + 1);

    private static Domain.EnterSendMessageOutcome ToDomain(
        Contract.EnterSendMessageOutcomeMessage value)
    {
        int numeric = (int)value - 1;
        return System.Enum.IsDefined(
            typeof(Domain.EnterSendMessageOutcome),
            numeric)
                ? (Domain.EnterSendMessageOutcome)numeric
                : throw new ArgumentOutOfRangeException(nameof(value));
    }

    private static Contract.EntryFocusTargetMessage ToTransport(
        Domain.EntryFocusTarget value) =>
        (Contract.EntryFocusTargetMessage)((int)value + 1);

    private static Domain.EntryFocusTarget ToDomain(
        Contract.EntryFocusTargetMessage value)
    {
        int numeric = (int)value - 1;
        return System.Enum.IsDefined(
            typeof(Domain.EntryFocusTarget),
            numeric)
                ? (Domain.EntryFocusTarget)numeric
                : throw new ArgumentOutOfRangeException(nameof(value));
    }

    private static Contract.RadioControlMessage ToTransport(
        Domain.RadioControl value) =>
        (Contract.RadioControlMessage)((int)value + 1);

    private static Domain.RadioControl ToDomain(
        Contract.RadioControlMessage value)
    {
        int numeric = (int)value - 1;
        return System.Enum.IsDefined(typeof(Domain.RadioControl), numeric)
            ? (Domain.RadioControl)numeric
            : throw new ArgumentOutOfRangeException(nameof(value));
    }

    private static Contract.RadioConditionMessage ToTransport(
        Domain.RadioCondition value) =>
        (Contract.RadioConditionMessage)((int)value + 1);

    private static Domain.RadioCondition ToDomain(
        Contract.RadioConditionMessage value)
    {
        int numeric = (int)value - 1;
        return System.Enum.IsDefined(typeof(Domain.RadioCondition), numeric)
            ? (Domain.RadioCondition)numeric
            : throw new ArgumentOutOfRangeException(nameof(value));
    }

    private static Contract.SessionEventKindMessage ToTransport(
        Domain.SessionEventKind value) =>
        value switch
        {
            Domain.SessionEventKind.Created =>
                Contract.SessionEventKindMessage.Created,
            Domain.SessionEventKind.Ready =>
                Contract.SessionEventKindMessage.Ready,
            Domain.SessionEventKind.Started =>
                Contract.SessionEventKindMessage.Started,
            Domain.SessionEventKind.Paused =>
                Contract.SessionEventKindMessage.Paused,
            Domain.SessionEventKind.Resumed =>
                Contract.SessionEventKindMessage.Resumed,
            Domain.SessionEventKind.Stopping =>
                Contract.SessionEventKindMessage.Stopping,
            Domain.SessionEventKind.Completed =>
                Contract.SessionEventKindMessage.Completed,
            Domain.SessionEventKind.Closed =>
                Contract.SessionEventKindMessage.Closed,
            Domain.SessionEventKind.CommandApplied =>
                Contract.SessionEventKindMessage.CommandApplied,
            Domain.SessionEventKind.CommandRejected =>
                Contract.SessionEventKindMessage.CommandRejected,
            Domain.SessionEventKind.CallerJoined =>
                Contract.SessionEventKindMessage.CallerJoined,
            Domain.SessionEventKind.AudioDeviceFailed =>
                Contract.SessionEventKindMessage.AudioDeviceFailed,
            Domain.SessionEventKind.AudioDeviceRecovered =>
                Contract.SessionEventKindMessage.AudioDeviceRecovered,
            Domain.SessionEventKind.ControlExpired =>
                Contract.SessionEventKindMessage.ControlExpired,
            Domain.SessionEventKind.ResyncRequired =>
                Contract.SessionEventKindMessage.ResyncRequired,
            Domain.SessionEventKind.StationReplyStarted =>
                Contract.SessionEventKindMessage.StationReplyStarted,
            Domain.SessionEventKind.StationReplyCompleted =>
                Contract.SessionEventKindMessage.StationReplyCompleted,
            Domain.SessionEventKind.CallerLeft =>
                Contract.SessionEventKindMessage.CallerLeft,
            Domain.SessionEventKind.QsoLogged =>
                Contract.SessionEventKindMessage.QsoLogged,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    private static Domain.SessionEventKind ToDomain(
        Contract.SessionEventKindMessage value) =>
        value switch
        {
            Contract.SessionEventKindMessage.Created =>
                Domain.SessionEventKind.Created,
            Contract.SessionEventKindMessage.Ready =>
                Domain.SessionEventKind.Ready,
            Contract.SessionEventKindMessage.Started =>
                Domain.SessionEventKind.Started,
            Contract.SessionEventKindMessage.Paused =>
                Domain.SessionEventKind.Paused,
            Contract.SessionEventKindMessage.Resumed =>
                Domain.SessionEventKind.Resumed,
            Contract.SessionEventKindMessage.Stopping =>
                Domain.SessionEventKind.Stopping,
            Contract.SessionEventKindMessage.Completed =>
                Domain.SessionEventKind.Completed,
            Contract.SessionEventKindMessage.Closed =>
                Domain.SessionEventKind.Closed,
            Contract.SessionEventKindMessage.CommandApplied =>
                Domain.SessionEventKind.CommandApplied,
            Contract.SessionEventKindMessage.CommandRejected =>
                Domain.SessionEventKind.CommandRejected,
            Contract.SessionEventKindMessage.CallerJoined =>
                Domain.SessionEventKind.CallerJoined,
            Contract.SessionEventKindMessage.AudioDeviceFailed =>
                Domain.SessionEventKind.AudioDeviceFailed,
            Contract.SessionEventKindMessage.AudioDeviceRecovered =>
                Domain.SessionEventKind.AudioDeviceRecovered,
            Contract.SessionEventKindMessage.ControlExpired =>
                Domain.SessionEventKind.ControlExpired,
            Contract.SessionEventKindMessage.ResyncRequired =>
                Domain.SessionEventKind.ResyncRequired,
            Contract.SessionEventKindMessage.StationReplyStarted =>
                Domain.SessionEventKind.StationReplyStarted,
            Contract.SessionEventKindMessage.StationReplyCompleted =>
                Domain.SessionEventKind.StationReplyCompleted,
            Contract.SessionEventKindMessage.CallerLeft =>
                Domain.SessionEventKind.CallerLeft,
            Contract.SessionEventKindMessage.QsoLogged =>
                Domain.SessionEventKind.QsoLogged,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    private static Contract.QsoLogErrorMessage ToTransport(
        Domain.LogError value) =>
        value switch
        {
            Domain.LogError.None => Contract.QsoLogErrorMessage.None,
            Domain.LogError.Nil => Contract.QsoLogErrorMessage.Nil,
            Domain.LogError.Duplicate => Contract.QsoLogErrorMessage.Duplicate,
            Domain.LogError.Call => Contract.QsoLogErrorMessage.Call,
            Domain.LogError.Rst => Contract.QsoLogErrorMessage.Rst,
            Domain.LogError.Name => Contract.QsoLogErrorMessage.Name,
            Domain.LogError.Class => Contract.QsoLogErrorMessage.Class,
            Domain.LogError.Number => Contract.QsoLogErrorMessage.Number,
            Domain.LogError.Section => Contract.QsoLogErrorMessage.Section,
            Domain.LogError.Qth => Contract.QsoLogErrorMessage.Qth,
            Domain.LogError.Zone => Contract.QsoLogErrorMessage.Zone,
            Domain.LogError.Society => Contract.QsoLogErrorMessage.Society,
            Domain.LogError.State => Contract.QsoLogErrorMessage.State,
            Domain.LogError.Power => Contract.QsoLogErrorMessage.Power,
            Domain.LogError.Error => Contract.QsoLogErrorMessage.Error,
            Domain.LogError.Precedence =>
                Contract.QsoLogErrorMessage.Precedence,
            Domain.LogError.Check => Contract.QsoLogErrorMessage.Check,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    private static Domain.LogError ToDomain(
        Contract.QsoLogErrorMessage value) =>
        value switch
        {
            Contract.QsoLogErrorMessage.None => Domain.LogError.None,
            Contract.QsoLogErrorMessage.Nil => Domain.LogError.Nil,
            Contract.QsoLogErrorMessage.Duplicate => Domain.LogError.Duplicate,
            Contract.QsoLogErrorMessage.Call => Domain.LogError.Call,
            Contract.QsoLogErrorMessage.Rst => Domain.LogError.Rst,
            Contract.QsoLogErrorMessage.Name => Domain.LogError.Name,
            Contract.QsoLogErrorMessage.Class => Domain.LogError.Class,
            Contract.QsoLogErrorMessage.Number => Domain.LogError.Number,
            Contract.QsoLogErrorMessage.Section => Domain.LogError.Section,
            Contract.QsoLogErrorMessage.Qth => Domain.LogError.Qth,
            Contract.QsoLogErrorMessage.Zone => Domain.LogError.Zone,
            Contract.QsoLogErrorMessage.Society => Domain.LogError.Society,
            Contract.QsoLogErrorMessage.State => Domain.LogError.State,
            Contract.QsoLogErrorMessage.Power => Domain.LogError.Power,
            Contract.QsoLogErrorMessage.Error => Domain.LogError.Error,
            Contract.QsoLogErrorMessage.Precedence =>
                Domain.LogError.Precedence,
            Contract.QsoLogErrorMessage.Check => Domain.LogError.Check,
            Contract.QsoLogErrorMessage.Unspecified => Domain.LogError.None,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    private static string? EmptyToNull(string value) =>
        value.Length == 0 ? null : value;
}
