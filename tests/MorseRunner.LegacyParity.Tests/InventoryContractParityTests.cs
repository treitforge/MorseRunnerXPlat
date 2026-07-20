using System.Text.Json;
using System.Text.Json.Serialization;

namespace MorseRunner.LegacyParity.Tests;

public sealed class InventoryContractParityTests
{
    public static TheoryData<string> ParityIds =>
        new()
        {
            "ux.main-form-objects",
            "ux.main-menu-commands",
            "ux.main-form-events",
            "ux.keyboard-workflows",
            "ux.legacy-vcl-components",
            "ux.score-dialog",
        };

    [Theory]
    [Trait("Category", "LegacyV1Noncertifying")]
    [MemberData(nameof(ParityIds))]
    public Task UxInventoryContractMatchesSelectedTarget(string parityId)
    {
        return AssertInventoryVectorAsync(
            parityId,
            static () => new XPlatUxContractTarget());
    }

    [Fact]
    [Trait("Category", "LegacyV1Noncertifying")]
    public Task DataAndOperationalPathsMatchSelectedTarget()
    {
        return AssertInventoryVectorAsync(
            "data.files-and-operational-paths",
            static () => new XPlatDataOperationsTarget());
    }

    [Fact]
    [Trait("Category", "LegacyV1Noncertifying")]
    public Task SettingsContractMatchesSelectedTarget()
    {
        return AssertInventoryVectorAsync(
            "configuration.persisted-settings",
            static () => new XPlatSettingsContractTarget());
    }

    [Fact]
    [Trait("Category", "LegacyV1Noncertifying")]
    public Task QsoContractMatchesSelectedTarget()
    {
        return AssertInventoryVectorAsync(
            "logging.qso-model",
            static () => new XPlatLoggingTarget());
    }

    private static async Task AssertInventoryVectorAsync(
        string parityId,
        Func<IParityTarget> createXPlat)
    {
        ManifestItem item = LoadManifestItem(parityId);
        InventoryFixture fixture = LoadFixture(
            GetLegacyV1FixturePath(parityId));
        Assert.Equal(parityId, fixture.ParityId);
        Assert.Equal(
            LegacyOracleProvenance.PinnedLegacyRevision,
            fixture.Revision);

        ParityScenario scenario = new(
            parityId,
            item.Category,
            fixture.Values);
        SelectedParityObservation selected =
            await ParityRegressionRunner.ExecuteSelectedAsync(
                scenario,
                () => new LegacySurfaceContractTarget(
                    fixture.SurfacePrefixes),
                createXPlat,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ParityTargetOutcome.Passed,
            selected.Observation.Outcome);
        Assert.Equal(fixture.Values, selected.Observation.Values);
        string evidencePath = GetLegacyV1EvidencePath(parityId);
        Assert.True(
            File.Exists(Path.Combine(RepositoryPaths.Root, evidencePath)),
            $"Evidence not found: {evidencePath}");
    }

    private static ManifestItem LoadManifestItem(string parityId)
    {
        string path = Path.Combine(
            RepositoryPaths.Root,
            "tests",
            "parity",
            "parity-manifest.json");
        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement element = document.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == parityId);

        return JsonSerializer.Deserialize<ManifestItem>(element.GetRawText())!;
    }

    private static string GetLegacyV1FixturePath(string parityId)
    {
        return $"tests/parity/fixtures/legacy/"
            + $"{parityId.Replace('.', '-')}.json";
    }

    private static string GetLegacyV1EvidencePath(string parityId)
    {
        return $"tests/parity/evidence/"
            + $"{parityId.Replace('.', '-')}.baseline.json";
    }

    private static InventoryFixture LoadFixture(string relativePath)
    {
        string path = Path.Combine(RepositoryPaths.Root, relativePath);
        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<InventoryFixture>(stream)!;
    }

    private sealed record ManifestItem(
        [property: JsonPropertyName("category")] string Category);

    private sealed record InventoryFixture(
        [property: JsonPropertyName("revision")] string Revision,
        [property: JsonPropertyName("parityId")] string ParityId,
        [property: JsonPropertyName("surfacePrefixes")]
        IReadOnlyList<string> SurfacePrefixes,
        [property: JsonPropertyName("values")] IReadOnlyList<string> Values);
}
