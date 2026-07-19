using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using MorseRunner.Infrastructure;

namespace MorseRunner.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (TryGetOption(args, "--startup-smoke", out string outputPath))
            {
                BuildAvaloniaApp().SetupWithoutStarting();
                string fullPath = Path.GetFullPath(outputPath);
                string? directory = Path.GetDirectoryName(fullPath);
                if (!String.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(
                    fullPath,
                    JsonSerializer.Serialize(
                        new
                        {
                            Started = true,
                            Platform = RuntimeInformation.OSDescription,
                            Architecture = RuntimeInformation
                                .ProcessArchitecture
                                .ToString(),
                            Framework = RuntimeInformation.FrameworkDescription,
                            ApplicationType = Application.Current?
                                .GetType()
                                .FullName,
                        }));
                return 0;
            }

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            _ = new CrashDiagnosticStore(new ApplicationPaths()).Write(exception);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }

    private static bool TryGetOption(
        string[] arguments,
        string option,
        out string value)
    {
        for (int index = 0; index < arguments.Length - 1; index++)
        {
            if (String.Equals(
                arguments[index],
                option,
                StringComparison.OrdinalIgnoreCase)
                && !String.IsNullOrWhiteSpace(arguments[index + 1]))
            {
                value = arguments[index + 1];
                return true;
            }
        }

        value = string.Empty;
        return false;
    }
}
