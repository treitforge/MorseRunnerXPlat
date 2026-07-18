from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[2]
INVENTORY_PATH = ROOT / "tests" / "parity" / "legacy-surface-inventory.json"
FIXTURE_ROOT = ROOT / "tests" / "parity" / "fixtures" / "legacy"
EVIDENCE_ROOT = ROOT / "tests" / "parity" / "evidence"

BASELINES = {
    "configuration.persisted-settings": ["legacy.ini.setting."],
    "ux.main-form-objects": ["legacy.main.object."],
    "ux.main-menu-commands": ["legacy.main.menu-item."],
    "ux.main-form-events": [
        "legacy.main.event-binding.",
        "legacy.main.handler.",
    ],
    "ux.keyboard-workflows": [
        "legacy.main.shortcut.",
        "legacy.main.keyboard.",
    ],
    "logging.qso-model": [
        "legacy.log.error.",
        "legacy.log.qso-field.",
    ],
    "ux.legacy-vcl-components": ["legacy.vcl.ui."],
    "ux.score-dialog": ["legacy.support.ui."],
    "data.files-and-operational-paths": [
        "legacy.data.reference.",
        "legacy.operation.",
    ],
}


def canonical_surface(surface: dict[str, Any]) -> str:
    details = json.dumps(
        surface["details"],
        ensure_ascii=False,
        separators=(",", ":"),
    )
    return f"{surface['id']}|{surface['name']}|{details}"


def render(value: dict[str, Any]) -> str:
    return json.dumps(value, ensure_ascii=False, indent=2) + "\n"


def captured_at(evidence_path: Path) -> str:
    if evidence_path.is_file():
        existing = json.loads(evidence_path.read_text(encoding="utf-8"))
        value = existing.get("capturedAtUtc")
        if isinstance(value, str):
            return value
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace(
        "+00:00",
        "Z",
    )


def main() -> int:
    inventory = json.loads(INVENTORY_PATH.read_text(encoding="utf-8"))
    revision = inventory["reference"]["revision"]
    surfaces = inventory["surfaces"]

    for parity_id, prefixes in BASELINES.items():
        matching = sorted(
            (
                surface
                for surface in surfaces
                if any(surface["id"].startswith(prefix) for prefix in prefixes)
            ),
            key=lambda surface: surface["id"],
        )
        if not matching:
            raise SystemExit(f"{parity_id} matched no legacy surfaces")

        file_stem = parity_id.replace(".", "-")
        fixture_relative = (
            f"tests/parity/fixtures/legacy/{file_stem}.json"
        )
        evidence_relative = f"tests/parity/evidence/{file_stem}.baseline.json"
        fixture_path = ROOT / fixture_relative
        evidence_path = ROOT / evidence_relative
        fixture = {
            "revision": revision,
            "parityId": parity_id,
            "surfacePrefixes": prefixes,
            "values": [canonical_surface(surface) for surface in matching],
        }
        evidence = {
            "parityId": parity_id,
            "referenceRevision": revision,
            "capturedAtUtc": captured_at(evidence_path),
            "legacy": {
                "outcome": "pass",
                "source": "tests/parity/legacy-surface-inventory.json",
                "fixture": fixture_relative,
                "observedSurfaceCount": len(matching),
            },
            "xplat": {
                "outcome": "fail",
                "failureCode": "unsupported-capability",
                "firstDivergence": (
                    f"The XPlat {parity_id} capability does not exist."
                ),
            },
            "classification": "legacy-green-xplat-red",
        }
        fixture_path.write_text(render(fixture), encoding="utf-8", newline="\n")
        evidence_path.write_text(
            render(evidence),
            encoding="utf-8",
            newline="\n",
        )
        print(f"Wrote {len(matching)} values for {parity_id}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
