using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

public sealed class LegacyOracleDescriptorVectorTests
{
    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void SharedDescriptorVectorsMatchCSharpValidation()
    {
        string path = Path.Combine(
            RepositoryPaths.Root,
            "tests",
            "parity",
            "legacy-oracle-descriptor-vectors.json");
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(path));
        Assert.Equal(
            1,
            document.RootElement
                .GetProperty("schemaVersion")
                .GetInt32());
        foreach (JsonElement vector in document.RootElement
                     .GetProperty("vectors")
                     .EnumerateArray())
        {
            string id = vector.GetProperty("id").GetString()!;
            var descriptor = new LegacyOracleDescriptor(
                "LegacyOracleTarget",
                vector.GetProperty("versionId").GetString()!,
                vector.GetProperty("source").GetString()!,
                new string('1', 64),
                vector.GetProperty("buildRecipe").GetString()!,
                new string('2', 64));
            Exception? failure = Record.Exception(
                () => ParityCertificationCase
                    .ValidateLegacyOracleVersionBinding(
                        descriptor,
                        id));
            bool expectedValid =
                vector.GetProperty("valid").GetBoolean();

            Assert.True(
                expectedValid == (failure is null),
                $"Descriptor vector '{id}' expected valid="
                + $"{expectedValid}, observed failure: {failure}");
        }
    }
}
