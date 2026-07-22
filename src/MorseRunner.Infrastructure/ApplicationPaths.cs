namespace MorseRunner.Infrastructure;

public sealed class ApplicationPaths
{
    public ApplicationPaths(string? rootOverride = null)
    {
        Root = Path.GetFullPath(
            rootOverride
                ?? Environment.GetEnvironmentVariable(
                    "MORSE_RUNNER_DATA_ROOT")
                ?? Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "MorseRunnerXPlat"));
    }

    public string Root { get; }

    public string Settings => Path.Combine(Root, "settings");

    public string IniSettingsImport => Path.Combine(Root, "MorseRunner.ini");

    public string Results => Path.Combine(Root, "results");

    public string Recordings => Path.Combine(Root, "recordings");

    public string Cache => Path.Combine(Root, "cache");

    public string Runtime => Path.Combine(Root, "runtime");

    public string Temporary => Path.Combine(Root, "temporary");

    public void EnsureWritableDirectories()
    {
        Directory.CreateDirectory(Settings);
        Directory.CreateDirectory(Results);
        Directory.CreateDirectory(Recordings);
        Directory.CreateDirectory(Cache);
        Directory.CreateDirectory(Runtime);
        Directory.CreateDirectory(Temporary);
    }
}
