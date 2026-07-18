using System.Xml.Linq;

namespace MorseRunner.Architecture.Tests;

public sealed class ProjectDependencyTests
{
    private static readonly Dictionary<string, IReadOnlySet<string>> AllowedReferences =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["MorseRunner.Domain"] = new HashSet<string>(StringComparer.Ordinal),
            ["MorseRunner.Dsp"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "MorseRunner.Domain",
            },
            ["MorseRunner.Engine"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "MorseRunner.Domain",
                "MorseRunner.Dsp",
            },
            ["MorseRunner.Infrastructure"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "MorseRunner.Domain",
            },
            ["MorseRunner.Audio"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "MorseRunner.Dsp",
                "MorseRunner.Engine",
            },
            ["MorseRunner.Client"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "MorseRunner.Audio",
                "MorseRunner.Domain",
                "MorseRunner.Engine",
            },
            ["MorseRunner.Contracts"] =
                new HashSet<string>(StringComparer.Ordinal),
            ["MorseRunner.Grpc"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "MorseRunner.Client",
                "MorseRunner.Contracts",
                "MorseRunner.Domain",
                "MorseRunner.Infrastructure",
            },
            ["MorseRunner.EngineHost"] =
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "MorseRunner.Audio",
                    "MorseRunner.Client",
                    "MorseRunner.Engine",
                    "MorseRunner.Grpc",
                    "MorseRunner.Infrastructure",
                },
            ["MorseRunner.Cli"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "MorseRunner.Client",
                "MorseRunner.Domain",
                "MorseRunner.Grpc",
                "MorseRunner.Infrastructure",
            },
            ["MorseRunner.App"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "MorseRunner.Client",
                "MorseRunner.Domain",
                "MorseRunner.Infrastructure",
            },
        };

    [Fact]
    public void ProductionProjectsObeyTheDeclaredDependencyDirection()
    {
        string root = RepositoryRoot.Find();
        string sourceRoot = Path.Combine(root, "src");
        string[] projectFiles = Directory.GetFiles(
            sourceRoot,
            "*.csproj",
            SearchOption.AllDirectories);

        Assert.NotEmpty(projectFiles);

        foreach (string projectFile in projectFiles)
        {
            string projectName = Path.GetFileNameWithoutExtension(projectFile);
            Assert.True(
                AllowedReferences.TryGetValue(projectName, out IReadOnlySet<string>? allowed),
                $"Project '{projectName}' has no declared architecture rule.");

            XDocument project = XDocument.Load(projectFile);
            IEnumerable<string> references = project
                .Descendants("ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetFileNameWithoutExtension(path!));

            foreach (string reference in references)
            {
                Assert.Contains(reference, allowed!);
            }
        }
    }

    [Fact]
    public void DomainHasNoPackageOrProjectDependencies()
    {
        string root = RepositoryRoot.Find();
        string projectPath = Path.Combine(
            root,
            "src",
            "MorseRunner.Domain",
            "MorseRunner.Domain.csproj");
        XDocument project = XDocument.Load(projectPath);

        Assert.Empty(project.Descendants("ProjectReference"));
        Assert.Empty(project.Descendants("PackageReference"));
    }
}
