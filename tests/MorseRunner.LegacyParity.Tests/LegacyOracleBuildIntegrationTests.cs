namespace MorseRunner.LegacyParity.Tests;

public sealed class LegacyOracleBuildIntegrationTests
{
    private static readonly LegacyOracleDescriptor V1 = Descriptor(
        version: 1,
        hashCharacter: '1');
    private static readonly LegacyOracleDescriptor V2 = Descriptor(
        version: 2,
        hashCharacter: '2');

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void TwoCasesMayShareOneIdenticalCompleteDescriptor()
    {
        LegacyOracleBuildSelection[] selected =
        [
            new("case.b", V1),
            new("case.a", V1),
        ];

        LegacyOracleBuildIntegration.Validate(
            selected,
            [RegistryEntry(V1)],
            [Provenance(V1, "case.a", "case.b")]);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void SharedVersionRejectsConflictingCompleteDescriptors()
    {
        LegacyOracleDescriptor conflict = V1 with
        {
            SourceSha256 = new string('a', 64),
        };
        LegacyOracleBuildSelection[] selected =
        [
            new("case.a", V1),
            new("case.b", conflict),
        ];

        Assert.Throws<InvalidDataException>(
            () => LegacyOracleBuildIntegration.Validate(
                selected,
                [RegistryEntry(V1)],
                [Provenance(V1, "case.a", "case.b")]));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void IndependentV1AndV2DescriptorsRequireBothVersions()
    {
        LegacyOracleBuildSelection[] selected =
        [
            new("case.v2", V2),
            new("case.v1", V1),
        ];

        LegacyOracleBuildIntegration.Validate(
            selected,
            [
                RegistryEntry(V2),
                RegistryEntry(V1),
            ],
            [
                Provenance(V1, "case.v1"),
                Provenance(V2, "case.v2"),
            ]);
    }

    [Theory]
    [Trait("Category", "ParityInfrastructure")]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("alias")]
    public void RegistryVersionSetMustExactlyMatchSelection(
        string mutation)
    {
        LegacyOracleRegistryEntry[] entries = mutation switch
        {
            "missing" => [RegistryEntry(V1)],
            "extra" =>
            [
                RegistryEntry(V1),
                RegistryEntry(V2),
                RegistryEntry(
                    Descriptor(
                        version: 3,
                        hashCharacter: '3')),
            ],
            "alias" =>
            [
                RegistryEntry(V1),
                RegistryEntry(V2) with
                {
                    VersionId = "legacy-oracle-v02",
                },
            ],
            _ => throw new InvalidOperationException(),
        };

        Assert.Throws<InvalidDataException>(
            () => LegacyOracleBuildIntegration.Validate(
                [
                    new("case.v1", V1),
                    new("case.v2", V2),
                ],
                entries,
                [
                    Provenance(V1, "case.v1"),
                    Provenance(V2, "case.v2"),
                ]));
    }

    [Theory]
    [Trait("Category", "ParityInfrastructure")]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("unsorted")]
    [InlineData("alias")]
    public void ProvenanceCaseIdsMustBeExactAndSorted(
        string mutation)
    {
        string[] selectedCaseIds = mutation switch
        {
            "missing" => ["case.a"],
            "extra" => ["case.a", "case.b", "case.c"],
            "unsorted" => ["case.b", "case.a"],
            "alias" => ["case.a", "case/../case.b"],
            _ => throw new InvalidOperationException(),
        };

        Assert.Throws<InvalidDataException>(
            () => LegacyOracleBuildIntegration.Validate(
                [
                    new("case.a", V1),
                    new("case.b", V1),
                ],
                [RegistryEntry(V1)],
                [Provenance(V1, selectedCaseIds)]));
    }

    [Theory]
    [Trait("Category", "ParityInfrastructure")]
    [InlineData("registry-source")]
    [InlineData("registry-recipe")]
    [InlineData("provenance-source")]
    [InlineData("provenance-recipe")]
    public void DescriptorIdentityAliasesAreRejected(string mutation)
    {
        LegacyOracleRegistryEntry registry = RegistryEntry(V1);
        LegacyOracleBuildProvenanceIdentity provenance =
            Provenance(V1, "case.a");
        const string sourceAlias =
            "tests/parity/legacy-oracle/v1/./LegacyOracle.lpr";
        const string recipeAlias =
            "tests/parity/legacy-oracle/v1/./build-recipe.json";
        switch (mutation)
        {
            case "registry-source":
                registry = registry with { Source = sourceAlias };
                break;
            case "registry-recipe":
                registry = registry with
                {
                    BuildRecipe = recipeAlias,
                };
                break;
            case "provenance-source":
                provenance = provenance with { Source = sourceAlias };
                break;
            case "provenance-recipe":
                provenance = provenance with
                {
                    BuildRecipe = recipeAlias,
                };
                break;
            default:
                throw new InvalidOperationException();
        }

        Assert.Throws<InvalidDataException>(
            () => LegacyOracleBuildIntegration.Validate(
                [new("case.a", V1)],
                [registry],
                [provenance]));
    }

    [Theory]
    [Trait("Category", "ParityInfrastructure")]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("alias")]
    public void ProvenanceVersionSetMustExactlyMatchSelection(
        string mutation)
    {
        LegacyOracleBuildProvenanceIdentity[] provenances =
            mutation switch
            {
                "missing" => [Provenance(V1, "case.v1")],
                "extra" =>
                [
                    Provenance(V1, "case.v1"),
                    Provenance(V2, "case.v2"),
                    Provenance(
                        Descriptor(
                            version: 3,
                            hashCharacter: '3'),
                        "case.v3"),
                ],
                "alias" =>
                [
                    Provenance(V1, "case.v1"),
                    Provenance(V2, "case.v2") with
                    {
                        VersionId = "legacy-oracle-v02",
                    },
                ],
                _ => throw new InvalidOperationException(),
            };

        Assert.Throws<InvalidDataException>(
            () => LegacyOracleBuildIntegration.Validate(
                [
                    new("case.v1", V1),
                    new("case.v2", V2),
                ],
                [
                    RegistryEntry(V1),
                    RegistryEntry(V2),
                ],
                provenances));
    }

    private static LegacyOracleDescriptor Descriptor(
        int version,
        char hashCharacter)
    {
        return new(
            "LegacyOracleTarget",
            $"legacy-oracle-v{version}",
            $"tests/parity/legacy-oracle/v{version}/LegacyOracle.lpr",
            new string(hashCharacter, 64),
            $"tests/parity/legacy-oracle/v{version}/build-recipe.json",
            new string(hashCharacter, 64));
    }

    private static LegacyOracleRegistryEntry RegistryEntry(
        LegacyOracleDescriptor descriptor)
    {
        return new(
            descriptor.AdapterId,
            descriptor.VersionId,
            descriptor.Source,
            descriptor.SourceSha256,
            descriptor.BuildRecipe,
            descriptor.BuildRecipeSha256,
            $"artifacts/{descriptor.VersionId}/LegacyOracle.exe",
            new string('e', 64),
            $"artifacts/{descriptor.VersionId}/provenance.json",
            new string('f', 64));
    }

    private static LegacyOracleBuildProvenanceIdentity Provenance(
        LegacyOracleDescriptor descriptor,
        params string[] selectedCaseIds)
    {
        return new(
            descriptor.AdapterId,
            descriptor.VersionId,
            descriptor.Source,
            descriptor.SourceSha256,
            descriptor.BuildRecipe,
            descriptor.BuildRecipeSha256,
            selectedCaseIds);
    }
}
