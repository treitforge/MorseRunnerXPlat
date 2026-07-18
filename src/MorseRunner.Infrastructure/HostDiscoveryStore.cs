using System.Text.Json;

namespace MorseRunner.Infrastructure;

public sealed record HostDiscoveryRecord(
    string Endpoint,
    int ProcessId,
    Guid EngineEpoch,
    string ContractVersion,
    string AuthenticationToken,
    DateTimeOffset PublishedAt);

public sealed class HostDiscoveryStore(ApplicationPaths paths)
{
    private const string FileName = "engine-host.json";

    public string Path => System.IO.Path.Combine(paths.Runtime, FileName);

    public async Task PublishAsync(
        HostDiscoveryRecord record,
        CancellationToken cancellationToken)
    {
        paths.EnsureWritableDirectories();
        string temporary = Path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = new(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    record,
                    cancellationToken: cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    temporary,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(temporary, Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public async Task<HostDiscoveryRecord?> ReadAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(Path);
        return await JsonSerializer.DeserializeAsync<HostDiscoveryRecord>(
            stream,
            cancellationToken: cancellationToken);
    }

    public void RemoveIfOwned(int processId, string authenticationToken)
    {
        if (!File.Exists(Path))
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(Path);
            HostDiscoveryRecord? record =
                JsonSerializer.Deserialize<HostDiscoveryRecord>(json);
            if (record?.ProcessId == processId
                && String.Equals(
                    record.AuthenticationToken,
                    authenticationToken,
                    StringComparison.Ordinal))
            {
                File.Delete(Path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (JsonException)
        {
        }
    }
}
