using MorseRunner.Client;
using MorseRunner.Grpc;
using MorseRunner.Infrastructure;
using MorseRunner.Tui;

if (args.Contains("--help", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine(
        """
        Usage: MorseRunner.Tui [--hosted] [--no-audio] [--no-color] [--snapshot]
                               [--snapshot-view <operator|settings|results|diagnostics|help>]

          --hosted    Connect to the locally discovered MorseRunner engine host.
          --no-audio  Run locally with automatic timing and a null audio sink.
          --no-color  Disable color while retaining terminal cursor control.
          --snapshot  Render one non-interactive frame and exit.
          --snapshot-view
                      Select the view rendered by --snapshot.

        The default is an in-process engine with physical audio.
        """);
    return 0;
}

bool hosted = args.Contains("--hosted", StringComparer.OrdinalIgnoreCase);
bool noAudio = args.Contains("--no-audio", StringComparer.OrdinalIgnoreCase);
bool noColor = args.Contains("--no-color", StringComparer.OrdinalIgnoreCase);
bool snapshot = args.Contains("--snapshot", StringComparer.OrdinalIgnoreCase);
string snapshotView = GetOptionValue(args, "--snapshot-view") ?? "operator";
var paths = new ApplicationPaths();
paths.EnsureWritableDirectories();
var recording = new TuiRecordingPreference(paths);

await using IMorseRunnerClient? client = hosted
    ? await CreateHostedClientAsync()
    : noAudio
        ? InProcessMorseRunnerClient.CreateWithAutomaticNullAudio()
        : InProcessMorseRunnerClient.CreateWithPhysicalAudio(
            recordingPathProvider: recording.CreatePath);
if (client is null)
{
    Console.Error.WriteLine(
        "No running engine host was discovered. Start MorseRunner.EngineHost first.");
    return 1;
}

using var application = new TuiApplication(
    client,
    hosted,
    paths,
    recording,
    forceNoColor: noColor);
await application.InitializeAsync(CancellationToken.None);
if (snapshot)
{
    application.State.View = snapshotView.ToLowerInvariant() switch
    {
        "operator" => TuiView.Operator,
        "settings" => TuiView.Settings,
        "results" => TuiView.Results,
        "diagnostics" => TuiView.Diagnostics,
        "help" => TuiView.Help,
        _ => throw new ArgumentException(
            $"Unknown snapshot view '{snapshotView}'."),
    };
    Console.Write(
        TuiRenderer.Render(
            application.State,
            width: 100,
            height: 28));
    return 0;
}

if (Console.IsInputRedirected || Console.IsOutputRedirected)
{
    Console.Error.WriteLine(
        "Interactive TUI requires a terminal. Use --snapshot for redirected output.");
    return 2;
}

return await application.RunAsync(CancellationToken.None);

static async Task<IMorseRunnerClient?> CreateHostedClientAsync()
{
    var store = new HostDiscoveryStore(new ApplicationPaths());
    HostDiscoveryRecord? discovery = await store.ReadAsync(CancellationToken.None);
    if (discovery is null)
    {
        return null;
    }

    if (!IsProcessRunning(discovery.ProcessId))
    {
        store.RemoveIfOwned(
            discovery.ProcessId,
            discovery.AuthenticationToken);
        return null;
    }

    return new GrpcMorseRunnerClient(
        new(discovery.Endpoint),
        discovery.AuthenticationToken,
        new("tui"));
}

static bool IsProcessRunning(int processId)
{
    try
    {
        using System.Diagnostics.Process process =
            System.Diagnostics.Process.GetProcessById(processId);
        return !process.HasExited;
    }
    catch (ArgumentException)
    {
        return false;
    }
}

static string? GetOptionValue(string[] arguments, string option)
{
    for (int index = 0; index < arguments.Length - 1; index++)
    {
        if (String.Equals(
            arguments[index],
            option,
            StringComparison.OrdinalIgnoreCase))
        {
            return arguments[index + 1];
        }
    }

    return null;
}
