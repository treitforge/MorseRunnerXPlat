from __future__ import annotations

import argparse
import base64
import copy
import fnmatch
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ElementTree
from collections import Counter, defaultdict
from contextlib import contextmanager
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any

import yaml

from inventory_legacy import (
    build_inventory,
    serialize_inventory,
    tracked_files_sha256,
    validate_tracked_file_classification,
)


ROOT = Path(__file__).resolve().parents[2]
MANIFEST_PATH = ROOT / "tests" / "parity" / "parity-manifest.json"
REPORT_PATH = ROOT / "tests" / "parity" / "PARITY_REPORT.md"
LEGACY_REFERENCE_PATH = (
    ROOT / "tests" / "parity" / "legacy-reference.json"
)
FUNCTIONAL_DIVERGENCE_TRX_EXCEPTION_TYPE = (
    "MorseRunner.LegacyParity.Tests."
    "ParityFunctionalDivergenceException"
)
MANIFEST_SCHEMA_VERSION = 3
TRUSTED_V1_MANIFEST_SHA256 = (
    "8b042faa574223dd4a3ac623bc508c3bad97c72313cbfd5f87aa373a80a080ad"
)
TRUSTED_V1_CAPABILITY_IDS = {
    "audio-dsp.legacy-processing",
    "audio.legacy-adapters",
    "catalog.contest-definitions",
    "catalog.contest-enumeration",
    "configuration.persisted-settings",
    "contest.legacy-implementations",
    "data.files-and-operational-paths",
    "data.legacy-parsers",
    "logging.qso-model",
    "logging.scoring-rate-and-results",
    "session.run-mode-enumeration",
    "simulation.legacy-effects",
    "simulation.runtime-routines",
    "simulation.state-models",
    "ux.keyboard-workflows",
    "ux.legacy-vcl-components",
    "ux.main-form-events",
    "ux.main-form-objects",
    "ux.main-menu-commands",
    "ux.score-dialog",
}
TRUSTED_V1_EVIDENCE_IDS = TRUSTED_V1_CAPABILITY_IDS | {
    "contest.cq-wpx-scoring",
    "contest.cwt-scoring",
    "contest.remaining-scoring",
    "simulation.live-operator-session",
    "simulation.live-station-session",
}
TRUSTED_V3_MIGRATION_OBLIGATION_OWNERS = {
    "catalog.contest-identifiers": "catalog.contest-enumeration",
    "catalog.contest-metadata-and-defaults": "catalog.contest-definitions",
    "session.run-mode-identifiers": "session.run-mode-enumeration",
    "quality.live-ce-oracle-required": "quality.legacy-tests-and-smoke",
    "quality.xplat-acceptance-target-executes": (
        "quality.legacy-tests-and-smoke"
    ),
    "quality.ci-builds-pinned-ce": "build.legacy-project-metadata",
    "quality.complete-replay-inputs-and-first-green": (
        "quality.legacy-tests-and-smoke"
    ),
    "quality.manifest-evidence-reconciliation": (
        "quality.legacy-tests-and-smoke"
    ),
    "quality.complete-legacy-inventory": (
        "quality.legacy-tests-and-smoke"
    ),
    "contest.oracle-active-contest-selection": (
        "contest.legacy-implementations"
    ),
    "quality.behavioral-not-structural-evidence": (
        "quality.legacy-tests-and-smoke"
    ),
    "audio.operator-sidetone-pipeline": "audio-dsp.legacy-processing",
    "audio.non-farnsworth-symbols-timing-and-ramps": (
        "audio-dsp.legacy-processing"
    ),
    "audio.moving-average-receiver-filter-numerics": (
        "audio-dsp.legacy-processing"
    ),
    "audio.carrier-quantization-and-modulator-numerics": (
        "audio-dsp.legacy-processing"
    ),
    "audio.agc-equations-and-state-numerics": (
        "audio-dsp.legacy-processing"
    ),
    "audio.default-compatibility-output-profile": "audio.legacy-adapters",
    "audio.physical-queue-depth-and-order": "audio.legacy-adapters",
    "audio.deterministic-random-primitives": "simulation.legacy-effects",
    "audio.complex-station-mixing-signs-order-and-normalization": (
        "audio-dsp.legacy-processing"
    ),
    "audio.qsk-receiver-ducking-and-recovery": (
        "audio-dsp.legacy-processing"
    ),
    "audio.rit-affects-rendered-stations": "audio-dsp.legacy-processing",
    "audio.runtime-bandwidth-updates-filters": (
        "audio-dsp.legacy-processing"
    ),
    "audio.qrm-interfering-cw-stations": "simulation.legacy-effects",
    "audio.qrn-impulses-and-burst-stations": (
        "simulation.legacy-effects"
    ),
    "audio.qsb-independent-per-station": "simulation.legacy-effects",
    "audio.flutter-fast-per-station-qsb": "simulation.legacy-effects",
    "audio.station-level-and-pitch-distributions": (
        "audio-dsp.legacy-processing"
    ),
    "audio.bfo-phase-state-and-reset": "audio-dsp.legacy-processing",
    "audio.sst-farnsworth-timing": "audio-dsp.legacy-processing",
    "audio.sst-farnsworth-session-wiring": (
        "audio-dsp.legacy-processing"
    ),
    "audio.single-seeded-random-stream": "simulation.legacy-effects",
    "audio.legacy-block-size-configurations": (
        "audio-dsp.legacy-processing"
    ),
    "audio.startup-warmup-and-filter-timing": (
        "audio-dsp.legacy-processing"
    ),
    "audio.realistic-hiss-and-noise-floor": (
        "audio-dsp.legacy-processing"
    ),
    "audio.wav-pcm-bit-exact": "audio.legacy-adapters",
    "audio.recording-failure-and-backpressure-isolation": (
        "audio.legacy-adapters"
    ),
    "audio.sink-preconversion-block-equivalence": "audio.legacy-adapters",
    "audio.physical-device-lifecycle": "audio.legacy-adapters",
    "audio.all-effects-performance": "audio.legacy-adapters",
    "engine.operator-message-completion-timing": (
        "simulation.runtime-routines"
    ),
    "engine.event-driven-poisson-caller-arrivals": (
        "simulation.runtime-routines"
    ),
    "engine.start-silent-empty-enter-cq": "simulation.runtime-routines",
    "engine.contest-specific-cq-tu-and-station-id": (
        "contest.legacy-implementations"
    ),
    "contest.full-remote-exchange-formatting": (
        "contest.legacy-implementations"
    ),
    "contest.required-exchange-fields-all-contests": (
        "contest.legacy-implementations"
    ),
    "contest.allja-acag-truth-column-mapping": (
        "contest.legacy-implementations"
    ),
    "contest.sweepstakes-complete-truth-model": (
        "contest.legacy-implementations"
    ),
    "contest.arrldx-naqp-home-filtering-and-location": (
        "contest.legacy-implementations"
    ),
    "contest.wpx-hst-station-serial-generation": (
        "contest.legacy-implementations"
    ),
    "session.hst-wpx-start-constraints": "session.run-mode-enumeration",
    "engine.station-delay-lifetime-and-call-pool-rules": (
        "simulation.state-models"
    ),
    "engine.midmessage-append-correction-and-abort": (
        "simulation.runtime-routines"
    ),
    "engine.confidence-lid-repeat-and-f12-branches": (
        "simulation.runtime-routines"
    ),
    "engine.reset-and-restart-state": "simulation.state-models",
    "contest.exchange-shapes-and-constructor-metadata": (
        "contest.legacy-implementations"
    ),
    "logging.nil-without-live-station-truth": "logging.qso-model",
    "logging.worked-call-after-verified-success": "logging.qso-model",
    "logging.duplicate-requires-prior-correct-qso": "logging.qso-model",
    "logging.complete-copied-true-error-model": "logging.qso-model",
    "logging.deliberate-wrong-incomplete-nil-b4-corrected": (
        "logging.qso-model"
    ),
    "logging.raw-verified-points-multipliers-rate-corrections": (
        "logging.scoring-rate-and-results"
    ),
    "logging.score-history-and-competition-results": (
        "logging.scoring-rate-and-results"
    ),
    "logging.complete-contest-cabrillo-export": (
        "logging.scoring-rate-and-results"
    ),
    "ux.persisted-per-contest-own-exchange": (
        "configuration.persisted-settings"
    ),
    "ux.f2-and-esm-send-own-exchange": "ux.main-form-events",
    "ux.contest-aware-entry-layout-and-focus": "ux.main-form-objects",
    "ux.input-transforms-paste-and-ime": "ux.keyboard-workflows",
    "ux.wipe-resets-authoritative-qso-state": "ux.main-form-events",
    "ux.abort-resets-authoritative-send-state": "ux.keyboard-workflows",
    "ux.punctuation-and-modified-enter-logging": "ux.keyboard-workflows",
    "ux.f9-explicit-pileup-action": "ux.main-menu-commands",
    "ux.live-settings-send-semantic-commands": "ux.main-menu-commands",
    "ux.rit-wpm-bandwidth-ranges-and-steps": "ux.keyboard-workflows",
    "ux.shortcut-labels-and-score-wipe-commands": (
        "ux.main-menu-commands"
    ),
    "ux.plus-equals-keypad-and-layouts": "ux.keyboard-workflows",
    "ux.keyboard-and-pointer-tuning-workflows": "ux.keyboard-workflows",
    "ux.log-truth-correction-and-error-presentation": (
        "ux.main-form-objects"
    ),
    "ux.start-stop-reset-fields-focus-and-pointer": "ux.main-form-events",
    "ux.high-frequency-tab-order": "ux.legacy-form-definitions",
    "ux.numeric-controls-and-live-status-accessibility": (
        "ux.legacy-form-definitions"
    ),
    "ux.native-scaling-contrast-and-layout": "ux.legacy-form-definitions",
    "ux.score-dialog-and-history-workflow": "ux.score-dialog",
    "ux.legacy-component-runtime-behavior": "ux.legacy-vcl-components",
    "settings.production-legacy-import": "configuration.persisted-settings",
    "settings.ce-encoding-translation": "configuration.persisted-settings",
    "settings.preserve-unknown-and-unconsumed-values": (
        "configuration.persisted-settings"
    ),
    "settings.clean-profile-ce-defaults": (
        "configuration.persisted-settings"
    ),
    "settings.full-duration-range": "configuration.persisted-settings",
    "data.replaceable-reference-root-and-fallback": (
        "data.files-and-operational-paths"
    ),
    "data.malformed-and-missing-file-reporting": "data.legacy-parsers",
    "data.legacy-parser-output-equivalence": "data.legacy-parsers",
    "recording.ce-filename-overwrite-and-discovery": (
        "data.files-and-operational-paths"
    ),
    "release.version-package-and-about-provenance": (
        "build.legacy-project-metadata"
    ),
    "release.real-native-gui-window-evidence": (
        "quality.legacy-tests-and-smoke"
    ),
    "release.physical-audio-evidence-required": (
        "quality.legacy-tests-and-smoke"
    ),
    "transport.completed-state-is-immutable": "simulation.state-models",
    "transport.single-active-interactive-session": (
        "simulation.state-models"
    ),
    "transport.commands-apply-at-every-block-boundary": (
        "simulation.runtime-routines"
    ),
    "transport.bounded-idempotent-mutations": (
        "simulation.runtime-routines"
    ),
    "transport.lossless-events-coalesced-snapshots": (
        "simulation.runtime-routines"
    ),
    "transport.resync-watermark-ordering": "simulation.state-models",
    "transport.required-event-groups": "simulation.state-models",
    "transport.control-lease-semantics": "simulation.state-models",
    "transport.inprocess-grpc-full-session-equivalence": (
        "quality.legacy-tests-and-smoke"
    ),
    "release.native-gui-input-accessibility-evidence": (
        "quality.legacy-tests-and-smoke"
    ),
    "release.all-client-and-artifact-path-evidence": (
        "quality.legacy-tests-and-smoke"
    ),
    "release.platform-complete-publication-gate": (
        "build.legacy-project-metadata"
    ),
    "release.zero-skips-live-final-certification": (
        "quality.legacy-tests-and-smoke"
    ),
    "release.experienced-user-ab-listening": (
        "quality.legacy-tests-and-smoke"
    ),
    "ux.enter-esm-partial-call-message-selection": (
        "ux.keyboard-workflows"
    ),
    "ux.log-selection-updates-callsign-information": "ux.main-form-events",
    "ux.semantic-duration-not-simulation-blocks": "ux.main-form-events",
    "ux.help-about-readme-community-actions": "ux.main-menu-commands",
    "ux.score-service-browse-submit-and-failures": "ux.score-dialog",
    "transport.session-lifecycle-transition-validity": (
        "simulation.state-models"
    ),
    "settings.all-supported-ce-keys-consumed-or-preserved": (
        "configuration.persisted-settings"
    ),
    "release.cli-help-version-exit-codes": (
        "build.legacy-project-metadata"
    ),
    "lifecycle.unit-initialization-finalization-order": (
        "lifecycle.legacy-unit-hooks"
    ),
}
TRUSTED_V3_MIGRATION_OBLIGATION_IDS = frozenset(
    TRUSTED_V3_MIGRATION_OBLIGATION_OWNERS
)
TRUSTED_V3_MIGRATION_OBLIGATION_SHA256 = (
    "032c76a2526ac886dc981744b78add57"
    "9450a82f16671e38a14ac5873e250ace"
)
TRUSTED_V3_MIGRATION_CAPABILITY_SHA256 = (
    "04330886992153a69429ed7f271e9bf7"
    "27b1443861008e3d786f25c4c5e3f903"
)
TRUSTED_V3_MIGRATION_REFERENCE = {
    "repository": "https://github.com/w7sst/MorseRunner.git",
    "revision": "55bbd019c29d8cf693184ea420a17a253f16fe1e",
    "tree": "a44212bfee5b1eebfd0129459d476736775adf36",
    "bundle": "tests/parity/legacy-reference.bundle",
    "bundleSha256": (
        "1D9FCAFB3ADB0227ABA360BC1884B5C32D2C1E8210448E646A4104F142B07772"
    ),
    "definition": "tests/parity/legacy-reference.json",
    "definitionSha256": (
        "663ADF3BF230161ABB923CF8B6651D394AF1B99EAB05EFEAFB696CB29992DA23"
    ),
}
CAPABILITY_STATUSES = {"not-authored", "partial", "complete"}
OBLIGATION_STATUSES = {"not-authored", "partial", "complete"}
OBLIGATION_SOURCE_BINDING_STATUSES = {"pending", "bound"}
RICH_EVIDENCE_REQUIRED_OBLIGATIONS = frozenset(
    {
        "audio.physical-device-lifecycle",
        "audio.all-effects-performance",
        "ux.numeric-controls-and-live-status-accessibility",
        "ux.native-scaling-contrast-and-layout",
        "release.real-native-gui-window-evidence",
        "release.physical-audio-evidence-required",
        "release.native-gui-input-accessibility-evidence",
        "release.experienced-user-ab-listening",
    }
)
# Additions require a locked history review proving that the behavior itself,
# rather than only one XPlat adapter, is Windows-specific.
WINDOWS_ONLY_OBLIGATION_IDS = frozenset(
    {
        "quality.live-ce-oracle-required",
        "quality.ci-builds-pinned-ce",
        "contest.oracle-active-contest-selection",
    }
)
CASE_STATUSES = {"legacy-green-xplat-red", "both-green"}
CERTIFIED_CAPABILITY_FIELDS = {
    "category",
    "feature",
    "behavior",
    "legacySources",
    "legacySurfaceSelectors",
    "platforms",
}
CERTIFIED_CASE_FIELDS = {
    "capabilityId",
    "obligationIds",
    "behavior",
    "legacyOracle",
    "legacySources",
    "legacySurfaceSelectors",
    "preconditions",
    "input",
    "targetAdapters",
    "assertions",
    "platforms",
    "legacyTestStatus",
    "xplatTestStatus",
    "status",
    "failureCode",
    "fixture",
    "evidence",
    "firstGreenCommit",
}
CERTIFIED_OBLIGATION_FIELDS = {
    "capabilityId",
    "behavior",
    "platforms",
    "sourceBindingStatus",
    "legacySources",
    "legacySurfaceSelectors",
}
CASE_DEFINITION_FIELDS = (
    "id",
    "capabilityId",
    "obligationIds",
    "behavior",
    "legacyOracle",
    "legacySources",
    "legacySurfaceSelectors",
    "preconditions",
    "input",
    "targetAdapters",
    "assertions",
    "platforms",
    "fixture",
)
EVIDENCE_SCHEMA_VERSION = 2
RETAINED_EVIDENCE_SCHEMA_VERSION = 1
CANONICAL_JSON_VERSION = 1
CANONICAL_JSON_VECTOR_PATH = (
    "tests/parity/canonical-json-vectors.json"
)
CANONICAL_JSON_VECTOR_SHA256 = (
    "8a3c187ebd3846533be418f811bb87a34"
    "a45ec4dba008c7f8e1db7c299a04d33"
)
CANONICAL_JSON_VECTORS_FILE_SHA256 = (
    "4ba11483bf880f673218b2e284bbae1c"
    "ff8e91f8e36e5a5cf42f810c10ae646f"
)
LEGACY_ORACLE_DESCRIPTOR_VECTOR_PATH = (
    "tests/parity/legacy-oracle-descriptor-vectors.json"
)
LEGACY_ORACLE_DESCRIPTOR_VECTOR_SHA256 = (
    "ce06dc4f09731963812182ff8a8cee90"
    "b766c79d1226f23bc7eb2535cc79c3a6"
)
HISTORY_KIND_ENVIRONMENT_VARIABLE = (
    "MORSE_RUNNER_PARITY_HISTORY_KIND"
)
HISTORY_BASE_REVISION_ENVIRONMENT_VARIABLE = (
    "MORSE_RUNNER_PARITY_BASE_REVISION"
)
RETAINED_EVIDENCE_CLASSIFICATION = "legacy-v1-uncertified"
RETAINED_EVIDENCE_STATUS = "legacy-v1-noncertifying"
COMMIT_PATTERN = re.compile(r"^[0-9a-f]{40}$")
SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
LEGACY_ORACLE_VERSION_PATTERN = re.compile(
    r"[a-z0-9][a-z0-9.-]*-v[1-9][0-9]*"
)
UTC_TIMESTAMP_PATTERN = re.compile(
    r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$"
)
SUPPORTED_PLATFORMS = {"windows", "linux", "macos"}
FULL_SUITE_TARGETS = {"Legacy", "XPlat"}
RUNTIME_IDENTIFIER_PREFIXES = {
    "windows": "win-",
    "linux": "linux-",
    "macos": "osx-",
}
SUPPORTED_PROCESS_ARCHITECTURES = {
    "x86",
    "x64",
    "arm",
    "arm64",
    "wasm",
    "s390x",
    "loongarch64",
    "ppc64le",
}


def reject_duplicate_json_keys(
    pairs: list[tuple[str, Any]],
) -> dict[str, Any]:
    value: dict[str, Any] = {}
    for key, entry in pairs:
        if key in value:
            raise ValueError(f"duplicate JSON field: {key}")
        value[key] = entry
    return value


def reject_nonstandard_json_number(value: str) -> Any:
    raise ValueError(f"non-standard JSON number: {value}")


def load_json(path: Path) -> dict[str, Any]:
    with path.open(encoding="utf-8") as stream:
        value = parse_json_value(stream.read(), str(path))
    return value


def parse_json_value(text: str, label: str) -> dict[str, Any]:
    value = json.loads(
        text,
        object_pairs_hook=reject_duplicate_json_keys,
        parse_constant=reject_nonstandard_json_number,
    )
    if not isinstance(value, dict):
        raise ValueError(f"{label} must contain a JSON object")
    return value


def require_fields(
    value: dict[str, Any],
    required: set[str],
    label: str,
    *,
    allowed: set[str] | None = None,
) -> None:
    missing = sorted(required - value.keys())
    if missing:
        raise ValueError(f"{label} is missing fields: {', '.join(missing)}")
    if allowed is not None:
        unexpected = sorted(value.keys() - allowed)
        if unexpected:
            raise ValueError(
                f"{label} has unsupported fields: {', '.join(unexpected)}"
            )


def require_nonempty_string(value: Any, label: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"{label} must be a non-empty string")
    return value


def require_signed_integer(
    value: Any,
    label: str,
    *,
    minimum: int | None = None,
    maximum: int | None = None,
) -> int:
    if (
        type(value) is not int
        or value < -(2**63)
        or value > 2**63 - 1
        or (minimum is not None and value < minimum)
        or (maximum is not None and value > maximum)
    ):
        raise ValueError(f"{label} must be a signed integer")
    return value


def require_string_array(
    value: Any,
    label: str,
    *,
    allow_empty: bool = False,
) -> list[str]:
    if not isinstance(value, list) or (not value and not allow_empty):
        qualifier = "" if allow_empty else " non-empty"
        raise ValueError(f"{label} must be a{qualifier} string array")
    if any(not isinstance(entry, str) or not entry.strip() for entry in value):
        raise ValueError(f"{label} must contain only non-empty strings")
    return value


def is_within(path: Path, parent: Path) -> bool:
    try:
        path.relative_to(parent)
    except ValueError:
        return False
    return True


def require_canonical_repo_relative_path(
    value: Any,
    label: str,
) -> str:
    path_text = require_nonempty_string(value, label)
    if "\\" in path_text:
        raise ValueError(
            f"{label} must use canonical forward-slash separators"
        )
    segments = path_text.split("/")
    if (
        not segments
        or any(segment in {"", ".", ".."} for segment in segments)
        or path_text.startswith("/")
    ):
        raise ValueError(
            f"{label} must be a canonical repository-relative path"
        )
    return path_text


def resolve_repo_file(
    relative_path: Any,
    allowed_root: str,
    label: str,
    *,
    root: Path = ROOT,
) -> Path:
    path_text = require_nonempty_string(relative_path, label)
    untrusted_path = Path(path_text)
    if untrusted_path.is_absolute():
        raise ValueError(f"{label} must be repository-relative: {path_text}")

    repository_root = root.resolve()
    permitted_root = (repository_root / allowed_root).resolve()
    resolved_path = (repository_root / untrusted_path).resolve()
    if not is_within(resolved_path, permitted_root):
        raise ValueError(
            f"{label} escapes {allowed_root}: {path_text}"
        )
    if not resolved_path.is_file():
        raise ValueError(f"{label} does not exist: {resolved_path}")
    return resolved_path


def validate_timestamp(value: Any, label: str) -> None:
    timestamp = require_nonempty_string(value, label)
    if not UTC_TIMESTAMP_PATTERN.fullmatch(timestamp):
        raise ValueError(f"{label} must use YYYY-MM-DDTHH:MM:SSZ")
    try:
        datetime.strptime(timestamp, "%Y-%m-%dT%H:%M:%SZ")
    except ValueError as error:
        raise ValueError(f"{label} is not a valid UTC timestamp") from error


def validate_manifest(
    manifest: dict[str, Any],
    legacy_root: Path | None,
    *,
    root: Path = ROOT,
    promotion_case_ids: set[str] | None = None,
) -> tuple[
    dict[str, Any],
    dict[str, list[str]],
    list[dict[str, Any]],
]:
    validate_hashing_attributes(root)
    validate_acceptance_test_wiring(root)
    required_root = {
        "schemaVersion",
        "canonicalJson",
        "inventoryStatus",
        "reference",
        "legacySurfaceInventory",
        "auditedSurfaces",
        "pendingAuditSurfaces",
        "items",
        "behavioralObligations",
        "cases",
    }
    require_fields(
        manifest,
        required_root,
        "manifest",
        allowed=required_root,
    )
    if manifest["schemaVersion"] != MANIFEST_SCHEMA_VERSION:
        raise ValueError("manifest has an unsupported schema version")
    validate_canonical_json_contract(
        manifest["canonicalJson"],
        root=root,
    )
    validate_legacy_oracle_descriptor_vectors(root=root)
    if manifest["inventoryStatus"] not in {"complete", "in-progress"}:
        raise ValueError("manifest has an invalid inventory status")

    reference = manifest["reference"]
    if not isinstance(reference, dict):
        raise ValueError("manifest reference must be an object")
    require_fields(
        reference,
        {
            "repository",
            "revision",
            "tree",
            "bundle",
            "bundleSha256",
            "definition",
            "definitionSha256",
        },
        "manifest reference",
        allowed={
            "repository",
            "revision",
            "tree",
            "bundle",
            "bundleSha256",
            "definition",
            "definitionSha256",
        },
    )
    require_nonempty_string(
        reference["repository"],
        "manifest reference repository",
    )
    pinned_revision = require_nonempty_string(
        reference["revision"],
        "manifest reference revision",
    )
    if not COMMIT_PATTERN.fullmatch(pinned_revision):
        raise ValueError("manifest reference revision must be a full commit ID")
    pinned_tree = require_nonempty_string(
        reference["tree"],
        "manifest reference tree",
    )
    if not COMMIT_PATTERN.fullmatch(pinned_tree):
        raise ValueError("manifest reference tree must be a full tree ID")
    require_nonempty_string(
        reference["bundle"],
        "manifest reference bundle",
    )
    validate_sha256(
        reference["bundleSha256"],
        "manifest reference bundleSha256",
    )
    definition_path = resolve_repo_file(
        reference["definition"],
        "tests/parity",
        "manifest reference definition",
        root=root,
    )
    require_json_suffix(
        definition_path,
        "manifest reference definition",
    )
    definition_digest = validate_sha256(
        reference["definitionSha256"],
        "manifest reference definitionSha256",
    )
    if sha256_file(definition_path) != definition_digest.lower():
        raise ValueError(
            "manifest reference definitionSha256 does not match "
            "legacy-reference.json"
        )
    legacy_reference, actual_definition_digest = load_legacy_reference(
        pinned_revision,
        root=root,
    )
    expected_reference = {
        "repository": legacy_reference["repository"],
        "revision": legacy_reference["revision"],
        "tree": legacy_reference["tree"],
        "bundle": legacy_reference["bundle"],
        "bundleSha256": legacy_reference["bundleSha256"],
        "definition": "tests/parity/legacy-reference.json",
        "definitionSha256": actual_definition_digest,
    }
    for key, expected_value in expected_reference.items():
        actual_value = reference[key]
        if key.endswith("Sha256"):
            matches = actual_value.lower() == expected_value.lower()
        else:
            matches = actual_value == expected_value
        if not matches:
            raise ValueError(
                f"manifest reference {key} does not match "
                "legacy-reference.json"
            )
    validate_certifying_workflow_contracts(
        legacy_reference,
        root=root,
    )

    audited = require_string_array(
        manifest["auditedSurfaces"],
        "manifest auditedSurfaces",
    )
    pending = require_string_array(
        manifest["pendingAuditSurfaces"],
        "manifest pendingAuditSurfaces",
        allow_empty=True,
    )
    inventory_path = resolve_repo_file(
        manifest["legacySurfaceInventory"],
        "tests/parity",
        "legacy surface inventory",
        root=root,
    )
    require_json_suffix(inventory_path, "legacy surface inventory")
    inventory = load_json(inventory_path)
    validate_inventory(manifest, inventory)

    if legacy_root is not None:
        regenerated = build_inventory(legacy_root.resolve())
        if serialize_inventory(regenerated) != inventory_path.read_text(
            encoding="utf-8"
        ):
            raise ValueError(
                "legacy surface inventory is stale; regenerate it with "
                "tools/parity/inventory_legacy.py"
            )

    capabilities = manifest["items"]
    if not isinstance(capabilities, list) or not capabilities:
        raise ValueError("manifest items must be a non-empty array")

    required_capability = {
        "id",
        "category",
        "feature",
        "behavior",
        "legacySources",
        "legacySurfaceSelectors",
        "platforms",
        "acceptanceStatus",
        "caseIds",
    }
    capability_ids: set[str] = set()
    capabilities_by_id: dict[str, dict[str, Any]] = {}
    for capability in capabilities:
        if not isinstance(capability, dict):
            raise ValueError("each manifest item must be an object")
        require_fields(
            capability,
            required_capability,
            "manifest capability",
            allowed=required_capability,
        )
        capability_id = require_nonempty_string(
            capability["id"],
            "capability ID",
        )
        if capability_id in capability_ids:
            raise ValueError(f"duplicate capability ID: {capability_id}")
        capability_ids.add(capability_id)
        capabilities_by_id[capability_id] = capability
        validate_capability_shape(capability)
    validate_audit_state(
        manifest["inventoryStatus"],
        audited,
        pending,
        capability_ids,
    )

    obligations = manifest["behavioralObligations"]
    if not isinstance(obligations, list) or not obligations:
        raise ValueError(
            "manifest behavioralObligations must be a non-empty array"
        )
    required_obligation = {
        "id",
        "capabilityId",
        "behavior",
        "platforms",
        "sourceBindingStatus",
        "legacySources",
        "legacySurfaceSelectors",
        "acceptanceStatus",
        "caseIds",
    }
    obligation_ids: set[str] = set()
    obligations_by_id: dict[str, dict[str, Any]] = {}
    for obligation in obligations:
        if not isinstance(obligation, dict):
            raise ValueError(
                "each manifest behavioral obligation must be an object"
            )
        require_fields(
            obligation,
            required_obligation,
            "manifest behavioral obligation",
            allowed=required_obligation,
        )
        obligation_id = require_nonempty_string(
            obligation["id"],
            "behavioral obligation ID",
        )
        if obligation_id in obligation_ids:
            raise ValueError(
                f"duplicate behavioral obligation ID: {obligation_id}"
            )
        if obligation_id in capability_ids:
            raise ValueError(
                "behavioral obligation ID duplicates a capability ID: "
                f"{obligation_id}"
            )
        obligation_ids.add(obligation_id)
        obligations_by_id[obligation_id] = obligation
        validate_obligation_shape(
            obligation,
            capabilities_by_id,
        )

    cases = manifest["cases"]
    if not isinstance(cases, list):
        raise ValueError("manifest cases must be an array")
    required_case = {
        "id",
        "capabilityId",
        "obligationIds",
        "behavior",
        "legacyOracle",
        "legacySources",
        "legacySurfaceSelectors",
        "preconditions",
        "input",
        "targetAdapters",
        "assertions",
        "platforms",
        "legacyTestStatus",
        "xplatTestStatus",
        "status",
        "failureCode",
        "fixture",
        "evidence",
        "firstGreenCommit",
    }
    case_ids: set[str] = set()
    legacy_oracle_descriptors_by_version: dict[
        str,
        dict[str, Any],
    ] = {}
    cases_by_id: dict[str, dict[str, Any]] = {}
    for case in cases:
        if not isinstance(case, dict):
            raise ValueError("each manifest case must be an object")
        require_fields(
            case,
            required_case,
            "manifest case",
            allowed=required_case,
        )
        case_id = require_nonempty_string(case["id"], "case ID")
        if case_id in case_ids:
            raise ValueError(f"duplicate case ID: {case_id}")
        if case_id in capability_ids:
            raise ValueError(
                f"case ID duplicates a capability ID: {case_id}"
            )
        if case_id in obligation_ids:
            raise ValueError(
                f"case ID duplicates a behavioral obligation ID: {case_id}"
            )
        case_ids.add(case_id)
        cases_by_id[case_id] = case
        validate_case_shape(case)
        validate_legacy_oracle_descriptor(case, root=root)
        register_shared_legacy_oracle_descriptor(
            legacy_oracle_descriptors_by_version,
            case["legacyOracle"],
        )

    mappings = map_surfaces(capabilities, inventory["surfaces"])
    validate_capability_case_links(
        capabilities,
        cases,
        capabilities_by_id=capabilities_by_id,
        cases_by_id=cases_by_id,
        obligations=obligations,
        obligations_by_id=obligations_by_id,
        inventory_surface_ids=[
            surface["id"] for surface in inventory["surfaces"]
        ],
    )

    evidence_paths: dict[Path, str] = {}
    active_evidence: list[dict[str, Any]] = []
    for case in cases:
        case_id = case["id"]
        if (
            promotion_case_ids is not None
            and case_id in promotion_case_ids
        ):
            validate_fixture(
                case,
                pinned_revision,
                require_schema_v2=True,
                root=root,
            )
            candidate = (root / case["evidence"]).resolve()
            evidence_root = (
                root / "tests/parity/evidence"
            ).resolve()
            if not is_within(candidate, evidence_root):
                raise ValueError(
                    f"{case_id} evidence escapes tests/parity/evidence"
                )
            if candidate.is_file():
                evidence_paths[candidate] = case_id
            continue
        evidence_path, evidence = validate_active_case(
            case,
            pinned_revision,
            manifest=manifest,
            root=root,
        )
        if evidence_path in evidence_paths:
            raise ValueError(
                f"{case_id} reuses evidence already owned by "
                f"{evidence_paths[evidence_path]}"
            )
        evidence_paths[evidence_path] = case_id
        active_evidence.append(evidence)

    retained_evidence = validate_evidence_directory(
        evidence_paths,
        capabilities_by_id,
        case_ids,
        pinned_revision,
        root=root,
    )
    if promotion_case_ids is None:
        validate_no_orphaned_regression_gates(
            active_evidence,
            root=root,
        )
    return inventory, mappings, retained_evidence


def register_shared_legacy_oracle_descriptor(
    descriptors_by_version: dict[str, dict[str, Any]],
    descriptor: dict[str, Any],
) -> None:
    version_id = descriptor["versionId"]
    previous_descriptor = descriptors_by_version.get(version_id)
    if (
        previous_descriptor is not None
        and previous_descriptor != descriptor
    ):
        raise ValueError(
            "shared legacyOracle versionId has conflicting immutable "
            f"descriptors: {version_id}"
        )
    descriptors_by_version[version_id] = copy.deepcopy(descriptor)


def validate_no_orphaned_regression_gates(
    active_evidence: list[dict[str, Any]],
    *,
    root: Path = ROOT,
) -> None:
    gate_root = (
        root / "tests/parity/evidence/regression-gates"
    ).resolve()
    referenced: set[Path] = set()
    for evidence in active_evidence:
        reference = evidence.get("regressionGate")
        if reference is None:
            continue
        if not isinstance(reference, dict):
            raise ValueError(
                "active evidence regressionGate reference is invalid"
            )
        path_text = require_nonempty_string(
            reference.get("path"),
            "active evidence regressionGate path",
        )
        referenced.add(
            resolve_repo_file(
                path_text,
                "tests/parity/evidence/regression-gates",
                "active evidence regressionGate",
                root=root,
            )
        )
    actual = (
        {
            candidate.resolve()
            for candidate in gate_root.glob("*.json")
            if candidate.is_file()
        }
        if gate_root.is_dir()
        else set()
    )
    orphaned = sorted(actual - referenced)
    if orphaned:
        raise ValueError(
            "orphaned retained full-suite regression gates: "
            + ", ".join(
                str(path.relative_to(root))
                for path in orphaned
            )
        )


def validate_hashing_attributes(root: Path = ROOT) -> None:
    attributes_path = root / ".gitattributes"
    if not attributes_path.is_file():
        raise ValueError(".gitattributes is required for parity hashing")
    lines = {
        line.strip()
        for line in attributes_path.read_text(encoding="utf-8").splitlines()
        if line.strip() and not line.lstrip().startswith("#")
    }
    required = {
        "tests/parity/**/*.json text eol=lf",
        "tests/parity/legacy-oracle/**/*.lpr text eol=crlf",
    }
    missing = sorted(required - lines)
    if missing:
        raise ValueError(
            "missing parity hashing attributes: " + ", ".join(missing)
        )
    repository_check = run_git(
        root,
        ["rev-parse", "--is-inside-work-tree"],
        "parity hashing attributes",
    )
    if (
        repository_check.returncode != 0
        or repository_check.stdout.strip().lower() != "true"
    ):
        return
    probes = {
        "tests/parity/evidence/runs/probe.json": "lf",
        "tests/parity/legacy-oracle/v1/Probe.lpr": "crlf",
        "tests/parity/legacy-oracle/v16/LegacyOracle.lpr": "lf",
    }
    attribute_check = run_git(
        root,
        ["check-attr", "eol", "--", *probes],
        "parity hashing attributes",
    )
    if attribute_check.returncode != 0:
        raise ValueError("could not resolve parity hashing attributes")
    resolved: dict[str, str] = {}
    for line in attribute_check.stdout.splitlines():
        parts = line.split(": ", maxsplit=2)
        if len(parts) == 3 and parts[1] == "eol":
            resolved[parts[0].replace("\\", "/")] = parts[2]
    mismatched = [
        f"{path}={resolved.get(path, 'unspecified')} (expected {expected})"
        for path, expected in probes.items()
        if resolved.get(path) != expected
    ]
    if mismatched:
        raise ValueError(
            "resolved parity hashing attributes are incorrect: "
            + ", ".join(mismatched)
        )


def validate_acceptance_test_wiring(root: Path = ROOT) -> None:
    workflow_paths = (
        root / ".github/workflows/dotnet-quality.yml",
        root / ".github/workflows/release-quality.yml",
    )
    for workflow_path in workflow_paths:
        if not workflow_path.is_file():
            raise ValueError(
                "required ordinary-test workflow is missing: "
                f"{workflow_path.relative_to(root)}"
            )
        text = workflow_path.read_text(encoding="utf-8")
        command_starts = [
            match.start()
            for match in re.finditer(r"\bdotnet\s+test\b", text)
        ]
        if not command_starts:
            raise ValueError(
                f"{workflow_path.name} has no ordinary dotnet test command"
            )
        step_starts = [
            match.start()
            for match in re.finditer(r"(?m)^\s{6}- name:", text)
        ]
        for command_start in command_starts:
            command_end = min(
                (
                    step_start
                    for step_start in step_starts
                    if step_start > command_start
                ),
                default=len(text),
            )
            command_step = text[command_start:command_end]
            for category in (
                "ParityAcceptance",
                "LegacyOracleBuildIntegration",
            ):
                if not re.search(
                    r"--filter-not-trait\s+['\"]?"
                    rf"Category={category}(?:['\"]|\s|$)",
                    command_step,
                ):
                    raise ValueError(
                        f"{workflow_path.name} ordinary dotnet test "
                        f"command must exclude Category={category}"
                    )

    parity_runner_path = root / "tests/parity/Run-Parity.ps1"
    if not parity_runner_path.is_file():
        raise ValueError("dedicated parity runner is missing")
    parity_runner = parity_runner_path.read_text(encoding="utf-8")
    required_runner_patterns = (
        (
            r"--filter-trait\s+['\"]Category=ParityAcceptance['\"]",
            "filter exactly Category=ParityAcceptance",
        ),
        (
            r"--minimum-expected-tests\s+1(?:\s|$)",
            "require at least one executed acceptance test",
        ),
    )
    for pattern, requirement in required_runner_patterns:
        if not re.search(pattern, parity_runner):
            raise ValueError(
                "dedicated parity runner must " + requirement
            )


def load_workflow_document(path: Path) -> dict[str, Any]:
    if not path.is_file():
        raise ValueError(
            f"required certifying workflow is missing: {path.name}"
        )
    try:
        document = yaml.safe_load(path.read_text(encoding="utf-8"))
    except yaml.YAMLError as error:
        raise ValueError(
            f"certifying workflow is invalid YAML: {path.name}"
        ) from error
    if not isinstance(document, dict):
        raise ValueError(
            f"certifying workflow root must be an object: {path.name}"
        )
    jobs = document.get("jobs")
    if not isinstance(jobs, dict):
        raise ValueError(
            f"certifying workflow has no jobs: {path.name}"
        )
    return document


def workflow_job(
    workflow: dict[str, Any],
    job_id: str,
    workflow_name: str,
) -> dict[str, Any]:
    job = workflow["jobs"].get(job_id)
    if not isinstance(job, dict):
        raise ValueError(
            f"{workflow_name} is missing required job {job_id}"
        )
    steps = job.get("steps")
    if not isinstance(steps, list) or not steps:
        raise ValueError(
            f"{workflow_name} job {job_id} has no steps"
        )
    if any(not isinstance(step, dict) for step in steps):
        raise ValueError(
            f"{workflow_name} job {job_id} has an invalid step"
        )
    return job


def workflow_run_text(job: dict[str, Any]) -> str:
    return "\n".join(
        str(step.get("run", ""))
        for step in job["steps"]
    )


def require_workflow_command(
    job: dict[str, Any],
    workflow_name: str,
    *,
    target: str,
    mode: str,
) -> None:
    matching = [
        str(step.get("run"))
        for step in job["steps"]
        if isinstance(step.get("run"), str)
        and re.search(
            r"(?im)^\s*(?:&\s*)?\.?[\\/]"
            r".*Run-Parity\.ps1(?:\s|$)",
            step["run"],
        )
    ]
    if len(matching) != 1:
        raise ValueError(
            f"{workflow_name} must run Run-Parity.ps1 exactly once"
        )
    command = matching[0]
    for pattern, label in (
        (
            rf"(?i)-Target\s+{re.escape(target)}(?:\s|`|$)",
            f"Target {target}",
        ),
        (
            rf"(?i)-Mode\s+{re.escape(mode)}(?:\s|`|$)",
            f"Mode {mode}",
        ),
    ):
        if not re.search(pattern, command):
            raise ValueError(
                f"{workflow_name} parity command must use {label}"
            )


def require_pinned_legacy_checkout(
    job: dict[str, Any],
    workflow_name: str,
    public_base_revision: str,
) -> None:
    checkouts = [
        step
        for step in job["steps"]
        if str(step.get("uses", "")).startswith("actions/checkout@")
    ]
    primary = [
        step
        for step in checkouts
        if not isinstance(step.get("with"), dict)
        or "repository" not in step["with"]
    ]
    if len(primary) != 1 or primary[0].get("with", {}).get(
        "fetch-depth"
    ) != 0:
        raise ValueError(
            f"{workflow_name} must checkout XPlat with full Git history"
        )
    legacy = [
        step
        for step in checkouts
        if isinstance(step.get("with"), dict)
        and step["with"].get("repository") == "w7sst/MorseRunner"
    ]
    if len(legacy) != 1:
        raise ValueError(
            f"{workflow_name} must checkout the public CE repository"
        )
    settings = legacy[0]["with"]
    if (
        settings.get("ref") != public_base_revision
        or settings.get("path")
        != "artifacts/legacy-source/MorseRunner"
        or settings.get("persist-credentials") is not False
    ):
        raise ValueError(
            f"{workflow_name} CE checkout is not hash-pinned"
        )


def require_certifying_upload(
    job: dict[str, Any],
    workflow_name: str,
    required_paths: set[str],
) -> None:
    uploads = [
        step
        for step in job["steps"]
        if str(step.get("uses", "")).startswith(
            "actions/upload-artifact@"
        )
    ]
    matching: list[dict[str, Any]] = []
    for upload in uploads:
        settings = upload.get("with")
        if not isinstance(settings, dict):
            continue
        paths = {
            line.strip().replace("\\", "/")
            for line in str(settings.get("path", "")).splitlines()
            if line.strip()
        }
        if required_paths.issubset(paths):
            matching.append(upload)
    if len(matching) != 1:
        raise ValueError(
            f"{workflow_name} must upload result, TRX, and execution "
            "envelope artifacts together"
        )
    upload = matching[0]
    condition = str(upload.get("if", "")).strip()
    if (
        "success()" not in condition
        or any(
            unsafe in condition
            for unsafe in ("always()", "failure()", "cancelled()")
        )
        or upload["with"].get("if-no-files-found") != "error"
    ):
        raise ValueError(
            f"{workflow_name} certifying artifact upload must require "
            "a successful producer and all files"
        )


def validate_certifying_workflow_contracts(
    reference: dict[str, Any],
    *,
    root: Path = ROOT,
) -> None:
    workflows_root = root / ".github/workflows"
    parity_workflow = load_workflow_document(
        workflows_root / "parity-quality.yml"
    )
    parity_job = workflow_job(
        parity_workflow,
        "parity",
        "parity-quality.yml",
    )
    if parity_job.get("runs-on") != "windows-latest":
        raise ValueError(
            "parity-quality.yml certifying job must run on Windows"
        )
    public_base = require_nonempty_string(
        reference.get("publicBaseRevision"),
        "legacy reference publicBaseRevision",
    )
    if not COMMIT_PATTERN.fullmatch(public_base):
        raise ValueError(
            "legacy reference publicBaseRevision must be a full commit ID"
        )
    require_pinned_legacy_checkout(
        parity_job,
        "parity-quality.yml",
        public_base,
    )
    require_workflow_command(
        parity_job,
        "parity-quality.yml",
        target="Both",
        mode="PullRequest",
    )
    parity_run = workflow_run_text(parity_job)
    for token in (
        HISTORY_KIND_ENVIRONMENT_VARIABLE,
        HISTORY_BASE_REVISION_ENVIRONMENT_VARIABLE,
    ):
        if token not in parity_run:
            raise ValueError(
                "parity-quality.yml must provide authenticated full-history "
                f"context via {token}"
            )
    parity_run = workflow_run_text(parity_job)
    for token in (
        "New-ParityBothArtifactPackage.ps1",
        "artifacts/parity-package-staging/live-both",
        "package_index",
        "package_index_sha256",
    ):
        if token not in parity_run:
            raise ValueError(
                "parity-quality.yml must stage a content-addressed "
                "complete Both-target package before upload"
            )
    require_certifying_upload(
        parity_job,
        "parity-quality.yml",
        {
            "${{ steps.live-parity-artifacts.outputs.package_root }}",
        },
    )

    dotnet_workflow = load_workflow_document(
        workflows_root / "dotnet-quality.yml"
    )
    quality_job = workflow_job(
        dotnet_workflow,
        "quality",
        "dotnet-quality.yml",
    )
    matrix = quality_job.get("strategy", {}).get("matrix", {})
    os_matrix = matrix.get("os") if isinstance(matrix, dict) else None
    if (
        not isinstance(os_matrix, list)
        or set(os_matrix)
        != {"windows-latest", "ubuntu-latest", "macos-latest"}
        or quality_job.get("runs-on") != "${{ matrix.os }}"
    ):
        raise ValueError(
            "dotnet-quality.yml native parity matrix must cover exactly "
            "Windows, Linux, and macOS"
        )
    require_workflow_command(
        quality_job,
        "dotnet-quality.yml",
        target="XPlat",
        mode="Development",
    )
    quality_run = workflow_run_text(quality_job)
    for token in (
        "$applicable.id -ccontains",
        "-CaptureGreenCaseId",
        "-GreenRegressionCaseId",
        "-Mode Baseline",
        "-Mode Development",
        "$platformCaptureIds.Count -eq 0",
        "artifacts/parity-full-suite",
        "Copy-Item",
    ):
        if token not in quality_run:
            raise ValueError(
                "dotnet-quality.yml green capture must preserve a distinct "
                "full-suite Development regression gate before selected "
                "Baseline capture, intersect requested IDs with "
                "platform-applicable cases, and skip an empty intersection"
            )
    require_certifying_upload(
        quality_job,
        "dotnet-quality.yml",
        {
            "artifacts/parity/xplat.json",
            "artifacts/parity/test-results/xplat.trx",
            "artifacts/parity/executions/xplat/*.json",
            "artifacts/parity-full-suite/${{ steps.native-parity.outputs.platform }}/xplat.json",
            "artifacts/parity-full-suite/${{ steps.native-parity.outputs.platform }}/xplat.trx",
            "artifacts/parity-full-suite/${{ steps.native-parity.outputs.platform }}/execution/*.json",
        },
    )
    windows_gate_job = workflow_job(
        dotnet_workflow,
        "green-regression-windows-both",
        "dotnet-quality.yml",
    )
    if windows_gate_job.get("runs-on") != "windows-latest":
        raise ValueError(
            "dotnet-quality.yml complete Both regression gate must run "
            "on Windows"
        )
    require_pinned_legacy_checkout(
        windows_gate_job,
        "dotnet-quality.yml green-regression-windows-both",
        public_base,
    )
    require_workflow_command(
        windows_gate_job,
        "dotnet-quality.yml green-regression-windows-both",
        target="Both",
        mode="Development",
    )
    windows_gate_run = workflow_run_text(windows_gate_job)
    for token in (
        "GreenRegressionCaseId",
        "capture_green_case_ids",
        "legacy-green-xplat-red",
        "New-ParityBothArtifactPackage.ps1",
        "artifacts/parity-package-staging/windows-both",
        "registry_sha256",
        "package_index",
        "package_index_sha256",
    ):
        if token not in windows_gate_run:
            raise ValueError(
                "dotnet-quality.yml Windows Both regression gate must "
                "bind selected manifest-red IDs and stage the complete "
                "content-addressed package"
            )
    package_script_path = (
        root / "tests/parity/New-ParityBothArtifactPackage.ps1"
    )
    assertion_script_path = (
        root / "tests/parity/Assert-ParityFullSuitePackage.ps1"
    )
    if (
        not package_script_path.is_file()
        or not assertion_script_path.is_file()
    ):
        raise ValueError(
            "Windows Both packaging scripts are missing"
        )
    package_script = package_script_path.read_text(encoding="utf-8")
    assertion_script = assertion_script_path.read_text(encoding="utf-8")
    for token in (
        "Assert-LegacyOracleRegistryArtifactSet.ps1",
        "Assert-ParityFullSuitePackage.ps1",
        "Copy-RepositoryArtifact",
        "RequireExactClosure",
    ):
        if token not in package_script:
            raise ValueError(
                "Windows Both package producer does not prove the exact "
                "registry and artifact closure"
            )
    for token in (
        "IndexSha256",
        "registry",
        "files",
        "RequireExactClosure",
    ):
        if token not in assertion_script:
            raise ValueError(
                "Windows Both package consumer does not validate the "
                "content-addressed closure"
            )
    require_certifying_upload(
        windows_gate_job,
        "dotnet-quality.yml green-regression-windows-both",
        {
            "${{ steps.legacy-oracle-artifacts.outputs.package_root }}",
        },
    )

    release_quality = load_workflow_document(
        workflows_root / "release-quality.yml"
    )
    release_job = workflow_job(
        release_quality,
        "validate",
        "release-quality.yml",
    )
    require_pinned_legacy_checkout(
        release_job,
        "release-quality.yml",
        public_base,
    )
    require_workflow_command(
        release_job,
        "release-quality.yml",
        target="Both",
        mode="Release",
    )
    release_run = workflow_run_text(release_job)
    for token in (
        "New-ParityBothArtifactPackage.ps1",
        "artifacts/parity-package-staging/release-both",
        "package_root",
        "package_index_sha256",
    ):
        if token not in release_run:
            raise ValueError(
                "release-quality.yml must stage the complete "
                "content-addressed Both-target package"
            )
    require_certifying_upload(
        release_job,
        "release-quality.yml",
        {
            "${{ steps.release-parity-artifacts.outputs.package_root }}",
        },
    )
    if r".\tools\release\Publish-Release.ps1" not in release_run:
        raise ValueError(
            "release-quality.yml must publish runtime packages after "
            "the certifying parity gate"
        )
    runtime_package_uploads = [
        step
        for step in release_job["steps"]
        if str(step.get("uses", "")).startswith(
            "actions/upload-artifact@"
        )
        and step.get("with", {}).get("name")
        == "MorseRunnerXPlat-runtime-packages"
    ]
    if len(runtime_package_uploads) != 1:
        raise ValueError(
            "release-quality.yml must retain one runtime package upload"
        )
    runtime_package_upload = runtime_package_uploads[0]
    runtime_package_paths = {
        line.strip().replace("\\", "/")
        for line in str(
            runtime_package_upload.get("with", {}).get("path", "")
        ).splitlines()
        if line.strip()
    }
    runtime_package_condition = str(
        runtime_package_upload.get("if", "")
    )
    if (
        not {
            "artifacts/release/*.zip",
            "artifacts/release/*.tar.gz",
        }.issubset(runtime_package_paths)
        or runtime_package_upload["with"].get("if-no-files-found")
        != "error"
        or any(
            unsafe in runtime_package_condition
            for unsafe in ("always()", "failure()", "cancelled()")
        )
    ):
        raise ValueError(
            "release-quality.yml runtime package upload must retain "
            "successful zip and tar output with error-on-missing"
        )

    release_evidence = load_workflow_document(
        workflows_root / "release-evidence.yml"
    )
    release_triggers = release_evidence.get(True, {})
    pull_request = (
        release_triggers.get("pull_request", {})
        if isinstance(release_triggers, dict)
        else {}
    )
    pull_request_paths = pull_request.get("paths", [])
    required_release_paths = {
        "proto/**",
        "src/**",
        "tests/**",
        "tools/parity/**",
        "tools/release/**",
        "LICENSE",
        "README.md",
        "THIRD-PARTY-NOTICES.md",
        ".github/workflows/release-evidence.yml",
        ".github/workflows/release-quality.yml",
    }
    if (
        not isinstance(pull_request_paths, list)
        or not required_release_paths.issubset(
            set(pull_request_paths)
        )
    ):
        raise ValueError(
            "release-evidence.yml pull-request paths omit a native "
            "release input"
        )
    live_job = workflow_job(
        release_evidence,
        "live-parity",
        "release-evidence.yml",
    )
    require_pinned_legacy_checkout(
        live_job,
        "release-evidence.yml",
        public_base,
    )
    release_evidence_run = workflow_run_text(live_job)
    for token in (
        "New-ParityBothArtifactPackage.ps1",
        "artifacts/parity-package-staging/native-evidence-both",
        "package_root",
        "package_index_sha256",
    ):
        if token not in release_evidence_run:
            raise ValueError(
                "release-evidence.yml must stage the complete "
                "content-addressed Both-target package"
            )
    require_certifying_upload(
        live_job,
        "release-evidence.yml",
        {
            "${{ steps.live-parity-artifacts.outputs.package_root }}",
        },
    )
    native_job = workflow_job(
        release_evidence,
        "native-evidence",
        "release-evidence.yml",
    )
    needs = native_job.get("needs")
    needs_set = (
        {needs}
        if isinstance(needs, str)
        else set(needs)
        if isinstance(needs, list)
        else set()
    )
    if "live-parity" not in needs_set:
        raise ValueError(
            "release-evidence.yml native publication must depend on "
            "live parity"
        )
    include = native_job.get("strategy", {}).get("matrix", {}).get(
        "include"
    )
    if not isinstance(include, list):
        raise ValueError(
            "release-evidence.yml has no native release matrix"
        )
    native_rids = {
        entry.get("rid")
        for entry in include
        if isinstance(entry, dict)
    }
    if not {"win-x64", "linux-x64", "osx-x64"}.issubset(native_rids):
        raise ValueError(
            "release-evidence.yml native publication does not cover "
            "Windows, Linux, and macOS"
        )
    require_workflow_command(
        native_job,
        "release-evidence.yml native-evidence",
        target="XPlat",
        mode="Development",
    )
    native_run = workflow_run_text(native_job)
    if "$applicable.Count -eq 0" not in native_run:
        raise ValueError(
            "release-evidence.yml native parity must skip platforms with "
            "no applicable cases"
        )
    for token in (
        r".\tools\release\Publish-Release.ps1",
        r".\tools\release\Test-ReleaseArchive.ps1",
    ):
        if token not in native_run:
            raise ValueError(
                "release-evidence.yml native job must publish and test "
                "each runtime archive before upload"
            )
    require_certifying_upload(
        native_job,
        "release-evidence.yml native-evidence",
        {
            "artifacts/parity/xplat.json",
            "artifacts/parity/test-results/xplat.trx",
            "artifacts/parity/executions/xplat/*.json",
        },
    )
    tested_archive_uploads = [
        step
        for step in native_job["steps"]
        if str(step.get("uses", "")).startswith(
            "actions/upload-artifact@"
        )
        and "artifacts/release/" in str(
            step.get("with", {}).get("path", "")
        )
    ]
    if len(tested_archive_uploads) != 1:
        raise ValueError(
            "release-evidence.yml must publish tested archives only from "
            "the native evidence job"
        )
    tested_archive_upload = tested_archive_uploads[0]
    tested_archive_condition = str(
        tested_archive_upload.get("if", "")
    )
    if (
        tested_archive_upload.get("with", {}).get(
            "if-no-files-found"
        )
        != "error"
        or any(
            unsafe in tested_archive_condition
            for unsafe in ("always()", "failure()", "cancelled()")
        )
    ):
        raise ValueError(
            "release-evidence.yml tested archive upload must require "
            "successful production and error on missing output"
        )
    diagnostic_uploads = []
    for step in native_job["steps"]:
        if not str(step.get("uses", "")).startswith(
            "actions/upload-artifact@"
        ):
            continue
        settings = step.get("with")
        if not isinstance(settings, dict):
            continue
        paths = {
            line.strip().replace("\\", "/")
            for line in str(settings.get("path", "")).splitlines()
            if line.strip()
        }
        if {
            "artifacts/evidence/${{ matrix.rid }}",
            "artifacts/visual/${{ matrix.rid }}",
        }.issubset(paths):
            diagnostic_uploads.append(step)
    if len(diagnostic_uploads) != 1:
        raise ValueError(
            "release-evidence.yml must retain one native diagnostic "
            "evidence upload"
        )
    diagnostic_upload = diagnostic_uploads[0]
    diagnostic_condition = str(
        diagnostic_upload.get("if", "")
    )
    diagnostic_paths = str(
        diagnostic_upload.get("with", {}).get("path", "")
    )
    if (
        "always()" not in diagnostic_condition
        or diagnostic_upload["with"].get("if-no-files-found")
        != "warn"
        or "artifacts/parity/" in diagnostic_paths
    ):
        raise ValueError(
            "release-evidence.yml native diagnostics must be retained "
            "on failure without mixing certifying parity artifacts"
        )


def validate_audit_state(
    inventory_status: str,
    audited: list[str],
    pending: list[str],
    capability_ids: set[str],
) -> None:
    if len(audited) != len(set(audited)):
        raise ValueError("manifest auditedSurfaces contains duplicates")
    if len(pending) != len(set(pending)):
        raise ValueError("manifest pendingAuditSurfaces contains duplicates")
    overlap = sorted(set(audited) & set(pending))
    if overlap:
        raise ValueError(
            "manifest auditedSurfaces and pendingAuditSurfaces overlap: "
            + ", ".join(overlap)
        )
    expected_inventory_status = "complete" if not pending else "in-progress"
    if inventory_status != expected_inventory_status:
        raise ValueError(
            "manifest inventoryStatus does not match pendingAuditSurfaces"
        )
    classified_audit_ids = set(audited) | set(pending)
    if classified_audit_ids != capability_ids:
        missing_audits = sorted(capability_ids - classified_audit_ids)
        unknown_audits = sorted(classified_audit_ids - capability_ids)
        details: list[str] = []
        if missing_audits:
            details.append("missing " + ", ".join(missing_audits))
        if unknown_audits:
            details.append("unknown " + ", ".join(unknown_audits))
        raise ValueError(
            "manifest audit buckets must be exact capability IDs: "
            + "; ".join(details)
        )


def validate_capability_shape(capability: dict[str, Any]) -> None:
    capability_id = capability["id"]
    for key in ("category", "feature", "behavior"):
        require_nonempty_string(
            capability[key],
            f"{capability_id} {key}",
        )
    require_string_array(
        capability["legacySources"],
        f"{capability_id} legacySources",
    )
    require_string_array(
        capability["legacySurfaceSelectors"],
        f"{capability_id} legacySurfaceSelectors",
    )
    platforms = require_string_array(
        capability["platforms"],
        f"{capability_id} platforms",
    )
    validate_platforms(platforms, capability_id)
    acceptance_status = capability["acceptanceStatus"]
    if acceptance_status not in CAPABILITY_STATUSES:
        raise ValueError(
            f"{capability_id} has invalid acceptanceStatus: "
            f"{acceptance_status}"
        )
    case_ids = require_string_array(
        capability["caseIds"],
        f"{capability_id} caseIds",
        allow_empty=True,
    )
    if len(case_ids) != len(set(case_ids)):
        raise ValueError(
            f"{capability_id} caseIds contains duplicates"
        )


def validate_obligation_shape(
    obligation: dict[str, Any],
    capabilities_by_id: dict[str, dict[str, Any]],
) -> None:
    obligation_id = obligation["id"]
    capability_id = require_nonempty_string(
        obligation["capabilityId"],
        f"{obligation_id} capabilityId",
    )
    capability = capabilities_by_id.get(capability_id)
    if capability is None:
        raise ValueError(
            f"{obligation_id} references unknown capability: "
            f"{capability_id}"
        )
    require_nonempty_string(
        obligation["behavior"],
        f"{obligation_id} behavior",
    )
    platforms = require_string_array(
        obligation["platforms"],
        f"{obligation_id} platforms",
    )
    validate_platforms(platforms, obligation_id)
    outside_platforms = sorted(
        set(platforms) - set(capability["platforms"])
    )
    if outside_platforms:
        raise ValueError(
            f"{obligation_id} platforms exceed its capability: "
            + ", ".join(outside_platforms)
        )
    expected_platforms = (
        ["windows"]
        if obligation_id in WINDOWS_ONLY_OBLIGATION_IDS
        else capability["platforms"]
    )
    if platforms != expected_platforms:
        raise ValueError(
            f"{obligation_id} must declare its full portable platform "
            "contract; the Windows-only CE oracle does not make XPlat "
            "behavior Windows-only"
        )
    status = obligation["acceptanceStatus"]
    if status not in OBLIGATION_STATUSES:
        raise ValueError(
            f"{obligation_id} has invalid acceptanceStatus: {status}"
        )
    case_ids = require_string_array(
        obligation["caseIds"],
        f"{obligation_id} caseIds",
        allow_empty=True,
    )
    if len(case_ids) != len(set(case_ids)):
        raise ValueError(
            f"{obligation_id} caseIds contains duplicates"
        )
    binding_status = obligation["sourceBindingStatus"]
    if binding_status not in OBLIGATION_SOURCE_BINDING_STATUSES:
        raise ValueError(
            f"{obligation_id} has invalid sourceBindingStatus: "
            f"{binding_status}"
        )
    legacy_sources = require_string_array(
        obligation["legacySources"],
        f"{obligation_id} legacySources",
        allow_empty=True,
    )
    selectors = require_string_array(
        obligation["legacySurfaceSelectors"],
        f"{obligation_id} legacySurfaceSelectors",
        allow_empty=True,
    )
    if binding_status == "pending":
        if legacy_sources or selectors or case_ids or status != "not-authored":
            raise ValueError(
                f"{obligation_id} pending source binding must be empty, "
                "not-authored, and noncertifying"
            )
    elif not legacy_sources or not selectors or not case_ids:
        raise ValueError(
            f"{obligation_id} bound source binding requires sources, "
            "selectors, and acceptance cases"
        )


def validate_case_shape(case: dict[str, Any]) -> None:
    case_id = case["id"]
    require_nonempty_string(
        case["capabilityId"],
        f"{case_id} capabilityId",
    )
    obligation_ids = require_string_array(
        case["obligationIds"],
        f"{case_id} obligationIds",
        allow_empty=True,
    )
    if len(obligation_ids) != 1:
        raise ValueError(
            f"{case_id} must link exactly one behavioral obligation"
        )
    require_nonempty_string(case["behavior"], f"{case_id} behavior")
    if not isinstance(case["legacyOracle"], dict):
        raise ValueError(f"{case_id} legacyOracle must be an object")
    require_string_array(
        case["legacySources"],
        f"{case_id} legacySources",
    )
    require_string_array(
        case["legacySurfaceSelectors"],
        f"{case_id} legacySurfaceSelectors",
    )
    require_string_array(
        case["preconditions"],
        f"{case_id} preconditions",
    )
    adapters = require_string_array(
        case["targetAdapters"],
        f"{case_id} targetAdapters",
    )
    if len(adapters) != 2 or len(set(adapters)) != 2:
        raise ValueError(
            f"{case_id} targetAdapters must contain one unique Legacy "
            "adapter followed by one unique XPlat adapter"
        )
    platforms = require_string_array(
        case["platforms"],
        f"{case_id} platforms",
    )
    validate_platforms(platforms, case_id)
    if not isinstance(case["input"], dict):
        raise ValueError(f"{case_id} input must be an object")
    if not isinstance(case["assertions"], dict) or not case["assertions"]:
        raise ValueError(f"{case_id} assertions must be a non-empty object")
    divergence_code = require_nonempty_string(
        case["assertions"].get("functionalDivergenceCode"),
        f"{case_id} assertions functionalDivergenceCode",
    )

    if case["legacyTestStatus"] != "pass":
        raise ValueError(f"{case_id} is not legacy-green")
    status = case["status"]
    if status not in CASE_STATUSES:
        raise ValueError(f"{case_id} has invalid status: {status}")
    expected_xplat_status = (
        "pass" if status == "both-green" else "fail"
    )
    if case["xplatTestStatus"] != expected_xplat_status:
        raise ValueError(
            f"{case_id} XPlat status does not match case status"
        )
    if status == "legacy-green-xplat-red":
        require_nonempty_string(
            case["failureCode"],
            f"{case_id} failureCode",
        )
        if case["failureCode"] != divergence_code:
            raise ValueError(
                f"{case_id} failureCode does not match its immutable "
                "functionalDivergenceCode assertion"
            )
        if case["firstGreenCommit"] is not None:
            raise ValueError(
                f"{case_id} red status must not claim a first-green commit"
            )
    else:
        if case["failureCode"] is not None:
            raise ValueError(
                f"{case_id} both-green status must not claim a failure"
            )
    require_nonempty_string(case["fixture"], f"{case_id} fixture")
    require_nonempty_string(case["evidence"], f"{case_id} evidence")


def validate_legacy_oracle_descriptor_location(
    version_id_value: Any,
    source_value: Any,
    build_recipe_value: Any,
    label: str,
) -> tuple[str, str, str]:
    version_id = require_nonempty_string(
        version_id_value,
        f"{label} versionId",
    )
    if not LEGACY_ORACLE_VERSION_PATTERN.fullmatch(version_id):
        raise ValueError(
            f"{label} versionId must be a stable versioned ID"
        )
    version_match = re.search(r"-v([1-9][0-9]*)$", version_id)
    assert version_match is not None
    version_prefix = (
        "tests/parity/legacy-oracle/"
        f"v{version_match.group(1)}/"
    )
    source_relative = require_canonical_repo_relative_path(
        source_value,
        f"{label} source",
    )
    if not source_relative.startswith(version_prefix):
        raise ValueError(
            f"{label} source must be under the exact version directory "
            f"{version_prefix.removesuffix('/')}"
        )
    build_recipe_relative = require_canonical_repo_relative_path(
        build_recipe_value,
        f"{label} buildRecipe",
    )
    if not build_recipe_relative.startswith(version_prefix):
        raise ValueError(
            f"{label} buildRecipe must be under the exact version "
            f"directory {version_prefix.removesuffix('/')}"
        )
    if Path(build_recipe_relative).suffix.lower() != ".json":
        raise ValueError(f"{label} buildRecipe must be a JSON file")
    return version_id, source_relative, build_recipe_relative


def validate_legacy_oracle_descriptor_vectors(
    *,
    root: Path = ROOT,
) -> None:
    vectors_path = resolve_repo_file(
        LEGACY_ORACLE_DESCRIPTOR_VECTOR_PATH,
        "tests/parity",
        "legacy oracle descriptor vectors",
        root=root,
    )
    if (
        sha256_file(vectors_path)
        != LEGACY_ORACLE_DESCRIPTOR_VECTOR_SHA256
    ):
        raise ValueError(
            "legacy oracle descriptor vectors do not match the reviewed "
            "shared contract"
        )
    document = load_json(vectors_path)
    fields = {"schemaVersion", "vectors"}
    require_fields(
        document,
        fields,
        "legacy oracle descriptor vectors",
        allowed=fields,
    )
    if document["schemaVersion"] != 1:
        raise ValueError(
            "legacy oracle descriptor vectors have unsupported schema"
        )
    vectors = document["vectors"]
    if not isinstance(vectors, list) or not vectors:
        raise ValueError(
            "legacy oracle descriptor vectors must be a non-empty array"
        )
    vector_fields = {
        "id",
        "valid",
        "versionId",
        "source",
        "buildRecipe",
    }
    seen_ids: set[str] = set()
    for vector in vectors:
        if not isinstance(vector, dict):
            raise ValueError(
                "legacy oracle descriptor vector must be an object"
            )
        require_fields(
            vector,
            vector_fields,
            "legacy oracle descriptor vector",
            allowed=vector_fields,
        )
        vector_id = require_nonempty_string(
            vector["id"],
            "legacy oracle descriptor vector ID",
        )
        if vector_id in seen_ids:
            raise ValueError(
                f"duplicate legacy oracle descriptor vector ID: {vector_id}"
            )
        seen_ids.add(vector_id)
        if not isinstance(vector["valid"], bool):
            raise ValueError(
                f"{vector_id} descriptor vector valid must be Boolean"
            )
        try:
            validate_legacy_oracle_descriptor_location(
                vector["versionId"],
                vector["source"],
                vector["buildRecipe"],
                f"{vector_id} descriptor vector",
            )
        except ValueError:
            accepted = False
        else:
            accepted = True
        if accepted is not vector["valid"]:
            raise ValueError(
                f"{vector_id} descriptor vector expectation does not "
                "match the Python validator"
            )


def validate_legacy_oracle_descriptor(
    case: dict[str, Any],
    *,
    root: Path = ROOT,
) -> None:
    case_id = case["id"]
    descriptor = case["legacyOracle"]
    if not isinstance(descriptor, dict):
        raise ValueError(f"{case_id} legacyOracle must be an object")
    fields = {
        "adapterId",
        "versionId",
        "source",
        "sourceSha256",
        "buildRecipe",
        "buildRecipeSha256",
    }
    require_fields(
        descriptor,
        fields,
        f"{case_id} legacyOracle",
        allowed=fields,
    )
    adapter_id = require_nonempty_string(
        descriptor["adapterId"],
        f"{case_id} legacyOracle adapterId",
    )
    if adapter_id != case["targetAdapters"][0]:
        raise ValueError(
            f"{case_id} legacyOracle adapterId does not match its Legacy "
            "target adapter"
        )
    (
        version_id,
        source_relative,
        build_recipe_relative,
    ) = validate_legacy_oracle_descriptor_location(
        descriptor["versionId"],
        descriptor["source"],
        descriptor["buildRecipe"],
        f"{case_id} legacyOracle",
    )
    source_path = resolve_repo_file(
        source_relative,
        "tests/parity/legacy-oracle",
        f"{case_id} legacyOracle source",
        root=root,
    )
    source_hash = validate_sha256(
        descriptor["sourceSha256"],
        f"{case_id} legacyOracle sourceSha256",
    )
    if sha256_file(source_path) != source_hash.lower():
        raise ValueError(
            f"{case_id} legacyOracle sourceSha256 does not match its source"
        )
    build_recipe_path = resolve_repo_file(
        build_recipe_relative,
        "tests/parity/legacy-oracle",
        f"{case_id} legacyOracle buildRecipe",
        root=root,
    )
    require_json_suffix(
        build_recipe_path,
        f"{case_id} legacyOracle buildRecipe",
    )
    build_recipe_hash = validate_sha256(
        descriptor["buildRecipeSha256"],
        f"{case_id} legacyOracle buildRecipeSha256",
    )
    if sha256_file(build_recipe_path) != build_recipe_hash.lower():
        raise ValueError(
            f"{case_id} legacyOracle buildRecipeSha256 does not match its "
            "build recipe"
        )
    recipe = load_json(build_recipe_path)
    recipe_fields = {
        "schemaVersion",
        "adapterId",
        "versionId",
        "sourceClosure",
        "invocation",
    }
    require_fields(
        recipe,
        recipe_fields,
        f"{case_id} legacyOracle build recipe",
        allowed=recipe_fields,
    )
    if recipe["schemaVersion"] != 1:
        raise ValueError(
            f"{case_id} legacyOracle build recipe has unsupported schema"
        )
    if (
        recipe["adapterId"] != adapter_id
        or recipe["versionId"] != version_id
    ):
        raise ValueError(
            f"{case_id} legacyOracle build recipe identity does not match "
            "the case descriptor"
        )
    source_closure = recipe["sourceClosure"]
    if not isinstance(source_closure, dict):
        raise ValueError(
            f"{case_id} legacyOracle sourceClosure must be an object"
        )
    source_closure_fields = {
        "oracleSource",
        "oracleSourceSha256",
        "legacyRevision",
        "legacyTree",
        "legacyBundleSha256",
        "toolchainFingerprintSha256",
    }
    require_fields(
        source_closure,
        source_closure_fields,
        f"{case_id} legacyOracle sourceClosure",
        allowed=source_closure_fields,
    )
    legacy_reference, _ = load_legacy_reference(
        source_closure["legacyRevision"],
        root=root,
    )
    expected_closure = {
        "oracleSource": descriptor["source"],
        "oracleSourceSha256": descriptor["sourceSha256"],
        "legacyRevision": legacy_reference["revision"],
        "legacyTree": legacy_reference["tree"],
        "legacyBundleSha256": legacy_reference["bundleSha256"],
        "toolchainFingerprintSha256": legacy_reference["toolchain"][
            "fingerprint"
        ]["aggregateSha256"],
    }
    if source_closure != expected_closure:
        raise ValueError(
            f"{case_id} legacyOracle source closure is not pinned"
        )
    invocation = recipe["invocation"]
    if not isinstance(invocation, dict):
        raise ValueError(
            f"{case_id} legacyOracle invocation must be an object"
        )
    invocation_fields = {"compiler", "arguments"}
    require_fields(
        invocation,
        invocation_fields,
        f"{case_id} legacyOracle invocation",
        allowed=invocation_fields,
    )
    require_nonempty_string(
        invocation["compiler"],
        f"{case_id} legacyOracle invocation compiler",
    )
    arguments = require_string_array(
        invocation["arguments"],
        f"{case_id} legacyOracle invocation arguments",
    )
    if "{source}" not in arguments:
        raise ValueError(
            f"{case_id} legacyOracle invocation does not compile its "
            "declared source"
        )


def validate_platforms(platforms: list[str], owner_id: str) -> None:
    if len(platforms) != len(set(platforms)):
        raise ValueError(f"{owner_id} platforms contains duplicates")
    unsupported_platforms = sorted(
        set(platforms) - SUPPORTED_PLATFORMS
    )
    if unsupported_platforms:
        raise ValueError(
            f"{owner_id} has unsupported platforms: "
            f"{', '.join(unsupported_platforms)}"
        )


def validate_capability_case_links(
    capabilities: list[dict[str, Any]],
    cases: list[dict[str, Any]],
    *,
    capabilities_by_id: dict[str, dict[str, Any]] | None = None,
    cases_by_id: dict[str, dict[str, Any]] | None = None,
    obligations: list[dict[str, Any]] | None = None,
    obligations_by_id: dict[str, dict[str, Any]] | None = None,
    inventory_surface_ids: list[str] | None = None,
) -> None:
    if capabilities_by_id is None:
        capabilities_by_id = {
            capability["id"]: capability for capability in capabilities
        }
    if cases_by_id is None:
        cases_by_id = {case["id"]: case for case in cases}
    obligation_completion: dict[str, bool] | None = None
    if obligations is not None:
        if obligations_by_id is None:
            obligations_by_id = {
                obligation["id"]: obligation
                for obligation in obligations
            }
        obligation_completion = validate_obligation_case_links(
            capabilities,
            cases,
            obligations,
            capabilities_by_id,
            cases_by_id,
            obligations_by_id,
            inventory_surface_ids=inventory_surface_ids,
        )

    referenced_case_ids: set[str] = set()
    capability_surface_ids: dict[str, set[str]] = {}
    surface_owner_by_id: dict[str, str] = {}
    if inventory_surface_ids is not None:
        for capability in capabilities:
            selected = {
                surface_id
                for surface_id in inventory_surface_ids
                if any(
                    fnmatch.fnmatchcase(surface_id, selector)
                    for selector in capability["legacySurfaceSelectors"]
                )
            }
            capability_surface_ids[capability["id"]] = selected
            for surface_id in selected:
                previous_owner = surface_owner_by_id.setdefault(
                    surface_id,
                    capability["id"],
                )
                if previous_owner != capability["id"]:
                    raise ValueError(
                        f"legacy surface {surface_id} has multiple "
                        "capability owners"
                    )
        missing_surface_owners = sorted(
            set(inventory_surface_ids) - surface_owner_by_id.keys()
        )
        if missing_surface_owners:
            raise ValueError(
                "legacy surfaces have no capability owner: "
                + ", ".join(missing_surface_owners[:10])
            )

    green_surface_platforms_by_capability: dict[
        str,
        set[tuple[str, str]],
    ] = defaultdict(set)
    if inventory_surface_ids is not None:
        for case in cases:
            if case["status"] != "both-green":
                continue
            case_surfaces = resolve_case_surfaces(
                case,
                inventory_surface_ids,
            )
            for surface_id in case_surfaces:
                owner_id = surface_owner_by_id[surface_id]
                green_surface_platforms_by_capability[owner_id].update(
                    (surface_id, platform)
                    for platform in case["platforms"]
                )
    for capability in capabilities:
        capability_id = capability["id"]
        case_ids = capability["caseIds"]
        acceptance_status = capability["acceptanceStatus"]
        if acceptance_status == "not-authored" and case_ids:
            raise ValueError(
                f"{capability_id} not-authored capability has case IDs"
            )
        if acceptance_status in {"partial", "complete"} and not case_ids:
            raise ValueError(
                f"{capability_id} {acceptance_status} capability has no cases"
            )

        for case_id in case_ids:
            if case_id in referenced_case_ids:
                raise ValueError(
                    f"case ID is referenced by multiple capabilities: "
                    f"{case_id}"
                )
            case = cases_by_id.get(case_id)
            if case is None:
                raise ValueError(
                    f"{capability_id} references unknown case: {case_id}"
                )
            if case["capabilityId"] != capability_id:
                raise ValueError(
                    f"{case_id} capabilityId does not match its owner"
                )
            validate_case_within_capability(case, capability)
            referenced_case_ids.add(case_id)
        all_cases_green = bool(case_ids) and all(
            cases_by_id[case_id]["status"] == "both-green"
            for case_id in case_ids
        )
        if acceptance_status == "complete" and not all_cases_green:
            raise ValueError(
                f"{capability_id} complete capability has a non-green case"
            )
        obligations_complete = (
            obligation_completion is None
            or obligation_completion[capability_id]
        )
        if acceptance_status == "complete" and not obligations_complete:
            raise ValueError(
                f"{capability_id} complete capability has incomplete "
                "behavioral obligations"
            )
        required_surface_platforms = (
            {
                (surface_id, platform)
                for surface_id in capability_surface_ids[capability_id]
                for platform in capability["platforms"]
            }
            if inventory_surface_ids is not None
            else None
        )
        if (
            acceptance_status == "complete"
            and required_surface_platforms is not None
            and green_surface_platforms_by_capability[capability_id]
            != required_surface_platforms
        ):
            missing = sorted(
                required_surface_platforms
                - green_surface_platforms_by_capability[capability_id]
            )
            raise ValueError(
                f"{capability_id} complete capability has uncovered "
                "legacy surface/platform pairs: "
                + ", ".join(
                    f"{surface_id}@{platform}"
                    for surface_id, platform in missing[:10]
                )
            )
        if (
            acceptance_status == "partial"
            and required_surface_platforms is not None
            and all_cases_green
            and green_surface_platforms_by_capability[capability_id]
            == required_surface_platforms
            and obligations_complete
        ):
            raise ValueError(
                f"{capability_id} fully green capability must be complete"
            )

    orphan_case_ids = sorted(cases_by_id.keys() - referenced_case_ids)
    if orphan_case_ids:
        raise ValueError(
            "manifest has orphan cases: " + ", ".join(orphan_case_ids)
        )


def validate_obligation_case_links(
    capabilities: list[dict[str, Any]],
    cases: list[dict[str, Any]],
    obligations: list[dict[str, Any]],
    capabilities_by_id: dict[str, dict[str, Any]],
    cases_by_id: dict[str, dict[str, Any]],
    obligations_by_id: dict[str, dict[str, Any]],
    *,
    inventory_surface_ids: list[str] | None = None,
) -> dict[str, bool]:
    linked_cases_by_obligation: dict[str, list[str]] = defaultdict(list)
    for case in cases:
        case_id = case["id"]
        capability_id = case["capabilityId"]
        if capability_id not in capabilities_by_id:
            raise ValueError(
                f"{case_id} references unknown capability: "
                f"{capability_id}"
            )
        for obligation_id in case["obligationIds"]:
            obligation = obligations_by_id.get(obligation_id)
            if obligation is None:
                raise ValueError(
                    f"{case_id} references unknown behavioral obligation: "
                    f"{obligation_id}"
                )
            if obligation["capabilityId"] != capability_id:
                raise ValueError(
                    f"{case_id} links behavioral obligation from another "
                    f"capability: {obligation_id}"
                )
            if obligation["sourceBindingStatus"] != "bound":
                raise ValueError(
                    f"{case_id} cannot link pending-source behavioral "
                    f"obligation {obligation_id}"
                )
            if case["platforms"] != obligation["platforms"]:
                raise ValueError(
                    f"{case_id} must execute the full platform contract of "
                    f"behavioral obligation {obligation_id}"
                )
            linked_cases_by_obligation[obligation_id].append(case_id)

    owned_obligations: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for obligation in obligations:
        obligation_id = obligation["id"]
        capability_id = obligation["capabilityId"]
        if capability_id not in capabilities_by_id:
            raise ValueError(
                f"{obligation_id} has unknown capability ownership"
            )
        owned_obligations[capability_id].append(obligation)
        linked_ids = linked_cases_by_obligation.get(obligation_id, [])
        declared_ids = obligation["caseIds"]
        if linked_ids != declared_ids:
            raise ValueError(
                f"{obligation_id} caseIds are not exact reciprocal links"
            )
        for case_id in declared_ids:
            if case_id not in cases_by_id:
                raise ValueError(
                    f"{obligation_id} references unknown case: {case_id}"
                )
        if obligation["sourceBindingStatus"] == "bound":
            bound_sources = obligation["legacySources"]
            linked_sources = [
                source
                for case_id in declared_ids
                for source in cases_by_id[case_id]["legacySources"]
            ]
            for case_id in declared_ids:
                for case_source in cases_by_id[case_id]["legacySources"]:
                    if not any(
                        legacy_source_is_within(
                            case_source,
                            obligation_source,
                        )
                        for obligation_source in bound_sources
                    ):
                        raise ValueError(
                            f"{case_id} legacy source is unrelated to "
                            f"behavioral obligation {obligation_id}"
                        )
            uncovered_sources = [
                obligation_source
                for obligation_source in bound_sources
                if not any(
                    legacy_source_is_within(
                        linked_source,
                        obligation_source,
                    )
                    for linked_source in linked_sources
                )
            ]
            if uncovered_sources:
                raise ValueError(
                    f"{obligation_id} linked cases do not cover bound "
                    "legacy sources: "
                    + ", ".join(uncovered_sources)
                )
            if inventory_surface_ids is not None:
                bound_surface_ids = resolve_surface_selectors(
                    obligation["legacySurfaceSelectors"],
                    inventory_surface_ids,
                    obligation_id,
                )
                linked_surface_ids: set[str] = set()
                for case_id in declared_ids:
                    linked_surface_ids.update(
                        resolve_case_surfaces(
                            cases_by_id[case_id],
                            inventory_surface_ids,
                        )
                    )
                unrelated_surface_ids = sorted(
                    linked_surface_ids - bound_surface_ids
                )
                if unrelated_surface_ids:
                    raise ValueError(
                        f"{obligation_id} linked cases select unrelated "
                        "legacy surfaces: "
                        + ", ".join(unrelated_surface_ids)
                    )
                missing_surface_ids = sorted(
                    bound_surface_ids - linked_surface_ids
                )
                if missing_surface_ids:
                    raise ValueError(
                        f"{obligation_id} linked cases do not cover bound "
                        "legacy surfaces: "
                        + ", ".join(missing_surface_ids)
                    )
        required_platforms = set(obligation["platforms"])
        green_platforms: set[str] = set()
        all_linked_green = bool(declared_ids)
        for case_id in declared_ids:
            case = cases_by_id[case_id]
            if case["status"] != "both-green":
                all_linked_green = False
            else:
                green_platforms.update(case["platforms"])
        fully_green = (
            all_linked_green
            and green_platforms == required_platforms
        )
        rich_evidence_blocked = (
            fully_green
            and obligation_id in RICH_EVIDENCE_REQUIRED_OBLIGATIONS
        )
        expected_status = (
            "not-authored"
            if not declared_ids
            else (
                "complete"
                if fully_green and not rich_evidence_blocked
                else "partial"
            )
        )
        if obligation["acceptanceStatus"] != expected_status:
            raise ValueError(
                f"{obligation_id} acceptanceStatus must be "
                f"{expected_status}"
            )

    completion: dict[str, bool] = {}
    for capability in capabilities:
        capability_id = capability["id"]
        owned = owned_obligations.get(capability_id, [])
        if not owned:
            raise ValueError(
                f"{capability_id} has no behavioral obligations"
            )
        completion[capability_id] = all(
            obligation["acceptanceStatus"] == "complete"
            for obligation in owned
        )
    return completion


def validate_case_within_capability(
    case: dict[str, Any],
    capability: dict[str, Any],
) -> None:
    case_id = case["id"]
    unsupported = sorted(
        set(case["platforms"]) - set(capability["platforms"])
    )
    if unsupported:
        raise ValueError(
            f"{case_id} platforms exceed capability coverage: "
            f"{', '.join(unsupported)}"
        )


def resolve_case_surfaces(
    case: dict[str, Any],
    inventory_surface_ids: list[str],
) -> set[str]:
    return resolve_surface_selectors(
        case["legacySurfaceSelectors"],
        inventory_surface_ids,
        case["id"],
    )


def resolve_surface_selectors(
    selectors: list[str],
    inventory_surface_ids: list[str],
    owner_id: str,
) -> set[str]:
    selected_surfaces: set[str] = set()
    for selector in selectors:
        selected = {
            surface_id
            for surface_id in inventory_surface_ids
            if fnmatch.fnmatchcase(surface_id, selector)
        }
        if not selected:
            raise ValueError(
                f"{owner_id} selector matches no legacy surfaces: "
                f"{selector}"
            )
        selected_surfaces.update(selected)
    if not selected_surfaces:
        raise ValueError(f"{owner_id} covers no legacy surfaces")
    return selected_surfaces


def legacy_source_is_within(source: str, owner_source: str) -> bool:
    if source == owner_source:
        return True
    source_path = source.rsplit(":", 1)[0]
    owner_path = owner_source.rsplit(":", 1)[0]
    if source_path == owner_path:
        return True
    normalized_owner = owner_path.rstrip("/\\")
    return (
        ":" not in owner_source
        and (
            source_path == normalized_owner
            or source_path.startswith(normalized_owner + "/")
            or source_path.startswith(normalized_owner + "\\")
        )
    )


def validate_inventory(
    manifest: dict[str, Any],
    inventory: dict[str, Any],
) -> None:
    inventory_fields = {"schemaVersion", "reference", "surfaces"}
    require_fields(
        inventory,
        inventory_fields,
        "legacy surface inventory",
        allowed=inventory_fields,
    )
    if inventory.get("schemaVersion") != 2:
        raise ValueError("legacy surface inventory has an unsupported schema")
    surfaces = inventory.get("surfaces")
    if not isinstance(surfaces, list) or not surfaces:
        raise ValueError("legacy surface inventory must contain surfaces")

    pinned_revision = manifest["reference"]["revision"]
    reference = inventory["reference"]
    if not isinstance(reference, dict):
        raise ValueError("legacy surface inventory reference must be an object")
    reference_fields = {
        "revision",
        "trackedFileCount",
        "trackedFilesSha256",
        "sources",
        "exclusions",
    }
    require_fields(
        reference,
        reference_fields,
        "legacy surface inventory reference",
        allowed=reference_fields,
    )
    sources = require_string_array(
        reference["sources"],
        "legacy surface inventory reference sources",
    )
    if len(sources) != len(set(sources)):
        raise ValueError(
            "legacy surface inventory reference sources contains duplicates"
        )
    tracked_file_count = reference["trackedFileCount"]
    if (
        not isinstance(tracked_file_count, int)
        or isinstance(tracked_file_count, bool)
        or tracked_file_count <= 0
    ):
        raise ValueError(
            "legacy surface inventory trackedFileCount must be positive"
        )
    tracked_files_digest = validate_sha256(
        reference["trackedFilesSha256"],
        "legacy surface inventory trackedFilesSha256",
    )
    exclusions = reference["exclusions"]
    if not isinstance(exclusions, list):
        raise ValueError(
            "legacy surface inventory exclusions must be an array"
        )
    exclusion_paths: list[str] = []
    exclusion_fields = {"path", "kind", "rationale"}
    for exclusion in exclusions:
        if not isinstance(exclusion, dict):
            raise ValueError(
                "legacy surface inventory exclusion must be an object"
            )
        require_fields(
            exclusion,
            exclusion_fields,
            "legacy surface inventory exclusion",
            allowed=exclusion_fields,
        )
        for field in sorted(exclusion_fields):
            require_nonempty_string(
                exclusion[field],
                f"legacy surface inventory exclusion {field}",
            )
        exclusion_paths.append(exclusion["path"])
    source_keys = {
        source.replace("\\", "/").casefold()
        for source in sources
    }
    exclusion_keys = {
        path.replace("\\", "/").casefold()
        for path in exclusion_paths
    }
    classification_overlap = sorted(source_keys & exclusion_keys)
    if classification_overlap:
        raise ValueError(
            "tracked files must be classified exactly once; both "
            "inventoried and excluded: "
            + ", ".join(classification_overlap)
        )
    classified_files = sources + exclusion_paths
    if tracked_file_count != len(classified_files):
        raise ValueError(
            "legacy surface inventory trackedFileCount does not match "
            "classified files"
        )
    if (
        tracked_files_sha256(classified_files).lower()
        != tracked_files_digest.lower()
    ):
        raise ValueError(
            "legacy surface inventory trackedFilesSha256 does not match "
            "classified files"
        )
    validate_tracked_file_classification(
        classified_files,
        sources,
        exclusions,
    )
    if reference["revision"] != pinned_revision:
        raise ValueError("legacy surface inventory revision is not pinned by manifest")

    required_surface = {"id", "category", "name", "source", "details"}
    surface_ids: set[str] = set()
    for surface in surfaces:
        if not isinstance(surface, dict):
            raise ValueError("each legacy surface must be an object")
        require_fields(
            surface,
            required_surface,
            "legacy surface",
            allowed=required_surface,
        )
        surface_id = require_nonempty_string(
            surface["id"],
            "legacy surface ID",
        )
        for key in ("category", "name", "source"):
            require_nonempty_string(
                surface[key],
                f"{surface_id} legacy surface {key}",
            )
        if not isinstance(surface["details"], dict):
            raise ValueError(
                f"{surface_id} legacy surface details must be an object"
            )
        if surface_id in surface_ids:
            raise ValueError(f"duplicate legacy surface ID: {surface_id}")
        surface_ids.add(surface_id)


def validate_active_case(
    case: dict[str, Any],
    reference_revision: str,
    *,
    manifest: dict[str, Any],
    root: Path = ROOT,
) -> tuple[Path, dict[str, Any]]:
    case_id = case["id"]
    fixture_path, fixture, fixture_count = validate_fixture(
        case,
        reference_revision,
        require_schema_v2=True,
        root=root,
    )
    evidence_path = resolve_repo_file(
        case["evidence"],
        "tests/parity/evidence",
        f"{case_id} evidence",
        root=root,
    )
    require_json_suffix(evidence_path, f"{case_id} evidence")
    evidence = load_json(evidence_path)
    validate_active_run_evidence(
        case,
        evidence,
        evidence_path,
        fixture_path,
        fixture,
        fixture_count,
        reference_revision,
        manifest=manifest,
        root=root,
    )
    return evidence_path, evidence


def validate_fixture(
    case: dict[str, Any],
    reference_revision: str,
    *,
    require_schema_v2: bool = False,
    root: Path = ROOT,
) -> tuple[Path, dict[str, Any], int]:
    parity_id = case["id"]
    fixture_path = resolve_repo_file(
        case["fixture"],
        "tests/parity/fixtures/legacy",
        f"{parity_id} fixture",
        root=root,
    )
    require_json_suffix(fixture_path, f"{parity_id} fixture")
    fixture = load_json(fixture_path)
    fixture_count = validate_fixture_document(
        fixture,
        parity_id,
        reference_revision,
        fixture_path,
        case=case,
        require_schema_v2=require_schema_v2,
        root=root,
    )
    validate_fixture_assertions(case, fixture, fixture_count)
    return fixture_path, fixture, fixture_count


def require_json_suffix(path: Path, label: str) -> None:
    if path.suffix.lower() != ".json":
        raise ValueError(f"{label} must be a JSON file: {path}")


def validate_fixture_document(
    fixture: dict[str, Any],
    parity_id: str,
    reference_revision: str,
    fixture_path: Path,
    *,
    case: dict[str, Any] | None = None,
    require_schema_v2: bool = False,
    root: Path = ROOT,
) -> int:
    common_required = {"revision", "values"}
    require_fields(fixture, common_required, f"{parity_id} fixture")
    if fixture["revision"] != reference_revision:
        raise ValueError(
            f"{parity_id} fixture revision does not match the manifest"
        )
    values = fixture["values"]
    if not isinstance(values, list) or not values:
        raise ValueError(f"{parity_id} fixture values must be a non-empty array")
    if any(not isinstance(value, str) for value in values):
        raise ValueError(
            f"{parity_id} active fixture values must be JSON strings"
        )

    if fixture.get("schemaVersion") == 2:
        if not require_schema_v2:
            return validate_retained_fixture_v2(
                fixture,
                parity_id,
                root=root,
            )
        return validate_fixture_v2(
            fixture,
            parity_id,
            fixture_path,
            bind_to_reference=require_schema_v2,
            reference_revision=reference_revision,
            case=case,
            root=root,
        )
    if require_schema_v2:
        raise ValueError(
            f"{parity_id} active fixture must use schema version 2"
        )
    if "schemaVersion" in fixture:
        raise ValueError(
            f"{parity_id} fixture has an unsupported schema version"
        )

    if "parityId" not in fixture:
        require_fields(
            fixture,
            {"revision", "source", "values"},
            f"{parity_id} legacy-v1 static fixture",
            allowed={"revision", "source", "values"},
        )
        require_nonempty_string(
            fixture["source"],
            f"{parity_id} fixture source",
        )
        return len(values)

    if fixture["parityId"] != parity_id:
        raise ValueError(
            f"{parity_id} fixture parityId is {fixture['parityId']!r}"
        )

    if "surfacePrefixes" in fixture:
        allowed = {"revision", "parityId", "surfacePrefixes", "values"}
        require_fields(
            fixture,
            allowed,
            f"{parity_id} inventory fixture",
            allowed=allowed,
        )
        require_string_array(
            fixture["surfacePrefixes"],
            f"{parity_id} fixture surfacePrefixes",
        )
    elif "oracle" in fixture:
        allowed = {"revision", "parityId", "oracle", "toolchain", "values"}
        require_fields(
            fixture,
            allowed,
            f"{parity_id} oracle fixture",
            allowed=allowed,
        )
        resolve_repo_file(
            fixture["oracle"],
            "tests/parity/legacy-oracle",
            f"{parity_id} fixture oracle",
            root=root,
        )
        toolchain = fixture["toolchain"]
        if not isinstance(toolchain, dict):
            raise ValueError(f"{parity_id} fixture toolchain must be an object")
        toolchain_fields = {"lazarus", "fpc", "target"}
        require_fields(
            toolchain,
            toolchain_fields,
            f"{parity_id} fixture toolchain",
            allowed=toolchain_fields,
        )
        for key in sorted(toolchain_fields):
            require_nonempty_string(
                toolchain[key],
                f"{parity_id} fixture toolchain {key}",
            )
    else:
        raise ValueError(
            f"{parity_id} fixture has no recognized provenance schema: "
            f"{fixture_path}"
        )
    return len(values)


def validate_retained_fixture_v2(
    fixture: dict[str, Any],
    parity_id: str,
    *,
    root: Path = ROOT,
) -> int:
    required = {
        "schemaVersion",
        "revision",
        "tree",
        "parityId",
        "oracle",
        "oracleSourceSha256",
        "oracleExecutableSha256",
        "toolchain",
        "values",
    }
    allowed = required | {
        "referenceDefinitionSha256",
        "oracleProvenanceSha256",
    }
    require_fields(
        fixture,
        required,
        f"{parity_id} retained schema-v2 fixture",
        allowed=allowed,
    )
    if fixture["parityId"] != parity_id:
        raise ValueError(
            f"{parity_id} fixture parityId is {fixture['parityId']!r}"
        )
    resolve_repo_file(
        fixture["oracle"],
        "tests/parity/legacy-oracle",
        f"{parity_id} fixture oracle",
        root=root,
    )
    for key in (
        "oracleSourceSha256",
        "oracleExecutableSha256",
    ):
        validate_retained_sha256(
            fixture[key],
            f"{parity_id} fixture {key}",
        )
    if "referenceDefinitionSha256" in fixture:
        validate_retained_sha256(
            fixture["referenceDefinitionSha256"],
            f"{parity_id} fixture referenceDefinitionSha256",
        )
    if "oracleProvenanceSha256" in fixture:
        validate_retained_sha256(
            fixture["oracleProvenanceSha256"],
            f"{parity_id} fixture oracleProvenanceSha256",
        )
    toolchain = fixture["toolchain"]
    if not isinstance(toolchain, dict):
        raise ValueError(f"{parity_id} fixture toolchain must be an object")
    required_toolchain = {
        "lazarus",
        "fpc",
        "target",
        "compilerSha256",
        "backendCompilerSha256",
        "lazbuildSha256",
    }
    allowed_toolchain = required_toolchain | {"fingerprintSha256"}
    require_fields(
        toolchain,
        required_toolchain,
        f"{parity_id} retained fixture toolchain",
        allowed=allowed_toolchain,
    )
    for key in ("lazarus", "fpc", "target"):
        require_nonempty_string(
            toolchain[key],
            f"{parity_id} fixture toolchain {key}",
        )
    for key in (
        "compilerSha256",
        "backendCompilerSha256",
        "lazbuildSha256",
    ):
        validate_retained_sha256(
            toolchain[key],
            f"{parity_id} fixture toolchain {key}",
        )
    if "fingerprintSha256" in toolchain:
        validate_retained_sha256(
            toolchain["fingerprintSha256"],
            f"{parity_id} fixture toolchain fingerprintSha256",
        )
    return len(fixture["values"])


def validate_fixture_v2(
    fixture: dict[str, Any],
    parity_id: str,
    fixture_path: Path,
    *,
    bind_to_reference: bool = False,
    reference_revision: str | None = None,
    case: dict[str, Any] | None = None,
    root: Path = ROOT,
) -> int:
    if fixture["schemaVersion"] != 2:
        raise ValueError(
            f"{parity_id} fixture has an unsupported schema version"
        )
    root_fields = {
        "schemaVersion",
        "revision",
        "tree",
        "parityId",
        "oracle",
        "legacyOracleVersionId",
        "referenceDefinitionSha256",
        "oracleSourceSha256",
        "oracleBuildRecipeSha256",
        "oracleExecutableSha256",
        "toolchain",
        "values",
    }
    require_fields(
        fixture,
        root_fields,
        f"{parity_id} schema-v2 fixture",
        allowed=root_fields,
    )
    if fixture["parityId"] != parity_id:
        raise ValueError(
            f"{parity_id} fixture parityId is {fixture['parityId']!r}"
        )
    if not COMMIT_PATTERN.fullmatch(
        require_nonempty_string(
            fixture["tree"],
            f"{parity_id} fixture tree",
        )
    ):
        raise ValueError(f"{parity_id} fixture tree must be a full tree ID")
    oracle_path = resolve_repo_file(
        fixture["oracle"],
        "tests/parity/legacy-oracle",
        f"{parity_id} fixture oracle",
        root=root,
    )
    require_nonempty_string(
        fixture["legacyOracleVersionId"],
        f"{parity_id} fixture legacyOracleVersionId",
    )
    for key in (
        "referenceDefinitionSha256",
        "oracleSourceSha256",
        "oracleBuildRecipeSha256",
        "oracleExecutableSha256",
    ):
        digest = require_nonempty_string(
            fixture[key],
            f"{parity_id} fixture {key}",
        )
        if not SHA256_PATTERN.fullmatch(digest):
            raise ValueError(
                f"{parity_id} fixture {key} must be a SHA-256 digest"
            )

    toolchain = fixture["toolchain"]
    if not isinstance(toolchain, dict):
        raise ValueError(f"{parity_id} fixture toolchain must be an object")
    toolchain_fields = {
        "lazarus",
        "fpc",
        "target",
        "compilerSha256",
        "backendCompilerSha256",
        "lazbuildSha256",
        "fingerprintSha256",
    }
    require_fields(
        toolchain,
        toolchain_fields,
        f"{parity_id} fixture toolchain",
        allowed=toolchain_fields,
    )
    for key in ("lazarus", "fpc", "target"):
        require_nonempty_string(
            toolchain[key],
            f"{parity_id} fixture toolchain {key}",
        )
    for key in (
        "compilerSha256",
        "backendCompilerSha256",
        "lazbuildSha256",
        "fingerprintSha256",
    ):
        digest = require_nonempty_string(
            toolchain[key],
            f"{parity_id} fixture toolchain {key}",
        )
        if not SHA256_PATTERN.fullmatch(digest):
            raise ValueError(
                f"{parity_id} fixture {key} must be a SHA-256 digest"
            )

    values = fixture["values"]
    if not isinstance(values, list) or not values:
        raise ValueError(f"{parity_id} fixture values must be a non-empty array")
    if not fixture_path.is_file():
        raise ValueError(f"{parity_id} fixture does not exist: {fixture_path}")
    if bind_to_reference:
        if reference_revision is None:
            raise ValueError(
                f"{parity_id} fixture reference revision was not supplied"
            )
        if case is None:
            raise ValueError(
                f"{parity_id} fixture case descriptor was not supplied"
            )
        legacy_reference, reference_definition_sha256 = load_legacy_reference(
            reference_revision,
            root=root,
        )
        validate_fixture_reference_binding(
            fixture,
            parity_id,
            oracle_path,
            legacy_reference,
            reference_definition_sha256,
            case,
            root=root,
        )
    return len(values)


def load_legacy_reference(
    reference_revision: str,
    *,
    root: Path = ROOT,
) -> tuple[dict[str, Any], str]:
    reference_path = resolve_repo_file(
        "tests/parity/legacy-reference.json",
        "tests/parity",
        "legacy reference",
        root=root,
    )
    require_json_suffix(reference_path, "legacy reference")
    reference = load_json(reference_path)
    root_fields = {
        "schemaVersion",
        "repository",
        "publicBaseRevision",
        "revision",
        "tree",
        "bundle",
        "bundleSha256",
        "toolchain",
    }
    require_fields(
        reference,
        root_fields,
        "legacy reference",
        allowed=root_fields,
    )
    if reference["schemaVersion"] != 1:
        raise ValueError("legacy reference has an unsupported schema version")
    if reference["revision"] != reference_revision:
        raise ValueError(
            "legacy reference revision does not match the manifest"
        )
    for key in ("publicBaseRevision", "revision", "tree"):
        value = require_nonempty_string(
            reference[key],
            f"legacy reference {key}",
        )
        if not COMMIT_PATTERN.fullmatch(value):
            raise ValueError(
                f"legacy reference {key} must be a full Git object ID"
            )
    for key in ("repository", "bundle"):
        require_nonempty_string(
            reference[key],
            f"legacy reference {key}",
        )
    declared_bundle_hash = validate_sha256(
        reference["bundleSha256"],
        "legacy reference bundleSha256",
    )
    bundle_path = resolve_repo_file(
        reference["bundle"],
        "tests/parity",
        "legacy reference bundle",
        root=root,
    )
    if sha256_file(bundle_path) != declared_bundle_hash.lower():
        raise ValueError(
            "legacy reference bundleSha256 does not match the committed "
            "bundle"
        )
    toolchain = reference["toolchain"]
    if not isinstance(toolchain, dict):
        raise ValueError("legacy reference toolchain must be an object")
    toolchain_fields = {
        "lazarusVersion",
        "fpcVersion",
        "targetCpu",
        "targetOs",
        "installer",
        "installerSha256",
        "compilerSha256",
        "backendCompilerSha256",
        "lazbuildSha256",
        "fingerprint",
    }
    require_fields(
        toolchain,
        toolchain_fields,
        "legacy reference toolchain",
        allowed=toolchain_fields,
    )
    for key in (
        "lazarusVersion",
        "fpcVersion",
        "targetCpu",
        "targetOs",
        "installer",
    ):
        require_nonempty_string(
            toolchain[key],
            f"legacy reference toolchain {key}",
        )
    for key in (
        "installerSha256",
        "compilerSha256",
        "backendCompilerSha256",
        "lazbuildSha256",
    ):
        validate_sha256(
            toolchain[key],
            f"legacy reference toolchain {key}",
        )
    fingerprint = toolchain["fingerprint"]
    if not isinstance(fingerprint, dict):
        raise ValueError(
            "legacy reference toolchain fingerprint must be an object"
        )
    fingerprint_fields = {
        "schemaVersion",
        "canonicalization",
        "roots",
        "aggregateSha256",
        "fileCount",
        "byteCount",
    }
    require_fields(
        fingerprint,
        fingerprint_fields,
        "legacy reference toolchain fingerprint",
        allowed=fingerprint_fields,
    )
    if fingerprint["schemaVersion"] != 1:
        raise ValueError(
            "legacy reference toolchain fingerprint has an "
            "unsupported schema version"
        )
    require_nonempty_string(
        fingerprint["canonicalization"],
        "legacy reference toolchain fingerprint canonicalization",
    )
    require_string_array(
        fingerprint["roots"],
        "legacy reference toolchain fingerprint roots",
    )
    validate_sha256(
        fingerprint["aggregateSha256"],
        "legacy reference toolchain fingerprint aggregateSha256",
    )
    for key in ("fileCount", "byteCount"):
        count = fingerprint[key]
        if (
            not isinstance(count, int)
            or isinstance(count, bool)
            or count <= 0
        ):
            raise ValueError(
                f"legacy reference toolchain fingerprint {key} "
                "must be a positive integer"
            )
    return reference, sha256_file(reference_path)


def validate_fixture_reference_binding(
    fixture: dict[str, Any],
    parity_id: str,
    oracle_path: Path,
    reference: dict[str, Any],
    reference_definition_sha256: str,
    case: dict[str, Any],
    *,
    root: Path = ROOT,
) -> None:
    descriptor = case["legacyOracle"]
    expected = {
        "revision": reference["revision"],
        "tree": reference["tree"],
        "oracle": descriptor["source"],
        "legacyOracleVersionId": descriptor["versionId"],
        "referenceDefinitionSha256": reference_definition_sha256,
        "oracleSourceSha256": descriptor["sourceSha256"],
        "oracleBuildRecipeSha256": descriptor["buildRecipeSha256"],
    }
    for key, expected_value in expected.items():
        actual = fixture[key]
        if key.endswith("Sha256"):
            matches = actual.lower() == expected_value.lower()
        else:
            matches = actual == expected_value
        if not matches:
            raise ValueError(
                f"{parity_id} fixture {key} does not match "
                "legacy-reference.json"
            )
    if sha256_file(oracle_path) != descriptor["sourceSha256"].lower():
        raise ValueError(
            f"{parity_id} fixture oracleSourceSha256 does not match "
            "the case legacyOracle source"
        )
    build_recipe = resolve_repo_file(
        descriptor["buildRecipe"],
        "tests/parity/legacy-oracle",
        f"{parity_id} legacyOracle buildRecipe",
        root=root,
    )
    if (
        sha256_file(build_recipe)
        != fixture["oracleBuildRecipeSha256"].lower()
    ):
        raise ValueError(
            f"{parity_id} fixture oracleBuildRecipeSha256 does not match "
            "the case legacyOracle build recipe"
        )

    reference_toolchain = reference["toolchain"]
    fixture_toolchain = fixture["toolchain"]
    expected_toolchain = {
        "lazarus": reference_toolchain["lazarusVersion"],
        "fpc": reference_toolchain["fpcVersion"],
        "target": (
            f"{reference_toolchain['targetCpu']}-"
            f"{reference_toolchain['targetOs']}"
        ),
        "compilerSha256": reference_toolchain["compilerSha256"],
        "backendCompilerSha256": (
            reference_toolchain["backendCompilerSha256"]
        ),
        "lazbuildSha256": reference_toolchain["lazbuildSha256"],
        "fingerprintSha256": (
            reference_toolchain["fingerprint"]["aggregateSha256"]
        ),
    }
    for key, expected_value in expected_toolchain.items():
        actual = fixture_toolchain[key]
        if key.endswith("Sha256"):
            matches = actual.lower() == expected_value.lower()
        else:
            matches = actual == expected_value
        if not matches:
            raise ValueError(
                f"{parity_id} fixture toolchain {key} does not match "
                "legacy-reference.json"
            )


def validate_sha256(value: Any, label: str) -> str:
    digest = require_nonempty_string(value, label)
    if not SHA256_PATTERN.fullmatch(digest):
        raise ValueError(f"{label} must be a SHA-256 digest")
    return digest


def validate_retained_sha256(value: Any, label: str) -> str:
    digest = require_nonempty_string(value, label)
    if re.fullmatch(r"[0-9A-Fa-f]{64}", digest) is None:
        raise ValueError(f"{label} must be a historical SHA-256 digest")
    return digest.lower()


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def validate_unicode_scalar_string(value: str, label: str) -> None:
    if any(0xD800 <= ord(character) <= 0xDFFF for character in value):
        raise ValueError(
            f"{label} contains an unpaired Unicode surrogate"
        )


def canonicalize_json_value(
    value: Any,
    label: str = "canonical JSON",
) -> Any:
    if value is None or isinstance(value, bool):
        return value
    if isinstance(value, str):
        validate_unicode_scalar_string(value, label)
        return value
    if type(value) is int:
        if -(2**63) <= value <= (2**63) - 1:
            return value
        raise ValueError(
            f"{label} integer is outside the signed 64-bit range"
        )
    if isinstance(value, (float, complex)):
        raise ValueError(
            f"{label} numbers must be signed 64-bit integers"
        )
    if isinstance(value, list):
        return [
            canonicalize_json_value(entry, f"{label}[{index}]")
            for index, entry in enumerate(value)
        ]
    if isinstance(value, dict):
        ordered: dict[str, Any] = {}
        for key in value:
            if not isinstance(key, str):
                raise ValueError(f"{label} object keys must be strings")
            validate_unicode_scalar_string(key, f"{label} object key")
        for key in sorted(
            value,
            key=lambda entry: entry.encode("utf-16-be"),
        ):
            ordered[key] = canonicalize_json_value(
                value[key],
                f"{label}.{key}",
            )
        return ordered
    raise ValueError(
        f"{label} contains a value outside the canonical JSON domain"
    )


def canonical_json_bytes(value: Any) -> bytes:
    canonical = canonicalize_json_value(value)
    return json.dumps(
        canonical,
        ensure_ascii=False,
        allow_nan=False,
        separators=(",", ":"),
    ).encode("utf-8")


def canonical_json_sha256(value: Any) -> str:
    return hashlib.sha256(canonical_json_bytes(value)).hexdigest()


def validate_canonical_json_contract(
    contract: Any,
    *,
    root: Path = ROOT,
) -> None:
    if not isinstance(contract, dict):
        raise ValueError("manifest canonicalJson must be an object")
    fields = {"version", "vectors", "vectorsSha256"}
    require_fields(
        contract,
        fields,
        "manifest canonicalJson",
        allowed=fields,
    )
    if contract["version"] != CANONICAL_JSON_VERSION:
        raise ValueError(
            "manifest canonicalJson has an unsupported version"
        )
    if contract["vectors"] != CANONICAL_JSON_VECTOR_PATH:
        raise ValueError(
            "manifest canonicalJson vectors path is not pinned"
        )
    vectors_path = resolve_repo_file(
        contract["vectors"],
        "tests/parity",
        "canonical JSON vectors",
        root=root,
    )
    declared_digest = validate_sha256(
        contract["vectorsSha256"],
        "manifest canonicalJson vectorsSha256",
    )
    if (
        declared_digest.lower()
        != CANONICAL_JSON_VECTORS_FILE_SHA256
    ):
        raise ValueError(
            "manifest canonicalJson vector file is not the reviewed "
            "version 1 contract"
        )
    if sha256_file(vectors_path) != declared_digest.lower():
        raise ValueError(
            "manifest canonicalJson vectorsSha256 does not match "
            "the shared vectors"
        )
    document = load_json(vectors_path)
    document_fields = {
        "schemaVersion",
        "ordering",
        "encoding",
        "vectors",
        "invalidJsonTexts",
    }
    require_fields(
        document,
        document_fields,
        "canonical JSON vectors",
        allowed=document_fields,
    )
    if (
        document["schemaVersion"] != CANONICAL_JSON_VERSION
        or document["ordering"]
        != "utf16-code-unit-ordinal-recursive"
        or document["encoding"]
        != "utf8-no-bom-no-normalization"
    ):
        raise ValueError(
            "canonical JSON vector semantics do not match version 1"
        )
    vectors = document["vectors"]
    if not isinstance(vectors, list) or len(vectors) != 1:
        raise ValueError(
            "canonical JSON vectors must contain the reviewed vector"
        )
    vector = vectors[0]
    vector_fields = {
        "name",
        "value",
        "canonicalUtf8Base64",
        "sha256",
    }
    if not isinstance(vector, dict):
        raise ValueError("canonical JSON vector must be an object")
    require_fields(
        vector,
        vector_fields,
        "canonical JSON vector",
        allowed=vector_fields,
    )
    if vector["name"] != "nested-unicode-escaping-and-int64":
        raise ValueError("canonical JSON vector identity is not pinned")
    expected_hash = validate_sha256(
        vector["sha256"],
        "canonical JSON vector sha256",
    ).lower()
    if expected_hash != CANONICAL_JSON_VECTOR_SHA256:
        raise ValueError(
            "canonical JSON vector hash does not match the reviewed "
            "cross-language constant"
        )
    try:
        expected_bytes = base64.b64decode(
            require_nonempty_string(
                vector["canonicalUtf8Base64"],
                "canonical JSON vector canonicalUtf8Base64",
            ),
            validate=True,
        )
    except (ValueError, TypeError) as error:
        raise ValueError(
            "canonical JSON vector bytes are not valid Base64"
        ) from error
    actual_bytes = canonical_json_bytes(vector["value"])
    if actual_bytes != expected_bytes:
        raise ValueError(
            "canonical JSON vector bytes differ from the reviewed bytes"
        )
    if hashlib.sha256(actual_bytes).hexdigest() != expected_hash:
        raise ValueError(
            "canonical JSON vector hash differs from the reviewed hash"
        )
    invalid_texts = require_string_array(
        document["invalidJsonTexts"],
        "canonical JSON invalidJsonTexts",
    )
    if invalid_texts != [
        '{"\\ud800":"key"}',
        '{"value":"\\udfff"}',
    ]:
        raise ValueError(
            "canonical JSON invalid-Unicode vectors are not pinned"
        )
    for invalid_text in invalid_texts:
        invalid_value = json.loads(invalid_text)
        try:
            canonical_json_bytes(invalid_value)
        except ValueError:
            continue
        raise ValueError(
            "canonical JSON accepted an invalid Unicode vector"
        )


def migration_obligation_anchor_sha256(
    obligations: list[dict[str, Any]],
) -> str:
    anchor = sorted(
        (
            {
                field: obligation[field]
                for field in (
                    "id",
                    "capabilityId",
                    "behavior",
                    "platforms",
                    "sourceBindingStatus",
                    "legacySources",
                    "legacySurfaceSelectors",
                )
            }
            for obligation in obligations
        ),
        key=lambda obligation: obligation["id"],
    )
    return canonical_json_sha256(anchor)


def migration_capability_anchor_sha256(
    capabilities: list[dict[str, Any]],
) -> str:
    anchor = sorted(
        (
            {
                field: capability[field]
                for field in (
                    "id",
                    "category",
                    "feature",
                    "behavior",
                    "legacySources",
                    "legacySurfaceSelectors",
                    "platforms",
                )
            }
            for capability in capabilities
        ),
        key=lambda capability: capability["id"],
    )
    return canonical_json_sha256(anchor)


def validate_case_definition_numeric_domain(
    value: Any,
    label: str = "case definition",
) -> None:
    if value is None or isinstance(value, (str, bool)):
        return
    if type(value) is int:
        if -(2**63) <= value <= (2**63) - 1:
            return
        raise ValueError(
            f"{label} integer is outside the signed 64-bit range"
        )
    if isinstance(value, (float, complex)):
        raise ValueError(
            f"{label} numbers must be signed 64-bit integers; "
            "fractional and scientific DSP values must be invariant strings"
        )
    if isinstance(value, list):
        for index, entry in enumerate(value):
            validate_case_definition_numeric_domain(
                entry,
                f"{label}[{index}]",
            )
        return
    if isinstance(value, dict):
        for key, entry in value.items():
            if not isinstance(key, str):
                raise ValueError(f"{label} object keys must be strings")
            validate_case_definition_numeric_domain(
                entry,
                f"{label}.{key}",
            )
        return
    raise ValueError(
        f"{label} contains a value outside the canonical JSON domain"
    )


def case_definition_sha256(case: dict[str, Any]) -> str:
    definition = {
        field: case[field]
        for field in CASE_DEFINITION_FIELDS
    }
    validate_case_definition_numeric_domain(definition)
    return canonical_json_sha256(definition)


def validate_fixture_assertions(
    item: dict[str, Any],
    fixture: dict[str, Any],
    fixture_count: int,
) -> None:
    parity_id = item["id"]
    assertions = item["assertions"]
    if assertions.get("fixtureComparison") != "exact":
        raise ValueError(
            f"{parity_id} fixtureComparison must be exact"
        )
    asserted_values_hash = validate_sha256(
        assertions.get("observedValuesSha256"),
        f"{parity_id} assertions observedValuesSha256",
    )
    if (
        asserted_values_hash.lower()
        != canonical_json_sha256(fixture["values"])
    ):
        raise ValueError(
            f"{parity_id} asserted observedValuesSha256 does not match "
            "fixture values"
        )
    require_nonempty_string(
        assertions.get("firstDivergence"),
        f"{parity_id} assertions firstDivergence",
    )
    ordered_values = assertions.get("orderedValues")
    count_keys = [
        key
        for key in ("observedSurfaceCount", "observedValueCount")
        if key in assertions
    ]
    if ordered_values is not None:
        if not isinstance(ordered_values, list) or not ordered_values:
            raise ValueError(
                f"{parity_id} orderedValues must be a non-empty array"
            )
        if ordered_values != fixture["values"]:
            raise ValueError(
                f"{parity_id} fixture and orderedValues assertion differ"
            )
    elif len(count_keys) != 1:
        raise ValueError(
            f"{parity_id} must declare exactly one observed count assertion"
        )

    if len(count_keys) > 1:
        raise ValueError(
            f"{parity_id} declares more than one observed count assertion"
        )
    if count_keys:
        asserted_count = assertions[count_keys[0]]
        if (
            not isinstance(asserted_count, int)
            or isinstance(asserted_count, bool)
            or asserted_count != fixture_count
        ):
            raise ValueError(
                f"{parity_id} asserted count does not match fixture values"
            )


def validate_retained_legacy_oracle_snapshot(
    snapshot: Any,
    case: dict[str, Any],
    fixture: dict[str, Any],
    reference_revision: str,
    *,
    root: Path = ROOT,
) -> tuple[dict[str, Any], str, dict[str, Any], str]:
    case_id = case["id"]
    if not isinstance(snapshot, dict):
        raise ValueError(
            f"{case_id} evidence legacyOracle must be an object"
        )
    fields = {
        "adapterId",
        "versionId",
        "source",
        "sourceSha256",
        "buildRecipe",
        "buildRecipeSha256",
        "executableSha256",
        "provenance",
        "provenanceSha256",
        "registrySha256",
        "retainedProvenance",
    }
    require_fields(
        snapshot,
        fields,
        f"{case_id} evidence legacyOracle",
        allowed=fields,
    )
    descriptor = case["legacyOracle"]
    for field in (
        "adapterId",
        "versionId",
        "source",
        "sourceSha256",
        "buildRecipe",
        "buildRecipeSha256",
    ):
        if snapshot[field] != descriptor[field]:
            raise ValueError(
                f"{case_id} retained legacyOracle {field} does not "
                "match the immutable case descriptor"
            )
    executable_hash = validate_sha256(
        snapshot["executableSha256"],
        f"{case_id} retained legacyOracle executableSha256",
    )
    if executable_hash != fixture["oracleExecutableSha256"]:
        raise ValueError(
            f"{case_id} retained legacyOracle executableSha256 does not "
            "match the fixture"
        )
    provenance_path = require_nonempty_string(
        snapshot["provenance"],
        f"{case_id} retained legacyOracle provenance",
    )
    provenance_hash = validate_sha256(
        snapshot["provenanceSha256"],
        f"{case_id} retained legacyOracle provenanceSha256",
    )
    registry_hash = validate_sha256(
        snapshot["registrySha256"],
        f"{case_id} retained legacyOracle registrySha256",
    )
    provenance_document, retained_hash = (
        load_content_addressed_document(
            snapshot["retainedProvenance"],
            "tests/parity/evidence/provenance",
            f"{case_id} retained legacy provenance",
            root=root,
        )
    )
    if retained_hash != provenance_hash:
        raise ValueError(
            f"{case_id} retained legacy provenance hash does not match "
            "the runtime registry binding"
        )
    validate_legacy_provenance(
        provenance_document,
        case_id,
        case,
        fixture,
        reference_revision,
        root=root,
    )
    entry = {
        **descriptor,
        "executableSha256": executable_hash,
        "provenance": provenance_path,
        "provenanceSha256": provenance_hash,
    }
    return provenance_document, provenance_hash, entry, registry_hash


def validate_active_run_evidence(
    case: dict[str, Any],
    evidence: dict[str, Any],
    evidence_path: Path,
    fixture_path: Path,
    fixture: dict[str, Any],
    fixture_count: int,
    reference_revision: str,
    *,
    manifest: dict[str, Any] | None = None,
    root: Path = ROOT,
) -> None:
    case_id = case["id"]
    validate_lf_json(evidence_path, f"{case_id} evidence")
    validate_lf_json(fixture_path, f"{case_id} fixture")
    root_fields = {
        "schemaVersion",
        "parityId",
        "referenceRevision",
        "capturedAtUtc",
        "fixture",
        "legacyOracle",
        "runs",
        "testReports",
        "executions",
        "regressionGate",
        "classification",
    }
    require_fields(
        evidence,
        root_fields,
        f"{case_id} evidence",
        allowed=root_fields,
    )
    if evidence["schemaVersion"] != EVIDENCE_SCHEMA_VERSION:
        raise ValueError(
            f"{case_id} evidence must use schema {EVIDENCE_SCHEMA_VERSION}"
        )
    if evidence["parityId"] != case_id:
        raise ValueError(
            f"{case_id} evidence parityId is {evidence['parityId']!r}"
        )
    if evidence["referenceRevision"] != reference_revision:
        raise ValueError(
            f"{case_id} evidence revision does not match the manifest"
        )
    validate_timestamp(
        evidence["capturedAtUtc"],
        f"{case_id} evidence capturedAtUtc",
    )

    fixture_reference = evidence["fixture"]
    if not isinstance(fixture_reference, dict):
        raise ValueError(f"{case_id} fixture reference must be an object")
    fixture_fields = {"path", "sha256", "observedValuesSha256"}
    require_fields(
        fixture_reference,
        fixture_fields,
        f"{case_id} fixture reference",
        allowed=fixture_fields,
    )
    if fixture_reference["path"] != case["fixture"]:
        raise ValueError(
            f"{case_id} evidence fixture path does not match the case"
        )
    fixture_hash = validate_sha256(
        fixture_reference["sha256"],
        f"{case_id} evidence fixture sha256",
    ).lower()
    if fixture_hash != sha256_file(fixture_path):
        raise ValueError(
            f"{case_id} evidence fixture sha256 does not match the fixture"
        )
    expected_values_hash = canonical_json_sha256(fixture["values"])
    observed_values_hash = validate_sha256(
        fixture_reference["observedValuesSha256"],
        f"{case_id} fixture observedValuesSha256",
    )
    if observed_values_hash.lower() != expected_values_hash:
        raise ValueError(
            f"{case_id} fixture observedValuesSha256 does not match values"
        )

    (
        provenance_document,
        provenance_hash,
        legacy_oracle_entry,
        registry_hash,
    ) = validate_retained_legacy_oracle_snapshot(
        evidence["legacyOracle"],
        case,
        fixture,
        reference_revision,
        root=root,
    )

    runs = evidence["runs"]
    if not isinstance(runs, dict):
        raise ValueError(f"{case_id} evidence runs must be an object")
    run_fields = {"legacy", "xplatRed", "xplatGreen"}
    require_fields(
        runs,
        run_fields,
        f"{case_id} evidence runs",
        allowed=run_fields,
    )
    legacy_run, legacy_run_hash = load_content_addressed_document(
        runs["legacy"],
        "tests/parity/evidence/runs",
        f"{case_id} Legacy run",
        root=root,
    )
    xplat_red_run, red_run_hash = load_content_addressed_document(
        runs["xplatRed"],
        "tests/parity/evidence/runs",
        f"{case_id} red XPlat run",
        root=root,
    )
    test_reports = evidence["testReports"]
    if not isinstance(test_reports, dict):
        raise ValueError(
            f"{case_id} evidence testReports must be an object"
        )
    report_fields = {"legacy", "xplatRed", "xplatGreen"}
    require_fields(
        test_reports,
        report_fields,
        f"{case_id} evidence testReports",
        allowed=report_fields,
    )
    legacy_report_path, _, legacy_report_hash = (
        load_content_addressed_file(
            test_reports["legacy"],
            "tests/parity/evidence/test-reports",
            ".trx",
            f"{case_id} Legacy test report",
            root=root,
        )
    )
    _, _, validated_legacy_report_hash = validate_test_report(
        legacy_report_path,
        legacy_run,
        "legacy",
        root=root,
        retained=True,
    )
    if validated_legacy_report_hash != legacy_report_hash:
        raise ValueError(
            f"{case_id} Legacy test report changed during validation"
        )
    red_report_path, _, red_report_hash = (
        load_content_addressed_file(
            test_reports["xplatRed"],
            "tests/parity/evidence/test-reports",
            ".trx",
            f"{case_id} red XPlat test report",
            root=root,
        )
    )
    _, _, validated_red_report_hash = validate_test_report(
        red_report_path,
        xplat_red_run,
        "xplat",
        root=root,
        retained=True,
    )
    if validated_red_report_hash != red_report_hash:
        raise ValueError(
            f"{case_id} red XPlat test report changed during validation"
        )
    executions = evidence["executions"]
    if not isinstance(executions, dict):
        raise ValueError(
            f"{case_id} evidence executions must be an object"
        )
    execution_fields = {"legacy", "xplatRed", "xplatGreen"}
    require_fields(
        executions,
        execution_fields,
        f"{case_id} evidence executions",
        allowed=execution_fields,
    )
    legacy_execution_path, _, legacy_execution_hash = (
        load_content_addressed_file(
            executions["legacy"],
            "tests/parity/evidence/executions",
            ".json",
            f"{case_id} Legacy execution envelope",
            root=root,
        )
    )
    _, _, validated_legacy_execution_hash = validate_execution_envelope(
        legacy_execution_path,
        legacy_run,
        legacy_run_hash,
        legacy_report_hash,
        "legacy",
        root=root,
        retained=True,
    )
    if validated_legacy_execution_hash != legacy_execution_hash:
        raise ValueError(
            f"{case_id} Legacy execution envelope changed during validation"
        )
    red_execution_path, _, red_execution_hash = (
        load_content_addressed_file(
            executions["xplatRed"],
            "tests/parity/evidence/executions",
            ".json",
            f"{case_id} red XPlat execution envelope",
            root=root,
        )
    )
    _, _, validated_red_execution_hash = validate_execution_envelope(
        red_execution_path,
        xplat_red_run,
        red_run_hash,
        red_report_hash,
        "xplat",
        root=root,
        retained=True,
    )
    if validated_red_execution_hash != red_execution_hash:
        raise ValueError(
            f"{case_id} red XPlat execution envelope changed during "
            "validation"
        )
    legacy_result, legacy_context = validate_run_document(
        legacy_run,
        "legacy",
        case,
        fixture,
        fixture_hash,
        reference_revision,
        expected_platform="windows",
        expected_adapter=case["targetAdapters"][0],
        expected_legacy_oracle_entry=legacy_oracle_entry,
        registry_sha256=registry_hash,
    )
    red_result, red_context = validate_run_document(
        xplat_red_run,
        "xplat",
        case,
        fixture,
        fixture_hash,
        reference_revision,
        expected_adapter=case["targetAdapters"][1],
        require_outcome="functional-divergence",
    )
    if legacy_context["xplat"] != red_context["xplat"]:
        raise ValueError(
            f"{case_id} Legacy and red XPlat runs used different "
            "XPlat revisions"
        )
    if legacy_context["xplat"] != provenance_document["xplat"]:
        raise ValueError(
            f"{case_id} Legacy run XPlat context does not match provenance"
        )
    if legacy_result["outcome"] != "passed":
        raise ValueError(f"{case_id} Legacy run is not green")
    if legacy_result["observedValues"] != fixture["values"]:
        raise ValueError(
            f"{case_id} Legacy run values do not match the fixture"
        )

    if red_result["failureCode"] != case["assertions"][
        "functionalDivergenceCode"
    ]:
        raise ValueError(
            f"{case_id} red run failureCode does not match the registered "
            "functional divergence"
        )
    red_platform = red_context["platform"]
    if red_platform not in case["platforms"]:
        raise ValueError(
            f"{case_id} red XPlat run platform is outside the case"
        )
    red_xplat = red_context["xplat"]
    validate_retained_xplat_revision(
        red_xplat["revision"],
        red_xplat["tree"],
        f"{case_id} red XPlat run",
        root=root,
    )

    green_references = runs["xplatGreen"]
    if not isinstance(green_references, dict):
        raise ValueError(
            f"{case_id} xplatGreen runs must be a platform map"
        )
    unsupported_green_platforms = sorted(
        set(green_references) - set(case["platforms"])
    )
    if unsupported_green_platforms:
        raise ValueError(
            f"{case_id} green runs claim unsupported platforms: "
            + ", ".join(unsupported_green_platforms)
        )
    green_contexts: dict[str, dict[str, Any]] = {}
    green_report_references = test_reports["xplatGreen"]
    if not isinstance(green_report_references, dict):
        raise ValueError(
            f"{case_id} xplatGreen test reports must be a platform map"
        )
    if set(green_report_references) != set(green_references):
        raise ValueError(
            f"{case_id} green run and TRX platform sets differ"
        )
    green_execution_references = executions["xplatGreen"]
    if not isinstance(green_execution_references, dict):
        raise ValueError(
            f"{case_id} xplatGreen execution envelopes must be a "
            "platform map"
        )
    if set(green_execution_references) != set(green_references):
        raise ValueError(
            f"{case_id} green run and execution platform sets differ"
        )
    for platform in sorted(green_references):
        green_run, green_run_hash = load_content_addressed_document(
            green_references[platform],
            "tests/parity/evidence/runs",
            f"{case_id} green XPlat run for {platform}",
            root=root,
        )
        green_result, green_context = validate_run_document(
            green_run,
            "xplat",
            case,
            fixture,
            fixture_hash,
            reference_revision,
            expected_platform=platform,
            expected_adapter=case["targetAdapters"][1],
            require_outcome="passed",
        )
        green_report_path, _, green_report_hash = (
            load_content_addressed_file(
                green_report_references[platform],
                "tests/parity/evidence/test-reports",
                ".trx",
                f"{case_id} green XPlat test report for {platform}",
                root=root,
            )
        )
        _, _, validated_green_report_hash = validate_test_report(
            green_report_path,
            green_run,
            "xplat",
            root=root,
            retained=True,
        )
        if validated_green_report_hash != green_report_hash:
            raise ValueError(
                f"{case_id} green XPlat test report changed during "
                f"validation for {platform}"
            )
        green_execution_path, _, green_execution_hash = (
            load_content_addressed_file(
                green_execution_references[platform],
                "tests/parity/evidence/executions",
                ".json",
                f"{case_id} green XPlat execution envelope for {platform}",
                root=root,
            )
        )
        _, _, validated_green_execution_hash = (
            validate_execution_envelope(
                green_execution_path,
                green_run,
                green_run_hash,
                green_report_hash,
                "xplat",
                root=root,
                retained=True,
            )
        )
        if validated_green_execution_hash != green_execution_hash:
            raise ValueError(
                f"{case_id} green XPlat execution envelope changed "
                f"during validation for {platform}"
            )
        if green_result["observedValues"] != fixture["values"]:
            raise ValueError(
                f"{case_id} green XPlat run values do not match the fixture"
            )
        green_xplat = green_context["xplat"]
        validate_retained_xplat_revision(
            green_xplat["revision"],
            green_xplat["tree"],
            f"{case_id} green XPlat run for {platform}",
            root=root,
        )
        validate_strict_revision_ancestry(
            red_xplat["revision"],
            green_xplat["revision"],
            f"{case_id} red-to-green history for {platform}",
            root=root,
        )
        green_contexts[platform] = green_context

    status = case["status"]
    classification = evidence["classification"]
    if status == "legacy-green-xplat-red":
        if (
            green_references
            or green_report_references
            or green_execution_references
        ):
            raise ValueError(
                f"{case_id} red status must not retain green artifacts"
            )
        if case["failureCode"] != red_result["failureCode"]:
            raise ValueError(
                f"{case_id} red run failureCode does not match the case"
            )
        if classification != "legacy-green-xplat-red":
            raise ValueError(
                f"{case_id} evidence classification does not match case"
            )
        if case["firstGreenCommit"] is not None:
            raise ValueError(
                f"{case_id} red status must not claim firstGreenCommit"
            )
        if evidence["regressionGate"] is not None:
            raise ValueError(
                f"{case_id} red status must not claim a full-suite "
                "regression gate"
            )
        return

    if classification != "both-green":
        raise ValueError(
            f"{case_id} evidence classification does not match case"
        )
    missing_green_platforms = sorted(
        set(case["platforms"]) - set(green_contexts)
    )
    if missing_green_platforms:
        raise ValueError(
            f"{case_id} both-green evidence is missing platforms: "
            + ", ".join(missing_green_platforms)
        )
    for platform, green_context in green_contexts.items():
        validate_first_green_commit(
            case["firstGreenCommit"],
            case_id,
            run_revision=green_context["xplat"]["revision"],
            run_tree=green_context["xplat"]["tree"],
            root=root,
        )
    if manifest is None:
        raise ValueError(
            f"{case_id} green evidence validation requires the full "
            "manifest"
        )
    validate_retained_regression_gate(
        evidence["regressionGate"],
        evidence,
        case,
        fixture,
        fixture_path,
        manifest,
        reference_revision,
        expected_xplat_context=next(
            iter(green_contexts.values())
        )["xplat"],
        root=root,
    )


def materialize_regression_manifest_case_snapshot(
    manifest_cases_snapshot: Any,
    current_cases: list[dict[str, Any]],
    label: str,
) -> tuple[
    list[dict[str, Any]],
    list[str],
    dict[str, dict[str, Any]],
    dict[str, dict[str, Any]],
]:
    if not isinstance(manifest_cases_snapshot, list) or not (
        manifest_cases_snapshot
    ):
        raise ValueError(
            f"{label} manifestCases must be a non-empty array"
        )
    current_cases_by_id = {
        candidate["id"]: candidate
        for candidate in current_cases
    }
    snapshot_fields = {
        "id",
        "platforms",
        "caseDefinitionSha256",
        "expectedXPlatOutcome",
        "failureCode",
    }
    historical_cases: list[dict[str, Any]] = []
    historical_ids: list[str] = []
    snapshot_by_id: dict[str, dict[str, Any]] = {}
    for snapshot in manifest_cases_snapshot:
        if not isinstance(snapshot, dict):
            raise ValueError(
                f"{label} manifestCases contains a non-object"
            )
        require_fields(
            snapshot,
            snapshot_fields,
            f"{label} manifest case",
            allowed=snapshot_fields,
        )
        historical_id = require_nonempty_string(
            snapshot["id"],
            f"{label} manifest case ID",
        )
        current_case = current_cases_by_id.get(historical_id)
        if current_case is None:
            raise ValueError(
                f"{label} references removed historical case "
                f"{historical_id}"
            )
        platforms = require_string_array(
            snapshot["platforms"],
            f"{historical_id} full-suite historical platforms",
        )
        if platforms != current_case["platforms"]:
            raise ValueError(
                f"{historical_id} full-suite historical platforms changed"
            )
        definition_hash = validate_sha256(
            snapshot["caseDefinitionSha256"],
            f"{historical_id} full-suite historical caseDefinitionSha256",
        )
        if definition_hash != case_definition_sha256(current_case):
            raise ValueError(
                f"{historical_id} full-suite historical case definition "
                "changed"
            )
        expected_outcome = snapshot["expectedXPlatOutcome"]
        if expected_outcome not in {
            "passed",
            "functional-divergence",
        }:
            raise ValueError(
                f"{historical_id} full-suite historical XPlat outcome "
                "is invalid"
            )
        if expected_outcome == "passed":
            if snapshot["failureCode"] is not None:
                raise ValueError(
                    f"{historical_id} passing full-suite historical case "
                    "claims a failureCode"
                )
            if current_case["status"] != "both-green":
                raise ValueError(
                    f"{historical_id} regressed after its retained "
                    "full-suite pass"
                )
        else:
            expected_failure_code = current_case["assertions"][
                "functionalDivergenceCode"
            ]
            if snapshot["failureCode"] != expected_failure_code:
                raise ValueError(
                    f"{historical_id} full-suite historical failureCode "
                    "is not pinned"
                )
        historical_ids.append(historical_id)
        snapshot_by_id[historical_id] = snapshot
        historical_case = copy.deepcopy(current_case)
        historical_case.update(
            {
                "xplatTestStatus": (
                    "pass"
                    if expected_outcome == "passed"
                    else "fail"
                ),
                "status": (
                    "both-green"
                    if expected_outcome == "passed"
                    else "legacy-green-xplat-red"
                ),
                "failureCode": snapshot["failureCode"],
                "firstGreenCommit": (
                    current_case["firstGreenCommit"]
                    if expected_outcome == "passed"
                    else None
                ),
            }
        )
        historical_cases.append(historical_case)
    if (
        historical_ids
        != sorted(historical_ids, key=utf16_ordinal_key)
        or len(historical_ids) != len(set(historical_ids))
    ):
        raise ValueError(
            f"{label} manifestCases are not unique and ordinally sorted"
        )
    return (
        historical_cases,
        historical_ids,
        snapshot_by_id,
        current_cases_by_id,
    )


def validate_retained_regression_gate(
    reference: Any,
    evidence: dict[str, Any],
    case: dict[str, Any],
    fixture: dict[str, Any],
    fixture_path: Path,
    manifest: dict[str, Any],
    reference_revision: str,
    *,
    expected_xplat_context: dict[str, Any],
    root: Path = ROOT,
) -> None:
    case_id = case["id"]
    gate, _ = load_content_addressed_document(
        reference,
        "tests/parity/evidence/regression-gates",
        f"{case_id} full-suite regression gate",
        root=root,
    )
    gate_fields = {
        "schemaVersion",
        "selectedCaseIds",
        "xplat",
        "oracleRegistrySha256",
        "fullSuitePackageIndexSha256",
        "legacyOracleBuildIntegration",
        "manifestCases",
        "manifestCasesSha256",
        "caseInventory",
        "runs",
        "testReports",
        "executions",
    }
    require_fields(
        gate,
        gate_fields,
        f"{case_id} full-suite regression gate",
        allowed=gate_fields,
    )
    if require_signed_integer(
        gate["schemaVersion"],
        f"{case_id} full-suite regression gate schemaVersion",
    ) != 1:
        raise ValueError(
            f"{case_id} full-suite regression gate has unsupported schema"
        )
    selected_case_ids = require_string_array(
        gate["selectedCaseIds"],
        f"{case_id} full-suite regression gate selectedCaseIds",
    )
    if (
        not selected_case_ids
        or selected_case_ids
        != sorted(selected_case_ids, key=utf16_ordinal_key)
        or len(selected_case_ids) != len(set(selected_case_ids))
        or case_id not in selected_case_ids
    ):
        raise ValueError(
            f"{case_id} full-suite regression gate selectedCaseIds are "
            "invalid"
        )
    manifest_cases_snapshot = gate["manifestCases"]
    (
        historical_cases,
        historical_ids,
        snapshot_by_id,
        current_cases_by_id,
    ) = materialize_regression_manifest_case_snapshot(
        manifest_cases_snapshot,
        manifest["cases"],
        f"{case_id} full-suite regression gate",
    )
    snapshot_hash = validate_sha256(
        gate["manifestCasesSha256"],
        f"{case_id} full-suite regression gate manifestCasesSha256",
    )
    if snapshot_hash != canonical_json_sha256(manifest_cases_snapshot):
        raise ValueError(
            f"{case_id} full-suite regression gate manifestCasesSha256 "
            "does not match its historical inventory"
        )
    if any(
        selected_id not in set(historical_ids)
        or current_cases_by_id[selected_id]["status"] != "both-green"
        or snapshot_by_id[selected_id]["expectedXPlatOutcome"]
        != "passed"
        for selected_id in selected_case_ids
    ):
        raise ValueError(
            f"{case_id} full-suite regression gate selectedCaseIds do not "
            "identify promoted manifest cases"
        )
    selected_cases = [
        current_cases_by_id[selected_id]
        for selected_id in selected_case_ids
    ]
    expected_keys = required_full_suite_gate_keys(selected_cases)
    case_inventory = gate["caseInventory"]
    runs = gate["runs"]
    test_reports = gate["testReports"]
    executions = gate["executions"]
    for label, artifact_map in (
        ("caseInventory", case_inventory),
        ("runs", runs),
        ("testReports", test_reports),
        ("executions", executions),
    ):
        if not isinstance(artifact_map, dict):
            raise ValueError(
                f"{case_id} full-suite regression gate {label} must be "
                "a platform/target map"
            )
        if set(artifact_map) != expected_keys:
            raise ValueError(
                f"{case_id} full-suite regression gate {label} keys do "
                "not exactly match the required platform/target set"
            )
    gate_xplat = gate["xplat"]
    if not isinstance(gate_xplat, dict):
        raise ValueError(
            f"{case_id} full-suite regression gate xplat must be an object"
        )
    repository_fields = {"revision", "tree", "clean"}
    require_fields(
        gate_xplat,
        repository_fields,
        f"{case_id} full-suite regression gate xplat",
        allowed=repository_fields,
    )
    validate_git_context(
        gate_xplat,
        f"{case_id} full-suite regression gate XPlat",
    )
    if gate_xplat != expected_xplat_context:
        raise ValueError(
            f"{case_id} selected Baseline and retained full-suite gate "
            "used different XPlat revisions or trees"
        )
    registry_hash = validate_sha256(
        gate["oracleRegistrySha256"],
        f"{case_id} full-suite regression gate oracleRegistrySha256",
    )
    validate_sha256(
        gate["fullSuitePackageIndexSha256"],
        f"{case_id} full-suite regression gate "
        "fullSuitePackageIndexSha256",
    )
    build_integration = gate["legacyOracleBuildIntegration"]
    if not isinstance(build_integration, dict):
        raise ValueError(
            f"{case_id} full-suite build integration must be an object"
        )
    require_fields(
        build_integration,
        {"testReport", "execution"},
        f"{case_id} full-suite build integration",
        allowed={"testReport", "execution"},
    )
    (
        integration_report_path,
        _,
        integration_report_hash,
    ) = load_content_addressed_file(
        build_integration["testReport"],
        "tests/parity/evidence/test-reports",
        ".trx",
        f"{case_id} full-suite build integration test report",
        root=root,
    )
    validate_legacy_oracle_build_integration_report(
        integration_report_path
    )
    (
        integration_execution_path,
        _,
        integration_execution_hash,
    ) = load_content_addressed_file(
        build_integration["execution"],
        "tests/parity/evidence/executions",
        ".json",
        f"{case_id} full-suite build integration execution",
        root=root,
    )
    historical_manifest = {
        "reference": manifest["reference"],
        "cases": historical_cases,
    }
    validated_documents: dict[str, dict[str, Any]] = {}
    for key in sorted(expected_keys, key=utf16_ordinal_key):
        platform, target_name = key.split("/", 1)
        target = target_name.lower()
        expected_case_ids = sorted(
            candidate["id"]
            for candidate in historical_cases
            if platform in candidate["platforms"]
        )
        declared_inventory = require_string_array(
            case_inventory[key],
            f"{case_id} full-suite regression gate inventory for {key}",
        )
        if declared_inventory != expected_case_ids:
            raise ValueError(
                f"{case_id} full-suite regression gate inventory for "
                f"{key} is incomplete or stale"
            )
        run, run_hash = load_content_addressed_document(
            runs[key],
            "tests/parity/evidence/runs",
            f"{case_id} full-suite run for {key}",
            root=root,
        )
        if run.get("expectedParityIds") != declared_inventory:
            raise ValueError(
                f"{case_id} full-suite run for {key} does not match its "
                "declared case inventory"
            )
        context = run.get("runContext")
        if (
            not isinstance(context, dict)
            or context.get("platform") != platform
            or context.get("xplat") != gate_xplat
        ):
            raise ValueError(
                f"{case_id} full-suite run for {key} has an invalid "
                "platform or XPlat context"
            )
        report_path, _, report_hash = load_content_addressed_file(
            test_reports[key],
            "tests/parity/evidence/test-reports",
            ".trx",
            f"{case_id} full-suite test report for {key}",
            root=root,
        )
        _, _, validated_report_hash = validate_test_report(
            report_path,
            run,
            target,
            root=root,
            retained=True,
        )
        if validated_report_hash != report_hash:
            raise ValueError(
                f"{case_id} full-suite test report changed during "
                f"validation for {key}"
            )
        execution_path, _, execution_hash = (
            load_content_addressed_file(
                executions[key],
                "tests/parity/evidence/executions",
                ".json",
                f"{case_id} full-suite execution envelope for {key}",
                root=root,
            )
        )
        _, _, validated_execution_hash = validate_execution_envelope(
            execution_path,
            run,
            run_hash,
            report_hash,
            target,
            root=root,
            retained=True,
        )
        if validated_execution_hash != execution_hash:
            raise ValueError(
                f"{case_id} full-suite execution envelope changed during "
                f"validation for {key}"
            )
        if target == "xplat":
            validate_live_run_document_for_manifest(
                historical_manifest,
                run,
                "xplat",
                root=root,
            )
        else:
            if platform != "windows":
                raise ValueError(
                    f"{case_id} retained Legacy full-suite gate is not "
                    "Windows"
                )
            for legacy_case in (
                candidate
                for candidate in historical_cases
                if "windows" in candidate["platforms"]
            ):
                legacy_fixture_path, legacy_fixture, _ = validate_fixture(
                    legacy_case,
                    reference_revision,
                    require_schema_v2=True,
                    root=root,
                )
                legacy_result = find_run_result(
                    run,
                    legacy_case["id"],
                    "legacy",
                )
                binding = legacy_result.get("legacyOracle")
                validate_legacy_result_binding_shape(
                    binding,
                    legacy_case["id"],
                )
                if (
                    legacy_result.get("outcome") != "passed"
                    or binding["executableSha256"]
                    != legacy_fixture["oracleExecutableSha256"]
                ):
                    raise ValueError(
                        f"{legacy_case['id']} retained Legacy full-suite "
                        "result is not a pinned pass"
                    )
                entry = {
                    field: binding[field]
                    for field in (
                        "adapterId",
                        "versionId",
                        "source",
                        "sourceSha256",
                        "buildRecipe",
                        "buildRecipeSha256",
                        "executableSha256",
                        "provenance",
                        "provenanceSha256",
                    )
                }
                validate_run_document(
                    run,
                    "legacy",
                    legacy_case,
                    legacy_fixture,
                    sha256_file(legacy_fixture_path),
                    reference_revision,
                    expected_platform="windows",
                    expected_adapter=legacy_case["targetAdapters"][0],
                    expected_case_ids=expected_case_ids,
                    require_outcome="passed",
                    expected_legacy_oracle_entry=entry,
                    registry_sha256=registry_hash,
                )
        validated_documents[key] = run
    _, validated_integration_execution_hash = (
        validate_legacy_oracle_build_integration_execution(
            integration_execution_path,
            test_report_sha256=integration_report_hash,
            registry_sha256=registry_hash,
            expected_selected_case_ids=sorted(
                historical_case["id"]
                for historical_case in historical_cases
                if "windows" in historical_case["platforms"]
            ),
            expected_run_context=validated_documents[
                "windows/Legacy"
            ]["runContext"],
        )
    )
    if (
        integration_execution_hash
        != validated_integration_execution_hash
    ):
        raise ValueError(
            f"{case_id} full-suite build integration execution changed "
            "during validation"
        )
    if (
        validated_documents["windows/Legacy"]["expectedParityIds"]
        != validated_documents["windows/XPlat"]["expectedParityIds"]
    ):
        raise ValueError(
            f"{case_id} Windows Legacy/XPlat full-suite inventories differ"
        )
    fixture_hash = sha256_file(fixture_path)
    for platform in case["platforms"]:
        selected_result = find_run_result(
            validated_documents[f"{platform}/XPlat"],
            case_id,
            "xplat",
        )
        if (
            selected_result["fixtureSha256"] != fixture_hash
            or selected_result["observedValues"] != fixture["values"]
        ):
            raise ValueError(
                f"{case_id} retained full-suite gate does not bind the "
                f"selected passing result for {platform}"
            )


def validate_lf_json(path: Path, label: str) -> None:
    content = path.read_bytes()
    if b"\r" in content:
        raise ValueError(
            f"{label} must use LF-only JSON bytes for stable hashing"
        )


def load_content_addressed_document(
    reference: Any,
    allowed_root: str,
    label: str,
    *,
    root: Path = ROOT,
) -> tuple[dict[str, Any], str]:
    if not isinstance(reference, dict):
        raise ValueError(f"{label} reference must be an object")
    fields = {"path", "sha256"}
    require_fields(
        reference,
        fields,
        f"{label} reference",
        allowed=fields,
    )
    declared_hash = validate_sha256(
        reference["sha256"],
        f"{label} sha256",
    ).lower()
    path = resolve_repo_file(
        reference["path"],
        allowed_root,
        f"{label} path",
        root=root,
    )
    require_json_suffix(path, label)
    expected_name = f"{declared_hash}.json"
    if path.name != expected_name:
        raise ValueError(
            f"{label} path is not content-addressed by its SHA-256"
        )
    validate_lf_json(path, label)
    actual_hash = sha256_file(path)
    if actual_hash != declared_hash:
        raise ValueError(f"{label} SHA-256 does not match its content")
    return load_json(path), actual_hash


def load_content_addressed_file(
    reference: Any,
    allowed_root: str,
    suffix: str,
    label: str,
    *,
    root: Path = ROOT,
) -> tuple[Path, bytes, str]:
    if not isinstance(reference, dict):
        raise ValueError(f"{label} reference must be an object")
    fields = {"path", "sha256"}
    require_fields(
        reference,
        fields,
        f"{label} reference",
        allowed=fields,
    )
    declared_hash = validate_sha256(
        reference["sha256"],
        f"{label} sha256",
    )
    path_text = require_nonempty_string(
        reference["path"],
        f"{label} path",
    )
    path = resolve_repo_file(
        path_text,
        allowed_root,
        f"{label} path",
        root=root,
    )
    if path.name != f"{declared_hash}{suffix}":
        raise ValueError(
            f"{label} path is not content-addressed by its SHA-256"
        )
    raw = path.read_bytes()
    actual_hash = hashlib.sha256(raw).hexdigest()
    if actual_hash != declared_hash:
        raise ValueError(f"{label} SHA-256 does not match its content")
    return path, raw, actual_hash


def validate_run_document(
    document: dict[str, Any],
    target: str,
    case: dict[str, Any],
    fixture: dict[str, Any],
    fixture_hash: str,
    reference_revision: str,
    *,
    expected_platform: str | None = None,
    expected_adapter: str | None = None,
    expected_case_ids: list[str] | None = None,
    require_outcome: str | None = None,
    expected_legacy_oracle_entry: dict[str, Any] | None = None,
    registry_sha256: str | None = None,
) -> tuple[dict[str, Any], dict[str, Any]]:
    case_id = case["id"]
    root_fields = {
        "schemaVersion",
        "target",
        "runContext",
        "expectedParityIds",
        "results",
    }
    require_fields(
        document,
        root_fields,
        f"{case_id} {target} run",
        allowed=root_fields,
    )
    if require_signed_integer(
        document["schemaVersion"],
        f"{case_id} {target} run schemaVersion",
    ) != 1:
        raise ValueError(f"{case_id} {target} run has unsupported schema")
    if document["target"] != target:
        raise ValueError(f"{case_id} run target is not {target}")

    context = validate_run_context(
        document["runContext"],
        target,
        case_id,
        fixture,
        reference_revision,
        expected_platform=expected_platform,
    )
    expected_ids = require_string_array(
        document["expectedParityIds"],
        f"{case_id} {target} run expectedParityIds",
    )
    if (
        expected_ids != sorted(expected_ids)
        or len(expected_ids) != len(set(expected_ids))
    ):
        raise ValueError(
            f"{case_id} {target} run expectedParityIds must be "
            "unique and ordinally sorted"
        )
    if (
        expected_case_ids is not None
        and expected_ids != expected_case_ids
    ):
        raise ValueError(
            f"{case_id} {target} run expectedParityIds do not match "
            "the active manifest cases"
        )
    if case_id not in expected_ids:
        raise ValueError(
            f"{case_id} is absent from the {target} run expected IDs"
        )
    results = document["results"]
    if not isinstance(results, list):
        raise ValueError(f"{case_id} {target} run results must be an array")
    result_ids: list[str] = []
    matching_result: dict[str, Any] | None = None
    for result in results:
        result_id = (
            result.get("parityId")
            if isinstance(result, dict)
            else None
        )
        validated = validate_run_result(
            result,
            target,
            case_id,
            case=case if result_id == case_id else None,
            fixture_hash=(
                fixture_hash if result_id == case_id else None
            ),
            expected_adapter=(
                expected_adapter if result_id == case_id else None
            ),
            expected_legacy_oracle_entry=(
                expected_legacy_oracle_entry
                if result_id == case_id
                else None
            ),
            registry_sha256=(
                registry_sha256 if result_id == case_id else None
            ),
        )
        result_ids.append(validated["parityId"])
        if validated["parityId"] == case_id:
            matching_result = validated
    if result_ids != expected_ids:
        raise ValueError(
            f"{case_id} {target} run result coverage is incomplete or stale"
        )
    if matching_result is None:
        raise ValueError(f"{case_id} has no {target} run result")
    if (
        require_outcome is not None
        and matching_result["outcome"] != require_outcome
    ):
        raise ValueError(
            f"{case_id} {target} run outcome is not {require_outcome}"
        )
    if target == "legacy":
        if matching_result["outcome"] != "passed":
            raise ValueError(f"{case_id} Legacy run is not green")
        if matching_result["firstDivergence"] is not None:
            raise ValueError(
                f"{case_id} passing Legacy run has a divergence"
            )
    elif matching_result["outcome"] == "functional-divergence":
        expected_divergence = find_first_divergence(
            fixture["values"],
            matching_result["observedValues"],
        )
        if (
            expected_divergence is None
            or matching_result["firstDivergence"]
            != expected_divergence
        ):
            raise ValueError(
                f"{case_id} red XPlat run has invalid first divergence"
            )
        if (
            matching_result["failureCode"]
            != case["assertions"]["functionalDivergenceCode"]
        ):
            raise ValueError(
                f"{case_id} red XPlat run has an unregistered "
                "functional divergence code"
            )
    elif matching_result["outcome"] == "passed":
        if matching_result["observedValues"] != fixture["values"]:
            raise ValueError(
                f"{case_id} passing XPlat run values do not match fixture"
            )
    return matching_result, context


def validate_run_context(
    context: Any,
    target: str,
    case_id: str,
    fixture: dict[str, Any],
    reference_revision: str,
    *,
    expected_platform: str | None,
) -> dict[str, Any]:
    if not isinstance(context, dict):
        raise ValueError(f"{case_id} {target} runContext must be an object")
    fields = {
        "platform",
        "processArchitecture",
        "runtimeIdentifier",
        "framework",
        "xplat",
        "legacy",
    }
    require_fields(
        context,
        fields,
        f"{case_id} {target} runContext",
        allowed=fields,
    )
    platform = require_nonempty_string(
        context["platform"],
        f"{case_id} {target} runContext platform",
    )
    if platform not in SUPPORTED_PLATFORMS:
        raise ValueError(
            f"{case_id} {target} runContext platform is unsupported"
        )
    if expected_platform is not None and platform != expected_platform:
        raise ValueError(
            f"{case_id} {target} runContext platform is not "
            f"{expected_platform}"
        )
    process_architecture = require_nonempty_string(
        context["processArchitecture"],
        f"{case_id} {target} runContext processArchitecture",
    ).lower()
    if process_architecture not in SUPPORTED_PROCESS_ARCHITECTURES:
        raise ValueError(
            f"{case_id} {target} runContext processArchitecture is "
            "unsupported"
        )
    runtime_identifier = require_nonempty_string(
        context["runtimeIdentifier"],
        f"{case_id} {target} runContext runtimeIdentifier",
    ).lower()
    if not runtime_identifier.startswith(
        RUNTIME_IDENTIFIER_PREFIXES[platform]
    ):
        raise ValueError(
            f"{case_id} {target} runtimeIdentifier does not match platform"
        )
    if not runtime_identifier.endswith("-" + process_architecture):
        raise ValueError(
            f"{case_id} {target} runtimeIdentifier does not match "
            "processArchitecture"
        )
    require_nonempty_string(
        context["framework"],
        f"{case_id} {target} runContext framework",
    )
    xplat = context["xplat"]
    if not isinstance(xplat, dict):
        raise ValueError(
            f"{case_id} {target} XPlat runContext must be an object"
        )
    repository_fields = {"revision", "tree", "clean"}
    require_fields(
        xplat,
        repository_fields,
        f"{case_id} {target} XPlat runContext",
        allowed=repository_fields,
    )
    validate_git_context(xplat, f"{case_id} {target} XPlat")

    legacy = context["legacy"]
    if target == "xplat":
        if legacy is not None:
            raise ValueError(
                f"{case_id} XPlat run must not claim Legacy runContext"
            )
        return context
    if platform != "windows":
        raise ValueError(
            f"{case_id} Legacy CE runContext must use windows"
        )
    if not isinstance(legacy, dict):
        raise ValueError(
            f"{case_id} Legacy runContext must be an object"
        )
    legacy_fields = {"revision", "tree", "clean"}
    require_fields(
        legacy,
        legacy_fields,
        f"{case_id} Legacy runContext",
        allowed=legacy_fields,
    )
    validate_git_context(legacy, f"{case_id} Legacy")
    if (
        legacy["revision"] != reference_revision
        or legacy["tree"] != fixture["tree"]
    ):
        raise ValueError(
            f"{case_id} Legacy runContext does not match the pinned reference"
        )
    return context


def validate_git_context(context: dict[str, Any], label: str) -> None:
    for field in ("revision", "tree"):
        value = require_nonempty_string(
            context[field],
            f"{label} runContext {field}",
        )
        if not COMMIT_PATTERN.fullmatch(value):
            raise ValueError(
                f"{label} runContext {field} must be a full Git object ID"
            )
    if context["clean"] is not True:
        raise ValueError(f"{label} runContext must be clean")


def validate_run_result(
    result: Any,
    target: str,
    case_id: str,
    *,
    case: dict[str, Any] | None = None,
    fixture_hash: str | None = None,
    expected_adapter: str | None = None,
    expected_legacy_oracle_entry: dict[str, Any] | None = None,
    registry_sha256: str | None = None,
) -> dict[str, Any]:
    if not isinstance(result, dict):
        raise ValueError(f"{case_id} {target} run result must be an object")
    fields = {
        "parityId",
        "acceptanceTestName",
        "target",
        "outcome",
        "failureCode",
        "evidenceSource",
        "observedValues",
        "observedValueCount",
        "observedValuesSha256",
        "firstDivergence",
        "executionCount",
        "adapter",
        "caseDefinitionSha256",
        "fixtureSha256",
        "legacyOracle",
    }
    require_fields(
        result,
        fields,
        f"{case_id} {target} run result",
        allowed=fields,
    )
    result_id = require_nonempty_string(
        result["parityId"],
        f"{case_id} {target} result parityId",
    )
    acceptance_test_name = require_nonempty_string(
        result["acceptanceTestName"],
        f"{result_id} {target} result acceptanceTestName",
    )
    if acceptance_test_name != f"parity:{result_id}()":
        raise ValueError(
            f"{result_id} {target} acceptanceTestName must be "
            f"parity:{result_id}()"
        )
    if result["target"] != target:
        raise ValueError(f"{result_id} result target is not {target}")
    outcome = result["outcome"]
    if outcome not in {
        "passed",
        "functional-divergence",
        "not-runnable",
    }:
        raise ValueError(f"{result_id} has invalid {target} outcome")
    if outcome == "not-runnable":
        raise ValueError(
            f"{result_id} {target} result was not runnable"
        )
    adapter = require_nonempty_string(
        result["adapter"],
        f"{result_id} {target} adapter",
    )
    case_definition_hash = validate_sha256(
        result["caseDefinitionSha256"],
        f"{result_id} {target} caseDefinitionSha256",
    ).lower()
    result_fixture_hash = validate_sha256(
        result["fixtureSha256"],
        f"{result_id} {target} fixtureSha256",
    ).lower()
    if case is not None:
        if case_definition_hash != case_definition_sha256(case):
            raise ValueError(
                f"{result_id} {target} caseDefinitionSha256 is stale"
            )
        if fixture_hash is None or result_fixture_hash != fixture_hash:
            raise ValueError(
                f"{result_id} {target} fixtureSha256 is stale"
            )
        if expected_adapter is None or adapter != expected_adapter:
            raise ValueError(
                f"{result_id} {target} adapter does not match the case"
            )
    legacy_oracle = result["legacyOracle"]
    if target == "legacy":
        validate_legacy_result_binding_shape(
            legacy_oracle,
            result_id,
        )
        if case is not None:
            if (
                expected_legacy_oracle_entry is None
                or registry_sha256 is None
            ):
                raise ValueError(
                    f"{result_id} Legacy result has no validated runtime "
                    "oracle registry binding"
                )
            validate_legacy_result_binding(
                legacy_oracle,
                result_id,
                case,
                expected_legacy_oracle_entry,
                registry_sha256,
            )
    elif legacy_oracle is not None:
        raise ValueError(
            f"{result_id} XPlat result must not claim a legacyOracle "
            "binding"
        )
    evidence_source = require_nonempty_string(
        result["evidenceSource"],
        f"{result_id} {target} evidenceSource",
    )
    if (
        case is not None
        and target == "legacy"
        and evidence_source != case["legacyOracle"]["source"]
    ):
        raise ValueError(
            f"{result_id} Legacy evidenceSource does not match the case "
            "legacyOracle source"
        )
    values = result["observedValues"]
    if (
        not isinstance(values, list)
        or any(not isinstance(value, str) for value in values)
    ):
        raise ValueError(
            f"{result_id} {target} observedValues must be a string array"
        )
    count = require_signed_integer(
        result["observedValueCount"],
        f"{result_id} {target} observedValueCount",
        minimum=0,
    )
    if count != len(values):
        raise ValueError(
            f"{result_id} {target} observedValueCount is invalid"
        )
    digest = validate_sha256(
        result["observedValuesSha256"],
        f"{result_id} {target} observedValuesSha256",
    )
    if digest.lower() != canonical_json_sha256(values):
        raise ValueError(
            f"{result_id} {target} observedValuesSha256 is stale"
        )
    if require_signed_integer(
        result["executionCount"],
        f"{result_id} {target} executionCount",
        minimum=0,
    ) != 1:
        raise ValueError(
            f"{result_id} {target} result did not execute exactly once"
        )
    if outcome == "passed":
        if result["failureCode"] is not None:
            raise ValueError(
                f"{result_id} passing {target} result has a failureCode"
            )
        if result["firstDivergence"] is not None:
            raise ValueError(
                f"{result_id} passing {target} result has a divergence"
            )
    else:
        require_nonempty_string(
            result["failureCode"],
            f"{result_id} {target} failureCode",
        )
        validate_first_divergence(
            result["firstDivergence"],
            result_id,
            target,
        )
    return result


LEGACY_RESULT_BINDING_FIELDS = {
    "adapterId",
    "versionId",
    "source",
    "sourceSha256",
    "buildRecipe",
    "buildRecipeSha256",
    "executableSha256",
    "provenance",
    "provenanceSha256",
    "registrySha256",
}


def validate_legacy_result_binding_shape(
    binding: Any,
    case_id: str,
) -> None:
    if not isinstance(binding, dict):
        raise ValueError(
            f"{case_id} Legacy result legacyOracle must be an object"
        )
    require_fields(
        binding,
        LEGACY_RESULT_BINDING_FIELDS,
        f"{case_id} Legacy result legacyOracle",
        allowed=LEGACY_RESULT_BINDING_FIELDS,
    )
    for field in (
        "adapterId",
        "versionId",
        "source",
        "buildRecipe",
        "provenance",
    ):
        require_nonempty_string(
            binding[field],
            f"{case_id} Legacy result legacyOracle {field}",
        )
    for field in (
        "sourceSha256",
        "buildRecipeSha256",
        "executableSha256",
        "provenanceSha256",
        "registrySha256",
    ):
        validate_sha256(
            binding[field],
            f"{case_id} Legacy result legacyOracle {field}",
        )


def validate_legacy_result_binding(
    binding: dict[str, Any],
    case_id: str,
    case: dict[str, Any],
    entry: dict[str, Any],
    registry_sha256: str,
) -> None:
    descriptor = case["legacyOracle"]
    expected = {
        "adapterId": descriptor["adapterId"],
        "versionId": descriptor["versionId"],
        "source": descriptor["source"],
        "sourceSha256": descriptor["sourceSha256"],
        "buildRecipe": descriptor["buildRecipe"],
        "buildRecipeSha256": descriptor["buildRecipeSha256"],
        "executableSha256": entry["executableSha256"],
        "provenance": entry["provenance"],
        "provenanceSha256": entry["provenanceSha256"],
        "registrySha256": registry_sha256,
    }
    if binding != expected:
        raise ValueError(
            f"{case_id} Legacy result legacyOracle binding does not "
            "match the immutable descriptor and supplied registry"
        )


def validate_first_divergence(
    divergence: Any,
    case_id: str,
    target: str,
) -> None:
    if not isinstance(divergence, dict):
        raise ValueError(
            f"{case_id} failed {target} result needs firstDivergence"
        )
    fields = {"index", "expected", "actual"}
    require_fields(
        divergence,
        fields,
        f"{case_id} {target} firstDivergence",
        allowed=fields,
    )
    index = divergence["index"]
    if not isinstance(index, int) or isinstance(index, bool) or index < 0:
        raise ValueError(
            f"{case_id} {target} firstDivergence index is invalid"
        )
    for field in ("expected", "actual"):
        value = divergence[field]
        if value is not None and not isinstance(value, str):
            raise ValueError(
                f"{case_id} {target} firstDivergence {field} is invalid"
            )
    if divergence["expected"] is None and divergence["actual"] is None:
        raise ValueError(
            f"{case_id} {target} firstDivergence has no values"
        )


def find_first_divergence(
    expected: list[str],
    actual: list[str],
) -> dict[str, Any] | None:
    for index in range(max(len(expected), len(actual))):
        expected_value = expected[index] if index < len(expected) else None
        actual_value = actual[index] if index < len(actual) else None
        if expected_value != actual_value:
            return {
                "index": index,
                "expected": expected_value,
                "actual": actual_value,
            }
    return None


def validate_legacy_provenance(
    document: dict[str, Any],
    case_id: str,
    case: dict[str, Any],
    fixture: dict[str, Any],
    reference_revision: str,
    *,
    expected_selected_case_ids: list[str] | None = None,
    root: Path = ROOT,
) -> None:
    legacy_reference, _ = load_legacy_reference(
        reference_revision,
        root=root,
    )
    root_fields = {
        "schemaVersion",
        "adapterId",
        "versionId",
        "source",
        "sourceSha256",
        "buildRecipe",
        "buildRecipeSha256",
        "selectedCaseIds",
        "reference",
        "legacy",
        "xplat",
        "oracle",
        "toolchain",
        "build",
        "manifest",
        "observations",
    }
    require_fields(
        document,
        root_fields,
        f"{case_id} Legacy provenance",
        allowed=root_fields,
    )
    if require_signed_integer(
        document["schemaVersion"],
        f"{case_id} Legacy provenance schemaVersion",
    ) != 1:
        raise ValueError(
            f"{case_id} Legacy provenance has unsupported schema"
        )
    descriptor = case["legacyOracle"]
    for field in ("adapterId", "versionId", "source", "buildRecipe"):
        actual = require_nonempty_string(
            document[field],
            f"{case_id} Legacy provenance {field}",
        )
        if actual != descriptor[field]:
            raise ValueError(
                f"{case_id} Legacy provenance {field} is not pinned"
            )
    for field, expected in (
        ("sourceSha256", descriptor["sourceSha256"]),
        ("buildRecipeSha256", descriptor["buildRecipeSha256"]),
    ):
        actual = validate_sha256(
            document[field],
            f"{case_id} Legacy provenance {field}",
        )
        if actual != expected:
            raise ValueError(
                f"{case_id} Legacy provenance {field} is not pinned"
            )
    selected_case_ids = require_string_array(
        document["selectedCaseIds"],
        f"{case_id} Legacy provenance selectedCaseIds",
    )
    if (
        len(selected_case_ids) != len(set(selected_case_ids))
        or selected_case_ids
        != sorted(selected_case_ids, key=utf16_ordinal_key)
        or case_id not in selected_case_ids
        or (
            expected_selected_case_ids is not None
            and selected_case_ids != expected_selected_case_ids
        )
    ):
        raise ValueError(
            f"{case_id} Legacy provenance selectedCaseIds are invalid"
        )
    reference = document["reference"]
    if not isinstance(reference, dict):
        raise ValueError(
            f"{case_id} Legacy provenance reference must be an object"
        )
    reference_fields = {
        "definition",
        "definitionSha256",
        "bundle",
        "bundleSha256",
    }
    require_fields(
        reference,
        reference_fields,
        f"{case_id} Legacy provenance reference",
        allowed=reference_fields,
    )
    for field, expected in (
        ("definitionSha256", fixture["referenceDefinitionSha256"]),
        ("bundleSha256", legacy_reference["bundleSha256"]),
    ):
        actual = validate_sha256(
            reference[field],
            f"{case_id} Legacy provenance {field}",
        )
        if actual != expected:
            raise ValueError(
                f"{case_id} Legacy provenance {field} is not pinned"
            )
    provenance_definition = require_nonempty_string(
        reference["definition"],
        f"{case_id} Legacy provenance definition",
    ).replace("\\", "/")
    if not provenance_definition.lower().endswith(
        "/tests/parity/legacy-reference.json"
    ) and provenance_definition.lower() != (
        "tests/parity/legacy-reference.json"
    ):
        raise ValueError(
            f"{case_id} Legacy provenance definition path is not pinned"
        )
    provenance_bundle = require_nonempty_string(
        reference["bundle"],
        f"{case_id} Legacy provenance bundle",
    ).replace("\\", "/")
    expected_bundle = legacy_reference["bundle"].replace("\\", "/")
    if not provenance_bundle.lower().endswith(
        "/" + expected_bundle.lower()
    ) and provenance_bundle.lower() != expected_bundle.lower():
        raise ValueError(
            f"{case_id} Legacy provenance bundle path is not pinned"
        )

    legacy = document["legacy"]
    xplat = document["xplat"]
    if not isinstance(legacy, dict) or not isinstance(xplat, dict):
        raise ValueError(
            f"{case_id} Legacy provenance repository contexts are invalid"
        )
    require_fields(
        legacy,
        {"repository", "revision", "tree", "root", "clean"},
        f"{case_id} provenance legacy",
        allowed={"repository", "revision", "tree", "root", "clean"},
    )
    require_fields(
        xplat,
        {"revision", "tree", "clean"},
        f"{case_id} provenance xplat",
        allowed={"revision", "tree", "clean"},
    )
    for context, label in ((legacy, "legacy"), (xplat, "xplat")):
        for field in ("revision", "tree"):
            value = require_nonempty_string(
                context.get(field),
                f"{case_id} provenance {label} {field}",
            )
            if not COMMIT_PATTERN.fullmatch(value):
                raise ValueError(
                    f"{case_id} provenance {label} {field} is invalid"
                )
        if context.get("clean") is not True:
            raise ValueError(
                f"{case_id} provenance {label} context must be clean"
            )
    if (
        legacy["revision"] != reference_revision
        or legacy["tree"] != fixture["tree"]
        or legacy["repository"] != legacy_reference["repository"]
    ):
        raise ValueError(
            f"{case_id} Legacy provenance source is not pinned"
        )
    require_nonempty_string(
        legacy["root"],
        f"{case_id} provenance legacy root",
    )

    oracle = document["oracle"]
    if not isinstance(oracle, dict):
        raise ValueError(f"{case_id} provenance oracle must be an object")
    oracle_fields = {
        "source",
        "sourcePath",
        "sourceSha256",
        "buildRecipe",
        "buildRecipePath",
        "buildRecipeSha256",
        "executable",
        "executableSha256",
        "length",
    }
    require_fields(
        oracle,
        oracle_fields,
        f"{case_id} provenance oracle",
        allowed=oracle_fields,
    )
    for field, expected in (
        ("source", descriptor["source"]),
        ("sourceSha256", descriptor["sourceSha256"]),
        ("buildRecipe", descriptor["buildRecipe"]),
        ("buildRecipeSha256", descriptor["buildRecipeSha256"]),
        ("executableSha256", fixture["oracleExecutableSha256"]),
    ):
        actual = oracle[field]
        if field.endswith("Sha256"):
            actual = validate_sha256(
                actual,
                f"{case_id} provenance oracle {field}",
            )
            matches = actual == expected
        else:
            matches = actual == expected
        if not matches:
            raise ValueError(
                f"{case_id} provenance oracle {field} is not pinned"
            )
    for field, expected_suffix in (
        ("sourcePath", descriptor["source"]),
        ("buildRecipePath", descriptor["buildRecipe"]),
    ):
        value = require_nonempty_string(
            oracle[field],
            f"{case_id} provenance oracle {field}",
        ).replace("\\", "/")
        if (
            value != expected_suffix
            and not value.endswith("/" + expected_suffix)
        ):
            raise ValueError(
                f"{case_id} provenance oracle {field} is not pinned"
            )
    executable = require_nonempty_string(
        oracle["executable"],
        f"{case_id} provenance oracle executable",
    )
    length = oracle["length"]
    if (
        not isinstance(length, int)
        or isinstance(length, bool)
        or length <= 0
    ):
        raise ValueError(
            f"{case_id} provenance oracle length is invalid"
        )

    toolchain = document["toolchain"]
    if not isinstance(toolchain, dict):
        raise ValueError(
            f"{case_id} provenance toolchain must be an object"
        )
    fixture_toolchain = fixture["toolchain"]
    toolchain_fields = {
        "root",
        "lazarusVersion",
        "fpcVersion",
        "targetCpu",
        "targetOs",
        "compiler",
        "compilerSha256",
        "backendCompiler",
        "backendCompilerSha256",
        "lazbuild",
        "lazbuildSha256",
        "fingerprint",
    }
    require_fields(
        toolchain,
        toolchain_fields,
        f"{case_id} provenance toolchain",
        allowed=toolchain_fields,
    )
    for field in (
        "root",
        "targetCpu",
        "targetOs",
        "compiler",
        "backendCompiler",
        "lazbuild",
    ):
        require_nonempty_string(
            toolchain[field],
            f"{case_id} provenance toolchain {field}",
        )
    comparisons = {
        "lazarusVersion": fixture_toolchain["lazarus"],
        "fpcVersion": fixture_toolchain["fpc"],
        "compilerSha256": fixture_toolchain["compilerSha256"],
        "backendCompilerSha256": (
            fixture_toolchain["backendCompilerSha256"]
        ),
        "lazbuildSha256": fixture_toolchain["lazbuildSha256"],
    }
    for field, expected in comparisons.items():
        actual = toolchain.get(field)
        if field.endswith("Sha256"):
            actual = validate_sha256(
                actual,
                f"{case_id} provenance toolchain {field}",
            )
            matches = actual == expected
        else:
            matches = actual == expected
        if not matches:
            raise ValueError(
                f"{case_id} provenance toolchain {field} is not pinned"
            )
    if (
        f"{toolchain['targetCpu']}-{toolchain['targetOs']}"
        != fixture_toolchain["target"]
    ):
        raise ValueError(
            f"{case_id} provenance toolchain target is not pinned"
        )
    fingerprint = toolchain.get("fingerprint")
    if not isinstance(fingerprint, dict):
        raise ValueError(
            f"{case_id} provenance toolchain fingerprint is missing"
        )
    fingerprint_fields = {
        "schemaVersion",
        "canonicalization",
        "roots",
        "aggregateSha256",
        "fileCount",
        "byteCount",
    }
    require_fields(
        fingerprint,
        fingerprint_fields,
        f"{case_id} provenance toolchain fingerprint",
        allowed=fingerprint_fields,
    )
    if fingerprint["schemaVersion"] != 1:
        raise ValueError(
            f"{case_id} provenance fingerprint schema is invalid"
        )
    require_nonempty_string(
        fingerprint["canonicalization"],
        f"{case_id} provenance fingerprint canonicalization",
    )
    roots = require_string_array(
        fingerprint["roots"],
        f"{case_id} provenance fingerprint roots",
    )
    if (
        len(roots) != len(set(roots))
        or roots != sorted(roots, key=utf16_ordinal_key)
    ):
        raise ValueError(
            f"{case_id} provenance fingerprint roots are invalid"
        )
    for field in ("fileCount", "byteCount"):
        value = fingerprint[field]
        if (
            not isinstance(value, int)
            or isinstance(value, bool)
            or value <= 0
        ):
            raise ValueError(
                f"{case_id} provenance fingerprint {field} is invalid"
            )
    fingerprint_hash = validate_sha256(
        fingerprint.get("aggregateSha256"),
        f"{case_id} provenance fingerprint aggregateSha256",
    )
    if (
        fingerprint_hash != fixture_toolchain["fingerprintSha256"]
    ):
        raise ValueError(
            f"{case_id} provenance fingerprint is not pinned"
        )
    build = document["build"]
    if not isinstance(build, dict):
        raise ValueError(f"{case_id} provenance build must be an object")
    build_fields = {
        "script",
        "scriptSha256",
        "arguments",
        "invocation",
        "builtAtUtc",
    }
    require_fields(
        build,
        build_fields,
        f"{case_id} provenance build",
        allowed=build_fields,
    )
    build_script = require_nonempty_string(
        build["script"],
        f"{case_id} provenance build script",
    ).replace("\\", "/")
    if not build_script.lower().endswith(
        "/tests/parity/build-legacyoracle.ps1"
    ) and build_script.lower() != "tests/parity/build-legacyoracle.ps1":
        raise ValueError(
            f"{case_id} provenance build script path is invalid"
        )
    validate_sha256(
        build["scriptSha256"],
        f"{case_id} provenance build scriptSha256",
    )
    arguments = require_string_array(
        build["arguments"],
        f"{case_id} provenance build arguments",
    )
    invocation = build["invocation"]
    if not isinstance(invocation, dict):
        raise ValueError(
            f"{case_id} provenance build invocation must be an object"
        )
    invocation_fields = {
        "compiler",
        "options",
        "unitSearchPaths",
        "toolSearchPaths",
        "librarySearchPaths",
        "unitOutputPath",
        "executableOutputPath",
        "outputExecutable",
        "source",
    }
    require_fields(
        invocation,
        invocation_fields,
        f"{case_id} provenance build invocation",
        allowed=invocation_fields,
    )
    for field in (
        "compiler",
        "unitOutputPath",
        "executableOutputPath",
        "outputExecutable",
        "source",
    ):
        require_nonempty_string(
            invocation[field],
            f"{case_id} provenance invocation {field}",
        )
    invocation_arrays = {
        field: require_string_array(
            invocation[field],
            f"{case_id} provenance invocation {field}",
            allow_empty=field != "options",
        )
        for field in (
            "options",
            "unitSearchPaths",
            "toolSearchPaths",
            "librarySearchPaths",
        )
    }
    expected_arguments = [
        *invocation_arrays["options"],
        *(
            "-Fu" + path
            for path in invocation_arrays["unitSearchPaths"]
        ),
        *(
            "-FD" + path
            for path in invocation_arrays["toolSearchPaths"]
        ),
        *(
            "-Fl" + path
            for path in invocation_arrays["librarySearchPaths"]
        ),
        "-FU" + invocation["unitOutputPath"],
        "-FE" + invocation["executableOutputPath"],
        "-o" + invocation["outputExecutable"],
        invocation["source"],
    ]
    if arguments != expected_arguments:
        raise ValueError(
            f"{case_id} provenance build arguments do not match invocation"
        )
    if (
        invocation["compiler"] != toolchain["compiler"]
        or invocation["outputExecutable"] != executable
        or invocation["source"].replace("\\", "/")
        != oracle["sourcePath"].replace("\\", "/")
    ):
        raise ValueError(
            f"{case_id} provenance build invocation is internally "
            "inconsistent"
        )
    built_at = require_nonempty_string(
        build["builtAtUtc"],
        f"{case_id} provenance build builtAtUtc",
    )
    try:
        parsed_built_at = datetime.fromisoformat(
            built_at[:-1] + "+00:00"
            if built_at.endswith("Z")
            else built_at
        )
    except ValueError as error:
        raise ValueError(
            f"{case_id} provenance build builtAtUtc is invalid"
        ) from error
    if parsed_built_at.utcoffset() != timedelta(0):
        raise ValueError(
            f"{case_id} provenance build builtAtUtc must be UTC"
        )

    provenance_manifest = document["manifest"]
    if not isinstance(provenance_manifest, dict):
        raise ValueError(
            f"{case_id} provenance manifest must be an object"
        )
    require_fields(
        provenance_manifest,
        {"path", "sha256"},
        f"{case_id} provenance manifest",
        allowed={"path", "sha256"},
    )
    manifest_path = require_nonempty_string(
        provenance_manifest["path"],
        f"{case_id} provenance manifest path",
    ).replace("\\", "/")
    if not manifest_path.lower().endswith(
        "/tests/parity/parity-manifest.json"
    ) and manifest_path.lower() != "tests/parity/parity-manifest.json":
        raise ValueError(
            f"{case_id} provenance manifest path is invalid"
        )
    validate_sha256(
        provenance_manifest["sha256"],
        f"{case_id} provenance manifest sha256",
    )

    observations = document["observations"]
    if not isinstance(observations, list):
        raise ValueError(
            f"{case_id} provenance observations must be an array"
        )
    observation_fields = {"scenario", "valueCount", "outputSha256"}
    scenarios: list[str] = []
    for observation in observations:
        if not isinstance(observation, dict):
            raise ValueError(
                f"{case_id} provenance observation must be an object"
            )
        require_fields(
            observation,
            observation_fields,
            f"{case_id} provenance observation",
            allowed=observation_fields,
        )
        scenario = require_nonempty_string(
            observation["scenario"],
            f"{case_id} provenance observation scenario",
        )
        value_count = observation["valueCount"]
        if (
            not isinstance(value_count, int)
            or isinstance(value_count, bool)
            or value_count < 0
        ):
            raise ValueError(
                f"{case_id} provenance observation valueCount is invalid"
            )
        validate_sha256(
            observation["outputSha256"],
            f"{case_id} provenance observation outputSha256",
        )
        scenarios.append(scenario)
    if (
        scenarios != selected_case_ids
        or len(scenarios) != len(set(scenarios))
    ):
        raise ValueError(
            f"{case_id} provenance observations do not exactly match "
            "selectedCaseIds"
        )
    matching = [
        observation
        for observation in observations
        if observation["scenario"] == case_id
    ]
    if len(matching) != 1:
        raise ValueError(
            f"{case_id} provenance has no unique scenario observation"
        )
    if matching[0].get("valueCount") != len(fixture["values"]):
        raise ValueError(
            f"{case_id} provenance observation count is stale"
        )


def validate_active_evidence(
    case: dict[str, Any],
    evidence: dict[str, Any],
    fixture_path: Path,
    fixture: dict[str, Any],
    fixture_count: int,
    reference_revision: str,
    *,
    root: Path = ROOT,
) -> None:
    parity_id = case["id"]
    root_fields = {
        "schemaVersion",
        "parityId",
        "referenceRevision",
        "capturedAtUtc",
        "legacy",
        "xplat",
        "classification",
    }
    require_fields(
        evidence,
        root_fields,
        f"{parity_id} evidence",
        allowed=root_fields,
    )
    if evidence["schemaVersion"] != EVIDENCE_SCHEMA_VERSION:
        raise ValueError(
            f"{parity_id} evidence must use schema "
            f"{EVIDENCE_SCHEMA_VERSION}"
        )
    if evidence["parityId"] != parity_id:
        raise ValueError(
            f"{parity_id} evidence parityId is {evidence['parityId']!r}"
        )
    if evidence["referenceRevision"] != reference_revision:
        raise ValueError(
            f"{parity_id} evidence revision does not match the manifest"
        )
    validate_timestamp(
        evidence["capturedAtUtc"],
        f"{parity_id} evidence capturedAtUtc",
    )

    legacy = evidence["legacy"]
    xplat = evidence["xplat"]
    if not isinstance(legacy, dict):
        raise ValueError(f"{parity_id} evidence legacy result must be an object")
    if not isinstance(xplat, dict):
        raise ValueError(f"{parity_id} evidence XPlat result must be an object")
    validate_legacy_evidence(
        parity_id,
        legacy,
        case["fixture"],
        fixture_path,
        fixture,
        fixture_count,
        root=root,
    )
    validate_xplat_evidence(
        parity_id,
        xplat,
        fixture_path,
        fixture,
        fixture_count,
        root=root,
    )

    derived_classification = classify_evidence(
        legacy["outcome"],
        xplat["outcome"],
        parity_id,
    )
    if evidence["classification"] != derived_classification:
        raise ValueError(
            f"{parity_id} evidence classification does not match outcomes"
        )
    if case["status"] != derived_classification:
        raise ValueError(
            f"{parity_id} manifest status does not match evidence classification"
        )
    if case["legacyTestStatus"] != legacy["outcome"]:
        raise ValueError(
            f"{parity_id} legacy status does not match evidence"
        )
    if case["xplatTestStatus"] != xplat["outcome"]:
        raise ValueError(
            f"{parity_id} XPlat status does not match evidence"
        )

    if derived_classification == "legacy-green-xplat-red":
        evidence_failure = xplat["failureCode"]
        if case["failureCode"] != evidence_failure:
            raise ValueError(
                f"{parity_id} failure code does not match evidence"
            )
        if case["firstGreenCommit"] is not None:
            raise ValueError(
                f"{parity_id} red status must not claim a first-green commit"
            )
    else:
        if case["failureCode"] is not None:
            raise ValueError(
                f"{parity_id} both-green status must not claim a failure"
            )
        validate_first_green_commit(
            case["firstGreenCommit"],
            parity_id,
            run_revision=xplat["runContext"]["revision"],
            run_tree=xplat["runContext"]["tree"],
            root=root,
        )


def validate_legacy_evidence(
    parity_id: str,
    legacy: dict[str, Any],
    fixture_relative: str,
    fixture_path: Path,
    fixture: dict[str, Any],
    fixture_count: int,
    *,
    root: Path = ROOT,
) -> None:
    required = {
        "outcome",
        "source",
        "fixture",
        "fixtureSha256",
        "observedValues",
        "observedValuesSha256",
    }
    allowed = required | {
        "observedSurfaceCount",
        "observedValueCount",
        "toolchain",
    }
    require_fields(
        legacy,
        required,
        f"{parity_id} legacy evidence",
        allowed=allowed,
    )
    if legacy["outcome"] != "pass":
        raise ValueError(f"{parity_id} active legacy evidence is not green")
    resolve_repo_file(
        legacy["source"],
        "tests/parity/legacy-oracle",
        f"{parity_id} legacy evidence source",
        root=root,
    )
    if legacy["fixture"] != fixture_relative:
        raise ValueError(
            f"{parity_id} evidence fixture does not match the manifest"
        )
    resolved_evidence_fixture = resolve_repo_file(
        legacy["fixture"],
        "tests/parity/fixtures/legacy",
        f"{parity_id} evidence fixture",
        root=root,
    )
    if resolved_evidence_fixture != fixture_path:
        raise ValueError(
            f"{parity_id} evidence fixture resolves to the wrong file"
        )
    validate_result_integrity(
        parity_id,
        legacy,
        fixture_path,
        fixture,
        "legacy",
        require_fixture_values=True,
    )
    validate_observed_count(
        parity_id,
        legacy,
        fixture_count,
        "legacy",
    )
    if "toolchain" in legacy:
        require_nonempty_string(
            legacy["toolchain"],
            f"{parity_id} legacy evidence toolchain",
        )


def validate_xplat_evidence(
    parity_id: str,
    xplat: dict[str, Any],
    fixture_path: Path,
    fixture: dict[str, Any],
    fixture_count: int,
    *,
    root: Path = ROOT,
) -> None:
    outcome = xplat.get("outcome")
    result_fields = {
        "outcome",
        "source",
        "fixtureSha256",
        "observedValues",
        "observedValuesSha256",
    }
    if outcome == "fail":
        required = result_fields | {"failureCode", "firstDivergence"}
        require_fields(
            xplat,
            required,
            f"{parity_id} XPlat evidence",
            allowed=required,
        )
        require_nonempty_string(
            xplat["failureCode"],
            f"{parity_id} XPlat failureCode",
        )
        require_nonempty_string(
            xplat["firstDivergence"],
            f"{parity_id} XPlat firstDivergence",
        )
    elif outcome == "pass":
        required = result_fields | {"runContext"}
        allowed = required | {"observedSurfaceCount", "observedValueCount"}
        require_fields(
            xplat,
            required,
            f"{parity_id} XPlat evidence",
            allowed=allowed,
        )
    else:
        raise ValueError(f"{parity_id} XPlat evidence has invalid outcome")
    resolve_repo_file(
        xplat["source"],
        "tests",
        f"{parity_id} XPlat evidence source",
        root=root,
    )
    validate_result_integrity(
        parity_id,
        xplat,
        fixture_path,
        fixture,
        "XPlat",
        require_fixture_values=outcome == "pass",
    )
    expected_count = (
        fixture_count if outcome == "pass" else len(xplat["observedValues"])
    )
    validate_observed_count(
        parity_id,
        xplat,
        expected_count,
        "XPlat",
    )
    if outcome == "pass":
        validate_green_run_context(parity_id, xplat)


def validate_green_run_context(
    parity_id: str,
    xplat: dict[str, Any],
) -> None:
    run_context = xplat["runContext"]
    if not isinstance(run_context, dict):
        raise ValueError(
            f"{parity_id} XPlat runContext must be an object"
        )
    fields = {"revision", "tree", "resultSha256"}
    require_fields(
        run_context,
        fields,
        f"{parity_id} XPlat runContext",
        allowed=fields,
    )
    for key in ("revision", "tree"):
        value = require_nonempty_string(
            run_context[key],
            f"{parity_id} XPlat runContext {key}",
        )
        if not COMMIT_PATTERN.fullmatch(value):
            raise ValueError(
                f"{parity_id} XPlat runContext {key} must be a "
                "full Git object ID"
            )
    declared_result_hash = validate_sha256(
        run_context["resultSha256"],
        f"{parity_id} XPlat runContext resultSha256",
    )
    result_document = {
        key: value
        for key, value in xplat.items()
        if key != "runContext"
    }
    actual_result_hash = canonical_json_sha256(result_document)
    if declared_result_hash.lower() != actual_result_hash:
        raise ValueError(
            f"{parity_id} XPlat runContext resultSha256 does not "
            "match the green result"
        )


def validate_result_integrity(
    parity_id: str,
    result: dict[str, Any],
    fixture_path: Path,
    fixture: dict[str, Any],
    target: str,
    *,
    require_fixture_values: bool,
) -> None:
    fixture_digest = validate_sha256(
        result["fixtureSha256"],
        f"{parity_id} {target} evidence fixtureSha256",
    )
    actual_fixture_digest = sha256_file(fixture_path)
    if fixture_digest.lower() != actual_fixture_digest:
        raise ValueError(
            f"{parity_id} {target} evidence fixtureSha256 does not "
            "match the fixture"
        )
    observed_values = result["observedValues"]
    if not isinstance(observed_values, list):
        raise ValueError(
            f"{parity_id} {target} evidence observedValues must be an array"
        )
    if any(not isinstance(value, str) for value in observed_values):
        raise ValueError(
            f"{parity_id} {target} evidence observedValues must contain "
            "only JSON strings"
        )
    observed_digest = validate_sha256(
        result["observedValuesSha256"],
        f"{parity_id} {target} evidence observedValuesSha256",
    )
    actual_observed_digest = canonical_json_sha256(observed_values)
    if observed_digest.lower() != actual_observed_digest:
        raise ValueError(
            f"{parity_id} {target} evidence observedValuesSha256 does "
            "not match observedValues"
        )
    if require_fixture_values and observed_values != fixture["values"]:
        raise ValueError(
            f"{parity_id} {target} evidence observedValues do not "
            "match the fixture"
        )


def validate_observed_count(
    parity_id: str,
    result: dict[str, Any],
    fixture_count: int,
    target: str,
) -> None:
    count_keys = [
        key
        for key in ("observedSurfaceCount", "observedValueCount")
        if key in result
    ]
    if len(count_keys) > 1:
        raise ValueError(
            f"{parity_id} {target} evidence declares multiple counts"
        )
    if not count_keys:
        return
    count = result[count_keys[0]]
    if (
        not isinstance(count, int)
        or isinstance(count, bool)
        or count != fixture_count
    ):
        raise ValueError(
            f"{parity_id} {target} evidence count does not match fixture"
        )


def classify_evidence(
    legacy_outcome: Any,
    xplat_outcome: Any,
    parity_id: str,
) -> str:
    if legacy_outcome != "pass":
        raise ValueError(f"{parity_id} legacy evidence is not green")
    if xplat_outcome == "pass":
        return "both-green"
    if xplat_outcome == "fail":
        return "legacy-green-xplat-red"
    raise ValueError(f"{parity_id} XPlat evidence has invalid outcome")


def validate_first_green_commit(
    commit: Any,
    parity_id: str,
    *,
    run_revision: Any | None = None,
    run_tree: Any | None = None,
    root: Path = ROOT,
) -> None:
    if not isinstance(commit, str) or not COMMIT_PATTERN.fullmatch(commit):
        raise ValueError(
            f"{parity_id} both-green status requires a full "
            "firstGreenCommit"
        )
    if run_revision is not None and commit != run_revision:
        raise ValueError(
            f"{parity_id} firstGreenCommit does not match the "
            "green XPlat run revision"
        )
    exists = run_git(
        root,
        ["cat-file", "-e", f"{commit}^{{commit}}"],
        parity_id,
    )
    if exists.returncode != 0:
        raise ValueError(
            f"{parity_id} firstGreenCommit is not a local commit: {commit}"
        )
    reachable = run_git(
        root,
        ["merge-base", "--is-ancestor", commit, "HEAD"],
        parity_id,
    )
    if reachable.returncode != 0:
        raise ValueError(
            f"{parity_id} firstGreenCommit is not reachable from HEAD: "
            f"{commit}"
        )
    if run_tree is not None:
        tree = require_nonempty_string(
            run_tree,
            f"{parity_id} green XPlat run tree",
        )
        if not COMMIT_PATTERN.fullmatch(tree):
            raise ValueError(
                f"{parity_id} green XPlat run tree must be a full "
                "tree ID"
            )
        actual_tree = run_git(
            root,
            ["rev-parse", f"{commit}^{{tree}}"],
            parity_id,
        )
        if (
            actual_tree.returncode != 0
            or actual_tree.stdout.strip() != tree
        ):
            raise ValueError(
                f"{parity_id} firstGreenCommit tree does not match "
                "the green XPlat run"
            )


def run_git(
    root: Path,
    arguments: list[str],
    parity_id: str,
) -> subprocess.CompletedProcess[str]:
    try:
        return subprocess.run(
            ["git", "-C", str(root), *arguments],
            capture_output=True,
            text=True,
            timeout=10,
            check=False,
        )
    except (OSError, subprocess.SubprocessError) as error:
        raise ValueError(
            f"{parity_id} could not validate Git provenance"
        ) from error


def run_git_bytes(
    root: Path,
    arguments: list[str],
    label: str,
) -> subprocess.CompletedProcess[bytes]:
    try:
        return subprocess.run(
            ["git", "-C", str(root), *arguments],
            capture_output=True,
            timeout=10,
            check=False,
        )
    except (OSError, subprocess.SubprocessError) as error:
        raise ValueError(
            f"{label} could not validate Git provenance"
        ) from error


def validate_retained_xplat_revision(
    revision: str,
    tree: str,
    label: str,
    *,
    root: Path = ROOT,
) -> None:
    resolved_revision = run_git(
        root,
        ["rev-parse", "--verify", f"{revision}^{{commit}}"],
        label,
    )
    if (
        resolved_revision.returncode != 0
        or resolved_revision.stdout.strip() != revision
    ):
        raise ValueError(f"{label} revision does not exist")
    reachable = run_git(
        root,
        ["merge-base", "--is-ancestor", revision, "HEAD"],
        label,
    )
    if reachable.returncode != 0:
        raise ValueError(f"{label} revision is not reachable from HEAD")
    resolved_tree = run_git(
        root,
        ["rev-parse", "--verify", f"{revision}^{{tree}}"],
        label,
    )
    if (
        resolved_tree.returncode != 0
        or resolved_tree.stdout.strip() != tree
    ):
        raise ValueError(f"{label} tree does not match its revision")


def validate_strict_revision_ancestry(
    earlier_revision: str,
    later_revision: str,
    label: str,
    *,
    root: Path = ROOT,
) -> None:
    if earlier_revision == later_revision:
        raise ValueError(
            f"{label} requires the red revision to be a strict ancestor"
        )
    ancestor = run_git(
        root,
        [
            "merge-base",
            "--is-ancestor",
            earlier_revision,
            later_revision,
        ],
        label,
    )
    if ancestor.returncode != 0:
        raise ValueError(
            f"{label} requires the red revision to be a strict ancestor"
        )


def inspect_current_xplat_repository(
    *,
    root: Path = ROOT,
) -> dict[str, Any]:
    revision_result = run_git(
        root,
        ["rev-parse", "--verify", "HEAD^{commit}"],
        "live XPlat repository",
    )
    revision = revision_result.stdout.strip()
    if (
        revision_result.returncode != 0
        or not COMMIT_PATTERN.fullmatch(revision)
    ):
        raise ValueError(
            "live XPlat repository revision could not be inspected"
        )
    tree_result = run_git(
        root,
        ["rev-parse", "--verify", "HEAD^{tree}"],
        "live XPlat repository",
    )
    tree = tree_result.stdout.strip()
    if (
        tree_result.returncode != 0
        or not COMMIT_PATTERN.fullmatch(tree)
    ):
        raise ValueError(
            "live XPlat repository tree could not be inspected"
        )
    status_result = run_git(
        root,
        ["status", "--porcelain=v2", "--untracked-files=all"],
        "live XPlat repository",
    )
    if status_result.returncode != 0:
        raise ValueError(
            "live XPlat repository cleanliness could not be inspected"
        )
    return {
        "revision": revision,
        "tree": tree,
        "clean": not status_result.stdout.strip(),
    }


def validate_evidence_directory(
    referenced_paths: dict[Path, str],
    capabilities_by_id: dict[str, dict[str, Any]],
    active_case_ids: set[str],
    reference_revision: str,
    *,
    root: Path = ROOT,
) -> list[dict[str, Any]]:
    evidence_root = (root.resolve() / "tests/parity/evidence").resolve()
    if not evidence_root.is_dir():
        raise ValueError(f"parity evidence directory not found: {evidence_root}")

    all_evidence_paths: list[Path] = []
    for candidate in evidence_root.glob("*.json"):
        resolved = candidate.resolve()
        if not is_within(resolved, evidence_root):
            raise ValueError(f"parity evidence escapes evidence root: {candidate}")
        all_evidence_paths.append(resolved)

    retained: list[dict[str, Any]] = []
    evidence_ids = set(active_case_ids)
    for evidence_path in sorted(set(all_evidence_paths) - referenced_paths.keys()):
        evidence = load_json(evidence_path)
        parity_id = require_nonempty_string(
            evidence.get("parityId"),
            f"{evidence_path} parityId",
        )
        if parity_id in evidence_ids:
            raise ValueError(
                f"orphan evidence reuses parity ID {parity_id}: "
                f"{evidence_path}"
            )
        capability = capabilities_by_id.get(parity_id)
        if (
            capability is not None
            and capability["acceptanceStatus"] == "complete"
        ):
            raise ValueError(
                f"retained evidence reuses complete capability ID "
                f"{parity_id}: {evidence_path}"
            )
        validate_retained_evidence(
            evidence,
            evidence_path,
            reference_revision,
            root=root,
        )
        evidence_ids.add(parity_id)
        retained.append(evidence)
    return retained


def validate_retained_evidence(
    evidence: dict[str, Any],
    evidence_path: Path,
    reference_revision: str,
    *,
    root: Path = ROOT,
) -> None:
    parity_id = evidence.get("parityId", str(evidence_path))
    if (
        evidence.get("schemaVersion") != RETAINED_EVIDENCE_SCHEMA_VERSION
        or "retention" not in evidence
    ):
        raise ValueError(
            f"orphan evidence must be explicitly marked legacy-v1: "
            f"{evidence_path}"
        )
    root_fields = {
        "schemaVersion",
        "parityId",
        "referenceRevision",
        "capturedAtUtc",
        "legacy",
        "xplat",
        "classification",
        "retention",
    }
    require_fields(
        evidence,
        root_fields,
        f"orphan evidence {parity_id}",
        allowed=root_fields,
    )
    if evidence["classification"] != RETAINED_EVIDENCE_CLASSIFICATION:
        raise ValueError(
            f"retained evidence {parity_id} must be noncertifying"
        )
    if evidence["referenceRevision"] != reference_revision:
        raise ValueError(
            f"retained evidence {parity_id} revision does not match manifest"
        )
    validate_timestamp(
        evidence["capturedAtUtc"],
        f"retained evidence {parity_id} capturedAtUtc",
    )

    retention = evidence["retention"]
    if not isinstance(retention, dict):
        raise ValueError(
            f"retained evidence {parity_id} retention must be an object"
        )
    retention_fields = {"status", "reason"}
    require_fields(
        retention,
        retention_fields,
        f"retained evidence {parity_id} retention",
        allowed=retention_fields,
    )
    if retention["status"] != RETAINED_EVIDENCE_STATUS:
        raise ValueError(
            f"retained evidence {parity_id} has invalid retention status"
        )
    require_nonempty_string(
        retention["reason"],
        f"retained evidence {parity_id} retention reason",
    )

    legacy = evidence["legacy"]
    xplat = evidence["xplat"]
    if not isinstance(legacy, dict) or legacy.get("outcome") != "pass":
        raise ValueError(
            f"retained evidence {parity_id} has invalid legacy outcome"
        )
    if not isinstance(xplat, dict) or xplat.get("outcome") not in {
        "pass",
        "fail",
    }:
        raise ValueError(
            f"retained evidence {parity_id} has invalid XPlat outcome"
        )
    require_nonempty_string(
        legacy.get("source"),
        f"retained evidence {parity_id} legacy source",
    )
    fixture_relative = require_nonempty_string(
        legacy.get("fixture"),
        f"retained evidence {parity_id} fixture",
    )
    fixture_path = resolve_repo_file(
        fixture_relative,
        "tests/parity/fixtures/legacy",
        f"retained evidence {parity_id} fixture",
        root=root,
    )
    require_json_suffix(
        fixture_path,
        f"retained evidence {parity_id} fixture",
    )
    fixture = load_json(fixture_path)
    fixture_count = validate_fixture_document(
        fixture,
        parity_id,
        reference_revision,
        fixture_path,
        require_schema_v2=False,
        root=root,
    )
    validate_observed_count(parity_id, legacy, fixture_count, "legacy")
    if xplat["outcome"] == "pass":
        require_nonempty_string(
            xplat.get("source"),
            f"retained evidence {parity_id} XPlat source",
        )
        validate_observed_count(parity_id, xplat, fixture_count, "XPlat")
    else:
        require_nonempty_string(
            xplat.get("failureCode"),
            f"retained evidence {parity_id} XPlat failureCode",
        )
        require_nonempty_string(
            xplat.get("firstDivergence"),
            f"retained evidence {parity_id} XPlat firstDivergence",
        )


def map_surfaces(
    items: list[dict[str, Any]],
    surfaces: list[dict[str, Any]],
) -> dict[str, list[str]]:
    surface_ids = [surface["id"] for surface in surfaces]
    mappings: dict[str, list[str]] = defaultdict(list)

    for item in items:
        parity_id = item["id"]
        for selector in item["legacySurfaceSelectors"]:
            selected = [
                surface_id
                for surface_id in surface_ids
                if fnmatch.fnmatchcase(surface_id, selector)
            ]
            if not selected:
                raise ValueError(
                    f"{parity_id} selector matches no legacy surfaces: {selector}"
                )
            for surface_id in selected:
                mappings[surface_id].append(parity_id)

    unmapped = sorted(set(surface_ids) - mappings.keys())
    if unmapped:
        raise ValueError(
            "unmapped legacy surfaces: "
            + ", ".join(unmapped[:10])
            + ("..." if len(unmapped) > 10 else "")
        )

    multiply_mapped = {
        surface_id: parity_ids
        for surface_id, parity_ids in mappings.items()
        if len(parity_ids) != 1
    }
    if multiply_mapped:
        first_id = sorted(multiply_mapped)[0]
        raise ValueError(
            f"legacy surface {first_id} maps to multiple items: "
            f"{', '.join(multiply_mapped[first_id])}"
        )
    return dict(mappings)


def derive_capability_status_counts(
    capabilities: list[dict[str, Any]],
) -> Counter[str]:
    return Counter(
        capability["acceptanceStatus"] for capability in capabilities
    )


def derive_obligation_status_counts(
    obligations: list[dict[str, Any]],
) -> Counter[str]:
    return Counter(
        obligation["acceptanceStatus"] for obligation in obligations
    )


def derive_rich_evidence_blockers(
    obligations: list[dict[str, Any]],
    cases: list[dict[str, Any]],
) -> list[str]:
    cases_by_id = {case["id"]: case for case in cases}
    return sorted(
        obligation["id"]
        for obligation in obligations
        if (
            obligation["id"] in RICH_EVIDENCE_REQUIRED_OBLIGATIONS
            and obligation["caseIds"]
            and all(
                cases_by_id[case_id]["status"] == "both-green"
                for case_id in obligation["caseIds"]
            )
            and obligation["acceptanceStatus"] == "partial"
        )
    )


def derive_case_status_counts(
    cases: list[dict[str, Any]],
) -> Counter[str]:
    return Counter(case["status"] for case in cases)


def derive_case_overlap_counts(
    manifest: dict[str, Any],
    inventory: dict[str, Any],
) -> dict[str, int]:
    surface_ids = [surface["id"] for surface in inventory["surfaces"]]
    surface_owner_by_id: dict[str, str] = {}
    for capability in manifest["items"]:
        for surface_id in {
            surface_id
            for surface_id in surface_ids
            if any(
                fnmatch.fnmatchcase(surface_id, selector)
                for selector in capability["legacySurfaceSelectors"]
            )
        }:
            surface_owner_by_id[surface_id] = capability["id"]
    memberships_by_capability: dict[
        str,
        Counter[tuple[str, str]],
    ] = defaultdict(Counter)
    for case in manifest["cases"]:
        for surface_id in resolve_case_surfaces(case, surface_ids):
            owner_id = surface_owner_by_id[surface_id]
            memberships_by_capability[owner_id].update(
                (surface_id, platform)
                for platform in case["platforms"]
            )
    counts: dict[str, int] = {}
    for capability in manifest["items"]:
        memberships = memberships_by_capability[capability["id"]]
        counts[capability["id"]] = sum(
            membership_count - 1
            for membership_count in memberships.values()
            if membership_count > 1
        )
    return counts


def validate_manifest_history(
    manifest: dict[str, Any],
    base_manifest: dict[str, Any],
    *,
    root: Path = ROOT,
    base_root: Path | None = None,
    base_commit: str | None = None,
    base_manifest_sha256: str | None = None,
    retained_evidence: list[dict[str, Any]] | None = None,
) -> None:
    if base_root is None:
        base_root = root
    if base_manifest.get("schemaVersion") == 1:
        validate_trusted_v1_migration(
            manifest,
            base_manifest,
            base_manifest_sha256,
            retained_evidence,
        )
        return
    if base_manifest.get("schemaVersion") != MANIFEST_SCHEMA_VERSION:
        raise ValueError(
            "base manifest must use parity manifest schema version 3"
        )
    for label, document in (
        ("current", manifest),
        ("base", base_manifest),
    ):
        if not isinstance(document.get("items"), list):
            raise ValueError(f"{label} manifest items must be an array")
        if not isinstance(
            document.get("behavioralObligations"),
            list,
        ):
            raise ValueError(
                f"{label} manifest behavioralObligations must be an array"
            )
        if not isinstance(document.get("cases"), list):
            raise ValueError(f"{label} manifest cases must be an array")
    if manifest.get("reference") != base_manifest.get("reference"):
        raise ValueError("manifest reference trust anchor changed")
    if (
        manifest.get("legacySurfaceInventory")
        != base_manifest.get("legacySurfaceInventory")
    ):
        raise ValueError("legacy surface inventory path changed")
    current_canonical_json = manifest.get("canonicalJson")
    base_canonical_json = base_manifest.get("canonicalJson")
    if current_canonical_json != base_canonical_json:
        current_version = (
            current_canonical_json.get("version")
            if isinstance(current_canonical_json, dict)
            else None
        )
        base_version = (
            base_canonical_json.get("version")
            if isinstance(base_canonical_json, dict)
            else None
        )
        if (
            not isinstance(current_version, int)
            or isinstance(current_version, bool)
            or not isinstance(base_version, int)
            or isinstance(base_version, bool)
            or current_version <= base_version
        ):
            raise ValueError(
                "canonical JSON semantics changed without an explicit "
                "canonicalization version bump"
            )

    current_cases = index_history_entries(
        manifest["cases"],
        "current case",
    )
    base_cases = index_history_entries(
        base_manifest["cases"],
        "base case",
    )
    removed_cases = sorted(base_cases.keys() - current_cases.keys())
    if removed_cases:
        raise ValueError(
            "parity case removal is not allowed: "
            + ", ".join(removed_cases)
        )
    new_case_ids = current_cases.keys() - base_cases.keys()
    for case_id in sorted(new_case_ids):
        case = current_cases[case_id]
        if (
            case.get("legacyTestStatus") != "pass"
            or case.get("xplatTestStatus") != "fail"
            or case.get("status") != "legacy-green-xplat-red"
            or case.get("firstGreenCommit") is not None
            or not isinstance(case.get("failureCode"), str)
            or not case["failureCode"]
        ):
            raise ValueError(
                f"{case_id} is a new acceptance case and must first land "
                "with retained legacy-green/XPlat-red evidence"
            )
        evidence = load_certified_document(
            case["evidence"],
            root=root,
        )
        runs = evidence.get("runs")
        if (
            evidence.get("schemaVersion") != EVIDENCE_SCHEMA_VERSION
            or evidence.get("classification")
            != "legacy-green-xplat-red"
            or not isinstance(runs, dict)
            or not isinstance(runs.get("xplatRed"), dict)
        ):
            raise ValueError(
                f"{case_id} new-case history has no live retained-red "
                "evidence"
            )

    current_capabilities = index_history_entries(
        manifest["items"],
        "current capability",
    )
    base_capabilities = index_history_entries(
        base_manifest["items"],
        "base capability",
    )
    removed_capabilities = sorted(
        base_capabilities.keys() - current_capabilities.keys()
    )
    if removed_capabilities:
        raise ValueError(
            "parity capability removal is not allowed: "
            + ", ".join(removed_capabilities)
        )

    for capability_id, base_capability in base_capabilities.items():
        current_capability = current_capabilities[capability_id]
        for field in sorted(CERTIFIED_CAPABILITY_FIELDS):
            if (
                current_capability.get(field)
                != base_capability.get(field)
            ):
                raise ValueError(
                    f"{capability_id} changed locked {field}"
                )
        base_status = base_capability.get("acceptanceStatus")
        current_status = current_capability.get("acceptanceStatus")
        base_case_ids = base_capability.get("caseIds")
        current_case_ids = current_capability.get("caseIds")
        if (
            not isinstance(base_case_ids, list)
            or not isinstance(current_case_ids, list)
            or not sequence_is_subsequence(
                base_case_ids,
                current_case_ids,
            )
        ):
            raise ValueError(
                f"{capability_id} removed or reordered acceptance cases"
            )
        added_case_ids = set(current_case_ids) - set(base_case_ids)
        additive_red_discovery = bool(added_case_ids) and all(
            case_id in new_case_ids
            and current_cases[case_id].get("status")
            == "legacy-green-xplat-red"
            for case_id in added_case_ids
        )
        if base_status == "complete" and current_status != "complete":
            if (
                current_status != "partial"
                or not additive_red_discovery
            ):
                raise ValueError(
                    f"{capability_id} regressed from complete to "
                    f"{current_status} without an additive retained-red "
                    "gap discovery"
                )
        if (
            base_status == "partial"
            and current_status == "not-authored"
        ):
            raise ValueError(
                f"{capability_id} regressed from partial to not-authored"
            )

    current_obligations = index_history_entries(
        manifest["behavioralObligations"],
        "current behavioral obligation",
    )
    base_obligations = index_history_entries(
        base_manifest["behavioralObligations"],
        "base behavioral obligation",
    )
    removed_obligations = sorted(
        base_obligations.keys() - current_obligations.keys()
    )
    if removed_obligations:
        raise ValueError(
            "behavioral obligation removal is not allowed: "
            + ", ".join(removed_obligations)
        )
    for obligation_id, base_obligation in base_obligations.items():
        current_obligation = current_obligations[obligation_id]
        for field in ("capabilityId", "behavior", "platforms"):
            if (
                current_obligation.get(field)
                != base_obligation.get(field)
            ):
                raise ValueError(
                    f"{obligation_id} changed locked obligation {field}"
                )
        base_case_ids = base_obligation.get("caseIds")
        current_case_ids = current_obligation.get("caseIds")
        if (
            not isinstance(base_case_ids, list)
            or not isinstance(current_case_ids, list)
            or not sequence_is_subsequence(
                base_case_ids,
                current_case_ids,
            )
        ):
            raise ValueError(
                f"{obligation_id} removed or reordered acceptance cases"
            )
        added_case_ids = set(current_case_ids) - set(base_case_ids)
        additive_red_discovery = bool(added_case_ids) and all(
            case_id in new_case_ids
            and current_cases[case_id].get("status")
            == "legacy-green-xplat-red"
            and current_cases[case_id].get("obligationIds")
            == [obligation_id]
            for case_id in added_case_ids
        )
        base_binding = base_obligation.get("sourceBindingStatus")
        current_binding = current_obligation.get("sourceBindingStatus")
        if base_binding == "bound":
            if current_binding != "bound":
                raise ValueError(
                    f"{obligation_id} regressed a bound source binding"
                )
            for field in ("legacySources", "legacySurfaceSelectors"):
                if (
                    current_obligation.get(field)
                    != base_obligation.get(field)
                ):
                    raise ValueError(
                        f"{obligation_id} changed locked obligation {field}"
                    )
        elif base_binding == "pending":
            if current_binding == "pending":
                for field in ("legacySources", "legacySurfaceSelectors"):
                    if (
                        current_obligation.get(field)
                        != base_obligation.get(field)
                    ):
                        raise ValueError(
                            f"{obligation_id} changed pending binding "
                            f"{field} without binding it"
                        )
            elif current_binding == "bound":
                if (
                    base_case_ids
                    or not additive_red_discovery
                    or current_obligation.get("acceptanceStatus")
                    != "partial"
                    or not current_obligation.get("legacySources")
                    or not current_obligation.get(
                        "legacySurfaceSelectors"
                    )
                ):
                    raise ValueError(
                        f"{obligation_id} pending source binding may bind "
                        "only with its first live retained-red case"
                    )
            else:
                raise ValueError(
                    f"{obligation_id} has invalid source-binding history"
                )
        else:
            raise ValueError(
                f"{obligation_id} base source binding is invalid"
            )

        base_status = base_obligation.get("acceptanceStatus")
        current_status = current_obligation.get("acceptanceStatus")
        if base_status == "complete" and current_status != "complete":
            if current_status != "partial" or not additive_red_discovery:
                raise ValueError(
                    f"{obligation_id} regressed from complete to "
                    f"{current_status} without an additive retained-red "
                    "gap discovery"
                )
        if (
            base_status == "partial"
            and current_status == "not-authored"
        ):
            raise ValueError(
                f"{obligation_id} regressed from partial to not-authored"
            )

    for case_id, base_case in base_cases.items():
        current_case = current_cases[case_id]
        if (
            current_case.get("capabilityId")
            != base_case.get("capabilityId")
        ):
            raise ValueError(f"{case_id} was remapped to another capability")
        base_status = base_case.get("status")
        current_status = current_case.get("status")
        if (
            base_status == "both-green"
            and current_status != "both-green"
        ):
            raise ValueError(f"{case_id} regressed from both-green")
        if base_status == "both-green":
            for field in sorted(CERTIFIED_CASE_FIELDS):
                if current_case.get(field) != base_case.get(field):
                    raise ValueError(
                        f"{case_id} changed certified {field}; "
                        "add a new case instead"
                    )
            for document_field in ("fixture", "evidence"):
                current_hash = canonical_document_sha256(
                    current_case[document_field],
                    root=root,
                )
                base_hash = canonical_document_sha256(
                    base_case[document_field],
                    root=base_root,
                    commit=base_commit,
                )
                if current_hash != base_hash:
                    raise ValueError(
                        f"{case_id} changed certified "
                        f"{document_field} content; add a new case instead"
                    )
        elif base_status == "legacy-green-xplat-red":
            transitioned_to_green = current_status == "both-green"
            transition_fields = {
                "xplatTestStatus",
                "status",
                "failureCode",
                "firstGreenCommit",
            }
            locked_fields = (
                CERTIFIED_CASE_FIELDS - transition_fields
                if transitioned_to_green
                else CERTIFIED_CASE_FIELDS
            )
            for field in sorted(locked_fields):
                if current_case.get(field) != base_case.get(field):
                    raise ValueError(
                        f"{case_id} weakened locked red-case {field}; "
                        "add a new case instead"
                    )
            current_fixture_hash = canonical_document_sha256(
                current_case["fixture"],
                root=root,
            )
            base_fixture_hash = canonical_document_sha256(
                base_case["fixture"],
                root=base_root,
                commit=base_commit,
            )
            if current_fixture_hash != base_fixture_hash:
                raise ValueError(
                    f"{case_id} changed locked red-case fixture content"
                )
            current_evidence = load_certified_document(
                current_case["evidence"],
                root=root,
            )
            base_evidence = load_certified_document(
                base_case["evidence"],
                root=base_root,
                commit=base_commit,
            )
            validate_red_evidence_history(
                case_id,
                base_evidence,
                current_evidence,
                transitioned_to_green=transitioned_to_green,
                base_cases=base_manifest["cases"],
                root=root,
            )


def validate_red_evidence_history(
    case_id: str,
    base_evidence: dict[str, Any],
    current_evidence: dict[str, Any],
    *,
    transitioned_to_green: bool,
    base_cases: list[dict[str, Any]] | None = None,
    root: Path = ROOT,
) -> None:
    mutable_root_fields = {
        "capturedAtUtc",
        "classification",
        "runs",
        "testReports",
        "executions",
        "regressionGate",
    }
    for field in sorted(set(base_evidence) - mutable_root_fields):
        if current_evidence.get(field) != base_evidence.get(field):
            raise ValueError(
                f"{case_id} changed locked red-case evidence field {field}"
            )
    if set(current_evidence) != set(base_evidence):
        raise ValueError(
            f"{case_id} changed red-case evidence schema"
        )
    green_platform_sets: list[set[str]] = []
    for root_field, singular_label in (
        ("runs", "run"),
        ("testReports", "test report"),
        ("executions", "execution envelope"),
    ):
        base_artifacts = base_evidence.get(root_field)
        current_artifacts = current_evidence.get(root_field)
        if not isinstance(base_artifacts, dict) or not isinstance(
            current_artifacts,
            dict,
        ):
            raise ValueError(
                f"{case_id} red-case evidence {root_field} are invalid"
            )
        expected_fields = {"legacy", "xplatRed", "xplatGreen"}
        if (
            set(base_artifacts) != expected_fields
            or set(current_artifacts) != expected_fields
        ):
            raise ValueError(
                f"{case_id} red-case evidence {root_field} schema changed"
            )
        for field in ("legacy", "xplatRed"):
            if current_artifacts[field] != base_artifacts[field]:
                raise ValueError(
                    f"{case_id} changed locked red-case {field} "
                    f"{singular_label}"
                )
        base_green = base_artifacts["xplatGreen"]
        current_green = current_artifacts["xplatGreen"]
        if not isinstance(base_green, dict) or not isinstance(
            current_green,
            dict,
        ):
            raise ValueError(
                f"{case_id} red-case green {root_field} must be "
                "platform maps"
            )
        removed_platforms = sorted(set(base_green) - set(current_green))
        if removed_platforms:
            raise ValueError(
                f"{case_id} removed retained green {root_field}: "
                + ", ".join(removed_platforms)
            )
        for platform, reference in base_green.items():
            if current_green.get(platform) != reference:
                raise ValueError(
                    f"{case_id} replaced retained green {singular_label} "
                    f"for {platform}"
                )
        added_platforms = set(current_green) - set(base_green)
        if not transitioned_to_green and added_platforms:
            raise ValueError(
                f"{case_id} retained green {root_field} while status "
                "remained red"
            )
        green_platform_sets.append(set(current_green))
    if len({frozenset(platforms) for platforms in green_platform_sets}) != 1:
        raise ValueError(
            f"{case_id} green artifact platform sets differ"
        )
    if base_evidence.get("regressionGate") is not None:
        raise ValueError(
            f"{case_id} red base evidence already claimed a regression gate"
        )
    current_gate_reference = current_evidence.get("regressionGate")
    if transitioned_to_green:
        if not isinstance(current_gate_reference, dict):
            raise ValueError(
                f"{case_id} red-to-green transition has no retained "
                "full-suite regression gate"
            )
        if base_cases is not None:
            gate, _ = load_content_addressed_document(
                current_gate_reference,
                "tests/parity/evidence/regression-gates",
                f"{case_id} transition regression gate",
                root=root,
            )
            selected_case_ids = set(
                require_string_array(
                    gate.get("selectedCaseIds"),
                    f"{case_id} transition regression gate "
                    "selectedCaseIds",
                )
            )
            expected_snapshot = (
                build_regression_manifest_case_snapshot(
                    base_cases,
                    selected_case_ids,
                )
            )
            if gate.get("manifestCases") != expected_snapshot:
                raise ValueError(
                    f"{case_id} transition regression gate is not bound "
                    "to the complete pre-promotion case inventory"
                )
            if gate.get("manifestCasesSha256") != (
                canonical_json_sha256(expected_snapshot)
            ):
                raise ValueError(
                    f"{case_id} transition regression gate historical "
                    "inventory digest is invalid"
                )
    elif current_gate_reference is not None:
        raise ValueError(
            f"{case_id} retained a regression gate while status remained "
            "red"
        )
    expected_classification = (
        "both-green"
        if transitioned_to_green
        else "legacy-green-xplat-red"
    )
    if current_evidence.get("classification") != expected_classification:
        raise ValueError(
            f"{case_id} evidence classification does not match transition"
        )


def validate_trusted_v1_migration(
    manifest: dict[str, Any],
    base_manifest: dict[str, Any],
    base_manifest_sha256: str | None,
    retained_evidence: list[dict[str, Any]] | None,
) -> None:
    if (
        base_manifest_sha256 is None
        or base_manifest_sha256.lower()
        != TRUSTED_V1_MANIFEST_SHA256
    ):
        raise ValueError(
            "unrecognized or tampered schema-v1 parity base manifest"
        )
    base_items = base_manifest.get("items")
    if not isinstance(base_items, list):
        raise ValueError("trusted schema-v1 base has no capability items")
    base_ids = {
        require_nonempty_string(
            item.get("id") if isinstance(item, dict) else None,
            "trusted schema-v1 capability ID",
        )
        for item in base_items
    }
    if base_ids != TRUSTED_V1_CAPABILITY_IDS:
        raise ValueError(
            "trusted schema-v1 base capability IDs do not match "
            "the reviewed migration"
        )
    current_capabilities = index_history_entries(
        manifest.get("items", []),
        "current capability",
    )
    if (
        migration_capability_anchor_sha256(
            list(current_capabilities.values())
        )
        != TRUSTED_V3_MIGRATION_CAPABILITY_SHA256
    ):
        raise ValueError(
            "schema-v1 migration capability semantics do not match the "
            "reviewed trust anchor"
        )
    missing = sorted(
        TRUSTED_V1_CAPABILITY_IDS - current_capabilities.keys()
    )
    if missing:
        raise ValueError(
            "schema-v1 migration removed capabilities: "
            + ", ".join(missing)
        )
    inherited_complete = sorted(
        capability_id
        for capability_id in TRUSTED_V1_CAPABILITY_IDS
        if current_capabilities[capability_id].get("acceptanceStatus")
        == "complete"
    )
    if inherited_complete:
        raise ValueError(
            "schema-v1 migration may not inherit complete status: "
            + ", ".join(inherited_complete)
        )
    current_obligations = index_history_entries(
        manifest.get("behavioralObligations", []),
        "schema-v1 migration behavioral obligation",
    )
    actual_obligation_owners = {
        obligation_id: require_nonempty_string(
            obligation.get("capabilityId"),
            f"schema-v1 migration obligation {obligation_id} capabilityId",
        )
        for obligation_id, obligation in current_obligations.items()
    }
    if actual_obligation_owners != TRUSTED_V3_MIGRATION_OBLIGATION_OWNERS:
        missing_obligations = sorted(
            TRUSTED_V3_MIGRATION_OBLIGATION_IDS
            - actual_obligation_owners.keys()
        )
        unexpected_obligations = sorted(
            actual_obligation_owners.keys()
            - TRUSTED_V3_MIGRATION_OBLIGATION_IDS
        )
        reowned_obligations = sorted(
            obligation_id
            for obligation_id in (
                TRUSTED_V3_MIGRATION_OBLIGATION_IDS
                & actual_obligation_owners.keys()
            )
            if actual_obligation_owners[obligation_id]
            != TRUSTED_V3_MIGRATION_OBLIGATION_OWNERS[obligation_id]
        )
        details: list[str] = []
        if missing_obligations:
            details.append("missing " + ", ".join(missing_obligations))
        if unexpected_obligations:
            details.append("unexpected " + ", ".join(unexpected_obligations))
        if reowned_obligations:
            details.append("reowned " + ", ".join(reowned_obligations))
        raise ValueError(
            "schema-v1 migration behavioral obligation inventory does not "
            "match the reviewed trust anchor: "
            + "; ".join(details)
        )
    if (
        migration_obligation_anchor_sha256(
            list(current_obligations.values())
        )
        != TRUSTED_V3_MIGRATION_OBLIGATION_SHA256
    ):
        raise ValueError(
            "schema-v1 migration behavioral obligation semantics or "
            "platforms do not match the reviewed trust anchor"
        )
    if (
        manifest.get("legacySurfaceInventory")
        != base_manifest.get("legacySurfaceInventory")
    ):
        raise ValueError(
            "schema-v1 migration changed the legacy surface inventory path"
        )
    expected_canonical_json = {
        "version": CANONICAL_JSON_VERSION,
        "vectors": CANONICAL_JSON_VECTOR_PATH,
        "vectorsSha256": CANONICAL_JSON_VECTORS_FILE_SHA256,
    }
    if manifest.get("canonicalJson") != expected_canonical_json:
        raise ValueError(
            "schema-v1 migration canonical JSON contract does not match "
            "the reviewed trust anchor"
        )
    reference = manifest.get("reference")
    if not isinstance(reference, dict):
        raise ValueError("schema-v1 migration has no reference trust anchor")
    for key, expected_value in TRUSTED_V3_MIGRATION_REFERENCE.items():
        actual_value = reference.get(key)
        if key.endswith("Sha256") and isinstance(actual_value, str):
            matches = actual_value.lower() == expected_value.lower()
        else:
            matches = actual_value == expected_value
        if not matches:
            raise ValueError(
                f"schema-v1 migration reference {key} does not match "
                "the reviewed trust anchor"
            )
    if retained_evidence is None:
        raise ValueError(
            "schema-v1 migration requires retained evidence validation"
        )
    migration_cases = manifest.get("cases")
    if not isinstance(migration_cases, list):
        raise ValueError("schema-v1 migration cases must be an array")
    non_red_cases = sorted(
        str(case.get("id"))
        for case in migration_cases
        if not isinstance(case, dict)
        or case.get("legacyTestStatus") != "pass"
        or case.get("xplatTestStatus") != "fail"
        or case.get("status") != "legacy-green-xplat-red"
        or case.get("firstGreenCommit") is not None
    )
    if non_red_cases:
        raise ValueError(
            "schema-v1 migration may contain only retained-red active "
            "cases: "
            + ", ".join(non_red_cases)
        )
    retained_by_id = {
        evidence.get("parityId"): evidence
        for evidence in retained_evidence
        if isinstance(evidence, dict)
    }
    if set(retained_by_id) != TRUSTED_V1_EVIDENCE_IDS:
        raise ValueError(
            "schema-v1 migration does not retain the reviewed 25 "
            "evidence records"
        )
    for parity_id, evidence in retained_by_id.items():
        if (
            evidence.get("schemaVersion") != 1
            or evidence.get("classification")
            != RETAINED_EVIDENCE_CLASSIFICATION
            or not isinstance(evidence.get("retention"), dict)
            or evidence["retention"].get("status")
            != RETAINED_EVIDENCE_STATUS
        ):
            raise ValueError(
                f"schema-v1 migration evidence is certifying or invalid: "
                f"{parity_id}"
            )


def canonical_document_sha256(
    relative_path: Any,
    *,
    root: Path,
    commit: str | None = None,
) -> str:
    return canonical_json_sha256(
        load_certified_document(
            relative_path,
            root=root,
            commit=commit,
        )
    )


def load_certified_document(
    relative_path: Any,
    *,
    root: Path,
    commit: str | None = None,
) -> dict[str, Any]:
    path_text = require_nonempty_string(
        relative_path,
        "certified document path",
    )
    untrusted = Path(path_text)
    if untrusted.is_absolute() or ".." in untrusted.parts:
        raise ValueError(
            f"certified document path must be repository-relative: "
            f"{path_text}"
        )
    if commit is None:
        path = (root / untrusted).resolve()
        if not is_within(path, root.resolve()) or not path.is_file():
            raise ValueError(
                f"certified document does not exist: {path_text}"
            )
        document = load_json(path)
    else:
        blob = subprocess.run(
            [
                "git",
                "-C",
                str(root),
                "show",
                f"{commit}:{untrusted.as_posix()}",
            ],
            capture_output=True,
            text=True,
            timeout=10,
            check=False,
        )
        if blob.returncode != 0:
            raise ValueError(
                f"base commit has no certified document: {path_text}"
            )
        document = parse_json_value(
            blob.stdout,
            f"certified document {path_text} at {commit}",
        )
    return document


def index_history_entries(
    entries: list[Any],
    label: str,
) -> dict[str, dict[str, Any]]:
    indexed: dict[str, dict[str, Any]] = {}
    for entry in entries:
        if not isinstance(entry, dict):
            raise ValueError(f"{label} must be an object")
        entry_id = require_nonempty_string(
            entry.get("id"),
            f"{label} ID",
        )
        if entry_id in indexed:
            raise ValueError(f"duplicate {label} ID: {entry_id}")
        indexed[entry_id] = entry
    return indexed


def sequence_is_subsequence(
    expected: list[Any],
    actual: list[Any],
) -> bool:
    actual_iterator = iter(actual)
    return all(
        any(candidate == entry for candidate in actual_iterator)
        for entry in expected
    )


def resolve_artifact_file(
    value: Path,
    label: str,
    *,
    root: Path = ROOT,
) -> Path:
    resolved = resolve_artifact_path(value, label, root=root)
    require_json_suffix(resolved, label)
    return resolved


def resolve_artifact_path(
    value: Path,
    label: str,
    *,
    root: Path = ROOT,
) -> Path:
    candidate = value if value.is_absolute() else root / value
    resolved = candidate.resolve()
    artifacts_root = (root / "artifacts").resolve()
    if not is_within(resolved, artifacts_root):
        raise ValueError(f"{label} must be below artifacts")
    if not resolved.is_file():
        raise ValueError(f"{label} does not exist: {resolved}")
    return resolved


def load_promotion_document(
    path: Path,
    label: str,
) -> tuple[dict[str, Any], bytes, str]:
    raw = path.read_bytes()
    if raw.startswith(b"\xef\xbb\xbf"):
        raise ValueError(f"{label} must not have a UTF-8 BOM")
    if b"\r" in raw:
        raise ValueError(f"{label} must use LF-only JSON bytes")
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError as error:
        raise ValueError(f"{label} must be UTF-8") from error
    document = parse_json_value(text, label)
    return document, raw, hashlib.sha256(raw).hexdigest()


def require_indexed_artifact_bytes(
    path: Path,
    raw: bytes,
    indexed_artifact_sha256: dict[Path, str] | None,
    label: str,
) -> None:
    if indexed_artifact_sha256 is None:
        return
    resolved = path.resolve()
    expected = indexed_artifact_sha256.get(resolved)
    if expected is None:
        raise ValueError(
            f"{label} is not present in the validated package index"
        )
    actual = hashlib.sha256(raw).hexdigest()
    if actual != expected:
        raise ValueError(
            f"{label} bytes changed after package-index validation"
        )


def validate_execution_envelope(
    envelope_path: Path,
    run_document: dict[str, Any],
    result_sha256: str,
    test_report_sha256: str,
    target: str,
    *,
    root: Path = ROOT,
    retained: bool = False,
) -> tuple[Path, bytes, str]:
    target_name = "Legacy" if target == "legacy" else "XPlat"
    if retained:
        candidate = (
            envelope_path
            if envelope_path.is_absolute()
            else root / envelope_path
        )
        path = candidate.resolve()
        permitted = (
            root / "tests/parity/evidence/executions"
        ).resolve()
        if not is_within(path, permitted) or not path.is_file():
            raise ValueError(
                f"{target_name} retained execution envelope is invalid"
            )
    else:
        path = resolve_artifact_file(
            envelope_path,
            f"{target_name} execution envelope",
            root=root,
        )
    document, raw, digest = load_promotion_document(
        path,
        f"{target_name} execution envelope",
    )
    root_fields = [
        "schemaVersion",
        "target",
        "platform",
        "operatingSystem",
        "architecture",
        "runtimeIdentifier",
        "revision",
        "tree",
        "resultSha256",
        "testReportSha256",
        "testProcessExitCode",
        "wrapper",
        "functionalDivergences",
    ]
    require_fields(
        document,
        set(root_fields),
        f"{target_name} execution envelope",
        allowed=set(root_fields),
    )
    if list(document) != root_fields or raw != serialize_lf_json(document):
        raise ValueError(
            f"{target_name} execution envelope is not canonical "
            "UTF-8/LF/indent-2 JSON"
        )
    if require_signed_integer(
        document["schemaVersion"],
        f"{target_name} execution envelope schemaVersion",
    ) != 1:
        raise ValueError(
            f"{target_name} execution envelope has unsupported schema"
        )
    if document["target"] != target_name:
        raise ValueError(
            f"{target_name} execution envelope target is invalid"
        )
    context = run_document.get("runContext")
    if not isinstance(context, dict):
        raise ValueError(
            f"{target_name} structured result runContext is invalid"
        )
    platform = document["platform"]
    if platform not in SUPPORTED_PLATFORMS:
        raise ValueError(
            f"{target_name} execution envelope platform is invalid"
        )
    expected_context = {
        "platform": context.get("platform"),
        "architecture": context.get("processArchitecture"),
        "runtimeIdentifier": context.get("runtimeIdentifier"),
    }
    for field, expected in expected_context.items():
        actual = require_nonempty_string(
            document[field],
            f"{target_name} execution envelope {field}",
        )
        if actual != expected:
            raise ValueError(
                f"{target_name} execution envelope {field} does not "
                "match the structured result"
            )
    require_nonempty_string(
        document["operatingSystem"],
        f"{target_name} execution envelope operatingSystem",
    )
    xplat = context.get("xplat")
    if not isinstance(xplat, dict):
        raise ValueError(
            f"{target_name} structured result XPlat context is invalid"
        )
    for field in ("revision", "tree"):
        value = require_nonempty_string(
            document[field],
            f"{target_name} execution envelope {field}",
        )
        if not COMMIT_PATTERN.fullmatch(value) or value != xplat.get(field):
            raise ValueError(
                f"{target_name} execution envelope {field} does not "
                "match the structured result"
            )
    for field, expected in (
        ("resultSha256", result_sha256),
        ("testReportSha256", test_report_sha256),
    ):
        actual = validate_sha256(
            document[field],
            f"{target_name} execution envelope {field}",
        )
        if actual != expected:
            raise ValueError(
                f"{target_name} execution envelope {field} is not bound"
            )
    wrapper = document["wrapper"]
    if not isinstance(wrapper, dict):
        raise ValueError(
            f"{target_name} execution envelope wrapper must be an object"
        )
    wrapper_fields = {"completed", "correlationValidated", "exitCode"}
    require_fields(
        wrapper,
        wrapper_fields,
        f"{target_name} execution envelope wrapper",
        allowed=wrapper_fields,
    )
    wrapper_exit_code = require_signed_integer(
        wrapper["exitCode"],
        f"{target_name} execution wrapper exitCode",
    )
    if (
        wrapper.get("completed") is not True
        or wrapper.get("correlationValidated") is not True
        or wrapper_exit_code != 0
    ):
        raise ValueError(
            f"{target_name} execution wrapper did not complete successfully"
        )
    results = run_document.get("results")
    if not isinstance(results, list):
        raise ValueError(
            f"{target_name} structured result set is invalid"
        )
    expected_divergences = sorted(
        (
            {
                "caseId": require_nonempty_string(
                    result.get("parityId"),
                    f"{target_name} divergence caseId",
                ),
                "failureCode": require_nonempty_string(
                    result.get("failureCode"),
                    f"{target_name} divergence failureCode",
                ),
            }
            for result in results
            if isinstance(result, dict)
            and result.get("outcome") == "functional-divergence"
        ),
        key=lambda entry: utf16_ordinal_key(entry["caseId"]),
    )
    if target == "legacy" and expected_divergences:
        raise ValueError(
            "Legacy execution envelope cannot certify divergences"
        )
    divergences = document["functionalDivergences"]
    if not isinstance(divergences, list):
        raise ValueError(
            f"{target_name} execution functionalDivergences must be an array"
        )
    for divergence in divergences:
        if not isinstance(divergence, dict):
            raise ValueError(
                f"{target_name} execution divergence must be an object"
            )
        require_fields(
            divergence,
            {"caseId", "failureCode"},
            f"{target_name} execution divergence",
            allowed={"caseId", "failureCode"},
        )
        require_nonempty_string(
            divergence["caseId"],
            f"{target_name} execution divergence caseId",
        )
        require_nonempty_string(
            divergence["failureCode"],
            f"{target_name} execution divergence failureCode",
        )
    if divergences != expected_divergences:
        raise ValueError(
            f"{target_name} execution divergence set does not match results"
        )
    expected_exit_code = (
        2 if target == "xplat" and expected_divergences else 0
    )
    process_exit_code = require_signed_integer(
        document["testProcessExitCode"],
        f"{target_name} execution testProcessExitCode",
    )
    if process_exit_code != expected_exit_code:
        raise ValueError(
            f"{target_name} execution test-process exit code is invalid"
        )
    return path, raw, digest


def xml_local_name(element: ElementTree.Element) -> str:
    return element.tag.rsplit("}", 1)[-1]


def direct_xml_children(
    element: ElementTree.Element,
    local_name: str,
) -> list[ElementTree.Element]:
    return [
        child
        for child in element
        if xml_local_name(child) == local_name
    ]


def functional_divergence_trx_message(
    parity_id: str,
    failure_code: str,
) -> str:
    marker = (
        "PARITY_FUNCTIONAL_DIVERGENCE|"
        f"{parity_id}|{failure_code}"
    )
    return (
        f"{FUNCTIONAL_DIVERGENCE_TRX_EXCEPTION_TYPE} : {marker}"
    )


def validate_test_report(
    report_path: Path,
    run_document: dict[str, Any],
    target: str,
    *,
    root: Path = ROOT,
    retained: bool = False,
) -> tuple[Path, bytes, str]:
    if retained:
        candidate = (
            report_path
            if report_path.is_absolute()
            else root / report_path
        )
        path = candidate.resolve()
        permitted = (
            root / "tests/parity/evidence/test-reports"
        ).resolve()
        if not is_within(path, permitted) or not path.is_file():
            raise ValueError(
                f"{target} retained acceptance test report is invalid"
            )
    else:
        path = resolve_artifact_path(
            report_path,
            f"{target} acceptance test report",
            root=root,
        )
    if path.suffix.lower() != ".trx":
        raise ValueError(
            f"{target} acceptance test report must use .trx"
        )
    raw = path.read_bytes()
    if raw.startswith(b"\xef\xbb\xbf"):
        raise ValueError(
            f"{target} acceptance test report must not have a UTF-8 BOM"
        )
    try:
        root_element = ElementTree.fromstring(raw)
    except ElementTree.ParseError as error:
        raise ValueError(
            f"{target} acceptance test report is not valid XML"
        ) from error
    if xml_local_name(root_element) != "TestRun":
        raise ValueError(
            f"{target} acceptance test report root must be TestRun"
        )
    summaries = direct_xml_children(root_element, "ResultSummary")
    if len(summaries) != 1:
        raise ValueError(
            f"{target} TRX must contain exactly one result summary"
        )
    summary = summaries[0]
    results = run_document.get("results")
    if not isinstance(results, list) or not results:
        raise ValueError(
            f"{target} structured result contains no acceptance cases"
        )
    structured_by_name: dict[str, dict[str, Any]] = {}
    for result in results:
        if not isinstance(result, dict):
            raise ValueError(
                f"{target} structured result contains a non-object result"
            )
        test_name = require_nonempty_string(
            result.get("acceptanceTestName"),
            f"{target} result acceptanceTestName",
        )
        if test_name in structured_by_name:
            raise ValueError(
                f"{target} structured result has duplicate acceptance "
                f"test name: {test_name}"
            )
        structured_by_name[test_name] = result
    unit_results = [
        element
        for element in root_element.iter()
        if xml_local_name(element) == "UnitTestResult"
    ]
    trx_by_name: dict[str, ElementTree.Element] = {}
    for result in unit_results:
        test_name = require_nonempty_string(
            result.get("testName"),
            f"{target} TRX UnitTestResult testName",
        )
        if test_name in trx_by_name:
            raise ValueError(
                f"{target} TRX contains duplicate testName: {test_name}"
            )
        outcome = require_nonempty_string(
            result.get("outcome"),
            f"{target} TRX {test_name} outcome",
        )
        if outcome not in {"Passed", "Failed"}:
            raise ValueError(
                f"{target} TRX test {test_name} has noncertifying "
                f"outcome {outcome}"
            )
        trx_by_name[test_name] = result
    expected_names = set(structured_by_name)
    actual_names = set(trx_by_name)
    if actual_names != expected_names:
        missing = sorted(expected_names - actual_names)
        extra = sorted(actual_names - expected_names)
        details: list[str] = []
        if missing:
            details.append("missing " + ", ".join(missing))
        if extra:
            details.append("extra " + ", ".join(extra))
        raise ValueError(
            f"{target} TRX active test set differs from structured "
            "results: "
            + "; ".join(details)
        )

    expected_failed: set[str] = set()
    expected_passed: set[str] = set()
    for test_name, structured in structured_by_name.items():
        parity_id = require_nonempty_string(
            structured.get("parityId"),
            f"{target} structured result parityId",
        )
        if test_name != f"parity:{parity_id}()":
            raise ValueError(
                f"{target} structured result test identity is stale: "
                f"{test_name}"
            )
        outcome = structured.get("outcome")
        if outcome == "passed":
            expected_passed.add(test_name)
        elif outcome == "functional-divergence":
            if target == "legacy":
                raise ValueError(
                    "Legacy acceptance report cannot certify a failed test"
                )
            expected_failed.add(test_name)
        else:
            raise ValueError(
                f"{target} structured result has noncertifying outcome"
            )
        trx_result = trx_by_name[test_name]
        expected_trx_outcome = (
            "Failed"
            if outcome == "functional-divergence"
            else "Passed"
        )
        if trx_result.get("outcome") != expected_trx_outcome:
            raise ValueError(
                f"{target} TRX outcome for {test_name} does not match "
                "the structured result"
            )
        error_infos = [
            element
            for element in trx_result.iter()
            if xml_local_name(element) == "ErrorInfo"
        ]
        if outcome == "functional-divergence":
            if len(error_infos) != 1:
                raise ValueError(
                    f"{target} expected-red test {test_name} must contain "
                    "exactly one TRX ErrorInfo"
                )
            messages = direct_xml_children(
                error_infos[0],
                "Message",
            )
            stacks = direct_xml_children(
                error_infos[0],
                "StackTrace",
            )
            failure_code = require_nonempty_string(
                structured.get("failureCode"),
                f"{target} {parity_id} functional divergence code",
            )
            expected_message = functional_divergence_trx_message(
                parity_id,
                failure_code,
            )
            actual_message = (
                messages[0].text if len(messages) == 1 else None
            )
            actual_stack = (
                stacks[0].text if len(stacks) == 1 else None
            )
            if (
                actual_message != expected_message
                or actual_stack is None
                or not actual_stack.strip()
            ):
                raise ValueError(
                    f"{target} expected-red test {test_name} lacks the "
                    "exact functional-divergence exception marker"
                )
        elif error_infos:
            raise ValueError(
                f"{target} passed test {test_name} contains TRX ErrorInfo"
            )

    counter_elements = direct_xml_children(summary, "Counters")
    if len(counter_elements) != 1:
        raise ValueError(
            f"{target} TRX must contain exactly one counters element"
        )
    expected_counters = {
        "total": len(results),
        "executed": len(results),
        "passed": len(expected_passed),
        "failed": len(expected_failed),
        "error": 0,
        "timeout": 0,
        "aborted": 0,
        "inconclusive": 0,
        "notExecuted": 0,
        "disconnected": 0,
        "warning": 0,
        "notRunnable": 0,
    }
    counters = counter_elements[0]
    for name, expected in expected_counters.items():
        actual = counters.get(name)
        try:
            parsed = int(actual) if actual is not None else None
        except ValueError as error:
            raise ValueError(
                f"{target} TRX counter {name} is invalid"
            ) from error
        if parsed != expected:
            raise ValueError(
                f"{target} TRX counter {name} must be {expected}"
            )
    summary_outcome = summary.get("outcome")
    if expected_failed:
        if summary_outcome != "Failed":
            raise ValueError(
                f"{target} expected-red TRX summary must be Failed"
            )
    elif summary_outcome not in {"Completed", "Passed"}:
        raise ValueError(
            f"{target} all-green TRX summary must be Completed or Passed"
        )
    return path, raw, hashlib.sha256(raw).hexdigest()


LEGACY_ORACLE_REGISTRY_ENTRY_FIELDS = (
    "adapterId",
    "versionId",
    "source",
    "sourceSha256",
    "buildRecipe",
    "buildRecipeSha256",
    "executable",
    "executableSha256",
    "provenance",
    "provenanceSha256",
)


def utf16_ordinal_key(value: str) -> bytes:
    validate_unicode_scalar_string(value, "ordinal string")
    return value.encode("utf-16-be")


def resolve_registry_artifact(
    value: Any,
    label: str,
    *,
    root: Path = ROOT,
    artifact_root: Path | None = None,
    require_json: bool = False,
) -> Path:
    path_text = require_nonempty_string(value, label)
    if "\\" in path_text:
        raise ValueError(f"{label} must use repository-relative / paths")
    untrusted = Path(path_text)
    if untrusted.is_absolute() or ".." in untrusted.parts:
        raise ValueError(f"{label} must be repository-relative")
    artifact_base = root if artifact_root is None else artifact_root
    resolved = (artifact_base / untrusted).resolve()
    permitted = (
        artifact_base / "artifacts/legacy-oracle"
    ).resolve()
    if not is_within(resolved, permitted):
        raise ValueError(f"{label} must be below artifacts/legacy-oracle")
    if not resolved.is_file():
        raise ValueError(f"{label} does not exist: {resolved}")
    if require_json:
        require_json_suffix(resolved, label)
    return resolved


def validate_legacy_oracle_registry(
    registry_path: Path,
    declared_sha256: Any,
    *,
    root: Path = ROOT,
    artifact_root: Path | None = None,
) -> tuple[
    Path,
    str,
    dict[str, dict[str, Any]],
]:
    path = resolve_artifact_file(
        registry_path,
        "Legacy oracle registry",
        root=root,
    )
    document, raw, actual_hash = load_promotion_document(
        path,
        "Legacy oracle registry",
    )
    expected_hash = validate_sha256(
        declared_sha256,
        "Legacy oracle registry sha256",
    )
    if actual_hash != expected_hash:
        raise ValueError(
            "Legacy oracle registry raw SHA-256 does not match "
            "--legacy-oracle-registry-sha256"
        )
    root_fields = {"schemaVersion", "entries"}
    require_fields(
        document,
        root_fields,
        "Legacy oracle registry",
        allowed=root_fields,
    )
    if document["schemaVersion"] != 1:
        raise ValueError(
            "Legacy oracle registry has unsupported schema"
        )
    entries = document["entries"]
    if not isinstance(entries, list) or not entries:
        raise ValueError(
            "Legacy oracle registry entries must be a non-empty array"
        )
    versions: list[str] = []
    by_version: dict[str, dict[str, Any]] = {}
    canonical_entries: list[dict[str, Any]] = []
    entry_fields = set(LEGACY_ORACLE_REGISTRY_ENTRY_FIELDS)
    for index, entry in enumerate(entries):
        if not isinstance(entry, dict):
            raise ValueError(
                f"Legacy oracle registry entry {index} must be an object"
            )
        require_fields(
            entry,
            entry_fields,
            f"Legacy oracle registry entry {index}",
            allowed=entry_fields,
        )
        adapter_id = require_nonempty_string(
            entry["adapterId"],
            f"Legacy oracle registry entry {index} adapterId",
        )
        version_id = require_nonempty_string(
            entry["versionId"],
            f"Legacy oracle registry entry {index} versionId",
        )
        if not LEGACY_ORACLE_VERSION_PATTERN.fullmatch(version_id):
            raise ValueError(
                f"Legacy oracle registry versionId is invalid: "
                f"{version_id}"
            )
        if version_id in by_version:
            raise ValueError(
                "Legacy oracle registry versionId must be globally "
                f"unique: {version_id}"
            )
        require_nonempty_string(
            adapter_id,
            f"Legacy oracle registry {version_id} adapterId",
        )
        source = resolve_repo_file(
            entry["source"],
            "tests/parity",
            f"Legacy oracle registry {version_id} source",
            root=root,
        )
        recipe = resolve_repo_file(
            entry["buildRecipe"],
            "tests/parity",
            f"Legacy oracle registry {version_id} buildRecipe",
            root=root,
        )
        require_json_suffix(
            recipe,
            f"Legacy oracle registry {version_id} buildRecipe",
        )
        executable = resolve_registry_artifact(
            entry["executable"],
            f"Legacy oracle registry {version_id} executable",
            root=root,
            artifact_root=artifact_root,
        )
        provenance = resolve_registry_artifact(
            entry["provenance"],
            f"Legacy oracle registry {version_id} provenance",
            root=root,
            artifact_root=artifact_root,
            require_json=True,
        )
        for field, artifact in (
            ("sourceSha256", source),
            ("buildRecipeSha256", recipe),
            ("executableSha256", executable),
            ("provenanceSha256", provenance),
        ):
            declared = validate_sha256(
                entry[field],
                f"Legacy oracle registry {version_id} {field}",
            )
            if sha256_file(artifact) != declared:
                raise ValueError(
                    f"Legacy oracle registry {version_id} {field} "
                    "does not match artifact bytes"
                )
        versions.append(version_id)
        by_version[version_id] = entry
        canonical_entries.append(
            {
                field: entry[field]
                for field in LEGACY_ORACLE_REGISTRY_ENTRY_FIELDS
            }
        )
    if versions != sorted(versions, key=utf16_ordinal_key):
        raise ValueError(
            "Legacy oracle registry entries must be ordinally sorted "
            "by versionId"
        )
    canonical = {
        "schemaVersion": 1,
        "entries": canonical_entries,
    }
    if raw != serialize_lf_json(canonical):
        raise ValueError(
            "Legacy oracle registry must use canonical UTF-8 LF "
            "indent-2 JSON with reviewed field order"
        )
    return path, actual_hash, by_version


def validate_full_suite_package_index(
    index_path: Path,
    declared_index_sha256: Any,
    registry_path: Path,
    declared_registry_sha256: Any,
    full_suite_results: dict[str, Path],
    full_suite_test_reports: dict[str, Path],
    full_suite_executions: dict[str, Path],
    *,
    root: Path = ROOT,
) -> tuple[
    Path,
    str,
    dict[str, dict[str, Any]],
    Path,
    Path,
    dict[Path, str],
]:
    index_sha256 = validate_sha256(
        declared_index_sha256,
        "full-suite package index SHA-256",
    )
    path = resolve_artifact_file(
        index_path,
        "full-suite package index",
        root=root,
    )
    import_root = (root / "artifacts/parity-imports").resolve()
    try:
        package_root = path.parents[3]
    except IndexError as error:
        raise ValueError(
            "full-suite package index path is not under an immutable "
            "import root"
        ) from error
    expected_path = (
        package_root
        / "artifacts/parity-full-suite/windows-both-package-index"
        / f"{index_sha256}.json"
    ).resolve()
    if (
        package_root.parent != import_root
        or package_root.name != index_sha256
        or path != expected_path
    ):
        raise ValueError(
            "full-suite package index must use immutable "
            "artifacts/parity-imports/<index-sha256>/artifacts layout"
        )
    document, _, actual_index_sha256 = load_promotion_document(
        path,
        "full-suite package index",
    )
    if actual_index_sha256 != index_sha256:
        raise ValueError(
            "full-suite package index raw SHA-256 does not match its "
            "declared digest"
        )
    fields = {
        "schemaVersion",
        "platform",
        "target",
        "registry",
        "files",
    }
    require_fields(
        document,
        fields,
        "full-suite package index",
        allowed=fields,
    )
    if (
        require_signed_integer(
            document["schemaVersion"],
            "full-suite package index schemaVersion",
        )
        != 1
        or document["platform"] != "windows"
        or document["target"] != "Both"
    ):
        raise ValueError(
            "full-suite package index has an invalid schema or target"
        )
    registry = document["registry"]
    if not isinstance(registry, dict):
        raise ValueError(
            "full-suite package index registry must be an object"
        )
    require_fields(
        registry,
        {"path", "sha256"},
        "full-suite package index registry",
        allowed={"path", "sha256"},
    )
    registry_relative = require_canonical_repo_relative_path(
        registry["path"],
        "full-suite package index registry path",
    )
    if not registry_relative.startswith("artifacts/legacy-oracle/"):
        raise ValueError(
            "full-suite package index registry must be below "
            "artifacts/legacy-oracle"
        )
    registry_sha256 = validate_sha256(
        registry["sha256"],
        "full-suite package index registry SHA-256",
    )
    expected_registry_sha256 = validate_sha256(
        declared_registry_sha256,
        "full-suite Legacy oracle registry SHA-256",
    )
    resolved_registry = resolve_artifact_file(
        registry_path,
        "full-suite Legacy oracle registry",
        root=root,
    )
    if (
        resolved_registry
        != (package_root / registry_relative).resolve()
        or registry_sha256 != expected_registry_sha256
        or sha256_file(resolved_registry) != registry_sha256
    ):
        raise ValueError(
            "full-suite package index registry binding does not exactly "
            "match the supplied registry path and SHA-256"
        )

    files = document["files"]
    if not isinstance(files, list) or not files:
        raise ValueError(
            "full-suite package index files must be a non-empty array"
        )
    indexed_files: dict[str, str] = {}
    ordered_paths: list[str] = []
    for index, entry in enumerate(files):
        if not isinstance(entry, dict):
            raise ValueError(
                f"full-suite package file {index} must be an object"
            )
        require_fields(
            entry,
            {"path", "sha256"},
            f"full-suite package file {index}",
            allowed={"path", "sha256"},
        )
        relative = require_canonical_repo_relative_path(
            entry["path"],
            f"full-suite package file {index} path",
        )
        if not relative.startswith("artifacts/"):
            raise ValueError(
                "full-suite package files must be below artifacts"
            )
        if relative in indexed_files:
            raise ValueError(
                f"duplicate full-suite package file path: {relative}"
            )
        digest = validate_sha256(
            entry["sha256"],
            f"full-suite package file {relative} SHA-256",
        )
        candidate = (package_root / relative).resolve()
        if (
            not is_within(candidate, package_root)
            or not candidate.is_file()
            or sha256_file(candidate) != digest
        ):
            raise ValueError(
                f"full-suite package file is missing or changed: "
                f"{relative}"
            )
        indexed_files[relative] = digest
        ordered_paths.append(relative)
    if ordered_paths != sorted(ordered_paths, key=utf16_ordinal_key):
        raise ValueError(
            "full-suite package file entries must be ordinally sorted"
        )
    actual_files = {
        candidate.relative_to(package_root).as_posix()
        for candidate in package_root.rglob("*")
        if candidate.is_file() and candidate.resolve() != path
    }
    if actual_files != set(indexed_files):
        missing = sorted(set(indexed_files) - actual_files)
        unindexed = sorted(actual_files - set(indexed_files))
        details: list[str] = []
        if missing:
            details.append("missing " + ", ".join(missing))
        if unindexed:
            details.append("unindexed " + ", ".join(unindexed))
        raise ValueError(
            "full-suite imported package does not have the exact indexed "
            "file closure"
            + (": " + "; ".join(details) if details else "")
        )
    if indexed_files.get(registry_relative) != registry_sha256:
        raise ValueError(
            "full-suite package registry is absent from its file closure"
        )
    indexed_artifact_sha256 = {
        (package_root / relative).resolve(): digest
        for relative, digest in indexed_files.items()
    }

    expected_role_paths = {
        ("result", "windows/Legacy"): "artifacts/parity/legacy.json",
        ("result", "windows/XPlat"): "artifacts/parity/xplat.json",
        (
            "test report",
            "windows/Legacy",
        ): "artifacts/parity/test-results/legacy.trx",
        (
            "test report",
            "windows/XPlat",
        ): "artifacts/parity/test-results/xplat.trx",
    }
    for (label, key), expected_relative in expected_role_paths.items():
        artifact_map = (
            full_suite_results
            if label == "result"
            else full_suite_test_reports
        )
        candidate = resolve_artifact_path(
            artifact_map[key],
            f"{key} full-suite {label}",
            root=root,
        )
        if (
            candidate != (package_root / expected_relative).resolve()
            or indexed_files.get(expected_relative)
            != sha256_file(candidate)
        ):
            raise ValueError(
                f"{key} full-suite {label} is not exactly bound to the "
                "imported package index"
            )
    for key, target_name in (
        ("windows/Legacy", "legacy"),
        ("windows/XPlat", "xplat"),
    ):
        candidate = resolve_artifact_path(
            full_suite_executions[key],
            f"{key} full-suite execution",
            root=root,
        )
        expected_prefix = (
            f"artifacts/parity/executions/{target_name}/"
        )
        try:
            relative = candidate.relative_to(package_root).as_posix()
        except ValueError as error:
            raise ValueError(
                f"{key} full-suite execution is outside the imported "
                "package"
            ) from error
        indexed_matches = [
            indexed
            for indexed in indexed_files
            if indexed.startswith(expected_prefix)
            and indexed.endswith(".json")
        ]
        if (
            len(indexed_matches) != 1
            or relative != indexed_matches[0]
            or indexed_files.get(relative) != sha256_file(candidate)
        ):
            raise ValueError(
                f"{key} full-suite execution is not exactly bound to the "
                "imported package index"
            )
    integration_report = (
        "artifacts/parity/test-results/"
        "legacy-oracle-build-integration.trx"
    )
    integration_path = (package_root / integration_report).resolve()
    if (
        indexed_files.get(integration_report)
        != (
            sha256_file(integration_path)
            if integration_path.is_file()
            else None
        )
    ):
        raise ValueError(
            "full-suite package omits the Legacy oracle build integration "
            "test report"
    )
    validate_legacy_oracle_build_integration_report(integration_path)

    _, _, entries = validate_legacy_oracle_registry(
        resolved_registry,
        registry_sha256,
        root=root,
        artifact_root=package_root,
    )
    provenance_case_ids: set[str] = set()
    for entry in entries.values():
        for field in ("executable", "provenance"):
            relative = entry[field]
            if indexed_files.get(relative) != entry[f"{field}Sha256"]:
                raise ValueError(
                    "full-suite package omits or changes a "
                    f"registry-referenced {field}"
                )
        provenance_path = (
            package_root / entry["provenance"]
        ).resolve()
        provenance, _, _ = load_promotion_document(
            provenance_path,
            "full-suite package Legacy provenance",
        )
        provenance_case_ids.update(
            require_string_array(
                provenance.get("selectedCaseIds"),
                "full-suite package Legacy provenance selectedCaseIds",
            )
        )
    expected_windows_case_ids = load_json(
        full_suite_results["windows/Legacy"]
    ).get("expectedParityIds")
    if not isinstance(expected_windows_case_ids, list):
        raise ValueError(
            "Windows Legacy full-suite result has no case inventory"
        )
    expected_windows_case_ids = require_string_array(
        expected_windows_case_ids,
        "Windows Legacy full-suite expectedParityIds",
    )
    if sorted(
        provenance_case_ids,
        key=utf16_ordinal_key,
    ) != expected_windows_case_ids:
        raise ValueError(
            "full-suite package registry provenance case union does not "
            "match the Windows Legacy result inventory"
        )
    integration_execution_prefix = (
        "artifacts/parity/executions/"
        "legacy-oracle-build-integration/"
    )
    integration_executions = [
        relative
        for relative in indexed_files
        if relative.startswith(integration_execution_prefix)
        and relative.endswith(".json")
    ]
    if len(integration_executions) != 1:
        raise ValueError(
            "full-suite package must contain exactly one Legacy oracle "
            "build integration execution envelope"
        )
    integration_execution_relative = integration_executions[0]
    integration_execution_path = (
        package_root / integration_execution_relative
    ).resolve()
    execution_hash = sha256_file(integration_execution_path)
    if (
        indexed_files[integration_execution_relative] != execution_hash
        or Path(integration_execution_relative).name
        != f"{execution_hash}.json"
    ):
        raise ValueError(
            "Legacy oracle build integration execution envelope is not "
            "content-addressed by the package index"
        )
    windows_legacy = load_json(
        full_suite_results["windows/Legacy"]
    )
    windows_run_context = windows_legacy.get("runContext")
    if not isinstance(windows_run_context, dict):
        raise ValueError(
            "Windows Legacy full-suite result has no runContext"
        )
    validate_legacy_oracle_build_integration_execution(
        integration_execution_path,
        test_report_sha256=sha256_file(integration_path),
        registry_sha256=registry_sha256,
        expected_selected_case_ids=expected_windows_case_ids,
        expected_run_context=windows_run_context,
    )
    return (
        package_root,
        actual_index_sha256,
        entries,
        integration_path,
        integration_execution_path,
        indexed_artifact_sha256,
    )


def validate_legacy_oracle_build_integration_report(
    path: Path,
) -> None:
    expected_test_name = (
        "MorseRunner.LegacyParity.Tests.LegacyOracleTargetTests."
        "ConfiguredBuildRegistryAttestsAndExecutesEverySelectedCase"
    )
    raw = path.read_bytes()
    try:
        root_element = ElementTree.fromstring(raw)
    except ElementTree.ParseError as error:
        raise ValueError(
            "Legacy oracle build integration TRX is not valid XML"
        ) from error
    if xml_local_name(root_element) != "TestRun":
        raise ValueError(
            "Legacy oracle build integration TRX root is not TestRun"
        )
    results = [
        element
        for element in root_element.iter()
        if xml_local_name(element) == "UnitTestResult"
    ]
    if (
        len(results) != 1
        or results[0].get("testName") != expected_test_name
        or results[0].get("outcome") != "Passed"
    ):
        raise ValueError(
            "Legacy oracle build integration TRX must contain exactly "
            "the configured-registry test passing once"
        )
    if any(
        xml_local_name(element) in {"Output", "ErrorInfo"}
        for element in results[0].iter()
        if element is not results[0]
    ):
        raise ValueError(
            "passing Legacy oracle build integration TRX contains "
            "captured errors"
        )
    summaries = [
        element
        for element in root_element
        if xml_local_name(element) == "ResultSummary"
    ]
    if (
        len(summaries) != 1
        or summaries[0].get("outcome") not in {"Completed", "Passed"}
    ):
        raise ValueError(
            "Legacy oracle build integration TRX summary is not a "
            "successful completion"
        )
    counters = [
        element
        for element in summaries[0]
        if xml_local_name(element) == "Counters"
    ]
    if len(counters) != 1:
        raise ValueError(
            "Legacy oracle build integration TRX has no unique counters"
        )
    successful = {"total": 1, "executed": 1, "passed": 1}
    zero = {
        "error",
        "failed",
        "timeout",
        "aborted",
        "inconclusive",
        "passedButRunAborted",
        "notRunnable",
        "notExecuted",
        "disconnected",
        "warning",
        "completed",
        "inProgress",
        "pending",
    }
    try:
        successful_match = all(
            int(counters[0].get(field, "-1")) == expected
            for field, expected in successful.items()
        )
        zero_match = all(
            int(counters[0].get(field, "-1")) == 0
            for field in zero
        )
    except ValueError as error:
        raise ValueError(
            "Legacy oracle build integration TRX counters are invalid"
        ) from error
    if not successful_match or not zero_match:
        raise ValueError(
            "Legacy oracle build integration process did not certify one "
            "passing test with zero failures"
        )


def validate_legacy_oracle_build_integration_execution(
    path: Path,
    *,
    test_report_sha256: str,
    registry_sha256: str,
    expected_selected_case_ids: list[str],
    expected_run_context: dict[str, Any],
) -> tuple[bytes, str]:
    document, raw, digest = load_promotion_document(
        path,
        "Legacy oracle build integration execution envelope",
    )
    ordered_fields = [
        "schemaVersion",
        "target",
        "platform",
        "architecture",
        "runtimeIdentifier",
        "revision",
        "tree",
        "registrySha256",
        "selectedCaseIds",
        "testName",
        "testReportSha256",
        "testProcessExitCode",
        "wrapper",
    ]
    require_fields(
        document,
        set(ordered_fields),
        "Legacy oracle build integration execution envelope",
        allowed=set(ordered_fields),
    )
    if (
        list(document) != ordered_fields
        or raw != serialize_lf_json(document)
    ):
        raise ValueError(
            "Legacy oracle build integration execution envelope is not "
            "canonical UTF-8/LF/indent-2 JSON"
        )
    if (
        require_signed_integer(
            document["schemaVersion"],
            "Legacy oracle build integration execution schemaVersion",
        )
        != 1
        or document["target"] != "LegacyOracleBuildIntegration"
        or document["platform"] != "windows"
    ):
        raise ValueError(
            "Legacy oracle build integration execution target is invalid"
        )
    expected_platform = require_nonempty_string(
        expected_run_context.get("platform"),
        "Windows full-suite runContext platform",
    )
    expected_architecture = require_nonempty_string(
        expected_run_context.get("processArchitecture"),
        "Windows full-suite runContext processArchitecture",
    ).lower()
    expected_runtime_identifier = require_nonempty_string(
        expected_run_context.get("runtimeIdentifier"),
        "Windows full-suite runContext runtimeIdentifier",
    ).lower()
    if (
        expected_platform != "windows"
        or expected_architecture
        not in SUPPORTED_PROCESS_ARCHITECTURES
        or not expected_runtime_identifier.startswith(
            RUNTIME_IDENTIFIER_PREFIXES["windows"]
        )
        or not expected_runtime_identifier.endswith(
            "-" + expected_architecture
        )
    ):
        raise ValueError(
            "Windows full-suite runContext has an invalid runtime identity"
        )
    architecture = require_nonempty_string(
        document["architecture"],
        "Legacy oracle build integration execution architecture",
    ).lower()
    runtime_identifier = require_nonempty_string(
        document["runtimeIdentifier"],
        "Legacy oracle build integration execution runtimeIdentifier",
    ).lower()
    if (
        architecture != expected_architecture
        or runtime_identifier != expected_runtime_identifier
    ):
        raise ValueError(
            "Legacy oracle build integration execution runtime identity "
            "does not match the Windows full-suite run"
        )
    expected_xplat_context = expected_run_context.get("xplat")
    if not isinstance(expected_xplat_context, dict):
        raise ValueError(
            "Windows full-suite runContext has no XPlat identity"
        )
    for field in ("revision", "tree"):
        value = require_nonempty_string(
            document[field],
            f"Legacy oracle build integration execution {field}",
        )
        if (
            not COMMIT_PATTERN.fullmatch(value)
            or value != expected_xplat_context.get(field)
        ):
            raise ValueError(
                "Legacy oracle build integration execution does not bind "
                f"the full-suite XPlat {field}"
            )
    if (
        validate_sha256(
            document["registrySha256"],
            "Legacy oracle build integration execution registrySha256",
        )
        != registry_sha256
        or validate_sha256(
            document["testReportSha256"],
            "Legacy oracle build integration execution "
            "testReportSha256",
        )
        != test_report_sha256
    ):
        raise ValueError(
            "Legacy oracle build integration execution artifact hashes "
            "are not bound"
        )
    selected_case_ids = require_string_array(
        document["selectedCaseIds"],
        "Legacy oracle build integration execution selectedCaseIds",
    )
    if (
        selected_case_ids
        != sorted(selected_case_ids, key=utf16_ordinal_key)
        or len(selected_case_ids) != len(set(selected_case_ids))
        or selected_case_ids != expected_selected_case_ids
    ):
        raise ValueError(
            "Legacy oracle build integration execution selectedCaseIds "
            "do not exactly match registry provenance"
        )
    expected_test_name = (
        "MorseRunner.LegacyParity.Tests.LegacyOracleTargetTests."
        "ConfiguredBuildRegistryAttestsAndExecutesEverySelectedCase"
    )
    if document["testName"] != expected_test_name:
        raise ValueError(
            "Legacy oracle build integration execution testName is invalid"
        )
    process_exit_code = require_signed_integer(
        document["testProcessExitCode"],
        "Legacy oracle build integration execution testProcessExitCode",
    )
    wrapper = document["wrapper"]
    if not isinstance(wrapper, dict):
        raise ValueError(
            "Legacy oracle build integration execution wrapper is invalid"
        )
    require_fields(
        wrapper,
        {"completed", "correlationValidated", "exitCode"},
        "Legacy oracle build integration execution wrapper",
        allowed={"completed", "correlationValidated", "exitCode"},
    )
    wrapper_exit_code = require_signed_integer(
        wrapper["exitCode"],
        "Legacy oracle build integration execution wrapper exitCode",
    )
    if (
        process_exit_code != 0
        or wrapper_exit_code != 0
        or wrapper["completed"] is not True
        or wrapper["correlationValidated"] is not True
    ):
        raise ValueError(
            "Legacy oracle build integration execution did not bind an "
            "actual successful process"
        )
    return raw, digest


def select_legacy_oracle_registry_entry(
    case: dict[str, Any],
    entries_by_version: dict[str, dict[str, Any]],
) -> dict[str, Any]:
    case_id = case["id"]
    descriptor = case["legacyOracle"]
    version_id = descriptor["versionId"]
    entry = entries_by_version.get(version_id)
    if entry is None:
        raise ValueError(
            f"{case_id} legacyOracle version is absent from the runtime "
            f"registry: {version_id}"
        )
    for field in (
        "adapterId",
        "versionId",
        "source",
        "sourceSha256",
        "buildRecipe",
        "buildRecipeSha256",
    ):
        if entry[field] != descriptor[field]:
            raise ValueError(
                f"{case_id} runtime registry {field} does not match "
                "the immutable case descriptor"
            )
    return entry


def content_reference(
    relative_directory: str,
    digest: str,
) -> dict[str, str]:
    return {
        "path": f"{relative_directory}/{digest}.json",
        "sha256": digest,
    }


def retain_content_addressed_bytes(
    raw: bytes,
    digest: str,
    relative_directory: str,
    *,
    root: Path = ROOT,
) -> dict[str, str]:
    destination_directory = (root / relative_directory).resolve()
    permitted_root = (root / "tests/parity/evidence").resolve()
    if not is_within(destination_directory, permitted_root):
        raise ValueError("content-addressed evidence directory escapes root")
    destination_directory.mkdir(parents=True, exist_ok=True)
    destination = destination_directory / f"{digest}.json"
    if destination.is_file():
        if destination.read_bytes() != raw:
            raise ValueError(
                f"content-addressed collision at {destination}"
            )
        return content_reference(relative_directory, digest)

    temporary_path: Path | None = None
    try:
        descriptor, temporary_name = tempfile.mkstemp(
            prefix=f".{digest}.",
            suffix=".tmp",
            dir=destination_directory,
        )
        temporary_path = Path(temporary_name)
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(raw)
            stream.flush()
            os.fsync(stream.fileno())
        try:
            os.link(temporary_path, destination)
        except FileExistsError:
            if destination.read_bytes() != raw:
                raise ValueError(
                    f"content-addressed collision at {destination}"
                )
        if sha256_file(destination) != digest:
            raise ValueError(
                f"retained content hash changed at {destination}"
            )
    finally:
        if temporary_path is not None:
            temporary_path.unlink(missing_ok=True)
    return content_reference(relative_directory, digest)


def serialize_lf_json(document: dict[str, Any]) -> bytes:
    return (
        json.dumps(
            document,
            ensure_ascii=False,
            allow_nan=False,
            indent=2,
        )
        + "\n"
    ).encode("utf-8")


def prepare_content_addressed_file(
    raw: bytes,
    relative_directory: str,
    *,
    suffix: str,
    root: Path = ROOT,
) -> tuple[dict[str, str], Path]:
    digest = hashlib.sha256(raw).hexdigest()
    directory = (root / relative_directory).resolve()
    permitted = (root / "tests/parity/evidence").resolve()
    if not is_within(directory, permitted):
        raise ValueError(
            "content-addressed evidence directory escapes root"
        )
    path = directory / f"{digest}{suffix}"
    return (
        {
            "path": (
                f"{relative_directory}/{digest}{suffix}"
            ),
            "sha256": digest,
        },
        path,
    )


def add_transaction_change(
    changes: dict[Path, tuple[bytes, bool]],
    path: Path,
    raw: bytes,
    *,
    allow_replace: bool,
) -> None:
    resolved = path.resolve()
    existing_change = changes.get(resolved)
    if existing_change is not None:
        if existing_change[0] != raw:
            raise ValueError(
                f"transaction assigns conflicting bytes to {resolved}"
            )
        return
    if resolved.exists():
        existing = resolved.read_bytes()
        if existing == raw:
            return
        if not allow_replace:
            raise ValueError(
                f"immutable transaction target already exists with "
                f"different bytes: {resolved}"
            )
    changes[resolved] = (raw, allow_replace)


def commit_file_transaction(
    changes: dict[Path, tuple[bytes, bool]],
) -> None:
    before: dict[Path, bytes | None] = {
        path: path.read_bytes() if path.is_file() else None
        for path in changes
    }
    staged: dict[Path, Path] = {}
    committed: list[Path] = []
    committed_identities: dict[Path, tuple[int, int]] = {}
    try:
        for path, (raw, _) in changes.items():
            path.parent.mkdir(parents=True, exist_ok=True)
            descriptor, temporary_name = tempfile.mkstemp(
                prefix=f".{path.name}.transaction.",
                suffix=".tmp",
                dir=path.parent,
            )
            temporary = Path(temporary_name)
            staged[path] = temporary
            with os.fdopen(descriptor, "wb") as stream:
                stream.write(raw)
                stream.flush()
                os.fsync(stream.fileno())
        for path, (_, allow_replace) in changes.items():
            original = before[path]
            if allow_replace:
                current = path.read_bytes() if path.is_file() else None
                if current != original:
                    raise ValueError(
                        f"mutable transaction target changed before "
                        f"commit: {path}"
                    )
            elif path.exists():
                raise ValueError(
                    f"immutable transaction target appeared before "
                    f"commit: {path}"
                )
        for path, temporary in staged.items():
            _, allow_replace = changes[path]
            if allow_replace:
                current = (
                    path.read_bytes() if path.is_file() else None
                )
                if current != before[path]:
                    raise ValueError(
                        f"mutable transaction target changed during "
                        f"commit: {path}"
                    )
                os.replace(temporary, path)
            else:
                try:
                    os.link(temporary, path)
                except FileExistsError as error:
                    raise ValueError(
                        "immutable transaction target appeared during "
                        f"commit: {path}"
                    ) from error
                temporary.unlink()
            committed.append(path)
            stat = path.stat()
            committed_identities[path] = (
                stat.st_dev,
                stat.st_ino,
            )
        for path, (raw, _) in changes.items():
            if path.read_bytes() != raw:
                raise ValueError(
                    f"transaction verification failed for {path}"
                )
    except BaseException:
        for path in reversed(committed):
            try:
                current_stat = path.stat()
            except FileNotFoundError:
                continue
            if (
                current_stat.st_dev,
                current_stat.st_ino,
            ) != committed_identities[path]:
                continue
            original = before[path]
            if original is None:
                path.unlink(missing_ok=True)
                continue
            descriptor, rollback_name = tempfile.mkstemp(
                prefix=f".{path.name}.rollback.",
                suffix=".tmp",
                dir=path.parent,
            )
            rollback = Path(rollback_name)
            try:
                with os.fdopen(descriptor, "wb") as stream:
                    stream.write(original)
                    stream.flush()
                    os.fsync(stream.fileno())
                os.replace(rollback, path)
            finally:
                rollback.unlink(missing_ok=True)
        raise
    finally:
        for temporary in staged.values():
            temporary.unlink(missing_ok=True)


@contextmanager
def repository_promotion_lock(
    root: Path = ROOT,
    *,
    timeout_seconds: float = 30.0,
) -> Any:
    if timeout_seconds <= 0:
        raise ValueError("promotion lock timeout must be positive")
    lock_root = (root / "artifacts").resolve()
    lock_root.mkdir(parents=True, exist_ok=True)
    lock_path = lock_root / ".parity-promotion.lock"
    stream = lock_path.open("a+b")
    acquired = False
    deadline = time.monotonic() + timeout_seconds
    try:
        if os.name == "nt":
            import msvcrt

            stream.seek(0, os.SEEK_END)
            if stream.tell() == 0:
                stream.write(b"\0")
                stream.flush()
            while not acquired:
                try:
                    stream.seek(0)
                    msvcrt.locking(
                        stream.fileno(),
                        msvcrt.LK_NBLCK,
                        1,
                    )
                    acquired = True
                except OSError:
                    if time.monotonic() >= deadline:
                        raise ValueError(
                            "timed out waiting for the repository parity "
                            "promotion lock"
                        )
                    time.sleep(0.05)
        else:
            import fcntl

            while not acquired:
                try:
                    fcntl.flock(
                        stream.fileno(),
                        fcntl.LOCK_EX | fcntl.LOCK_NB,
                    )
                    acquired = True
                except BlockingIOError:
                    if time.monotonic() >= deadline:
                        raise ValueError(
                            "timed out waiting for the repository parity "
                            "promotion lock"
                        )
                    time.sleep(0.05)
        yield lock_path
    finally:
        if acquired:
            if os.name == "nt":
                import msvcrt

                stream.seek(0)
                msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                import fcntl

                fcntl.flock(stream.fileno(), fcntl.LOCK_UN)
        stream.close()


def write_promoted_evidence(
    path: Path,
    raw: bytes,
    *,
    allow_replace: bool,
) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists() and not allow_replace:
        raise ValueError(
            f"active evidence already exists; refusing overwrite: {path}"
        )
    temporary_path: Path | None = None
    try:
        descriptor, temporary_name = tempfile.mkstemp(
            prefix=f".{path.name}.",
            suffix=".tmp",
            dir=path.parent,
        )
        temporary_path = Path(temporary_name)
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(raw)
            stream.flush()
            os.fsync(stream.fileno())
        if allow_replace:
            os.replace(temporary_path, path)
            temporary_path = None
        else:
            try:
                os.link(temporary_path, path)
            except FileExistsError as error:
                raise ValueError(
                    "active evidence appeared during promotion; refusing "
                    f"overwrite: {path}"
                ) from error
    finally:
        if temporary_path is not None:
            temporary_path.unlink(missing_ok=True)


def select_manifest_case(
    manifest: dict[str, Any],
    case_id: str,
) -> dict[str, Any]:
    matching = [
        case
        for case in manifest["cases"]
        if isinstance(case, dict) and case.get("id") == case_id
    ]
    if len(matching) != 1:
        raise ValueError(
            f"promotion case must identify exactly one manifest case: "
            f"{case_id}"
        )
    return matching[0]


def validate_live_run_document_for_manifest(
    manifest: dict[str, Any],
    document: dict[str, Any],
    target: str,
    *,
    registry_entries: dict[str, dict[str, Any]] | None = None,
    registry_sha256: str | None = None,
    registry_artifact_root: Path | None = None,
    outcome_overrides: dict[str, str] | None = None,
    selected_case_ids: set[str] | None = None,
    root: Path = ROOT,
) -> tuple[dict[str, Any], dict[str, dict[str, Any]]]:
    run_context = document.get("runContext")
    if not isinstance(run_context, dict):
        raise ValueError(f"{target} live runContext must be an object")
    run_platform = require_nonempty_string(
        run_context.get("platform"),
        f"{target} live runContext platform",
    )
    applicable_cases = [
        case
        for case in manifest["cases"]
        if run_platform in case["platforms"]
    ]
    if selected_case_ids is not None:
        applicable_ids = {case["id"] for case in applicable_cases}
        missing = sorted(
            selected_case_ids - applicable_ids,
            key=utf16_ordinal_key,
        )
        if missing:
            raise ValueError(
                f"{target} selected cases are not active on {run_platform}: "
                + ", ".join(missing)
            )
        cases = [
            case
            for case in applicable_cases
            if case["id"] in selected_case_ids
        ]
    else:
        cases = applicable_cases
    expected_case_ids = sorted(case["id"] for case in cases)
    if not expected_case_ids:
        raise ValueError(
            f"live parity results have no active cases for {run_platform}"
        )
    outcomes: dict[str, dict[str, Any]] = {}
    shared_context: dict[str, Any] | None = None
    for case in cases:
        case_id = case["id"]
        fixture_path, fixture, _ = validate_fixture(
            case,
            manifest["reference"]["revision"],
            require_schema_v2=True,
            root=root,
        )
        fixture_hash = sha256_file(fixture_path)
        provenance_document: dict[str, Any] | None = None
        legacy_oracle_entry: dict[str, Any] | None = None
        if target == "legacy":
            if registry_entries is None or registry_sha256 is None:
                raise ValueError(
                    "Legacy live results require a validated runtime "
                    "oracle registry"
                )
            legacy_oracle_entry = (
                select_legacy_oracle_registry_entry(
                    case,
                    registry_entries,
                )
            )
            if (
                legacy_oracle_entry["executableSha256"]
                != fixture["oracleExecutableSha256"]
            ):
                raise ValueError(
                    f"{case_id} runtime oracle executable does not match "
                    "the pinned fixture"
                )
            provenance_path = resolve_registry_artifact(
                legacy_oracle_entry["provenance"],
                f"{case_id} runtime oracle provenance",
                root=root,
                artifact_root=registry_artifact_root,
                require_json=True,
            )
            (
                provenance_document,
                _,
                provenance_hash,
            ) = load_promotion_document(
                provenance_path,
                f"{case_id} runtime oracle provenance",
            )
            if (
                provenance_hash
                != legacy_oracle_entry["provenanceSha256"]
            ):
                raise ValueError(
                    f"{case_id} runtime oracle provenance changed after "
                    "registry validation"
                )
            validate_legacy_provenance(
                provenance_document,
                case_id,
                case,
                fixture,
                manifest["reference"]["revision"],
                expected_selected_case_ids=sorted(
                    candidate["id"]
                    for candidate in cases
                    if candidate["legacyOracle"]["versionId"]
                    == case["legacyOracle"]["versionId"]
                ),
                root=root,
            )
            required_outcome = "passed"
            expected_platform = "windows"
            adapter = case["targetAdapters"][0]
        else:
            required_outcome = (
                outcome_overrides.get(case_id)
                if outcome_overrides is not None
                and case_id in outcome_overrides
                else (
                    "passed"
                    if case["status"] == "both-green"
                    else "functional-divergence"
                )
            )
            expected_platform = None
            adapter = case["targetAdapters"][1]
        result, context = validate_run_document(
            document,
            target,
            case,
            fixture,
            fixture_hash,
            manifest["reference"]["revision"],
            expected_platform=expected_platform,
            expected_adapter=adapter,
            expected_case_ids=expected_case_ids,
            require_outcome=required_outcome,
            expected_legacy_oracle_entry=legacy_oracle_entry,
            registry_sha256=(
                registry_sha256 if target == "legacy" else None
            ),
        )
        if target == "xplat" and context["platform"] not in case["platforms"]:
            raise ValueError(
                f"{case_id} live XPlat result platform is outside the case"
            )
        if (
            target == "legacy"
            and provenance_document is not None
            and context["xplat"] != provenance_document["xplat"]
        ):
            raise ValueError(
                f"{case_id} live Legacy XPlat context does not match "
                "provenance"
            )
        if shared_context is None:
            shared_context = context
        elif shared_context != context:
            raise ValueError(
                f"{target} run context changed across active cases"
            )
        outcomes[case_id] = result
    assert shared_context is not None
    return shared_context, outcomes


def validate_live_results(
    manifest: dict[str, Any],
    legacy_results_path: Path | None,
    xplat_results_path: Path | None,
    registry_path: Path | None,
    registry_sha256: str | None,
    legacy_test_report_path: Path | None,
    xplat_test_report_path: Path | None,
    mode: str,
    *,
    legacy_execution_path: Path | None = None,
    xplat_execution_path: Path | None = None,
    outcome_overrides: dict[str, str] | None = None,
    selected_case_ids: set[str] | None = None,
    registry_artifact_root: Path | None = None,
    indexed_artifact_sha256: dict[Path, str] | None = None,
    root: Path = ROOT,
) -> dict[str, Any]:
    if selected_case_ids is not None:
        if mode != "Baseline":
            raise ValueError(
                "selected-case live verification is allowed only in "
                "Baseline mode"
            )
        if not selected_case_ids:
            raise ValueError(
                "selected-case live verification requires case IDs"
            )
        if outcome_overrides is None or set(outcome_overrides) != (
            selected_case_ids
        ):
            raise ValueError(
                "selected-case live verification requires an exact "
                "outcome override for every selected case"
            )
    if legacy_results_path is None and xplat_results_path is None:
        raise ValueError(
            "live result verification requires at least one result file"
        )
    if mode == "Release" and (
        legacy_results_path is None or xplat_results_path is None
    ):
        raise ValueError(
            "Release mode requires both Legacy and XPlat live result files"
        )
    summary: dict[str, Any] = {
        "legacy": None,
        "xplat": None,
    }
    recorded_contexts: list[tuple[str, dict[str, Any]]] = []
    legacy_context: dict[str, Any] | None = None
    if legacy_results_path is not None:
        if registry_path is None or registry_sha256 is None:
            raise ValueError(
                "Legacy live results require --legacy-oracle-registry "
                "and --legacy-oracle-registry-sha256"
            )
        if legacy_test_report_path is None:
            raise ValueError(
                "Legacy live results require --legacy-test-report"
            )
        if legacy_execution_path is None:
            raise ValueError(
                "Legacy live results require --legacy-execution"
            )
        legacy_path = resolve_artifact_file(
            legacy_results_path,
            "Legacy live results",
            root=root,
        )
        (
            resolved_registry_path,
            actual_registry_hash,
            registry_entries,
        ) = validate_legacy_oracle_registry(
            registry_path,
            registry_sha256,
            root=root,
            artifact_root=registry_artifact_root,
        )
        require_indexed_artifact_bytes(
            resolved_registry_path,
            resolved_registry_path.read_bytes(),
            indexed_artifact_sha256,
            "Legacy oracle registry",
        )
        legacy_document, legacy_raw, legacy_result_hash = (
            load_promotion_document(
            legacy_path,
            "Legacy live results",
            )
        )
        require_indexed_artifact_bytes(
            legacy_path,
            legacy_raw,
            indexed_artifact_sha256,
            "Legacy live result",
        )
        (
            legacy_report,
            legacy_report_raw,
            legacy_report_hash,
        ) = validate_test_report(
            legacy_test_report_path,
            legacy_document,
            "legacy",
            root=root,
        )
        require_indexed_artifact_bytes(
            legacy_report,
            legacy_report_raw,
            indexed_artifact_sha256,
            "Legacy acceptance test report",
        )
        (
            legacy_execution,
            legacy_execution_raw,
            legacy_execution_hash,
        ) = (
            validate_execution_envelope(
                legacy_execution_path,
                legacy_document,
                legacy_result_hash,
                legacy_report_hash,
                "legacy",
                root=root,
            )
        )
        require_indexed_artifact_bytes(
            legacy_execution,
            legacy_execution_raw,
            indexed_artifact_sha256,
            "Legacy execution envelope",
        )
        legacy_context, legacy_outcomes = (
            validate_live_run_document_for_manifest(
                manifest,
                legacy_document,
                "legacy",
                registry_entries=registry_entries,
                registry_sha256=actual_registry_hash,
                registry_artifact_root=registry_artifact_root,
                selected_case_ids=selected_case_ids,
                root=root,
            )
        )
        summary["legacy"] = {
            "path": str(legacy_path),
            "count": len(legacy_outcomes),
            "platform": legacy_context["platform"],
            "testReport": str(legacy_report),
            "testReportSha256": legacy_report_hash,
            "execution": str(legacy_execution),
            "executionSha256": legacy_execution_hash,
        }
        recorded_contexts.append(("Legacy", legacy_context["xplat"]))
    if xplat_results_path is not None:
        if xplat_test_report_path is None:
            raise ValueError(
                "XPlat live results require --xplat-test-report"
            )
        if xplat_execution_path is None:
            raise ValueError(
                "XPlat live results require --xplat-execution"
            )
        xplat_path = resolve_artifact_file(
            xplat_results_path,
            "XPlat live results",
            root=root,
        )
        xplat_document, xplat_raw, xplat_result_hash = (
            load_promotion_document(
                xplat_path,
                "XPlat live results",
            )
        )
        require_indexed_artifact_bytes(
            xplat_path,
            xplat_raw,
            indexed_artifact_sha256,
            "XPlat live result",
        )
        (
            xplat_report,
            xplat_report_raw,
            xplat_report_hash,
        ) = validate_test_report(
            xplat_test_report_path,
            xplat_document,
            "xplat",
            root=root,
        )
        require_indexed_artifact_bytes(
            xplat_report,
            xplat_report_raw,
            indexed_artifact_sha256,
            "XPlat acceptance test report",
        )
        (
            xplat_execution,
            xplat_execution_raw,
            xplat_execution_hash,
        ) = (
            validate_execution_envelope(
                xplat_execution_path,
                xplat_document,
                xplat_result_hash,
                xplat_report_hash,
                "xplat",
                root=root,
            )
        )
        require_indexed_artifact_bytes(
            xplat_execution,
            xplat_execution_raw,
            indexed_artifact_sha256,
            "XPlat execution envelope",
        )
        xplat_context, xplat_outcomes = (
            validate_live_run_document_for_manifest(
                manifest,
                xplat_document,
                "xplat",
                outcome_overrides=outcome_overrides,
                selected_case_ids=selected_case_ids,
                root=root,
            )
        )
        if (
            legacy_context is not None
            and legacy_context["xplat"] != xplat_context["xplat"]
        ):
            raise ValueError(
                "Legacy and XPlat live results used different XPlat "
                "revisions"
            )
        summary["xplat"] = {
            "path": str(xplat_path),
            "count": len(xplat_outcomes),
            "platform": xplat_context["platform"],
            "testReport": str(xplat_report),
            "testReportSha256": xplat_report_hash,
            "execution": str(xplat_execution),
            "executionSha256": xplat_execution_hash,
        }
        recorded_contexts.append(("XPlat", xplat_context["xplat"]))
    current_xplat = inspect_current_xplat_repository(root=root)
    if current_xplat["clean"] is not True:
        raise ValueError(
            "live XPlat repository changed during or after the parity run"
        )
    for target, recorded_xplat in recorded_contexts:
        if recorded_xplat != current_xplat:
            raise ValueError(
                f"{target} live results do not match the freshly "
                "inspected XPlat repository"
            )
    return summary


def load_existing_red_evidence_for_promotion(
    case: dict[str, Any],
    fixture_path: Path,
    fixture: dict[str, Any],
    fixture_count: int,
    reference_revision: str,
    *,
    root: Path = ROOT,
) -> tuple[Path, dict[str, Any]]:
    evidence_path = (root / case["evidence"]).resolve()
    if not evidence_path.is_file():
        raise ValueError(
            f"{case['id']} has no red evidence to promote"
        )
    evidence = load_json(evidence_path)
    red_case = dict(case)
    red_case.update(
        {
            "legacyTestStatus": "pass",
            "xplatTestStatus": "fail",
            "status": "legacy-green-xplat-red",
            "failureCode": case["assertions"][
                "functionalDivergenceCode"
            ],
            "firstGreenCommit": None,
        }
    )
    validate_active_run_evidence(
        red_case,
        evidence,
        evidence_path,
        fixture_path,
        fixture,
        fixture_count,
        reference_revision,
        root=root,
    )
    return evidence_path, evidence


def find_run_result(
    document: dict[str, Any],
    case_id: str,
    target: str,
) -> dict[str, Any]:
    results = document.get("results")
    if not isinstance(results, list):
        raise ValueError(f"{target} run results must be an array")
    matching = [
        result
        for result in results
        if isinstance(result, dict)
        and result.get("parityId") == case_id
    ]
    if len(matching) != 1:
        raise ValueError(
            f"{case_id} has no unique {target} result in the batch"
        )
    return matching[0]


def derive_manifest_acceptance_statuses(
    manifest: dict[str, Any],
) -> None:
    cases_by_id = {
        case["id"]: case for case in manifest["cases"]
    }
    obligations_by_capability: dict[
        str,
        list[dict[str, Any]],
    ] = defaultdict(list)
    for obligation in manifest["behavioralObligations"]:
        case_ids = obligation["caseIds"]
        if not case_ids:
            status = "not-authored"
        else:
            fully_green = all(
                cases_by_id[case_id]["status"] == "both-green"
                for case_id in case_ids
            )
            status = (
                "complete"
                if (
                    fully_green
                    and obligation["id"]
                    not in RICH_EVIDENCE_REQUIRED_OBLIGATIONS
                )
                else "partial"
            )
        obligation["acceptanceStatus"] = status
        obligations_by_capability[
            obligation["capabilityId"]
        ].append(obligation)
    for capability in manifest["items"]:
        case_ids = capability["caseIds"]
        if not case_ids:
            capability["acceptanceStatus"] = "not-authored"
            continue
        all_green = all(
            cases_by_id[case_id]["status"] == "both-green"
            for case_id in case_ids
        )
        obligations_complete = all(
            obligation["acceptanceStatus"] == "complete"
            for obligation in obligations_by_capability[capability["id"]]
        )
        capability["acceptanceStatus"] = (
            "complete"
            if all_green and obligations_complete
            else "partial"
        )


def add_content_transaction_change(
    changes: dict[Path, tuple[bytes, bool]],
    raw: bytes,
    relative_directory: str,
    *,
    suffix: str,
    root: Path,
) -> dict[str, str]:
    reference, path = prepare_content_addressed_file(
        raw,
        relative_directory,
        suffix=suffix,
        root=root,
    )
    add_transaction_change(
        changes,
        path,
        raw,
        allow_replace=False,
    )
    return reference


def validate_candidate_history_against_clean_head(
    manifest: dict[str, Any],
    retained_evidence: list[dict[str, Any]],
    *,
    candidate_root: Path,
    repository_root: Path,
) -> None:
    head_result = run_git(
        repository_root,
        ["rev-parse", "--verify", "HEAD^{commit}"],
        "promotion candidate history",
    )
    head = head_result.stdout.strip()
    if (
        head_result.returncode != 0
        or not COMMIT_PATTERN.fullmatch(head)
    ):
        raise ValueError(
            "promotion candidate history could not resolve clean HEAD"
        )
    manifest_blob = run_git_bytes(
        repository_root,
        [
            "cat-file",
            "blob",
            f"{head}:tests/parity/parity-manifest.json",
        ],
        "promotion candidate history",
    )
    if manifest_blob.returncode != 0:
        raise ValueError(
            "clean HEAD has no parity manifest for candidate history"
        )
    try:
        base_text = manifest_blob.stdout.decode("utf-8")
    except UnicodeDecodeError as error:
        raise ValueError(
            "clean HEAD parity manifest is not UTF-8"
        ) from error
    base_manifest = parse_json_value(
        base_text,
        "clean HEAD parity manifest",
    )
    validate_manifest_history(
        manifest,
        base_manifest,
        root=candidate_root,
        base_root=repository_root,
        base_commit=head,
        base_manifest_sha256=hashlib.sha256(
            manifest_blob.stdout
        ).hexdigest(),
        retained_evidence=retained_evidence,
    )


def validate_promotion_candidate(
    manifest: dict[str, Any],
    changes: dict[Path, tuple[bytes, bool]],
    promotion_kind: str,
    *,
    root: Path,
) -> bytes | None:
    required_manifest_fields = {
        "schemaVersion",
        "canonicalJson",
        "inventoryStatus",
        "reference",
        "legacySurfaceInventory",
        "auditedSurfaces",
        "pendingAuditSurfaces",
        "items",
        "behavioralObligations",
        "cases",
    }
    if not required_manifest_fields.issubset(manifest):
        return None
    repository_root = root.resolve()
    artifacts_root = repository_root / "artifacts"
    artifacts_root.mkdir(parents=True, exist_ok=True)

    def ignore_candidate_sources(
        directory: str,
        names: list[str],
    ) -> set[str]:
        ignored = {
            name
            for name in names
            if name in {
                ".git",
                ".venv",
                "artifacts",
                "__pycache__",
                ".pytest_cache",
                ".ruff_cache",
                "bin",
                "obj",
            }
        }
        return ignored

    with tempfile.TemporaryDirectory(
        prefix="parity-candidate.",
        dir=artifacts_root,
    ) as temporary_directory:
        candidate_root = Path(temporary_directory) / "repository"
        shutil.copytree(
            repository_root,
            candidate_root,
            ignore=ignore_candidate_sources,
        )
        for path, (raw, _) in changes.items():
            try:
                relative = path.resolve().relative_to(repository_root)
            except ValueError as error:
                raise ValueError(
                    f"promotion transaction target escapes repository: "
                    f"{path}"
                ) from error
            candidate_path = candidate_root / relative
            candidate_path.parent.mkdir(parents=True, exist_ok=True)
            candidate_path.write_bytes(raw)
        candidate_manifest_path = (
            candidate_root / "tests/parity/parity-manifest.json"
        )
        if not candidate_manifest_path.is_file():
            raise ValueError(
                "promotion candidate has no parity manifest"
            )
        candidate_manifest = load_json(candidate_manifest_path)
        if candidate_manifest != manifest:
            raise ValueError(
                "promotion candidate manifest bytes do not match "
                "the prepared manifest"
            )
        inventory, mappings, retained_evidence = validate_manifest(
            candidate_manifest,
            None,
            root=candidate_root,
        )
        rendered_report = render_report(
            candidate_manifest,
            inventory,
            mappings,
            retained_evidence,
        )
        report_raw = rendered_report.encode("utf-8")
        report_path = (
            candidate_root / "tests/parity/PARITY_REPORT.md"
        )
        report_path.parent.mkdir(parents=True, exist_ok=True)
        report_path.write_bytes(report_raw)
        if report_path.read_bytes() != report_raw:
            raise ValueError(
                "promotion candidate report bytes changed during validation"
            )
        if promotion_kind == "green":
            validate_candidate_history_against_clean_head(
                candidate_manifest,
                retained_evidence,
                candidate_root=candidate_root,
                repository_root=repository_root,
            )
        return report_raw


def validate_prepared_evidence_document(
    evidence: dict[str, Any],
    case: dict[str, Any],
) -> None:
    expected_fields = {
        "schemaVersion",
        "parityId",
        "referenceRevision",
        "capturedAtUtc",
        "fixture",
        "legacyOracle",
        "runs",
        "testReports",
        "executions",
        "regressionGate",
        "classification",
    }
    require_fields(
        evidence,
        expected_fields,
        f"{case['id']} prepared evidence",
        allowed=expected_fields,
    )
    if evidence["schemaVersion"] != EVIDENCE_SCHEMA_VERSION:
        raise ValueError(
            f"{case['id']} prepared evidence schema is invalid"
        )
    if evidence["parityId"] != case["id"]:
        raise ValueError(
            f"{case['id']} prepared evidence identity is invalid"
        )
    validate_timestamp(
        evidence["capturedAtUtc"],
        f"{case['id']} prepared evidence capturedAtUtc",
    )
    for field in ("runs", "testReports", "executions"):
        value = evidence[field]
        if not isinstance(value, dict) or set(value) != {
            "legacy",
            "xplatRed",
            "xplatGreen",
        }:
            raise ValueError(
                f"{case['id']} prepared evidence {field} schema is invalid"
            )
    regression_gate = evidence["regressionGate"]
    if case["status"] == "legacy-green-xplat-red":
        if regression_gate is not None:
            raise ValueError(
                f"{case['id']} red prepared evidence must not claim a "
                "regression gate"
            )
    elif not isinstance(regression_gate, dict):
        raise ValueError(
            f"{case['id']} green prepared evidence requires a "
            "regression-gate reference"
        )


def required_full_suite_gate_keys(
    selected_cases: list[dict[str, Any]],
) -> set[str]:
    if not selected_cases:
        raise ValueError(
            "full-suite regression gate requires selected cases"
        )
    return {
        "windows/Legacy",
        "windows/XPlat",
        "linux/XPlat",
        "macos/XPlat",
    }


def build_regression_manifest_case_snapshot(
    cases: list[dict[str, Any]],
    promoted_case_ids: set[str],
) -> list[dict[str, Any]]:
    snapshot: list[dict[str, Any]] = []
    for case in sorted(
        cases,
        key=lambda entry: utf16_ordinal_key(entry["id"]),
    ):
        passed = (
            case["id"] in promoted_case_ids
            or case["status"] == "both-green"
        )
        snapshot.append(
            {
                "id": case["id"],
                "platforms": copy.deepcopy(case["platforms"]),
                "caseDefinitionSha256": (
                    case_definition_sha256(case)
                ),
                "expectedXPlatOutcome": (
                    "passed" if passed else "functional-divergence"
                ),
                "failureCode": (
                    None
                    if passed
                    else case["assertions"][
                        "functionalDivergenceCode"
                    ]
                ),
            }
        )
    return snapshot


def validate_exact_full_suite_artifact_maps(
    expected_keys: set[str],
    results: dict[str, Path],
    test_reports: dict[str, Path],
    executions: dict[str, Path],
) -> None:
    for label, artifact_map in (
        ("result", results),
        ("TRX", test_reports),
        ("execution", executions),
    ):
        if set(artifact_map) != expected_keys:
            missing = sorted(expected_keys - set(artifact_map))
            unknown = sorted(set(artifact_map) - expected_keys)
            details: list[str] = []
            if missing:
                details.append("missing " + ", ".join(missing))
            if unknown:
                details.append("unknown " + ", ".join(unknown))
            raise ValueError(
                f"green full-suite {label} keys must exactly match the "
                "required platform/target set"
                + (": " + "; ".join(details) if details else "")
            )


def promote_evidence_batch(
    manifest: dict[str, Any],
    promotion_kind: str,
    case_ids: list[str],
    **kwargs: Any,
) -> list[Path]:
    root = kwargs.get("root", ROOT)
    if not isinstance(root, Path):
        raise ValueError("promotion root must be a Path")
    with repository_promotion_lock(root):
        manifest_path = (
            root / "tests/parity/parity-manifest.json"
        ).resolve()
        if manifest_path.is_file():
            current_manifest = load_json(manifest_path)
            if current_manifest != manifest:
                raise ValueError(
                    "parity manifest changed while this promotion waited "
                    "for the repository lock; reload and retry"
                )
        return _promote_evidence_batch_locked(
            manifest,
            promotion_kind,
            case_ids,
            **kwargs,
        )


def _promote_evidence_batch_locked(
    manifest: dict[str, Any],
    promotion_kind: str,
    case_ids: list[str],
    *,
    legacy_run_path: Path | None = None,
    xplat_run_path: Path | None = None,
    registry_path: Path | None = None,
    registry_sha256: str | None = None,
    legacy_test_report_path: Path | None = None,
    xplat_test_report_path: Path | None = None,
    legacy_execution_path: Path | None = None,
    xplat_execution_path: Path | None = None,
    green_results: dict[str, Path] | None = None,
    green_test_reports: dict[str, Path] | None = None,
    green_executions: dict[str, Path] | None = None,
    full_suite_results: dict[str, Path] | None = None,
    full_suite_test_reports: dict[str, Path] | None = None,
    full_suite_executions: dict[str, Path] | None = None,
    full_suite_package_index: Path | None = None,
    full_suite_package_index_sha256: str | None = None,
    root: Path = ROOT,
) -> list[Path]:
    if promotion_kind not in {"red", "green"}:
        raise ValueError(f"unsupported evidence promotion: {promotion_kind}")
    if (
        not case_ids
        or len(case_ids) != len(set(case_ids))
        or case_ids != sorted(case_ids, key=utf16_ordinal_key)
    ):
        raise ValueError(
            "promotion case IDs must be non-empty, unique, and "
            "ordinally sorted"
        )
    selected = [
        select_manifest_case(manifest, case_id)
        for case_id in case_ids
    ]
    if any(
        case["status"] != "legacy-green-xplat-red"
        for case in selected
    ):
        raise ValueError(
            f"{promotion_kind} promotion requires red manifest cases"
        )
    current_xplat = inspect_current_xplat_repository(root=root)
    if current_xplat["clean"] is not True:
        raise ValueError(
            "evidence promotion requires a clean XPlat repository"
        )
    working_manifest = copy.deepcopy(manifest)
    working_cases = {
        case["id"]: case for case in working_manifest["cases"]
    }
    reference_revision = manifest["reference"]["revision"]
    changes: dict[Path, tuple[bytes, bool]] = {}
    evidence_paths: list[Path] = []
    timestamp = datetime.now(timezone.utc).strftime(
        "%Y-%m-%dT%H:%M:%SZ"
    )

    if promotion_kind == "red":
        required = (
            legacy_run_path,
            xplat_run_path,
            registry_path,
            registry_sha256,
            legacy_test_report_path,
            xplat_test_report_path,
            legacy_execution_path,
            xplat_execution_path,
        )
        if any(value is None for value in required):
            raise ValueError(
                "red batch promotion requires Legacy and XPlat results, "
                "TRX reports, execution envelopes, and the hash-pinned "
                "runtime oracle registry"
            )
        overrides = {
            case_id: "functional-divergence"
            for case_id in case_ids
        }
        validate_live_results(
            manifest,
            legacy_run_path,
            xplat_run_path,
            registry_path,
            registry_sha256,
            legacy_test_report_path,
            xplat_test_report_path,
            "Baseline",
            legacy_execution_path=legacy_execution_path,
            xplat_execution_path=xplat_execution_path,
            outcome_overrides=overrides,
            selected_case_ids=set(case_ids),
            root=root,
        )
        (
            _,
            actual_registry_hash,
            entries,
        ) = validate_legacy_oracle_registry(
            registry_path,
            registry_sha256,
            root=root,
        )
        legacy_source = resolve_artifact_file(
            legacy_run_path,
            "Legacy promotion run",
            root=root,
        )
        legacy_document, legacy_raw, legacy_result_hash = (
            load_promotion_document(
            legacy_source,
            "Legacy promotion run",
            )
        )
        xplat_source = resolve_artifact_file(
            xplat_run_path,
            "XPlat red promotion run",
            root=root,
        )
        xplat_document, xplat_raw, red_result_hash = (
            load_promotion_document(
            xplat_source,
            "XPlat red promotion run",
            )
        )
        _, legacy_report_raw, legacy_report_hash = validate_test_report(
            legacy_test_report_path,
            legacy_document,
            "legacy",
            root=root,
        )
        _, red_report_raw, red_report_hash = validate_test_report(
            xplat_test_report_path,
            xplat_document,
            "xplat",
            root=root,
        )
        _, legacy_execution_raw, _ = validate_execution_envelope(
            legacy_execution_path,
            legacy_document,
            legacy_result_hash,
            legacy_report_hash,
            "legacy",
            root=root,
        )
        _, red_execution_raw, _ = validate_execution_envelope(
            xplat_execution_path,
            xplat_document,
            red_result_hash,
            red_report_hash,
            "xplat",
            root=root,
        )
        legacy_run_reference = add_content_transaction_change(
            changes,
            legacy_raw,
            "tests/parity/evidence/runs",
            suffix=".json",
            root=root,
        )
        red_run_reference = add_content_transaction_change(
            changes,
            xplat_raw,
            "tests/parity/evidence/runs",
            suffix=".json",
            root=root,
        )
        legacy_report_reference = add_content_transaction_change(
            changes,
            legacy_report_raw,
            "tests/parity/evidence/test-reports",
            suffix=".trx",
            root=root,
        )
        red_report_reference = add_content_transaction_change(
            changes,
            red_report_raw,
            "tests/parity/evidence/test-reports",
            suffix=".trx",
            root=root,
        )
        legacy_execution_reference = add_content_transaction_change(
            changes,
            legacy_execution_raw,
            "tests/parity/evidence/executions",
            suffix=".json",
            root=root,
        )
        red_execution_reference = add_content_transaction_change(
            changes,
            red_execution_raw,
            "tests/parity/evidence/executions",
            suffix=".json",
            root=root,
        )
        for case in selected:
            case_id = case["id"]
            fixture_path, fixture, _ = validate_fixture(
                case,
                reference_revision,
                require_schema_v2=True,
                root=root,
            )
            fixture_hash = sha256_file(fixture_path)
            entry = select_legacy_oracle_registry_entry(
                case,
                entries,
            )
            provenance_path = resolve_registry_artifact(
                entry["provenance"],
                f"{case_id} promotion provenance",
                root=root,
                require_json=True,
            )
            (
                provenance_document,
                provenance_raw,
                provenance_hash,
            ) = load_promotion_document(
                provenance_path,
                f"{case_id} promotion provenance",
            )
            validate_legacy_provenance(
                provenance_document,
                case_id,
                case,
                fixture,
                reference_revision,
                root=root,
            )
            provenance_reference = add_content_transaction_change(
                changes,
                provenance_raw,
                "tests/parity/evidence/provenance",
                suffix=".json",
                root=root,
            )
            legacy_result = find_run_result(
                legacy_document,
                case_id,
                "legacy",
            )
            red_result = find_run_result(
                xplat_document,
                case_id,
                "xplat",
            )
            if legacy_result["observedValues"] != fixture["values"]:
                raise ValueError(
                    f"{case_id} Legacy promotion values do not match "
                    "the fixture"
                )
            if (
                red_result["failureCode"]
                != case["assertions"]["functionalDivergenceCode"]
            ):
                raise ValueError(
                    f"{case_id} red promotion has an unregistered "
                    "functional divergence"
                )
            evidence_path = (root / case["evidence"]).resolve()
            if evidence_path.exists():
                raise ValueError(
                    f"{case_id} red evidence already exists"
                )
            evidence = {
                "schemaVersion": EVIDENCE_SCHEMA_VERSION,
                "parityId": case_id,
                "referenceRevision": reference_revision,
                "capturedAtUtc": timestamp,
                "fixture": {
                    "path": case["fixture"],
                    "sha256": fixture_hash,
                    "observedValuesSha256": canonical_json_sha256(
                        fixture["values"]
                    ),
                },
                "legacyOracle": {
                    **copy.deepcopy(case["legacyOracle"]),
                    "executableSha256": entry[
                        "executableSha256"
                    ],
                    "provenance": entry["provenance"],
                    "provenanceSha256": provenance_hash,
                    "registrySha256": actual_registry_hash,
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
            validate_prepared_evidence_document(evidence, case)
            add_transaction_change(
                changes,
                evidence_path,
                serialize_lf_json(evidence),
                allow_replace=False,
            )
            evidence_paths.append(evidence_path)
    else:
        result_map = green_results or {}
        report_map = green_test_reports or {}
        execution_map = green_executions or {}
        full_result_map = full_suite_results or {}
        full_report_map = full_suite_test_reports or {}
        full_execution_map = full_suite_executions or {}
        required_platforms = sorted(
            {
                platform
                for case in selected
                for platform in case["platforms"]
            }
        )
        required_gate_keys = required_full_suite_gate_keys(selected)
        validate_exact_full_suite_artifact_maps(
            required_gate_keys,
            full_result_map,
            full_report_map,
            full_execution_map,
        )
        if registry_path is None or registry_sha256 is None:
            raise ValueError(
                "green promotion requires the full-suite Legacy oracle "
                "registry and SHA-256"
            )
        if (
            full_suite_package_index is None
            or full_suite_package_index_sha256 is None
        ):
            raise ValueError(
                "green promotion requires the content-addressed "
                "full-suite package index and SHA-256"
            )
        (
            full_suite_package_root,
            validated_package_index_sha256,
            _,
            build_integration_report_path,
            build_integration_execution_path,
            indexed_package_artifact_sha256,
        ) = validate_full_suite_package_index(
            full_suite_package_index,
            full_suite_package_index_sha256,
            registry_path,
            registry_sha256,
            full_result_map,
            full_report_map,
            full_execution_map,
            root=root,
        )
        if sorted(result_map) != required_platforms:
            raise ValueError(
                "green batch result platforms must exactly match: "
                + ", ".join(required_platforms)
            )
        if sorted(report_map) != required_platforms:
            raise ValueError(
                "green batch TRX platforms must exactly match: "
                + ", ".join(required_platforms)
            )
        if sorted(execution_map) != required_platforms:
            raise ValueError(
                "green batch execution platforms must exactly match: "
                + ", ".join(required_platforms)
            )
        for platform in required_platforms:
            full_key = f"{platform}/XPlat"
            for label, selected_path, full_path in (
                (
                    "result",
                    result_map[platform],
                    full_result_map[full_key],
                ),
                (
                    "TRX",
                    report_map[platform],
                    full_report_map[full_key],
                ),
                (
                    "execution",
                    execution_map[platform],
                    full_execution_map[full_key],
                ),
            ):
                selected_artifact = resolve_artifact_path(
                    selected_path,
                    f"{platform} selected green {label}",
                    root=root,
                )
                full_artifact = resolve_artifact_path(
                    full_path,
                    f"{platform} full-suite {label}",
                    root=root,
                )
                if selected_artifact == full_artifact:
                    raise ValueError(
                        f"{platform} selected Baseline {label} must be a "
                        "distinct artifact from its full-suite regression "
                        "gate"
                    )
                if (
                    label != "result"
                    and sha256_file(selected_artifact)
                    == sha256_file(full_artifact)
                ):
                    raise ValueError(
                        f"{platform} selected Baseline {label} must come "
                        "from a distinct capture, not copied full-suite "
                        "bytes"
                    )
        full_documents: dict[str, dict[str, Any]] = {}
        full_raw: dict[str, bytes] = {}
        full_report_raw: dict[str, bytes] = {}
        full_execution_raw: dict[str, bytes] = {}
        full_xplat_context: dict[str, Any] | None = None
        for platform in sorted(
            {
                key.split("/", 1)[0]
                for key in required_gate_keys
                if key.endswith("/XPlat")
            }
        ):
            xplat_key = f"{platform}/XPlat"
            platform_overrides = {
                case["id"]: "passed"
                for case in selected
                if platform in case["platforms"]
            }
            is_windows = platform == "windows"
            validate_live_results(
                manifest,
                (
                    full_result_map["windows/Legacy"]
                    if is_windows
                    else None
                ),
                full_result_map[xplat_key],
                registry_path if is_windows else None,
                registry_sha256 if is_windows else None,
                (
                    full_report_map["windows/Legacy"]
                    if is_windows
                    else None
                ),
                full_report_map[xplat_key],
                "Development",
                legacy_execution_path=(
                    full_execution_map["windows/Legacy"]
                    if is_windows
                    else None
                ),
                xplat_execution_path=full_execution_map[xplat_key],
                outcome_overrides=platform_overrides,
                registry_artifact_root=(
                    full_suite_package_root
                    if is_windows
                    else None
                ),
                indexed_artifact_sha256=(
                    indexed_package_artifact_sha256
                    if is_windows
                    else None
                ),
                root=root,
            )
            xplat_source = resolve_artifact_file(
                full_result_map[xplat_key],
                f"{xplat_key} full-suite result",
                root=root,
            )
            xplat_document, xplat_raw, xplat_result_hash = (
                load_promotion_document(
                    xplat_source,
                    f"{xplat_key} full-suite result",
                )
            )
            if is_windows:
                require_indexed_artifact_bytes(
                    xplat_source,
                    xplat_raw,
                    indexed_package_artifact_sha256,
                    f"{xplat_key} full-suite result",
                )
            (
                xplat_report_path,
                xplat_trx_raw,
                xplat_trx_hash,
            ) = validate_test_report(
                full_report_map[xplat_key],
                xplat_document,
                "xplat",
                root=root,
            )
            if is_windows:
                require_indexed_artifact_bytes(
                    xplat_report_path,
                    xplat_trx_raw,
                    indexed_package_artifact_sha256,
                    f"{xplat_key} full-suite test report",
                )
            (
                xplat_execution_path,
                xplat_envelope_raw,
                _,
            ) = validate_execution_envelope(
                full_execution_map[xplat_key],
                xplat_document,
                xplat_result_hash,
                xplat_trx_hash,
                "xplat",
                root=root,
            )
            if is_windows:
                require_indexed_artifact_bytes(
                    xplat_execution_path,
                    xplat_envelope_raw,
                    indexed_package_artifact_sha256,
                    f"{xplat_key} full-suite execution envelope",
                )
            current_context = xplat_document["runContext"]["xplat"]
            if full_xplat_context is None:
                full_xplat_context = current_context
            elif current_context != full_xplat_context:
                raise ValueError(
                    "full-suite regression artifacts used different clean "
                    "XPlat revisions or trees"
                )
            full_documents[xplat_key] = xplat_document
            full_raw[xplat_key] = xplat_raw
            full_report_raw[xplat_key] = xplat_trx_raw
            full_execution_raw[xplat_key] = xplat_envelope_raw
        legacy_key = "windows/Legacy"
        legacy_source = resolve_artifact_file(
            full_result_map[legacy_key],
            f"{legacy_key} full-suite result",
            root=root,
        )
        legacy_document, legacy_raw, legacy_result_hash = (
            load_promotion_document(
                legacy_source,
                f"{legacy_key} full-suite result",
            )
        )
        require_indexed_artifact_bytes(
            legacy_source,
            legacy_raw,
            indexed_package_artifact_sha256,
            f"{legacy_key} full-suite result",
        )
        (
            legacy_report_path,
            legacy_trx_raw,
            legacy_trx_hash,
        ) = validate_test_report(
            full_report_map[legacy_key],
            legacy_document,
            "legacy",
            root=root,
        )
        require_indexed_artifact_bytes(
            legacy_report_path,
            legacy_trx_raw,
            indexed_package_artifact_sha256,
            f"{legacy_key} full-suite test report",
        )
        (
            legacy_execution_artifact_path,
            legacy_envelope_raw,
            _,
        ) = validate_execution_envelope(
            full_execution_map[legacy_key],
            legacy_document,
            legacy_result_hash,
            legacy_trx_hash,
            "legacy",
            root=root,
        )
        require_indexed_artifact_bytes(
            legacy_execution_artifact_path,
            legacy_envelope_raw,
            indexed_package_artifact_sha256,
            f"{legacy_key} full-suite execution envelope",
        )
        if (
            full_xplat_context is None
            or legacy_document["runContext"]["xplat"]
            != full_xplat_context
        ):
            raise ValueError(
                "Windows Legacy and native XPlat full-suite artifacts used "
                "different XPlat revisions or trees"
            )
        full_documents[legacy_key] = legacy_document
        full_raw[legacy_key] = legacy_raw
        full_report_raw[legacy_key] = legacy_trx_raw
        full_execution_raw[legacy_key] = legacy_envelope_raw
        platform_documents: dict[str, dict[str, Any]] = {}
        platform_raw: dict[str, bytes] = {}
        report_raw: dict[str, bytes] = {}
        execution_raw: dict[str, bytes] = {}
        shared_xplat: dict[str, Any] | None = None
        for platform in required_platforms:
            platform_case_ids = {
                case["id"]
                for case in selected
                if platform in case["platforms"]
            }
            platform_overrides = {
                case_id: "passed"
                for case_id in platform_case_ids
            }
            validate_live_results(
                manifest,
                None,
                result_map[platform],
                None,
                None,
                None,
                report_map[platform],
                "Baseline",
                xplat_execution_path=execution_map[platform],
                outcome_overrides=platform_overrides,
                selected_case_ids=platform_case_ids,
                root=root,
            )
            source = resolve_artifact_file(
                result_map[platform],
                f"{platform} green promotion run",
                root=root,
            )
            document, raw, result_hash = load_promotion_document(
                source,
                f"{platform} green promotion run",
            )
            context = document["runContext"]
            if context["platform"] != platform:
                raise ValueError(
                    f"{platform} green result claims another platform"
                )
            if shared_xplat is None:
                shared_xplat = context["xplat"]
            elif context["xplat"] != shared_xplat:
                raise ValueError(
                    "all green platform results must use the same clean "
                    "XPlat revision and tree"
                )
            _, trx_raw, trx_hash = validate_test_report(
                report_map[platform],
                document,
                "xplat",
                root=root,
            )
            _, envelope_raw, _ = validate_execution_envelope(
                execution_map[platform],
                document,
                result_hash,
                trx_hash,
                "xplat",
                root=root,
            )
            platform_documents[platform] = document
            platform_raw[platform] = raw
            report_raw[platform] = trx_raw
            execution_raw[platform] = envelope_raw
        assert shared_xplat is not None
        if shared_xplat != full_xplat_context:
            raise ValueError(
                "selected Baseline and full-suite regression artifacts "
                "used different clean XPlat revisions or trees"
            )
        if shared_xplat != current_xplat:
            raise ValueError(
                "green platform batch does not match the freshly "
                "inspected clean XPlat repository"
            )
        first_green_revision = shared_xplat["revision"]
        green_run_references = {
            platform: add_content_transaction_change(
                changes,
                platform_raw[platform],
                "tests/parity/evidence/runs",
                suffix=".json",
                root=root,
            )
            for platform in required_platforms
        }
        green_report_references = {
            platform: add_content_transaction_change(
                changes,
                report_raw[platform],
                "tests/parity/evidence/test-reports",
                suffix=".trx",
                root=root,
            )
            for platform in required_platforms
        }
        green_execution_references = {
            platform: add_content_transaction_change(
                changes,
                execution_raw[platform],
                "tests/parity/evidence/executions",
                suffix=".json",
                root=root,
            )
            for platform in required_platforms
        }
        full_run_references = {
            key: add_content_transaction_change(
                changes,
                full_raw[key],
                "tests/parity/evidence/runs",
                suffix=".json",
                root=root,
            )
            for key in sorted(required_gate_keys, key=utf16_ordinal_key)
        }
        full_report_references = {
            key: add_content_transaction_change(
                changes,
                full_report_raw[key],
                "tests/parity/evidence/test-reports",
                suffix=".trx",
                root=root,
            )
            for key in sorted(required_gate_keys, key=utf16_ordinal_key)
        }
        full_execution_references = {
            key: add_content_transaction_change(
                changes,
                full_execution_raw[key],
                "tests/parity/evidence/executions",
                suffix=".json",
                root=root,
            )
            for key in sorted(required_gate_keys, key=utf16_ordinal_key)
        }
        build_integration_report_raw = (
            build_integration_report_path.read_bytes()
        )
        require_indexed_artifact_bytes(
            build_integration_report_path,
            build_integration_report_raw,
            indexed_package_artifact_sha256,
            "Legacy oracle build integration test report",
        )
        build_integration_execution_raw = (
            build_integration_execution_path.read_bytes()
        )
        require_indexed_artifact_bytes(
            build_integration_execution_path,
            build_integration_execution_raw,
            indexed_package_artifact_sha256,
            "Legacy oracle build integration execution envelope",
        )
        build_integration_report_reference = (
            add_content_transaction_change(
                changes,
                build_integration_report_raw,
                "tests/parity/evidence/test-reports",
                suffix=".trx",
                root=root,
            )
        )
        build_integration_execution_reference = (
            add_content_transaction_change(
                changes,
                build_integration_execution_raw,
                "tests/parity/evidence/executions",
                suffix=".json",
                root=root,
            )
        )
        _, full_registry_hash, _ = validate_legacy_oracle_registry(
            registry_path,
            registry_sha256,
            root=root,
            artifact_root=full_suite_package_root,
        )
        regression_gate = {
            "schemaVersion": 1,
            "selectedCaseIds": case_ids,
            "xplat": copy.deepcopy(full_xplat_context),
            "oracleRegistrySha256": full_registry_hash,
            "fullSuitePackageIndexSha256": (
                validated_package_index_sha256
            ),
            "legacyOracleBuildIntegration": {
                "testReport": build_integration_report_reference,
                "execution": build_integration_execution_reference,
            },
            "manifestCases": build_regression_manifest_case_snapshot(
                manifest["cases"],
                set(case_ids),
            ),
            "caseInventory": {
                key: copy.deepcopy(
                    full_documents[key]["expectedParityIds"]
                )
                for key in sorted(
                    required_gate_keys,
                    key=utf16_ordinal_key,
                )
            },
            "runs": full_run_references,
            "testReports": full_report_references,
            "executions": full_execution_references,
        }
        regression_gate["manifestCasesSha256"] = canonical_json_sha256(
            regression_gate["manifestCases"]
        )
        regression_gate_reference = add_content_transaction_change(
            changes,
            serialize_lf_json(regression_gate),
            "tests/parity/evidence/regression-gates",
            suffix=".json",
            root=root,
        )
        for case in selected:
            case_id = case["id"]
            fixture_path, fixture, fixture_count = validate_fixture(
                case,
                reference_revision,
                require_schema_v2=True,
                root=root,
            )
            evidence_path, evidence = (
                load_existing_red_evidence_for_promotion(
                    case,
                    fixture_path,
                    fixture,
                    fixture_count,
                    reference_revision,
                    root=root,
                )
            )
            green_map = evidence["runs"]["xplatGreen"]
            report_green_map = evidence["testReports"]["xplatGreen"]
            execution_green_map = evidence["executions"]["xplatGreen"]
            if green_map or report_green_map or execution_green_map:
                raise ValueError(
                    f"{case_id} green promotion must start from an "
                    "unmodified retained-red evidence record"
                )
            red_run, _ = load_content_addressed_document(
                evidence["runs"]["xplatRed"],
                "tests/parity/evidence/runs",
                f"{case_id} retained red run",
                root=root,
            )
            red_revision = red_run["runContext"]["xplat"]["revision"]
            validate_strict_revision_ancestry(
                red_revision,
                first_green_revision,
                f"{case_id} atomic red-to-green history",
                root=root,
            )
            for platform in case["platforms"]:
                result = find_run_result(
                    platform_documents[platform],
                    case_id,
                    "xplat",
                )
                if result["observedValues"] != fixture["values"]:
                    raise ValueError(
                        f"{case_id} {platform} green values do not match "
                        "the fixture"
                    )
                green_map[platform] = green_run_references[platform]
                report_green_map[platform] = (
                    green_report_references[platform]
                )
                execution_green_map[platform] = (
                    green_execution_references[platform]
                )
            evidence["capturedAtUtc"] = timestamp
            evidence["classification"] = "both-green"
            evidence["regressionGate"] = regression_gate_reference
            working_case = working_cases[case_id]
            working_case.update(
                {
                    "xplatTestStatus": "pass",
                    "status": "both-green",
                    "failureCode": None,
                    "firstGreenCommit": first_green_revision,
                }
            )
            validate_prepared_evidence_document(
                evidence,
                working_case,
            )
            add_transaction_change(
                changes,
                evidence_path,
                serialize_lf_json(evidence),
                allow_replace=True,
            )
            evidence_paths.append(evidence_path)
        if (
            "behavioralObligations" in working_manifest
            and "items" in working_manifest
        ):
            derive_manifest_acceptance_statuses(working_manifest)
        manifest_path = (
            root / "tests/parity/parity-manifest.json"
        ).resolve()
        add_transaction_change(
            changes,
            manifest_path,
            serialize_lf_json(working_manifest),
            allow_replace=True,
        )

    if promotion_kind == "green":
        (
            revalidated_package_root,
            revalidated_package_index_sha256,
            _,
            _,
            _,
            revalidated_package_artifact_sha256,
        ) = validate_full_suite_package_index(
            full_suite_package_index,
            full_suite_package_index_sha256,
            registry_path,
            registry_sha256,
            full_result_map,
            full_report_map,
            full_execution_map,
            root=root,
        )
        if (
            revalidated_package_root != full_suite_package_root
            or revalidated_package_index_sha256
            != validated_package_index_sha256
            or revalidated_package_artifact_sha256
            != indexed_package_artifact_sha256
        ):
            raise ValueError(
                "full-suite package identity changed during green "
                "promotion"
            )

    report_raw = validate_promotion_candidate(
        working_manifest,
        changes,
        promotion_kind,
        root=root,
    )
    if report_raw is not None:
        report_path = (
            root / "tests/parity/PARITY_REPORT.md"
        ).resolve()
        add_transaction_change(
            changes,
            report_path,
            report_raw,
            allow_replace=True,
        )
    commit_file_transaction(changes)
    manifest.clear()
    manifest.update(working_manifest)
    return evidence_paths




def promote_evidence(
    manifest: dict[str, Any],
    promotion_kind: str,
    case_id: str | list[str],
    legacy_run_path: Path | None = None,
    xplat_run_path: Path | None = None,
    registry_path: Path | None = None,
    *,
    registry_sha256: str | None = None,
    legacy_test_report_path: Path | None = None,
    xplat_test_report_path: Path | None = None,
    legacy_execution_path: Path | None = None,
    xplat_execution_path: Path | None = None,
    green_results: dict[str, Path] | None = None,
    green_test_reports: dict[str, Path] | None = None,
    green_executions: dict[str, Path] | None = None,
    full_suite_results: dict[str, Path] | None = None,
    full_suite_test_reports: dict[str, Path] | None = None,
    full_suite_executions: dict[str, Path] | None = None,
    full_suite_package_index: Path | None = None,
    full_suite_package_index_sha256: str | None = None,
    root: Path = ROOT,
) -> Path | list[Path]:
    case_ids = [case_id] if isinstance(case_id, str) else case_id
    if promotion_kind == "green" and green_results is None:
        if (
            xplat_run_path is None
            or xplat_test_report_path is None
            or xplat_execution_path is None
        ):
            raise ValueError(
                "green promotion requires platform=result and "
                "platform=TRX/execution maps"
            )
        document, _, _ = load_promotion_document(
            resolve_artifact_file(
                xplat_run_path,
                "green promotion run",
                root=root,
            ),
            "green promotion run",
        )
        platform = document["runContext"]["platform"]
        green_results = {platform: xplat_run_path}
        green_test_reports = {
            platform: xplat_test_report_path,
        }
        green_executions = {
            platform: xplat_execution_path,
        }
    promoted = promote_evidence_batch(
        manifest,
        promotion_kind,
        sorted(case_ids, key=utf16_ordinal_key),
        legacy_run_path=legacy_run_path,
        xplat_run_path=xplat_run_path,
        registry_path=registry_path,
        registry_sha256=registry_sha256,
        legacy_test_report_path=legacy_test_report_path,
        xplat_test_report_path=xplat_test_report_path,
        legacy_execution_path=legacy_execution_path,
        xplat_execution_path=xplat_execution_path,
        green_results=green_results,
        green_test_reports=green_test_reports,
        green_executions=green_executions,
        full_suite_results=full_suite_results,
        full_suite_test_reports=full_suite_test_reports,
        full_suite_executions=full_suite_executions,
        full_suite_package_index=full_suite_package_index,
        full_suite_package_index_sha256=(
            full_suite_package_index_sha256
        ),
        root=root,
    )
    return promoted[0] if len(promoted) == 1 else promoted


def render_report(
    manifest: dict[str, Any],
    inventory: dict[str, Any],
    mappings: dict[str, list[str]],
    retained_evidence: list[dict[str, Any]],
) -> str:
    capabilities = manifest["items"]
    obligations = manifest["behavioralObligations"]
    cases = manifest["cases"]
    capability_counts = derive_capability_status_counts(capabilities)
    obligation_counts = derive_obligation_status_counts(obligations)
    binding_counts = Counter(
        obligation["sourceBindingStatus"] for obligation in obligations
    )
    rich_evidence_blockers = derive_rich_evidence_blockers(
        obligations,
        cases,
    )
    case_counts = derive_case_status_counts(cases)
    category_counts = Counter(
        surface["category"] for surface in inventory["surfaces"]
    )
    overlap_counts = derive_case_overlap_counts(manifest, inventory)
    mapped_counts = Counter(
        parity_ids[0] for parity_ids in mappings.values()
    )
    both_green = case_counts["both-green"]
    gaps = case_counts["legacy-green-xplat-red"]

    lines = [
        "# MorseRunnerXPlat parity report",
        "",
        "Generated from validated manifest, fixture, and evidence records. "
        "Do not edit by hand.",
        "",
        "## Evidence policy",
        "",
        f"- Active evidence schema: `{EVIDENCE_SCHEMA_VERSION}`",
        "- Active case totals below are derived from evidence whose fixture "
        "and observed-value hashes were recomputed.",
        "- `observedValuesSha256` hashes a compact UTF-8 JSON string array "
        "with no BOM, whitespace, or trailing newline.",
        "- `firstGreenCommit` is the first XPlat code revision demonstrated "
        "green by the retained run. It must equal the run-context revision, "
        "be reachable from HEAD, and have the retained run-context tree.",
        "- Cases may overlap when independent behavior vectors exercise the "
        "same CE surface. Overlap counts remain visible for review.",
        "- Complete capabilities require both-green coverage for every mapped "
        "legacy surface on every declared capability platform.",
        "- Native GUI, physical-audio, performance, and experienced-user "
        "obligations remain structurally non-completable until typed, "
        "content-addressed artifact and sign-off evidence is implemented.",
        f"- Retained legacy-v1 noncertifying observations: "
        f"{len(retained_evidence)}",
        "- Retained legacy-v1 observations are provenance only. They do not "
        "count toward release parity.",
        "",
        "## Inventory",
        "",
        f"- Inventory status: `{manifest['inventoryStatus']}`",
        f"- Pinned legacy revision: `{manifest['reference']['revision']}`",
        f"- Discovered legacy surfaces: {len(inventory['surfaces'])}",
        f"- Mapped legacy surfaces: {len(mappings)}",
        f"- Unmapped legacy surfaces: {len(inventory['surfaces']) - len(mappings)}",
        f"- Pending audit surfaces: {len(manifest['pendingAuditSurfaces'])}",
        "- Overlapping case surface/platform assignments: "
        f"{sum(overlap_counts.values())}",
        "",
        "| Category | Discovered surfaces |",
        "|---|---:|",
    ]
    for category, count in sorted(category_counts.items()):
        lines.append(f"| `{category}` | {count} |")

    lines.extend(
        [
            "",
            "## Acceptance progress",
            "",
            f"- Manifest capabilities: {len(capabilities)}",
            f"- Complete capabilities: {capability_counts['complete']}",
            f"- Partially authored capabilities: "
            f"{capability_counts['partial']}",
            f"- Not-authored capabilities: "
            f"{capability_counts['not-authored']}",
            f"- Behavioral obligations: {len(obligations)}",
            f"- Source-bound obligations: {binding_counts['bound']}",
            f"- Pending source bindings: {binding_counts['pending']}",
            f"- Complete obligations: {obligation_counts['complete']}",
            f"- Partially authored obligations: "
            f"{obligation_counts['partial']}",
            f"- Not-authored obligations: "
            f"{obligation_counts['not-authored']}",
            "- Rich-artifact evidence blockers: "
            f"{len(rich_evidence_blockers)}",
            f"- Active acceptance cases: {len(cases)}",
            f"- Evidence-certified both-green cases: {both_green}",
            f"- Legacy-green/XPlat-red cases: {gaps}",
            "- Skipped, waived, quarantined, disabled, or expected-failure: 0",
            "",
            "| Capability ID | Feature | Acceptance status | Cases | "
            "Mapped surfaces | Overlap assignments | Legacy source |",
            "|---|---|---|---:|---:|---:|---|",
        ]
    )
    for capability in capabilities:
        sources = "<br>".join(
            f"`{source}`" for source in capability["legacySources"]
        )
        lines.append(
            f"| `{capability['id']}` | {capability['feature']} | "
            f"`{capability['acceptanceStatus']}` | "
            f"{len(capability['caseIds'])} | "
            f"{mapped_counts[capability['id']]} | "
            f"{overlap_counts[capability['id']]} | {sources} |"
        )

    lines.extend(
        [
            "",
            "## Behavioral obligations",
            "",
            "| Obligation ID | Capability | Binding | Status | Cases | Platforms | "
            "Required behavior |",
            "|---|---|---|---|---:|---|---|",
        ]
    )
    for obligation in obligations:
        platforms = ", ".join(
            f"`{platform}`" for platform in obligation["platforms"]
        )
        lines.append(
            f"| `{obligation['id']}` | "
            f"`{obligation['capabilityId']}` | "
            f"`{obligation['sourceBindingStatus']}` | "
            f"`{obligation['acceptanceStatus']}` | "
            f"{len(obligation['caseIds'])} | {platforms} | "
            f"{obligation['behavior']} |"
        )

    lines.extend(["", "## Rich-artifact evidence blockers", ""])
    if rich_evidence_blockers:
        for obligation_id in rich_evidence_blockers:
            lines.append(
                f"- `{obligation_id}` has useful green vector evidence, "
                "but remains partial until its typed content-addressed "
                "artifact or human-sign-off contract is implemented."
            )
    else:
        lines.append("- None.")

    lines.extend(["", "## Active acceptance cases", ""])
    if cases:
        lines.extend(
            [
                "| Case ID | Capability | Obligations | Status | "
                "Failure code | Legacy | XPlat |",
                "|---|---|---|---|---|---|---|",
            ]
        )
        for case in cases:
            failure_code = (
                f"`{case['failureCode']}`"
                if case["failureCode"] is not None
                else ""
            )
            obligation_ids = "<br>".join(
                f"`{obligation_id}`"
                for obligation_id in case["obligationIds"]
            )
            lines.append(
                f"| `{case['id']}` | `{case['capabilityId']}` | "
                f"{obligation_ids} | `{case['status']}` | {failure_code} | "
                f"`{case['legacyTestStatus']}` | "
                f"`{case['xplatTestStatus']}` |"
            )
    else:
        lines.append("- None.")

    lines.extend(["", "## Retained noncertifying observations", ""])
    if retained_evidence:
        for evidence in sorted(
            retained_evidence,
            key=lambda entry: entry["parityId"],
        ):
            lines.append(
                f"- `{evidence['parityId']}`: "
                f"{evidence['retention']['reason']}"
            )
    else:
        lines.append("- None.")

    lines.extend(["", "## Pending completeness audits", ""])
    if manifest["pendingAuditSurfaces"]:
        for pending in manifest["pendingAuditSurfaces"]:
            lines.append(f"- {pending}")
    else:
        lines.append("- None.")
    lines.append("")
    return "\n".join(lines)


def validate_report(rendered: str) -> None:
    if not REPORT_PATH.is_file():
        raise ValueError(f"generated parity report not found: {REPORT_PATH}")
    if REPORT_PATH.read_text(encoding="utf-8") != rendered:
        raise ValueError(
            "generated parity report is stale; run validate_parity.py "
            "--mode completeness --write-report"
        )


def report(
    manifest: dict[str, Any],
    inventory: dict[str, Any],
    retained_evidence: list[dict[str, Any]],
    mode: str,
    monotonic_comparison: str | None = None,
) -> int:
    capabilities = manifest["items"]
    obligations = manifest["behavioralObligations"]
    cases = manifest["cases"]
    capability_counts = derive_capability_status_counts(capabilities)
    obligation_counts = derive_obligation_status_counts(obligations)
    binding_counts = Counter(
        obligation["sourceBindingStatus"] for obligation in obligations
    )
    rich_evidence_blockers = derive_rich_evidence_blockers(
        obligations,
        cases,
    )
    case_counts = derive_case_status_counts(cases)
    legacy_passed = sum(
        case["legacyTestStatus"] == "pass" for case in cases
    )
    xplat_passed = sum(
        case["xplatTestStatus"] == "pass" for case in cases
    )
    xplat_failed = sum(
        case["xplatTestStatus"] == "fail" for case in cases
    )
    both_green = case_counts["both-green"]
    gaps = case_counts["legacy-green-xplat-red"]

    print(f"Manifest status:           {manifest['inventoryStatus']}")
    print(f"Manifest capabilities:     {len(capabilities)}")
    print(f"Legacy surfaces:           {len(inventory['surfaces'])}")
    print(
        "Complete capabilities:    "
        f"{capability_counts['complete']}"
    )
    print(
        "Partial capabilities:     "
        f"{capability_counts['partial']}"
    )
    print(
        "Not-authored capabilities:"
        f" {capability_counts['not-authored']}"
    )
    print(f"Behavioral obligations:    {len(obligations)}")
    print(f"Source-bound obligations:  {binding_counts['bound']}")
    print(f"Pending source bindings:   {binding_counts['pending']}")
    print(
        "Complete obligations:     "
        f"{obligation_counts['complete']}"
    )
    print(
        "Partial obligations:      "
        f"{obligation_counts['partial']}"
    )
    print(
        "Not-authored obligations: "
        f"{obligation_counts['not-authored']}"
    )
    print(
        "Rich evidence blockers:   "
        f"{len(rich_evidence_blockers)}"
    )
    for obligation_id in rich_evidence_blockers:
        print(
            "  - "
            f"{obligation_id}: typed content-addressed artifact or "
            "human-sign-off evidence is still required"
        )
    print(f"Active acceptance cases:   {len(cases)}")
    print(f"Legacy evidence green:     {legacy_passed}")
    print(f"XPlat evidence green:      {xplat_passed}")
    print(f"XPlat evidence red:        {xplat_failed}")
    print(f"Evidence both-green:       {both_green}")
    print(f"Functional gaps:           {gaps}")
    print("Skipped/waived cases:      0")
    print("Expected-failure cases:    0")
    print(
        "Retained legacy-v1:       "
        f"{len(retained_evidence)} (noncertifying)"
    )
    print(
        "Pending audit surfaces:   "
        f"{len(manifest['pendingAuditSurfaces'])}"
    )
    if monotonic_comparison is not None:
        print(f"Monotonic comparison:      {monotonic_comparison}")

    release_ready = (
        manifest["inventoryStatus"] == "complete"
        and not manifest["pendingAuditSurfaces"]
        and capability_counts["complete"] == len(capabilities)
        and obligation_counts["complete"] == len(obligations)
        and binding_counts["pending"] == 0
        and gaps == 0
        and both_green == len(cases)
    )
    if mode == "Release" and not release_ready:
        print("Release parity gate: FAILED")
        return 1

    print(f"{mode} parity validation: completed")
    return 0


def load_monotonic_base(
    base_manifest_path: Path | None,
    *,
    root: Path = ROOT,
    checkpoint_kind: str = "merge-base",
    checkpoint_revision: str | None = None,
) -> tuple[
    dict[str, Any] | None,
    str,
    Path | None,
    str | None,
    str | None,
]:
    if base_manifest_path is not None:
        path = base_manifest_path
        if not path.is_absolute():
            path = root / path
        resolved = path.resolve()
        if not resolved.is_file():
            raise ValueError(f"base manifest does not exist: {resolved}")
        if (
            resolved.parent.name == "parity"
            and resolved.parent.parent.name == "tests"
        ):
            base_root = resolved.parents[2]
        else:
            base_root = root
        raw = resolved.read_bytes()
        try:
            text = raw.decode("utf-8")
        except UnicodeDecodeError as error:
            raise ValueError(
                f"base manifest is not UTF-8: {resolved}"
            ) from error
        return (
            parse_json_value(text, str(resolved)),
            str(resolved),
            base_root,
            None,
            hashlib.sha256(raw).hexdigest(),
        )

    history_label = "monotonic parity history"
    if checkpoint_kind in {"pull-request", "main-push"}:
        if (
            checkpoint_revision is None
            or not COMMIT_PATTERN.fullmatch(checkpoint_revision)
            or checkpoint_revision == "0" * 40
        ):
            return (
                None,
                f"{checkpoint_kind} base revision is missing or invalid",
                None,
                None,
                None,
            )
        resolved_base = run_git(
            root,
            [
                "rev-parse",
                "--verify",
                f"{checkpoint_revision}^{{commit}}",
            ],
            history_label,
        )
        if (
            resolved_base.returncode != 0
            or resolved_base.stdout.strip() != checkpoint_revision
        ):
            return (
                None,
                f"{checkpoint_kind} base revision is unavailable",
                None,
                None,
                None,
            )
        if checkpoint_kind == "pull-request":
            checkpoint = run_git(
                root,
                [
                    "merge-base",
                    "--all",
                    "HEAD",
                    checkpoint_revision,
                ],
                history_label,
            )
            commits = [
                line.strip()
                for line in checkpoint.stdout.splitlines()
                if line.strip()
            ]
            if (
                checkpoint.returncode != 0
                or len(commits) != 1
                or not COMMIT_PATTERN.fullmatch(commits[0])
            ):
                return (
                    None,
                    "pull-request history did not resolve one unique "
                    "merge-base",
                    None,
                    None,
                    None,
                )
            commit = commits[0]
            source_name = (
                "pull-request merge-base with base "
                f"{checkpoint_revision}"
            )
        else:
            commit = checkpoint_revision
            source_name = "earlier main-push checkpoint"
    elif checkpoint_kind in {"first-parent", "development"}:
        arguments = (
            ["rev-parse", "--verify", "HEAD^1^{commit}"]
            if checkpoint_kind == "first-parent"
            else ["merge-base", "--all", "HEAD", "origin/main"]
        )
        checkpoint = run_git(root, arguments, history_label)
        commits = [
            line.strip()
            for line in checkpoint.stdout.splitlines()
            if line.strip()
        ]
        if (
            checkpoint.returncode != 0
            or len(commits) != 1
            or not COMMIT_PATTERN.fullmatch(commits[0])
        ):
            unavailable = (
                "HEAD has no first-parent parity checkpoint"
                if checkpoint_kind == "first-parent"
                else "origin/main merge-base is unavailable"
            )
            return None, unavailable, None, None, None
        commit = commits[0]
        source_name = (
            "HEAD first parent"
            if checkpoint_kind == "first-parent"
            else "origin/main merge-base"
        )
    else:
        raise ValueError(
            f"unsupported parity history checkpoint kind: "
            f"{checkpoint_kind}"
        )
    current = run_git(
        root,
        ["rev-parse", "--verify", "HEAD^{commit}"],
        "monotonic parity history",
    )
    if current.returncode != 0 or not COMMIT_PATTERN.fullmatch(
        current.stdout.strip()
    ):
        return (
            None,
            "the current XPlat commit is unavailable",
            None,
            None,
            None,
        )
    current_commit = current.stdout.strip()
    if commit == current_commit:
        return (
            None,
            f"{source_name} resolves to the current commit",
            None,
            None,
            None,
        )
    ancestor = run_git(
        root,
        ["merge-base", "--is-ancestor", commit, current_commit],
        "monotonic parity history",
    )
    if ancestor.returncode != 0:
        return (
            None,
            f"{source_name} is not an ancestor of the current commit",
            None,
            None,
            None,
        )
    if checkpoint_kind == "main-push":
        first_parent_count = run_git(
            root,
            [
                "rev-list",
                "--first-parent",
                "--count",
                f"{commit}..{current_commit}",
            ],
            history_label,
        )
        try:
            first_parent_distance = int(
                first_parent_count.stdout.strip()
            )
        except ValueError:
            first_parent_distance = 0
        if (
            first_parent_count.returncode != 0
            or first_parent_distance <= 0
        ):
            return (
                None,
                f"{source_name} is not a strict first-parent ancestor "
                "of the current commit",
                None,
                None,
                None,
            )
        first_parent_checkpoint = run_git(
            root,
            [
                "rev-parse",
                "--verify",
                f"HEAD~{first_parent_distance}^{{commit}}",
            ],
            history_label,
        )
        if (
            first_parent_checkpoint.returncode != 0
            or first_parent_checkpoint.stdout.strip() != commit
        ):
            return (
                None,
                f"{source_name} is not on the current first-parent chain",
                None,
                None,
                None,
            )
    manifest_blob = run_git_bytes(
        root,
        [
            "cat-file",
            "blob",
            f"{commit}:tests/parity/parity-manifest.json",
        ],
        history_label,
    )
    if manifest_blob.returncode != 0:
        return (
            None,
            f"the {source_name} has no parity manifest",
            None,
            None,
            None,
        )
    try:
        manifest_text = manifest_blob.stdout.decode("utf-8")
    except UnicodeDecodeError as error:
        raise ValueError(
            f"base manifest at {commit} is not UTF-8"
        ) from error
    base_manifest = parse_json_value(
        manifest_text,
        f"base manifest at {commit}",
    )
    if base_manifest.get("schemaVersion") not in {
        1,
        MANIFEST_SCHEMA_VERSION,
    }:
        return (
            None,
            "the origin/main merge-base manifest has an unsupported schema",
            None,
            None,
            None,
        )
    return (
        base_manifest,
        f"{source_name} {commit}",
        root,
        commit,
        hashlib.sha256(manifest_blob.stdout).hexdigest(),
    )


def validate_mode_history(
    manifest: dict[str, Any],
    mode: str,
    base_manifest_path: Path | None,
    *,
    retained_evidence: list[dict[str, Any]] | None = None,
    root: Path = ROOT,
) -> str | None:
    if mode not in {"Development", "PullRequest", "Release"}:
        return None
    required = mode in {"PullRequest", "Release"}
    if required and base_manifest_path is not None:
        raise ValueError(
            f"{mode} mode forbids --base-manifest; certified history must "
            "come from the verified Git graph"
        )
    checkpoint_kind = "development"
    checkpoint_revision: str | None = None
    if mode == "Release":
        checkpoint_kind = "first-parent"
    elif mode == "PullRequest":
        history_kind = os.environ.get(
            HISTORY_KIND_ENVIRONMENT_VARIABLE,
            "pull-request",
        )
        if history_kind == "pull-request":
            checkpoint_revision = os.environ.get(
                HISTORY_BASE_REVISION_ENVIRONMENT_VARIABLE
            )
            checkpoint_kind = "pull-request"
        elif history_kind == "main-push":
            checkpoint_revision = os.environ.get(
                HISTORY_BASE_REVISION_ENVIRONMENT_VARIABLE
            )
            checkpoint_kind = "main-push"
        elif history_kind == "manual-main":
            checkpoint_kind = "first-parent"
        else:
            raise ValueError(
                "PullRequest mode has unsupported parity history kind: "
                f"{history_kind}"
            )
    (
        base_manifest,
        base_source,
        base_root,
        base_commit,
        base_manifest_sha256,
    ) = load_monotonic_base(
        base_manifest_path,
        root=root,
        checkpoint_kind=checkpoint_kind,
        checkpoint_revision=checkpoint_revision,
    )
    if base_manifest is None:
        if required:
            raise ValueError(
                f"{mode} mode requires a trusted earlier parity "
                "base manifest: "
                f"{base_source}"
            )
        return f"not run ({base_source})"
    validate_manifest_history(
        manifest,
        base_manifest,
        root=root,
        base_root=base_root,
        base_commit=base_commit,
        base_manifest_sha256=base_manifest_sha256,
        retained_evidence=retained_evidence,
    )
    return f"passed against {base_source}"


def parse_platform_path_arguments(
    values: list[str] | None,
    label: str,
) -> dict[str, Path]:
    parsed: dict[str, Path] = {}
    for value in values or []:
        if "=" not in value:
            raise ValueError(
                f"{label} must use platform=path syntax: {value}"
            )
        platform, path_text = value.split("=", 1)
        if platform not in SUPPORTED_PLATFORMS:
            raise ValueError(
                f"{label} has unsupported platform: {platform}"
            )
        if platform in parsed:
            raise ValueError(
                f"{label} repeats platform: {platform}"
            )
        if not path_text.strip():
            raise ValueError(
                f"{label} path is empty for {platform}"
            )
        parsed[platform] = Path(path_text)
    return parsed


def parse_full_suite_path_arguments(
    values: list[str] | None,
    label: str,
) -> dict[str, Path]:
    parsed: dict[str, Path] = {}
    for value in values or []:
        if "=" not in value:
            raise ValueError(
                f"{label} must use platform/Target=path syntax: {value}"
            )
        key, path_text = value.split("=", 1)
        if "/" not in key:
            raise ValueError(
                f"{label} must use platform/Target=path syntax: {value}"
            )
        platform, target = key.split("/", 1)
        if platform not in SUPPORTED_PLATFORMS:
            raise ValueError(
                f"{label} has unsupported platform: {platform}"
            )
        if target not in FULL_SUITE_TARGETS:
            raise ValueError(
                f"{label} has unsupported target: {target}"
            )
        canonical_key = f"{platform}/{target}"
        if key != canonical_key:
            raise ValueError(
                f"{label} key is not canonical: {key}"
            )
        if canonical_key in parsed:
            raise ValueError(
                f"{label} repeats platform/target: {canonical_key}"
            )
        if not path_text.strip():
            raise ValueError(
                f"{label} path is empty for {canonical_key}"
            )
        parsed[canonical_key] = Path(path_text)
    return parsed


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--mode",
        choices=(
            "completeness",
            "Baseline",
            "PullRequest",
            "Development",
            "Release",
        ),
        required=True,
    )
    parser.add_argument("--legacy-root", type=Path)
    parser.add_argument("--base-manifest", type=Path)
    parser.add_argument("--write-report", action="store_true")
    parser.add_argument("--verify-live-results", action="store_true")
    parser.add_argument(
        "--promote-evidence",
        choices=("red", "green"),
    )
    parser.add_argument("--case-id", action="append")
    parser.add_argument(
        "--legacy-results",
        "--legacy-run",
        dest="legacy_results",
        type=Path,
    )
    parser.add_argument(
        "--xplat-results",
        "--xplat-run",
        dest="xplat_results",
        type=Path,
    )
    parser.add_argument("--legacy-oracle-registry", type=Path)
    parser.add_argument("--legacy-oracle-registry-sha256")
    parser.add_argument("--legacy-test-report", type=Path)
    parser.add_argument("--xplat-test-report", type=Path)
    parser.add_argument("--legacy-execution", type=Path)
    parser.add_argument("--xplat-execution", type=Path)
    parser.add_argument("--green-result", action="append")
    parser.add_argument("--green-test-report", action="append")
    parser.add_argument("--green-execution", action="append")
    parser.add_argument("--full-suite-result", action="append")
    parser.add_argument("--full-suite-test-report", action="append")
    parser.add_argument("--full-suite-execution", action="append")
    parser.add_argument("--full-suite-package-index", type=Path)
    parser.add_argument("--full-suite-package-index-sha256")
    parser.add_argument("--capture-green-case-id", action="append")
    parser.add_argument("--green-regression-case-id", action="append")
    return parser.parse_args()


def validate_cli_option_contract(args: argparse.Namespace) -> None:
    promotion = args.promote_evidence
    has_green_inputs = any(
        value is not None
        for value in (
            args.green_result,
            args.green_test_report,
            args.green_execution,
        )
    )
    has_full_suite_inputs = any(
        value is not None
        for value in (
            args.full_suite_result,
            args.full_suite_test_report,
            args.full_suite_execution,
        )
    )
    has_full_suite_package_input = (
        args.full_suite_package_index is not None
        or args.full_suite_package_index_sha256 is not None
    )
    if (
        args.capture_green_case_id is not None
        and args.green_regression_case_id is not None
    ):
        raise ValueError(
            "selected green capture and its full-suite regression gate "
            "must be separate invocations"
        )
    if args.capture_green_case_id is not None:
        if promotion is not None:
            raise ValueError(
                "--capture-green-case-id cannot be combined with "
                "--promote-evidence"
            )
        if (
            args.case_id is not None
            or has_green_inputs
            or has_full_suite_inputs
            or has_full_suite_package_input
        ):
            raise ValueError(
                "green-candidate capture cannot use promotion case/map "
                "arguments"
            )
        if args.mode != "Baseline" or not args.verify_live_results:
            raise ValueError(
                "--capture-green-case-id requires Baseline mode and "
                "--verify-live-results"
            )
        if args.write_report:
            raise ValueError(
                "green-candidate capture is validation-only and cannot "
                "write the report"
            )
        if (
            args.xplat_results is None
            or args.xplat_test_report is None
            or args.xplat_execution is None
        ):
            raise ValueError(
                "green-candidate capture requires XPlat result, TRX, "
                "and execution envelope artifacts"
            )
        if any(
            value is not None
            for value in (
                args.legacy_results,
                args.legacy_oracle_registry,
                args.legacy_oracle_registry_sha256,
                args.legacy_test_report,
                args.legacy_execution,
            )
        ):
            raise ValueError(
                "green-candidate capture is XPlat-only"
            )
    if args.green_regression_case_id is not None:
        if promotion is not None:
            raise ValueError(
                "--green-regression-case-id cannot be combined with "
                "--promote-evidence"
            )
        if (
            args.case_id is not None
            or has_green_inputs
            or has_full_suite_inputs
            or has_full_suite_package_input
        ):
            raise ValueError(
                "green regression verification cannot use promotion "
                "case/map arguments"
            )
        if args.mode != "Development" or not args.verify_live_results:
            raise ValueError(
                "--green-regression-case-id requires Development mode "
                "and --verify-live-results"
            )
        if args.write_report:
            raise ValueError(
                "green regression verification is validation-only and "
                "cannot write the report"
            )
        if (
            args.xplat_results is None
            or args.xplat_test_report is None
            or args.xplat_execution is None
        ):
            raise ValueError(
                "green regression verification requires XPlat result, "
                "TRX, and execution envelope artifacts"
            )
        if any(
            value is not None
            for value in (
                args.legacy_results,
                args.legacy_oracle_registry,
                args.legacy_oracle_registry_sha256,
                args.legacy_test_report,
                args.legacy_execution,
            )
        ):
            raise ValueError(
                "green regression verification is XPlat-only"
            )
    if promotion is None:
        if args.case_id is not None:
            raise ValueError(
                "--case-id requires --promote-evidence"
            )
        if has_green_inputs:
            raise ValueError(
                "green result/TRX/execution maps require green promotion"
            )
        if has_full_suite_inputs:
            raise ValueError(
                "full-suite result/TRX/execution maps require green "
                "promotion"
            )
        if has_full_suite_package_input:
            raise ValueError(
                "full-suite package index inputs require green promotion"
            )
        return
    if args.mode != "Baseline":
        raise ValueError(
            "evidence promotion is allowed only in Baseline mode"
        )
    if (
        args.capture_green_case_id is not None
        or args.green_regression_case_id is not None
    ):
        raise ValueError(
            "green capture/regression verification cannot promote evidence"
        )
    if args.verify_live_results:
        raise ValueError(
            "evidence promotion validates its inputs directly and cannot "
            "use --verify-live-results"
        )
    if args.write_report:
        raise ValueError(
            "evidence promotion writes its report atomically"
        )
    if not args.case_id:
        raise ValueError(
            "evidence promotion requires at least one --case-id"
        )
    if promotion == "green":
        if not all(
            value
            for value in (
                args.green_result,
                args.green_test_report,
                args.green_execution,
            )
        ):
            raise ValueError(
                "green promotion requires complete result, TRX, and "
                "execution platform maps"
            )
        if not all(
            value
            for value in (
                args.full_suite_result,
                args.full_suite_test_report,
                args.full_suite_execution,
            )
        ):
            raise ValueError(
                "green promotion requires complete full-suite result, "
                "TRX, and execution platform/target maps"
            )
        if (
            args.legacy_oracle_registry is None
            or args.legacy_oracle_registry_sha256 is None
        ):
            raise ValueError(
                "green promotion requires the full-suite Legacy oracle "
                "registry and SHA-256"
            )
        if (
            args.full_suite_package_index is None
            or args.full_suite_package_index_sha256 is None
        ):
            raise ValueError(
                "green promotion requires the full-suite package index "
                "and SHA-256"
            )
        if any(
            value is not None
            for value in (
                args.legacy_results,
                args.xplat_results,
                args.legacy_test_report,
                args.xplat_test_report,
                args.legacy_execution,
                args.xplat_execution,
            )
        ):
            raise ValueError(
                "green promotion supplies selected evidence through "
                "--green-* and regression evidence through "
                "--full-suite-* maps"
            )
    else:
        if has_green_inputs:
            raise ValueError(
                "green result/TRX/execution maps are forbidden for red "
                "promotion"
            )
        if has_full_suite_inputs:
            raise ValueError(
                "full-suite result/TRX/execution maps are forbidden for "
                "red promotion"
            )
        if has_full_suite_package_input:
            raise ValueError(
                "full-suite package index inputs are forbidden for red "
                "promotion"
            )


def main() -> int:
    args = parse_args()
    monotonic_comparison: str | None = None
    live_summary: dict[str, Any] | None = None
    try:
        validate_cli_option_contract(args)
        manifest = load_json(MANIFEST_PATH)
        if args.promote_evidence is not None:
            if args.mode != "Baseline":
                raise ValueError(
                    "evidence promotion is allowed only in Baseline mode"
                )
            if not args.case_id:
                raise ValueError(
                    "evidence promotion requires at least one --case-id"
                )
            case_ids = sorted(
                (
                    require_nonempty_string(
                        case_id,
                        "evidence promotion case ID",
                    )
                    for case_id in args.case_id
                ),
                key=utf16_ordinal_key,
            )
            validate_manifest(
                manifest,
                args.legacy_root,
                promotion_case_ids=set(case_ids),
            )
            promoted_paths = promote_evidence_batch(
                manifest,
                args.promote_evidence,
                case_ids,
                legacy_run_path=args.legacy_results,
                xplat_run_path=args.xplat_results,
                registry_path=args.legacy_oracle_registry,
                registry_sha256=(
                    args.legacy_oracle_registry_sha256
                ),
                legacy_test_report_path=args.legacy_test_report,
                xplat_test_report_path=args.xplat_test_report,
                legacy_execution_path=args.legacy_execution,
                xplat_execution_path=args.xplat_execution,
                green_results=parse_platform_path_arguments(
                    args.green_result,
                    "--green-result",
                ),
                green_test_reports=parse_platform_path_arguments(
                    args.green_test_report,
                    "--green-test-report",
                ),
                green_executions=parse_platform_path_arguments(
                    args.green_execution,
                    "--green-execution",
                ),
                full_suite_results=parse_full_suite_path_arguments(
                    args.full_suite_result,
                    "--full-suite-result",
                ),
                full_suite_test_reports=parse_full_suite_path_arguments(
                    args.full_suite_test_report,
                    "--full-suite-test-report",
                ),
                full_suite_executions=parse_full_suite_path_arguments(
                    args.full_suite_execution,
                    "--full-suite-execution",
                ),
                full_suite_package_index=(
                    args.full_suite_package_index
                ),
                full_suite_package_index_sha256=(
                    args.full_suite_package_index_sha256
                ),
            )
            for promoted_path in promoted_paths:
                print(
                    "Promoted immutable parity evidence: "
                    f"{promoted_path}"
                )
        elif args.case_id is not None:
            raise ValueError(
                "--case-id requires --promote-evidence"
            )
        inventory, mappings, retained_evidence = validate_manifest(
            manifest,
            args.legacy_root,
        )
        capture_case_ids: set[str] | None = None
        regression_case_ids: set[str] | None = None
        if args.capture_green_case_id is not None:
            if args.promote_evidence is not None:
                raise ValueError(
                    "--capture-green-case-id cannot be combined with "
                    "--promote-evidence"
                )
            if args.mode != "Baseline" or not args.verify_live_results:
                raise ValueError(
                    "--capture-green-case-id requires Baseline mode and "
                    "--verify-live-results"
                )
            if args.write_report:
                raise ValueError(
                    "green-candidate capture is validation-only and cannot "
                    "write the report"
                )
            capture_case_ids = {
                require_nonempty_string(
                    case_id,
                    "green-candidate case ID",
                )
                for case_id in args.capture_green_case_id
            }
            if len(capture_case_ids) != len(
                args.capture_green_case_id
            ):
                raise ValueError(
                    "--capture-green-case-id values must be unique"
                )
            selected_cases = [
                select_manifest_case(manifest, case_id)
                for case_id in capture_case_ids
            ]
            if any(
                case["status"] != "legacy-green-xplat-red"
                for case in selected_cases
            ):
                raise ValueError(
                    "green-candidate capture requires active red cases"
                )
            if (
                args.legacy_results is not None
                or args.legacy_oracle_registry is not None
                or args.legacy_oracle_registry_sha256 is not None
                or args.legacy_test_report is not None
                or args.legacy_execution is not None
            ):
                raise ValueError(
                    "green-candidate capture is XPlat-only"
                )
        if args.green_regression_case_id is not None:
            regression_case_ids = {
                require_nonempty_string(
                    case_id,
                    "green regression case ID",
                )
                for case_id in args.green_regression_case_id
            }
            if len(regression_case_ids) != len(
                args.green_regression_case_id
            ):
                raise ValueError(
                    "--green-regression-case-id values must be unique"
                )
            regression_cases = [
                select_manifest_case(manifest, case_id)
                for case_id in regression_case_ids
            ]
            if any(
                case["status"] != "legacy-green-xplat-red"
                for case in regression_cases
            ):
                raise ValueError(
                    "green regression verification requires active red "
                    "cases"
                )
            regression_document = load_json(
                resolve_artifact_file(
                    args.xplat_results,
                    "green regression XPlat result",
                )
            )
            regression_context = regression_document.get("runContext")
            regression_platform = (
                regression_context.get("platform")
                if isinstance(regression_context, dict)
                else None
            )
            applicable_regression_ids = {
                case["id"]
                for case in regression_cases
                if regression_platform in case["platforms"]
            }
            if applicable_regression_ids != regression_case_ids:
                raise ValueError(
                    "green regression case IDs must all apply to the "
                    "captured platform"
                )
        if args.promote_evidence is None and args.verify_live_results:
            live_summary = validate_live_results(
                manifest,
                args.legacy_results,
                args.xplat_results,
                args.legacy_oracle_registry,
                args.legacy_oracle_registry_sha256,
                args.legacy_test_report,
                args.xplat_test_report,
                args.mode,
                legacy_execution_path=args.legacy_execution,
                xplat_execution_path=args.xplat_execution,
                outcome_overrides=(
                    {
                        case_id: "passed"
                        for case_id in capture_case_ids
                    }
                    if capture_case_ids is not None
                    else (
                        {
                            case_id: "passed"
                            for case_id in regression_case_ids
                        }
                        if regression_case_ids is not None
                        else None
                    )
                ),
                selected_case_ids=capture_case_ids,
            )
        elif (
            args.promote_evidence is None
            and (
                args.legacy_results is not None
                or args.xplat_results is not None
                or args.legacy_oracle_registry is not None
                or args.legacy_oracle_registry_sha256 is not None
                or args.legacy_test_report is not None
                 or args.xplat_test_report is not None
                 or args.legacy_execution is not None
                 or args.xplat_execution is not None
                 or args.green_result is not None
                 or args.green_test_report is not None
                 or args.green_execution is not None
             )
        ):
            raise ValueError(
                "live result paths require --verify-live-results"
            )
        elif args.promote_evidence is None and args.mode == "Release":
            raise ValueError(
                "Release mode requires --verify-live-results"
            )
        rendered_report = render_report(
            manifest,
            inventory,
            mappings,
            retained_evidence,
        )
        if args.write_report and args.promote_evidence is None:
            REPORT_PATH.write_text(
                rendered_report,
                encoding="utf-8",
                newline="\n",
            )
            print(f"Wrote generated parity report: {REPORT_PATH}")
        else:
            validate_report(rendered_report)

        monotonic_comparison = validate_mode_history(
            manifest,
            args.mode,
            args.base_manifest,
            retained_evidence=retained_evidence,
        )
        if live_summary is not None:
            for target in ("legacy", "xplat"):
                target_summary = live_summary[target]
                if target_summary is not None:
                    print(
                        f"Validated live {target} results: "
                        f"{target_summary['count']} cases on "
                        f"{target_summary['platform']}"
                    )
    except (
        OSError,
        ValueError,
        KeyError,
        TypeError,
        subprocess.SubprocessError,
    ) as error:
        print(f"Parity validation failed: {error}", file=sys.stderr)
        return 1

    if args.mode == "completeness":
        capability_counts = derive_capability_status_counts(
            manifest["items"]
        )
        case_counts = derive_case_status_counts(manifest["cases"])
        print("Declared mappings and evidence records are structurally valid.")
        print(f"Discovered legacy surfaces: {len(inventory['surfaces'])}")
        print(f"Mapped legacy surfaces: {len(mappings)}")
        print("Unmapped legacy surfaces: 0")
        print(f"Inventory status: {manifest['inventoryStatus']}")
        print(
            "Not-authored capabilities: "
            f"{capability_counts['not-authored']}"
        )
        print(
            "Partial capabilities: "
            f"{capability_counts['partial']}"
        )
        print(
            "Complete capabilities: "
            f"{capability_counts['complete']}"
        )
        print(
            "Active acceptance cases: "
            f"{len(manifest['cases'])}"
        )
        print(
            "Legacy-green/XPlat-red cases: "
            f"{case_counts['legacy-green-xplat-red']}"
        )
        print(
            "Evidence-certified both-green cases: "
            f"{case_counts['both-green']}"
        )
        print(
            "Retained legacy-v1 observations: "
            f"{len(retained_evidence)} (noncertifying)"
        )
        print(
            "Pending audit surfaces: "
            f"{len(manifest['pendingAuditSurfaces'])}"
        )
        return 0

    return report(
        manifest,
        inventory,
        retained_evidence,
        args.mode,
        monotonic_comparison,
    )


if __name__ == "__main__":
    raise SystemExit(main())
