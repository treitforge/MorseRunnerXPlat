using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MorseRunner.LegacyParity.Tests;

public sealed class LegacyAudioV3CandidateTests
{
    private const string Category = "ParityDraftCandidate";
    private static readonly string[] CaseIds =
    [
        "audio.bandwidth-live-control",
        "audio.operator-sidetone-pipeline",
        "audio.qsk-receiver-ducking",
        "audio.rit-live-control",
        "audio.sst-farnsworth-timing",
    ];

    [Fact]
    [Trait("Category", Category)]
    public void ArtifactsAreContentAddressedAndExplicitlyUnactivated()
    {
        using JsonDocument descriptor = LoadJson(
            "adapter-descriptor.json");
        using JsonDocument recipe = LoadJson("build-recipe.json");
        using JsonDocument contracts = LoadJson(
            "case-contracts.json");
        using JsonDocument capture = LoadJson(
            "captured-checkpoints.json");
        using JsonDocument requirements = LoadJson(
            "integration-requirements.json");

        Assert.Equal(
            "unactivated-noncertifying-ce-runtime-candidate",
            contracts.RootElement
                .GetProperty("certificationStatus")
                .GetString());
        Assert.False(
            contracts.RootElement
                .GetProperty("manifestActivation")
                .GetBoolean());
        Assert.False(
            contracts.RootElement
                .GetProperty("externalDescriptorBuildSupported")
                .GetBoolean());
        Assert.False(
            requirements.RootElement
                .GetProperty("manifestActivation")
                .GetBoolean());
        Assert.False(
            requirements.RootElement
                .GetProperty("registryActivation")
                .GetBoolean());
        Assert.False(
            requirements.RootElement
                .GetProperty("externalDescriptorBuildSupported")
                .GetBoolean());
        Assert.True(
            capture.RootElement
                .GetProperty("nonCertifyingDevelopmentCapture")
                .GetBoolean());

        JsonElement descriptorRoot = descriptor.RootElement;
        string sourceIdentity = descriptorRoot
            .GetProperty("source")
            .GetString()!;
        string recipeIdentity = descriptorRoot
            .GetProperty("buildRecipe")
            .GetString()!;
        string caseDefinitionIdentity = descriptorRoot
            .GetProperty("caseDefinition")
            .GetString()!;
        Assert.Equal(
            FileSha256(RepositoryFile(sourceIdentity)),
            descriptorRoot
                .GetProperty("sourceSha256")
                .GetString());
        Assert.Equal(
            FileSha256(RepositoryFile(recipeIdentity)),
            descriptorRoot
                .GetProperty("buildRecipeSha256")
                .GetString());
        Assert.Equal(
            FileSha256(RepositoryFile(caseDefinitionIdentity)),
            descriptorRoot
                .GetProperty("caseDefinitionSha256")
                .GetString());
        Assert.Equal(
            FileSha256(
                RepositoryFile(
                    descriptorRoot
                        .GetProperty("legacyReferenceDefinition")
                        .GetString()!)),
            descriptorRoot
                .GetProperty("legacyReferenceDefinitionSha256")
                .GetString());
        Assert.Equal(
            FileSha256(
                RepositoryFile(
                    descriptorRoot
                        .GetProperty("legacyBundle")
                        .GetString()!)),
            descriptorRoot
                .GetProperty("legacyBundleSha256")
                .GetString());
        Assert.Equal(
            descriptorRoot.GetProperty("source").GetString(),
            recipe.RootElement
                .GetProperty("sourceClosure")
                .GetProperty("oracleSource")
                .GetString());
        Assert.Equal(
            descriptorRoot.GetProperty("sourceSha256").GetString(),
            recipe.RootElement
                .GetProperty("sourceClosure")
                .GetProperty("oracleSourceSha256")
                .GetString());
        JsonElement sourceClosure = recipe.RootElement.GetProperty(
            "sourceClosure");
        JsonElement[] executedUnits = sourceClosure
            .GetProperty("executedLegacyUnits")
            .EnumerateArray()
            .ToArray();
        JsonElement[] referenceOnlyUnits = sourceClosure
            .GetProperty("compiledReferenceOnlyLegacyUnits")
            .EnumerateArray()
            .ToArray();
        JsonElement[] referenceOnlyResources = sourceClosure
            .GetProperty("compiledReferenceOnlyLegacyResources")
            .EnumerateArray()
            .ToArray();
        JsonElement[] runtimeData = sourceClosure
            .GetProperty("runtimeData")
            .EnumerateArray()
            .ToArray();
        string[] executedPaths = executedUnits
            .Select(item => item.GetProperty("path").GetString()!)
            .ToArray();
        string[] referenceOnlyPaths = referenceOnlyUnits
            .Select(item => item.GetProperty("path").GetString()!)
            .ToArray();
        Assert.Equal(25, executedPaths.Length);
        Assert.Equal(24, referenceOnlyPaths.Length);
        Assert.Equal(
            49,
            executedPaths
                .Concat(referenceOnlyPaths)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.Empty(
            executedPaths.Intersect(
                referenceOnlyPaths,
                StringComparer.Ordinal));
        Assert.Equal(2, referenceOnlyResources.Length);
        Assert.Single(runtimeData);
        Assert.Contains("QrmStn.pas", referenceOnlyPaths);
        Assert.Contains("QrnStn.pas", referenceOnlyPaths);
        Assert.Contains("Qsb.pas", referenceOnlyPaths);
        Assert.Contains("VCL/QuickAvg.pas", referenceOnlyPaths);
        if (Directory.Exists(RepositoryPaths.LegacyRoot))
        {
            foreach (JsonElement item in executedUnits
                         .Concat(referenceOnlyUnits)
                         .Concat(referenceOnlyResources)
                         .Concat(runtimeData))
            {
                string path = item.GetProperty("path").GetString()!;
                Assert.Equal(
                    item.GetProperty("sha256").GetString(),
                    FileSha256(
                        Path.Combine(
                            RepositoryPaths.LegacyRoot,
                            path.Replace(
                                '/',
                                Path.DirectorySeparatorChar))));
            }
        }

        JsonElement binding = capture.RootElement.GetProperty(
            "binding");
        Assert.Equal(
            FileSha256(CandidateFile("adapter-descriptor.json")),
            binding
                .GetProperty("adapterDescriptorSha256")
                .GetString());
        Assert.Equal(
            FileSha256(CandidateFile("case-contracts.json")),
            binding
                .GetProperty("caseContractsSha256")
                .GetString());
        Assert.Equal(
            FileSha256(CandidateFile("integration-requirements.json")),
            binding
                .GetProperty("integrationRequirementsSha256")
                .GetString());
        Assert.Equal(
            descriptorRoot.GetProperty("sourceSha256").GetString(),
            binding.GetProperty("sourceSha256").GetString());
        Assert.Equal(
            descriptorRoot
                .GetProperty("buildRecipeSha256")
                .GetString(),
            binding
                .GetProperty("buildRecipeSha256")
                .GetString());
        Assert.Equal(
            descriptorRoot
                .GetProperty("legacyReferenceDefinitionSha256")
                .GetString(),
            binding
                .GetProperty("legacyReferenceDefinitionSha256")
                .GetString());
        Assert.Equal(
            descriptorRoot
                .GetProperty("legacyBundleSha256")
                .GetString(),
            binding
                .GetProperty("legacyBundleSha256")
                .GetString());

        using JsonDocument bandwidthInput = JsonDocument.Parse(
            File.ReadAllBytes(
                CandidateFile(
                    "cases/audio.bandwidth-live-control.json")));
        Assert.Equal(
            [500, 250, 600],
            bandwidthInput.RootElement
                .GetProperty("controls")
                .EnumerateArray()
                .Select(
                    control => control
                        .GetProperty("bandwidthHz")
                        .GetInt32())
                .ToArray());

        foreach (string caseId in CaseIds)
        {
            LegacyAudioV3CandidateTarget.CandidateBinding caseBinding =
                LegacyAudioV3CandidateTarget.LoadAndValidateBinding(
                    caseId);
            Assert.Equal(caseId, caseBinding.Scenario);
        }
    }

    [Fact]
    [Trait("Category", Category)]
    public void SourceUsesRealCeRuntimePathsAndFailClosedGuards()
    {
        string source = File.ReadAllText(
            CandidateFile("LegacyOracle.lpr"));
        string[] requiredFragments =
        [
            "MainForm := TMainForm.CreateNew(nil);",
            "MainForm.Panel8MouseDown(",
            "MainForm.Shape2MouseDown(",
            "MainForm.SetBw(",
            "MainForm.SetQsk(",
            "Tst.GetAudio",
            "Tst.SendText(Tst.Me,",
            "Tst := TCWSST.Create",
            "Keyer := TFarnsKeyer.Create(",
            "inherited CreateStation;",
            "procedure TFixedStation.ProcessEvent(",
            "StubCalled('",
            "RequireNoHandles;",
            "AbstractStubCalls <> 0",
            "ProcessEventStubCalls <> 0",
            "RequireBoundFile(",
            "ValidateAdapterDescriptor(",
            "ExpectedDxccListSha256",
            "DXCC.LIST SHA-256 mismatch",
            "ExpectedZeroSingleSha256",
            "BlockData := nil;",
            "MainForm.Panel8.Width := 225;",
            "for Index := 0 to 10 do",
            "MainForm.SpinEdit1.MinValue := 10;",
            "MainForm.SpinEdit1.MaxValue := 120;",
            "MainForm.SpinEdit1.Value := 25;",
            "MainForm.VolumeSlider1.HintStep := 3;",
            "Ini.Activity := 2;",
            "Ini.Duration := 30;",
            "MainForm.AlSoundOut1.Enabled",
            "MainForm.AlWavFile1.IsOpen",
            "FreeAndNil(Tst);",
            "DestroyKeyer;",
            "FreeAndNil(gDXCCList);",
            "FreeAndNil(MainForm);",
        ];
        foreach (string fragment in requiredFragments)
        {
            Assert.Contains(
                fragment,
                source,
                StringComparison.Ordinal);
        }

        string[] linkedUnits =
        [
            "  Main,",
            "  Contest,",
            "  Station,",
            "  MyStn,",
            "  CWSST,",
            "  MorseKey,",
            "  FarnsKeyer,",
            "  SndOut,",
            "  WavFile,",
        ];
        foreach (string unit in linkedUnits)
        {
            Assert.Contains(unit, source, StringComparison.Ordinal);
        }

        string[] forbiddenFragments =
        [
            "Application.Run",
            "MainForm.FormCreate",
            "AlSoundOut1.Enabled := True",
            "AlWavFile1.Open",
        ];
        foreach (string fragment in forbiddenFragments)
        {
            Assert.DoesNotContain(
                fragment,
                source,
                StringComparison.Ordinal);
        }

        int dxccValidation = source.LastIndexOf(
            "if FileSha256(ParamStr(1) + 'DXCC.LIST')",
            StringComparison.Ordinal);
        int applicationInitialization = source.LastIndexOf(
            "Application.Initialize;",
            StringComparison.Ordinal);
        Assert.True(dxccValidation >= 0);
        Assert.True(applicationInitialization > dxccValidation);
    }

    [Fact]
    [Trait("Category", Category)]
    public void ContractsDistinguishExecutedAndReferenceOnlyPaths()
    {
        using JsonDocument contracts = LoadJson(
            "case-contracts.json");
        using JsonDocument recipe = LoadJson("build-recipe.json");
        string[] globalReferenceOnly = contracts.RootElement
            .GetProperty("legacyEvidenceClassification")
            .GetProperty("compiledReferenceOnlyForEveryCase")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
        Assert.Equal(
            recipe.RootElement
                .GetProperty("sourceClosure")
                .GetProperty("compiledReferenceOnlyLegacyUnits")
                .EnumerateArray()
                .Select(
                    item => item.GetProperty("path").GetString()!),
            globalReferenceOnly);
        Assert.Contains(
            "Ini.Qrm, Ini.Qrn, Ini.Qsb, and Ini.Flutter are false",
            contracts.RootElement
                .GetProperty("legacyEvidenceClassification")
                .GetProperty("disabledConditionalAudioPaths")
                .GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "exact synthetic controlled-receiver stimulus",
            contracts.RootElement
                .GetProperty("runtimeContract")
                .GetProperty("remoteInput")
                .GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "They do not claim production TDxStation",
            contracts.RootElement
                .GetProperty("runtimeContract")
                .GetProperty("remoteInput")
                .GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "not labeled as clean no-INI UI defaults",
            contracts.RootElement
                .GetProperty("runtimeContract")
                .GetProperty("explicitCaseSettings")
                .GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "450 Hz pitch and 550 Hz bandwidth",
            contracts.RootElement
                .GetProperty("runtimeContract")
                .GetProperty("explicitCaseSettings")
                .GetString(),
            StringComparison.Ordinal);
        JsonElement[] cases = contracts.RootElement
            .GetProperty("cases")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(
            CaseIds.OrderBy(
                id => id,
                StringComparer.Ordinal),
            cases
                .Select(
                    item => item.GetProperty("id").GetString()!)
                .OrderBy(
                    id => id,
                    StringComparer.Ordinal));

        foreach (JsonElement item in cases)
        {
            string id = item.GetProperty("id").GetString()!;
            string input = item.GetProperty("input").GetString()!;
            Assert.Equal(
                FileSha256(RepositoryFile(input)),
                item.GetProperty("inputSha256").GetString());
            string[] executedUnits = item
                .GetProperty("executedLegacyUnits")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray();
            string[] referenceOnly = item
                .GetProperty("referenceOnlyLegacyLocations")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray();
            string[] substitutions = item
                .GetProperty("candidateSubstitutions")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray();
            Assert.NotEmpty(executedUnits);
            Assert.NotEmpty(referenceOnly);
            Assert.NotEmpty(substitutions);
            Assert.Contains("Main.pas", executedUnits);
            Assert.Contains("Contest.pas", executedUnits);
            Assert.Contains("Station.pas", executedUnits);
            Assert.Contains("MyStn.pas", executedUnits);
            Assert.Empty(
                executedUnits.Intersect(
                    referenceOnly,
                    StringComparer.Ordinal));
            Assert.Empty(
                executedUnits.Intersect(
                    globalReferenceOnly,
                    StringComparer.Ordinal));
            Assert.Contains(
                substitutions,
                value => value.Contains(
                    "nogui widgetset bootstrap",
                    StringComparison.Ordinal));

            if (id == "audio.sst-farnsworth-timing")
            {
                Assert.Contains("CWSST.pas", executedUnits);
                Assert.Contains(
                    "VCL/FarnsKeyer.pas",
                    executedUnits);
                Assert.Contains(
                    substitutions,
                    value => value.Contains(
                        "No contest or station subclass",
                        StringComparison.Ordinal));
            }
            else
            {
                Assert.DoesNotContain("CWSST.pas", executedUnits);
                Assert.Contains(
                    substitutions,
                    value => value.Contains(
                        "TFixedStation",
                        StringComparison.Ordinal));
                Assert.Contains(
                    substitutions,
                    value => value.Contains(
                        "TFailClosedContest",
                        StringComparison.Ordinal));
                Assert.Contains(
                    substitutions,
                    value => value.Contains(
                        "synthetic receiver stimulus",
                        StringComparison.Ordinal)
                        && value.Contains(
                            "not evidence of production TDxStation",
                            StringComparison.Ordinal));
            }
        }

        JsonElement rit = cases.Single(
            item => item.GetProperty("id").GetString()
                == "audio.rit-live-control");
        Assert.Contains(
            rit.GetProperty("executedLegacyEntryPoints")
                .EnumerateArray()
                .Select(value => value.GetString()!),
            value => value.Contains(
                "IncRit",
                StringComparison.Ordinal));
        Assert.Contains(
            rit.GetProperty("outputContract")
                .EnumerateArray()
                .Select(value => value.GetString()!),
            value => value.Contains(
                "fine dF plus or minus 2 rows are omitted",
                StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", Category)]
    public void CaptureProvesRebuildWidgetsetAndRuntimeSafetyControls()
    {
        using JsonDocument capture = LoadJson(
            "captured-checkpoints.json");
        JsonElement builds = capture.RootElement.GetProperty(
            "builds");
        Assert.True(
            builds
                .GetProperty("primaryBuildsByteIdentical")
                .GetBoolean());
        Assert.Equal(
            builds.GetProperty("primaryNoguiSha256").GetString(),
            builds
                .GetProperty("independentNoguiRebuildSha256")
                .GetString());
        Assert.NotEqual(
            builds.GetProperty("primaryNoguiSha256").GetString(),
            builds
                .GetProperty("win32CrossCheckSha256")
                .GetString());
        Assert.True(
            builds
                .GetProperty("win32BuildsByteIdentical")
                .GetBoolean());
        Assert.Equal(
            builds.GetProperty("win32CrossCheckSha256").GetString(),
            builds
                .GetProperty("independentWin32RebuildSha256")
                .GetString());

        var expectedCounts = new Dictionary<string, int>(
            StringComparer.Ordinal)
        {
            ["audio.bandwidth-live-control"] = 28,
            ["audio.operator-sidetone-pipeline"] = 74,
            ["audio.qsk-receiver-ducking"] = 74,
            ["audio.rit-live-control"] = 43,
            ["audio.sst-farnsworth-timing"] = 19,
        };
        var expectedSafetyRows = new Dictionary<string, int>(
            StringComparer.Ordinal)
        {
            ["audio.bandwidth-live-control"] = 2,
            ["audio.operator-sidetone-pipeline"] = 4,
            ["audio.qsk-receiver-ducking"] = 4,
            ["audio.rit-live-control"] = 7,
            ["audio.sst-farnsworth-timing"] = 2,
        };
        foreach (JsonElement item in capture.RootElement
                     .GetProperty("cases")
                     .EnumerateArray())
        {
            string id = item.GetProperty("id").GetString()!;
            Assert.Equal(
                expectedCounts[id],
                item.GetProperty("valueCount").GetInt32());
            Assert.Equal(
                expectedSafetyRows[id],
                item.GetProperty("safetyRowCount").GetInt32());
            Assert.Equal(
                expectedSafetyRows[id],
                item.GetProperty("teardownRowCount").GetInt32());
            string valuesSha256 =
                item.GetProperty("valuesSha256").GetString()!;
            Assert.Equal(
                valuesSha256,
                item.GetProperty(
                        "independentNoguiValuesSha256")
                    .GetString());
            Assert.Equal(
                valuesSha256,
                item.GetProperty("win32ValuesSha256").GetString());
            Assert.True(
                ParityCanonicalJson.IsLowercaseSha256(
                    valuesSha256));
            Assert.True(
                ParityCanonicalJson.IsLowercaseSha256(
                    item.GetProperty("emittedJsonLineSha256")
                        .GetString()));
        }

        string[] rit = Checkpoints(
            capture,
            "audio.rit-live-control");
        Assert.Contains(
            "startup-request-count=5|sample-counts=1,1,1,1,1"
            + "|first-full-absolute-block=6",
            rit);
        Assert.Contains(
            rit,
            value => value.StartsWith(
                "block[4]|absolute-block=10|filter-swap=true|",
                StringComparison.Ordinal)
                && value.Contains(
                    "|equal=false|",
                    StringComparison.Ordinal));
        Assert.Contains(
            rit,
            value => value.StartsWith(
                "block[14]|absolute-block=20|filter-swap=true|",
                StringComparison.Ordinal));
        Assert.Contains(
            rit,
            value => value.StartsWith(
                "contract=ce-runtime-rit-v3|",
                StringComparison.Ordinal)
                && value.Contains(
                    "|settings=explicit-case-stimulus"
                    + "|clean-no-ini-ui-defaults=450/550|",
                    StringComparison.Ordinal));
        Assert.Contains(
            "rit-reversed-right=0,-25,-50,-75,-100",
            rit);
        Assert.Contains(
            "rit-hst-right=0,50,100,150,200",
            rit);
        Assert.DoesNotContain(
            rit,
            value => value.StartsWith(
                "rit-fine-",
                StringComparison.Ordinal));

        string[] bandwidth = Checkpoints(
            capture,
            "audio.bandwidth-live-control");
        Assert.Contains(
            bandwidth,
            value => value.StartsWith(
                "block[8]|absolute-block=14|",
                StringComparison.Ordinal)
                && value.Contains(
                    "|equal=false|",
                    StringComparison.Ordinal));
        Assert.Contains(
            bandwidth,
            value => value.StartsWith(
                "contract=ce-runtime-bandwidth-v3|",
                StringComparison.Ordinal)
                && value.Contains(
                    "|settings=explicit-case-stimulus"
                    + "|clean-no-ini-ui-defaults=450/550|",
                    StringComparison.Ordinal));

        string[] sidetone = Checkpoints(
            capture,
            "audio.operator-sidetone-pipeline");
        Assert.Contains(
            sidetone,
            value => value.StartsWith(
                "level[3]|block[4]|absolute-block=10"
                + "|filter-swap=true|peak=0.000000000"
                 + "|rms=0.000000000|",
                 StringComparison.Ordinal));
        Assert.Contains(
            sidetone,
            value => value.StartsWith(
                "contract=ce-runtime-local-audio-v3|",
                StringComparison.Ordinal)
                && value.Contains(
                    "|settings=explicit-case-stimulus"
                    + "|clean-no-ini-ui-defaults=450/550|",
                    StringComparison.Ordinal));
        Assert.Contains(
            "startup-all-levels-zero-single-sha256="
            + "df3f619804a92fdb4057192dc43dd748e"
            + "a778adc52bc498ce80524c014b81119"
            + "|request-count=20",
            sidetone);
        string[] qsk = Checkpoints(
            capture,
            "audio.qsk-receiver-ducking");
        Assert.Contains(
            qsk,
            value => value.StartsWith(
                "level[3]|block[4]|absolute-block=10"
                + "|filter-swap=true|peak=0.310655087"
                 + "|rms=0.192164607|",
                 StringComparison.Ordinal));
        Assert.Contains(
            "startup-all-levels-zero-single-sha256="
            + "df3f619804a92fdb4057192dc43dd748e"
            + "a778adc52bc498ce80524c014b81119"
            + "|request-count=20",
            qsk);

        string[] sst = Checkpoints(
            capture,
            "audio.sst-farnsworth-timing");
        Assert.Contains("true-length[0]=71363", sst);
        Assert.Contains("padded-length[0]=71680", sst);
        Assert.Contains("true-length[1]=136971", sst);
        Assert.Contains("padded-length[1]=137216", sst);

        foreach (JsonElement item in capture.RootElement
                     .GetProperty("cases")
                     .EnumerateArray())
        {
            string[] checkpoints = item
                .GetProperty("checkpoints")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray();
            Assert.Contains(
                checkpoints,
                value => value.Contains(
                    "|handles=0|abstract-stub-calls=0"
                    + "|process-event-stub-calls=0"
                    + "|sound-enabled=false|wav-open=false",
                    StringComparison.Ordinal));
            Assert.Contains(
                checkpoints,
                value => value.EndsWith(
                    "|tst=nil|keyer=nil|gdxcc-list=nil"
                    + "|main-form=nil",
                    StringComparison.Ordinal));
        }
    }

    [Fact]
    [Trait("Category", Category)]
    public void ObservationParserRejectsMetadataDrift()
    {
        LegacyAudioV3CandidateTarget.CandidateBinding binding =
            LegacyAudioV3CandidateTarget.LoadAndValidateBinding(
                "audio.bandwidth-live-control");
        JsonObject output = ValidOutput(binding);
        LegacyAudioV3CandidateObservation observation =
            LegacyAudioV3CandidateTarget.ParseAndValidate(
                output.ToJsonString(),
                binding);
        Assert.Equal(binding.Scenario, observation.Scenario);
        Assert.Equal(["value=1"], observation.Values);

        JsonObject changedHash = ValidOutput(binding);
        changedHash["caseDefinitionSha256"] = new string('0', 64);
        Assert.Throws<InvalidDataException>(
            () => LegacyAudioV3CandidateTarget.ParseAndValidate(
                changedHash.ToJsonString(),
                binding));

        JsonObject extraProperty = ValidOutput(binding);
        extraProperty["unexpected"] = true;
        Assert.Throws<InvalidDataException>(
            () => LegacyAudioV3CandidateTarget.ParseAndValidate(
                extraProperty.ToJsonString(),
                binding));
    }

    [Fact]
    [Trait("Category", Category)]
    public void MaterializedLegacySourceAndDxccMutationsAreRejected()
    {
        LegacyAudioV3CandidateTarget.CandidateBinding binding =
            LegacyAudioV3CandidateTarget.LoadAndValidateBinding(
                "audio.bandwidth-live-control");
        string temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"morse-runner-v3-materialization-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            byte[] dxccBytes = "pinned DXCC data"u8.ToArray();
            byte[] sourceBytes = "pinned CE source"u8.ToArray();
            string dxccPath = Path.Combine(
                temporaryRoot,
                "DXCC.LIST");
            string sourcePath = Path.Combine(
                temporaryRoot,
                "Main.pas");
            File.WriteAllBytes(dxccPath, dxccBytes);
            File.WriteAllBytes(sourcePath, sourceBytes);
            binding = binding with
            {
                LegacyMaterializationFiles =
                [
                    new(
                        "DXCC.LIST",
                        FileSha256(dxccPath)),
                    new(
                        "Main.pas",
                        FileSha256(sourcePath)),
                ],
            };
            LegacyAudioV3CandidateTarget.ValidateLegacyMaterialization(
                binding,
                temporaryRoot);
            foreach ((string Identity, byte[] OriginalBytes) mutation in
                     new[]
                     {
                         ("DXCC.LIST", dxccBytes),
                         ("Main.pas", sourceBytes),
                     })
            {
                string path = Path.Combine(
                    temporaryRoot,
                    mutation.Identity.Replace(
                        '/',
                        Path.DirectorySeparatorChar));
                File.AppendAllText(path, "tampered");
                InvalidDataException exception =
                    Assert.Throws<InvalidDataException>(
                        () => LegacyAudioV3CandidateTarget
                            .ValidateLegacyMaterialization(
                                binding,
                                temporaryRoot));
                Assert.Contains(
                    mutation.Identity,
                    exception.Message,
                    StringComparison.Ordinal);
                File.WriteAllBytes(
                    path,
                    mutation.OriginalBytes);
                LegacyAudioV3CandidateTarget
                    .ValidateLegacyMaterialization(
                        binding,
                        temporaryRoot);
            }
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Theory]
    [Trait("Category", Category)]
    [InlineData("revision")]
    [InlineData("tree")]
    [InlineData("bundle")]
    public void PinnedLegacyIdentityMetadataMutationIsRejected(
        string mutation)
    {
        LegacyAudioV3CandidateTarget.CandidateBinding binding =
            LegacyAudioV3CandidateTarget.LoadAndValidateBinding(
                "audio.bandwidth-live-control");
        LegacyAudioV3CandidateTarget.CandidateBinding changed =
            mutation switch
            {
                "revision" => binding with
                {
                    LegacyRevision = new string('1', 40),
                },
                "tree" => binding with
                {
                    LegacyTree = new string('2', 40),
                },
                "bundle" => binding with
                {
                    LegacyBundleSha256 = new string('3', 64),
                },
                _ => throw new InvalidOperationException(),
            };

        Assert.Throws<InvalidDataException>(
            () => LegacyAudioV3CandidateTarget
                .ValidatePinnedLegacyReferenceArtifacts(
                    changed,
                    binding.LegacyReferenceDefinitionPath,
                    binding.LegacyBundlePath));
    }

    [Fact]
    [Trait("Category", Category)]
    public void AlteredLegacyReferenceDefinitionIsRejected()
    {
        LegacyAudioV3CandidateTarget.CandidateBinding binding =
            LegacyAudioV3CandidateTarget.LoadAndValidateBinding(
                "audio.bandwidth-live-control");
        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"morse-runner-v3-reference-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            string alteredDefinition = Path.Combine(
                temporaryDirectory,
                "legacy-reference.json");
            JsonObject document = JsonNode.Parse(
                File.ReadAllText(
                    binding.LegacyReferenceDefinitionPath))!
                .AsObject();
            document["revision"] = new string('4', 40);
            File.WriteAllText(
                alteredDefinition,
                document.ToJsonString());

            Assert.Throws<InvalidDataException>(
                () => LegacyAudioV3CandidateTarget
                    .ValidatePinnedLegacyReferenceArtifacts(
                        binding,
                        alteredDefinition,
                        binding.LegacyBundlePath));
        }
        finally
        {
            Directory.Delete(
                temporaryDirectory,
                recursive: true);
        }
    }

    [Fact]
    [Trait("Category", Category)]
    public async Task CompiledOracleRejectsBindingAndDxccTampering()
    {
        string? executable =
            LegacyAudioV3CandidateTarget.FindLocalExecutable();
        if (executable is null)
        {
            return;
        }

        LegacyAudioV3CandidateTarget.CandidateBinding binding =
            LegacyAudioV3CandidateTarget.LoadAndValidateBinding(
                "audio.bandwidth-live-control");
        foreach ((
                     string? SourceSha256,
                     string? BuildRecipeSha256,
                     string? CaseDefinitionSha256,
                     string ExpectedError) mutation in
                 new (
                     string? SourceSha256,
                     string? BuildRecipeSha256,
                     string? CaseDefinitionSha256,
                     string ExpectedError)[]
                 {
                     (
                         new string('0', 64),
                         null,
                         null,
                         "source file SHA-256 mismatch"),
                     (
                         null,
                         new string('0', 64),
                         null,
                         "build recipe file SHA-256 mismatch"),
                     (
                         null,
                         null,
                         new string('0', 64),
                         "case definition file SHA-256 mismatch"),
                 })
        {
            RawCandidateResult result = await RunRawCandidateAsync(
                executable,
                binding,
                RepositoryPaths.LegacyRoot,
                sourceSha256:
                    mutation.SourceSha256
                    ?? binding.SourceSha256,
                buildRecipeSha256:
                    mutation.BuildRecipeSha256
                    ?? binding.BuildRecipeSha256,
                caseDefinitionSha256:
                    mutation.CaseDefinitionSha256
                    ?? binding.CaseContractsSha256,
                descriptorPath: binding.DescriptorPath);
            Assert.Equal(4, result.ExitCode);
            Assert.Empty(result.StandardOutput);
            Assert.Contains(
                mutation.ExpectedError,
                result.StandardError,
                StringComparison.Ordinal);
        }

        string descriptorRoot = Path.Combine(
            Path.GetTempPath(),
            $"morse-runner-v3-descriptor-{Guid.NewGuid():N}");
        string legacyRoot = Path.Combine(
            Path.GetTempPath(),
            $"morse-runner-v3-dxcc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(
            Path.Combine(
                descriptorRoot,
                "tests",
                "parity",
                "legacy-oracle",
                "v3"));
        Directory.CreateDirectory(legacyRoot);
        try
        {
            string alteredDescriptor = Path.Combine(
                descriptorRoot,
                "tests",
                "parity",
                "legacy-oracle",
                "v3",
                "adapter-descriptor.json");
            JsonObject descriptor = JsonNode.Parse(
                File.ReadAllText(binding.DescriptorPath))!
                .AsObject();
            descriptor["sourceSha256"] = new string('5', 64);
            File.WriteAllText(
                alteredDescriptor,
                descriptor.ToJsonString());
            RawCandidateResult descriptorResult =
                await RunRawCandidateAsync(
                    executable,
                    binding,
                    RepositoryPaths.LegacyRoot,
                    binding.SourceSha256,
                    binding.BuildRecipeSha256,
                    binding.CaseContractsSha256,
                    alteredDescriptor);
            Assert.Equal(4, descriptorResult.ExitCode);
            Assert.Empty(descriptorResult.StandardOutput);
            Assert.Contains(
                "adapter descriptor source SHA-256 mismatch",
                descriptorResult.StandardError,
                StringComparison.Ordinal);

            string dxccPath = Path.Combine(
                legacyRoot,
                "DXCC.LIST");
            File.Copy(
                Path.Combine(
                    RepositoryPaths.LegacyRoot,
                    "DXCC.LIST"),
                dxccPath);
            File.AppendAllText(dxccPath, "tampered");
            RawCandidateResult dxccResult =
                await RunRawCandidateAsync(
                    executable,
                    binding,
                    legacyRoot,
                    binding.SourceSha256,
                    binding.BuildRecipeSha256,
                    binding.CaseContractsSha256,
                    binding.DescriptorPath);
            Assert.Equal(4, dxccResult.ExitCode);
            Assert.Empty(dxccResult.StandardOutput);
            Assert.Contains(
                "DXCC.LIST SHA-256 mismatch",
                dxccResult.StandardError,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(
                descriptorRoot,
                recursive: true);
            Directory.Delete(
                legacyRoot,
                recursive: true);
        }
    }

    [Fact]
    [Trait("Category", Category)]
    public async Task LocallyBuiltCandidatesMatchRetainedDigests()
    {
        string? primary =
            LegacyAudioV3CandidateTarget.FindLocalExecutable();
        if (primary is null)
        {
            using JsonDocument requirements = LoadJson(
                "integration-requirements.json");
            Assert.False(
                requirements.RootElement
                    .GetProperty("externalDescriptorBuildSupported")
                    .GetBoolean());
            return;
        }

        string artifactRoot = Path.GetDirectoryName(
            Path.GetDirectoryName(primary)!)!;
        string[] executablePaths =
        [
            primary,
            Path.Combine(
                artifactRoot,
                "release-nogui-b",
                "LegacyOracleV3.exe"),
            Path.Combine(
                artifactRoot,
                "release-win32",
                "LegacyOracleV3.exe"),
        ];
        foreach (string executable in executablePaths)
        {
            Assert.True(
                File.Exists(executable),
                $"Missing local candidate executable: {executable}");
        }

        foreach (string caseId in CaseIds)
        {
            LegacyAudioV3CandidateTarget.CandidateBinding binding =
                LegacyAudioV3CandidateTarget.LoadAndValidateBinding(
                    caseId);
            LegacyAudioV3CandidateObservation[] observations =
                await Task.WhenAll(
                    executablePaths.Select(
                        executable =>
                            LegacyAudioV3CandidateTarget.ExecuteAsync(
                                executable,
                                RepositoryPaths.LegacyRoot,
                                caseId,
                                TestContext.Current
                                    .CancellationToken)));
            foreach (LegacyAudioV3CandidateObservation observation
                     in observations)
            {
                Assert.Equal(
                    binding.ExpectedValueCount,
                    observation.Values.Count);
                Assert.Equal(
                    binding.ExpectedValuesSha256,
                    observation.ValuesSha256);
                Assert.Equal(
                    binding.ExpectedEmittedJsonLineSha256,
                    observation.EmittedJsonLineSha256);
            }

            Assert.Equal(
                observations[0].Values,
                observations[1].Values);
            Assert.Equal(
                observations[0].Values,
                observations[2].Values);
        }
    }

    private static async Task<RawCandidateResult> RunRawCandidateAsync(
        string executable,
        LegacyAudioV3CandidateTarget.CandidateBinding binding,
        string legacyRoot,
        string sourceSha256,
        string buildRecipeSha256,
        string caseDefinitionSha256,
        string descriptorPath)
    {
        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"morse-runner-v3-raw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            string input = Path.Combine(
                temporaryDirectory,
                $"{binding.InputSha256}.json");
            File.Copy(binding.InputPath, input);
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            string fullLegacyRoot = Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(legacyRoot))
                + Path.DirectorySeparatorChar;
            foreach (string argument in new[]
                     {
                         fullLegacyRoot,
                         binding.Scenario,
                         "LegacyOracleTarget",
                         "legacy-oracle-v3",
                         binding.Source,
                         sourceSha256,
                         binding.BuildRecipe,
                         buildRecipeSha256,
                         caseDefinitionSha256,
                         binding.InputSha256,
                         input,
                         binding.SourcePath,
                         binding.BuildRecipePath,
                         binding.CaseContractsPath,
                         descriptorPath,
                     })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "The raw v3 candidate process did not start.");
            Task<string> output =
                process.StandardOutput.ReadToEndAsync(
                    TestContext.Current.CancellationToken);
            Task<string> error =
                process.StandardError.ReadToEndAsync(
                    TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(
                TestContext.Current.CancellationToken);
            return new(
                process.ExitCode,
                await output,
                await error);
        }
        finally
        {
            Directory.Delete(
                temporaryDirectory,
                recursive: true);
        }
    }

    private static JsonObject ValidOutput(
        LegacyAudioV3CandidateTarget.CandidateBinding binding)
    {
        return new()
        {
            ["scenario"] = binding.Scenario,
            ["adapterId"] = "LegacyOracleTarget",
            ["versionId"] = "legacy-oracle-v3",
            ["source"] = binding.Source,
            ["sourceSha256"] = binding.SourceSha256,
            ["buildRecipe"] = binding.BuildRecipe,
            ["buildRecipeSha256"] = binding.BuildRecipeSha256,
            ["caseDefinitionSha256"] =
                binding.CaseContractsSha256,
            ["inputSha256"] = binding.InputSha256,
            ["values"] = new JsonArray("value=1"),
        };
    }

    private static string[] Checkpoints(
        JsonDocument capture,
        string caseId)
    {
        return capture.RootElement
            .GetProperty("cases")
            .EnumerateArray()
            .Single(
                item => item.GetProperty("id").GetString()
                    == caseId)
            .GetProperty("checkpoints")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
    }

    private static JsonDocument LoadJson(string name)
    {
        return JsonDocument.Parse(
            File.ReadAllBytes(CandidateFile(name)));
    }

    private static string CandidateFile(string name)
    {
        return RepositoryFile(
            $"tests/parity/legacy-oracle/v3/{name}");
    }

    private static string RepositoryFile(string identity)
    {
        return Path.GetFullPath(
            Path.Combine(
                RepositoryPaths.Root,
                identity.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));
    }

    private static string FileSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(
            SHA256.HashData(stream));
    }

    private sealed record RawCandidateResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
