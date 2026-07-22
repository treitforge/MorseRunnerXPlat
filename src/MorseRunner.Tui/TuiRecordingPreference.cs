using System.Globalization;
using MorseRunner.Infrastructure;

namespace MorseRunner.Tui;

public sealed class TuiRecordingPreference(ApplicationPaths paths)
{
    private readonly ApplicationPaths _paths =
        paths ?? throw new ArgumentNullException(nameof(paths));

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
            CultureInfo.InvariantCulture);
        LastPath = Path.Combine(
            _paths.Recordings,
            $"MorseRunner-{timestamp}.wav");
        return LastPath;
    }

    public string? DiscoverLatest()
    {
        if (!Directory.Exists(_paths.Recordings))
        {
            return LastPath;
        }

        LastPath = Directory
            .EnumerateFiles(_paths.Recordings, "*.wav")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        return LastPath;
    }
}
