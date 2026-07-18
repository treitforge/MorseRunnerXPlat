using MorseRunner.Audio;
using MorseRunner.Domain;
using MorseRunner.Dsp;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatSimulationTarget : IParityTarget
{
    public async Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string[] values = scenario.Id switch
        {
            "simulation.state-models" => ObserveStates(),
            "simulation.runtime-routines" => ObserveRuntime(),
            "simulation.live-operator-session" =>
                await ObserveLiveOperatorSessionAsync(cancellationToken),
            _ => [],
        };
        bool matches = values.SequenceEqual(
            scenario.ExpectedValues,
            StringComparer.Ordinal);
        return new(
            matches ? ParityTargetOutcome.Passed : ParityTargetOutcome.Failed,
            values,
            matches ? null : DomainErrorCodes.UnsupportedCapability,
            "MorseRunner.Engine");
    }

    private static string[] ObserveStates()
    {
        var values = new List<string>();
        var value = new SimulatedOperator(
            "W7SST",
            OperatorState.NeedPreviousEnd,
            new LegacyRandom(12_345),
            OperatorRunMode.Pileup);
        values.Add(
            $"created={(int)value.State}|skills={value.Skills}|patience={value.Patience}");
        value.Receive(StationMessage.Cq);
        values.Add($"after-cq={(int)value.State}|patience={value.Patience}");
        value.Receive(StationMessage.Cq);
        values.Add(
            $"after-repeat-cq={(int)value.State}|patience={value.Patience}");
        value.SetState(OperatorState.NeedNumber);
        values.Add($"need-nr={(int)value.State}|patience={value.Patience}");
        value.Receive(StationMessage.Nil);
        values.Add($"after-nil={(int)value.State}");
        value.SetState(OperatorState.NeedQso);
        values.Add($"active-ghosting={value.IsGhosting}");
        AddMatch(values, "full", value, "W7SST");
        AddMatch(values, "wildcard", value, "W7S??");
        AddMatch(values, "substring", value, "SST");
        AddMatch(values, "none", value, "K1ABC");
        return [.. values];
    }

    private static string[] ObserveRuntime()
    {
        var values = new List<string>();
        var value = new SimulatedOperator(
            "K1ABC",
            OperatorState.NeedQso,
            new LegacyRandom(24_680),
            OperatorRunMode.SingleCall);
        values.Add($"send-delay={value.GetSendDelay()}");
        values.Add($"reply-timeout={value.GetReplyTimeout()}");
        value.Receive(StationMessage.Cq);
        values.Add($"repeat-cq={(int)value.State}|patience={value.Patience}");
        value.SetState(OperatorState.NeedNumber);
        value.Receive(StationMessage.Number);
        values.Add($"after-number={(int)value.State}");
        value.Receive(StationMessage.ThankYou);
        values.Add($"after-tu={(int)value.State}");
        value.SetState(OperatorState.NeedQso);
        for (int index = 1; index <= 8; index++)
        {
            value.Receive(StationMessage.Nil);
            values.Add(
                $"timeout-path[{index}]={(int)value.State}|patience={value.Patience}");
            if (value.State == OperatorState.Failed)
            {
                break;
            }
        }

        return [.. values];
    }

    private static async Task<string[]> ObserveLiveOperatorSessionAsync(
        CancellationToken cancellationToken)
    {
        var values = new List<string>();
        await using MorseRunnerEngine engine = new(_ => new NullAudioSink());
        SessionSettings settings = SessionSettings.CreateDefault(24_680) with
        {
            RunModeId = RunModeCatalog.All[2],
        };
        SessionHandle handle = await engine.CreateSessionAsync(
            settings,
            cancellationToken);
        ClientId client = new("legacy-parity");
        await ExecuteAsync(
            engine,
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                client),
            cancellationToken);
        await ExecuteAsync(
            engine,
            new AdvanceSimulationCommand(
                RequestId.New(),
                handle.SessionId,
                client,
                BlockCount: 8),
            cancellationToken);
        SessionSnapshot snapshot = engine.GetSnapshot(handle.SessionId);
        values.Add($"joined={(int?)snapshot.ActiveOperatorState}");

        await ExecuteAsync(
            engine,
            new SendOperatorIntentCommand(
                RequestId.New(),
                handle.SessionId,
                client,
                OperatorIntent.HisCall,
                snapshot.LastCaller!,
                string.Empty,
                string.Empty,
                string.Empty),
            cancellationToken,
            expectedBlock: 8);
        snapshot = engine.GetSnapshot(handle.SessionId);
        values.Add($"after-call={(int?)snapshot.ActiveOperatorState}");

        await ExecuteAsync(
            engine,
            new SendOperatorIntentCommand(
                RequestId.New(),
                handle.SessionId,
                client,
                OperatorIntent.Exchange,
                snapshot.LastCaller!,
                "5NN",
                "001",
                string.Empty),
            cancellationToken,
            expectedBlock: 8);
        snapshot = engine.GetSnapshot(handle.SessionId);
        values.Add($"after-exchange={(int?)snapshot.ActiveOperatorState}");

        await ExecuteAsync(
            engine,
            new SendOperatorIntentCommand(
                RequestId.New(),
                handle.SessionId,
                client,
                OperatorIntent.ThankYou,
                snapshot.LastCaller!,
                string.Empty,
                string.Empty,
                string.Empty),
            cancellationToken,
            expectedBlock: 8);
        snapshot = engine.GetSnapshot(handle.SessionId);
        values.Add($"after-tu={(int?)snapshot.ActiveOperatorState}");
        return [.. values];
    }

    private static async Task ExecuteAsync(
        MorseRunnerEngine engine,
        SessionCommand command,
        CancellationToken cancellationToken,
        long? expectedBlock = null)
    {
        CommandResult result = await engine.ExecuteAsync(
            command,
            cancellationToken);
        if (!result.Accepted)
        {
            throw new InvalidOperationException(result.Message);
        }

        if (expectedBlock is long expected
            && result.AppliedBlock != expected)
        {
            throw new InvalidOperationException(
                $"Command applied at block {result.AppliedBlock}, expected {expected}.");
        }
    }

    private static void AddMatch(
        List<string> values,
        string name,
        SimulatedOperator value,
        string pattern)
    {
        CallMatch match = value.MatchCall(pattern);
        values.Add(
            $"match-{name}={(int)match}|confidence={value.CallConfidence}");
    }
}
