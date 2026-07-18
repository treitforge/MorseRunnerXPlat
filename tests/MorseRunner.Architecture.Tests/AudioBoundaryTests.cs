using System.Xml.Linq;

namespace MorseRunner.Architecture.Tests;

public sealed class AudioBoundaryTests
{
    [Fact]
    public void AudioDependsOnEngineOwnedSinkContractWithoutReverseDependency()
    {
        string root = RepositoryRoot.Find();
        string engineProject = Path.Combine(
            root,
            "src",
            "MorseRunner.Engine",
            "MorseRunner.Engine.csproj");
        string audioProject = Path.Combine(
            root,
            "src",
            "MorseRunner.Audio",
            "MorseRunner.Audio.csproj");

        Assert.True(File.Exists(engineProject), $"Project not found: {engineProject}");
        Assert.True(File.Exists(audioProject), $"Project not found: {audioProject}");

        string[] engineReferences = ReadProjectReferences(engineProject);
        string[] audioReferences = ReadProjectReferences(audioProject);

        Assert.DoesNotContain("MorseRunner.Audio", engineReferences);
        Assert.Contains("MorseRunner.Engine", audioReferences);
        Assert.Contains("MorseRunner.Dsp", audioReferences);
    }

    private static string[] ReadProjectReferences(string projectPath)
    {
        XDocument project = XDocument.Load(projectPath);

        return project
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(
                path => Path.GetFileNameWithoutExtension(
                    path!.Replace('\\', Path.DirectorySeparatorChar)))
            .ToArray();
    }
}
