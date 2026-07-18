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
    [MemberData(nameof(ParityIds))]
    public async Task UxInventoryContractIsBothGreen(string parityId)
    {
        ManifestItem item = LoadManifestItem(parityId);
        InventoryFixture fixture = LoadFixture(item.Fixture);
        Assert.Equal(parityId, fixture.ParityId);
        Assert.Equal(
            "55bbd019c29d8cf693184ea420a17a253f16fe1e",
            fixture.Revision);

        ParityScenario scenario = new(
            parityId,
            item.Category,
            fixture.Values);
        LegacySurfaceContractTarget legacyTarget = new(fixture.SurfacePrefixes);
        XPlatUxContractTarget xplatTarget = new();
        ParityObservation legacy = await legacyTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);
        ParityObservation xplat = await xplatTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, legacy.Outcome);
        Assert.Equal(fixture.Values, legacy.Values);
        Assert.Equal(fixture.Values, xplat.Values);
        Assert.Equal(ParityTargetOutcome.Passed, xplat.Outcome);
        Assert.Equal(
            ParityAssessment.BothGreen,
            ParityAssessmentClassifier.Classify(legacy, xplat));
        Assert.True(
            File.Exists(Path.Combine(RepositoryPaths.Root, item.Evidence)),
            $"Evidence not found: {item.Evidence}");
    }

    [Fact]
    public async Task DataAndOperationalPathsAreBothGreen()
    {
        const string parityId = "data.files-and-operational-paths";
        ManifestItem item = LoadManifestItem(parityId, "both-green");
        InventoryFixture fixture = LoadFixture(item.Fixture);
        ParityScenario scenario = new(parityId, item.Category, fixture.Values);
        LegacySurfaceContractTarget legacyTarget = new(fixture.SurfacePrefixes);
        XPlatDataOperationsTarget xplatTarget = new();

        ParityObservation legacy = await legacyTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);
        ParityObservation xplat = await xplatTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, legacy.Outcome);
        Assert.Equal(fixture.Values, xplat.Values);
        Assert.Equal(ParityTargetOutcome.Passed, xplat.Outcome);
        Assert.Equal(
            ParityAssessment.BothGreen,
            ParityAssessmentClassifier.Classify(legacy, xplat));
    }

    [Fact]
    public async Task SettingsContractIsBothGreen()
    {
        const string parityId = "configuration.persisted-settings";
        ManifestItem item = LoadManifestItem(parityId, "both-green");
        InventoryFixture fixture = LoadFixture(item.Fixture);
        ParityScenario scenario = new(parityId, item.Category, fixture.Values);
        LegacySurfaceContractTarget legacyTarget = new(fixture.SurfacePrefixes);
        XPlatSettingsContractTarget xplatTarget = new();

        ParityObservation legacy = await legacyTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);
        ParityObservation xplat = await xplatTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, legacy.Outcome);
        Assert.Equal(fixture.Values, xplat.Values);
        Assert.Equal(ParityTargetOutcome.Passed, xplat.Outcome);
        Assert.Equal(
            ParityAssessment.BothGreen,
            ParityAssessmentClassifier.Classify(legacy, xplat));
    }

    [Fact]
    public async Task QsoContractIsBothGreen()
    {
        const string parityId = "logging.qso-model";
        ManifestItem item = LoadManifestItem(parityId, "both-green");
        InventoryFixture fixture = LoadFixture(item.Fixture);
        ParityScenario scenario = new(parityId, item.Category, fixture.Values);
        LegacySurfaceContractTarget legacyTarget = new(fixture.SurfacePrefixes);
        XPlatLoggingTarget xplatTarget = new();

        ParityObservation legacy = await legacyTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);
        ParityObservation xplat = await xplatTarget.ExecuteAsync(
            scenario,
            TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, legacy.Outcome);
        Assert.Equal(fixture.Values, xplat.Values);
        Assert.Equal(ParityTargetOutcome.Passed, xplat.Outcome);
        Assert.Equal(
            ParityAssessment.BothGreen,
            ParityAssessmentClassifier.Classify(legacy, xplat));
    }

    private static ManifestItem LoadManifestItem(
        string parityId,
        string expectedStatus = "both-green")
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

        Assert.Equal(
            expectedStatus,
            element.GetProperty("status").GetString());
        Assert.Equal("pass", element.GetProperty("legacyTestStatus").GetString());
        Assert.Equal(
            expectedStatus == "both-green" ? "pass" : "fail",
            element.GetProperty("xplatTestStatus").GetString());

        return JsonSerializer.Deserialize<ManifestItem>(element.GetRawText())!;
    }

    private static InventoryFixture LoadFixture(string relativePath)
    {
        string path = Path.Combine(RepositoryPaths.Root, relativePath);
        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<InventoryFixture>(stream)!;
    }

    private sealed record ManifestItem(
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("failureCode")] string? FailureCode,
        [property: JsonPropertyName("fixture")] string Fixture,
        [property: JsonPropertyName("evidence")] string Evidence);

    private sealed record InventoryFixture(
        [property: JsonPropertyName("revision")] string Revision,
        [property: JsonPropertyName("parityId")] string ParityId,
        [property: JsonPropertyName("surfacePrefixes")]
        IReadOnlyList<string> SurfacePrefixes,
        [property: JsonPropertyName("values")] IReadOnlyList<string> Values);
}
