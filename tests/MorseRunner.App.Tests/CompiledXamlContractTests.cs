namespace MorseRunner.App.Tests;

public sealed class CompiledXamlContractTests
{
    [Fact]
    public void MainWindowRetainsKeyboardFirstSemanticControls()
    {
        string xaml = File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                "src",
                "MorseRunner.App",
                "Views",
                "MainWindow.axaml"));

        Assert.Contains(
            "x:DataType=\"viewModels:MainWindowViewModel\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CallEntryBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"QsoLog\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Gesture=\"F1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Gesture=\"F12\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Gesture=\"Shift+F9\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Gesture=\"Ctrl+F9\"", xaml, StringComparison.Ordinal);
        Assert.Equal(28, CountOccurrences(xaml, "<KeyBinding"));
    }

    [Fact]
    public void ScoreWindowHasCompiledBindingsAndCloseAction()
    {
        string xaml = File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                "src",
                "MorseRunner.App",
                "Views",
                "ScoreWindow.axaml"));

        Assert.Contains(
            "x:DataType=\"viewModels:ScoreWindowViewModel\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CloseButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"CloseClick\"", xaml, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string token)
    {
        int count = 0;
        int index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
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

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
