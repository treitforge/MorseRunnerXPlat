from __future__ import annotations

import copy
import hashlib
import io
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from unittest.mock import patch


sys.path.insert(0, str(Path(__file__).resolve().parent))

from inventory_legacy import (  # noqa: E402
    decode_shortcut,
    keyboard_token_id,
    mask_pascal_comments,
    parse_data_references,
    parse_main_dfm,
    parse_operational_paths,
    parse_unit_declarations,
    parse_unit_routines,
)
import validate_parity as parity  # noqa: E402


class SurfaceMappingTests(unittest.TestCase):
    def test_unmapped_surface_fails_completeness(self) -> None:
        capabilities = [
            {
                "id": "mapped-capability",
                "legacySurfaceSelectors": ["legacy.surface.mapped"],
            }
        ]
        surfaces = [
            {"id": "legacy.surface.mapped"},
            {"id": "legacy.surface.unmapped"},
        ]

        with self.assertRaisesRegex(ValueError, "unmapped legacy surfaces"):
            parity.map_surfaces(capabilities, surfaces)

    def test_surface_mapped_twice_fails_completeness(self) -> None:
        capabilities = [
            {
                "id": "first-capability",
                "legacySurfaceSelectors": ["legacy.surface.*"],
            },
            {
                "id": "second-capability",
                "legacySurfaceSelectors": ["legacy.surface.one"],
            },
        ]
        surfaces = [{"id": "legacy.surface.one"}]

        with self.assertRaisesRegex(ValueError, "maps to multiple items"):
            parity.map_surfaces(capabilities, surfaces)


class ManifestTrustTests(unittest.TestCase):
    def test_shared_legacy_oracle_descriptor_vectors_are_consumed(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            vector_path = (
                root
                / parity.LEGACY_ORACLE_DESCRIPTOR_VECTOR_PATH
            )
            vector_path.parent.mkdir(parents=True)
            shutil.copy2(
                parity.ROOT
                / parity.LEGACY_ORACLE_DESCRIPTOR_VECTOR_PATH,
                vector_path,
            )
            parity.validate_legacy_oracle_descriptor_vectors(root=root)

            document = parity.load_json(vector_path)
            document["vectors"][0]["valid"] = False
            vector_path.write_text(
                json.dumps(document, indent=2) + "\n",
                encoding="utf-8",
                newline="\n",
            )
            with self.assertRaisesRegex(ValueError, "reviewed shared"):
                parity.validate_legacy_oracle_descriptor_vectors(
                    root=root
                )

    def test_audit_state_is_exact_disjoint_and_status_derived(self) -> None:
        capability_ids = {"capability.one", "capability.two"}
        parity.validate_audit_state(
            "complete",
            ["capability.one", "capability.two"],
            [],
            capability_ids,
        )
        parity.validate_audit_state(
            "in-progress",
            ["capability.one"],
            ["capability.two"],
            capability_ids,
        )

        invalid_states = (
            (
                "complete",
                ["capability.one", "capability.one"],
                ["capability.two"],
                "duplicates",
            ),
            (
                "in-progress",
                ["capability.one"],
                ["capability.one", "capability.two"],
                "overlap",
            ),
            (
                "complete",
                ["capability.one"],
                ["capability.two"],
                "inventoryStatus",
            ),
            (
                "complete",
                ["capability.one"],
                [],
                "exact capability IDs",
            ),
            (
                "complete",
                ["capability.one", "capability.two", "unknown"],
                [],
                "exact capability IDs",
            ),
        )
        for status, audited, pending, message in invalid_states:
            with self.subTest(message=message):
                with self.assertRaisesRegex(ValueError, message):
                    parity.validate_audit_state(
                        status,
                        audited,
                        pending,
                        capability_ids,
                    )

    def test_inventory_v2_binds_complete_tracked_file_classification(
        self,
    ) -> None:
        tracked_files = ["Legacy.pas", "README.md"]
        inventory = {
            "schemaVersion": 2,
            "reference": {
                "revision": "a" * 40,
                "trackedFileCount": len(tracked_files),
                "trackedFilesSha256": parity.tracked_files_sha256(
                    tracked_files
                ),
                "sources": ["Legacy.pas"],
                "exclusions": [
                    {
                        "path": "README.md",
                        "kind": "documentation",
                        "rationale": "Documentation has no runtime behavior.",
                    }
                ],
            },
            "surfaces": [
                {
                    "id": "legacy.test.surface",
                    "category": "test",
                    "name": "Surface",
                    "source": "Legacy.pas:1",
                    "details": {},
                }
            ],
        }
        manifest = {"reference": {"revision": "a" * 40}}
        parity.validate_inventory(manifest, inventory)

        mutations = (
            ("trackedFileCount", 1, "trackedFileCount"),
            ("trackedFilesSha256", "0" * 64, "trackedFilesSha256"),
        )
        for field, value, message in mutations:
            with self.subTest(field=field):
                changed = copy.deepcopy(inventory)
                changed["reference"][field] = value
                with self.assertRaisesRegex(ValueError, message):
                    parity.validate_inventory(manifest, changed)

        overlapping = copy.deepcopy(inventory)
        overlapping["reference"]["sources"].append("README.md")
        overlapping["reference"]["trackedFileCount"] = 3
        overlapping["reference"]["trackedFilesSha256"] = (
            parity.tracked_files_sha256(
                ["Legacy.pas", "README.md", "README.md"]
            )
        )
        with self.assertRaisesRegex(ValueError, "classified exactly once"):
            parity.validate_inventory(manifest, overlapping)

    def test_hashing_attributes_are_resolved_not_only_declared(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / ".gitattributes").write_text(
                "tests/parity/**/*.json text eol=lf\n"
                "tests/parity/legacy-oracle/**/*.lpr text eol=crlf\n"
                "tests/parity/legacy-oracle/v16/LegacyOracle.lpr "
                "text eol=lf\n",
                encoding="utf-8",
            )
            repository = subprocess.CompletedProcess(
                ["git"],
                0,
                stdout="true\n",
            )
            correct = subprocess.CompletedProcess(
                ["git"],
                0,
                stdout=(
                    "tests/parity/evidence/runs/probe.json: eol: lf\n"
                    "tests/parity/legacy-oracle/v1/Probe.lpr: eol: crlf\n"
                    "tests/parity/legacy-oracle/v16/LegacyOracle.lpr: "
                    "eol: lf\n"
                ),
            )
            with patch.object(
                parity,
                "run_git",
                side_effect=[repository, correct],
            ):
                parity.validate_hashing_attributes(root)

            incorrect = subprocess.CompletedProcess(
                ["git"],
                0,
                stdout=(
                    "tests/parity/evidence/runs/probe.json: eol: crlf\n"
                    "tests/parity/legacy-oracle/v1/Probe.lpr: eol: crlf\n"
                    "tests/parity/legacy-oracle/v16/LegacyOracle.lpr: "
                    "eol: lf\n"
                ),
            )
            with patch.object(
                parity,
                "run_git",
                side_effect=[repository, incorrect],
            ):
                with self.assertRaisesRegex(
                    ValueError,
                    "resolved parity hashing attributes",
                ):
                    parity.validate_hashing_attributes(root)

    def test_parsed_certifying_workflow_contract_rejects_semantic_drift(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            workflows = root / ".github/workflows"
            workflows.mkdir(parents=True)
            for name in (
                "dotnet-quality.yml",
                "parity-quality.yml",
                "release-quality.yml",
                "release-evidence.yml",
            ):
                shutil.copy2(
                    parity.ROOT / ".github/workflows" / name,
                    workflows / name,
                )
            runner = root / "tests/parity/Run-Parity.ps1"
            runner.parent.mkdir(parents=True)
            for script_name in (
                "Run-Parity.ps1",
                "New-ParityBothArtifactPackage.ps1",
                "Assert-ParityFullSuitePackage.ps1",
            ):
                shutil.copy2(
                    parity.ROOT / "tests/parity" / script_name,
                    runner.parent / script_name,
                )
            reference = {
                "publicBaseRevision": (
                    "2dd6f5cfd8a96e00f472d189adae594823ae5b61"
                )
            }
            parity.validate_acceptance_test_wiring(root)
            parity.validate_certifying_workflow_contracts(
                reference,
                root=root,
            )

            mutations = (
                (
                    "parity-quality.yml",
                    "-Mode PullRequest",
                    "-Mode Development",
                    "Mode PullRequest",
                ),
                (
                    "dotnet-quality.yml",
                    "os: [windows-latest, ubuntu-latest, macos-latest]",
                    "os: [windows-latest, ubuntu-latest]",
                    "native parity matrix",
                ),
                (
                    "release-evidence.yml",
                    "needs: live-parity",
                    "needs: []",
                    "depend on live parity",
                ),
                (
                    "release-evidence.yml",
                    '      - "proto/**"',
                    '      - "proto-omitted/**"',
                    "omit a native release input",
                ),
                (
                    "release-quality.yml",
                    "New-ParityBothArtifactPackage.ps1",
                    "Missing-ParityBothArtifactPackage.ps1",
                    "content-addressed Both-target package",
                ),
                (
                    "release-quality.yml",
                    (
                        "${{ steps.release-parity-artifacts.outputs."
                        "package_root }}"
                    ),
                    "artifacts/parity-package-staging/incomplete",
                    "result, TRX, and execution",
                ),
                (
                    "release-quality.yml",
                    r".\tools\release\Publish-Release.ps1",
                    r".\tools\release\Missing-Publish-Release.ps1",
                    "publish runtime packages",
                ),
                (
                    "release-quality.yml",
                    "MorseRunnerXPlat-runtime-packages",
                    "MorseRunnerXPlat-missing-runtime-packages",
                    "runtime package upload",
                ),
                (
                    "release-evidence.yml",
                    (
                        "artifacts/parity-package-staging/"
                        "native-evidence-both"
                    ),
                    "artifacts/parity-package-staging/incomplete",
                    "content-addressed Both-target package",
                ),
                (
                    "release-evidence.yml",
                    r".\tools\release\Publish-Release.ps1",
                    r".\tools\release\Missing-Publish-Release.ps1",
                    "publish and test",
                ),
                (
                    "release-evidence.yml",
                    r".\tools\release\Test-ReleaseArchive.ps1",
                    r".\tools\release\Missing-Test-ReleaseArchive.ps1",
                    "publish and test",
                ),
                (
                    "release-evidence.yml",
                    "        if: always()",
                    "        if: success()",
                    "native diagnostics",
                ),
                (
                    "release-evidence.yml",
                    (
                        "path: artifacts/release/"
                        "MorseRunnerXPlat-${{ matrix.rid }}"
                    ),
                    (
                        "path: artifacts/unverified/"
                        "MorseRunnerXPlat-${{ matrix.rid }}"
                    ),
                    "publish tested archives",
                ),
            )
            for name, old, new, message in mutations:
                with self.subTest(workflow=name, mutation=old):
                    path = workflows / name
                    original = path.read_text(encoding="utf-8")
                    self.assertIn(old, original)
                    path.write_text(
                        original.replace(old, new, 1),
                        encoding="utf-8",
                        newline="\n",
                    )
                    try:
                        with self.assertRaisesRegex(ValueError, message):
                            parity.validate_certifying_workflow_contracts(
                                reference,
                                root=root,
                            )
                    finally:
                        path.write_text(
                            original,
                            encoding="utf-8",
                            newline="\n",
                        )


class PascalSourceTests(unittest.TestCase):
    def test_comment_masking_preserves_lines_and_ignores_comment_content(
        self,
    ) -> None:
        source = (
            "ReadString(SEC_STN, 'Active', '');\n"
            "{ WriteString(SEC_STN, 'Commented', ''); }\n"
            "// ReadBool(SEC_STN, 'AlsoCommented', False);\n"
        )

        masked = mask_pascal_comments(source)

        self.assertEqual(source.count("\n"), masked.count("\n"))
        self.assertIn("ReadString", masked)
        self.assertNotIn("WriteString", masked)
        self.assertNotIn("ReadBool", masked)

    def test_dfm_inventory_preserves_objects_events_and_shortcuts(self) -> None:
        source = (
            "object MainForm: TMainForm\n"
            "  Caption = 'Main'\n"
            "  object RunItem: TMenuItem\n"
            "    Caption = 'Run'\n"
            "    ShortCut = 8312\n"
            "    OnClick = RunClick\n"
            "  end\n"
            "end\n"
        )

        objects, events, shortcuts, handlers = parse_main_dfm(source)

        self.assertEqual(2, len(objects))
        self.assertEqual("Run", objects[1]["name"])
        self.assertEqual("MainForm", objects[1]["details"]["parent"])
        self.assertEqual("RunClick", events[0]["details"]["handler"])
        self.assertEqual("Shift+F9", shortcuts[0]["details"]["display"])
        self.assertEqual({"RunClick"}, handlers)

    def test_keyboard_token_ids_distinguish_case_and_punctuation(self) -> None:
        token_ids = {
            keyboard_token_id("'a'"),
            keyboard_token_id("'A'"),
            keyboard_token_id("'.'"),
            keyboard_token_id("','"),
            keyboard_token_id("#8"),
            keyboard_token_id("VK_F1"),
        }

        self.assertEqual(6, len(token_ids))
        self.assertEqual("Ctrl+F9", decode_shortcut("16504"))

    def test_unit_inventory_keeps_type_property_and_overload_identity(
        self,
    ) -> None:
        source = (
            "unit Filter;\n"
            "interface\n"
            "type\n"
            "  TFilter = class\n"
            "  published\n"
            "    property Gain: Single read FGain write FGain;\n"
            "  end;\n"
            "implementation\n"
            "function TFilter.Process(Value: Single): Single;\n"
            "begin\n"
            "end;\n"
            "function TFilter.Process(Value: Integer): Single;\n"
            "begin\n"
            "end;\n"
            "end.\n"
        )

        declarations = parse_unit_declarations(
            source,
            "Filter.pas",
            "legacy.test",
            "test",
        )
        routines = parse_unit_routines(
            source,
            "Filter.pas",
            "legacy.test",
            "test",
        )

        self.assertEqual(
            [
                "legacy.test.type.filter.tfilter",
                "legacy.test.property.filter.tfilter.gain",
            ],
            [surface["id"] for surface in declarations],
        )
        self.assertEqual(1, len(routines))
        self.assertEqual(2, len(routines[0]["details"]["declarations"]))

    def test_data_and_failure_paths_capture_assets_and_routine_operations(
        self,
    ) -> None:
        source = (
            "unit Loader;\n"
            "interface\n"
            "implementation\n"
            "procedure Load;\n"
            "begin\n"
            "  if FileExists('CALLS.TXT') then "
            "List.LoadFromFile('CALLS.TXT');\n"
            "  if Broken then raise Exception.Create('bad');\n"
            "end;\n"
            "end.\n"
        )
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "calls.txt").write_bytes(b"K1ABC\n")

            references = parse_data_references(root, {"Loader.pas": source})
            operations = parse_operational_paths({"Loader.pas": source})

        self.assertEqual(1, len(references))
        self.assertEqual("calls.txt", references[0]["details"]["asset"])
        self.assertEqual(6, references[0]["details"]["bytes"])
        self.assertEqual(
            ["FileExists", "LoadFromFile", "raise"],
            [surface["details"]["operation"] for surface in operations],
        )


class ParityValidationTests(unittest.TestCase):
    reference_revision = "a" * 40
    reference_tree = "c" * 40
    first_green_commit = "b" * 40
    first_green_tree = "d" * 40
    oracle_relative = (
        "tests/parity/legacy-oracle/v1/LegacyOracle.lpr"
    )
    build_recipe_relative = (
        "tests/parity/legacy-oracle/v1/build-recipe.json"
    )
    xplat_source_relative = "tests/XPlatTarget.cs"

    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory(
            dir=Path.home()
        )
        self.addCleanup(self.temporary_directory.cleanup)
        self.original_validate_retained_xplat_revision = (
            parity.validate_retained_xplat_revision
        )
        self.original_validate_strict_revision_ancestry = (
            parity.validate_strict_revision_ancestry
        )
        self.original_inspect_current_xplat_repository = (
            parity.inspect_current_xplat_repository
        )
        retained_revision_patch = patch.object(
            parity,
            "validate_retained_xplat_revision",
        )
        strict_ancestry_patch = patch.object(
            parity,
            "validate_strict_revision_ancestry",
        )
        current_repository_patch = patch.object(
            parity,
            "inspect_current_xplat_repository",
            return_value={
                "revision": "f" * 40,
                "tree": "6" * 40,
                "clean": True,
            },
        )
        retained_revision_patch.start()
        strict_ancestry_patch.start()
        current_repository_patch.start()
        self.addCleanup(retained_revision_patch.stop)
        self.addCleanup(strict_ancestry_patch.stop)
        self.addCleanup(current_repository_patch.stop)
        self.root = Path(self.temporary_directory.name)
        (self.root / "tests/parity/fixtures/legacy").mkdir(parents=True)
        (self.root / "tests/parity/evidence").mkdir(parents=True)
        bundle_path = self.root / "tests/parity/legacy-reference.bundle"
        bundle_path.write_bytes(b"trusted legacy bundle\n")
        self.bundle_sha256 = parity.sha256_file(bundle_path)
        oracle_path = self.root / self.oracle_relative
        oracle_path.parent.mkdir(parents=True)
        oracle_path.write_text("program LegacyOracle;\n", encoding="utf-8")
        build_recipe_path = self.root / self.build_recipe_relative
        build_recipe_path.write_text(
            json.dumps(
                {
                    "schemaVersion": 1,
                    "adapterId": "LegacyOracleTarget",
                    "versionId": "legacy-oracle-v1",
                    "sourceClosure": {
                        "oracleSource": self.oracle_relative,
                        "oracleSourceSha256": parity.sha256_file(
                            oracle_path
                        ),
                        "legacyRevision": self.reference_revision,
                        "legacyTree": self.reference_tree,
                        "legacyBundleSha256": self.bundle_sha256,
                        "toolchainFingerprintSha256": "3" * 64,
                    },
                    "invocation": {
                        "compiler": "fpc",
                        "arguments": ["{source}"],
                    },
                },
                indent=2,
            )
            + "\n",
            encoding="utf-8",
            newline="\n",
        )
        xplat_source = self.root / self.xplat_source_relative
        xplat_source.parent.mkdir(parents=True, exist_ok=True)
        xplat_source.write_text("// executable test target\n", encoding="utf-8")
        self.oracle_executable_path = (
            self.root / "artifacts/legacy-oracle/LegacyOracle.exe"
        )
        self.oracle_executable_path.parent.mkdir(
            parents=True,
            exist_ok=True,
        )
        self.oracle_executable_path.write_bytes(b"legacy oracle binary\n")
        self.oracle_executable_sha256 = parity.sha256_file(
            self.oracle_executable_path
        )

        self.parity_id = "test.case"
        self.capability_id = "test.capability"
        self.obligation_id = "test.behavioral-obligation"
        self.legacy_oracle = {
            "adapterId": "LegacyOracleTarget",
            "versionId": "legacy-oracle-v1",
            "source": self.oracle_relative,
            "sourceSha256": parity.sha256_file(oracle_path),
            "buildRecipe": self.build_recipe_relative,
            "buildRecipeSha256": parity.sha256_file(build_recipe_path),
        }
        self.fixture_relative = (
            "tests/parity/fixtures/legacy/test-case.json"
        )
        self.evidence_relative = (
            "tests/parity/evidence/test-case.baseline.json"
        )
        self.registry_sha256 = "7" * 64
        self.runtime_provenance_relative = (
            "artifacts/legacy-oracle/LegacyOracle.provenance.json"
        )
        self.write_json(
            "tests/parity/legacy-reference.json",
            self.make_legacy_reference(),
        )
        self.fixture = self.make_fixture(self.parity_id)
        self.write_json(self.fixture_relative, self.fixture)
        self.case = self.make_case(self.parity_id)
        self.evidence = self.make_red_evidence(self.parity_id)
        self.write_json(self.evidence_relative, self.evidence)

    def write_json(
        self,
        relative_path: str,
        value: dict[str, object],
        *,
        root: Path | None = None,
    ) -> None:
        target_root = self.root if root is None else root
        path = target_root / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(
            json.dumps(value, indent=2, ensure_ascii=False) + "\n",
            encoding="utf-8",
            newline="\n",
        )

    def make_legacy_reference(self) -> dict[str, object]:
        return {
            "schemaVersion": 1,
            "repository": "https://example.invalid/legacy.git",
            "publicBaseRevision": "e" * 40,
            "revision": self.reference_revision,
            "tree": self.reference_tree,
            "bundle": "tests/parity/legacy-reference.bundle",
            "bundleSha256": self.bundle_sha256,
            "toolchain": {
                "lazarusVersion": "4.6",
                "fpcVersion": "3.2.2",
                "targetCpu": "x86_64",
                "targetOs": "win64",
                "installer": "https://example.invalid/lazarus.exe",
                "installerSha256": "b" * 64,
                "compilerSha256": "f" * 64,
                "backendCompilerSha256": "1" * 64,
                "lazbuildSha256": "2" * 64,
                "fingerprint": {
                    "schemaVersion": 1,
                    "canonicalization": (
                        "utf8-lf-nul-lowercase-relative-path-v1"
                    ),
                    "roots": ["fpc/3.2.2", "lazbuild.exe"],
                    "aggregateSha256": "3" * 64,
                    "fileCount": 2,
                    "byteCount": 100,
                },
            },
        }

    def make_fixture(self, parity_id: str) -> dict[str, object]:
        reference_path = self.root / "tests/parity/legacy-reference.json"
        return {
            "schemaVersion": 2,
            "revision": self.reference_revision,
            "tree": self.reference_tree,
            "parityId": parity_id,
            "oracle": self.oracle_relative,
            "legacyOracleVersionId": self.legacy_oracle["versionId"],
            "referenceDefinitionSha256": parity.sha256_file(reference_path),
            "oracleSourceSha256": self.legacy_oracle["sourceSha256"],
            "oracleBuildRecipeSha256": self.legacy_oracle[
                "buildRecipeSha256"
            ],
            "oracleExecutableSha256": self.oracle_executable_sha256,
            "toolchain": {
                "lazarus": "4.6",
                "fpc": "3.2.2",
                "target": "x86_64-win64",
                "compilerSha256": "f" * 64,
                "backendCompilerSha256": "1" * 64,
                "lazbuildSha256": "2" * 64,
                "fingerprintSha256": "3" * 64,
            },
            "values": ["legacy-value"],
        }

    def make_case(self, parity_id: str) -> dict[str, object]:
        return {
            "id": parity_id,
            "capabilityId": self.capability_id,
            "obligationIds": [self.obligation_id],
            "behavior": "One independently reviewable behavior.",
            "legacyOracle": copy.deepcopy(self.legacy_oracle),
            "legacySources": [self.oracle_relative],
            "legacySurfaceSelectors": ["legacy.surface.one"],
            "preconditions": ["The deterministic fixture is available."],
            "input": {"seed": 1, "commands": []},
            "targetAdapters": ["LegacyOracleTarget", "XPlatTarget"],
            "assertions": {
                "fixtureComparison": "exact",
                "observedValueCount": 1,
                "observedValuesSha256": parity.canonical_json_sha256(
                    ["legacy-value"]
                ),
                "firstDivergence": (
                    "The first zero-based ordinal value mismatch."
                ),
                "functionalDivergenceCode": "behavior-diverged",
            },
            "platforms": ["windows"],
            "legacyTestStatus": "pass",
            "xplatTestStatus": "fail",
            "status": "legacy-green-xplat-red",
            "failureCode": "behavior-diverged",
            "fixture": self.fixture_relative,
            "evidence": self.evidence_relative,
            "firstGreenCommit": None,
        }

    def make_capability(
        self,
        *,
        status: str = "partial",
        case_ids: list[str] | None = None,
    ) -> dict[str, object]:
        if case_ids is None:
            case_ids = [self.parity_id]
        return {
            "id": self.capability_id,
            "category": "test",
            "feature": "Test capability",
            "behavior": "All mapped behavior is covered.",
            "legacySources": [self.oracle_relative],
            "legacySurfaceSelectors": ["legacy.surface.*"],
            "platforms": ["windows", "linux", "macos"],
            "acceptanceStatus": status,
            "caseIds": case_ids,
        }

    def make_obligation(
        self,
        *,
        status: str = "partial",
        case_ids: list[str] | None = None,
        platforms: list[str] | None = None,
    ) -> dict[str, object]:
        if case_ids is None:
            case_ids = [self.parity_id]
        if platforms is None:
            platforms = ["windows"]
        return {
            "id": self.obligation_id,
            "capabilityId": self.capability_id,
            "behavior": "The exact observable behavior is acceptance-tested.",
            "platforms": platforms,
            "sourceBindingStatus": "bound",
            "legacySources": [self.oracle_relative],
            "legacySurfaceSelectors": ["legacy.surface.one"],
            "acceptanceStatus": status,
            "caseIds": case_ids,
        }

    def fixture_digest(self) -> str:
        return parity.sha256_file(self.root / self.fixture_relative)

    def make_red_evidence(self, parity_id: str) -> dict[str, object]:
        fixture_values = self.fixture["values"]
        fixture_digest = self.fixture_digest()
        provenance = self.make_provenance(parity_id)
        provenance_reference = self.write_content_document(
            "provenance",
            provenance,
        )
        provenance_digest = provenance_reference["sha256"]
        legacy_run = self.make_run_document(
            "legacy",
            fixture_values,
            "passed",
            None,
            provenance_digest=provenance_digest,
        )
        red_run = self.make_run_document(
            "xplat",
            ["xplat-value"],
            "functional-divergence",
            "behavior-diverged",
        )
        legacy_run_reference = self.write_content_document(
            "runs",
            legacy_run,
        )
        red_run_reference = self.write_content_document(
            "runs",
            red_run,
        )
        legacy_report_reference = self.write_content_file(
            "test-reports",
            self.make_trx(legacy_run),
            ".trx",
        )
        red_report_reference = self.write_content_file(
            "test-reports",
            self.make_trx(red_run),
            ".trx",
        )
        legacy_execution_reference = self.write_content_document(
            "executions",
            self.make_execution_envelope(
                legacy_run,
                legacy_run_reference["sha256"],
                legacy_report_reference["sha256"],
            ),
        )
        red_execution_reference = self.write_content_document(
            "executions",
            self.make_execution_envelope(
                red_run,
                red_run_reference["sha256"],
                red_report_reference["sha256"],
            ),
        )
        return {
            "schemaVersion": 2,
            "parityId": parity_id,
            "referenceRevision": self.reference_revision,
            "capturedAtUtc": "2026-07-18T00:00:00Z",
            "fixture": {
                "path": self.fixture_relative,
                "sha256": fixture_digest,
                "observedValuesSha256": parity.canonical_json_sha256(
                    fixture_values
                ),
            },
            "legacyOracle": {
                **copy.deepcopy(self.legacy_oracle),
                "executableSha256": self.fixture[
                    "oracleExecutableSha256"
                ],
                "provenance": self.runtime_provenance_relative,
                "provenanceSha256": provenance_digest,
                "registrySha256": self.registry_sha256,
                "retainedProvenance": provenance_reference,
            },
            "runs": {
                "legacy": legacy_run_reference,
                "xplatRed": red_run_reference,
                "xplatGreen": {},
            },
            "testReports": {
                "legacy": legacy_report_reference,
                "xplatRed": red_report_reference,
                "xplatGreen": {},
            },
            "executions": {
                "legacy": legacy_execution_reference,
                "xplatRed": red_execution_reference,
                "xplatGreen": {},
            },
            "regressionGate": None,
            "classification": "legacy-green-xplat-red",
        }

    def make_green_evidence(self) -> dict[str, object]:
        evidence = self.make_red_evidence(self.parity_id)
        green_runs: dict[str, dict[str, object]] = {}
        green_reports: dict[str, dict[str, object]] = {}
        green_executions: dict[str, dict[str, object]] = {}
        for platform in self.case["platforms"]:
            green_run = self.make_run_document(
                "xplat",
                self.fixture["values"],
                "passed",
                None,
                platform=platform,
                revision=self.first_green_commit,
                tree=self.first_green_tree,
            )
            green_run_reference = self.write_content_document(
                "runs",
                green_run,
            )
            green_report_reference = self.write_content_file(
                "test-reports",
                self.make_trx(green_run),
                ".trx",
            )
            green_execution_reference = self.write_content_document(
                "executions",
                self.make_execution_envelope(
                    green_run,
                    green_run_reference["sha256"],
                    green_report_reference["sha256"],
                ),
            )
            green_runs[platform] = green_run_reference
            green_reports[platform] = green_report_reference
            green_executions[platform] = green_execution_reference
        evidence["runs"]["xplatGreen"] = green_runs
        evidence["testReports"]["xplatGreen"] = green_reports
        evidence["executions"]["xplatGreen"] = green_executions
        full_legacy_run = self.make_run_document(
            "legacy",
            self.fixture["values"],
            "passed",
            None,
            provenance_digest=evidence["legacyOracle"][
                "provenanceSha256"
            ],
            revision=self.first_green_commit,
            tree=self.first_green_tree,
        )
        full_legacy_run_reference = self.write_content_document(
            "runs",
            full_legacy_run,
        )
        full_legacy_report_reference = self.write_content_file(
            "test-reports",
            self.make_trx(full_legacy_run),
            ".trx",
        )
        full_legacy_execution_reference = self.write_content_document(
            "executions",
            self.make_execution_envelope(
                full_legacy_run,
                full_legacy_run_reference["sha256"],
                full_legacy_report_reference["sha256"],
            ),
        )
        manifest_cases = parity.build_regression_manifest_case_snapshot(
            [self.case],
            {self.parity_id},
        )
        gate_inventory = {
            "windows/Legacy": [self.parity_id],
            **{
                f"{platform}/XPlat": [self.parity_id]
                for platform in parity.SUPPORTED_PLATFORMS
            },
        }
        gate_runs = {
            "windows/Legacy": full_legacy_run_reference,
            **{
                f"{platform}/XPlat": green_runs[platform]
                for platform in parity.SUPPORTED_PLATFORMS
            },
        }
        gate_reports = {
            "windows/Legacy": full_legacy_report_reference,
            **{
                f"{platform}/XPlat": green_reports[platform]
                for platform in parity.SUPPORTED_PLATFORMS
            },
        }
        gate_executions = {
            "windows/Legacy": full_legacy_execution_reference,
            **{
                f"{platform}/XPlat": green_executions[platform]
                for platform in parity.SUPPORTED_PLATFORMS
            },
        }
        build_integration_report_reference = self.write_content_file(
            "test-reports",
            self.make_legacy_build_integration_trx(),
            ".trx",
        )
        build_integration_execution_reference = (
            self.write_content_document(
                "executions",
                self.make_legacy_build_integration_execution(
                    build_integration_report_reference["sha256"],
                    registry_sha256=self.registry_sha256,
                    revision=self.first_green_commit,
                    tree=self.first_green_tree,
                ),
            )
        )
        gate = {
            "schemaVersion": 1,
            "selectedCaseIds": [self.parity_id],
            "xplat": {
                "revision": self.first_green_commit,
                "tree": self.first_green_tree,
                "clean": True,
            },
            "oracleRegistrySha256": self.registry_sha256,
            "fullSuitePackageIndexSha256": "8" * 64,
            "legacyOracleBuildIntegration": {
                "testReport": build_integration_report_reference,
                "execution": (
                    build_integration_execution_reference
                ),
            },
            "manifestCases": manifest_cases,
            "caseInventory": gate_inventory,
            "runs": gate_runs,
            "testReports": gate_reports,
            "executions": gate_executions,
        }
        gate["manifestCasesSha256"] = parity.canonical_json_sha256(
            manifest_cases
        )
        evidence["regressionGate"] = self.write_content_document(
            "regression-gates",
            gate,
        )
        evidence["classification"] = "both-green"
        return evidence

    def make_provenance(self, parity_id: str) -> dict[str, object]:
        fixture_toolchain = self.fixture["toolchain"]
        compiler = "C:\\tools\\fpc.exe"
        source_path = str(
            (self.root / self.legacy_oracle["source"]).resolve()
        )
        recipe_path = str(
            (self.root / self.legacy_oracle["buildRecipe"]).resolve()
        )
        executable_path = str(self.oracle_executable_path.resolve())
        unit_output = str(
            (self.root / "artifacts/legacy-oracle/units").resolve()
        )
        executable_output = str(
            self.oracle_executable_path.parent.resolve()
        )
        invocation = {
            "compiler": compiler,
            "options": ["-O2"],
            "unitSearchPaths": ["C:\\legacy"],
            "toolSearchPaths": [],
            "librarySearchPaths": [],
            "unitOutputPath": unit_output,
            "executableOutputPath": executable_output,
            "outputExecutable": executable_path,
            "source": source_path,
        }
        return {
            "schemaVersion": 1,
            "adapterId": self.legacy_oracle["adapterId"],
            "versionId": self.legacy_oracle["versionId"],
            "source": self.legacy_oracle["source"],
            "sourceSha256": self.legacy_oracle["sourceSha256"],
            "buildRecipe": self.legacy_oracle["buildRecipe"],
            "buildRecipeSha256": self.legacy_oracle[
                "buildRecipeSha256"
            ],
            "selectedCaseIds": [parity_id],
            "reference": {
                "definition": "tests/parity/legacy-reference.json",
                "definitionSha256": self.fixture[
                    "referenceDefinitionSha256"
                ],
                "bundle": "tests/parity/legacy-reference.bundle",
                "bundleSha256": self.bundle_sha256,
            },
            "legacy": {
                "repository": "https://example.invalid/legacy.git",
                "revision": self.reference_revision,
                "tree": self.reference_tree,
                "root": "C:\\legacy",
                "clean": True,
            },
            "xplat": {
                "revision": "f" * 40,
                "tree": "6" * 40,
                "clean": True,
            },
            "oracle": {
                "source": self.legacy_oracle["source"],
                "sourcePath": source_path,
                "sourceSha256": self.fixture["oracleSourceSha256"],
                "buildRecipe": self.legacy_oracle["buildRecipe"],
                "buildRecipePath": recipe_path,
                "buildRecipeSha256": self.fixture[
                    "oracleBuildRecipeSha256"
                ],
                "executable": executable_path,
                "executableSha256": self.fixture[
                    "oracleExecutableSha256"
                ],
                "length": self.oracle_executable_path.stat().st_size,
            },
            "toolchain": {
                "root": "C:\\tools",
                "lazarusVersion": fixture_toolchain["lazarus"],
                "fpcVersion": fixture_toolchain["fpc"],
                "targetCpu": "x86_64",
                "targetOs": "win64",
                "compiler": compiler,
                "compilerSha256": fixture_toolchain["compilerSha256"],
                "backendCompiler": "C:\\tools\\ppcx64.exe",
                "backendCompilerSha256": fixture_toolchain[
                    "backendCompilerSha256"
                ],
                "lazbuild": "C:\\tools\\lazbuild.exe",
                "lazbuildSha256": fixture_toolchain[
                    "lazbuildSha256"
                ],
                "fingerprint": {
                    "schemaVersion": 1,
                    "canonicalization": (
                        "utf8-lf-nul-lowercase-relative-path-v1"
                    ),
                    "roots": ["fpc/3.2.2", "lazbuild.exe"],
                    "aggregateSha256": fixture_toolchain[
                        "fingerprintSha256"
                    ],
                    "fileCount": 2,
                    "byteCount": 100,
                },
            },
            "build": {
                "script": "tests/parity/Build-LegacyOracle.ps1",
                "scriptSha256": "9" * 64,
                "arguments": [
                    "-O2",
                    "-FuC:\\legacy",
                    "-FU" + unit_output,
                    "-FE" + executable_output,
                    "-o" + executable_path,
                    source_path,
                ],
                "invocation": invocation,
                "builtAtUtc": "2026-07-18T00:00:00Z",
            },
            "manifest": {
                "path": "tests/parity/parity-manifest.json",
                "sha256": "a" * 64,
            },
            "observations": [
                {
                    "scenario": parity_id,
                    "valueCount": len(self.fixture["values"]),
                    "outputSha256": "8" * 64,
                }
            ],
        }

    def make_run_document(
        self,
        target: str,
        values: list[str],
        outcome: str,
        failure_code: str | None,
        *,
        provenance_digest: str | None = None,
        platform: str = "windows",
        revision: str = "f" * 40,
        tree: str = "6" * 40,
    ) -> dict[str, object]:
        first_divergence = (
            None
            if outcome == "passed"
            else parity.find_first_divergence(
                self.fixture["values"],
                values,
            )
        )
        legacy_context: dict[str, object] | None = None
        legacy_oracle: dict[str, object] | None = None
        if target == "legacy":
            legacy_context = {
                "revision": self.reference_revision,
                "tree": self.reference_tree,
                "clean": True,
            }
            legacy_oracle = {
                **copy.deepcopy(self.legacy_oracle),
                "executableSha256": self.fixture[
                    "oracleExecutableSha256"
                ],
                "provenance": self.runtime_provenance_relative,
                "provenanceSha256": provenance_digest,
                "registrySha256": self.registry_sha256,
            }
        return {
            "schemaVersion": 1,
            "target": target,
            "runContext": {
                "platform": platform,
                "processArchitecture": "x64",
                "runtimeIdentifier": {
                    "windows": "win-x64",
                    "linux": "linux-x64",
                    "macos": "osx-x64",
                }[platform],
                "framework": ".NET 10.0.0",
                "xplat": {
                    "revision": revision,
                    "tree": tree,
                    "clean": True,
                },
                "legacy": legacy_context,
            },
            "expectedParityIds": [self.parity_id],
            "results": [
                {
                    "parityId": self.parity_id,
                    "acceptanceTestName": (
                        f"parity:{self.parity_id}()"
                    ),
                    "target": target,
                    "adapter": self.case["targetAdapters"][
                        0 if target == "legacy" else 1
                    ],
                    "caseDefinitionSha256": (
                        parity.case_definition_sha256(self.case)
                    ),
                    "fixtureSha256": self.fixture_digest(),
                    "outcome": outcome,
                    "failureCode": failure_code,
                    "evidenceSource": (
                        self.oracle_relative
                        if target == "legacy"
                        else self.xplat_source_relative
                    ),
                    "observedValues": values,
                    "observedValueCount": len(values),
                    "observedValuesSha256": (
                        parity.canonical_json_sha256(values)
                    ),
                    "firstDivergence": first_divergence,
                    "legacyOracle": legacy_oracle,
                    "executionCount": 1,
                }
            ],
        }

    def make_execution_envelope(
        self,
        run: dict[str, object],
        result_sha256: str,
        test_report_sha256: str,
    ) -> dict[str, object]:
        context = run["runContext"]
        results = run["results"]
        divergences = sorted(
            (
                {
                    "caseId": result["parityId"],
                    "failureCode": result["failureCode"],
                }
                for result in results
                if result["outcome"] == "functional-divergence"
            ),
            key=lambda entry: entry["caseId"],
        )
        return {
            "schemaVersion": 1,
            "target": (
                "Legacy" if run["target"] == "legacy" else "XPlat"
            ),
            "platform": context["platform"],
            "operatingSystem": "Test Operating System",
            "architecture": context["processArchitecture"],
            "runtimeIdentifier": context["runtimeIdentifier"],
            "revision": context["xplat"]["revision"],
            "tree": context["xplat"]["tree"],
            "resultSha256": result_sha256,
            "testReportSha256": test_report_sha256,
            "testProcessExitCode": 2 if divergences else 0,
            "wrapper": {
                "completed": True,
                "correlationValidated": True,
                "exitCode": 0,
            },
            "functionalDivergences": divergences,
        }

    def write_content_document(
        self,
        kind: str,
        value: dict[str, object],
    ) -> dict[str, str]:
        raw = (
            json.dumps(value, indent=2, ensure_ascii=False) + "\n"
        ).encode("utf-8")
        digest = hashlib.sha256(raw).hexdigest()
        relative = f"tests/parity/evidence/{kind}/{digest}.json"
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)
        return {"path": relative, "sha256": digest}

    def write_content_file(
        self,
        kind: str,
        raw: bytes,
        suffix: str,
    ) -> dict[str, str]:
        digest = hashlib.sha256(raw).hexdigest()
        relative = (
            f"tests/parity/evidence/{kind}/{digest}{suffix}"
        )
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)
        return {"path": relative, "sha256": digest}

    def make_trx(self, run: dict[str, object]) -> bytes:
        result = run["results"][0]
        failed = result["outcome"] == "functional-divergence"
        error = ""
        if failed:
            marker = (
                "PARITY_FUNCTIONAL_DIVERGENCE|"
                f"{result['parityId']}|{result['failureCode']}"
            )
            message = (
                f"{parity.FUNCTIONAL_DIVERGENCE_TRX_EXCEPTION_TYPE} : "
                f"{marker}"
            )
            error = (
                "<Output><ErrorInfo>"
                f"<Message>{message}</Message>"
                "<StackTrace>test stack</StackTrace>"
                "</ErrorInfo></Output>"
            )
        counters = {
            "total": 1,
            "executed": 1,
            "passed": 0 if failed else 1,
            "failed": 1 if failed else 0,
            "error": 0,
            "timeout": 0,
            "aborted": 0,
            "inconclusive": 0,
            "notExecuted": 0,
            "disconnected": 0,
            "warning": 0,
            "notRunnable": 0,
        }
        counter_text = " ".join(
            f'{name}="{value}"'
            for name, value in counters.items()
        )
        summary = "Failed" if failed else "Completed"
        outcome = "Failed" if failed else "Passed"
        return (
            '<?xml version="1.0" encoding="utf-8"?>'
            "<TestRun><Results>"
            f'<UnitTestResult testName="'
            f'{result["acceptanceTestName"]}" outcome="{outcome}">'
            f"{error}</UnitTestResult></Results>"
            f'<ResultSummary outcome="{summary}">'
            f"<Counters {counter_text} />"
            "</ResultSummary></TestRun>"
        ).encode("utf-8")

    def referenced_document(
        self,
        reference: dict[str, str],
    ) -> dict[str, object]:
        return parity.load_json(self.root / reference["path"])

    def replace_run(
        self,
        field: str,
        mutate: object,
        *,
        platform: str | None = None,
    ) -> None:
        reference = (
            self.evidence["runs"][field]
            if platform is None
            else self.evidence["runs"][field][platform]
        )
        document = self.referenced_document(reference)
        mutate(document)
        replacement = self.write_content_document("runs", document)
        if platform is None:
            self.evidence["runs"][field] = replacement
        else:
            self.evidence["runs"][field][platform] = replacement
        report_reference = (
            self.evidence["testReports"][field]
            if platform is None
            else self.evidence["testReports"][field][platform]
        )
        execution_replacement = self.write_content_document(
            "executions",
            self.make_execution_envelope(
                document,
                replacement["sha256"],
                report_reference["sha256"],
            ),
        )
        if platform is None:
            self.evidence["executions"][field] = execution_replacement
        else:
            self.evidence["executions"][field][
                platform
            ] = execution_replacement
        self.rewrite_evidence()

    def write_artifact_document(
        self,
        relative_path: str,
        document: dict[str, object],
    ) -> Path:
        path = self.root / "artifacts" / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(
            json.dumps(document, indent=2, ensure_ascii=False) + "\n",
            encoding="utf-8",
            newline="\n",
        )
        return path

    def write_artifact_bytes(
        self,
        relative_path: str,
        raw: bytes,
    ) -> Path:
        path = self.root / "artifacts" / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)
        return path

    def make_live_artifacts(
        self,
        *,
        xplat_outcome: str = "functional-divergence",
        xplat_failure_code: str | None = "behavior-diverged",
        platform: str = "windows",
        revision: str = "f" * 40,
        tree: str = "6" * 40,
    ) -> tuple[Path, Path, Path]:
        provenance = self.make_provenance(self.parity_id)
        provenance_artifact_relative = (
            Path(self.runtime_provenance_relative)
            .relative_to("artifacts")
            .as_posix()
        )
        provenance_path = self.write_artifact_document(
            provenance_artifact_relative,
            provenance,
        )
        provenance_hash = parity.sha256_file(provenance_path)
        registry_entry = {
            **copy.deepcopy(self.legacy_oracle),
            "executable": self.oracle_executable_path.relative_to(
                self.root
            ).as_posix(),
            "executableSha256": self.oracle_executable_sha256,
            "provenance": self.runtime_provenance_relative,
            "provenanceSha256": provenance_hash,
        }
        registry = {
            "schemaVersion": 1,
            "entries": [
                {
                    field: registry_entry[field]
                    for field in (
                        parity.LEGACY_ORACLE_REGISTRY_ENTRY_FIELDS
                    )
                }
            ],
        }
        registry_raw = parity.serialize_lf_json(registry)
        self.registry_path = self.write_artifact_bytes(
            "legacy-oracle/registry.json",
            registry_raw,
        )
        self.registry_sha256 = hashlib.sha256(
            registry_raw
        ).hexdigest()
        legacy = self.make_run_document(
            "legacy",
            self.fixture["values"],
            "passed",
            None,
            provenance_digest=provenance_hash,
            revision=revision,
            tree=tree,
        )
        xplat_values = (
            self.fixture["values"]
            if xplat_outcome == "passed"
            else ["xplat-value"]
        )
        xplat = self.make_run_document(
            "xplat",
            xplat_values,
            xplat_outcome,
            xplat_failure_code,
            platform=platform,
            revision=revision,
            tree=tree,
        )
        legacy_path = self.write_artifact_document(
            "parity/legacy.json",
            legacy,
        )
        xplat_path = self.write_artifact_document(
            "parity/xplat.json",
            xplat,
        )
        self.legacy_test_report_path = self.write_artifact_bytes(
            "parity/legacy.trx",
            self.make_trx(legacy),
        )
        self.xplat_test_report_path = self.write_artifact_bytes(
            "parity/xplat.trx",
            self.make_trx(xplat),
        )
        self.legacy_execution_path = self.write_artifact_document(
            "parity/legacy.execution.json",
            self.make_execution_envelope(
                legacy,
                parity.sha256_file(legacy_path),
                parity.sha256_file(self.legacy_test_report_path),
            ),
        )
        self.xplat_execution_path = self.write_artifact_document(
            "parity/xplat.execution.json",
            self.make_execution_envelope(
                xplat,
                parity.sha256_file(xplat_path),
                parity.sha256_file(self.xplat_test_report_path),
            ),
        )
        return legacy_path, xplat_path, provenance_path

    def make_green_platform_maps(
        self,
        platforms: list[str],
    ) -> tuple[
        dict[str, Path],
        dict[str, Path],
        dict[str, Path],
    ]:
        results: dict[str, Path] = {}
        reports: dict[str, Path] = {}
        executions: dict[str, Path] = {}
        for platform in platforms:
            _, result_path, _ = self.make_live_artifacts(
                xplat_outcome="passed",
                xplat_failure_code=None,
                platform=platform,
            )
            results[platform] = self.write_artifact_bytes(
                f"parity/green/{platform}/xplat.json",
                result_path.read_bytes(),
            )
            reports[platform] = self.write_artifact_bytes(
                f"parity/green/{platform}/xplat.trx",
                self.xplat_test_report_path.read_bytes() + b"\n",
            )
            selected_document = parity.load_json(results[platform])
            executions[platform] = self.write_artifact_document(
                f"parity/green/{platform}/execution.json",
                self.make_execution_envelope(
                    selected_document,
                    parity.sha256_file(results[platform]),
                    parity.sha256_file(reports[platform]),
                ),
            )
        return results, reports, executions

    def make_full_suite_maps(
        self,
        platforms: list[str],
    ) -> tuple[
        dict[str, Path],
        dict[str, Path],
        dict[str, Path],
    ]:
        results: dict[str, Path] = {}
        reports: dict[str, Path] = {}
        executions: dict[str, Path] = {}
        self.assertTrue(set(platforms).issubset(parity.SUPPORTED_PLATFORMS))
        for platform in ("linux", "macos", "windows"):
            legacy_path, xplat_path, _ = self.make_live_artifacts(
                xplat_outcome="passed",
                xplat_failure_code=None,
                platform=platform,
            )
            xplat_key = f"{platform}/XPlat"
            xplat_result_relative = (
                "parity/xplat.json"
                if platform == "windows"
                else f"parity/full/{platform}/xplat.json"
            )
            xplat_report_relative = (
                "parity/test-results/xplat.trx"
                if platform == "windows"
                else f"parity/full/{platform}/xplat.trx"
            )
            xplat_execution_relative = (
                "parity/executions/xplat/execution.json"
                if platform == "windows"
                else (
                    f"parity/full/{platform}/"
                    "xplat.execution.json"
                )
            )
            results[xplat_key] = self.write_artifact_bytes(
                xplat_result_relative,
                xplat_path.read_bytes(),
            )
            reports[xplat_key] = self.write_artifact_bytes(
                xplat_report_relative,
                self.xplat_test_report_path.read_bytes(),
            )
            executions[xplat_key] = self.write_artifact_bytes(
                xplat_execution_relative,
                self.xplat_execution_path.read_bytes(),
            )
            if platform == "windows":
                results["windows/Legacy"] = self.write_artifact_bytes(
                    "parity/legacy.json",
                    legacy_path.read_bytes(),
                )
                reports["windows/Legacy"] = self.write_artifact_bytes(
                    "parity/test-results/legacy.trx",
                    self.legacy_test_report_path.read_bytes(),
                )
                executions["windows/Legacy"] = (
                    self.write_artifact_bytes(
                        "parity/executions/legacy/execution.json",
                        self.legacy_execution_path.read_bytes(),
                    )
                )
        return results, reports, executions

    def make_legacy_build_integration_trx(
        self,
        *,
        test_name: str = (
            "MorseRunner.LegacyParity.Tests.LegacyOracleTargetTests."
            "ConfiguredBuildRegistryAttestsAndExecutesEverySelectedCase"
        ),
        outcome: str = "Passed",
    ) -> bytes:
        failed = outcome != "Passed"
        return (
            '<?xml version="1.0" encoding="utf-8"?>'
            '<TestRun xmlns="http://microsoft.com/schemas/'
            'VisualStudio/TeamTest/2010"><Results>'
            f'<UnitTestResult testName="{test_name}" '
            f'outcome="{outcome}" />'
            "</Results>"
            f'<ResultSummary outcome="'
            f'{"Failed" if failed else "Completed"}">'
            '<Counters total="1" executed="1" passed="'
            f'{"0" if failed else "1"}" error="0" failed="'
            f'{"1" if failed else "0"}" timeout="0" aborted="0" '
            'inconclusive="0" passedButRunAborted="0" '
            'notRunnable="0" notExecuted="0" disconnected="0" '
            'warning="0" completed="0" inProgress="0" pending="0" />'
            "</ResultSummary></TestRun>"
        ).encode("utf-8")

    def make_legacy_build_integration_execution(
        self,
        test_report_sha256: str,
        *,
        selected_case_ids: list[str] | None = None,
        registry_sha256: str | None = None,
        revision: str | None = None,
        tree: str | None = None,
        process_exit_code: int = 0,
    ) -> dict[str, object]:
        return {
            "schemaVersion": 1,
            "target": "LegacyOracleBuildIntegration",
            "platform": "windows",
            "architecture": "x64",
            "runtimeIdentifier": "win-x64",
            "revision": revision or "f" * 40,
            "tree": tree or "6" * 40,
            "registrySha256": (
                registry_sha256 or self.registry_sha256
            ),
            "selectedCaseIds": (
                selected_case_ids or [self.parity_id]
            ),
            "testName": (
                "MorseRunner.LegacyParity.Tests."
                "LegacyOracleTargetTests."
                "ConfiguredBuildRegistryAttestsAndExecutesEverySelectedCase"
            ),
            "testReportSha256": test_report_sha256,
            "testProcessExitCode": process_exit_code,
            "wrapper": {
                "completed": True,
                "correlationValidated": True,
                "exitCode": process_exit_code,
            },
        }

    def make_full_suite_package(
        self,
        results: dict[str, Path],
        reports: dict[str, Path],
        executions: dict[str, Path],
    ) -> tuple[Path, str]:
        integration = self.write_artifact_bytes(
            "parity/test-results/"
            "legacy-oracle-build-integration.trx",
            self.make_legacy_build_integration_trx(),
        )
        integration_execution_document = (
            self.make_legacy_build_integration_execution(
                parity.sha256_file(integration)
            )
        )
        integration_execution_raw = parity.serialize_lf_json(
            integration_execution_document
        )
        integration_execution_sha256 = hashlib.sha256(
            integration_execution_raw
        ).hexdigest()
        integration_execution = self.write_artifact_bytes(
            "parity/executions/legacy-oracle-build-integration/"
            f"{integration_execution_sha256}.json",
            integration_execution_raw,
        )
        registry = parity.load_json(self.registry_path)
        source_paths = [
            self.registry_path,
            integration,
            integration_execution,
            results["windows/Legacy"],
            results["windows/XPlat"],
            reports["windows/Legacy"],
            reports["windows/XPlat"],
            executions["windows/Legacy"],
            executions["windows/XPlat"],
        ]
        for entry in registry["entries"]:
            source_paths.extend(
                (
                    self.root / entry["executable"],
                    self.root / entry["provenance"],
                )
            )
        relative_sources = {
            path.resolve().relative_to(self.root).as_posix(): path
            for path in source_paths
        }
        index = {
            "schemaVersion": 1,
            "platform": "windows",
            "target": "Both",
            "registry": {
                "path": self.registry_path.resolve().relative_to(
                    self.root
                ).as_posix(),
                "sha256": self.registry_sha256,
            },
            "files": [
                {
                    "path": relative,
                    "sha256": parity.sha256_file(path),
                }
                for relative, path in sorted(
                    relative_sources.items(),
                    key=lambda pair: parity.utf16_ordinal_key(pair[0]),
                )
            ],
        }
        index_raw = parity.serialize_lf_json(index)
        index_sha256 = hashlib.sha256(index_raw).hexdigest()
        package_root = (
            self.root
            / "artifacts/parity-imports"
            / index_sha256
        )
        for relative, source in relative_sources.items():
            destination = package_root / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, destination)
        index_path = (
            package_root
            / "artifacts/parity-full-suite/"
            "windows-both-package-index"
            / f"{index_sha256}.json"
        )
        index_path.parent.mkdir(parents=True, exist_ok=True)
        index_path.write_bytes(index_raw)
        self.registry_path = (
            package_root / index["registry"]["path"]
        )
        results["windows/Legacy"] = (
            package_root / "artifacts/parity/legacy.json"
        )
        results["windows/XPlat"] = (
            package_root / "artifacts/parity/xplat.json"
        )
        reports["windows/Legacy"] = (
            package_root
            / "artifacts/parity/test-results/legacy.trx"
        )
        reports["windows/XPlat"] = (
            package_root
            / "artifacts/parity/test-results/xplat.trx"
        )
        executions["windows/Legacy"] = (
            package_root
            / "artifacts/parity/executions/legacy/execution.json"
        )
        executions["windows/XPlat"] = (
            package_root
            / "artifacts/parity/executions/xplat/execution.json"
        )
        return index_path, index_sha256

    def make_live_manifest(self) -> dict[str, object]:
        return {
            "reference": {"revision": self.reference_revision},
            "cases": [self.case],
        }

    def validate_live_results(
        self,
        manifest: dict[str, object],
        legacy_path: Path | None,
        xplat_path: Path | None,
        provenance_path: Path | None,
        mode: str,
        **_: object,
    ) -> dict[str, object]:
        registry_entry = {
            **copy.deepcopy(self.legacy_oracle),
            "executable": (
                "artifacts/legacy-oracle/LegacyOracle.exe"
            ),
            "executableSha256": self.fixture[
                "oracleExecutableSha256"
            ],
            "provenance": self.runtime_provenance_relative,
            "provenanceSha256": (
                parity.sha256_file(provenance_path)
                if provenance_path is not None
                else "9" * 64
            ),
        }
        registry_result = (
            self.root / "artifacts/legacy-oracle/registry.json",
            self.registry_sha256,
            {self.legacy_oracle["versionId"]: registry_entry},
        )
        report_result = (
            self.root / "artifacts/parity/results.trx",
            b"<TestRun />",
            "a" * 64,
        )
        with (
            patch.object(
                parity,
                "validate_legacy_oracle_registry",
                return_value=registry_result,
            ),
            patch.object(
                parity,
                "validate_test_report",
                return_value=report_result,
            ),
            patch.object(
                parity,
                "validate_execution_envelope",
                return_value=(
                    self.root / "artifacts/parity/execution.json",
                    b"{}\n",
                    "b" * 64,
                ),
            ),
        ):
            return parity.validate_live_results(
                manifest,
                legacy_path,
                xplat_path,
                (
                    provenance_path
                    if provenance_path is not None
                    else None
                ),
                (
                    self.registry_sha256
                    if legacy_path is not None
                    else None
                ),
                (
                    report_result[0]
                    if legacy_path is not None
                    else None
                ),
                (
                    report_result[0]
                    if xplat_path is not None
                    else None
                ),
                mode,
                legacy_execution_path=(
                    self.legacy_execution_path
                    if legacy_path is not None
                    else None
                ),
                xplat_execution_path=(
                    self.xplat_execution_path
                    if xplat_path is not None
                    else None
                ),
                root=self.root,
            )

    def make_retained_evidence(self, parity_id: str) -> dict[str, object]:
        fixture_relative = (
            f"tests/parity/fixtures/legacy/{parity_id.replace('.', '-')}.json"
        )
        self.write_json(
            fixture_relative,
            {
                "revision": self.reference_revision,
                "parityId": parity_id,
                "oracle": self.oracle_relative,
                "toolchain": {
                    "lazarus": "4.6",
                    "fpc": "3.2.2",
                    "target": "x86_64-win64",
                },
                "values": ["legacy-value"],
            },
        )
        return {
            "schemaVersion": 1,
            "parityId": parity_id,
            "referenceRevision": self.reference_revision,
            "capturedAtUtc": "2026-07-18T00:00:00Z",
            "legacy": {
                "outcome": "pass",
                "source": self.oracle_relative,
                "fixture": fixture_relative,
                "observedValueCount": 1,
            },
            "xplat": {
                "outcome": "pass",
                "source": self.xplat_source_relative,
                "observedValueCount": 1,
            },
            "classification": "legacy-v1-uncertified",
            "retention": {
                "status": "legacy-v1-noncertifying",
                "reason": "Retained for provenance only.",
            },
        }

    def validate_current_case(self) -> None:
        parity.validate_active_case(
            self.case,
            self.reference_revision,
            manifest=self.make_live_manifest(),
            root=self.root,
        )

    def rewrite_evidence(self) -> None:
        self.write_json(self.evidence_relative, self.evidence)

    def clear_evidence(self) -> None:
        for path in (self.root / "tests/parity/evidence").glob("*.json"):
            path.unlink()

    def test_valid_red_case_evidence_is_accepted(self) -> None:
        self.validate_current_case()

    def test_repository_path_traversal_and_absolute_paths_are_rejected(
        self,
    ) -> None:
        with self.assertRaisesRegex(ValueError, "escapes"):
            parity.resolve_repo_file(
                "../outside.json",
                "tests/parity/evidence",
                "evidence",
                root=self.root,
            )
        with self.assertRaisesRegex(ValueError, "repository-relative"):
            parity.resolve_repo_file(
                str((self.root / self.evidence_relative).resolve()),
                "tests/parity/evidence",
                "evidence",
                root=self.root,
            )

    def test_active_oracle_version_and_paths_are_canonical_and_bound(
        self,
    ) -> None:
        unversioned = (
            self.root / "tests/parity/legacy-oracle/LegacyOracle.lpr"
        )
        unversioned.write_text(
            "program HistoricalLegacyOracle;\n",
            encoding="utf-8",
        )
        mutations = (
            (
                "version-underscore",
                lambda descriptor: descriptor.update(
                    {"versionId": "legacy_oracle-v1"}
                ),
                "stable versioned ID",
            ),
            (
                "source-dot",
                lambda descriptor: descriptor.update(
                    {
                        "source": (
                            "tests/parity/legacy-oracle/v1/./"
                            "LegacyOracle.lpr"
                        )
                    }
                ),
                "canonical repository-relative path",
            ),
            (
                "source-empty-segment",
                lambda descriptor: descriptor.update(
                    {
                        "source": (
                            "tests/parity/legacy-oracle/v1//"
                            "LegacyOracle.lpr"
                        )
                    }
                ),
                "canonical repository-relative path",
            ),
            (
                "source-parent-alias",
                lambda descriptor: descriptor.update(
                    {
                        "source": (
                            "tests/parity/legacy-oracle/v1/sub/../"
                            "LegacyOracle.lpr"
                        )
                    }
                ),
                "canonical repository-relative path",
            ),
            (
                "recipe-dot",
                lambda descriptor: descriptor.update(
                    {
                        "buildRecipe": (
                            "tests/parity/legacy-oracle/v1/./"
                            "build-recipe.json"
                        )
                    }
                ),
                "canonical repository-relative path",
            ),
            (
                "wrong-version-directory",
                lambda descriptor: descriptor.update(
                    {"versionId": "legacy-oracle-v2"}
                ),
                "exact version directory",
            ),
            (
                "unversioned-source",
                lambda descriptor: descriptor.update(
                    {
                        "source": (
                            "tests/parity/legacy-oracle/"
                            "LegacyOracle.lpr"
                        ),
                        "sourceSha256": parity.sha256_file(unversioned),
                    }
                ),
                "exact version directory",
            ),
        )
        for name, mutate, message in mutations:
            with self.subTest(name=name):
                changed = copy.deepcopy(self.case)
                mutate(changed["legacyOracle"])
                with self.assertRaisesRegex(ValueError, message):
                    parity.validate_legacy_oracle_descriptor(
                        changed,
                        root=self.root,
                    )

    def test_shared_oracle_version_requires_an_identical_descriptor(
        self,
    ) -> None:
        registered: dict[str, dict[str, object]] = {}
        parity.register_shared_legacy_oracle_descriptor(
            registered,
            self.legacy_oracle,
        )
        parity.register_shared_legacy_oracle_descriptor(
            registered,
            copy.deepcopy(self.legacy_oracle),
        )
        self.assertEqual(
            self.legacy_oracle,
            registered[self.legacy_oracle["versionId"]],
        )

        conflict = copy.deepcopy(self.legacy_oracle)
        conflict["sourceSha256"] = "9" * 64
        with self.assertRaisesRegex(
            ValueError,
            "conflicting immutable descriptors",
        ):
            parity.register_shared_legacy_oracle_descriptor(
                registered,
                conflict,
            )

    def test_shared_oracle_provenance_binds_exact_selected_case_ids(
        self,
    ) -> None:
        provenance = self.make_provenance(self.parity_id)
        expected = ["case.two", self.parity_id]
        with self.assertRaisesRegex(ValueError, "selectedCaseIds"):
            parity.validate_legacy_provenance(
                provenance,
                self.parity_id,
                self.case,
                self.fixture,
                self.reference_revision,
                expected_selected_case_ids=expected,
                root=self.root,
            )
        provenance["selectedCaseIds"] = expected
        second_observation = copy.deepcopy(
            provenance["observations"][0]
        )
        second_observation["scenario"] = "case.two"
        provenance["observations"] = [
            second_observation,
            provenance["observations"][0],
        ]
        parity.validate_legacy_provenance(
            provenance,
            self.parity_id,
            self.case,
            self.fixture,
            self.reference_revision,
            expected_selected_case_ids=expected,
            root=self.root,
        )

    def test_historical_gate_survives_a_later_case_promotion(
        self,
    ) -> None:
        case_a = copy.deepcopy(self.case)
        case_a["id"] = "case.a"
        case_b = copy.deepcopy(self.case)
        case_b["id"] = "case.b"
        snapshot = parity.build_regression_manifest_case_snapshot(
            [case_a, case_b],
            {"case.a"},
        )
        self.assertEqual(
            ["passed", "functional-divergence"],
            [
                entry["expectedXPlatOutcome"]
                for entry in snapshot
            ],
        )

        for current in (case_a, case_b):
            current.update(
                {
                    "status": "both-green",
                    "xplatTestStatus": "pass",
                    "failureCode": None,
                    "firstGreenCommit": "f" * 40,
                }
            )
        historical, ids, _, _ = (
            parity.materialize_regression_manifest_case_snapshot(
                snapshot,
                [case_a, case_b],
                "sequential promotion",
            )
        )
        self.assertEqual(["case.a", "case.b"], ids)
        self.assertEqual(
            ["both-green", "legacy-green-xplat-red"],
            [case["status"] for case in historical],
        )

    def test_historical_schema_v2_hash_case_is_noncertifying_only(
        self,
    ) -> None:
        retained = {
            "schemaVersion": 2,
            "revision": self.reference_revision,
            "tree": self.reference_tree,
            "parityId": self.parity_id,
            "oracle": self.oracle_relative,
            "oracleSourceSha256": "A" * 64,
            "oracleExecutableSha256": "B" * 64,
            "referenceDefinitionSha256": "C" * 64,
            "oracleProvenanceSha256": "D" * 64,
            "toolchain": {
                "lazarus": "4.6",
                "fpc": "3.2.2",
                "target": "x86_64-win64",
                "compilerSha256": "E" * 64,
                "backendCompilerSha256": "F" * 64,
                "lazbuildSha256": "A" * 64,
                "fingerprintSha256": "B" * 64,
            },
            "values": ["historical"],
        }
        self.assertEqual(
            1,
            parity.validate_retained_fixture_v2(
                retained,
                self.parity_id,
                root=self.root,
            ),
        )

        active = copy.deepcopy(self.fixture)
        active["oracleSourceSha256"] = str(
            active["oracleSourceSha256"]
        ).upper()
        self.write_json(self.fixture_relative, active)
        with self.assertRaisesRegex(ValueError, "SHA-256 digest"):
            self.validate_current_case()

    def test_full_suite_platform_target_path_parser_is_exact(self) -> None:
        parsed = parity.parse_full_suite_path_arguments(
            [
                "windows/Legacy=artifacts/legacy.json",
                "windows/XPlat=artifacts/windows.json",
                "linux/XPlat=artifacts/linux.json",
                "macos/XPlat=artifacts/macos.json",
            ],
            "--full-suite-result",
        )
        self.assertEqual(
            {
                "windows/Legacy",
                "windows/XPlat",
                "linux/XPlat",
                "macos/XPlat",
            },
            set(parsed),
        )
        for value, message in (
            ("windows=path", "platform/Target"),
            ("windows/xplat=path", "unsupported target"),
            ("freebsd/XPlat=path", "unsupported platform"),
        ):
            with self.subTest(value=value):
                with self.assertRaisesRegex(ValueError, message):
                    parity.parse_full_suite_path_arguments(
                        [value],
                        "--full-suite-result",
                    )
        with self.assertRaisesRegex(ValueError, "repeats"):
            parity.parse_full_suite_path_arguments(
                [
                    "windows/XPlat=one",
                    "windows/XPlat=two",
                ],
                "--full-suite-result",
            )

    def test_green_cli_requires_package_index_pair(self) -> None:
        values: dict[str, object] = {
            "mode": "Baseline",
            "legacy_root": None,
            "base_manifest": None,
            "write_report": False,
            "verify_live_results": False,
            "promote_evidence": "green",
            "case_id": [self.parity_id],
            "legacy_results": None,
            "xplat_results": None,
            "legacy_oracle_registry": Path("registry.json"),
            "legacy_oracle_registry_sha256": "1" * 64,
            "legacy_test_report": None,
            "xplat_test_report": None,
            "legacy_execution": None,
            "xplat_execution": None,
            "green_result": ["windows=result.json"],
            "green_test_report": ["windows=result.trx"],
            "green_execution": ["windows=execution.json"],
            "full_suite_result": ["windows/Legacy=legacy.json"],
            "full_suite_test_report": [
                "windows/Legacy=legacy.trx"
            ],
            "full_suite_execution": [
                "windows/Legacy=legacy-execution.json"
            ],
            "full_suite_package_index": None,
            "full_suite_package_index_sha256": None,
            "capture_green_case_id": None,
            "green_regression_case_id": None,
        }
        args = parity.argparse.Namespace(**values)
        with self.assertRaisesRegex(ValueError, "package index"):
            parity.validate_cli_option_contract(args)

        args.full_suite_package_index = Path("package-index.json")
        with self.assertRaisesRegex(ValueError, "package index"):
            parity.validate_cli_option_contract(args)
        args.full_suite_package_index_sha256 = "2" * 64
        parity.validate_cli_option_contract(args)

        args.promote_evidence = "red"
        args.green_result = None
        args.green_test_report = None
        args.green_execution = None
        args.full_suite_result = None
        args.full_suite_test_report = None
        args.full_suite_execution = None
        with self.assertRaisesRegex(ValueError, "forbidden for red"):
            parity.validate_cli_option_contract(args)

    def test_legacy_build_integration_trx_requires_exact_pass(
        self,
    ) -> None:
        path = self.write_artifact_bytes(
            "parity/build-integration.trx",
            self.make_legacy_build_integration_trx(),
        )
        parity.validate_legacy_oracle_build_integration_report(path)

        mutations = (
            (
                self.make_legacy_build_integration_trx(
                    test_name="WrongTest"
                ),
                "configured-registry test",
            ),
            (
                self.make_legacy_build_integration_trx(
                    outcome="Failed"
                ),
                "configured-registry test",
            ),
            (b"<not-a-test-run>", "not valid XML"),
        )
        for raw, message in mutations:
            with self.subTest(message=message):
                path.write_bytes(raw)
                with self.assertRaisesRegex(ValueError, message):
                    parity.validate_legacy_oracle_build_integration_report(
                        path
                    )

    def test_legacy_build_integration_execution_binds_process(
        self,
    ) -> None:
        report_hash = "3" * 64
        path = self.write_artifact_document(
            "parity/build-integration.execution.json",
            self.make_legacy_build_integration_execution(report_hash),
        )
        _, digest = (
            parity.validate_legacy_oracle_build_integration_execution(
                path,
                test_report_sha256=report_hash,
                registry_sha256=self.registry_sha256,
                expected_selected_case_ids=[self.parity_id],
                expected_run_context={
                    "platform": "windows",
                    "processArchitecture": "x64",
                    "runtimeIdentifier": "win-x64",
                    "xplat": {
                        "revision": "f" * 40,
                        "tree": "6" * 40,
                        "clean": True,
                    },
                },
            )
        )
        self.assertEqual(parity.sha256_file(path), digest)

        changes = (
            ("testProcessExitCode", 1, "actual successful process"),
            ("testName", "WrongTest", "testName"),
            ("testReportSha256", "4" * 64, "artifact hashes"),
            ("architecture", "raced-architecture", "runtime identity"),
            ("runtimeIdentifier", "win-raced", "runtime identity"),
        )
        original = parity.load_json(path)
        for field, value, message in changes:
            with self.subTest(field=field):
                changed = copy.deepcopy(original)
                changed[field] = value
                if field == "testProcessExitCode":
                    changed["wrapper"]["exitCode"] = value
                self.write_json(
                    "artifacts/parity/"
                    "build-integration.execution.json",
                    changed,
                )
                with self.assertRaisesRegex(ValueError, message):
                    parity.validate_legacy_oracle_build_integration_execution(
                        path,
                        test_report_sha256=report_hash,
                        registry_sha256=self.registry_sha256,
                        expected_selected_case_ids=[self.parity_id],
                        expected_run_context={
                            "platform": "windows",
                            "processArchitecture": "x64",
                            "runtimeIdentifier": "win-x64",
                            "xplat": {
                                "revision": "f" * 40,
                                "tree": "6" * 40,
                                "clean": True,
                            },
                        },
                    )

    def test_active_fixture_must_use_schema_v2(self) -> None:
        del self.fixture["schemaVersion"]
        self.write_json(self.fixture_relative, self.fixture)

        with self.assertRaisesRegex(ValueError, "must use schema version 2"):
            self.validate_current_case()

    def test_fixture_identity_and_revision_must_match(self) -> None:
        for field, value, message in (
            ("parityId", "wrong.case", "fixture parityId"),
            ("revision", "9" * 40, "fixture revision"),
        ):
            with self.subTest(field=field):
                changed = copy.deepcopy(self.fixture)
                changed[field] = value
                self.write_json(self.fixture_relative, changed)
                with self.assertRaisesRegex(ValueError, message):
                    self.validate_current_case()
        self.write_json(self.fixture_relative, self.fixture)

    def test_fixture_requires_all_provenance_digests(self) -> None:
        del self.fixture["toolchain"]["backendCompilerSha256"]
        self.write_json(self.fixture_relative, self.fixture)

        with self.assertRaisesRegex(
            ValueError,
            "missing fields: backendCompilerSha256",
        ):
            self.validate_current_case()

    def test_fixture_rejects_malformed_provenance_digest(self) -> None:
        self.fixture["oracleSourceSha256"] = "not-a-digest"
        self.write_json(self.fixture_relative, self.fixture)

        with self.assertRaisesRegex(ValueError, "SHA-256 digest"):
            self.validate_current_case()

    def test_fixture_binds_reference_definition_and_fingerprint(self) -> None:
        for field_path, message in (
            (("referenceDefinitionSha256",), "referenceDefinitionSha256"),
            (
                ("toolchain", "fingerprintSha256"),
                "toolchain fingerprintSha256",
            ),
        ):
            with self.subTest(field_path=field_path):
                changed = copy.deepcopy(self.fixture)
                if len(field_path) == 1:
                    changed[field_path[0]] = "9" * 64
                else:
                    changed[field_path[0]][field_path[1]] = "9" * 64
                self.write_json(self.fixture_relative, changed)
                with self.assertRaisesRegex(ValueError, message):
                    self.validate_current_case()
        self.write_json(self.fixture_relative, self.fixture)

    def test_fixture_binds_tree_versions_and_toolchain_hashes(self) -> None:
        changes = (
            (("tree",), "9" * 40, "fixture tree"),
            (("toolchain", "lazarus"), "4.5", "toolchain lazarus"),
            (
                ("toolchain", "compilerSha256"),
                "9" * 64,
                "toolchain compilerSha256",
            ),
            (
                ("toolchain", "backendCompilerSha256"),
                "9" * 64,
                "toolchain backendCompilerSha256",
            ),
            (
                ("toolchain", "lazbuildSha256"),
                "9" * 64,
                "toolchain lazbuildSha256",
            ),
        )
        for field_path, value, message in changes:
            with self.subTest(field_path=field_path):
                changed = copy.deepcopy(self.fixture)
                if len(field_path) == 1:
                    changed[field_path[0]] = value
                else:
                    changed[field_path[0]][field_path[1]] = value
                self.write_json(self.fixture_relative, changed)
                with self.assertRaisesRegex(ValueError, message):
                    self.validate_current_case()
        self.write_json(self.fixture_relative, self.fixture)

    def test_actual_oracle_source_hash_is_recomputed(self) -> None:
        (self.root / self.oracle_relative).write_text(
            "program Tampered;\n",
            encoding="utf-8",
        )

        with self.assertRaisesRegex(
            ValueError,
            "case legacyOracle source",
        ):
            self.validate_current_case()

    def test_legacy_reference_bundle_must_exist_and_match_hash(self) -> None:
        bundle_path = self.root / "tests/parity/legacy-reference.bundle"
        original = bundle_path.read_bytes()

        bundle_path.unlink()
        with self.assertRaisesRegex(
            ValueError,
            "legacy reference bundle does not exist",
        ):
            self.validate_current_case()

        bundle_path.write_bytes(original + b"tampered")
        with self.assertRaisesRegex(
            ValueError,
            "bundleSha256 does not match",
        ):
            self.validate_current_case()

        bundle_path.write_bytes(original)
        definition = self.make_legacy_reference()
        definition["bundleSha256"] = "9" * 64
        self.write_json(
            "tests/parity/legacy-reference.json",
            definition,
        )
        with self.assertRaisesRegex(
            ValueError,
            "bundleSha256 does not match",
        ):
            self.validate_current_case()

    def test_executable_hash_is_historical_shape_provenance(self) -> None:
        self.fixture["oracleExecutableSha256"] = "4" * 64
        self.write_json(self.fixture_relative, self.fixture)
        self.evidence = self.make_red_evidence(self.parity_id)
        self.rewrite_evidence()

        self.validate_current_case()

    def test_active_values_are_limited_to_json_strings(self) -> None:
        self.fixture["values"] = [1]
        self.write_json(self.fixture_relative, self.fixture)

        with self.assertRaisesRegex(ValueError, "must be JSON strings"):
            self.validate_current_case()

    def test_fixture_asserted_count_must_agree(self) -> None:
        self.case["assertions"]["observedValueCount"] = 2

        with self.assertRaisesRegex(ValueError, "asserted count"):
            self.validate_current_case()

    def test_duplicate_json_fields_are_rejected(self) -> None:
        path = self.root / self.evidence_relative
        path.write_text(
            '{"schemaVersion": 2, "schemaVersion": 2}\n',
            encoding="utf-8",
        )

        with self.assertRaisesRegex(ValueError, "duplicate JSON field"):
            parity.load_json(path)

    def test_active_evidence_schema_identity_and_revision_are_bound(self) -> None:
        changes = (
            ("schemaVersion", 1, "must use schema 2"),
            ("parityId", "wrong.case", "evidence parityId"),
            ("referenceRevision", "9" * 40, "evidence revision"),
        )
        for field, value, message in changes:
            with self.subTest(field=field):
                changed = copy.deepcopy(self.evidence)
                changed[field] = value
                self.write_json(self.evidence_relative, changed)
                with self.assertRaisesRegex(ValueError, message):
                    self.validate_current_case()
        self.rewrite_evidence()

    def test_evidence_fixture_path_must_match_case(self) -> None:
        self.evidence["fixture"]["path"] = (
            "tests/parity/fixtures/legacy/other.json"
        )
        self.rewrite_evidence()

        with self.assertRaisesRegex(ValueError, "fixture path"):
            self.validate_current_case()

    def test_evidence_classification_status_and_failure_code_are_bound(
        self,
    ) -> None:
        changes = (
            ("classification", "both-green", "classification"),
            ("case-status", "both-green", "classification"),
            ("failure-code", "other", "failureCode"),
        )
        for kind, value, message in changes:
            with self.subTest(kind=kind):
                self.case = self.make_case(self.parity_id)
                self.evidence = self.make_red_evidence(self.parity_id)
                if kind == "classification":
                    self.evidence["classification"] = value
                elif kind == "case-status":
                    self.case["status"] = value
                else:
                    self.case["failureCode"] = value
                self.rewrite_evidence()
                with self.assertRaisesRegex(ValueError, message):
                    self.validate_current_case()

    def test_red_evidence_requires_first_divergence(self) -> None:
        self.replace_run(
            "xplatRed",
            lambda document: document["results"][0].update(
                {"firstDivergence": None}
            ),
        )

        with self.assertRaisesRegex(ValueError, "firstDivergence"):
            self.validate_current_case()

    def test_fixture_bytes_are_bound_by_evidence_hash(self) -> None:
        fixture_path = self.root / self.fixture_relative
        fixture_path.write_bytes(fixture_path.read_bytes() + b" ")

        with self.assertRaisesRegex(ValueError, "fixture sha256"):
            self.validate_current_case()

    def test_fixture_hash_tampering_is_rejected(self) -> None:
        self.evidence["fixture"]["sha256"] = "0" * 64
        self.rewrite_evidence()

        with self.assertRaisesRegex(ValueError, "fixture sha256"):
            self.validate_current_case()

    def test_observed_values_and_hash_tampering_are_rejected(self) -> None:
        for field, value in (
            ("observedValues", ["tampered"]),
            ("observedValuesSha256", "0" * 64),
        ):
            with self.subTest(field=field):
                self.evidence = self.make_red_evidence(self.parity_id)
                self.replace_run(
                    "legacy",
                    lambda document, field=field, value=value: (
                        document["results"][0].update({field: value})
                    ),
                )
                with self.assertRaisesRegex(
                    ValueError,
                    "observedValues",
                ):
                    self.validate_current_case()
        self.rewrite_evidence()

    def test_result_sources_must_be_executable_repository_paths(self) -> None:
        self.replace_run(
            "xplatRed",
            lambda document: document["results"][0].update(
                {"evidenceSource": ""}
            ),
        )

        with self.assertRaisesRegex(ValueError, "evidenceSource"):
            self.validate_current_case()

    def test_observed_values_hash_known_vector(self) -> None:
        values = ["A", "é", 'quote"', "line\n"]
        self.assertEqual(
            "2acb493cf0bbc08a9623affff082ec06082b1be1d6a85949b4b9daea32033cba",
            parity.canonical_json_sha256(values),
        )

    def test_crlf_transport_does_not_change_logical_values_digest(
        self,
    ) -> None:
        values = ["first\r\nsecond", "é"]
        compact = json.dumps(
            values,
            ensure_ascii=False,
            separators=(",", ":"),
        )
        pretty_crlf = json.dumps(
            values,
            ensure_ascii=False,
            indent=2,
        ).replace("\n", "\r\n")

        self.assertEqual(
            parity.canonical_json_sha256(
                json.loads(compact)
            ),
            parity.canonical_json_sha256(
                json.loads(pretty_crlf)
            ),
        )

    def test_live_result_aggregation_accepts_exact_manifest_red(
        self,
    ) -> None:
        legacy, xplat, provenance = self.make_live_artifacts()

        summary = self.validate_live_results(
            self.make_live_manifest(),
            legacy,
            xplat,
            provenance,
            "Development",
            root=self.root,
        )

        self.assertEqual(1, summary["legacy"]["count"])
        self.assertEqual(1, summary["xplat"]["count"])
        self.assertEqual("windows", summary["xplat"]["platform"])

    def test_live_results_reject_status_adapter_and_hash_mismatch(
        self,
    ) -> None:
        changes = (
            ("outcome", "passed", "outcome is not functional-divergence"),
            ("adapter", "WrongAdapter", "adapter does not match"),
            (
                "caseDefinitionSha256",
                "0" * 64,
                "caseDefinitionSha256 is stale",
            ),
            ("fixtureSha256", "0" * 64, "fixtureSha256 is stale"),
        )
        for field, value, message in changes:
            with self.subTest(field=field):
                legacy, xplat, provenance = self.make_live_artifacts()
                document = parity.load_json(xplat)
                result = document["results"][0]
                result[field] = value
                if field == "outcome":
                    result["failureCode"] = None
                    result["observedValues"] = self.fixture["values"]
                    result["observedValueCount"] = 1
                    result["observedValuesSha256"] = (
                        parity.canonical_json_sha256(
                            self.fixture["values"]
                        )
                    )
                    result["firstDivergence"] = None
                self.write_artifact_document(
                    "parity/xplat.json",
                    document,
                )
                with self.assertRaisesRegex(ValueError, message):
                    self.validate_live_results(
                        self.make_live_manifest(),
                        legacy,
                        xplat,
                        provenance,
                        "Development",
                        root=self.root,
                    )

    def test_not_runnable_and_stale_expected_ids_never_certify(
        self,
    ) -> None:
        for kind, message in (
            ("not-runnable", "was not runnable"),
            ("expected-ids", "active manifest cases"),
        ):
            with self.subTest(kind=kind):
                legacy, xplat, provenance = self.make_live_artifacts()
                document = parity.load_json(xplat)
                if kind == "not-runnable":
                    result = document["results"][0]
                    result["outcome"] = "not-runnable"
                    result["failureCode"] = "adapter-failed"
                else:
                    document["expectedParityIds"] = ["other.case"]
                self.write_artifact_document(
                    "parity/xplat.json",
                    document,
                )
                with self.assertRaisesRegex(ValueError, message):
                    self.validate_live_results(
                        self.make_live_manifest(),
                        legacy,
                        xplat,
                        provenance,
                        "Development",
                        root=self.root,
                    )

    def test_live_results_bind_platform_runtime_and_clean_context(
        self,
    ) -> None:
        changes = (
            (
                "platform",
                "linux",
                "no active cases for linux",
            ),
            (
                "runtimeIdentifier",
                "linux-x64",
                "runtimeIdentifier does not match platform",
            ),
            ("clean", False, "runContext must be clean"),
        )
        for field, value, message in changes:
            with self.subTest(field=field):
                legacy, xplat, provenance = self.make_live_artifacts()
                document = parity.load_json(xplat)
                if field == "clean":
                    document["runContext"]["xplat"]["clean"] = value
                else:
                    document["runContext"][field] = value
                    if field == "platform":
                        document["runContext"][
                            "runtimeIdentifier"
                        ] = "linux-x64"
                self.write_artifact_document(
                    "parity/xplat.json",
                    document,
                )
                with self.assertRaisesRegex(ValueError, message):
                    self.validate_live_results(
                        self.make_live_manifest(),
                        legacy,
                        xplat,
                        provenance,
                        "Development",
                        root=self.root,
                    )

    def test_live_results_select_only_cases_for_current_platform(
        self,
    ) -> None:
        legacy, xplat, provenance = self.make_live_artifacts()
        linux_case = copy.deepcopy(self.case)
        linux_case.update(
            {
                "id": "test.case.linux",
                "platforms": ["linux"],
                "fixture": (
                    "tests/parity/fixtures/legacy/linux-only.json"
                ),
                "evidence": (
                    "tests/parity/evidence/linux-only.baseline.json"
                ),
            }
        )
        manifest = self.make_live_manifest()
        manifest["cases"].append(linux_case)

        summary = self.validate_live_results(
            manifest,
            legacy,
            xplat,
            provenance,
            "Development",
            root=self.root,
        )

        self.assertEqual(1, summary["legacy"]["count"])
        self.assertEqual(1, summary["xplat"]["count"])

    def test_live_results_must_match_fresh_xplat_repository_state(
        self,
    ) -> None:
        changes = (
            (
                {
                    "revision": "9" * 40,
                    "tree": "6" * 40,
                    "clean": True,
                },
                "freshly inspected",
            ),
            (
                {
                    "revision": "f" * 40,
                    "tree": "9" * 40,
                    "clean": True,
                },
                "freshly inspected",
            ),
            (
                {
                    "revision": "f" * 40,
                    "tree": "6" * 40,
                    "clean": False,
                },
                "changed during or after",
            ),
        )
        for current_context, message in changes:
            with self.subTest(current_context=current_context):
                legacy, xplat, provenance = self.make_live_artifacts()
                with patch.object(
                    parity,
                    "inspect_current_xplat_repository",
                    return_value=current_context,
                ):
                    with self.assertRaisesRegex(ValueError, message):
                        self.validate_live_results(
                            self.make_live_manifest(),
                            legacy,
                            xplat,
                            provenance,
                            "Development",
                            root=self.root,
                        )

    def test_release_live_gate_requires_both_targets(self) -> None:
        _, xplat, _ = self.make_live_artifacts()

        with self.assertRaisesRegex(ValueError, "requires both"):
            self.validate_live_results(
                self.make_live_manifest(),
                None,
                xplat,
                None,
                "Release",
                root=self.root,
            )

    def test_red_promotion_retains_full_content_addressed_documents(
        self,
    ) -> None:
        legacy, xplat, provenance = self.make_live_artifacts()
        (self.root / self.evidence_relative).unlink()
        manifest = self.make_live_manifest()

        evidence_path = parity.promote_evidence(
            manifest,
            "red",
            self.parity_id,
            legacy,
            xplat,
            self.registry_path,
            registry_sha256=self.registry_sha256,
            legacy_test_report_path=self.legacy_test_report_path,
            xplat_test_report_path=self.xplat_test_report_path,
            legacy_execution_path=self.legacy_execution_path,
            xplat_execution_path=self.xplat_execution_path,
            root=self.root,
        )

        evidence = parity.load_json(evidence_path)
        self.assertEqual(
            "legacy-green-xplat-red",
            evidence["classification"],
        )
        self.assertEqual({}, evidence["runs"]["xplatGreen"])
        for reference in (
            evidence["legacyOracle"]["retainedProvenance"],
            evidence["runs"]["legacy"],
            evidence["runs"]["xplatRed"],
        ):
            retained = self.root / reference["path"]
            self.assertEqual(
                reference["sha256"] + ".json",
                retained.name,
            )
            self.assertEqual(
                reference["sha256"],
                parity.sha256_file(retained),
            )

        with self.assertRaisesRegex(ValueError, "already exists"):
            parity.promote_evidence(
                manifest,
                "red",
                self.parity_id,
                legacy,
                xplat,
                self.registry_path,
                registry_sha256=self.registry_sha256,
                legacy_test_report_path=self.legacy_test_report_path,
                xplat_test_report_path=self.xplat_test_report_path,
                legacy_execution_path=self.legacy_execution_path,
                xplat_execution_path=self.xplat_execution_path,
                root=self.root,
            )

    def test_test_report_accepts_canonical_divergence_message(
        self,
    ) -> None:
        _, xplat, _ = self.make_live_artifacts()
        run = parity.load_json(xplat)

        parity.validate_test_report(
            self.xplat_test_report_path,
            run,
            "xplat",
            root=self.root,
        )

    def test_test_report_rejects_inexact_divergence_messages(
        self,
    ) -> None:
        _, xplat, _ = self.make_live_artifacts()
        run = parity.load_json(xplat)
        marker = (
            "PARITY_FUNCTIONAL_DIVERGENCE|"
            f"{self.parity_id}|behavior-diverged"
        )
        exact_message = (
            f"{parity.FUNCTIONAL_DIVERGENCE_TRX_EXCEPTION_TYPE} : "
            f"{marker}"
        )
        original = self.xplat_test_report_path.read_bytes()
        encoded_exact = exact_message.encode("utf-8")
        self.assertEqual(1, original.count(encoded_exact))
        candidates = (
            marker,
            f"ParityFunctionalDivergenceException : {marker}",
            f"System.Exception : {marker}",
            (
                "MorseRunner.LegacyParity."
                f"ParityFunctionalDivergenceException : {marker}"
            ),
            (
                f"{parity.FUNCTIONAL_DIVERGENCE_TRX_EXCEPTION_TYPE}:"
                f"{marker}"
            ),
            (
                f"{parity.FUNCTIONAL_DIVERGENCE_TRX_EXCEPTION_TYPE}  : "
                f"{marker}"
            ),
            f" {exact_message}",
            f"{exact_message} ",
            f"{exact_message}\n",
            f"prefix {exact_message}",
            f"{exact_message} suffix",
            exact_message.replace(
                self.parity_id,
                "wrong.case",
            ),
            exact_message.replace(
                "behavior-diverged",
                "wrong-code",
            ),
        )

        for candidate in candidates:
            with self.subTest(candidate=candidate):
                self.xplat_test_report_path.write_bytes(
                    original.replace(
                        encoded_exact,
                        candidate.encode("utf-8"),
                    )
                )
                with self.assertRaisesRegex(
                    ValueError,
                    "exact functional-divergence exception marker",
                ):
                    parity.validate_test_report(
                        self.xplat_test_report_path,
                        run,
                        "xplat",
                        root=self.root,
                    )

    def test_green_promotion_is_monotonic_by_platform(self) -> None:
        platforms = ["windows", "linux", "macos"]
        self.case["platforms"] = platforms
        self.evidence = self.make_red_evidence(self.parity_id)
        self.rewrite_evidence()
        legacy, red, provenance = self.make_live_artifacts()
        (self.root / self.evidence_relative).unlink()
        manifest = self.make_live_manifest()
        parity.promote_evidence(
            manifest,
            "red",
            self.parity_id,
            legacy,
            red,
            self.registry_path,
            registry_sha256=self.registry_sha256,
            legacy_test_report_path=self.legacy_test_report_path,
            xplat_test_report_path=self.xplat_test_report_path,
            legacy_execution_path=self.legacy_execution_path,
            xplat_execution_path=self.xplat_execution_path,
            root=self.root,
        )
        before = parity.load_json(self.root / self.evidence_relative)
        results, reports, executions = self.make_green_platform_maps(
            platforms
        )
        full_results, full_reports, full_executions = (
            self.make_full_suite_maps(platforms)
        )
        package_index, package_index_sha256 = (
            self.make_full_suite_package(
                full_results,
                full_reports,
                full_executions,
            )
        )

        parity.promote_evidence(
            manifest,
            "green",
            self.parity_id,
            green_results=results,
            green_test_reports=reports,
            green_executions=executions,
            registry_path=self.registry_path,
            registry_sha256=self.registry_sha256,
            full_suite_results=full_results,
            full_suite_test_reports=full_reports,
            full_suite_executions=full_executions,
            full_suite_package_index=package_index,
            full_suite_package_index_sha256=package_index_sha256,
            root=self.root,
        )

        after = parity.load_json(self.root / self.evidence_relative)
        self.assertEqual(
            before["runs"]["legacy"],
            after["runs"]["legacy"],
        )
        self.assertEqual(
            before["runs"]["xplatRed"],
            after["runs"]["xplatRed"],
        )
        self.assertIn("windows", after["runs"]["xplatGreen"])
        with self.assertRaisesRegex(ValueError, "requires red"):
            parity.promote_evidence(
                manifest,
                "green",
                self.parity_id,
                green_results=results,
                green_test_reports=reports,
                green_executions=executions,
                root=self.root,
            )

    def test_green_promotion_is_one_atomic_three_platform_batch(self) -> None:
        platforms = ["windows", "linux", "macos"]
        self.case["platforms"] = platforms
        self.evidence = self.make_red_evidence(self.parity_id)
        self.rewrite_evidence()
        manifest = self.make_live_manifest()
        results, reports, executions = self.make_green_platform_maps(
            platforms
        )
        full_results, full_reports, full_executions = (
            self.make_full_suite_maps(platforms)
        )
        package_index, package_index_sha256 = (
            self.make_full_suite_package(
                full_results,
                full_reports,
                full_executions,
            )
        )

        with patch.object(
            parity,
            "commit_file_transaction",
            wraps=parity.commit_file_transaction,
        ) as commit:
            promoted = parity.promote_evidence_batch(
                manifest,
                "green",
                [self.parity_id],
                green_results=results,
                green_test_reports=reports,
                green_executions=executions,
                registry_path=self.registry_path,
                registry_sha256=self.registry_sha256,
                full_suite_results=full_results,
                full_suite_test_reports=full_reports,
                full_suite_executions=full_executions,
                full_suite_package_index=package_index,
                full_suite_package_index_sha256=(
                    package_index_sha256
                ),
                root=self.root,
            )

        self.assertEqual(
            [self.root / self.evidence_relative],
            promoted,
        )
        self.assertEqual(1, commit.call_count)
        evidence = parity.load_json(self.root / self.evidence_relative)
        expected = set(platforms)
        self.assertEqual(
            expected,
            set(evidence["runs"]["xplatGreen"]),
        )
        self.assertEqual(
            expected,
            set(evidence["testReports"]["xplatGreen"]),
        )
        self.assertEqual(
            expected,
            set(evidence["executions"]["xplatGreen"]),
        )
        self.assertEqual("both-green", evidence["classification"])
        self.assertEqual("both-green", manifest["cases"][0]["status"])
        self.assertEqual(
            "f" * 40,
            manifest["cases"][0]["firstGreenCommit"],
        )

    def test_green_promotion_rejects_selected_full_suite_alias(
        self,
    ) -> None:
        platforms = ["windows", "linux", "macos"]
        self.case["platforms"] = platforms
        self.evidence = self.make_red_evidence(self.parity_id)
        self.rewrite_evidence()
        manifest = self.make_live_manifest()
        results, reports, executions = self.make_green_platform_maps(
            platforms
        )
        full_results, full_reports, full_executions = (
            self.make_full_suite_maps(platforms)
        )
        package_index, package_index_sha256 = (
            self.make_full_suite_package(
                full_results,
                full_reports,
                full_executions,
            )
        )
        full_reports["linux/XPlat"] = reports["linux"]

        with self.assertRaisesRegex(ValueError, "distinct artifact"):
            parity.promote_evidence_batch(
                manifest,
                "green",
                [self.parity_id],
                registry_path=self.registry_path,
                registry_sha256=self.registry_sha256,
                green_results=results,
                green_test_reports=reports,
                green_executions=executions,
                full_suite_results=full_results,
                full_suite_test_reports=full_reports,
                full_suite_executions=full_executions,
                full_suite_package_index=package_index,
                full_suite_package_index_sha256=(
                    package_index_sha256
                ),
                root=self.root,
            )

    def test_green_promotion_rejects_copied_full_suite_capture(
        self,
    ) -> None:
        platforms = ["windows", "linux", "macos"]
        self.case["platforms"] = platforms
        self.evidence = self.make_red_evidence(self.parity_id)
        self.rewrite_evidence()
        manifest = self.make_live_manifest()
        full_results, full_reports, full_executions = (
            self.make_full_suite_maps(platforms)
        )
        package_index, package_index_sha256 = (
            self.make_full_suite_package(
                full_results,
                full_reports,
                full_executions,
            )
        )
        copied_results: dict[str, Path] = {}
        copied_reports: dict[str, Path] = {}
        copied_executions: dict[str, Path] = {}
        for platform in platforms:
            full_key = f"{platform}/XPlat"
            copied_results[platform] = self.write_artifact_bytes(
                f"parity/copied-selected/{platform}.json",
                full_results[full_key].read_bytes(),
            )
            copied_reports[platform] = self.write_artifact_bytes(
                f"parity/copied-selected/{platform}.trx",
                full_reports[full_key].read_bytes(),
            )
            copied_executions[platform] = self.write_artifact_bytes(
                f"parity/copied-selected/{platform}-execution.json",
                full_executions[full_key].read_bytes(),
            )

        with self.assertRaisesRegex(
            ValueError,
            "distinct capture",
        ):
            parity.promote_evidence_batch(
                manifest,
                "green",
                [self.parity_id],
                registry_path=self.registry_path,
                registry_sha256=self.registry_sha256,
                green_results=copied_results,
                green_test_reports=copied_reports,
                green_executions=copied_executions,
                full_suite_results=full_results,
                full_suite_test_reports=full_reports,
                full_suite_executions=full_executions,
                full_suite_package_index=package_index,
                full_suite_package_index_sha256=(
                    package_index_sha256
                ),
                root=self.root,
            )

    def test_green_promotion_rejects_package_swap_after_validation(
        self,
    ) -> None:
        platforms = ["windows", "linux", "macos"]
        self.case["platforms"] = platforms
        self.evidence = self.make_red_evidence(self.parity_id)
        self.rewrite_evidence()
        manifest = self.make_live_manifest()
        selected_results, selected_reports, selected_executions = (
            self.make_green_platform_maps(platforms)
        )
        full_results, full_reports, full_executions = (
            self.make_full_suite_maps(platforms)
        )
        package_index, package_index_sha256 = (
            self.make_full_suite_package(
                full_results,
                full_reports,
                full_executions,
            )
        )
        original_validate = parity.validate_full_suite_package_index

        def validate_then_swap(*args: object, **kwargs: object) -> object:
            validated = original_validate(*args, **kwargs)
            changed = full_results["windows/XPlat"].read_bytes() + b"\n"
            full_results["windows/XPlat"].write_bytes(changed)
            return validated

        with (
            patch.object(
                parity,
                "validate_full_suite_package_index",
                side_effect=validate_then_swap,
            ),
            self.assertRaisesRegex(
                ValueError,
                "bytes changed after package-index validation",
            ),
        ):
            parity.promote_evidence_batch(
                manifest,
                "green",
                [self.parity_id],
                registry_path=self.registry_path,
                registry_sha256=self.registry_sha256,
                green_results=selected_results,
                green_test_reports=selected_reports,
                green_executions=selected_executions,
                full_suite_results=full_results,
                full_suite_test_reports=full_reports,
                full_suite_executions=full_executions,
                full_suite_package_index=package_index,
                full_suite_package_index_sha256=(
                    package_index_sha256
                ),
                root=self.root,
            )

    def test_powershell_package_producer_is_accepted_by_python(
        self,
    ) -> None:
        powershell = shutil.which("pwsh")
        self.assertIsNotNone(
            powershell,
            "PowerShell is required for the package interop contract",
        )
        assert powershell is not None

        nested_build_root = (
            self.root
            / "artifacts/legacy-oracle/builds/interop-v1/build-id/run"
        )
        nested_executable = (
            nested_build_root / "bin/LegacyOracle.exe"
        )
        nested_executable.parent.mkdir(parents=True)
        nested_executable.write_bytes(
            self.oracle_executable_path.read_bytes()
        )
        self.oracle_executable_path = nested_executable
        self.runtime_provenance_relative = (
            "artifacts/legacy-oracle/builds/interop-v1/"
            "build-id/run/LegacyOracle.provenance.json"
        )

        full_results, full_reports, full_executions = (
            self.make_full_suite_maps(["windows"])
        )
        content_addressed_registry = (
            self.root
            / "artifacts/legacy-oracle/registries"
            / f"{self.registry_sha256}.json"
        )
        content_addressed_registry.parent.mkdir(parents=True)
        content_addressed_registry.write_bytes(
            self.registry_path.read_bytes()
        )

        integration_report = self.write_artifact_bytes(
            "parity/test-results/"
            "legacy-oracle-build-integration.trx",
            self.make_legacy_build_integration_trx(),
        )
        integration_execution_raw = parity.serialize_lf_json(
            self.make_legacy_build_integration_execution(
                parity.sha256_file(integration_report),
                registry_sha256=self.registry_sha256,
            )
        )
        integration_execution_sha256 = hashlib.sha256(
            integration_execution_raw
        ).hexdigest()
        self.write_artifact_bytes(
            "parity/executions/legacy-oracle-build-integration/"
            f"{integration_execution_sha256}.json",
            integration_execution_raw,
        )

        package_relative = (
            "artifacts/parity-package-staging/python-interop"
        )
        producer = (
            parity.ROOT
            / "tests/parity/New-ParityBothArtifactPackage.ps1"
        )
        completed = subprocess.run(
            [
                powershell,
                "-NoLogo",
                "-NoProfile",
                "-File",
                str(producer),
                "-PackageRoot",
                package_relative,
                "-RegistryPath",
                str(content_addressed_registry),
                "-RegistrySha256",
                self.registry_sha256,
                "-RepositoryRoot",
                str(self.root),
            ],
            cwd=parity.ROOT,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
        self.assertEqual(
            0,
            completed.returncode,
            completed.stdout + completed.stderr,
        )

        produced_root = self.root / package_relative
        produced_indexes = list(
            (
                produced_root
                / "artifacts/parity-full-suite/"
                "windows-both-package-index"
            ).glob("*.json")
        )
        self.assertEqual(1, len(produced_indexes))
        produced_index = produced_indexes[0]
        index_sha256 = produced_index.stem
        index_document = parity.load_json(produced_index)
        produced_paths = [
            entry["path"] for entry in index_document["files"]
        ]
        self.assertEqual(
            sorted(produced_paths, key=parity.utf16_ordinal_key),
            produced_paths,
        )
        provenance_relative = self.runtime_provenance_relative
        executable_relative = (
            "artifacts/legacy-oracle/builds/interop-v1/"
            "build-id/run/bin/LegacyOracle.exe"
        )
        self.assertLess(
            produced_paths.index(provenance_relative),
            produced_paths.index(executable_relative),
        )

        imported_root = (
            self.root
            / "artifacts/parity-imports"
            / index_sha256
        )
        imported_root.parent.mkdir(parents=True)
        shutil.copytree(produced_root, imported_root)
        imported_index = (
            imported_root
            / "artifacts/parity-full-suite/"
            "windows-both-package-index"
            / f"{index_sha256}.json"
        )
        imported_registry = (
            imported_root
            / content_addressed_registry.relative_to(self.root)
        )
        imported_results = {
            "windows/Legacy": (
                imported_root / "artifacts/parity/legacy.json"
            ),
            "windows/XPlat": (
                imported_root / "artifacts/parity/xplat.json"
            ),
        }
        imported_reports = {
            "windows/Legacy": (
                imported_root
                / "artifacts/parity/test-results/legacy.trx"
            ),
            "windows/XPlat": (
                imported_root
                / "artifacts/parity/test-results/xplat.trx"
            ),
        }
        imported_executions = {
            "windows/Legacy": next(
                (
                    imported_root
                    / "artifacts/parity/executions/legacy"
                ).glob("*.json")
            ),
            "windows/XPlat": next(
                (
                    imported_root
                    / "artifacts/parity/executions/xplat"
                ).glob("*.json")
            ),
        }
        validated = parity.validate_full_suite_package_index(
            imported_index,
            index_sha256,
            imported_registry,
            self.registry_sha256,
            imported_results,
            imported_reports,
            imported_executions,
            root=self.root,
        )
        self.assertEqual(imported_root.resolve(), validated[0])

    def test_execution_envelope_binds_process_outcome_and_artifacts(
        self,
    ) -> None:
        _, xplat, _ = self.make_live_artifacts()
        document = parity.load_json(xplat)
        report_hash = parity.sha256_file(self.xplat_test_report_path)
        _, raw, digest = parity.validate_execution_envelope(
            self.xplat_execution_path,
            document,
            parity.sha256_file(xplat),
            report_hash,
            "xplat",
            root=self.root,
        )
        self.assertEqual(
            parity.sha256_file(self.xplat_execution_path),
            digest,
        )
        self.assertEqual(self.xplat_execution_path.read_bytes(), raw)

        mutations = (
            ("resultSha256", "0" * 64, "resultSha256"),
            ("testReportSha256", "1" * 64, "testReportSha256"),
            ("testProcessExitCode", 0, "exit code"),
            ("schemaVersion", True, "signed integer"),
        )
        original = parity.load_json(self.xplat_execution_path)
        for field, value, message in mutations:
            with self.subTest(field=field):
                changed = copy.deepcopy(original)
                changed[field] = value
                path = self.write_artifact_document(
                    f"parity/envelope-{field}.json",
                    changed,
                )
                with self.assertRaisesRegex(ValueError, message):
                    parity.validate_execution_envelope(
                        path,
                        document,
                        parity.sha256_file(xplat),
                        report_hash,
                        "xplat",
                        root=self.root,
                    )

        for field, value in (
            ("completed", False),
            ("correlationValidated", False),
            ("exitCode", False),
        ):
            with self.subTest(wrapper=field):
                changed = copy.deepcopy(original)
                changed["wrapper"][field] = value
                path = self.write_artifact_document(
                    f"parity/envelope-wrapper-{field}.json",
                    changed,
                )
                with self.assertRaisesRegex(
                    ValueError,
                    "signed integer"
                    if field == "exitCode"
                    else "did not complete",
                ):
                    parity.validate_execution_envelope(
                        path,
                        document,
                        parity.sha256_file(xplat),
                        report_hash,
                        "xplat",
                        root=self.root,
                    )

    def test_live_result_numeric_fields_reject_booleans(self) -> None:
        _, xplat, _ = self.make_live_artifacts()
        original = parity.load_json(xplat)
        for field in (
            "observedValueCount",
            "executionCount",
        ):
            with self.subTest(field=field):
                changed = copy.deepcopy(original)
                changed["results"][0][field] = True
                path = self.write_artifact_document(
                    f"parity/result-bool-{field}.json",
                    changed,
                )
                with self.assertRaisesRegex(
                    ValueError,
                    field,
                ):
                    parity.validate_live_run_document_for_manifest(
                        self.make_live_manifest(),
                        parity.load_json(path),
                        "xplat",
                        root=self.root,
                    )

    def test_full_suite_gate_rejects_an_unselected_green_regression(
        self,
    ) -> None:
        second_fixture = copy.deepcopy(self.fixture)
        second_fixture["parityId"] = "case.two"
        second_fixture_relative = (
            "tests/parity/fixtures/legacy/case-two.json"
        )
        self.write_json(second_fixture_relative, second_fixture)
        second_case = copy.deepcopy(self.case)
        second_case.update(
            {
                "id": "case.two",
                "fixture": second_fixture_relative,
                "evidence": (
                    "tests/parity/evidence/case-two.baseline.json"
                ),
                "status": "both-green",
                "xplatTestStatus": "pass",
                "failureCode": None,
                "firstGreenCommit": "f" * 40,
            }
        )
        full_run = self.make_run_document(
            "xplat",
            self.fixture["values"],
            "passed",
            None,
        )
        selected_result = full_run["results"][0]
        second_result = copy.deepcopy(selected_result)
        second_result.update(
            {
                "parityId": "case.two",
                "acceptanceTestName": "parity:case.two()",
                "caseDefinitionSha256": (
                    parity.case_definition_sha256(second_case)
                ),
                "fixtureSha256": parity.sha256_file(
                    self.root / second_fixture_relative
                ),
            }
        )
        full_run["expectedParityIds"] = [
            "case.two",
            self.parity_id,
        ]
        full_run["results"] = [
            second_result,
            selected_result,
        ]
        manifest = {
            "reference": {"revision": self.reference_revision},
            "cases": [self.case, second_case],
        }
        parity.validate_live_run_document_for_manifest(
            manifest,
            full_run,
            "xplat",
            outcome_overrides={self.parity_id: "passed"},
            root=self.root,
        )

        regressed = copy.deepcopy(full_run)
        regressed_result = regressed["results"][0]
        regressed_result.update(
            {
                "outcome": "functional-divergence",
                "failureCode": "behavior-diverged",
                "observedValues": ["regressed"],
                "observedValueCount": 1,
                "observedValuesSha256": (
                    parity.canonical_json_sha256(["regressed"])
                ),
                "firstDivergence": {
                    "index": 0,
                    "expected": "legacy-value",
                    "actual": "regressed",
                },
            }
        )
        with self.assertRaisesRegex(ValueError, "outcome is not passed"):
            parity.validate_live_run_document_for_manifest(
                manifest,
                regressed,
                "xplat",
                outcome_overrides={self.parity_id: "passed"},
                root=self.root,
            )

    def test_promotion_rejects_crlf_recorder_document(self) -> None:
        legacy, xplat, provenance = self.make_live_artifacts()
        xplat.write_bytes(
            xplat.read_bytes().replace(b"\n", b"\r\n")
        )
        (self.root / self.evidence_relative).unlink()

        with self.assertRaisesRegex(ValueError, "LF-only"):
            parity.promote_evidence(
                self.make_live_manifest(),
                "red",
                self.parity_id,
                legacy,
                xplat,
                self.registry_path,
                registry_sha256=self.registry_sha256,
                legacy_test_report_path=self.legacy_test_report_path,
                xplat_test_report_path=self.xplat_test_report_path,
                legacy_execution_path=self.legacy_execution_path,
                xplat_execution_path=self.xplat_execution_path,
                root=self.root,
            )

    def test_both_green_requires_run_bound_first_green_commit(self) -> None:
        self.case["platforms"] = ["windows", "linux", "macos"]
        self.case.update(
            {
                "status": "both-green",
                "xplatTestStatus": "pass",
                "failureCode": None,
                "firstGreenCommit": self.first_green_commit,
            }
        )
        self.evidence = self.make_green_evidence()
        self.rewrite_evidence()
        def successful_git(
            root: Path,
            arguments: list[str],
            label: str,
        ) -> subprocess.CompletedProcess[str]:
            stdout = (
                self.first_green_tree + "\n"
                if arguments[:1] == ["rev-parse"]
                else ""
            )
            return subprocess.CompletedProcess(
                ["git", *arguments],
                0,
                stdout=stdout,
            )

        with patch.object(parity, "run_git", side_effect=successful_git):
            self.validate_current_case()

    def test_retained_full_suite_gate_detects_tamper_and_orphans(
        self,
    ) -> None:
        self.case["platforms"] = ["windows", "linux", "macos"]
        self.case.update(
            {
                "status": "both-green",
                "xplatTestStatus": "pass",
                "failureCode": None,
                "firstGreenCommit": self.first_green_commit,
            }
        )
        self.evidence = self.make_green_evidence()
        self.rewrite_evidence()
        gate = self.referenced_document(
            self.evidence["regressionGate"]
        )
        legacy_report = (
            self.root
            / gate["testReports"]["windows/Legacy"]["path"]
        )
        legacy_report.write_bytes(
            legacy_report.read_bytes() + b"\n"
        )
        with self.assertRaisesRegex(ValueError, "SHA-256"):
            self.validate_current_case()

        self.evidence = self.make_green_evidence()
        self.rewrite_evidence()
        self.write_content_document(
            "regression-gates",
            {"schemaVersion": 1, "orphan": True},
        )
        with self.assertRaisesRegex(ValueError, "orphaned"):
            parity.validate_no_orphaned_regression_gates(
                [self.evidence],
                root=self.root,
            )

    def test_first_green_revision_tree_and_result_hash_are_bound(self) -> None:
        self.case["platforms"] = ["windows", "linux", "macos"]
        self.case.update(
            {
                "status": "both-green",
                "xplatTestStatus": "pass",
                "failureCode": None,
                "firstGreenCommit": self.first_green_commit,
            }
        )
        changes = (
            ("revision", "9" * 40, "green XPlat run revision"),
            (
                "caseDefinitionSha256",
                "9" * 64,
                "caseDefinitionSha256",
            ),
        )
        for field, value, message in changes:
            with self.subTest(field=field):
                self.evidence = self.make_green_evidence()
                if field == "revision":
                    self.replace_run(
                        "xplatGreen",
                        lambda document: document["runContext"][
                            "xplat"
                        ].update({field: value}),
                        platform="linux",
                    )
                else:
                    self.replace_run(
                        "xplatGreen",
                        lambda document: document["results"][0].update(
                            {field: value}
                        ),
                        platform="linux",
                    )
                with self.assertRaisesRegex(ValueError, message):
                    self.validate_current_case()

        self.evidence = self.make_green_evidence()
        self.rewrite_evidence()
        completed = [
            subprocess.CompletedProcess(["git"], 0),
            subprocess.CompletedProcess(["git"], 0),
            subprocess.CompletedProcess(
                ["git"],
                0,
                stdout="9" * 40 + "\n",
            ),
        ]
        with patch.object(parity.subprocess, "run", side_effect=completed):
            with self.assertRaisesRegex(ValueError, "tree does not match"):
                self.validate_current_case()

    def test_first_green_commit_must_be_reachable(self) -> None:
        completed = [
            subprocess.CompletedProcess(["git"], 0),
            subprocess.CompletedProcess(["git"], 1),
        ]
        with patch.object(parity.subprocess, "run", side_effect=completed):
            with self.assertRaisesRegex(ValueError, "not reachable"):
                parity.validate_first_green_commit(
                    self.first_green_commit,
                    self.parity_id,
                    root=self.root,
                )

    def test_retained_red_revision_must_exist_be_reachable_and_match_tree(
        self,
    ) -> None:
        revision = "a" * 40
        tree = "b" * 40
        scenarios = (
            (
                [
                    subprocess.CompletedProcess(
                        ["git"],
                        1,
                        stdout="",
                    )
                ],
                "does not exist",
            ),
            (
                [
                    subprocess.CompletedProcess(
                        ["git"],
                        0,
                        stdout=revision + "\n",
                    ),
                    subprocess.CompletedProcess(["git"], 1),
                ],
                "not reachable",
            ),
            (
                [
                    subprocess.CompletedProcess(
                        ["git"],
                        0,
                        stdout=revision + "\n",
                    ),
                    subprocess.CompletedProcess(["git"], 0),
                    subprocess.CompletedProcess(
                        ["git"],
                        0,
                        stdout="c" * 40 + "\n",
                    ),
                ],
                "tree does not match",
            ),
        )
        for completed, message in scenarios:
            with self.subTest(message=message):
                with patch.object(
                    parity,
                    "run_git",
                    side_effect=completed,
                ):
                    with self.assertRaisesRegex(ValueError, message):
                        self.original_validate_retained_xplat_revision(
                            revision,
                            tree,
                            "test red run",
                            root=self.root,
                        )

        completed = [
            subprocess.CompletedProcess(
                ["git"],
                0,
                stdout=revision + "\n",
            ),
            subprocess.CompletedProcess(["git"], 0),
            subprocess.CompletedProcess(
                ["git"],
                0,
                stdout=tree + "\n",
            ),
        ]
        with patch.object(parity, "run_git", side_effect=completed):
            self.original_validate_retained_xplat_revision(
                revision,
                tree,
                "test red run",
                root=self.root,
            )

    def test_red_revision_must_strictly_precede_every_green_revision(
        self,
    ) -> None:
        revision = "a" * 40
        with self.assertRaisesRegex(ValueError, "strict ancestor"):
            self.original_validate_strict_revision_ancestry(
                revision,
                revision,
                "test transition",
                root=self.root,
            )

        for relation in ("later", "unrelated"):
            with self.subTest(relation=relation):
                with patch.object(
                    parity,
                    "run_git",
                    return_value=subprocess.CompletedProcess(
                        ["git"],
                        1,
                    ),
                ):
                    with self.assertRaisesRegex(
                        ValueError,
                        "strict ancestor",
                    ):
                        self.original_validate_strict_revision_ancestry(
                            revision,
                            "b" * 40,
                            f"test {relation} transition",
                            root=self.root,
                        )

        with patch.object(
            parity,
            "run_git",
            return_value=subprocess.CompletedProcess(["git"], 0),
        ):
            self.original_validate_strict_revision_ancestry(
                revision,
                "b" * 40,
                "test valid transition",
                root=self.root,
            )

    def test_capability_acceptance_status_matches_case_authorship(self) -> None:
        no_case_statuses = ("partial", "complete")
        for status in no_case_statuses:
            with self.subTest(status=status):
                capability = self.make_capability(
                    status=status,
                    case_ids=[],
                )
                with self.assertRaisesRegex(ValueError, "has no cases"):
                    parity.validate_capability_case_links([capability], [])

        capability = self.make_capability(
            status="not-authored",
            case_ids=[self.parity_id],
        )
        with self.assertRaisesRegex(ValueError, "has case IDs"):
            parity.validate_capability_case_links(
                [capability],
                [self.case],
            )

    def test_case_must_link_exactly_one_behavioral_obligation(self) -> None:
        for obligation_ids in (
            [],
            [self.obligation_id, "test.other-obligation"],
        ):
            with self.subTest(obligation_ids=obligation_ids):
                case = copy.deepcopy(self.case)
                case["obligationIds"] = obligation_ids
                with self.assertRaisesRegex(
                    ValueError,
                    "exactly one behavioral obligation",
                ):
                    parity.validate_case_shape(case)

    def test_get_audio_case_cannot_claim_all_audio_obligations(self) -> None:
        get_audio_case = copy.deepcopy(self.case)
        get_audio_case["id"] = "audio.get-audio"
        get_audio_case["behavior"] = (
            "GetAudio returns one deterministic integrated render block."
        )
        get_audio_case["obligationIds"] = [
            "audio.operator-sidetone-pipeline",
            "audio.qsb-independent-per-station",
            "audio.qrm-interfering-cw-stations",
            "audio.qrn-impulses-and-burst-stations",
        ]

        with self.assertRaisesRegex(
            ValueError,
            "exactly one behavioral obligation",
        ):
            parity.validate_case_shape(get_audio_case)

    def test_case_definition_hash_has_cross_language_numeric_domain(
        self,
    ) -> None:
        case = copy.deepcopy(self.case)
        case["input"] = {
            "unicode": "Morse Ω 日本 🎧",
            "boolean": True,
            "nothing": None,
            "bounds": [
                -9223372036854775808,
                9223372036854775807,
            ],
            "nested": {
                "fractionalAsString": "-0.125e+2",
            },
        }

        self.assertEqual(
            "733d1476305226092c6626382e40c422"
            "81ec6b6ace3d2f3fe7186bb1623ad4cb",
            parity.case_definition_sha256(case),
        )

        for invalid in (
            1.5,
            1e3,
            -9223372036854775809,
            9223372036854775808,
        ):
            with self.subTest(invalid=invalid):
                changed = copy.deepcopy(case)
                changed["input"]["nested"]["invalid"] = invalid
                with self.assertRaisesRegex(
                    ValueError,
                    "signed 64-bit",
                ):
                    parity.case_definition_sha256(changed)

    def test_canonical_json_uses_utf16_ordinal_keys_and_exact_escaping(
        self,
    ) -> None:
        value = {
            "outer": {
                "\U00010000": "astral-key-first",
                "\ue000": "bmp-private-key-second",
                "nested": {
                    "escapes": (
                        'quote:" backslash:\\ slash:/ '
                        "controls:\b\f\t\r\n"
                    ),
                    "separators": "\u2028\u2029",
                    "unicode": "Ω 日本 🎧 e\u0301 é",
                    "array": [
                        True,
                        False,
                        None,
                        -9223372036854775808,
                        9223372036854775807,
                    ],
                },
            },
        }
        expected = (
            '{"outer":{"nested":{"array":[true,false,null,'
            "-9223372036854775808,9223372036854775807],"
            '"escapes":"quote:\\" backslash:\\\\ slash:/ '
            'controls:\\b\\f\\t\\r\\n","separators":"\u2028\u2029",'
            '"unicode":"Ω 日本 🎧 e\u0301 é"},'
            '"\U00010000":"astral-key-first",'
            '"\ue000":"bmp-private-key-second"}}'
        ).encode("utf-8")

        self.assertEqual(expected, parity.canonical_json_bytes(value))
        self.assertEqual(
            "8a3c187ebd3846533be418f811bb87a34"
            "a45ec4dba008c7f8e1db7c299a04d33",
            parity.canonical_json_sha256(value),
        )
        self.assertLess(
            expected.index("\U00010000".encode("utf-8")),
            expected.index("\ue000".encode("utf-8")),
        )

        for invalid in (
            {"\ud800": "key"},
            {"value": "\udfff"},
        ):
            with self.subTest(invalid=repr(invalid)):
                with self.assertRaisesRegex(
                    ValueError,
                    "unpaired Unicode surrogate",
                ):
                    parity.canonical_json_sha256(invalid)

        for invalid_number in (
            -9223372036854775809,
            9223372036854775808,
            0.5,
            1e20,
        ):
            with self.subTest(invalid_number=invalid_number):
                with self.assertRaisesRegex(
                    ValueError,
                    "signed 64-bit",
                ):
                    parity.canonical_json_bytes(
                        {"number": invalid_number}
                    )

    def test_complete_capability_requires_every_obligation_green(self) -> None:
        capability = self.make_capability(status="complete")
        capability["platforms"] = ["windows"]
        case = copy.deepcopy(self.case)
        case.update(
            {
                "status": "both-green",
                "xplatTestStatus": "pass",
                "failureCode": None,
                "firstGreenCommit": self.first_green_commit,
            }
        )
        covered = self.make_obligation(status="complete")
        uncovered = {
            "id": "test.uncovered-obligation",
            "capabilityId": self.capability_id,
            "behavior": "A separate required behavior remains untested.",
            "platforms": ["windows"],
            "sourceBindingStatus": "pending",
            "legacySources": [],
            "legacySurfaceSelectors": [],
            "acceptanceStatus": "not-authored",
            "caseIds": [],
        }

        with self.assertRaisesRegex(
            ValueError,
            "incomplete behavioral obligations",
        ):
            parity.validate_capability_case_links(
                [capability],
                [case],
                obligations=[covered, uncovered],
                inventory_surface_ids=["legacy.surface.one"],
            )

    def test_rich_evidence_obligation_retains_green_case_as_partial(
        self,
    ) -> None:
        capability = self.make_capability(status="partial")
        capability["platforms"] = ["windows"]
        protected_id = "audio.physical-device-lifecycle"
        case = copy.deepcopy(self.case)
        case.update(
            {
                "obligationIds": [protected_id],
                "status": "both-green",
                "xplatTestStatus": "pass",
                "failureCode": None,
                "firstGreenCommit": self.first_green_commit,
            }
        )
        obligation = {
            "id": protected_id,
            "capabilityId": self.capability_id,
            "behavior": "A real physical device lifecycle is proven.",
            "platforms": ["windows"],
            "sourceBindingStatus": "bound",
            "legacySources": [self.oracle_relative],
            "legacySurfaceSelectors": ["legacy.surface.one"],
            "acceptanceStatus": "partial",
            "caseIds": [self.parity_id],
        }

        parity.validate_capability_case_links(
            [capability],
            [case],
            obligations=[obligation],
            inventory_surface_ids=["legacy.surface.one"],
        )
        self.assertEqual(
            [protected_id],
            parity.derive_rich_evidence_blockers(
                [obligation],
                [case],
            ),
        )

        obligation["acceptanceStatus"] = "complete"
        with self.assertRaisesRegex(ValueError, "must be partial"):
            parity.validate_capability_case_links(
                [capability],
                [case],
                obligations=[obligation],
                inventory_surface_ids=["legacy.surface.one"],
            )

    def test_portable_obligations_and_cases_require_all_platforms(
        self,
    ) -> None:
        capability = self.make_capability()
        windows_only = self.make_obligation(platforms=["windows"])
        with self.assertRaisesRegex(
            ValueError,
            "full portable platform contract",
        ):
            parity.validate_obligation_shape(
                windows_only,
                {self.capability_id: capability},
            )

        obligation = self.make_obligation(
            platforms=["windows", "linux", "macos"],
        )
        with self.assertRaisesRegex(
            ValueError,
            "full platform contract",
        ):
            parity.validate_capability_case_links(
                [capability],
                [self.case],
                obligations=[obligation],
            )

    def test_partial_case_must_be_linked_to_owning_capability(self) -> None:
        wrong = copy.deepcopy(self.case)
        wrong["capabilityId"] = "other.capability"

        with self.assertRaisesRegex(ValueError, "does not match its owner"):
            parity.validate_capability_case_links(
                [self.make_capability()],
                [wrong],
            )

    def test_orphan_case_is_rejected(self) -> None:
        capability = self.make_capability(
            status="not-authored",
            case_ids=[],
        )
        with self.assertRaisesRegex(ValueError, "orphan cases"):
            parity.validate_capability_case_links(
                [capability],
                [self.case],
            )

    def test_case_platforms_must_be_within_capability(self) -> None:
        case = copy.deepcopy(self.case)
        case["platforms"] = ["windows", "linux"]
        capability = self.make_capability()
        capability["platforms"] = ["macos"]

        with self.assertRaisesRegex(ValueError, "platforms exceed"):
            parity.validate_capability_case_links(
                [capability],
                [case],
            )

    def test_case_selector_must_match_an_inventory_surface(self) -> None:
        case = copy.deepcopy(self.case)
        case["legacySurfaceSelectors"] = ["legacy.missing.*"]

        with self.assertRaisesRegex(
            ValueError,
            "matches no legacy surfaces",
        ):
            parity.resolve_case_surfaces(
                case,
                ["legacy.surface.one", "legacy.other.one"],
            )

    def test_case_may_span_capability_surface_silos(self) -> None:
        case = copy.deepcopy(self.case)
        case["legacySources"] = [
            self.oracle_relative,
            "Other.pas",
        ]
        case["legacySurfaceSelectors"] = [
            "legacy.surface.one",
            "legacy.other.one",
        ]
        obligation = self.make_obligation()
        obligation["legacySources"] = copy.deepcopy(case["legacySources"])
        obligation["legacySurfaceSelectors"] = copy.deepcopy(
            case["legacySurfaceSelectors"]
        )
        other_capability = {
            "id": "other.capability",
            "category": "test",
            "feature": "Other capability",
            "behavior": "Owns the second legacy surface silo.",
            "legacySources": ["Other.pas"],
            "legacySurfaceSelectors": ["legacy.other.*"],
            "platforms": ["windows", "linux", "macos"],
            "acceptanceStatus": "not-authored",
            "caseIds": [],
        }
        other_obligation = {
            "id": "other.obligation",
            "capabilityId": "other.capability",
            "behavior": "A future independent obligation.",
            "platforms": ["windows", "linux", "macos"],
            "sourceBindingStatus": "pending",
            "legacySources": [],
            "legacySurfaceSelectors": [],
            "acceptanceStatus": "not-authored",
            "caseIds": [],
        }

        parity.validate_capability_case_links(
            [self.make_capability(), other_capability],
            [case],
            obligations=[obligation, other_obligation],
            inventory_surface_ids=[
                "legacy.surface.one",
                "legacy.other.one",
            ],
        )

    def test_complete_requires_full_green_surface_coverage(self) -> None:
        capability = self.make_capability(status="complete")
        case = copy.deepcopy(self.case)
        case.update(
            {
                "status": "both-green",
                "xplatTestStatus": "pass",
                "failureCode": None,
                "firstGreenCommit": self.first_green_commit,
            }
        )
        with self.assertRaisesRegex(
            ValueError,
            "uncovered legacy surface/platform pairs",
        ):
            parity.validate_capability_case_links(
                [capability],
                [case],
                inventory_surface_ids=[
                    "legacy.surface.one",
                    "legacy.surface.two",
                ],
            )

    def test_complete_rejects_red_case(self) -> None:
        with self.assertRaisesRegex(ValueError, "non-green case"):
            parity.validate_capability_case_links(
                [self.make_capability(status="complete")],
                [self.case],
                inventory_surface_ids=["legacy.surface.one"],
            )

    def test_fully_green_covered_capability_must_be_complete(self) -> None:
        capability = self.make_capability(
            status="partial",
        )
        capability["platforms"] = ["windows"]
        case = copy.deepcopy(self.case)
        case.update(
            {
                "status": "both-green",
                "xplatTestStatus": "pass",
                "failureCode": None,
                "firstGreenCommit": self.first_green_commit,
            }
        )

        with self.assertRaisesRegex(ValueError, "must be complete"):
            parity.validate_capability_case_links(
                [capability],
                [case],
                inventory_surface_ids=["legacy.surface.one"],
            )

    def test_case_surface_ownership_may_overlap(self) -> None:
        second = copy.deepcopy(self.case)
        second["id"] = "test.case.two"
        capability = self.make_capability(
            status="partial",
            case_ids=[self.parity_id, second["id"]],
        )
        parity.validate_capability_case_links(
            [capability],
            [self.case, second],
            inventory_surface_ids=["legacy.surface.one"],
        )

    def test_unmarked_orphan_evidence_is_rejected(self) -> None:
        with self.assertRaisesRegex(
            ValueError,
            "explicitly marked legacy-v1",
        ):
            parity.validate_evidence_directory(
                {},
                {},
                set(),
                self.reference_revision,
                root=self.root,
            )

    def test_retained_evidence_may_share_not_authored_or_partial_capability(
        self,
    ) -> None:
        for status in ("not-authored", "partial"):
            with self.subTest(status=status):
                self.clear_evidence()
                retained = self.make_retained_evidence(self.capability_id)
                self.write_json(
                    "tests/parity/evidence/retained.json",
                    retained,
                )
                result = parity.validate_evidence_directory(
                    {},
                    {
                        self.capability_id: {
                            "acceptanceStatus": status,
                        }
                    },
                    set(),
                    self.reference_revision,
                    root=self.root,
                )
                self.assertEqual(1, len(result))

    def test_retained_evidence_cannot_share_complete_or_active_case_id(
        self,
    ) -> None:
        for capability_status, active_ids, message in (
            ("complete", set(), "complete capability ID"),
            ("not-authored", {self.capability_id}, "reuses parity ID"),
        ):
            with self.subTest(message=message):
                self.clear_evidence()
                retained = self.make_retained_evidence(self.capability_id)
                self.write_json(
                    "tests/parity/evidence/retained.json",
                    retained,
                )
                with self.assertRaisesRegex(ValueError, message):
                    parity.validate_evidence_directory(
                        {},
                        {
                            self.capability_id: {
                                "acceptanceStatus": capability_status,
                            }
                        },
                        active_ids,
                        self.reference_revision,
                        root=self.root,
                    )

    def test_duplicate_retained_evidence_ids_are_rejected(self) -> None:
        self.clear_evidence()
        retained = self.make_retained_evidence("retained.case")
        self.write_json("tests/parity/evidence/one.json", retained)
        self.write_json("tests/parity/evidence/two.json", retained)

        with self.assertRaisesRegex(ValueError, "reuses parity ID"):
            parity.validate_evidence_directory(
                {},
                {},
                set(),
                self.reference_revision,
                root=self.root,
            )

    def test_capability_and_case_counts_are_derived(self) -> None:
        capability_counts = parity.derive_capability_status_counts(
            [
                {"acceptanceStatus": "not-authored"},
                {"acceptanceStatus": "partial"},
                {"acceptanceStatus": "complete"},
            ]
        )
        case_counts = parity.derive_case_status_counts(
            [
                {"status": "both-green"},
                {"status": "legacy-green-xplat-red"},
            ]
        )

        self.assertEqual(1, capability_counts["not-authored"])
        self.assertEqual(1, capability_counts["partial"])
        self.assertEqual(1, capability_counts["complete"])
        self.assertEqual(1, case_counts["both-green"])
        self.assertEqual(1, case_counts["legacy-green-xplat-red"])


class FileTransactionTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary_directory.cleanup)
        self.root = Path(self.temporary_directory.name)

    def test_immutable_transaction_create_is_no_overwrite(self) -> None:
        target = self.root / "immutable.json"
        parity.commit_file_transaction(
            {target: (b"certified\n", False)}
        )
        self.assertEqual(b"certified\n", target.read_bytes())

        with self.assertRaisesRegex(ValueError, "immutable"):
            parity.commit_file_transaction(
                {target: (b"different\n", False)}
            )
        self.assertEqual(b"certified\n", target.read_bytes())

    def test_immutable_race_never_overwrites_or_deletes_intruder(self) -> None:
        target = self.root / "immutable.json"
        original_link = os.link
        intruder = b"concurrent owner\n"

        def race(source: Path, destination: Path) -> None:
            destination_path = Path(destination)
            if destination_path == target:
                target.write_bytes(intruder)
            original_link(source, destination)

        with patch.object(parity.os, "link", side_effect=race):
            with self.assertRaisesRegex(ValueError, "appeared during"):
                parity.commit_file_transaction(
                    {target: (b"candidate\n", False)}
                )

        self.assertEqual(intruder, target.read_bytes())

    def test_failure_mid_transaction_restores_every_owned_byte(self) -> None:
        first = self.root / "first.json"
        mutable = self.root / "manifest.json"
        last = self.root / "last.json"
        mutable.write_bytes(b"before\n")
        before = {
            path: path.read_bytes() if path.exists() else None
            for path in (first, mutable, last)
        }
        original_link = os.link

        def fail_last(source: Path, destination: Path) -> None:
            if Path(destination) == last:
                raise OSError("injected post-validation write failure")
            original_link(source, destination)

        changes = {
            first: (b"first\n", False),
            mutable: (b"after\n", True),
            last: (b"last\n", False),
        }
        with patch.object(parity.os, "link", side_effect=fail_last):
            with self.assertRaisesRegex(OSError, "injected"):
                parity.commit_file_transaction(changes)

        after = {
            path: path.read_bytes() if path.exists() else None
            for path in (first, mutable, last)
        }
        self.assertEqual(before, after)

    def test_repository_promotion_lock_is_cross_process(self) -> None:
        script = (
            "import sys\n"
            "from pathlib import Path\n"
            "sys.path.insert(0, sys.argv[2])\n"
            "import validate_parity as parity\n"
            "with parity.repository_promotion_lock("
            "Path(sys.argv[1]), timeout_seconds=0.2):\n"
            "    print('acquired')\n"
        )
        tooling = str(parity.ROOT / "tools/parity")
        with parity.repository_promotion_lock(self.root):
            blocked = subprocess.run(
                [
                    sys.executable,
                    "-c",
                    script,
                    str(self.root),
                    tooling,
                ],
                capture_output=True,
                text=True,
                timeout=10,
                check=False,
            )
        self.assertNotEqual(0, blocked.returncode)
        self.assertIn(
            "timed out waiting for the repository parity promotion lock",
            blocked.stderr,
        )

        acquired = subprocess.run(
            [
                sys.executable,
                "-c",
                script,
                str(self.root),
                tooling,
            ],
            capture_output=True,
            text=True,
            timeout=10,
            check=False,
        )
        self.assertEqual(0, acquired.returncode, acquired.stderr)
        self.assertEqual("acquired", acquired.stdout.strip())

    def test_waiting_promotion_rejects_a_stale_manifest_snapshot(
        self,
    ) -> None:
        manifest_path = (
            self.root / "tests/parity/parity-manifest.json"
        )
        manifest_path.parent.mkdir(parents=True)
        manifest_path.write_text(
            '{"cases":[{"id":"case.a"}]}\n',
            encoding="utf-8",
            newline="\n",
        )
        ready_path = self.root / "child-ready"
        script = (
            "import sys\n"
            "from pathlib import Path\n"
            "sys.path.insert(0, sys.argv[3])\n"
            "import validate_parity as parity\n"
            "root = Path(sys.argv[1])\n"
            "snapshot = parity.load_json("
            "root / 'tests/parity/parity-manifest.json')\n"
            "Path(sys.argv[2]).write_text('ready', encoding='utf-8')\n"
            "parity.promote_evidence_batch("
            "snapshot, 'red', ['case.a'], root=root)\n"
        )
        with parity.repository_promotion_lock(self.root):
            process = subprocess.Popen(
                [
                    sys.executable,
                    "-c",
                    script,
                    str(self.root),
                    str(ready_path),
                    str(parity.ROOT / "tools/parity"),
                ],
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
            )
            deadline = time.monotonic() + 5
            while not ready_path.is_file():
                if process.poll() is not None:
                    stdout, stderr = process.communicate()
                    self.fail(
                        "waiting promotion exited before lock contention: "
                        f"{stdout}\n{stderr}"
                    )
                if time.monotonic() >= deadline:
                    process.kill()
                    process.communicate()
                    self.fail("waiting promotion did not reach the lock")
                time.sleep(0.02)
            manifest_path.write_text(
                '{"cases":[{"id":"case.a"},{"id":"case.b"}]}\n',
                encoding="utf-8",
                newline="\n",
            )
        stdout, stderr = process.communicate(timeout=10)
        self.assertNotEqual(0, process.returncode, stdout)
        self.assertIn(
            "manifest changed while this promotion waited",
            stderr,
        )


class ManifestHistoryTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary_directory.cleanup)
        temporary_root = Path(self.temporary_directory.name)
        self.base_root = temporary_root / "base"
        self.current_root = temporary_root / "current"
        self.fixture_path = "tests/parity/fixtures/legacy/case.json"
        self.evidence_path = "tests/parity/evidence/case.json"
        self.reference = {
            "repository": "https://example.invalid/legacy.git",
            "revision": "a" * 40,
            "tree": "b" * 40,
            "bundle": "tests/parity/reference.bundle",
            "bundleSha256": "c" * 64,
            "definition": "tests/parity/legacy-reference.json",
            "definitionSha256": "d" * 64,
        }
        self.write_document(
            self.base_root,
            self.fixture_path,
            {"values": ["expected"]},
        )
        self.write_document(
            self.current_root,
            self.fixture_path,
            {"values": ["expected"]},
        )
        self.base_evidence = {
            "schemaVersion": 2,
            "parityId": "case.one",
            "referenceRevision": "a" * 40,
            "capturedAtUtc": "2026-07-18T00:00:00Z",
            "fixture": {
                "path": self.fixture_path,
                "sha256": "1" * 64,
                "observedValuesSha256": "2" * 64,
            },
            "legacyProvenance": {
                "path": "tests/parity/evidence/provenance/" + "3" * 64
                + ".json",
                "sha256": "3" * 64,
            },
            "runs": {
                "legacy": {
                    "path": "tests/parity/evidence/runs/" + "4" * 64
                    + ".json",
                    "sha256": "4" * 64,
                },
                "xplatRed": {
                    "path": "tests/parity/evidence/runs/" + "5" * 64
                    + ".json",
                    "sha256": "5" * 64,
                },
                "xplatGreen": {
                    "windows": {
                        "path": "tests/parity/evidence/runs/" + "6" * 64
                        + ".json",
                        "sha256": "6" * 64,
                    }
                },
            },
            "testReports": {
                "legacy": {
                    "path": "tests/parity/evidence/test-reports/"
                    + "7" * 64
                    + ".trx",
                    "sha256": "7" * 64,
                },
                "xplatRed": {
                    "path": "tests/parity/evidence/test-reports/"
                    + "8" * 64
                    + ".trx",
                    "sha256": "8" * 64,
                },
                "xplatGreen": {
                    "windows": {
                        "path": "tests/parity/evidence/test-reports/"
                        + "9" * 64
                        + ".trx",
                        "sha256": "9" * 64,
                    }
                },
            },
            "executions": {
                "legacy": {
                    "path": "tests/parity/evidence/executions/"
                    + "a" * 64
                    + ".json",
                    "sha256": "a" * 64,
                },
                "xplatRed": {
                    "path": "tests/parity/evidence/executions/"
                    + "b" * 64
                    + ".json",
                    "sha256": "b" * 64,
                },
                "xplatGreen": {
                    "windows": {
                        "path": "tests/parity/evidence/executions/"
                        + "c" * 64
                        + ".json",
                        "sha256": "c" * 64,
                    }
                },
            },
            "regressionGate": {
                "path": (
                    "tests/parity/evidence/regression-gates/"
                    + "d" * 64
                    + ".json"
                ),
                "sha256": "d" * 64,
            },
            "classification": "both-green",
        }
        self.write_document(
            self.base_root,
            self.evidence_path,
            self.base_evidence,
        )
        self.write_document(
            self.current_root,
            self.evidence_path,
            self.base_evidence,
        )
        self.base = self.make_manifest("complete", "both-green")
        self.current = copy.deepcopy(self.base)

    def write_document(
        self,
        root: Path,
        relative_path: str,
        value: dict[str, object],
    ) -> None:
        path = root / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(
            json.dumps(value, indent=2) + "\n",
            encoding="utf-8",
        )

    def make_manifest(
        self,
        capability_status: str,
        case_status: str,
    ) -> dict[str, object]:
        green = case_status == "both-green"
        return {
            "schemaVersion": 3,
            "reference": copy.deepcopy(self.reference),
            "legacySurfaceInventory": (
                "tests/parity/legacy-surface-inventory.json"
            ),
            "items": [
                {
                    "id": "capability.one",
                    "category": "test",
                    "feature": "Feature",
                    "behavior": "Locked behavior",
                    "legacySources": ["Legacy.pas"],
                    "legacySurfaceSelectors": ["legacy.surface.*"],
                    "platforms": ["windows"],
                    "acceptanceStatus": capability_status,
                    "caseIds": ["case.one"],
                }
            ],
            "behavioralObligations": [
                {
                    "id": "obligation.one",
                    "capabilityId": "capability.one",
                    "behavior": "Locked behavioral obligation",
                    "platforms": ["windows"],
                    "sourceBindingStatus": "bound",
                    "legacySources": ["Legacy.pas"],
                    "legacySurfaceSelectors": ["legacy.surface.one"],
                    "acceptanceStatus": (
                        "complete" if green else "partial"
                    ),
                    "caseIds": ["case.one"],
                }
            ],
            "cases": [
                {
                    "id": "case.one",
                    "capabilityId": "capability.one",
                    "obligationIds": ["obligation.one"],
                    "behavior": "Locked case behavior",
                    "legacyOracle": {
                        "adapterId": "Legacy",
                        "versionId": "legacy-v1",
                        "source": (
                            "tests/parity/legacy-oracle/v1/Legacy.lpr"
                        ),
                        "sourceSha256": "1" * 64,
                        "buildRecipe": (
                            "tests/parity/legacy-oracle/v1/"
                            "build-recipe.json"
                        ),
                        "buildRecipeSha256": "2" * 64,
                    },
                    "legacySources": ["Legacy.pas"],
                    "legacySurfaceSelectors": ["legacy.surface.one"],
                    "preconditions": ["Ready"],
                    "input": {"seed": 1},
                    "targetAdapters": ["Legacy", "XPlat"],
                    "assertions": {"fixtureComparison": "exact"},
                    "platforms": ["windows"],
                    "legacyTestStatus": "pass",
                    "xplatTestStatus": "pass" if green else "fail",
                    "status": case_status,
                    "failureCode": None if green else "diverged",
                    "fixture": self.fixture_path,
                    "evidence": self.evidence_path,
                    "firstGreenCommit": "e" * 40 if green else None,
                }
            ],
        }

    def validate_history(self) -> None:
        parity.validate_manifest_history(
            self.current,
            self.base,
            root=self.current_root,
            base_root=self.base_root,
        )

    def write_history_gate(
        self,
        manifest: dict[str, object],
    ) -> dict[str, str]:
        manifest_cases = parity.build_regression_manifest_case_snapshot(
            manifest["cases"],
            {"case.one"},
        )
        gate = {
            "selectedCaseIds": ["case.one"],
            "manifestCases": manifest_cases,
            "manifestCasesSha256": parity.canonical_json_sha256(
                manifest_cases
            ),
        }
        raw = (
            json.dumps(gate, indent=2, ensure_ascii=False) + "\n"
        ).encode("utf-8")
        digest = hashlib.sha256(raw).hexdigest()
        relative = (
            f"tests/parity/evidence/regression-gates/{digest}.json"
        )
        path = self.current_root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)
        return {"path": relative, "sha256": digest}

    def test_additive_green_case_history_is_rejected(self) -> None:
        added = copy.deepcopy(self.current["cases"][0])
        added["id"] = "case.two"
        self.current["cases"].append(added)
        self.current["items"][0]["caseIds"].append("case.two")
        self.current["behavioralObligations"][0]["caseIds"].append("case.two")

        with self.assertRaisesRegex(
            ValueError,
            "must first land with retained legacy-green/XPlat-red evidence",
        ):
            self.validate_history()

    def test_additive_red_case_may_reopen_complete_inventory(self) -> None:
        added = copy.deepcopy(self.current["cases"][0])
        added.update(
            {
                "id": "case.two",
                "xplatTestStatus": "fail",
                "status": "legacy-green-xplat-red",
                "failureCode": "new-divergence",
                "firstGreenCommit": None,
                "evidence": "tests/parity/evidence/case-two.json",
            }
        )
        self.current["cases"].append(added)
        self.current["items"][0]["caseIds"].append("case.two")
        self.current["items"][0]["acceptanceStatus"] = "partial"
        obligation = self.current["behavioralObligations"][0]
        obligation["caseIds"].append("case.two")
        obligation["acceptanceStatus"] = "partial"
        self.write_document(
            self.current_root,
            added["evidence"],
            {
                "schemaVersion": 2,
                "parityId": "case.two",
                "classification": "legacy-green-xplat-red",
                "runs": {
                    "legacy": {"path": "legacy", "sha256": "1" * 64},
                    "xplatRed": {"path": "red", "sha256": "2" * 64},
                    "xplatGreen": {},
                },
            },
        )

        self.validate_history()

    def test_capability_removal_case_removal_and_remapping_are_rejected(
        self,
    ) -> None:
        changes = (
            ("capability removal", lambda m: m["items"].clear(), "removal"),
            ("case removal", lambda m: m["cases"].clear(), "case removal"),
            (
                "case remapping",
                lambda m: m["cases"][0].update(
                    {"capabilityId": "other.capability"}
                ),
                "remapped",
            ),
        )
        for name, mutate, message in changes:
            with self.subTest(name=name):
                self.current = copy.deepcopy(self.base)
                mutate(self.current)
                with self.assertRaisesRegex(ValueError, message):
                    self.validate_history()

    def test_capability_semantics_and_root_trust_are_locked(self) -> None:
        changes = (
            ("behavior", "changed"),
            ("legacySources", ["Other.pas"]),
            ("platforms", ["linux"]),
            ("legacySurfaceSelectors", ["legacy.other.*"]),
        )
        for field, value in changes:
            with self.subTest(field=field):
                self.current = copy.deepcopy(self.base)
                self.current["items"][0][field] = value
                with self.assertRaisesRegex(ValueError, "changed locked"):
                    self.validate_history()

        for root_field, message in (
            ("reference", "trust anchor"),
            ("legacySurfaceInventory", "inventory path"),
        ):
            with self.subTest(root_field=root_field):
                self.current = copy.deepcopy(self.base)
                if root_field == "reference":
                    self.current[root_field]["revision"] = "9" * 40
                else:
                    self.current[root_field] = "other.json"
                with self.assertRaisesRegex(ValueError, message):
                    self.validate_history()

    def test_complete_and_partial_status_regressions_are_rejected(self) -> None:
        self.current["items"][0]["acceptanceStatus"] = "partial"
        with self.assertRaisesRegex(ValueError, "regressed from complete"):
            self.validate_history()

        self.base = self.make_manifest(
            "partial",
            "legacy-green-xplat-red",
        )
        self.current = copy.deepcopy(self.base)
        self.current["items"][0]["acceptanceStatus"] = "not-authored"
        with self.assertRaisesRegex(ValueError, "partial to not-authored"):
            self.validate_history()

    def test_all_green_case_semantic_fields_are_locked(self) -> None:
        changes = {
            "behavior": "changed",
            "legacySources": ["Other.pas"],
            "legacySurfaceSelectors": ["legacy.other.*"],
            "platforms": ["linux"],
            "preconditions": ["Changed"],
            "input": {"seed": 2},
            "targetAdapters": ["Other"],
            "assertions": {"other": True},
            "failureCode": "changed",
            "status": "legacy-green-xplat-red",
            "firstGreenCommit": "9" * 40,
        }
        for field, value in changes.items():
            with self.subTest(field=field):
                self.current = copy.deepcopy(self.base)
                self.current["cases"][0][field] = value
                message = (
                    "regressed from both-green"
                    if field == "status"
                    else f"changed certified {field}"
                )
                with self.assertRaisesRegex(ValueError, message):
                    self.validate_history()

    def test_certified_fixture_and_evidence_paths_are_locked(self) -> None:
        for field, value in (
            ("fixture", "tests/parity/fixtures/legacy/other.json"),
            ("evidence", "tests/parity/evidence/other.json"),
        ):
            with self.subTest(field=field):
                self.current = copy.deepcopy(self.base)
                self.current["cases"][0][field] = value
                with self.assertRaisesRegex(
                    ValueError,
                    f"changed certified {field}",
                ):
                    self.validate_history()

    def test_certified_fixture_and_evidence_content_are_hashed(self) -> None:
        for field, path, value in (
            ("fixture", self.fixture_path, {"values": ["tampered"]}),
            (
                "evidence",
                self.evidence_path,
                {
                    **self.base_evidence,
                    "capturedAtUtc": "2026-07-19T00:00:00Z",
                },
            ),
        ):
            with self.subTest(field=field):
                self.write_document(self.current_root, path, value)
                with self.assertRaisesRegex(
                    ValueError,
                    f"{field} content",
                ):
                    self.validate_history()
                self.write_document(
                    self.current_root,
                    self.fixture_path,
                    {"values": ["expected"]},
                )
                self.write_document(
                    self.current_root,
                    self.evidence_path,
                    self.base_evidence,
                )

    def test_red_case_weakening_is_rejected(self) -> None:
        self.base = self.make_manifest(
            "partial",
            "legacy-green-xplat-red",
        )
        self.current = copy.deepcopy(self.base)
        red_evidence = {
            **self.base_evidence,
            "runs": {
                **self.base_evidence["runs"],
                "xplatGreen": {},
            },
            "testReports": {
                **self.base_evidence["testReports"],
                "xplatGreen": {},
            },
            "executions": {
                **self.base_evidence["executions"],
                "xplatGreen": {},
            },
            "regressionGate": None,
            "classification": "legacy-green-xplat-red",
        }
        self.write_document(self.base_root, self.evidence_path, red_evidence)
        self.write_document(
            self.current_root,
            self.evidence_path,
            red_evidence,
        )
        self.current["cases"][0]["assertions"] = {"weaker": True}

        with self.assertRaisesRegex(ValueError, "weakened locked red-case"):
            self.validate_history()

    def test_red_to_green_transition_preserves_fixture_and_legacy_evidence(
        self,
    ) -> None:
        self.base = self.make_manifest(
            "partial",
            "legacy-green-xplat-red",
        )
        self.current = copy.deepcopy(self.base)
        base_evidence = {
            **self.base_evidence,
            "runs": {
                **self.base_evidence["runs"],
                "xplatGreen": {},
            },
            "testReports": {
                **self.base_evidence["testReports"],
                "xplatGreen": {},
            },
            "executions": {
                **self.base_evidence["executions"],
                "xplatGreen": {},
            },
            "regressionGate": None,
            "classification": "legacy-green-xplat-red",
        }
        current_evidence = {
            **base_evidence,
            "capturedAtUtc": "2026-07-19T00:00:00Z",
            "runs": copy.deepcopy(self.base_evidence["runs"]),
            "testReports": copy.deepcopy(
                self.base_evidence["testReports"]
            ),
            "executions": copy.deepcopy(
                self.base_evidence["executions"]
            ),
            "regressionGate": self.write_history_gate(self.base),
            "classification": "both-green",
        }
        self.write_document(self.base_root, self.evidence_path, base_evidence)
        self.write_document(
            self.current_root,
            self.evidence_path,
            current_evidence,
        )
        self.current["cases"][0].update(
            {
                "xplatTestStatus": "pass",
                "status": "both-green",
                "failureCode": None,
                "firstGreenCommit": "e" * 40,
            }
        )

        self.validate_history()

        current_evidence["runs"]["legacy"] = {
            "path": "tests/parity/evidence/runs/" + "9" * 64 + ".json",
            "sha256": "9" * 64,
        }
        self.write_document(
            self.current_root,
            self.evidence_path,
            current_evidence,
        )
        with self.assertRaisesRegex(ValueError, "legacy run"):
            self.validate_history()

    def test_release_history_rejects_red_case_weakening_against_parent(
        self,
    ) -> None:
        self.base = self.make_manifest(
            "partial",
            "legacy-green-xplat-red",
        )
        self.current = copy.deepcopy(self.base)
        red_evidence = {
            **self.base_evidence,
            "runs": {
                **self.base_evidence["runs"],
                "xplatGreen": {},
            },
            "testReports": {
                **self.base_evidence["testReports"],
                "xplatGreen": {},
            },
            "executions": {
                **self.base_evidence["executions"],
                "xplatGreen": {},
            },
            "regressionGate": None,
            "classification": "legacy-green-xplat-red",
        }
        self.write_document(self.base_root, self.evidence_path, red_evidence)
        self.write_document(
            self.current_root,
            self.evidence_path,
            red_evidence,
        )
        self.current["cases"][0]["assertions"] = {"weaker": True}
        loaded = (
            self.base,
            "HEAD first parent " + "1" * 40,
            self.base_root,
            None,
            "2" * 64,
        )

        with patch.object(
            parity,
            "load_monotonic_base",
            return_value=loaded,
        ) as loader:
            with self.assertRaisesRegex(ValueError, "weakened locked"):
                parity.validate_mode_history(
                    self.current,
                    "Release",
                    None,
                    root=self.current_root,
                )
        self.assertEqual(
            "first-parent",
            loader.call_args.kwargs["checkpoint_kind"],
        )

    def test_main_push_history_rejects_case_deletion_against_before_sha(
        self,
    ) -> None:
        self.current["cases"].clear()
        self.current["items"][0]["caseIds"].clear()
        before_revision = "1" * 40
        loaded = (
            self.base,
            f"earlier main checkpoint {before_revision}",
            self.base_root,
            None,
            "2" * 64,
        )
        environment = {
            parity.HISTORY_KIND_ENVIRONMENT_VARIABLE: "main-push",
            parity.HISTORY_BASE_REVISION_ENVIRONMENT_VARIABLE: (
                before_revision
            ),
        }

        with patch.dict(parity.os.environ, environment, clear=False):
            with patch.object(
                parity,
                "load_monotonic_base",
                return_value=loaded,
            ) as loader:
                with self.assertRaisesRegex(
                    ValueError,
                    "case removal",
                ):
                    parity.validate_mode_history(
                        self.current,
                        "PullRequest",
                        None,
                        root=self.current_root,
                    )
        self.assertEqual(
            "main-push",
            loader.call_args.kwargs["checkpoint_kind"],
        )
        self.assertEqual(
            before_revision,
            loader.call_args.kwargs["checkpoint_revision"],
        )


    def test_red_status_rejects_any_retained_green_artifact(self) -> None:
        base = copy.deepcopy(self.base_evidence)
        base["classification"] = "legacy-green-xplat-red"
        base["regressionGate"] = None
        for root_field in ("runs", "testReports", "executions"):
            base[root_field]["xplatGreen"] = {}
        for root_field in ("runs", "testReports", "executions"):
            with self.subTest(root_field=root_field):
                current = copy.deepcopy(base)
                current[root_field]["xplatGreen"]["windows"] = {
                    "path": (
                        f"tests/parity/evidence/{root_field}/"
                        + "e" * 64
                        + ".json"
                    ),
                    "sha256": "e" * 64,
                }
                with self.assertRaisesRegex(
                    ValueError,
                    "status remained red",
                ):
                    parity.validate_red_evidence_history(
                        "case.one",
                        base,
                        current,
                        transitioned_to_green=False,
                    )

    def test_red_to_green_history_accepts_atomic_three_platform_maps(
        self,
    ) -> None:
        base = copy.deepcopy(self.base_evidence)
        base["classification"] = "legacy-green-xplat-red"
        base["regressionGate"] = None
        for root_field in ("runs", "testReports", "executions"):
            base[root_field]["xplatGreen"] = {}
        current = copy.deepcopy(base)
        current["classification"] = "both-green"
        current["regressionGate"] = {
            "path": (
                "tests/parity/evidence/regression-gates/"
                + "e" * 64
                + ".json"
            ),
            "sha256": "e" * 64,
        }
        for root_field in ("runs", "testReports", "executions"):
            suffix = ".trx" if root_field == "testReports" else ".json"
            for index, platform in enumerate(
                ("windows", "linux", "macos"),
                start=1,
            ):
                digest = str(index) * 64
                current[root_field]["xplatGreen"][platform] = {
                    "path": (
                        f"tests/parity/evidence/{root_field}/"
                        f"{digest}{suffix}"
                    ),
                    "sha256": digest,
                }
        parity.validate_red_evidence_history(
            "case.one",
            base,
            current,
            transitioned_to_green=True,
        )


class ModeAndMigrationTests(unittest.TestCase):
    def test_trusted_migration_reference_matches_reviewed_manifest(
        self,
    ) -> None:
        manifest = parity.load_json(parity.MANIFEST_PATH)
        reference = manifest["reference"]
        for key, expected in (
            parity.TRUSTED_V3_MIGRATION_REFERENCE.items()
        ):
            actual = reference[key]
            with self.subTest(key=key):
                if key.endswith("Sha256"):
                    self.assertEqual(expected.lower(), actual.lower())
                else:
                    self.assertEqual(expected, actual)

        definition_path = (
            parity.ROOT
            / parity.TRUSTED_V3_MIGRATION_REFERENCE["definition"]
        )
        self.assertEqual(
            parity.TRUSTED_V3_MIGRATION_REFERENCE[
                "definitionSha256"
            ].lower(),
            parity.sha256_file(definition_path),
        )

    def trusted_base(self) -> tuple[dict[str, object], bytes]:
        result = subprocess.run(
            [
                "git",
                "show",
                "origin/main:tests/parity/parity-manifest.json",
            ],
            capture_output=True,
            check=True,
        )
        return json.loads(result.stdout), result.stdout

    def migration_manifest(self) -> dict[str, object]:
        reviewed_manifest = parity.load_json(parity.MANIFEST_PATH)
        return {
            "schemaVersion": 3,
            "canonicalJson": copy.deepcopy(
                reviewed_manifest["canonicalJson"]
            ),
            "reference": copy.deepcopy(
                parity.TRUSTED_V3_MIGRATION_REFERENCE
            ),
            "legacySurfaceInventory": (
                "tests/parity/legacy-surface-inventory.json"
            ),
            "items": copy.deepcopy(reviewed_manifest["items"]),
            "behavioralObligations": copy.deepcopy(
                reviewed_manifest["behavioralObligations"]
            ),
            "cases": [],
        }

    def migration_evidence(self) -> list[dict[str, object]]:
        return [
            {
                "schemaVersion": 1,
                "parityId": parity_id,
                "classification": "legacy-v1-uncertified",
                "retention": {
                    "status": "legacy-v1-noncertifying",
                },
            }
            for parity_id in sorted(parity.TRUSTED_V1_EVIDENCE_IDS)
        ]

    def test_trusted_schema_v1_to_v3_migration_is_accepted(self) -> None:
        base, raw = self.trusted_base()

        parity.validate_manifest_history(
            self.migration_manifest(),
            base,
            base_manifest_sha256=hashlib.sha256(raw).hexdigest(),
            retained_evidence=self.migration_evidence(),
        )

    def test_tampered_or_unrecognized_schema_v1_base_is_rejected(self) -> None:
        base, raw = self.trusted_base()
        base["items"][0]["feature"] = "tampered"
        tampered_raw = (
            json.dumps(base, separators=(",", ":")).encode("utf-8")
        )

        with self.assertRaisesRegex(ValueError, "unrecognized or tampered"):
            parity.validate_manifest_history(
                self.migration_manifest(),
                base,
                base_manifest_sha256=hashlib.sha256(
                    tampered_raw
                ).hexdigest(),
                retained_evidence=self.migration_evidence(),
            )
        self.assertEqual(
            parity.TRUSTED_V1_MANIFEST_SHA256,
            hashlib.sha256(raw).hexdigest(),
        )

    def test_migration_cannot_drop_capability_evidence_or_inherit_complete(
        self,
    ) -> None:
        base, raw = self.trusted_base()
        digest = hashlib.sha256(raw).hexdigest()
        manifest = self.migration_manifest()
        manifest["items"].pop()
        with self.assertRaisesRegex(ValueError, "capability semantics"):
            parity.validate_manifest_history(
                manifest,
                base,
                base_manifest_sha256=digest,
                retained_evidence=self.migration_evidence(),
            )

        manifest = self.migration_manifest()
        manifest["items"][0]["acceptanceStatus"] = "complete"
        with self.assertRaisesRegex(ValueError, "may not inherit complete"):
            parity.validate_manifest_history(
                manifest,
                base,
                base_manifest_sha256=digest,
                retained_evidence=self.migration_evidence(),
            )

        with self.assertRaisesRegex(ValueError, "reviewed 25"):
            parity.validate_manifest_history(
                self.migration_manifest(),
                base,
                base_manifest_sha256=digest,
                retained_evidence=self.migration_evidence()[:-1],
            )

    def test_migration_obligation_anchor_rejects_inventory_mutation(
        self,
    ) -> None:
        base, raw = self.trusted_base()
        digest = hashlib.sha256(raw).hexdigest()
        mutations = (
            (
                "deletion",
                lambda manifest: manifest[
                    "behavioralObligations"
                ].pop(),
                "obligation inventory",
            ),
            (
                "rename",
                lambda manifest: manifest[
                    "behavioralObligations"
                ][0].update({"id": "renamed.obligation"}),
                "obligation inventory",
            ),
            (
                "reownership",
                lambda manifest: manifest[
                    "behavioralObligations"
                ][0].update(
                    {"capabilityId": "audio.legacy-adapters"}
                ),
                "obligation inventory",
            ),
            (
                "semantics",
                lambda manifest: manifest[
                    "behavioralObligations"
                ][0].update({"behavior": "weakened"}),
                "semantics or platforms",
            ),
            (
                "platforms",
                lambda manifest: manifest[
                    "behavioralObligations"
                ][0].update({"platforms": ["windows"]}),
                "semantics or platforms",
            ),
        )
        for name, mutate, message in mutations:
            with self.subTest(name=name):
                manifest = self.migration_manifest()
                mutate(manifest)
                with self.assertRaisesRegex(ValueError, message):
                    parity.validate_manifest_history(
                        manifest,
                        base,
                        base_manifest_sha256=digest,
                        retained_evidence=self.migration_evidence(),
                    )

    def test_migration_capability_anchor_rejects_semantic_mutation(
        self,
    ) -> None:
        base, raw = self.trusted_base()
        digest = hashlib.sha256(raw).hexdigest()
        for field, value in (
            ("category", "weakened"),
            ("feature", "weakened"),
            ("behavior", "weakened"),
            ("legacySources", ["Other.pas"]),
            ("legacySurfaceSelectors", ["legacy.other.*"]),
            ("platforms", ["windows"]),
        ):
            with self.subTest(field=field):
                manifest = self.migration_manifest()
                manifest["items"][0][field] = value
                with self.assertRaisesRegex(
                    ValueError,
                    "capability semantics",
                ):
                    parity.validate_manifest_history(
                        manifest,
                        base,
                        base_manifest_sha256=digest,
                        retained_evidence=self.migration_evidence(),
                    )

    def test_development_reports_missing_base_but_pull_request_requires_it(
        self,
    ) -> None:
        completed = subprocess.CompletedProcess(
            ["git"],
            1,
            stdout="",
            stderr="missing",
        )
        with patch.object(parity.subprocess, "run", return_value=completed):
            note = parity.validate_mode_history(
                {"schemaVersion": 3},
                "Development",
                None,
            )
            self.assertIn("not run", note)
        with patch.object(parity.subprocess, "run", return_value=completed):
            with self.assertRaisesRegex(ValueError, "requires a trusted"):
                parity.validate_mode_history(
                    {"schemaVersion": 3},
                    "PullRequest",
                    None,
                )

    def test_release_and_baseline_modes_have_distinct_gates(self) -> None:
        red_case = {
            "id": "case.one",
            "legacyTestStatus": "pass",
            "xplatTestStatus": "fail",
            "status": "legacy-green-xplat-red",
        }
        manifest = {
            "inventoryStatus": "complete",
            "pendingAuditSurfaces": [],
            "items": [
                {
                    "acceptanceStatus": "partial",
                }
            ],
            "behavioralObligations": [
                {
                    "id": "obligation.one",
                    "caseIds": ["case.one"],
                    "sourceBindingStatus": "pending",
                    "acceptanceStatus": "partial",
                }
            ],
            "cases": [red_case],
        }
        inventory = {"surfaces": []}
        with redirect_stdout(io.StringIO()):
            self.assertEqual(
                0,
                parity.report(
                    manifest,
                    inventory,
                    [],
                    "Baseline",
                ),
            )
            self.assertEqual(
                1,
                parity.report(
                    manifest,
                    inventory,
                    [],
                    "Release",
                ),
            )


if __name__ == "__main__":
    unittest.main()
