using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace MorseRunner.LegacyParity.Tests;

internal sealed record ParityRunContext(
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("processArchitecture")]
    string ProcessArchitecture,
    [property: JsonPropertyName("runtimeIdentifier")]
    string RuntimeIdentifier,
    [property: JsonPropertyName("framework")] string Framework,
    [property: JsonPropertyName("xplat")]
    ParityRepositoryRunContext XPlat,
    [property: JsonPropertyName("legacy")]
    ParityLegacyRunContext? Legacy);

internal sealed record ParityRepositoryRunContext(
    [property: JsonPropertyName("revision")] string Revision,
    [property: JsonPropertyName("tree")] string Tree,
    [property: JsonPropertyName("clean")] bool Clean);

internal sealed record ParityLegacyRunContext(
    [property: JsonPropertyName("revision")] string Revision,
    [property: JsonPropertyName("tree")] string Tree,
    [property: JsonPropertyName("clean")] bool Clean);

internal sealed record ParityRunEnvironment(
    string Platform,
    string ProcessArchitecture,
    string RuntimeIdentifier,
    string Framework)
{
    public static ParityRunEnvironment Capture()
    {
        string platform = OperatingSystem.IsWindows()
            ? "windows"
            : OperatingSystem.IsLinux()
                ? "linux"
                : OperatingSystem.IsMacOS()
                    ? "macos"
                    : throw new PlatformNotSupportedException(
                        "Parity certification supports Windows, Linux, "
                        + "and macOS.");
        return new ParityRunEnvironment(
            platform,
            RuntimeInformation.ProcessArchitecture
                .ToString()
                .ToLowerInvariant(),
            RuntimeInformation.RuntimeIdentifier,
            RuntimeInformation.FrameworkDescription);
    }
}

internal static class ParityRunContextProvider
{
    public static async Task<ParityRunContext> CaptureAsync(
        ParityTargetKind target,
        CancellationToken cancellationToken)
    {
        ParityRunEnvironment environment =
            ParityRunEnvironment.Capture();
        GitLegacyRepositoryInspector inspector = new();
        LegacyRepositoryInspection xplat =
            await inspector.InspectAsync(
                RepositoryPaths.Root,
                cancellationToken);
        EnsureInspectionSucceeded("XPlat", xplat);

        ParityRepositoryRunContext xplatContext = new(
            xplat.Revision!,
            xplat.Tree!,
            xplat.Clean);
        if (target == ParityTargetKind.XPlat)
        {
            return new ParityRunContext(
                environment.Platform,
                environment.ProcessArchitecture,
                environment.RuntimeIdentifier,
                environment.Framework,
                xplatContext,
                null);
        }

        string legacyRoot = RequireEnvironment(
            "MORSE_RUNNER_LEGACY_ROOT");
        string registryPath = RequireEnvironment(
            "MORSE_RUNNER_LEGACY_ORACLE_REGISTRY");
        string registrySha256 = RequireEnvironment(
            "MORSE_RUNNER_LEGACY_ORACLE_REGISTRY_SHA256");
        if (!ParityCanonicalJson.IsLowercaseSha256(
                registrySha256))
        {
            throw new InvalidOperationException(
                "Required legacy oracle registry SHA-256 is invalid.");
        }

        if (!File.Exists(Path.GetFullPath(registryPath)))
        {
            throw new InvalidOperationException(
                "Required legacy oracle registry does not exist.");
        }

        string actualRegistrySha256 =
            ParityCanonicalJson.ComputeSha256(
                await File.ReadAllBytesAsync(
                    Path.GetFullPath(registryPath),
                    cancellationToken));
        if (!StringComparer.Ordinal.Equals(
                actualRegistrySha256,
                registrySha256))
        {
            throw new InvalidOperationException(
                "Required legacy oracle registry SHA-256 does not match.");
        }

        LegacyRepositoryInspection legacy =
            await inspector.InspectAsync(
                Path.GetFullPath(legacyRoot),
                cancellationToken);
        EnsureInspectionSucceeded("Legacy", legacy);

        return new ParityRunContext(
            environment.Platform,
            environment.ProcessArchitecture,
            environment.RuntimeIdentifier,
            environment.Framework,
            xplatContext,
            new ParityLegacyRunContext(
                legacy.Revision!,
                legacy.Tree!,
                legacy.Clean));
    }

    private static void EnsureInspectionSucceeded(
        string name,
        LegacyRepositoryInspection inspection)
    {
        if (!String.IsNullOrWhiteSpace(inspection.Failure)
            || String.IsNullOrWhiteSpace(inspection.Revision)
            || String.IsNullOrWhiteSpace(inspection.Tree))
        {
            throw new InvalidOperationException(
                $"{name} run-context inspection failed: "
                + inspection.Failure);
        }
    }

    private static string RequireEnvironment(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (String.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Required parity environment variable '{name}' is missing.");
        }

        return value;
    }

}
