using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MorseRunner.LegacyParity.Tests;

public sealed class ParityInfrastructureTests
{
    private static readonly string[] SstFarnsworthMessages =
    [
        "PARIS TEST",
        "K1ABC 599 123",
    ];

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void EmptyTargetSelectionDefaultsToXPlat()
    {
        Assert.Equal(
            ParityTargetKind.XPlat,
            ParityTargetSelection.Parse(null));
        Assert.Equal(
            ParityTargetKind.XPlat,
            ParityTargetSelection.Parse(String.Empty));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void XPlatSelectionDoesNotConstructLegacyTarget()
    {
        bool legacyConstructed = false;

        IParityTarget target = ParityTargetFactory.Create(
            ParityTargetKind.XPlat,
            () =>
            {
                legacyConstructed = true;
                return new StubTarget();
            },
            static () => new StubTarget());

        Assert.IsType<StubTarget>(target);
        Assert.False(legacyConstructed);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void InvalidTargetSelectionFails()
    {
        InvalidOperationException exception = Assert.Throws<
            InvalidOperationException>(
            () => ParityTargetSelection.Parse("Both"));

        Assert.Contains(
            ParityTargetSelection.EnvironmentVariableName,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void ActiveRegistryContainsEveryExecutableCase()
    {
        Assert.Equal(
            [
                "audio.bandwidth-runtime-narrow-second-cq-block-seed-12345",
                "audio.deterministic-random-primitives-seed-12345",
                "audio.flutter-no-station-noise-invariance-seed-12345",
                "audio.operator-monitor-minus-60db-mute-first-cq-block-seed-12345",
                "audio.operator-monitor-runtime-mute-second-cq-block-seed-12345",
                "audio.qrm-caller-collision-retry-limit-seed-24680",
                "audio.qrm-first-triggered-station-seed-1843",
                "audio.qrm-no-trigger-invariance-seed-12345",
                "audio.qrn-background-sparse-impulses-seed-12345",
                "audio.qrn-burst-station-lifecycle-seed-1903",
                "audio.qsb-no-station-noise-invariance-seed-12345",
                "audio.qsb-runtime-toggle-active-station-seed-12345",
                "audio.qsk-receiver-ducking-first-cq-block-seed-12345",
                "audio.qsk-runtime-enable-second-cq-block-seed-12345",
                "audio.realistic-hiss-noise-floor",
                "audio.receiver-hiss-shared-random-checkpoint-seed-12345",
                "audio.rit-runtime-plus-50-second-caller-block-seed-12345",
                "audio.rit-upper-clamp-extra-click-second-caller-block-seed-12345",
                "audio.sst-farnsworth-envelope-timing",
                "audio.startup-warmup-and-filter-timing-fresh-seed-12345",
                "contest.arrldx-high-r1-power-remote-exchange-format-seed-12345",
                "contest.cqww-random-consumption-remote-exchange-format-seed-12345",
                "contest.cwt-remote-exchange-format-seed-12345",
                "contest.default-two-field-remote-exchange-format-seed-12345",
                "contest.exchange-shapes",
                "contest.fieldday-remote-exchange-format-seed-12345",
                "contest.full-cut-numeric-remote-exchange-format-seed-12345",
                "contest.hst-remote-exchange-format-seed-12345",
                "contest.jarl-random-cut-remote-exchange-format-seed-12345",
                "contest.lid-serial-correction-remote-exchange-format-seed-16",
                "contest.naqp-remote-exchange-format-seed-12345",
                "contest.rare-rst-error-remote-exchange-format-seed-12345",
                "contest.sst-remote-exchange-format-seed-12345",
                "contest.sweepstakes-remote-exchange-format-seed-12345",
                "contest.wpx-custom-range-remote-exchange-format-seed-12345",
                "contest.wpx-midcontest-remote-exchange-format-seed-12345",
                "engine.contest-specific-cq-tu-station-id-seed-12345",
                "engine.start-silent-empty-enter-cq-seed-12345",
                "ux.enter-esm-partial-call-message-selection-live",
            ],
            ParityAcceptanceRegistry.AllIds,
            StringComparer.Ordinal);
        Assert.Equal(
            [
                "audio.bandwidth-runtime-narrow-second-cq-block-seed-12345",
                "audio.deterministic-random-primitives-seed-12345",
                "audio.flutter-no-station-noise-invariance-seed-12345",
                "audio.operator-monitor-minus-60db-mute-first-cq-block-seed-12345",
                "audio.operator-monitor-runtime-mute-second-cq-block-seed-12345",
                "audio.qrm-caller-collision-retry-limit-seed-24680",
                "audio.qrm-first-triggered-station-seed-1843",
                "audio.qrm-no-trigger-invariance-seed-12345",
                "audio.qrn-background-sparse-impulses-seed-12345",
                "audio.qrn-burst-station-lifecycle-seed-1903",
                "audio.qsb-no-station-noise-invariance-seed-12345",
                "audio.qsb-runtime-toggle-active-station-seed-12345",
                "audio.qsk-receiver-ducking-first-cq-block-seed-12345",
                "audio.qsk-runtime-enable-second-cq-block-seed-12345",
                "audio.realistic-hiss-noise-floor",
                "audio.receiver-hiss-shared-random-checkpoint-seed-12345",
                "audio.rit-runtime-plus-50-second-caller-block-seed-12345",
                "audio.rit-upper-clamp-extra-click-second-caller-block-seed-12345",
                "audio.sst-farnsworth-envelope-timing",
                "audio.startup-warmup-and-filter-timing-fresh-seed-12345",
                "contest.arrldx-high-r1-power-remote-exchange-format-seed-12345",
                "contest.cqww-random-consumption-remote-exchange-format-seed-12345",
                "contest.cwt-remote-exchange-format-seed-12345",
                "contest.default-two-field-remote-exchange-format-seed-12345",
                "contest.exchange-shapes",
                "contest.fieldday-remote-exchange-format-seed-12345",
                "contest.full-cut-numeric-remote-exchange-format-seed-12345",
                "contest.hst-remote-exchange-format-seed-12345",
                "contest.jarl-random-cut-remote-exchange-format-seed-12345",
                "contest.lid-serial-correction-remote-exchange-format-seed-16",
                "contest.naqp-remote-exchange-format-seed-12345",
                "contest.rare-rst-error-remote-exchange-format-seed-12345",
                "contest.sst-remote-exchange-format-seed-12345",
                "contest.sweepstakes-remote-exchange-format-seed-12345",
                "contest.wpx-custom-range-remote-exchange-format-seed-12345",
                "contest.wpx-midcontest-remote-exchange-format-seed-12345",
                "engine.contest-specific-cq-tu-station-id-seed-12345",
                "engine.start-silent-empty-enter-cq-seed-12345",
                "ux.enter-esm-partial-call-message-selection-live",
            ],
            ParityAcceptanceRegistry.ActiveIds,
            StringComparer.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void CaseIdSelectsItsExactXPlatAdapter()
    {
        Assert.IsType<XPlatRandomPrimitivesTarget>(
            ParityAcceptanceRegistry
                .Get("audio.deterministic-random-primitives-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatFlutterNoStationNoiseInvarianceTarget>(
            ParityAcceptanceRegistry
                .Get(
                    "audio.flutter-no-station-noise-invariance-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatQrmNoTriggerInvarianceTarget>(
            ParityAcceptanceRegistry
                .Get("audio.qrm-no-trigger-invariance-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatQrmCallerCollisionTarget>(
            ParityAcceptanceRegistry
                .Get(
                    "audio.qrm-caller-collision-retry-limit-seed-24680")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatQrmFirstTriggeredStationTarget>(
            ParityAcceptanceRegistry
                .Get("audio.qrm-first-triggered-station-seed-1843")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatQrnBackgroundSparseImpulsesTarget>(
            ParityAcceptanceRegistry
                .Get(
                    "audio.qrn-background-sparse-impulses-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatQrnBurstStationLifecycleTarget>(
            ParityAcceptanceRegistry
                .Get(
                    "audio.qrn-burst-station-lifecycle-seed-1903")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatQsbNoStationNoiseInvarianceTarget>(
            ParityAcceptanceRegistry
                .Get(
                    "audio.qsb-no-station-noise-invariance-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatQsbRuntimeToggleTarget>(
            ParityAcceptanceRegistry
                .Get(
                    "audio.qsb-runtime-toggle-active-station-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatRealisticHissNoiseFloorTarget>(
            ParityAcceptanceRegistry
                .Get("audio.realistic-hiss-noise-floor")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatReceiverHissSharedRandomCheckpointTarget>(
            ParityAcceptanceRegistry
                .Get(
                    "audio.receiver-hiss-shared-random-checkpoint"
                    + "-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatSstFarnsworthTarget>(
            ParityAcceptanceRegistry
                .Get("audio.sst-farnsworth-envelope-timing")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatStartupWarmupFilterTimingTarget>(
            ParityAcceptanceRegistry
                .Get(
                    "audio.startup-warmup-and-filter-timing"
                    + "-fresh-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatContestRulesTarget>(
            ParityAcceptanceRegistry
                .Get("contest.exchange-shapes")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatCwtRemoteExchangeFormatTarget>(
            ParityAcceptanceRegistry
                .Get("contest.cwt-remote-exchange-format-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatDefaultTwoFieldRemoteExchangeFormatTarget>(
            ParityAcceptanceRegistry
                .Get(
                    "contest.default-two-field-remote-exchange-format-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatFieldDayRemoteExchangeFormatTarget>(
            ParityAcceptanceRegistry
                .Get(
                    "contest.fieldday-remote-exchange-format-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatFullCutNumericRemoteExchangeFormatTarget>(
            ParityAcceptanceRegistry
                .Get(
                    "contest.full-cut-numeric-remote-exchange-format-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatArrlDxHighR1PowerRemoteExchangeFormatTarget>(
            ParityAcceptanceRegistry
                .Get(
                    "contest.arrldx-high-r1-power-remote-exchange-format-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatCqwwRandomConsumptionRemoteExchangeFormatTarget>(
            ParityAcceptanceRegistry
                .Get(
                    "contest.cqww-random-consumption-remote-exchange-format-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatJarlRandomCutRemoteExchangeFormatTarget>(
            ParityAcceptanceRegistry
                .Get(
                    "contest.jarl-random-cut-remote-exchange-format-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatRareRstErrorRemoteExchangeFormatTarget>(
            ParityAcceptanceRegistry
                .Get(
                    "contest.rare-rst-error-remote-exchange-format-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatLidSerialCorrectionRemoteExchangeFormatTarget>(
            ParityAcceptanceRegistry
                .Get(
                    "contest.lid-serial-correction-remote-exchange-format-seed-16")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatHstRemoteExchangeFormatTarget>(
            ParityAcceptanceRegistry
                .Get("contest.hst-remote-exchange-format-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatNaqpRemoteExchangeFormatTarget>(
            ParityAcceptanceRegistry
                .Get("contest.naqp-remote-exchange-format-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatSstRemoteExchangeFormatTarget>(
            ParityAcceptanceRegistry
                .Get("contest.sst-remote-exchange-format-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatSweepstakesRemoteExchangeFormatTarget>(
            ParityAcceptanceRegistry
                .Get(
                    "contest.sweepstakes-remote-exchange-format-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatWpxMidContestRemoteExchangeFormatTarget>(
            ParityAcceptanceRegistry
                .Get(
                    "contest.wpx-midcontest-remote-exchange-format"
                    + "-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatWpxCustomRangeRemoteExchangeFormatTarget>(
            ParityAcceptanceRegistry
                .Get(
                    "contest.wpx-custom-range-remote-exchange-format"
                    + "-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatStartSilentEmptyEnterCqTarget>(
            ParityAcceptanceRegistry
                .Get(
                    "engine.start-silent-empty-enter-cq-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatContestOperatorMessagesTarget>(
            ParityAcceptanceRegistry
                .Get(
                    "engine.contest-specific-cq-tu-station-id-seed-12345")
                .CreateTarget(ParityTargetKind.XPlat)());
        Assert.IsType<XPlatEnterEsmTarget>(
            ParityAcceptanceRegistry
                .Get("ux.enter-esm-partial-call-message-selection-live")
                .CreateTarget(ParityTargetKind.XPlat)());
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void EveryExecutableCaseMatchesItsManifestBinding()
    {
        foreach (string parityId in ParityAcceptanceRegistry.AllIds)
        {
            ParityCertificationCase definition =
                ParityCertificationCase.LoadForInspection(parityId);
            ParityAcceptanceRegistry.Get(parityId)
                .ValidateManifestBinding(definition);
        }
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void ActiveCaseIsBoundToExactRegisteredAdapters()
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                "contest.exchange-shapes");
        ParityAcceptanceRegistration registration =
            ParityAcceptanceRegistry.Get(definition.Id);

        Assert.Equal(
            ["LegacyOracleTarget", "XPlatContestRulesTarget"],
            definition.TargetAdapters,
            StringComparer.Ordinal);
        Assert.Equal(
            ["contest.exchange-shapes-and-constructor-metadata"],
            definition.ObligationIds,
            StringComparer.Ordinal);
        Assert.Equal(
            "contest-exchange-shape-mismatch",
            definition.FunctionalDivergenceCode);
        registration.ValidateManifestBinding(definition);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void RegistryRejectsManifestAdapterMismatch()
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                "contest.exchange-shapes");
        ParityCertificationCase invalid = definition with
        {
            TargetAdapters =
            [
                "LegacyOracleTarget",
                "WrongXPlatAdapter",
            ],
        };

        Assert.Throws<InvalidDataException>(
            () => ParityAcceptanceRegistry.Get(definition.Id)
                .ValidateManifestBinding(invalid));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void ActiveCaseHashesMatchCanonicalSources()
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                "contest.exchange-shapes");
        string fixtureHash = Convert.ToHexStringLower(
            SHA256.HashData(
                File.ReadAllBytes(definition.FixturePath)));

        Assert.Equal(
            fixtureHash,
            definition.FixtureSha256);
        Assert.Matches(
            "^[0-9a-f]{64}$",
            definition.CaseDefinitionSha256);
        Assert.Matches(
            "^[0-9a-f]{64}$",
            definition.FixtureSha256);
    }

    [Theory]
    [Trait("Category", "ParityInfrastructure")]
    [InlineData(
        "legacy-oracle-v1",
        "tests/parity/legacy-oracle/LegacyOracle.lpr",
        "tests/parity/legacy-oracle/v1/build-recipe.json")]
    [InlineData(
        "legacy-oracle-v1",
        "tests/parity/legacy-oracle/v1/LegacyOracle.lpr",
        "tests/parity/legacy-oracle/LegacyOracle.lpr")]
    [InlineData(
        "legacy-oracle-v2",
        "tests/parity/legacy-oracle/v1/LegacyOracle.lpr",
        "tests/parity/legacy-oracle/v1/build-recipe.json")]
    [InlineData(
        "legacy-oracle-v01",
        "tests/parity/legacy-oracle/v1/LegacyOracle.lpr",
        "tests/parity/legacy-oracle/v1/build-recipe.json")]
    [InlineData(
        "legacy_oracle-v1",
        "tests/parity/legacy-oracle/v1/LegacyOracle.lpr",
        "tests/parity/legacy-oracle/v1/build-recipe.json")]
    public void ActiveOracleDescriptorMustBindItsExactVersionDirectory(
        string versionId,
        string source,
        string buildRecipe)
    {
        string manifestPath = Path.Combine(
            RepositoryPaths.Root,
            "tests",
            "parity",
            "parity-manifest.json");
        JsonObject manifest = JsonNode.Parse(
            File.ReadAllText(manifestPath))!.AsObject();
        JsonObject descriptor = manifest["cases"]!
            .AsArray()
            .Select(node => node!.AsObject())
            .Single(
                caseNode => StringComparer.Ordinal.Equals(
                    caseNode["id"]!.GetValue<string>(),
                    "contest.exchange-shapes"))["legacyOracle"]!
            .AsObject();
        descriptor["versionId"] = versionId;
        descriptor["source"] = source;
        descriptor["sourceSha256"] = ComputeRepositoryFileSha256(source);
        descriptor["buildRecipe"] = buildRecipe;
        descriptor["buildRecipeSha256"] =
            ComputeRepositoryFileSha256(buildRecipe);

        using TemporaryDirectory temporary = new();
        string mutatedManifest = Path.Combine(
            temporary.Path,
            "parity-manifest.json");
        File.WriteAllText(mutatedManifest, manifest.ToJsonString());

        Assert.Throws<InvalidDataException>(
            () => ParityCertificationCase.Load(
                "contest.exchange-shapes",
                mutatedManifest,
                RepositoryPaths.Root,
                requiredPlatform: null));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task ManifestContestInputDrivesXPlatAdapterOrder()
    {
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                "contest.exchange-shapes");
        ContestExchangeShapesInput typedInput =
            ContestExchangeShapesInput.Parse(
                definition.Scenario);
        string[] selectedIds =
        [
            typedInput.ContestIds[3],
            typedInput.ContestIds[0],
        ];
        string[] selectedExpectedValues =
        [
            definition.Scenario.ExpectedValues[3],
            definition.Scenario.ExpectedValues[0],
        ];
        JsonElement reorderedInput =
            JsonSerializer.SerializeToElement(
                new
                {
                    scenario = definition.Id,
                    contestIds = selectedIds,
                });
        ParityScenario reordered = new(
            definition.Id,
            definition.Scenario.Capability,
            selectedExpectedValues,
            reorderedInput);

        ParityObservation observation =
            await new XPlatContestRulesTarget().ExecuteAsync(
                reordered,
                TestContext.Current.CancellationToken);

        Assert.Equal(2, observation.Values.Count);
        Assert.StartsWith(
            selectedIds[0] + "|",
            observation.Values[0],
            StringComparison.Ordinal);
        Assert.StartsWith(
            selectedIds[1] + "|",
            observation.Values[1],
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void SstFarnsworthInputParsesTheExactCertifyingVector()
    {
        ParityScenario scenario = CreateSstFarnsworthScenario(
            Enumerable.Repeat("expected", 11).ToArray());

        SstFarnsworthTimingInput input =
            SstFarnsworthTimingInput.Parse(scenario);

        Assert.Equal(11_025, input.SampleRate);
        Assert.Equal(512, input.BlockSize);
        Assert.Equal(300_000, input.Amplitude);
        Assert.Equal(15, input.SendingWordsPerMinute);
        Assert.Equal(25, input.CharacterWordsPerMinute);
        Assert.Equal(
            SstFarnsworthMessages,
            input.Messages,
            StringComparer.Ordinal);
    }

    [Theory]
    [Trait("Category", "ParityInfrastructure")]
    [InlineData("sampleRate", 11_024)]
    [InlineData("blockSize", 256)]
    [InlineData("amplitude", 299_999)]
    [InlineData("sendingWpm", 14)]
    [InlineData("characterWpm", 24)]
    public void SstFarnsworthInputRejectsNumericVectorDrift(
        string propertyName,
        int value)
    {
        JsonObject input = CreateSstFarnsworthInput();
        input[propertyName] = value;

        AssertSstFarnsworthInputRejected(input);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void SstFarnsworthInputRejectsStructuralVectorDrift()
    {
        JsonObject extraProperty = CreateSstFarnsworthInput();
        extraProperty["unexpected"] = true;
        JsonObject missingProperty = CreateSstFarnsworthInput();
        Assert.True(missingProperty.Remove("amplitude"));
        JsonObject fractionalNumber = CreateSstFarnsworthInput();
        fractionalNumber["sampleRate"] = 11_025.5;
        JsonObject wrongMessageOrder = CreateSstFarnsworthInput();
        wrongMessageOrder["messages"] = new JsonArray(
            "K1ABC 599 123",
            "PARIS TEST");
        JsonObject wrongMessageType = CreateSstFarnsworthInput();
        wrongMessageType["messages"] = new JsonArray(
            "PARIS TEST",
            123);
        JsonObject missingMessage = CreateSstFarnsworthInput();
        missingMessage["messages"] = new JsonArray("PARIS TEST");

        foreach (JsonObject input in new[]
                 {
                     extraProperty,
                     missingProperty,
                     fractionalNumber,
                     wrongMessageOrder,
                     wrongMessageType,
                     missingMessage,
                 })
        {
            AssertSstFarnsworthInputRejected(input);
        }

        AssertSstFarnsworthInputRejected(
            CreateSstFarnsworthInput(),
            expectedValueCount: 10);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task
        SstFarnsworthTargetMatchesPinnedCeValuesAndIsDeterministic()
    {
        string[] expectedCurrentValues =
        [
            "configuration|sample-rate=11025|block-size=512"
            + "|amplitude=300000|sending-wpm=15|character-wpm=25",
            "message[0]=PARIS TEST",
            "timing[0]|sending-wpm=15|character-wpm=25"
            + "|amplitude=300000",
            "true-length[0]=71363",
            "padded-length[0]=71680",
            "float-sha256[0]="
            + "8d24e5cd0f054a2d846a01120bf57fa4"
            + "ca6c341937c1fc93c834fa5851fb1546",
            "message[1]=K1ABC 599 123",
            "timing[1]|sending-wpm=15|character-wpm=25"
            + "|amplitude=300000",
            "true-length[1]=136971",
            "padded-length[1]=137216",
            "float-sha256[1]="
            + "8e8c1b424dcd8925c46bebb8051d11d9"
            + "cf36c57fe4950d89592a80f3ea914a9e",
        ];
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                XPlatSstFarnsworthTarget.ParityId);
        var target = new XPlatSstFarnsworthTarget();
        ParityObservation first = await target.ExecuteAsync(
            definition.Scenario,
            TestContext.Current.CancellationToken);

        Assert.Equal(ParityTargetOutcome.Passed, first.Outcome);
        Assert.Null(first.FailureCode);
        Assert.Equal(
            expectedCurrentValues,
            first.Values,
            StringComparer.Ordinal);
        Assert.Equal(
            definition.Scenario.ExpectedValues,
            first.Values,
            StringComparer.Ordinal);

        ParityObservation second = await target.ExecuteAsync(
            definition.Scenario,
            TestContext.Current.CancellationToken);

        Assert.Equal(first.Outcome, second.Outcome);
        Assert.Equal(first.FailureCode, second.FailureCode);
        Assert.Equal(
            first.Values,
            second.Values,
            StringComparer.Ordinal);
    }

    [Theory]
    [Trait("Category", "ParityInfrastructure")]
    [InlineData("linux")]
    [InlineData("macos")]
    public async Task NonApplicableCaseCannotEmitPlatformCertification(
        string certificationPlatform)
    {
        using TemporaryDirectory temporary = new();
        string path = Path.Combine(
            temporary.Path,
            $"{certificationPlatform}-results.json");
        ParityCertificationCase definition =
            ParityCertificationCase.LoadForInspection(
                "contest.exchange-shapes");
        ParityCertificationCase windowsOnly = definition with
        {
            Platforms = ["windows"],
        };

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => ParityRunRecorder.RecordForPlatformAsync(
                    path,
                    windowsOnly,
                    ParityTargetKind.XPlat,
                    new ParityObservation(
                        ParityTargetOutcome.Passed,
                        windowsOnly.Scenario.ExpectedValues,
                        null,
                        "test:wrong-platform"),
                    adapterCompleted: true,
                    certificationPlatform,
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "not applicable",
            exception.Message,
            StringComparison.Ordinal);
        Assert.False(File.Exists(path));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void ObservedValuesDigestMatchesCrossLanguageKnownVector()
    {
        Assert.Equal(
            "99d80c4caa91790ab1700adadd0982d7058fbaedc691a644a253e361a85b927a",
            ParityObservedValuesDigest.Compute(
                ["alpha", "MØRSE", String.Empty]));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void CanonicalJsonDigestMatchesCrossLanguageKnownVector()
    {
        string vectorPath = Path.Combine(
            RepositoryPaths.Root,
            "tests",
            "parity",
            "canonical-json-vectors.json");
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllBytes(vectorPath));
        JsonElement root = document.RootElement;
        Assert.Equal(
            "utf16-code-unit-ordinal-recursive",
            root.GetProperty("ordering").GetString());
        Assert.Equal(
            "utf8-no-bom-no-normalization",
            root.GetProperty("encoding").GetString());
        JsonElement vector = root
            .GetProperty("vectors")
            .EnumerateArray()
            .Single();
        byte[] expectedBytes = Convert.FromBase64String(
            vector.GetProperty(
                "canonicalUtf8Base64").GetString()!);
        byte[] actualBytes =
            ParityCanonicalJson.SerializeToUtf8Bytes(
                vector.GetProperty("value"));

        Assert.Equal(
            expectedBytes,
            actualBytes);
        Assert.Equal(
            "8a3c187ebd3846533be418f811bb87a34"
            + "a45ec4dba008c7f8e1db7c299a04d33",
            ParityCanonicalJson.ComputeSha256(actualBytes));
        Assert.Equal(
            vector.GetProperty("sha256").GetString(),
            ParityCanonicalJson.ComputeSha256(actualBytes));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void CanonicalJsonRejectsSharedInvalidUnicodeVectors()
    {
        string vectorPath = Path.Combine(
            RepositoryPaths.Root,
            "tests",
            "parity",
            "canonical-json-vectors.json");
        using JsonDocument vectors = JsonDocument.Parse(
            File.ReadAllBytes(vectorPath));

        foreach (JsonElement invalidText in vectors.RootElement
                     .GetProperty("invalidJsonTexts")
                     .EnumerateArray())
        {
            using JsonDocument invalid = JsonDocument.Parse(
                invalidText.GetString()!);
            Assert.Throws<InvalidDataException>(
                () => ParityCanonicalJson.SerializeToUtf8Bytes(
                    invalid.RootElement));
        }
    }

    [Theory]
    [Trait("Category", "ParityInfrastructure")]
    [InlineData("1.5")]
    [InlineData("1e3")]
    [InlineData("-2E-2")]
    [InlineData("9223372036854775808")]
    [InlineData("-9223372036854775809")]
    public void CanonicalJsonRejectsNonInt64NumberLiteral(
        string numberLiteral)
    {
        using JsonDocument document = JsonDocument.Parse(
            $$"""{"scenario":"test.scenario","value":{{numberLiteral}}}""");

        Assert.Throws<InvalidDataException>(
            () => ParityCanonicalJson.ComputeSha256(
                document.RootElement));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void ScenarioClonesArbitraryJsonInput()
    {
        ParityScenario scenario;
        using (JsonDocument document = JsonDocument.Parse(
            """
            {
              "scenario": "test.scenario",
              "nested": {"values": [true, null, "0.125", "MØRSE"]}
            }
            """))
        {
            scenario = new ParityScenario(
                "test.scenario",
                "test",
                [],
                document.RootElement);
        }

        Assert.Equal(
            "MØRSE",
            scenario.Input.GetProperty("nested")
                .GetProperty("values")[3]
                .GetString());
        Assert.Matches("^[0-9a-f]{64}$", scenario.InputSha256);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void CentralInputOnlyValidatesScenarioDiscriminator()
    {
        using JsonDocument document = JsonDocument.Parse(
            """
            {
              "scenario": "contest.exchange-shapes",
              "contestIds": ["scWpx"],
              "futureSeed": "123.5"
            }
            """);
        ParityScenario scenario = new(
            "contest.exchange-shapes",
            "test",
            ["scWpx|expected"],
            document.RootElement);

        Assert.Throws<InvalidDataException>(
            () => ContestExchangeShapesInput.Parse(scenario));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void ScenarioRejectsMismatchedDiscriminator()
    {
        using JsonDocument document = JsonDocument.Parse(
            """{"scenario":"different","commands":[]}""");

        Assert.Throws<InvalidDataException>(
            () => new ParityScenario(
                "test.scenario",
                "test",
                [],
                document.RootElement));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void CanonicalJsonRejectsDuplicateNestedAndTopLevelProperties()
    {
        using JsonDocument nested = JsonDocument.Parse(
            """{"outer":{"value":1,"value":2}}""");
        Assert.Throws<InvalidDataException>(
            () => ParityCanonicalJson.ComputeSha256(
                nested.RootElement));

        using JsonDocument topLevel = JsonDocument.Parse(
            """{"id":"one","id":"two"}""");
        Assert.Throws<InvalidDataException>(
            () => ParityCertificationCase
                .ComputeCaseDefinitionSha256(
                    topLevel.RootElement));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void CaseShapeRejectsUnsupportedTopLevelField()
    {
        string manifestPath = Path.Combine(
            RepositoryPaths.Root,
            "tests",
            "parity",
            "parity-manifest.json");
        JsonObject manifest = JsonNode.Parse(
            File.ReadAllText(manifestPath))!.AsObject();
        JsonObject caseObject = manifest["cases"]!
            .AsArray()[0]!
            .AsObject();
        caseObject["unsupported"] = true;
        using JsonDocument document = JsonDocument.Parse(
            caseObject.ToJsonString());

        Assert.Throws<InvalidDataException>(
            () => ParityCertificationCase.ValidateCaseShape(
                document.RootElement,
                "contest.exchange-shapes"));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void AcceptanceTheoryRowsHaveStableUniqueNames()
    {
        TheoryDataRow[] rows =
            LegacyOracleParityTests.ActiveParityCases()
                .ToArray();

        Assert.Equal(
            ParityAcceptanceRegistry.ActiveIds
                .Select(id => $"parity:{id}"),
            rows.Select(row => row.TestDisplayName)
                .ToArray(),
            StringComparer.Ordinal);
        Assert.Equal(
            rows.Length,
            rows.Select(row => row.TestDisplayName)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.DoesNotContain(
            rows,
            row => String.IsNullOrWhiteSpace(
                row.TestDisplayName));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void UnconfiguredCaseSelectionUsesAllApplicableCases()
    {
        Assert.Equal(
            ParityAcceptanceRegistry.ActiveIds,
            ParityAcceptanceRegistry.ParseSelectedIds(
                configuredIds: null,
                ParityRunEnvironment.Capture().Platform),
            StringComparer.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void JsonCaseSelectionUsesExactActiveApplicableIds()
    {
        Assert.Equal(
            ["contest.exchange-shapes"],
            ParityAcceptanceRegistry.ParseSelectedIds(
                """["contest.exchange-shapes"]""",
                ParityRunEnvironment.Capture().Platform),
            StringComparer.Ordinal);
    }

    [Theory]
    [Trait("Category", "ParityInfrastructure")]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[1]")]
    [InlineData("""["unknown.case"]""")]
    [InlineData(
        """["contest.exchange-shapes","contest.exchange-shapes"]""")]
    public void InvalidCaseSelectionFailsClosed(string configuredIds)
    {
        Assert.Throws<InvalidDataException>(
            () => ParityAcceptanceRegistry.ParseSelectedIds(
                configuredIds,
                ParityRunEnvironment.Capture().Platform));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public void NonApplicableCaseSelectionFailsClosed()
    {
        Assert.Throws<InvalidDataException>(
            () => ParityAcceptanceRegistry.ParseSelectedIds(
                """["contest.exchange-shapes"]""",
                "unsupported-platform"));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task RecorderAtomicallyAccumulatesConcurrentResults()
    {
        using TemporaryDirectory temporary = new();
        string path = Path.Combine(temporary.Path, "xplat-results.json");

        await Task.WhenAll(
            ParityAcceptanceRegistry.ActiveIds.Select(
                async id =>
                {
                    ParityCertificationCase definition =
                        ParityCertificationCase.Load(id);
                    await ParityRunRecorder.RecordAsync(
                        path,
                        definition,
                        ParityTargetKind.XPlat,
                        new ParityObservation(
                            ParityTargetOutcome.Passed,
                            definition.Scenario.ExpectedValues,
                            null,
                            $"test:{id}"),
                        adapterCompleted: true,
                        TestContext.Current.CancellationToken);
                }));

        byte[] resultBytes = await File.ReadAllBytesAsync(
            path,
            TestContext.Current.CancellationToken);
        Assert.Contains((byte)'\n', resultBytes);
        Assert.DoesNotContain((byte)'\r', resultBytes);

        using MemoryStream stream = new(resultBytes);
        using JsonDocument document = JsonDocument.Parse(stream);
        Assert.Equal(
            "xplat",
            document.RootElement.GetProperty("target").GetString());
        JsonElement runContext =
            document.RootElement.GetProperty("runContext");
        Assert.Equal(
            ParityRunEnvironment.Capture().Platform,
            runContext.GetProperty("platform").GetString());
        Assert.False(
            String.IsNullOrWhiteSpace(
                runContext.GetProperty(
                    "processArchitecture").GetString()));
        Assert.False(
            String.IsNullOrWhiteSpace(
                runContext.GetProperty(
                    "runtimeIdentifier").GetString()));
        Assert.False(
            String.IsNullOrWhiteSpace(
                runContext.GetProperty("framework").GetString()));
        JsonElement xplatContext = runContext.GetProperty("xplat");
        Assert.False(
            String.IsNullOrWhiteSpace(
                xplatContext.GetProperty("revision").GetString()));
        Assert.False(
            String.IsNullOrWhiteSpace(
                xplatContext.GetProperty("tree").GetString()));
        Assert.True(
            xplatContext.GetProperty("clean").ValueKind is
                JsonValueKind.True
                or JsonValueKind.False);
        Assert.Equal(
            JsonValueKind.Null,
            runContext.GetProperty("legacy").ValueKind);
        string[] expectedIds = document.RootElement
            .GetProperty("expectedParityIds")
            .EnumerateArray()
            .Select(id => id.GetString()!)
            .ToArray();
        Assert.Equal(
            ParityAcceptanceRegistry.ActiveIds,
            expectedIds,
            StringComparer.Ordinal);
        string[] recordedIds = document.RootElement
            .GetProperty("results")
            .EnumerateArray()
            .Select(result => result.GetProperty("parityId").GetString()!)
            .ToArray();
        Assert.Equal(
            ParityAcceptanceRegistry.ActiveIds,
            recordedIds,
            StringComparer.Ordinal);
        Assert.All(
            document.RootElement
                .GetProperty("results")
                .EnumerateArray(),
            result =>
            {
                Assert.Equal(
                    "passed",
                    result.GetProperty("outcome").GetString());
                string parityId = result.GetProperty(
                    "parityId").GetString()!;
                Assert.Equal(
                    ParityAcceptanceRegistry.Get(parityId)
                        .AdapterId(ParityTargetKind.XPlat),
                    result.GetProperty("adapter").GetString());
                Assert.Equal(
                    1,
                    result.GetProperty("executionCount").GetInt32());
                ParityCertificationCase definition =
                    ParityCertificationCase.Load(parityId);
                Assert.Equal(
                    definition.Scenario.ExpectedValues.Count,
                    result.GetProperty(
                        "observedValueCount").GetInt32());
                Assert.Equal(
                    definition.Scenario.ExpectedValues,
                    result.GetProperty("observedValues")
                        .EnumerateArray()
                        .Select(value => value.GetString()!)
                        .ToArray(),
                    StringComparer.Ordinal);
                Assert.Equal(
                    ParityObservedValuesDigest.Compute(
                        definition.Scenario.ExpectedValues),
                    result.GetProperty(
                        "observedValuesSha256").GetString());
                Assert.Equal(
                    definition.CaseDefinitionSha256,
                    result.GetProperty(
                        "caseDefinitionSha256").GetString());
                Assert.Equal(
                    definition.FixtureSha256,
                    result.GetProperty(
                        "fixtureSha256").GetString());
                Assert.Equal(
                    JsonValueKind.Null,
                    result.GetProperty("firstDivergence").ValueKind);
            });
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task RecorderRejectsNoncertifyingVectorsWithoutCreatingFile()
    {
        using TemporaryDirectory temporary = new();
        string path = Path.Combine(temporary.Path, "xplat-results.json");

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => ParityRunRecorder.RecordAsync(
                    path,
                    CreateUnregisteredDefinition(),
                    ParityTargetKind.XPlat,
                    new ParityObservation(
                        ParityTargetOutcome.Passed,
                        [],
                        null,
                        "test:noncertifying"),
                    adapterCompleted: true,
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "not registered",
            exception.Message,
            StringComparison.Ordinal);
        Assert.False(File.Exists(path));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task RecorderRejectsNoncertifyingVectorsWhenOutputIsDisabled()
    {
        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => ParityRunRecorder.RecordAsync(
                    configuredPath: null,
                    CreateUnregisteredDefinition(),
                    ParityTargetKind.XPlat,
                    new ParityObservation(
                        ParityTargetOutcome.Passed,
                        [],
                        null,
                        "test:noncertifying"),
                    adapterCompleted: true,
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "not registered",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task RecorderCapturesMissingSideFirstDivergence()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        string path = Path.Combine(temporary.Path, "xplat-results.json");
        ParityCertificationCase definition =
            ParityCertificationCase.Load("contest.exchange-shapes");
        string[] observedValues =
        [
            .. definition.Scenario.ExpectedValues.Take(
                definition.Scenario.ExpectedValues.Count - 1),
        ];
        ParityObservation observation = new(
            ParityTargetOutcome.Failed,
            observedValues,
            "contest-exchange-shape-mismatch",
            "test:divergence");

        await ParityRunRecorder.RecordAsync(
            path,
            definition,
            ParityTargetKind.XPlat,
            observation,
            adapterCompleted: true,
            TestContext.Current.CancellationToken);

        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement result = document.RootElement
            .GetProperty("results")
            .EnumerateArray()
            .Single();
        Assert.Equal(
            observedValues,
            result.GetProperty("observedValues")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray(),
            StringComparer.Ordinal);
        Assert.Equal(
            observedValues.Length,
            result.GetProperty("observedValueCount").GetInt32());
        Assert.Equal(
            ParityObservedValuesDigest.Compute(observedValues),
            result.GetProperty("observedValuesSha256").GetString());
        Assert.Equal(
            "functional-divergence",
            result.GetProperty("outcome").GetString());
        JsonElement divergence =
            result.GetProperty("firstDivergence");
        int missingIndex =
            definition.Scenario.ExpectedValues.Count - 1;
        Assert.Equal(
            missingIndex,
            divergence.GetProperty("index").GetInt32());
        Assert.Equal(
            definition.Scenario.ExpectedValues[missingIndex],
            divergence.GetProperty("expected").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            divergence.GetProperty("actual").ValueKind);
    }

    [Theory]
    [Trait("Category", "ParityInfrastructure")]
    [InlineData("count")]
    [InlineData("hash")]
    [InlineData("divergence")]
    [InlineData("case-hash")]
    [InlineData("fixture-hash")]
    [InlineData("classification")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    public async Task RecorderRejectsTamperedObservedResultIntegrity(
        string field)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        string path = Path.Combine(temporary.Path, "xplat-results.json");
        ParityCertificationCase definition =
            ParityCertificationCase.Load("contest.exchange-shapes");
        string[] observedValues =
        [
            .. definition.Scenario.ExpectedValues.Take(
                definition.Scenario.ExpectedValues.Count - 1),
        ];
        ParityObservation observation = new(
            ParityTargetOutcome.Failed,
            observedValues,
            "contest-exchange-shape-mismatch",
            "test:divergence");
        await ParityRunRecorder.RecordAsync(
            path,
            definition,
            ParityTargetKind.XPlat,
            observation,
            adapterCompleted: true,
            TestContext.Current.CancellationToken);

        JsonObject document = JsonNode.Parse(
            File.ReadAllText(path))!.AsObject();
        JsonObject result = document["results"]!
            .AsArray()[0]!
            .AsObject();
        bool writeDuplicate = false;
        switch (field)
        {
            case "count":
                result["observedValueCount"] = 2;
                break;
            case "hash":
                result["observedValuesSha256"] = new string('0', 64);
                break;
            case "divergence":
                result["firstDivergence"]!["index"] = 0;
                break;
            case "case-hash":
                result["caseDefinitionSha256"] = new string('0', 64);
                break;
            case "fixture-hash":
                result["fixtureSha256"] = new string('0', 64);
                break;
            case "classification":
                result["outcome"] = "not-runnable";
                break;
            case "unknown":
                result["unexpected"] = true;
                break;
            case "duplicate":
                writeDuplicate = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(field),
                    field,
                    null);
        }

        string resultJson = document.ToJsonString();
        if (writeDuplicate)
        {
            resultJson = resultJson.Replace(
                "\"schemaVersion\":1",
                "\"schemaVersion\":1,\"schemaVersion\":1",
                StringComparison.Ordinal);
        }

        File.WriteAllText(path, resultJson);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => ParityRunRecorder.RecordAsync(
                path,
                definition,
                ParityTargetKind.XPlat,
                observation,
                adapterCompleted: true,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "ParityInfrastructure")]
    public async Task RecorderClassifiesAdapterFailureAsNotRunnable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        string path = Path.Combine(temporary.Path, "xplat-results.json");
        ParityCertificationCase definition =
            ParityCertificationCase.Load("contest.exchange-shapes");
        ParityObservation observation = new(
            ParityTargetOutcome.Failed,
            [],
            "parity-adapter-exception",
            "XPlatContestRulesTarget:System.InvalidOperationException");

        await ParityRunRecorder.RecordAsync(
            path,
            definition,
            ParityTargetKind.XPlat,
            observation,
            adapterCompleted: false,
            TestContext.Current.CancellationToken);

        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllBytes(path));
        JsonElement result = document.RootElement
            .GetProperty("results")
            .EnumerateArray()
            .Single();
        Assert.Equal(
            "not-runnable",
            result.GetProperty("outcome").GetString());
        Assert.Equal(
            "parity-adapter-exception",
            result.GetProperty("failureCode").GetString());
        Assert.Equal(
            0,
            result.GetProperty("firstDivergence")
                .GetProperty("index")
                .GetInt32());
    }

    [Theory]
    [Trait("Category", "ParityInfrastructure")]
    [InlineData("legacy-provenance-invalid")]
    [InlineData("legacy-oracle-not-configured")]
    [InlineData("legacy-oracle-launch-failed")]
    [InlineData("legacy-observation-mismatch")]
    public async Task RecorderClassifiesNonproductFailuresAsNotRunnable(
        string failureCode)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        string path = Path.Combine(temporary.Path, "xplat-results.json");
        ParityCertificationCase definition =
            ParityCertificationCase.Load("contest.exchange-shapes");

        await ParityRunRecorder.RecordAsync(
            path,
            definition,
            ParityTargetKind.XPlat,
            new ParityObservation(
                ParityTargetOutcome.Failed,
                [],
                failureCode,
                "test:infrastructure"),
            adapterCompleted: true,
            TestContext.Current.CancellationToken);

        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllBytes(path));
        JsonElement result = document.RootElement
            .GetProperty("results")
            .EnumerateArray()
            .Single();
        Assert.Equal(
            "not-runnable",
            result.GetProperty("outcome").GetString());
        Assert.Equal(
            failureCode,
            result.GetProperty("failureCode").GetString());
    }

    private static ParityCertificationCase
        CreateUnregisteredDefinition()
    {
        return new ParityCertificationCase(
            new ParityScenario(
                "contest.cq-wpx-scoring",
                "test",
                []),
            new string('0', 64),
            new string('0', 64),
            "test-fixture.json",
            ["test-obligation"],
            ["LegacyOracleTarget", "XPlatContestRulesTarget"],
            ["windows", "linux", "macos"],
            "test-mismatch");
    }

    private static ParityScenario CreateSstFarnsworthScenario(
        IReadOnlyList<string> expectedValues)
    {
        return new ParityScenario(
            XPlatSstFarnsworthTarget.ParityId,
            "audio-dsp.legacy-processing",
            expectedValues,
            JsonSerializer.SerializeToElement(
                CreateSstFarnsworthInput()));
    }

    private static JsonObject CreateSstFarnsworthInput()
    {
        return new JsonObject
        {
            ["scenario"] = XPlatSstFarnsworthTarget.ParityId,
            ["sampleRate"] = 11_025,
            ["blockSize"] = 512,
            ["amplitude"] = 300_000,
            ["sendingWpm"] = 15,
            ["characterWpm"] = 25,
            ["messages"] = new JsonArray(
                "PARIS TEST",
                "K1ABC 599 123"),
        };
    }

    private static void AssertSstFarnsworthInputRejected(
        JsonObject input,
        int expectedValueCount = 11)
    {
        Assert.Throws<InvalidDataException>(
            () =>
            {
                ParityScenario scenario = new(
                    XPlatSstFarnsworthTarget.ParityId,
                    "audio-dsp.legacy-processing",
                    Enumerable.Repeat(
                        "expected",
                        expectedValueCount).ToArray(),
                    JsonSerializer.SerializeToElement(input));
                _ = SstFarnsworthTimingInput.Parse(scenario);
            });
    }

    private sealed class StubTarget : IParityTarget
    {
        public Task<ParityObservation> ExecuteAsync(
            ParityScenario scenario,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private static string ComputeRepositoryFileSha256(string relativePath) =>
        Convert.ToHexStringLower(
            SHA256.HashData(
                File.ReadAllBytes(
                    Path.Combine(
                        RepositoryPaths.Root,
                        relativePath.Replace(
                            '/',
                            Path.DirectorySeparatorChar)))));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"mrx-parity-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
