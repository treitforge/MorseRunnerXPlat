using System.Text.Json;
using System.Xml.Linq;

namespace MorseRunner.Architecture.Tests;

public sealed class BuildPolicyTests
{
    [Fact]
    public void SdkIsPinnedToDotnet10()
    {
        string root = RepositoryRoot.Find();
        using FileStream stream = File.OpenRead(Path.Combine(root, "global.json"));
        using JsonDocument document = JsonDocument.Parse(stream);

        string? version = document.RootElement
            .GetProperty("sdk")
            .GetProperty("version")
            .GetString();

        Assert.NotNull(version);
        Assert.StartsWith("10.0.", version, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryBuildPolicyEnablesRequiredCompilerGuards()
    {
        string root = RepositoryRoot.Find();
        XDocument props = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
        XElement project = Assert.Single(props.Elements("Project"));

        Assert.Equal("enable", FindProperty(project, "Nullable"));
        Assert.Equal("enable", FindProperty(project, "ImplicitUsings"));
        Assert.Equal("true", FindProperty(project, "Deterministic"));
        Assert.Equal("true", FindProperty(project, "TreatWarningsAsErrors"));
    }

    private static string? FindProperty(XElement project, string name) =>
        project.Descendants(name).SingleOrDefault()?.Value;
}
