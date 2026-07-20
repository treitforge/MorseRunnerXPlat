using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using MorseRunner.App.ViewModels;
using MorseRunner.App.Views;
using MorseRunner.Client;
using MorseRunner.Domain;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatEnterEsmTarget : IParityTarget
{
    private static readonly object HeadlessPlatformGate = new();
    private static HeadlessUnitTestSession? _headlessSession;

    internal const string ParityId =
        "ux.enter-esm-partial-call-message-selection-live";
    internal const string FunctionalDivergenceCode =
        "ux-enter-esm-partial-call-message-selection-mismatch";
    internal const string EvidenceSource =
        "Avalonia.Headless.MainWindow.KeyPress"
        + "+MorseRunner.App.ViewModels.MainWindowViewModel"
        + "+MorseRunner.Client.InProcessMorseRunnerClient"
        + "+EnterSendMessageResult.QueuedSemanticMessages";

    public async Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!StringComparer.Ordinal.Equals(scenario.Id, ParityId))
        {
            return new ParityObservation(
                ParityTargetOutcome.Failed,
                [],
                DomainErrorCodes.UnsupportedCapability,
                EvidenceSource);
        }

        EnterEsmScenarioInput input = EnterEsmScenarioInput.Parse(scenario);
        HeadlessUnitTestSession session = EnsureHeadlessPlatform();
        string[] values = await session.Dispatch(
            () => ObserveAsync(input, cancellationToken),
            cancellationToken);
        bool matches = values.SequenceEqual(
            scenario.ExpectedValues,
            StringComparer.Ordinal);
        return new ParityObservation(
            matches
                ? ParityTargetOutcome.Passed
                : ParityTargetOutcome.Failed,
            values,
            matches ? null : FunctionalDivergenceCode,
            EvidenceSource);
    }

    private static async Task<string[]> ObserveAsync(
        EnterEsmScenarioInput input,
        CancellationToken cancellationToken)
    {
        var rows = new List<string>(input.Actions.Count);
        EnterEsmRuntime? runtime = null;
        SessionSettings? firstSettings = null;
        try
        {
            for (int index = 0; index < input.Actions.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnterEsmActionInput action = input.Actions[index];
                if (action.Reset)
                {
                    if (runtime is not null)
                    {
                        await runtime.DisposeAsync();
                    }

                    runtime = await EnterEsmRuntime.CreateAsync(
                        input,
                        cancellationToken);
                    firstSettings ??= runtime.Settings;
                }
                else if (runtime is null)
                {
                    throw new InvalidDataException(
                        $"Enter/ESM action '{action.Id}' requested "
                        + "continuation without a runtime.");
                }

                rows.Add(
                    await runtime.ObserveAsync(
                        index,
                        action,
                        cancellationToken));
            }
        }
        finally
        {
            if (runtime is not null)
            {
                await runtime.DisposeAsync();
            }
        }

        if (firstSettings is null)
        {
            throw new InvalidOperationException(
                "Enter/ESM observation did not create an XPlat session.");
        }

        return
        [
            FormatConfiguration(input, firstSettings),
            .. rows,
        ];
    }

    private static string FormatConfiguration(
        EnterEsmScenarioInput input,
        SessionSettings settings)
    {
        return "configuration"
            + $"|scenario={input.Scenario}"
            + $"|contest={settings.ContestId.Value}"
            + $"|run-mode={settings.RunModeId.Value}"
            + $"|station={settings.StationCall}"
            + $"|seed={Format(settings.Seed)}"
            + $"|action-count={Format(input.Actions.Count)}";
    }

    private static string Format(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    internal static string NormalizeMessages(
        EnterEsmActionInput action,
        CommandResult result)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Accepted)
        {
            throw new InvalidOperationException(
                $"Enter/ESM command for action '{action.Id}' was rejected"
                + $" with error '{result.ErrorCode ?? "<none>"}': "
                + $"{result.Message ?? "<no message>"}");
        }

        if (result.ErrorCode is not null)
        {
            throw new InvalidOperationException(
                $"Accepted Enter/ESM command for action '{action.Id}' "
                + $"reported error '{result.ErrorCode}'.");
        }

        EnterSendMessageResult enter = result.EnterSendMessage
            ?? throw new InvalidOperationException(
                $"Accepted Enter/ESM command for action '{action.Id}' "
                + "did not return an Enter result.");
        ExpectedEnterResult expected = action.Id switch
        {
            "empty" when action.Call.Length == 0 => new(
                EnterSendMessageOutcome.SendCq,
                [],
                ClearEntry: false,
                "cq"),
            "short-partial" when action.Call == "K1" => new(
                EnterSendMessageOutcome.SendEnteredCall,
                ["K1"],
                ClearEntry: false,
                "his-call:K1"),
            "uncertain" when action.Call == "K1A?" => new(
                EnterSendMessageOutcome.SendEnteredCall,
                ["K1A?"],
                ClearEntry: false,
                "his-call:K1A?"),
            "corrected" when action.Call == "K1ABC" => new(
                EnterSendMessageOutcome.SendCallAndExchange,
                ["K1ABC", "5NN 001"],
                ClearEntry: false,
                "his-call:K1ABC,exchange"),
            "same-call-repeat" when action.Call == "K1ABC" => new(
                EnterSendMessageOutcome.RequestExchangeRepeat,
                ["?"],
                ClearEntry: false,
                "question"),
            "complete" when action.Call == "K2XYZ" => new(
                EnterSendMessageOutcome.SendCallAndExchange,
                ["K2XYZ", "5NN 001"],
                ClearEntry: false,
                "his-call:K2XYZ,exchange"),
            _ => throw new InvalidDataException(
                $"Enter/ESM action '{action.Id}' is not a fixed vector row."),
        };

        bool messagesMatch = action.Id == "empty"
            ? enter.SentMessages is [string cq]
                && !String.IsNullOrWhiteSpace(cq)
            : enter.SentMessages is not null
                && enter.SentMessages.SequenceEqual(
                    expected.SentMessages,
                    StringComparer.Ordinal);
        if (enter.Outcome != expected.Outcome
            || !messagesMatch
            || enter.ClearEntry != expected.ClearEntry)
        {
            throw new InvalidOperationException(
                $"Enter/ESM result for action '{action.Id}' has an "
                + "unsupported outcome or message shape.");
        }

        return expected.NormalizedMessages;
    }

    private static HeadlessUnitTestSession EnsureHeadlessPlatform()
    {
        lock (HeadlessPlatformGate)
        {
            if (_headlessSession is not null)
            {
                return _headlessSession;
            }

            _headlessSession = HeadlessUnitTestSession.StartNew(
                typeof(HeadlessAppBuilder),
                AvaloniaTestIsolationLevel.PerAssembly);
            return _headlessSession;
        }
    }

    private sealed class HeadlessAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<MorseRunner.App.App>()
                .UseSkia()
                .UseHeadless(
                    new AvaloniaHeadlessPlatformOptions
                    {
                        UseHeadlessDrawing = false,
                    });
        }
    }

    private sealed record ExpectedEnterResult(
        EnterSendMessageOutcome Outcome,
        IReadOnlyList<string> SentMessages,
        bool ClearEntry,
        string NormalizedMessages);

    private sealed class EnterEsmRuntime : IAsyncDisposable
    {
        private readonly RecordingMorseRunnerClient _client;
        private readonly MainWindowViewModel _viewModel;
        private readonly MainWindow _window;
        private TaskCompletionSource<EntryUiObservation>? _focusCompletion;
        private int _disposed;

        private EnterEsmRuntime(
            RecordingMorseRunnerClient client,
            MainWindowViewModel viewModel,
            MainWindow window)
        {
            _client = client;
            _viewModel = viewModel;
            _window = window;
            _viewModel.EntryFocusRequested += OnEntryFocusRequested;
        }

        public SessionSettings Settings =>
            _client.CreatedSettings
            ?? throw new InvalidOperationException(
                "The XPlat view model did not create a session.");

        public static async Task<EnterEsmRuntime> CreateAsync(
            EnterEsmScenarioInput input,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureHeadlessPlatform();
            var client = new RecordingMorseRunnerClient(
                InProcessMorseRunnerClient.CreateDefault(),
                cancellationToken);
            var viewModel = new MainWindowViewModel(client);
            var window = new MainWindow(viewModel);
            var runtime = new EnterEsmRuntime(client, viewModel, window);
            try
            {
                viewModel.SelectedContest = viewModel.Contests.Single(
                    option => StringComparer.Ordinal.Equals(
                        option.Id.Value,
                        input.ContestId));
                viewModel.SelectedRunMode = viewModel.RunModes.Single(
                    option => StringComparer.Ordinal.Equals(
                        option.Id.Value,
                        input.RunModeId));
                viewModel.StationCall = input.StationCall;
                viewModel.Seed = input.Seed;

                window.Show();
                TextBox callEntry = runtime.RequiredTextBox("CallEntryBox");
                if (!callEntry.IsFocused)
                {
                    throw new InvalidOperationException(
                        "The opened MainWindow did not focus the call entry.");
                }

                await viewModel.StartCommand.ExecuteAsync(null);
                cancellationToken.ThrowIfCancellationRequested();
                if (client.CreatedSettings is null
                    || !viewModel.EnterSendMessageCommand.CanExecute(null))
                {
                    throw new InvalidOperationException(
                        "The XPlat view model did not start the configured "
                        + "Enter/ESM session.");
                }

                return runtime;
            }
            catch
            {
                await runtime.DisposeAsync();
                throw;
            }
        }

        public async Task<string> ObserveAsync(
            int index,
            EnterEsmActionInput action,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var focusCompletion =
                new TaskCompletionSource<EntryUiObservation>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            if (Interlocked.CompareExchange(
                    ref _focusCompletion,
                    focusCompletion,
                    null) is not null)
            {
                throw new InvalidOperationException(
                    "A previous Enter/ESM focus observation is still active.");
            }

            _client.ResetEnterObservation();
            try
            {
                TextBox callEntry = RequiredTextBox("CallEntryBox");
                callEntry.Focus();
                callEntry.Text = action.Call;
                callEntry.CaretIndex = action.Call.Length;
                if (!StringComparer.Ordinal.Equals(
                        _viewModel.CallEntry,
                        action.Call))
                {
                    throw new InvalidOperationException(
                        "The real call TextBox did not update its binding.");
                }

                // This case certifies the semantic messages synchronously
                // accepted and queued by ESM. It does not advance simulation
                // blocks or claim renderer, envelope, or audio completion.
                _window.KeyPress(
                    Key.Enter,
                    RawInputModifiers.None,
                    PhysicalKey.Enter,
                    "\r");
                if (!_client.EnterInvocationObserved)
                {
                    throw new InvalidOperationException(
                        "The real MainWindow Enter route did not invoke ESM.");
                }

                CommandResult result =
                    await _client.WaitForEnterResultAsync(cancellationToken);
                string messages = NormalizeMessages(action, result);
                EntryUiObservation ui =
                    await focusCompletion.Task.WaitAsync(cancellationToken);
                string focus = NormalizeFocus(ui.FocusedControlName);
                (int questionStart, int questionLength) =
                    ObserveQuestionSelection(ui);
                EnterSendMessageResult enter = result.EnterSendMessage!;
                if (enter.FocusTarget
                        != ObserveFocusTarget(ui.FocusedControlName)
                    || enter.SelectQuestionMark != (questionLength == 1))
                {
                    throw new InvalidOperationException(
                        $"Enter/ESM result for action '{action.Id}' did not "
                        + "match the real MainWindow focus and selection.");
                }

                bool callRetained = StringComparer.Ordinal.Equals(
                    ui.Call,
                    action.Call);

                return $"action[{Format(index)}]"
                    + $"|id={action.Id}"
                    + $"|input={action.Call}"
                    + $"|messages={messages}"
                    + $"|focus={focus}"
                    + $"|question-start={Format(questionStart)}"
                    + $"|question-length={Format(questionLength)}"
                    + $"|call={ui.Call}"
                    + $"|rst={ui.Rst}"
                    + $"|exchange1={ui.Exchange1}"
                    + $"|call-retained={FormatBoolean(callRetained)}"
                    + $"|qso-count={Format(_viewModel.QsoCount)}";
            }
            finally
            {
                Interlocked.CompareExchange(
                    ref _focusCompletion,
                    null,
                    focusCompletion);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _viewModel.EntryFocusRequested -= OnEntryFocusRequested;
            await _viewModel.DisposeAsync();
            if (_window.IsVisible)
            {
                _window.Close();
            }
        }

        private void OnEntryFocusRequested(
            object? sender,
            EntryFocusRequestedEventArgs args)
        {
            TaskCompletionSource<EntryUiObservation>? completion =
                Interlocked.Exchange(ref _focusCompletion, null);
            if (completion is null)
            {
                return;
            }

            try
            {
                TextBox focused = _window.FocusManager?.GetFocusedElement()
                    as TextBox
                    ?? throw new InvalidOperationException(
                        "MainWindow did not focus a real entry TextBox.");
                completion.SetResult(
                    new EntryUiObservation(
                        focused.Name,
                        focused.Text ?? String.Empty,
                        focused.SelectionStart,
                        focused.SelectionEnd,
                        RequiredTextBox("CallEntryBox").Text ?? String.Empty,
                        RequiredTextBox("RstEntryBox").Text ?? String.Empty,
                        RequiredTextBox("Exchange1EntryBox").Text
                            ?? String.Empty));
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }

        private TextBox RequiredTextBox(string name)
        {
            return _window.FindControl<TextBox>(name)
                ?? throw new InvalidOperationException(
                    $"MainWindow is missing TextBox '{name}'.");
        }

        private static string NormalizeFocus(string? controlName)
        {
            return ObserveFocusTarget(controlName) switch
            {
                EntryFocusTarget.Call => "call",
                EntryFocusTarget.Rst => "rst",
                EntryFocusTarget.Exchange1 => "exchange1",
                EntryFocusTarget.Exchange2 => "exchange2",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(controlName)),
            };
        }

        private static EntryFocusTarget ObserveFocusTarget(
            string? controlName)
        {
            return controlName switch
            {
                "CallEntryBox" => EntryFocusTarget.Call,
                "RstEntryBox" => EntryFocusTarget.Rst,
                "Exchange1EntryBox" => EntryFocusTarget.Exchange1,
                "Exchange2EntryBox" => EntryFocusTarget.Exchange2,
                _ => throw new InvalidOperationException(
                    $"MainWindow focused unsupported control "
                    + $"'{controlName ?? "<none>"}'."),
            };
        }

        private static (int Start, int Length) ObserveQuestionSelection(
            EntryUiObservation observation)
        {
            int start = Math.Min(
                observation.SelectionStart,
                observation.SelectionEnd);
            int end = Math.Max(
                observation.SelectionStart,
                observation.SelectionEnd);
            if (start < 0
                || end < start
                || end > observation.FocusedText.Length)
            {
                throw new InvalidOperationException(
                    "The focused TextBox reported an invalid selection.");
            }

            int length = end - start;
            return length == 1 && observation.FocusedText[start] == '?'
                ? (start, length)
                : (-1, 0);
        }

        private static string FormatBoolean(bool value)
        {
            return value ? "true" : "false";
        }

        private sealed record EntryUiObservation(
            string? FocusedControlName,
            string FocusedText,
            int SelectionStart,
            int SelectionEnd,
            string Call,
            string Rst,
            string Exchange1);
    }

    private sealed class RecordingMorseRunnerClient : IMorseRunnerClient
    {
        private readonly IMorseRunnerClient _inner;
        private readonly CancellationToken _parityCancellationToken;
        private TaskCompletionSource<bool>? _enterInvoked;
        private TaskCompletionSource<CommandResult>? _enterCompleted;

        public RecordingMorseRunnerClient(
            IMorseRunnerClient inner,
            CancellationToken parityCancellationToken)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _parityCancellationToken = parityCancellationToken;
        }

        public SessionSettings? CreatedSettings { get; private set; }

        public CommandResult? LastEnterResult { get; private set; }

        public bool EnterInvocationObserved =>
            _enterInvoked?.Task.IsCompletedSuccessfully == true;

        public async Task<EngineInfo> GetEngineInfoAsync(
            CancellationToken cancellationToken)
        {
            using CancellationTokenSource linked =
                CreateLinkedTokenSource(cancellationToken);
            return await _inner.GetEngineInfoAsync(linked.Token);
        }

        public async Task<IReadOnlyList<AudioOutputDevice>>
            GetAudioOutputDevicesAsync(
                CancellationToken cancellationToken)
        {
            using CancellationTokenSource linked =
                CreateLinkedTokenSource(cancellationToken);
            return await _inner.GetAudioOutputDevicesAsync(linked.Token);
        }

        public async Task<SessionHandle> CreateSessionAsync(
            SessionSettings settings,
            CancellationToken cancellationToken)
        {
            CreatedSettings = settings;
            using CancellationTokenSource linked =
                CreateLinkedTokenSource(cancellationToken);
            return await _inner.CreateSessionAsync(settings, linked.Token);
        }

        public async Task<CommandResult> ExecuteAsync(
            SessionCommand command,
            CancellationToken cancellationToken)
        {
            bool isEnter = command is TriggerEnterSendMessageCommand;
            if (isEnter)
            {
                _enterInvoked?.TrySetResult(true);
            }

            using CancellationTokenSource linked =
                CreateLinkedTokenSource(cancellationToken);
            try
            {
                CommandResult result = await _inner.ExecuteAsync(
                    command,
                    linked.Token);
                if (isEnter)
                {
                    LastEnterResult = result;
                    _enterCompleted?.TrySetResult(result);
                }

                return result;
            }
            catch (Exception exception)
            {
                if (isEnter)
                {
                    _enterCompleted?.TrySetException(exception);
                }

                throw;
            }
        }

        public async Task<SessionSnapshot> GetSnapshotAsync(
            SessionId sessionId,
            CancellationToken cancellationToken)
        {
            using CancellationTokenSource linked =
                CreateLinkedTokenSource(cancellationToken);
            return await _inner.GetSnapshotAsync(sessionId, linked.Token);
        }

        public IAsyncEnumerable<SessionUpdate> SubscribeAsync(
            SessionSubscription subscription,
            CancellationToken cancellationToken)
        {
            return SubscribeLinkedAsync(subscription, cancellationToken);
        }

        public async Task<IReadOnlyList<Qso>> ListCompletedQsosAsync(
            SessionId sessionId,
            CancellationToken cancellationToken)
        {
            using CancellationTokenSource linked =
                CreateLinkedTokenSource(cancellationToken);
            return await _inner.ListCompletedQsosAsync(
                sessionId,
                linked.Token);
        }

        public async Task<SessionResult> GetResultAsync(
            SessionId sessionId,
            CancellationToken cancellationToken)
        {
            using CancellationTokenSource linked =
                CreateLinkedTokenSource(cancellationToken);
            return await _inner.GetResultAsync(sessionId, linked.Token);
        }

        public async Task CloseSessionAsync(
            SessionId sessionId,
            CancellationToken cancellationToken)
        {
            using CancellationTokenSource linked =
                CreateLinkedTokenSource(cancellationToken);
            await _inner.CloseSessionAsync(sessionId, linked.Token);
        }

        public ValueTask DisposeAsync()
        {
            return _inner.DisposeAsync();
        }

        public void ResetEnterObservation()
        {
            LastEnterResult = null;
            _enterInvoked = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _enterCompleted = new TaskCompletionSource<CommandResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task<CommandResult> WaitForEnterResultAsync(
            CancellationToken cancellationToken)
        {
            TaskCompletionSource<CommandResult> completion = _enterCompleted
                ?? throw new InvalidOperationException(
                    "Enter/ESM observation was not initialized.");
            return completion.Task.WaitAsync(cancellationToken);
        }

        private async IAsyncEnumerable<SessionUpdate> SubscribeLinkedAsync(
            SessionSubscription subscription,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using CancellationTokenSource linked =
                CreateLinkedTokenSource(cancellationToken);
            await foreach (SessionUpdate update in _inner.SubscribeAsync(
                    subscription,
                    linked.Token).ConfigureAwait(false))
            {
                yield return update;
            }
        }

        private CancellationTokenSource CreateLinkedTokenSource(
            CancellationToken cancellationToken)
        {
            return CancellationTokenSource.CreateLinkedTokenSource(
                _parityCancellationToken,
                cancellationToken);
        }
    }
}

internal sealed record EnterEsmScenarioInput(
    string Scenario,
    string ContestId,
    string RunModeId,
    string StationCall,
    int Seed,
    IReadOnlyList<EnterEsmActionInput> Actions)
{
    private const string ExpectedContestId = "scWpx";
    private const string ExpectedRunModeId = "rmPileup";
    private const string ExpectedStationCall = "W7SST";
    private const int ExpectedSeed = 12_345;
    private static readonly EnterEsmActionInput[] ExpectedActions =
    [
        new("empty", Reset: true, Call: String.Empty),
        new("short-partial", Reset: true, Call: "K1"),
        new("uncertain", Reset: true, Call: "K1A?"),
        new("corrected", Reset: false, Call: "K1ABC"),
        new("same-call-repeat", Reset: false, Call: "K1ABC"),
        new("complete", Reset: true, Call: "K2XYZ"),
    ];

    public static EnterEsmScenarioInput Parse(ParityScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        JsonElement input = scenario.Input;
        RequireExactProperties(
            input,
            [
                "actions",
                "contestId",
                "runModeId",
                "scenario",
                "seed",
                "stationCall",
            ],
            scenario.Id);

        string scenarioDiscriminator = RequireString(
            input,
            "scenario",
            scenario.Id);
        string contestId = RequireString(
            input,
            "contestId",
            scenario.Id);
        string runModeId = RequireString(
            input,
            "runModeId",
            scenario.Id);
        string stationCall = RequireString(
            input,
            "stationCall",
            scenario.Id);
        int seed = RequireInt32(input, "seed", scenario.Id);
        JsonElement actionsElement = input.GetProperty("actions");
        if (actionsElement.ValueKind != JsonValueKind.Array
            || actionsElement.GetArrayLength() != ExpectedActions.Length
            || scenario.ExpectedValues.Count != ExpectedActions.Length + 1)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' Enter/ESM row count "
                + "is invalid.");
        }

        var actions = ImmutableArray.CreateBuilder<EnterEsmActionInput>(
            ExpectedActions.Length);
        int index = 0;
        foreach (JsonElement actionElement in actionsElement.EnumerateArray())
        {
            RequireExactProperties(
                actionElement,
                ["call", "id", "reset"],
                scenario.Id);
            string id = RequireString(
                actionElement,
                "id",
                scenario.Id);
            bool reset = RequireBoolean(
                actionElement,
                "reset",
                scenario.Id);
            string call = RequireString(
                actionElement,
                "call",
                scenario.Id);
            var action = new EnterEsmActionInput(id, reset, call);
            if (action != ExpectedActions[index])
            {
                throw new InvalidDataException(
                    $"Parity case '{scenario.Id}' Enter/ESM action "
                    + $"{index.ToString(CultureInfo.InvariantCulture)} "
                    + "does not match the fixed vector.");
            }

            actions.Add(action);
            index++;
        }

        if (!StringComparer.Ordinal.Equals(
                scenarioDiscriminator,
                XPlatEnterEsmTarget.ParityId)
            || !StringComparer.Ordinal.Equals(
                contestId,
                ExpectedContestId)
            || !StringComparer.Ordinal.Equals(
                runModeId,
                ExpectedRunModeId)
            || !StringComparer.Ordinal.Equals(
                stationCall,
                ExpectedStationCall)
            || seed != ExpectedSeed)
        {
            throw new InvalidDataException(
                $"Parity case '{scenario.Id}' Enter/ESM input "
                + "does not match the fixed vector.");
        }

        return new EnterEsmScenarioInput(
            scenarioDiscriminator,
            contestId,
            runModeId,
            stationCall,
            seed,
            actions.MoveToImmutable());
    }

    private static string RequireString(
        JsonElement input,
        string propertyName,
        string scenarioId)
    {
        JsonElement value = input.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                $"Parity case '{scenarioId}' {propertyName} is not "
                + "a string.");
        }

        return value.GetString()
            ?? throw new InvalidDataException(
                $"Parity case '{scenarioId}' {propertyName} is null.");
    }

    private static int RequireInt32(
        JsonElement input,
        string propertyName,
        string scenarioId)
    {
        JsonElement value = input.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out int result))
        {
            throw new InvalidDataException(
                $"Parity case '{scenarioId}' {propertyName} is not "
                + "an Int32.");
        }

        return result;
    }

    private static bool RequireBoolean(
        JsonElement input,
        string propertyName,
        string scenarioId)
    {
        JsonElement value = input.GetProperty(propertyName);
        if (value.ValueKind is not (
                JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException(
                $"Parity case '{scenarioId}' {propertyName} is not "
                + "a Boolean.");
        }

        return value.GetBoolean();
    }

    private static void RequireExactProperties(
        JsonElement input,
        IReadOnlyList<string> expectedPropertyNames,
        string scenarioId)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"Parity case '{scenarioId}' input item is not an object.");
        }

        string[] actualPropertyNames = input
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!actualPropertyNames.SequenceEqual(
                expectedPropertyNames,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Parity case '{scenarioId}' input has unsupported fields.");
        }
    }
}

internal sealed record EnterEsmActionInput(
    string Id,
    bool Reset,
    string Call);
