using Grpc.Core;
using Grpc.Net.Client;
using MorseRunner.Audio;
using MorseRunner.Client;
using MorseRunner.Contracts.V1;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.Grpc.Tests;

public sealed class GrpcTransportTests
{
    [Fact]
    public void ContestDefinitionMetadataMapsWithoutLoss()
    {
        ContestDefinition expected = ContestCatalog.Get(new("scCwt"));

        ContestDefinitionMessage actual =
            TransportMapper.ToTransport(expected);

        Assert.Equal(expected.Id.Value, actual.Id);
        Assert.Equal(expected.Key, actual.Key);
        Assert.Equal(expected.DisplayName, actual.DisplayName);
        Assert.Equal(expected.ExchangeType1, actual.ExchangeType1);
        Assert.Equal(expected.ExchangeType2, actual.ExchangeType2);
        Assert.Equal(expected.ExchangeCaption1, actual.ExchangeCaption1);
        Assert.Equal(expected.ExchangeCaption2, actual.ExchangeCaption2);
        Assert.Equal(
            expected.ExchangeFieldEditable,
            actual.ExchangeFieldEditable);
        Assert.Equal(expected.ExchangeDefault, actual.ExchangeDefault);
        Assert.Equal(expected.ValidationMessage, actual.ValidationMessage);
    }

    [Fact]
    public void AdvancedSessionSettingsRoundTripWithoutLoss()
    {
        SessionSettings expected = SessionSettings.CreateDefault(42) with
        {
            OperatorExchange = "599 #",
            ReceiveSpeedBelowWpm = 6,
            ReceiveSpeedAboveWpm = 2,
            StationIdRate = 5,
            CompetitionDurationMinutes = 23,
            SerialNumberRange = SerialNumberRangeMode.Custom,
            CustomSerialNumberMinimum = 70,
            CustomSerialNumberExclusiveMaximum = 80,
            CustomSerialNumberMinimumDigits = 3,
            CustomSerialNumberMaximumDigits = 4,
            HstOperatorName = "RANDY",
            AudioOutputDeviceName = "Test Device",
        };

        SessionSettings actual = TransportMapper.ToDomain(
            TransportMapper.ToTransport(expected));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void OmittedStationIdRateUsesCeDefault()
    {
        SessionSettingsMessage transport = TransportMapper.ToTransport(
            SessionSettings.CreateDefault(42));
        transport.ClearStationIdRate();

        SessionSettings actual = TransportMapper.ToDomain(transport);

        Assert.Equal(3, actual.StationIdRate);
    }

    [Fact]
    public void OmittedCompetitionDurationUsesCeDefault()
    {
        SessionSettingsMessage transport = TransportMapper.ToTransport(
            SessionSettings.CreateDefault(42) with
            {
                CompetitionDurationMinutes = 23,
            });
        transport.ClearCompetitionDurationMinutes();

        SessionSettings actual = TransportMapper.ToDomain(transport);

        Assert.Equal(60, actual.CompetitionDurationMinutes);
    }

    [Fact]
    public void OmittedCustomSerialNumberDigitsUseIniDefaults()
    {
        SessionSettingsMessage transport = TransportMapper.ToTransport(
            SessionSettings.CreateDefault(42));
        transport.ClearCustomSerialNumberMinimumDigits();
        transport.ClearCustomSerialNumberMaximumDigits();

        SessionSettings actual = TransportMapper.ToDomain(transport);

        Assert.Equal(2, actual.CustomSerialNumberMinimumDigits);
        Assert.Equal(2, actual.CustomSerialNumberMaximumDigits);
    }

    [Fact]
    public void QsoTruthAndCorrectionFieldsRoundTripWithoutLoss()
    {
        Qso expected = new()
        {
            Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(12),
            Call = "K1AB?",
            TrueCall = "K1ABC",
            RawCallsign = "k1ab?",
            Rst = 599,
            TrueRst = 579,
            Number = 12,
            TrueNumber = 13,
            Precedence = "A",
            TruePrecedence = "B",
            Check = 72,
            TrueCheck = 73,
            Section = "OR",
            TrueSection = "WA",
            Exchange1 = "AL",
            TrueExchange1 = "ALEX",
            Exchange2 = "12",
            TrueExchange2 = "13",
            TrueWpm = "31",
            Prefix = "K1",
            Multiplier = "K1",
            Points = 0,
            AwaitingStationConfirmation = true,
            ExchangeError = LogError.Call,
            Exchange1Error = LogError.Name,
            Exchange1SecondaryError = LogError.Number,
            Exchange2Error = LogError.Section,
            Exchange2SecondaryError = LogError.Check,
            ErrorText = "CALL",
            ColumnErrorFlags = 0x21,
        };

        Qso actual = TransportMapper.ToDomain(
            TransportMapper.ToTransport(expected));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SetRadioConditionCommandRoundTripsWithoutLoss()
    {
        MorseRunner.Domain.SetRadioConditionCommand expected = new(
            RequestId.New(),
            SessionId.New(),
            new ClientId("condition"),
            RadioCondition.Qsb,
            Enabled: true,
            ExpectedRevision: 14);

        SessionCommand actual = TransportMapper.ToDomain(
            TransportMapper.ToTransport(expected));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SetQskConditionCommandRoundTripsWithoutLoss()
    {
        MorseRunner.Domain.SetRadioConditionCommand expected = new(
            RequestId.New(),
            SessionId.New(),
            new ClientId("condition"),
            RadioCondition.Qsk,
            Enabled: true,
            ExpectedRevision: 15);

        SessionCommand actual = TransportMapper.ToDomain(
            TransportMapper.ToTransport(expected));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResetOperatorEntryCommandRoundTripsWithoutLoss()
    {
        MorseRunner.Domain.ResetOperatorEntryCommand expected = new(
            RequestId.New(),
            SessionId.New(),
            new ClientId("wipe"),
            ExpectedRevision: 16);

        SessionCommand actual = TransportMapper.ToDomain(
            TransportMapper.ToTransport(expected));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void MonitorLevelCommandRoundTripsWithoutLoss()
    {
        AdjustRadioControlCommand expected = new(
            RequestId.New(),
            SessionId.New(),
            new ClientId("monitor"),
            RadioControl.MonitorLevel,
            Delta: -60,
            ExpectedRevision: 15);

        SessionCommand actual = TransportMapper.ToDomain(
            TransportMapper.ToTransport(expected));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task InProcessAndGrpcClientsProduceEquivalentScenario()
    {
        await using GrpcTestHost host = await GrpcTestHost.StartAsync();
        await using GrpcMorseRunnerClient grpc = host.CreateClient("scenario");
        await using var direct = new InProcessMorseRunnerClient(
            new MorseRunnerEngine(_ => new NullAudioSink()));
        SessionSettings settings = SessionSettings.CreateDefault(8675309);

        SessionHandle directHandle = await direct.CreateSessionAsync(
            settings,
            TestContext.Current.CancellationToken);
        SessionHandle grpcHandle = await grpc.CreateSessionAsync(
            settings,
            TestContext.Current.CancellationToken);

        await RunScenarioAsync(direct, directHandle.SessionId, "scenario");
        await RunScenarioAsync(grpc, grpcHandle.SessionId, "scenario");

        SessionSnapshot directSnapshot = await direct.GetSnapshotAsync(
            directHandle.SessionId,
            TestContext.Current.CancellationToken);
        SessionSnapshot grpcSnapshot = await grpc.GetSnapshotAsync(
            grpcHandle.SessionId,
            TestContext.Current.CancellationToken);
        AssertEquivalent(directSnapshot, grpcSnapshot);

        IReadOnlyList<Qso> directQsos = await direct.ListCompletedQsosAsync(
            directHandle.SessionId,
            TestContext.Current.CancellationToken);
        IReadOnlyList<Qso> grpcQsos = await grpc.ListCompletedQsosAsync(
            grpcHandle.SessionId,
            TestContext.Current.CancellationToken);
        Assert.Equal(directQsos, grpcQsos);

        SessionResult directResult = await direct.GetResultAsync(
            directHandle.SessionId,
            TestContext.Current.CancellationToken);
        SessionResult grpcResult = await grpc.GetResultAsync(
            grpcHandle.SessionId,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            directResult with { SessionId = grpcResult.SessionId },
            grpcResult);
    }

    [Fact]
    public async Task EnterSendMessageOutcomeRoundTripsAcrossGrpc()
    {
        await using GrpcTestHost host = await GrpcTestHost.StartAsync();
        await using GrpcMorseRunnerClient client = host.CreateClient("esm");
        SessionHandle handle = await client.CreateSessionAsync(
            SessionSettings.CreateDefault(314_159),
            TestContext.Current.CancellationToken);
        var clientId = new ClientId("esm");
        Assert.True(
            (await client.ExecuteAsync(
                new StartSessionCommand(
                    RequestId.New(),
                    handle.SessionId,
                    clientId),
                TestContext.Current.CancellationToken)).Accepted);

        CommandResult first = await client.ExecuteAsync(
            new TriggerEnterSendMessageCommand(
                RequestId.New(),
                handle.SessionId,
                clientId,
                new("K1ABC", "5NN", "123", "")),
            TestContext.Current.CancellationToken);
        CommandResult completed = await client.ExecuteAsync(
            new TriggerEnterSendMessageCommand(
                RequestId.New(),
                handle.SessionId,
                clientId,
                new("K1ABC", "5NN", "123", "")),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            EnterSendMessageOutcome.SendCallAndExchange,
            first.EnterSendMessage?.Outcome);
        Assert.Equal(
            ["K1ABC", "5NN 001"],
            first.EnterSendMessage?.SentMessages);
        Assert.Equal(
            EnterSendMessageOutcome.CompleteAndLogQso,
            completed.EnterSendMessage?.Outcome);
        Assert.Equal(["TU"], completed.EnterSendMessage?.SentMessages);
        Assert.True(completed.EnterSendMessage?.ClearEntry);
        Assert.Single(
            await client.ListCompletedQsosAsync(
                handle.SessionId,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HostRejectsMissingAuthenticationToken()
    {
        await using GrpcTestHost host = await GrpcTestHost.StartAsync();
        using GrpcChannel channel = GrpcChannel.ForAddress(host.Address);
        var client = new EngineService.EngineServiceClient(channel);

        RpcException exception = await Assert.ThrowsAsync<RpcException>(
            async () => await client.GetEngineInfoAsync(
                new(),
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
    }

    [Fact]
    public async Task ControlLeasePreventsConcurrentMutation()
    {
        await using GrpcTestHost host = await GrpcTestHost.StartAsync();
        await using GrpcMorseRunnerClient owner = host.CreateClient("owner");
        await using GrpcMorseRunnerClient observer = host.CreateClient("observer");
        SessionHandle handle = await owner.CreateSessionAsync(
            SessionSettings.CreateDefault(7),
            TestContext.Current.CancellationToken);

        MorseRunnerTransportException held =
            await Assert.ThrowsAsync<MorseRunnerTransportException>(
                () => observer.AcquireControlAsync(
                    handle.SessionId,
                    TestContext.Current.CancellationToken));
        Assert.Equal("FailedPrecondition", held.TransportStatus);

        await owner.ReleaseControlAsync(
            handle.SessionId,
            TestContext.Current.CancellationToken);
        ControlLease lease = await observer.AcquireControlAsync(
            handle.SessionId,
            TestContext.Current.CancellationToken);
        Assert.Equal(new ClientId("observer"), lease.OwningClientId);
    }

    [Fact]
    public async Task UnreadSubscriptionDoesNotDelayCommands()
    {
        await using GrpcTestHost host = await GrpcTestHost.StartAsync();
        await using GrpcMorseRunnerClient client = host.CreateClient("slow");
        SessionHandle handle = await client.CreateSessionAsync(
            SessionSettings.CreateDefault(11),
            TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        await using IAsyncEnumerator<SessionUpdate> subscription =
            client.SubscribeAsync(
                    new(handle.SessionId),
                    cancellation.Token)
                .GetAsyncEnumerator(cancellation.Token);
        Assert.True(await subscription.MoveNextAsync());

        CommandResult started = await client.ExecuteAsync(
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                new("slow")),
            TestContext.Current.CancellationToken);
        CommandResult advanced = await client.ExecuteAsync(
            new AdvanceSimulationCommand(
                RequestId.New(),
                handle.SessionId,
                new("slow"),
                128),
            TestContext.Current.CancellationToken);

        Assert.True(started.Accepted);
        Assert.True(advanced.Accepted);
        SessionSnapshot snapshot = await client.GetSnapshotAsync(
            handle.SessionId,
            TestContext.Current.CancellationToken);
        Assert.Equal(128, snapshot.SimulationBlock);
        cancellation.Cancel();
    }

    [Fact]
    public async Task LeaseExpiryPausesRunningSessionAtBlockBoundary()
    {
        var clock = new AdjustableTimeProvider(
            new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero));
        await using GrpcTestHost host = await GrpcTestHost.StartAsync(clock);
        await using GrpcMorseRunnerClient client = host.CreateClient("expiring");
        SessionHandle handle = await client.CreateSessionAsync(
            SessionSettings.CreateDefault(23),
            TestContext.Current.CancellationToken);
        Assert.True(
            (await client.ExecuteAsync(
                new StartSessionCommand(
                    RequestId.New(),
                    handle.SessionId,
                    new("expiring")),
                TestContext.Current.CancellationToken)).Accepted);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using IAsyncEnumerator<SessionUpdate> updates =
            client.SubscribeAsync(new(handle.SessionId), timeout.Token)
                .GetAsyncEnumerator(timeout.Token);
        Assert.True(await updates.MoveNextAsync());

        clock.Advance(TimeSpan.FromSeconds(13));
        SessionEvent? expired = null;
        while (expired is null && await updates.MoveNextAsync())
        {
            if (updates.Current.Event?.Kind == SessionEventKind.ControlExpired)
            {
                expired = updates.Current.Event;
            }
        }

        Assert.NotNull(expired);
        SessionSnapshot snapshot = await client.GetSnapshotAsync(
            handle.SessionId,
            TestContext.Current.CancellationToken);
        Assert.Equal(SessionState.Paused, snapshot.State);
    }

    private static async Task RunScenarioAsync(
        IMorseRunnerClient client,
        SessionId sessionId,
        string clientId)
    {
        ClientId id = new(clientId);
        Assert.True(
            (await client.ExecuteAsync(
                new StartSessionCommand(RequestId.New(), sessionId, id),
                TestContext.Current.CancellationToken)).Accepted);
        Assert.True(
            (await client.ExecuteAsync(
                new SendOperatorIntentCommand(
                    RequestId.New(),
                    sessionId,
                    id,
                    OperatorIntent.Cq,
                    String.Empty,
                    "5NN",
                    String.Empty,
                    String.Empty),
                TestContext.Current.CancellationToken)).Accepted);
        Assert.True(
            (await client.ExecuteAsync(
                new MorseRunner.Domain.SetRadioConditionCommand(
                    RequestId.New(),
                    sessionId,
                    id,
                    RadioCondition.Qsb,
                    Enabled: true),
                TestContext.Current.CancellationToken)).Accepted);
        Assert.True(
            (await client.ExecuteAsync(
                new MorseRunner.Domain.SetRadioConditionCommand(
                    RequestId.New(),
                    sessionId,
                    id,
                    RadioCondition.Qsk,
                    Enabled: true),
                TestContext.Current.CancellationToken)).Accepted);
        Assert.True(
            (await client.ExecuteAsync(
                new AdjustRadioControlCommand(
                    RequestId.New(),
                    sessionId,
                    id,
                    RadioControl.MonitorLevel,
                    -60),
                TestContext.Current.CancellationToken)).Accepted);
        Assert.True(
            (await client.ExecuteAsync(
                new AdvanceSimulationCommand(
                    RequestId.New(),
                    sessionId,
                    id,
                    3),
                TestContext.Current.CancellationToken)).Accepted);
        string[] calls = ["K1ABC", "K2XYZ", "K1ABC"];
        for (int index = 0; index < calls.Length; index++)
        {
            Assert.True(
                (await client.ExecuteAsync(
                    new MorseRunner.Domain.LogQsoCommand(
                        RequestId.New(),
                        sessionId,
                        id,
                        calls[index],
                        "5NN",
                        (index + 1).ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        String.Empty),
                    TestContext.Current.CancellationToken)).Accepted);
        }
    }

    private static void AssertEquivalent(
        SessionSnapshot expected,
        SessionSnapshot actual)
    {
        Assert.Equal(expected.State, actual.State);
        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.SimulationBlock, actual.SimulationBlock);
        Assert.Equal(expected.RenderedSamples, actual.RenderedSamples);
        Assert.Equal(
            expected.ElapsedSimulationTime,
            actual.ElapsedSimulationTime);
        Assert.Equal(expected.Seed, actual.Seed);
        Assert.Equal(expected.ContestId, actual.ContestId);
        Assert.Equal(expected.RunModeId, actual.RunModeId);
        Assert.Equal(expected.LastCaller, actual.LastCaller);
        Assert.Equal(expected.ActiveOperatorState, actual.ActiveOperatorState);
        Assert.Equal(expected.ActiveStations, actual.ActiveStations);
        Assert.Equal(expected.LastOperatorMessage, actual.LastOperatorMessage);
        Assert.Equal(expected.QsoCount, actual.QsoCount);
        Assert.Equal(expected.Score, actual.Score);
        Assert.Equal(expected.QsbEnabled, actual.QsbEnabled);
        Assert.Equal(expected.QskEnabled, actual.QskEnabled);
        Assert.Equal(
            expected.CurrentMonitorLevelDb,
            actual.CurrentMonitorLevelDb);
    }
}
