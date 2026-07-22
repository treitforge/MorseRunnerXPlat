using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
        "RECORDING-PROBE" => await RunRecordingProbeAsync(args[1..]),
        "SCENARIO" => await RunHeadlessScenarioAsync(),
        "HOST-INFO" => await ShowHostInfoAsync(),
        "HOSTED-SCENARIO" => await RunHostedScenarioAsync(),
        _ => PrintUsage(),
    };

static int PrintUsage()
{
    Console.Error.WriteLine(
        "Usage: MorseRunner.Cli <audio-devices|audio-probe [--seconds N]|scenario"
        + "|recording-probe --output PATH|host-info|hosted-scenario>\n"
        + "audio-probe options: --device NAME --recover-device NAME "
        + "--record PATH");
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
    string? deviceName = null;
    string? recoveryDeviceName = null;
    string? recordingPath = null;
    for (int index = 0; index < arguments.Length; index += 2)
    {
        if (index + 1 >= arguments.Length)
        {
            return PrintAudioProbeUsage();
        }

        string option = arguments[index];
        string value = arguments[index + 1];
        if (option.Equals(
                "--seconds",
                StringComparison.OrdinalIgnoreCase)
            && Double.TryParse(
                value,
                CultureInfo.InvariantCulture,
                out double parsedSeconds)
            && parsedSeconds is > 0d and <= 300d)
        {
            durationSeconds = parsedSeconds;
        }
        else if (option.Equals(
            "--device",
            StringComparison.OrdinalIgnoreCase)
            && !String.IsNullOrWhiteSpace(value))
        {
            deviceName = value;
        }
        else if (option.Equals(
            "--recover-device",
            StringComparison.OrdinalIgnoreCase)
            && !String.IsNullOrWhiteSpace(value))
        {
            recoveryDeviceName = value;
        }
        else if (option.Equals(
            "--record",
            StringComparison.OrdinalIgnoreCase)
            && !String.IsNullOrWhiteSpace(value))
        {
            recordingPath = Path.GetFullPath(value);
            string? directory = Path.GetDirectoryName(recordingPath);
            if (!String.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
        else
        {
            return PrintAudioProbeUsage();
        }
    }

    await using InProcessMorseRunnerClient client =
        InProcessMorseRunnerClient.CreateWithPhysicalAudio(
            deviceName,
            recordingPathProvider: () => recordingPath);
    IReadOnlyList<AudioOutputDevice> devices =
        await client.GetAudioOutputDevicesAsync(CancellationToken.None);
    string? effectiveDeviceName = deviceName
        ?? devices.FirstOrDefault(device => device.IsDefault)?.Name
        ?? (devices.Count > 0 ? devices[0].Name : null);
    SessionHandle handle = await client.CreateSessionAsync(
        SessionSettings.CreateDefault(seed: 12345),
        CancellationToken.None);
    ClientId clientId = new("cli-audio-probe");
    CommandResult start = await client.ExecuteAsync(
        new StartSessionCommand(
            RequestId.New(),
            handle.SessionId,
            clientId),
        CancellationToken.None);

    bool recoveryAccepted = true;
    await Task.Delay(TimeSpan.FromSeconds(durationSeconds / 2d));
    if (!String.IsNullOrWhiteSpace(recoveryDeviceName))
    {
        CommandResult pause = await client.ExecuteAsync(
            new PauseSessionCommand(
                RequestId.New(),
                handle.SessionId,
                clientId),
            CancellationToken.None);
        CommandResult recover = await client.ExecuteAsync(
            new RecoverAudioCommand(
                RequestId.New(),
                handle.SessionId,
                clientId,
                recoveryDeviceName),
            CancellationToken.None);
        CommandResult resume = await client.ExecuteAsync(
            new ResumeSessionCommand(
                RequestId.New(),
                handle.SessionId,
                clientId),
            CancellationToken.None);
        recoveryAccepted =
            pause.Accepted && recover.Accepted && resume.Accepted;
    }

    await Task.Delay(TimeSpan.FromSeconds(durationSeconds / 2d));

    SessionSnapshot snapshot = await client.GetSnapshotAsync(
        handle.SessionId,
        CancellationToken.None);
    CommandResult stop = await client.ExecuteAsync(
        new StopSessionCommand(
            RequestId.New(),
            handle.SessionId,
            clientId),
        CancellationToken.None);
    await client.CloseSessionAsync(handle.SessionId, CancellationToken.None);
    long recordingLength = recordingPath is not null
        && File.Exists(recordingPath)
            ? new FileInfo(recordingPath).Length
            : 0;
    bool passed = start.Accepted
        && stop.Accepted
        && recoveryAccepted
        && snapshot.AudioOutputHealthy
        && snapshot.AudioUnderrunCount == 0
        && snapshot.AudioDroppedBlockCount == 0
        && (recordingPath is null || recordingLength > 44);
    Console.WriteLine(
        JsonSerializer.Serialize(
            new
            {
                Platform = RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Passed = passed,
                DeviceCount = devices.Count,
                RequestedDevice = effectiveDeviceName ?? "default",
                RecoveryAttempted =
                    !String.IsNullOrWhiteSpace(recoveryDeviceName),
                RecoveryDevice = recoveryDeviceName,
                DeviceChanged = !String.IsNullOrWhiteSpace(
                        recoveryDeviceName)
                    && !String.Equals(
                        effectiveDeviceName,
                        recoveryDeviceName,
                        StringComparison.Ordinal),
                RecoveryAccepted = recoveryAccepted,
                snapshot.AudioOutputHealthy,
                snapshot.AudioQueuedBlocks,
                snapshot.AudioUnderrunCount,
                snapshot.AudioDroppedBlockCount,
                ElapsedMilliseconds = snapshot.ElapsedSimulationTime
                    .TotalMilliseconds
                    .ToString("F3", CultureInfo.InvariantCulture),
                RecordingPath = recordingPath,
                RecordingLength = recordingLength,
            }));
    return passed ? 0 : 1;
}

static int PrintAudioProbeUsage()
{
    Console.Error.WriteLine(
        "audio-probe accepts --seconds N, --device NAME, "
        + "--recover-device NAME, and --record PATH.");
    return 2;
}

static async Task<int> RunRecordingProbeAsync(string[] arguments)
{
    if (arguments.Length != 2
        || !arguments[0].Equals(
            "--output",
            StringComparison.OrdinalIgnoreCase)
        || String.IsNullOrWhiteSpace(arguments[1]))
    {
        Console.Error.WriteLine(
            "recording-probe requires --output followed by a WAV path.");
        return 2;
    }

    string path = Path.GetFullPath(arguments[1]);
    string? directory = Path.GetDirectoryName(path);
    if (!String.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }

    await using (InProcessMorseRunnerClient client =
        InProcessMorseRunnerClient.CreateWithBufferedWavAudio(path))
    {
        SessionHandle handle = await client.CreateSessionAsync(
            SessionSettings.CreateDefault(seed: 12345),
            CancellationToken.None);
        ClientId clientId = new("cli-recording-probe");
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
                BlockCount: 32),
            CancellationToken.None);
        await client.ExecuteAsync(
            new StopSessionCommand(
                RequestId.New(),
                handle.SessionId,
                clientId),
            CancellationToken.None);
        await client.CloseSessionAsync(
            handle.SessionId,
            CancellationToken.None);
    }

    byte[] content = await File.ReadAllBytesAsync(
        path,
        CancellationToken.None);
    bool riff = content.Length >= 44
        && content.AsSpan(0, 4).SequenceEqual("RIFF"u8)
        && content.AsSpan(8, 4).SequenceEqual("WAVE"u8)
        && content.AsSpan(36, 4).SequenceEqual("data"u8);
    int sampleRate = riff
        ? BinaryPrimitives.ReadInt32LittleEndian(content.AsSpan(24, 4))
        : 0;
    short channels = riff
        ? BinaryPrimitives.ReadInt16LittleEndian(content.AsSpan(22, 2))
        : (short)0;
    short bitsPerSample = riff
        ? BinaryPrimitives.ReadInt16LittleEndian(content.AsSpan(34, 2))
        : (short)0;
    int dataLength = riff
        ? BinaryPrimitives.ReadInt32LittleEndian(content.AsSpan(40, 4))
        : 0;
    bool valid = riff
        && sampleRate == SimulationAudioProfile.SampleRate
        && channels == 1
        && bitsPerSample == 16
        && dataLength > 0
        && content.Length == 44 + dataLength;
    Console.WriteLine(
        JsonSerializer.Serialize(
            new
            {
                Platform = RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Path = path,
                Valid = valid,
                SampleRate = sampleRate,
                Channels = channels,
                BitsPerSample = bitsPerSample,
                DataLength = dataLength,
                FileLength = content.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(content)),
            }));
    return valid ? 0 : 1;
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
