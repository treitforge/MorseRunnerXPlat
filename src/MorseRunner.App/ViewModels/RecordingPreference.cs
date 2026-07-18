using MorseRunner.Infrastructure;

namespace MorseRunner.App.ViewModels;

public sealed class RecordingPreference
{
    private readonly ApplicationPaths _paths;

    public RecordingPreference(ApplicationPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public bool Enabled { get; set; }

    public string? LastPath { get; private set; }

    public string? CreatePath()
    {
        if (!Enabled)
        {
            return null;
        }

        Directory.CreateDirectory(_paths.Recordings);
        string timestamp = DateTimeOffset.Now.ToString(
            "yyyyMMdd-HHmmss-fff",
            System.Globalization.CultureInfo.InvariantCulture);
        LastPath = Path.Combine(
            _paths.Recordings,
            $"MorseRunner-{timestamp}.wav");
        return LastPath;
    }
}
