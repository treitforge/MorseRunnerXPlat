using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MorseRunner.LegacyParity.Tests;

public sealed class LegacyOracleTargetTests
{
    private const string TestXPlatRevision =
        "1111111111111111111111111111111111111111";
    private const string TestXPlatTree =
        "2222222222222222222222222222222222222222";

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task MissingOracleRegistryConfigurationFailsClosed()
    {
        LegacyOracleTarget target = new(
            new LegacyOracleConfiguration(null, null, null),
            new StubProcessRunner());

        ParityObservation observation = await target.ExecuteAsync(
            CreateUnboundScenario(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-oracle-registry-not-configured",
            observation.FailureCode);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task MissingLegacyRootConfigurationFailsClosed()
    {
        LegacyOracleTarget target = new(
            new LegacyOracleConfiguration(
                "registry.json",
                new string('0', 64),
                null),
            new StubProcessRunner());

        ParityObservation observation = await target.ExecuteAsync(
            CreateUnboundScenario(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-root-not-configured",
            observation.FailureCode);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task MissingRegistryHashConfigurationFailsClosed()
    {
        LegacyOracleTarget target = new(
            new LegacyOracleConfiguration(
                "registry.json",
                null,
                "legacy-root"),
            new StubProcessRunner());

        ParityObservation observation = await target.ExecuteAsync(
            CreateUnboundScenario(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-oracle-registry-hash-not-configured",
            observation.FailureCode);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task RegistryHashMismatchFailsBeforeParsing()
    {
        using OracleHarness harness = new();

        ParityObservation observation = await harness
            .CreateTarget(
                registrySha256: new string('0', 64))
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-oracle-registry-hash-mismatch",
            observation.FailureCode);
    }

    [Theory]
    [Trait("Category", "ParityInfrastructure")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    public async Task RegistryRejectsUnknownAndDuplicateProperties(
        string mutation)
    {
        using OracleHarness harness = new();
        string registryJson = File.ReadAllText(
            harness.RegistryPath);
        if (mutation == "unknown")
        {
            JsonObject registry = JsonNode.Parse(
                registryJson)!.AsObject();
            registry["unexpected"] = true;
            registryJson = registry.ToJsonString();
        }
        else
        {
            registryJson = registryJson.Replace(
                "\"schemaVersion\":1",
                "\"schemaVersion\":1,\"schemaVersion\":1",
                StringComparison.Ordinal);
        }

        harness.WriteRawRegistry(registryJson);

        ParityObservation observation = await harness.CreateTarget()
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-oracle-registry-invalid",
            observation.FailureCode);
    }

    [Theory]
    [Trait("Category", "ParityInfrastructure")]
    [InlineData("C:/outside/LegacyOracle.exe")]
    [InlineData("../LegacyOracle.exe")]
    [InlineData("artifacts/legacy-reference/LegacyOracle.exe")]
    public async Task RegistryRejectsUnsafeExecutableIdentity(
        string executableIdentity)
    {
        using OracleHarness harness = new();
        JsonObject registry = JsonNode.Parse(
            File.ReadAllText(harness.RegistryPath))!
            .AsObject();
        registry["entries"]![0]!["executable"] =
            executableIdentity;
        harness.WriteRawRegistry(registry.ToJsonString());

        ParityObservation observation = await harness.CreateTarget()
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-oracle-registry-path-invalid",
            observation.FailureCode);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task RegistryPathWithJunctionAncestorFailsClosed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using OracleHarness harness = new();
        string realDirectory = Path.Combine(
            harness.RootPath,
            "real-registry");
        string junctionPath = Path.Combine(
            harness.RootPath,
            "registry-junction");
        Directory.CreateDirectory(realDirectory);
        string realRegistryPath = Path.Combine(
            realDirectory,
            "registry.json");
        File.Copy(harness.RegistryPath, realRegistryPath);
        CreateJunction(junctionPath, realDirectory);
        try
        {
            LegacyOracleTarget target = new(
                new LegacyOracleConfiguration(
                    Path.Combine(junctionPath, "registry.json"),
                    ComputeFileHash(realRegistryPath),
                    harness.RootPath),
                new StubProcessRunner(),
                harness.ReferenceDefinition,
                new StubRepositoryInspector());

            ParityObservation observation = await target.ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

            Assert.Equal(
                ParityTargetOutcome.Failed,
                observation.Outcome);
            Assert.Equal(
                "legacy-oracle-registry-path-invalid",
                observation.FailureCode);
        }
        finally
        {
            Directory.Delete(junctionPath);
        }
    }

    [Theory]
    [Trait("Category", "ParityInfrastructure")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("omitted")]
    public async Task ProvenanceRejectsUnknownAndDuplicateProperties(
        string mutation)
    {
        using OracleHarness harness = new();
        string provenanceJson = File.ReadAllText(
            harness.ProvenancePath);
        if (mutation == "unknown")
        {
            JsonObject provenance = JsonNode.Parse(
                provenanceJson)!.AsObject();
            provenance["unexpected"] = true;
            provenanceJson = provenance.ToJsonString();
        }
        else if (mutation == "omitted")
        {
            JsonObject provenance = JsonNode.Parse(
                provenanceJson)!.AsObject();
            provenance["xplat"]!.AsObject().Remove("clean");
            provenanceJson = provenance.ToJsonString();
        }
        else
        {
            provenanceJson = provenanceJson.Replace(
                "\"schemaVersion\":1",
                "\"schemaVersion\":1,\"schemaVersion\":1",
                StringComparison.Ordinal);
        }

        harness.WriteRawProvenance(provenanceJson);

        ParityObservation observation = await harness.CreateTarget()
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-provenance-invalid",
            observation.FailureCode);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task ProvenanceRejectsUnsortedSelectedCaseIds()
    {
        using OracleHarness harness = new();
        LegacyOracleProvenance provenance = harness.Provenance with
        {
            SelectedCaseIds =
            [
                "test.scenario.z",
                "test.scenario",
            ],
            Observations =
            [
                .. harness.Provenance.Observations!,
                new OracleScenarioProvenance(
                    "test.scenario.z",
                    0,
                    new string('a', 64)),
            ],
        };
        harness.WriteProvenance(provenance);

        ParityObservation observation = await harness.CreateTarget()
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-provenance-invalid",
            observation.FailureCode);
    }

    [Theory]
    [Trait("Category", "ParityInfrastructure")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    public async Task BuildRecipeRejectsUnknownAndDuplicateProperties(
        string mutation)
    {
        using OracleHarness harness = new();
        string recipeJson = File.ReadAllText(
            harness.BuildRecipePath);
        if (mutation == "unknown")
        {
            JsonObject recipe = JsonNode.Parse(
                recipeJson)!.AsObject();
            recipe["unexpected"] = true;
            recipeJson = recipe.ToJsonString();
        }
        else
        {
            recipeJson = recipeJson.Replace(
                "\"schemaVersion\":1",
                "\"schemaVersion\":1,\"schemaVersion\":1",
                StringComparison.Ordinal);
        }

        harness.WriteRawBuildRecipe(recipeJson);

        ParityObservation observation = await harness.CreateTarget()
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-build-provenance-mismatch",
            observation.FailureCode);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task MissingRegistryFileFailsClosed()
    {
        using OracleHarness harness = new();
        File.Delete(harness.RegistryPath);

        ParityObservation observation = await harness.CreateTarget()
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-oracle-registry-not-found",
            observation.FailureCode);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task MissingOracleFileFailsClosed()
    {
        using OracleHarness harness = new();
        File.Delete(harness.OraclePath);

        ParityObservation observation = await harness.CreateTarget()
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-oracle-registry-artifact-not-found",
            observation.FailureCode);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task MissingProvenanceFileFailsClosed()
    {
        using OracleHarness harness = new();
        File.Delete(harness.ProvenancePath);

        ParityObservation observation = await harness.CreateTarget()
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-oracle-registry-artifact-not-found",
            observation.FailureCode);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task MissingCaseDescriptorFailsClosed()
    {
        using OracleHarness harness = new();

        ParityObservation observation = await harness.CreateTarget()
            .ExecuteAsync(
                CreateUnboundScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-oracle-case-binding-missing",
            observation.FailureCode);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task ProvenanceRevisionMismatchFailsClosed()
    {
        using OracleHarness harness = new();
        LegacyOracleProvenance invalid = harness.Provenance with
        {
            Legacy = harness.Provenance.Legacy! with
            {
                Revision = "0000000000000000000000000000000000000000",
            },
        };
        harness.WriteProvenance(invalid);

        ParityObservation observation = await harness.CreateTarget()
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal("legacy-revision-mismatch", observation.FailureCode);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task BinaryTamperFailsIndependentRegistryAnchor()
    {
        using OracleHarness harness = new();
        File.WriteAllBytes(
            harness.OraclePath,
            Encoding.UTF8.GetBytes("changed!-oracle"));

        ParityObservation observation = await harness.CreateTarget()
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-oracle-anchor-mismatch",
            observation.FailureCode);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task ProvenanceTamperFailsIndependentRegistryAnchor()
    {
        using OracleHarness harness = new();
        LegacyOracleProvenance tampered = harness.Provenance with
        {
            Build = harness.Provenance.Build! with
            {
                BuiltAtUtc = "2026-07-20T00:00:00Z",
            },
        };
        harness.WriteProvenance(tampered, updateRegistry: false);

        ParityObservation observation = await harness.CreateTarget()
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-provenance-anchor-mismatch",
            observation.FailureCode);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task BuildRecipeTamperFailsIndependentRegistryAnchor()
    {
        using OracleHarness harness = new();
        File.AppendAllText(harness.BuildRecipePath, " ");

        ParityObservation observation = await harness.CreateTarget()
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-oracle-build-recipe-hash-mismatch",
            observation.FailureCode);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task PostBuildDirtyLegacyTreeFailsClosed()
    {
        using OracleHarness harness = new();
        StubRepositoryInspector inspector = new(
            new LegacyRepositoryInspection(
                LegacyOracleProvenance.PinnedLegacyRevision,
                LegacyOracleProvenance.PinnedLegacyTree,
                Clean: false,
                null));

        ParityObservation observation = await harness
            .CreateTarget(repositoryInspector: inspector)
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal("legacy-worktree-dirty", observation.FailureCode);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task OracleMustNotChangeLegacyWorktree()
    {
        using OracleHarness harness = new();
        LegacyRepositoryInspection clean = new(
            LegacyOracleProvenance.PinnedLegacyRevision,
            LegacyOracleProvenance.PinnedLegacyTree,
            Clean: true,
            null);
        StubRepositoryInspector inspector = new(
            clean,
            clean with
            {
                Clean = false,
            });

        ParityObservation observation = await harness
            .CreateTarget(repositoryInspector: inspector)
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-worktree-changed-during-oracle",
            observation.FailureCode);
    }

    [Theory]
    [Trait("Category", "ParityInfrastructure")]
    [InlineData("compiler")]
    [InlineData("schema")]
    [InlineData("canonicalization")]
    [InlineData("roots")]
    [InlineData("hash")]
    [InlineData("file-count")]
    [InlineData("byte-count")]
    public async Task ToolchainAttestationMustExactlyMatchReference(
        string field)
    {
        using OracleHarness harness = new();
        OracleToolchainProvenance toolchain =
            harness.Provenance.Toolchain!;
        OracleToolchainFingerprintProvenance fingerprint =
            toolchain.Fingerprint!;
        OracleToolchainProvenance invalid = field switch
        {
            "compiler" => toolchain with
            {
                CompilerSha256 = new string('1', 64),
            },
            "schema" => toolchain with
            {
                Fingerprint = fingerprint with
                {
                    SchemaVersion = fingerprint.SchemaVersion + 1,
                },
            },
            "canonicalization" => toolchain with
            {
                Fingerprint = fingerprint with
                {
                    Canonicalization = "different-canonicalization",
                },
            },
            "roots" => toolchain with
            {
                Fingerprint = fingerprint with
                {
                    Roots = fingerprint.Roots!.Reverse().ToArray(),
                },
            },
            "hash" => toolchain with
            {
                Fingerprint = fingerprint with
                {
                    AggregateSha256 = new string('b', 64),
                },
            },
            "file-count" => toolchain with
            {
                Fingerprint = fingerprint with
                {
                    FileCount = fingerprint.FileCount + 1,
                },
            },
            "byte-count" => toolchain with
            {
                Fingerprint = fingerprint with
                {
                    ByteCount = fingerprint.ByteCount + 1,
                },
            },
            _ => throw new ArgumentOutOfRangeException(
                nameof(field),
                field,
                null),
        };
        harness.WriteProvenance(
            harness.Provenance with
            {
                Toolchain = invalid,
            });

        ParityObservation observation = await harness.CreateTarget()
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-toolchain-provenance-mismatch",
            observation.FailureCode);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task StructuredBuildInvocationOrderIsExact()
    {
        using OracleHarness harness = new();
        OracleBuildInvocationProvenance invocation =
            harness.Provenance.Build!.Invocation!;
        harness.WriteProvenance(
            harness.Provenance with
            {
                Build = harness.Provenance.Build with
                {
                    Invocation = invocation with
                    {
                        UnitSearchPaths = invocation.UnitSearchPaths!
                            .Reverse()
                            .ToArray(),
                    },
                },
            });

        ParityObservation observation = await harness.CreateTarget()
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-build-provenance-mismatch",
            observation.FailureCode);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task RawBuildArgumentsMustExactlyMatchRecipe()
    {
        using OracleHarness harness = new();
        harness.WriteProvenance(
            harness.Provenance with
            {
                Build = harness.Provenance.Build! with
                {
                    Arguments = harness.Provenance.Build.Arguments!
                        .Append("-unexpected")
                        .ToArray(),
                },
            });

        ParityObservation observation = await harness.CreateTarget()
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-build-provenance-mismatch",
            observation.FailureCode);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task TamperedSourceProvenanceFailsClosed()
    {
        using OracleHarness harness = new();
        LegacyOracleProvenance invalid = harness.Provenance with
        {
            Oracle = harness.Provenance.Oracle! with
            {
                SourceSha256 = new string('1', 64),
            },
        };
        harness.WriteProvenance(invalid);

        ParityObservation observation = await harness.CreateTarget()
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-oracle-source-provenance-mismatch",
            observation.FailureCode);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task MalformedOracleOutputFailsClosed()
    {
        using OracleHarness harness = new();
        StubProcessRunner processRunner = new(
            _ => new LegacyOracleProcessResult(
                0,
                "{not-json",
                String.Empty));

        ParityObservation observation = await harness
            .CreateTarget(processRunner)
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.True(processRunner.WasCalled);
        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-oracle-output-invalid",
            observation.FailureCode);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task OracleMustEchoCompleteBinding()
    {
        using OracleHarness harness = new();
        StubProcessRunner processRunner = new(
            _ => new LegacyOracleProcessResult(
                0,
                """
                {"scenario":"test.scenario","values":["expected"]}
                """,
                String.Empty));

        ParityObservation observation = await harness
            .CreateTarget(processRunner)
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-oracle-output-invalid",
            observation.FailureCode);
    }

    [Theory]
    [Trait("Category", "ParityInfrastructure")]
    [InlineData("case")]
    [InlineData("input")]
    [InlineData("version")]
    [InlineData("source")]
    public async Task OracleEchoMustExactlyMatchInvocationBinding(
        string field)
    {
        using OracleHarness harness = new();
        StubProcessRunner processRunner = new(
            invocation =>
            {
                string output = field switch
                {
                    "case" => StubProcessRunner.CreateValidOutput(
                        invocation,
                        caseDefinitionSha256: new string('d', 64)),
                    "input" => StubProcessRunner.CreateValidOutput(
                        invocation,
                        inputSha256: new string('e', 64)),
                    "version" => StubProcessRunner.CreateValidOutput(
                        invocation,
                        versionId: "legacy-oracle-v2"),
                    "source" => StubProcessRunner.CreateValidOutput(
                        invocation,
                        source: "some/other/oracle.lpr"),
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(field),
                        field,
                        null),
                };
                return new LegacyOracleProcessResult(
                    0,
                    output,
                    String.Empty);
            });

        ParityObservation observation = await harness
            .CreateTarget(processRunner)
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-oracle-binding-mismatch",
            observation.FailureCode);
        Assert.NotNull(observation.LegacyOracle);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task OracleOutputHashMustMatchScenarioProvenance()
    {
        using OracleHarness harness = new();
        StubProcessRunner processRunner = new(
            invocation => new LegacyOracleProcessResult(
                0,
                StubProcessRunner.CreateValidOutput(
                    invocation,
                    ["changed"]),
                String.Empty));

        ParityObservation observation = await harness
            .CreateTarget(processRunner)
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(
            "legacy-oracle-observation-provenance-mismatch",
            observation.FailureCode);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task FullCanonicalInputAndCaseHashReachOracleProcess()
    {
        using OracleHarness harness = new();
        StubProcessRunner processRunner = new();
        ParityScenario scenario = harness.CreateScenario();

        ParityObservation observation = await harness
            .CreateTarget(processRunner)
            .ExecuteAsync(
                scenario,
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, observation.Outcome);
        LegacyOracleInvocation invocation =
            Assert.IsType<LegacyOracleInvocation>(
                processRunner.Invocation);
        Assert.Equal(scenario.Id, invocation.ScenarioId);
        Assert.Equal(
            scenario.CaseDefinitionSha256,
            invocation.CaseDefinitionSha256);
        Assert.Equal(scenario.InputSha256, invocation.InputSha256);
        Assert.Equal(
            scenario.CanonicalInputJson,
            invocation.CanonicalInputJson);
        Assert.Equal(
            """{"payload":{"fraction":"0.125"},"scenario":"test.scenario"}""",
            invocation.CanonicalInputJson);
        Assert.Equal(scenario.LegacyOracle, invocation.Descriptor);
    }

    [Theory]
    [Trait("Category", "ParityInfrastructure")]
    [InlineData(
        "registry",
        "legacy-oracle-registry-changed-during-oracle")]
    [InlineData(
        "source",
        "legacy-oracle-source-changed-during-oracle")]
    [InlineData(
        "recipe",
        "legacy-oracle-build-recipe-changed-during-oracle")]
    [InlineData(
        "executable",
        "legacy-oracle-changed-during-oracle")]
    [InlineData(
        "provenance",
        "legacy-provenance-changed-during-oracle")]
    public async Task SelectedArtifactSwapDuringExecutionFailsClosed(
        string artifact,
        string expectedFailureCode)
    {
        using OracleHarness harness = new();
        StubProcessRunner processRunner = new(
            invocation =>
            {
                string path = artifact switch
                {
                    "registry" => harness.RegistryPath,
                    "source" => harness.SourcePath,
                    "recipe" => harness.BuildRecipePath,
                    "executable" => harness.OraclePath,
                    "provenance" => harness.ProvenancePath,
                    _ => throw new InvalidOperationException(
                        $"Unknown artifact '{artifact}'."),
                };
                File.AppendAllText(path, "changed-during-execution");
                return new LegacyOracleProcessResult(
                    0,
                    StubProcessRunner.CreateValidOutput(invocation),
                    String.Empty);
            });

        ParityObservation observation = await harness
            .CreateTarget(processRunner)
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Failed, observation.Outcome);
        Assert.Equal(expectedFailureCode, observation.FailureCode);
        Assert.NotNull(observation.LegacyOracle);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task FullyAttestedOracleObservationPasses()
    {
        using OracleHarness harness = new();

        ParityObservation observation = await harness.CreateTarget()
            .ExecuteAsync(
                harness.CreateScenario(),
                TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, observation.Outcome);
        Assert.Equal(["expected"], observation.Values);
        Assert.Null(observation.FailureCode);
        Assert.Equal(
            harness.Descriptor.Source,
            observation.EvidenceSource);
        LegacyOracleResultBinding binding =
            Assert.IsType<LegacyOracleResultBinding>(
                observation.LegacyOracle);
        Assert.Equal(harness.Descriptor.VersionId, binding.VersionId);
        Assert.Equal(
            harness.RegistrySha256,
            binding.RegistrySha256);
        Assert.Equal(harness.OracleSha256, binding.ExecutableSha256);
        Assert.Equal(
            harness.ProvenanceSha256,
            binding.ProvenanceSha256);
    }

    [Fact]
    [Trait("Category", "LegacyOracleBuildIntegration")]
    public async Task
        ConfiguredBuildRegistryAttestsAndExecutesEverySelectedCase()
    {
        string? registryPath = Environment.GetEnvironmentVariable(
            "MORSE_RUNNER_LEGACY_ORACLE_REGISTRY");
        string? registrySha256 = Environment.GetEnvironmentVariable(
            "MORSE_RUNNER_LEGACY_ORACLE_REGISTRY_SHA256");
        string? legacyRoot = Environment.GetEnvironmentVariable(
            "MORSE_RUNNER_LEGACY_ROOT");
        if (String.IsNullOrWhiteSpace(registryPath)
            || String.IsNullOrWhiteSpace(registrySha256)
            || String.IsNullOrWhiteSpace(legacyRoot))
        {
            Assert.Fail(
                "The build integration test requires the registry, "
                + "registry SHA-256, and legacy root environment variables.");
        }

        string absoluteRegistryPath = Path.GetFullPath(registryPath);
        Assert.Equal(
            registrySha256,
            Path.GetFileNameWithoutExtension(absoluteRegistryPath));
        Assert.Equal(
            registrySha256,
            ComputeFileHash(absoluteRegistryPath));

        ParityCertificationCase[] definitions =
            ParityAcceptanceRegistry.SelectedIdsForCurrentRun()
                .Select(ParityCertificationCase.Load)
                .ToArray();
        Assert.NotEmpty(definitions);
        LegacyOracleBuildIntegration.ValidateArtifacts(
            definitions,
            absoluteRegistryPath);

        var target = new LegacyOracleTarget();
        foreach (ParityCertificationCase definition in definitions)
        {
            ParityObservation observation =
                await target.ExecuteAsync(
                    definition.Scenario,
                    TestContext.Current.CancellationToken);

            Assert.Equal(
                ParityTargetOutcome.Passed,
                observation.Outcome);
            Assert.Equal(
                definition.Scenario.ExpectedValues,
                observation.Values);
            Assert.Null(observation.FailureCode);
            Assert.Equal(
                definition.Scenario.LegacyOracle!.Source,
                observation.EvidenceSource);
            LegacyOracleResultBinding binding =
                Assert.IsType<LegacyOracleResultBinding>(
                    observation.LegacyOracle);
            Assert.Equal(registrySha256, binding.RegistrySha256);
            Assert.Equal(
                definition.Scenario.LegacyOracle!.VersionId,
                binding.VersionId);
            Assert.Equal(
                binding.ProvenanceSha256,
                ComputeFileHash(
                    Path.Combine(
                        RepositoryPaths.Root,
                        binding.Provenance.Replace(
                            '/',
                            Path.DirectorySeparatorChar))));
        }
    }

    private static ParityScenario CreateUnboundScenario()
    {
        return new ParityScenario(
            "test.scenario",
            "test",
            ["expected"]);
    }

    private static string ComputeFileHash(string path)
    {
        return ParityCanonicalJson.ComputeSha256(
            File.ReadAllBytes(path));
    }

    private static void CreateJunction(
        string junctionPath,
        string targetPath)
    {
        ProcessStartInfo startInfo = new("cmd.exe")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(junctionPath);
        startInfo.ArgumentList.Add(targetPath);
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Could not start the junction creation command.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(
            process.ExitCode == 0,
            $"Could not create test junction: "
            + $"{standardOutput}{standardError}");
    }

    private sealed class StubProcessRunner : ILegacyOracleProcessRunner
    {
        private readonly Func<
            LegacyOracleInvocation,
            LegacyOracleProcessResult>? _resultFactory;

        public StubProcessRunner(
            Func<
                LegacyOracleInvocation,
                LegacyOracleProcessResult>? resultFactory = null)
        {
            _resultFactory = resultFactory;
        }

        public bool WasCalled { get; private set; }

        public LegacyOracleInvocation? Invocation { get; private set; }

        public Task<LegacyOracleProcessResult> ExecuteAsync(
            string oraclePath,
            string legacyRoot,
            LegacyOracleInvocation invocation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WasCalled = true;
            Invocation = invocation;
            return Task.FromResult(
                _resultFactory?.Invoke(invocation)
                ?? new LegacyOracleProcessResult(
                    0,
                    CreateValidOutput(invocation),
                    String.Empty));
        }

        public static string CreateValidOutput(
            LegacyOracleInvocation invocation,
            IReadOnlyList<string>? values = null,
            string? caseDefinitionSha256 = null,
            string? inputSha256 = null,
            string? versionId = null,
            string? source = null)
        {
            Dictionary<string, object?> output =
                new(StringComparer.Ordinal)
                {
                    ["scenario"] = invocation.ScenarioId,
                    ["adapterId"] =
                        invocation.Descriptor.AdapterId,
                    ["versionId"] =
                        versionId
                        ?? invocation.Descriptor.VersionId,
                    ["source"] =
                        source ?? invocation.Descriptor.Source,
                    ["sourceSha256"] =
                        invocation.Descriptor.SourceSha256,
                    ["buildRecipe"] =
                        invocation.Descriptor.BuildRecipe,
                    ["buildRecipeSha256"] =
                        invocation.Descriptor.BuildRecipeSha256,
                    ["caseDefinitionSha256"] =
                        caseDefinitionSha256
                        ?? invocation.CaseDefinitionSha256,
                    ["inputSha256"] =
                        inputSha256 ?? invocation.InputSha256,
                    ["values"] = values ?? ["expected"],
                };
            return JsonSerializer.Serialize(output);
        }
    }

    private sealed class StubRepositoryInspector(
        LegacyRepositoryInspection? inspection = null,
        LegacyRepositoryInspection? postExecutionInspection = null)
        : ILegacyRepositoryInspector
    {
        private int _legacyInspectionCount;

        public Task<LegacyRepositoryInspection> InspectAsync(
            string legacyRoot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string comparisonRoot = Path.GetFullPath(legacyRoot);
            string xplatRoot = Path.GetFullPath(RepositoryPaths.Root);
            if (String.Equals(
                    comparisonRoot,
                    xplatRoot,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                return Task.FromResult(
                    new LegacyRepositoryInspection(
                        TestXPlatRevision,
                        TestXPlatTree,
                        Clean: false,
                        null));
            }

            LegacyRepositoryInspection initial =
                inspection
                ?? new LegacyRepositoryInspection(
                    LegacyOracleProvenance.PinnedLegacyRevision,
                    LegacyOracleProvenance.PinnedLegacyTree,
                    Clean: true,
                    null);
            LegacyRepositoryInspection result =
                _legacyInspectionCount++ > 0
                    && postExecutionInspection is not null
                    ? postExecutionInspection
                    : initial;
            return Task.FromResult(result);
        }
    }

    private sealed class OracleHarness : IDisposable
    {
        private readonly string[] _arguments;

        public OracleHarness()
        {
            RootPath = Path.Combine(
                RepositoryPaths.Root,
                "artifacts",
                "legacy-oracle",
                "test-runs",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
            RegistryPath = Path.Combine(RootPath, "registry.json");
            OraclePath = Path.Combine(
                RootPath,
                "bin",
                "LegacyOracle.exe");
            SourcePath = Path.Combine(RootPath, "LegacyOracle.lpr");
            BuildRecipePath = Path.Combine(
                RootPath,
                "build-recipe.json");
            BuildDriverPath = Path.Combine(
                RootPath,
                "Build-LegacyOracle.ps1");
            ProvenancePath = Path.Combine(RootPath, "provenance.json");
            ReferencePath = Path.Combine(
                RootPath,
                "legacy-reference.json");
            BundlePath = Path.Combine(
                RootPath,
                "legacy-reference.bundle");
            LazarusRoot = Path.Combine(RootPath, "lazarus");
            string compilerDirectory = Path.Combine(
                LazarusRoot,
                "fpc",
                "3.2.2",
                "bin",
                "x86_64-win64");
            CompilerPath = Path.Combine(compilerDirectory, "fpc.exe");
            BackendCompilerPath = Path.Combine(
                compilerDirectory,
                "ppcx64.exe");
            LazbuildPath = Path.Combine(LazarusRoot, "lazbuild.exe");
            WriteFile(OraclePath, "original-oracle");
            WriteFile(SourcePath, "oracle-source");
            WriteFile(BuildDriverPath, "build-driver");
            WriteFile(ReferencePath, "reference-definition");
            WriteFile(BundlePath, "reference-bundle");
            WriteFile(CompilerPath, "compiler");
            WriteFile(BackendCompilerPath, "backend-compiler");
            WriteFile(LazbuildPath, "lazbuild");

            ReferenceDefinition = new LegacyReferenceDefinition(
                1,
                ReferencePath,
                "https://example.invalid/MorseRunner.git",
                LegacyOracleProvenance.PinnedLegacyRevision,
                LegacyOracleProvenance.PinnedLegacyTree,
                BundlePath,
                ComputeHash(BundlePath),
                new LegacyReferenceToolchain(
                    "4.6",
                    "3.2.2",
                    "x86_64",
                    "win64",
                    ComputeHash(CompilerPath),
                    ComputeHash(BackendCompilerPath),
                    ComputeHash(LazbuildPath),
                    new LegacyReferenceToolchainFingerprint(
                        1,
                        "utf8-lf-nul-lowercase-relative-path-v1",
                        [
                            "components/freetype/lib/x86_64-win64",
                            "components/lazutils/lib/x86_64-win64",
                            "fpc/3.2.2",
                            "lazbuild.exe",
                            "lcl/units/x86_64-win64",
                            "packager/units/x86_64-win64",
                        ],
                        new string('a', 64),
                        42,
                        4_096)));

            string[] compilerOptions =
            [
                "-n",
                "-Sgic",
                "-Xs",
                "-B",
                "-Twin64",
                "-Px86_64",
                "-MDelphi",
                "-O2",
            ];
            string[] unitSearchPaths =
            [
                RootPath,
                Path.Combine(RootPath, "Lazarus"),
                Path.Combine(RootPath, "VCL"),
                Path.Combine(RootPath, "Util"),
                Path.Combine(
                    LazarusRoot,
                    "lcl",
                    "units",
                    "x86_64-win64",
                    "win32"),
                Path.Combine(
                    LazarusRoot,
                    "lcl",
                    "units",
                    "x86_64-win64"),
                Path.Combine(
                    LazarusRoot,
                    "components",
                    "freetype",
                    "lib",
                    "x86_64-win64"),
                Path.Combine(
                    LazarusRoot,
                    "components",
                    "lazutils",
                    "lib",
                    "x86_64-win64"),
                Path.Combine(
                    LazarusRoot,
                    "packager",
                    "units",
                    "x86_64-win64"),
                Path.Combine(
                    LazarusRoot,
                    "fpc",
                    "3.2.2",
                    "units",
                    "x86_64-win64"),
                Path.Combine(
                    LazarusRoot,
                    "fpc",
                    "3.2.2",
                    "units",
                    "x86_64-win64",
                    "*"),
                Path.Combine(
                    LazarusRoot,
                    "fpc",
                    "3.2.2",
                    "units",
                    "x86_64-win64",
                    "rtl"),
            ];
            string[] toolSearchPaths =
            [
                Path.Combine(
                    LazarusRoot,
                    "fpc",
                    "3.2.2",
                    "bin",
                    "x86_64-win64"),
            ];
            string[] librarySearchPaths =
            [
                Path.Combine(
                    LazarusRoot,
                    "fpc",
                    "3.2.2",
                    "lib",
                    "x86_64-win64"),
            ];
            string unitOutputPath = Path.Combine(RootPath, "units");
            _arguments =
            [
                .. compilerOptions,
                .. unitSearchPaths.Select(path => $"-Fu{path}"),
                .. toolSearchPaths.Select(path => $"-FD{path}"),
                .. librarySearchPaths.Select(path => $"-Fl{path}"),
                $"-FU{unitOutputPath}",
                $"-FE{Path.GetDirectoryName(OraclePath)!}",
                $"-o{OraclePath}",
                SourcePath,
            ];

            SourceIdentity = ToRepositoryIdentity(SourcePath);
            BuildRecipeIdentity =
                ToRepositoryIdentity(BuildRecipePath);
            LegacyOracleBuildRecipe recipe = new(
                1,
                "legacy-ce-pascal",
                "legacy-oracle-v1",
                new LegacyOracleBuildRecipeSourceClosure(
                    SourceIdentity,
                    ComputeHash(SourcePath),
                    ReferenceDefinition.Revision,
                    ReferenceDefinition.Tree,
                    ReferenceDefinition.BundleSha256,
                    ReferenceDefinition.Toolchain.Fingerprint
                        .AggregateSha256),
                new LegacyOracleBuildRecipeInvocation(
                    CompilerPath,
                    _arguments));
            File.WriteAllText(
                BuildRecipePath,
                JsonSerializer.Serialize(recipe));

            Descriptor = new LegacyOracleDescriptor(
                "legacy-ce-pascal",
                "legacy-oracle-v1",
                SourceIdentity,
                ComputeHash(SourcePath),
                BuildRecipeIdentity,
                ComputeHash(BuildRecipePath));

            LegacyOracleInvocation oracleInvocation =
                CreateInvocation(CreateScenario());
            string validOutput =
                StubProcessRunner.CreateValidOutput(oracleInvocation);
            Provenance = new LegacyOracleProvenance(
                LegacyOracleProvenance.CurrentSchemaVersion,
                Descriptor.AdapterId,
                Descriptor.VersionId,
                Descriptor.Source,
                Descriptor.SourceSha256,
                Descriptor.BuildRecipe,
                Descriptor.BuildRecipeSha256,
                ["test.scenario"],
                new OracleReferenceProvenance(
                    ReferencePath,
                    ComputeHash(ReferencePath),
                    BundlePath,
                    ComputeHash(BundlePath)),
                new LegacySourceProvenance(
                    ReferenceDefinition.Repository,
                    LegacyOracleProvenance.PinnedLegacyRevision,
                    LegacyOracleProvenance.PinnedLegacyTree,
                    RootPath,
                    Clean: true),
                new XPlatSourceProvenance(
                    TestXPlatRevision,
                    TestXPlatTree,
                    Clean: false),
                new OracleBinaryProvenance(
                    Descriptor.Source,
                    SourcePath,
                    ComputeHash(SourcePath),
                    Descriptor.BuildRecipe,
                    BuildRecipePath,
                    ComputeHash(BuildRecipePath),
                    OraclePath,
                    ComputeHash(OraclePath),
                    new FileInfo(OraclePath).Length),
                new OracleToolchainProvenance(
                    LazarusRoot,
                    "4.6",
                    "3.2.2",
                    "x86_64",
                    "win64",
                    CompilerPath,
                    ComputeHash(CompilerPath),
                    BackendCompilerPath,
                    ComputeHash(BackendCompilerPath),
                    LazbuildPath,
                    ComputeHash(LazbuildPath),
                    new OracleToolchainFingerprintProvenance(
                        ReferenceDefinition.Toolchain.Fingerprint
                            .SchemaVersion,
                        ReferenceDefinition.Toolchain.Fingerprint
                            .Canonicalization,
                        ReferenceDefinition.Toolchain.Fingerprint.Roots,
                        ReferenceDefinition.Toolchain.Fingerprint
                            .AggregateSha256,
                        ReferenceDefinition.Toolchain.Fingerprint
                            .FileCount,
                        ReferenceDefinition.Toolchain.Fingerprint
                            .ByteCount)),
                new OracleBuildProvenance(
                    BuildDriverPath,
                    ComputeHash(BuildDriverPath),
                    _arguments,
                    new OracleBuildInvocationProvenance(
                        CompilerPath,
                        compilerOptions,
                        unitSearchPaths,
                        toolSearchPaths,
                        librarySearchPaths,
                        unitOutputPath,
                        Path.GetDirectoryName(OraclePath)!,
                        OraclePath,
                        SourcePath),
                    "2026-07-19T00:00:00Z"),
                new OracleManifestProvenance(
                    Path.Combine(
                        RepositoryPaths.Root,
                        "tests",
                        "parity",
                        "parity-manifest.json"),
                    ComputeHash(
                        Path.Combine(
                            RepositoryPaths.Root,
                            "tests",
                            "parity",
                            "parity-manifest.json"))),
                [
                    new OracleScenarioProvenance(
                        "test.scenario",
                        1,
                        ComputeHash(
                            Encoding.UTF8.GetBytes(
                                validOutput.Trim()))),
                ]);
            OracleSha256 = ComputeHash(OraclePath);
            WriteProvenance(Provenance);
        }

        public string RootPath { get; }

        public string RegistryPath { get; }

        public string OraclePath { get; }

        public string SourcePath { get; }

        public string BuildRecipePath { get; }

        public string BuildDriverPath { get; }

        public string SourceIdentity { get; }

        public string BuildRecipeIdentity { get; }

        public string ProvenancePath { get; }

        public string ReferencePath { get; }

        public string BundlePath { get; }

        public string LazarusRoot { get; }

        public string CompilerPath { get; }

        public string BackendCompilerPath { get; }

        public string LazbuildPath { get; }

        public LegacyReferenceDefinition ReferenceDefinition { get; }

        public LegacyOracleDescriptor Descriptor { get; private set; }

        public LegacyOracleProvenance Provenance { get; private set; }

        public string OracleSha256 { get; }

        public string ProvenanceSha256 { get; private set; } =
            String.Empty;

        public string RegistrySha256 { get; private set; } =
            String.Empty;

        public ParityScenario CreateScenario()
        {
            using JsonDocument document = JsonDocument.Parse(
                """
                {
                  "scenario": "test.scenario",
                  "payload": {
                    "fraction": "0.125"
                  }
                }
                """);
            return new ParityScenario(
                "test.scenario",
                "test",
                ["expected"],
                document.RootElement,
                new string('c', 64),
                Descriptor);
        }

        public LegacyOracleTarget CreateTarget(
            ILegacyOracleProcessRunner? processRunner = null,
            ILegacyRepositoryInspector? repositoryInspector = null,
            string? registrySha256 = null)
        {
            return new LegacyOracleTarget(
                new LegacyOracleConfiguration(
                    RegistryPath,
                    registrySha256 ?? RegistrySha256,
                    RootPath),
                processRunner ?? new StubProcessRunner(),
                ReferenceDefinition,
                repositoryInspector ?? new StubRepositoryInspector());
        }

        public void WriteProvenance(
            LegacyOracleProvenance provenance,
            bool updateRegistry = true)
        {
            File.WriteAllText(
                ProvenancePath,
                JsonSerializer.Serialize(provenance));
            if (updateRegistry)
            {
                ProvenanceSha256 = ComputeHash(ProvenancePath);
                WriteRegistry();
            }
        }

        public void WriteRawProvenance(string provenanceJson)
        {
            File.WriteAllText(
                ProvenancePath,
                provenanceJson);
            ProvenanceSha256 = ComputeHash(ProvenancePath);
            WriteRegistry();
        }

        public void WriteRawRegistry(string registryJson)
        {
            File.WriteAllText(
                RegistryPath,
                registryJson);
            RegistrySha256 = ComputeHash(RegistryPath);
        }

        public void WriteRawBuildRecipe(string recipeJson)
        {
            File.WriteAllText(
                BuildRecipePath,
                recipeJson);
            Descriptor = Descriptor with
            {
                BuildRecipeSha256 =
                    ComputeHash(BuildRecipePath),
            };
            Provenance = Provenance with
            {
                BuildRecipeSha256 =
                    Descriptor.BuildRecipeSha256,
                Oracle = Provenance.Oracle! with
                {
                    BuildRecipeSha256 =
                        Descriptor.BuildRecipeSha256,
                },
            };
            WriteProvenance(Provenance);
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }

        private LegacyOracleInvocation CreateInvocation(
            ParityScenario scenario)
        {
            return new LegacyOracleInvocation(
                scenario.Id,
                scenario.CaseDefinitionSha256!,
                scenario.InputSha256,
                scenario.CanonicalInputJson,
                ComputeHash(OraclePath),
                Descriptor);
        }

        private void WriteRegistry()
        {
            LegacyOracleRegistryDocument registry = new(
                1,
                [
                    new LegacyOracleRegistryEntry(
                        Descriptor.AdapterId,
                        Descriptor.VersionId,
                        Descriptor.Source,
                        Descriptor.SourceSha256,
                        Descriptor.BuildRecipe,
                        Descriptor.BuildRecipeSha256,
                        ToRepositoryIdentity(OraclePath),
                        OracleSha256,
                        ToRepositoryIdentity(ProvenancePath),
                        ProvenanceSha256),
                ]);
            File.WriteAllText(
                RegistryPath,
                JsonSerializer.Serialize(registry));
            RegistrySha256 = ComputeHash(RegistryPath);
        }

        private static string ComputeHash(string path)
        {
            byte[] hash = SHA256.HashData(File.ReadAllBytes(path));
            return Convert.ToHexStringLower(hash);
        }

        private static string ComputeHash(ReadOnlySpan<byte> contents)
        {
            return Convert.ToHexStringLower(SHA256.HashData(contents));
        }

        private static void WriteFile(string path, string contents)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes(contents));
        }

        private static string ToRepositoryIdentity(string path)
        {
            return Path.GetRelativePath(
                    RepositoryPaths.Root,
                    path)
                .Replace('\\', '/');
        }
    }
}
