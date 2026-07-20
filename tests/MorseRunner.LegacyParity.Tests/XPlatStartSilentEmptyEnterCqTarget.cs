using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using MorseRunner.Domain;
using MorseRunner.Engine;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatStartSilentEmptyEnterCqTarget : IParityTarget
{
    internal const string ParityId =
        "engine.start-silent-empty-enter-cq-seed-12345";
    internal const string FunctionalDivergenceCode =
        "engine-start-silent-empty-enter-cq-mismatch";
    internal const string EvidenceSource =
        "MorseRunner.Engine.MorseRunnerEngine.ExecuteAsync"
        + "+MorseRunner.Engine.EngineSession.ApplyStart"
        + "+MorseRunner.Engine.EngineSession.ApplyEnterSendMessage"
        + "+MorseRunner.Engine.IAudioSink.WriteAsync";

    private static readonly ClientId ParityClient =
        new("parity-start-empty-enter");

    public async Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!StringComparer.Ordinal.Equals(scenario.Id, ParityId))
        {
            return new(
                ParityTargetOutcome.Failed,
                [],
                DomainErrorCodes.UnsupportedCapability,
                EvidenceSource);
        }

        StartSilentEmptyEnterCqInput input =
            StartSilentEmptyEnterCqInput.Parse(scenario);
        string[] values = await ObserveAsync(input, cancellationToken);
        bool matches = values.SequenceEqual(
            scenario.ExpectedValues,
            StringComparer.Ordinal);
        return new(
            matches
                ? ParityTargetOutcome.Passed
                : ParityTargetOutcome.Failed,
            values,
            matches ? null : FunctionalDivergenceCode,
            EvidenceSource);
    }

    internal static async Task<string[]> ObserveAsync(
        StartSilentEmptyEnterCqInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        float[] started = await CaptureFirstBlockAsync(
            input,
            abortBeforeAdvance: false,
            cancellationToken);
        float[] aborted = await CaptureFirstBlockAsync(
            input,
            abortBeforeAdvance: true,
            cancellationToken);
        bool startWasSilent = started.AsSpan().SequenceEqual(aborted);

        EnterSendMessageResult enter = await CaptureEmptyEnterAsync(
            input,
            cancellationToken);
        string messageText = enter.SentMessages.Count == 1
            ? enter.SentMessages[0]
            : String.Join(",", enter.SentMessages);

        return
        [
            "configuration"
            + $"|scenario={ParityId}"
            + $"|contest={input.ContestId}"
            + $"|run-mode={input.RunModeId}"
            + $"|station={input.StationCall}"
            + $"|seed={Format(input.Seed)}",
            startWasSilent
                ? "start-boundary"
                    + "|operator-state=stListening"
                    + "|message-set=none"
                    + "|message-text="
                    + "|envelope-sample-count=0"
                    + "|qso-count=0"
                : "start-boundary"
                    + "|operator-state=stSending"
                    + "|message-set=cq"
                    + "|message-text=CQ TEST"
                    + "|envelope-sample-count-positive=true"
                    + "|qso-count=0",
            "empty-enter"
            + "|operator-state=stSending"
            + "|message-set=cq"
            + $"|message-text={messageText}"
            + "|envelope-sample-count-positive=true"
            + "|qso-count=0",
        ];
    }

    private static async Task<float[]> CaptureFirstBlockAsync(
        StartSilentEmptyEnterCqInput input,
        bool abortBeforeAdvance,
        CancellationToken cancellationToken)
    {
        var sink = new SingleBlockAudioSink();
        await using var engine = new MorseRunnerEngine(
            _ => sink,
            new MorseRunnerEngineOptions
            {
                AutomaticTiming = false,
            });
        SessionHandle handle = await engine.CreateSessionAsync(
            CreateSettings(input),
            cancellationToken);
        await RequireAcceptedAsync(
            engine,
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                ParityClient),
            "start",
            cancellationToken);
        if (abortBeforeAdvance)
        {
            await RequireAcceptedAsync(
                engine,
                new SendOperatorIntentCommand(
                    RequestId.New(),
                    handle.SessionId,
                    ParityClient,
                    OperatorIntent.Abort,
                    String.Empty,
                    String.Empty,
                    String.Empty,
                    String.Empty),
                "abort",
                cancellationToken);
        }

        await RequireAcceptedAsync(
            engine,
            new AdvanceSimulationCommand(
                RequestId.New(),
                handle.SessionId,
                ParityClient,
                BlockCount: 1),
            "advance",
            cancellationToken);
        SessionSnapshot snapshot = engine.GetSnapshot(handle.SessionId);
        if (snapshot.State != SessionState.Running
            || snapshot.SimulationBlock != 1
            || snapshot.QsoCount != 0)
        {
            throw new InvalidOperationException(
                "The XPlat start-boundary capture entered an "
                + "unexpected session state.");
        }

        return sink.RequireSingleBlock();
    }

    private static async Task<EnterSendMessageResult> CaptureEmptyEnterAsync(
        StartSilentEmptyEnterCqInput input,
        CancellationToken cancellationToken)
    {
        await using var engine = new MorseRunnerEngine(
            _ => new NullParityAudioSink(),
            new MorseRunnerEngineOptions
            {
                AutomaticTiming = false,
            });
        SessionHandle handle = await engine.CreateSessionAsync(
            CreateSettings(input),
            cancellationToken);
        await RequireAcceptedAsync(
            engine,
            new StartSessionCommand(
                RequestId.New(),
                handle.SessionId,
                ParityClient),
            "start",
            cancellationToken);
        CommandResult result = await RequireAcceptedAsync(
            engine,
            new TriggerEnterSendMessageCommand(
                RequestId.New(),
                handle.SessionId,
                ParityClient,
                new QsoEntrySnapshot(
                    String.Empty,
                    String.Empty,
                    String.Empty,
                    String.Empty)),
            "empty Enter",
            cancellationToken);
        EnterSendMessageResult enter = result.EnterSendMessage
            ?? throw new InvalidOperationException(
                "The accepted empty Enter returned no semantic result.");
        if (enter.Outcome != EnterSendMessageOutcome.SendCq
            || enter.SentMessages.Count != 1
            || enter.ClearEntry)
        {
            throw new InvalidOperationException(
                "The XPlat empty Enter returned an unexpected semantic "
                + "result.");
        }

        return enter;
    }

    private static SessionSettings CreateSettings(
        StartSilentEmptyEnterCqInput input)
    {
        return new(
            input.Seed,
            new ContestId(input.ContestId),
            new RunModeId(input.RunModeId),
            DurationBlocks: 0)
        {
            StationCall = input.StationCall,
            WordsPerMinute = 25,
            PitchHz = 600,
            BandwidthHz = 500,
            Activity = 2,
            Qsk = false,
            Qsb = false,
            Qrm = false,
            Qrn = false,
            Flutter = false,
            Lids = false,
        };
    }

    private static async Task<CommandResult> RequireAcceptedAsync(
        MorseRunnerEngine engine,
        SessionCommand command,
        string operation,
        CancellationToken cancellationToken)
    {
        CommandResult result = await engine.ExecuteAsync(
            command,
            cancellationToken);
        if (!result.Accepted)
        {
            throw new InvalidOperationException(
                $"The XPlat {operation} command was rejected with "
                + $"'{result.ErrorCode ?? "<none>"}': "
                + $"{result.Message ?? "<no message>"}.");
        }

        return result;
    }

    private static string Format(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private sealed class SingleBlockAudioSink : IAudioSink
    {
        private float[]? _block;

        public ValueTask InitializeAsync(
            SessionId sessionId,
            AudioStreamFormat format,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (format != AudioStreamFormat.Compatibility)
            {
                throw new InvalidOperationException(
                    "The start-boundary sink received an unexpected "
                    + "format.");
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask WriteAsync(
            ReadOnlyMemory<float> samples,
            long simulationBlock,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_block is not null
                || simulationBlock != 0
                || samples.Length
                    != CompatibilityProfile.BlockSize)
            {
                throw new InvalidOperationException(
                    "The start-boundary sink received invalid audio "
                    + "framing.");
            }

            _block = samples.ToArray();
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public float[] RequireSingleBlock()
        {
            return _block
                ?? throw new InvalidOperationException(
                    "The start-boundary sink captured no audio.");
        }
    }

    private sealed class NullParityAudioSink : IAudioSink
    {
        public ValueTask InitializeAsync(
            SessionId sessionId,
            AudioStreamFormat format,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteAsync(
            ReadOnlyMemory<float> samples,
            long simulationBlock,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed record StartSilentEmptyEnterCqInput(
    string ContestId,
    string RunModeId,
    string StationCall,
    int Seed)
{
    public static StartSilentEmptyEnterCqInput Parse(
        ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        string[] names = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        string[] expectedNames =
        [
            "contestId",
            "runModeId",
            "scenario",
            "seed",
            "stationCall",
        ];
        if (!names.SequenceEqual(expectedNames, StringComparer.Ordinal)
            || input.GetProperty("scenario").GetString()
                != XPlatStartSilentEmptyEnterCqTarget.ParityId
            || !input.GetProperty("seed").TryGetInt32(out int seed))
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' input is invalid.");
        }

        string contestId = RequireString(input, "contestId", scenario.Id);
        string runModeId = RequireString(input, "runModeId", scenario.Id);
        string stationCall = RequireString(
            input,
            "stationCall",
            scenario.Id);
        if (contestId != "scWpx"
            || runModeId != "rmPileup"
            || stationCall != "W7SST"
            || seed != 12345
            || scenario.ExpectedValues.Count != 3)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' fixed vector is invalid.");
        }

        return new(contestId, runModeId, stationCall, seed);
    }

    private static string RequireString(
        JsonElement input,
        string propertyName,
        string scenarioId)
    {
        JsonElement value = input.GetProperty(propertyName);
        string? result = value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
        return !String.IsNullOrWhiteSpace(result)
            ? result
            : throw new InvalidDataException(
                $"Parity case '{scenarioId}' {propertyName} is invalid.");
    }
}
