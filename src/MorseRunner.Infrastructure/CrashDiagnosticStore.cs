using System.Runtime.InteropServices;
using System.Text.Json;

namespace MorseRunner.Infrastructure;

public sealed record CrashDiagnostic(
    DateTimeOffset Timestamp,
    int ProcessId,
    string ExceptionType,
    string Message,
    string? StackTrace,
    string OperatingSystem,
    string Runtime);

public sealed class CrashDiagnosticStore(ApplicationPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public string DirectoryPath => Path.Combine(paths.Root, "diagnostics");

    public string Write(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Directory.CreateDirectory(DirectoryPath);
        string path = Path.Combine(
            DirectoryPath,
            $"crash-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}.json");
        var diagnostic = new CrashDiagnostic(
            DateTimeOffset.UtcNow,
            Environment.ProcessId,
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            exception.StackTrace,
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription);
        string json = JsonSerializer.Serialize(diagnostic, JsonOptions);
        File.WriteAllText(path, json);
        return path;
    }
}
