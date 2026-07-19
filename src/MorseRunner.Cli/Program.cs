using System.Globalization;
using System.Text.Json;
using MorseRunner.Client;
using MorseRunner.Domain;
using MorseRunner.Grpc;
using MorseRunner.Infrastructure;

return args.Length == 0
    ? PrintUsage()
    : args[0].ToUpperInvariant() switch
    {
        "AUDIO-DEVICES" => await ListAudioDevicesAsync(),
        "AUDIO-PROBE" => await RunAudioProbeAsync(args[1..]),
        "SCENARIO" => await RunHeadlessScenarioAsync(),
        "HOST-INFO" => await ShowHostInfoAsync(),
        "HOSTED-SCENARIO" => await RunHostedScenarioAsync(),
        _ => PrintUsage(),
    };

static int PrintUsage()
{
    Console.Error.WriteLine(
        "Usage: MorseRunner.Cli <audio-devices|audio-probe [--seconds N]|scenario"
        + "|host-info|hosted-scenario>");
    return 2;
}

static async Task<int> ListAudioDevicesAsync()
{
    await using InProcessMorseRunnerClient client =
        InProcessMorseRunnerClient.CreateDefault();
    IReadOnlyList<AudioOutputDevice> devices =
        await client.GetAudioOutputDevicesAsync(CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(devices));
    return 0;
}

static async Task<int> RunAudioProbeAsync(string[] arguments)
{
    double durationSeconds = 0.75d;
    if (arguments.Length == 2
        && arguments[0].Equals("--seconds", StringComparison.OrdinalIgnoreCase)
        && double.TryParse(
            arguments[1],
            CultureInfo.InvariantCulture,
            out double parsedSeconds)
        && parsedSeconds is > 0d and <= 300d)
    {
        durationSeconds = parsedSeconds;
    }
    else if (arguments.Length != 0)
    {
        Console.Error.WriteLine(
            "audio-probe accepts --seconds greater than 0 and at most 300.");
        return 2;
    }

    await using InProcessMorseRunnerClient client =
        InProcessMorseRunnerClient.CreateWithPhysicalAudio();
    SessionHandle handle = await client.CreateSessionAsync(
        SessionSettings.CreateDefault(seed: 12345),
        CancellationToken.None);
    ClientId clientId = new("cli-audio-probe");
    await client.ExecuteAsync(
        new StartSessionCommand(
            RequestId.New(),
            handle.SessionId,
            clientId),
        CancellationToken.None);

    await Task.Delay(TimeSpan.FromSeconds(durationSeconds));

    SessionSnapshot snapshot = await client.GetSnapshotAsync(
        handle.SessionId,
        CancellationToken.None);
    Console.WriteLine(
        JsonSerializer.Serialize(
            new
            {
                snapshot.AudioOutputHealthy,
                snapshot.AudioQueuedBlocks,
                snapshot.AudioUnderrunCount,
                snapshot.AudioDroppedBlockCount,
                ElapsedMilliseconds = snapshot.ElapsedSimulationTime
                    .TotalMilliseconds
                    .ToString("F3", CultureInfo.InvariantCulture),
            }));

    await client.ExecuteAsync(
        new StopSessionCommand(
            RequestId.New(),
            handle.SessionId,
            clientId),
        CancellationToken.None);
    return snapshot.AudioOutputHealthy
        && snapshot.AudioUnderrunCount == 0
        && snapshot.AudioDroppedBlockCount == 0
        ? 0
        : 1;
}

static async Task<int> RunHeadlessScenarioAsync()
{
    await using InProcessMorseRunnerClient client =
        InProcessMorseRunnerClient.CreateDefault();
    SessionHandle handle = await client.CreateSessionAsync(
        SessionSettings.CreateDefault(seed: 12345),
        CancellationToken.None);
    ClientId clientId = new("cli-scenario");
    await client.ExecuteAsync(
        new StartSessionCommand(RequestId.New(), handle.SessionId, clientId),
        CancellationToken.None);
    await client.ExecuteAsync(
        new AdvanceSimulationCommand(
            RequestId.New(),
            handle.SessionId,
            clientId,
            BlockCount: 8),
        CancellationToken.None);
    SessionSnapshot snapshot = await client.GetSnapshotAsync(
        handle.SessionId,
        CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(snapshot));
    return 0;
}

static async Task<int> ShowHostInfoAsync()
{
    HostDiscoveryRecord? discovery = await ReadDiscoveryAsync();
    if (discovery is null)
    {
        Console.Error.WriteLine("No running engine host was discovered.");
        return 1;
    }

    await using var client = new GrpcMorseRunnerClient(
        new(discovery.Endpoint),
        discovery.AuthenticationToken,
        new("cli-host-info"));
    EngineInfo info = await client.GetEngineInfoAsync(CancellationToken.None);
    Console.WriteLine(
        JsonSerializer.Serialize(
            new
            {
                discovery.Endpoint,
                discovery.ProcessId,
                info.EngineId,
                info.EngineEpoch,
                info.SemanticVersion,
                info.Capabilities,
            }));
    return 0;
}

static async Task<int> RunHostedScenarioAsync()
{
    HostDiscoveryRecord? discovery = await ReadDiscoveryAsync();
    if (discovery is null)
    {
        Console.Error.WriteLine("No running engine host was discovered.");
        return 1;
    }

    ClientId clientId = new("cli-hosted-scenario");
    await using var client = new GrpcMorseRunnerClient(
        new(discovery.Endpoint),
        discovery.AuthenticationToken,
        clientId);
    SessionHandle handle = await client.CreateSessionAsync(
        SessionSettings.CreateDefault(seed: 12345),
        CancellationToken.None);
    await client.ExecuteAsync(
        new StartSessionCommand(
            RequestId.New(),
            handle.SessionId,
            clientId),
        CancellationToken.None);
    await client.ExecuteAsync(
        new AdvanceSimulationCommand(
            RequestId.New(),
            handle.SessionId,
            clientId,
            BlockCount: 8),
        CancellationToken.None);
    SessionSnapshot snapshot = await client.GetSnapshotAsync(
        handle.SessionId,
        CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(snapshot));
    await client.CloseSessionAsync(handle.SessionId, CancellationToken.None);
    return 0;
}

static async Task<HostDiscoveryRecord?> ReadDiscoveryAsync()
{
    var store = new HostDiscoveryStore(new ApplicationPaths());
    HostDiscoveryRecord? record = await store.ReadAsync(CancellationToken.None);
    if (record is not null && !IsProcessRunning(record.ProcessId))
    {
        store.RemoveIfOwned(record.ProcessId, record.AuthenticationToken);
        return null;
    }

    return record;
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
