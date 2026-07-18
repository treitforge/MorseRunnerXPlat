namespace MorseRunner.LegacyParity.Tests;

internal static class RepositoryPaths
{
    public static string Root { get; } = FindRoot();

    public static string LegacyRoot
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable(
                "MORSE_RUNNER_LEGACY_ROOT");

            return string.IsNullOrWhiteSpace(configured)
                ? Path.GetFullPath(Path.Combine(Root, "..", "MorseRunner"))
                : Path.GetFullPath(configured);
        }
    }

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MorseRunnerXPlat.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
