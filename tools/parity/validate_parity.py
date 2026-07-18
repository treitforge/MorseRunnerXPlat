from __future__ import annotations

import argparse
import fnmatch
import json
import subprocess
import sys
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any

from inventory_legacy import build_inventory, serialize_inventory


ROOT = Path(__file__).resolve().parents[2]
MANIFEST_PATH = ROOT / "tests" / "parity" / "parity-manifest.json"
REPORT_PATH = ROOT / "tests" / "parity" / "PARITY_REPORT.md"
ACTIVE_STATUSES = {"legacy-green-xplat-red", "both-green"}


def load_json(path: Path) -> dict[str, Any]:
    with path.open(encoding="utf-8") as stream:
        value = json.load(stream)
    if not isinstance(value, dict):
        raise ValueError(f"{path} must contain a JSON object")
    return value


def validate_manifest(
    manifest: dict[str, Any],
    legacy_root: Path | None,
) -> tuple[dict[str, Any], dict[str, list[str]]]:
    required_root = {
        "schemaVersion",
        "inventoryStatus",
        "reference",
        "legacySurfaceInventory",
        "auditedSurfaces",
        "pendingAuditSurfaces",
        "items",
    }
    missing_root = sorted(required_root - manifest.keys())
    if missing_root:
        raise ValueError(f"manifest is missing root fields: {', '.join(missing_root)}")

    inventory_path = ROOT / manifest["legacySurfaceInventory"]
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

    items = manifest["items"]
    if not isinstance(items, list) or not items:
        raise ValueError("manifest items must be a non-empty array")

    required_item = {
        "id",
        "category",
        "feature",
        "behavior",
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
    ids: set[str] = set()
    for item in items:
        if not isinstance(item, dict):
            raise ValueError("each manifest item must be an object")
        missing_item = sorted(required_item - item.keys())
        if missing_item:
            raise ValueError(
                f"manifest item is missing fields: {', '.join(missing_item)}"
            )
        parity_id = item["id"]
        if parity_id in ids:
            raise ValueError(f"duplicate parity ID: {parity_id}")
        ids.add(parity_id)

        if not item["legacySources"]:
            raise ValueError(f"{parity_id} has no legacy source references")
        if not item["legacySurfaceSelectors"]:
            raise ValueError(f"{parity_id} has no legacy surface selectors")

        status = item["status"]
        if status == "inventory-only":
            validate_inventory_only_item(item)
        elif status in ACTIVE_STATUSES:
            validate_active_item(item)
        else:
            raise ValueError(f"{parity_id} has invalid status: {status}")

    mappings = map_surfaces(items, inventory["surfaces"])
    validate_contest_catalog_item(items, inventory)
    return inventory, mappings


def validate_inventory(
    manifest: dict[str, Any],
    inventory: dict[str, Any],
) -> None:
    if inventory.get("schemaVersion") != 1:
        raise ValueError("legacy surface inventory has an unsupported schema")
    surfaces = inventory.get("surfaces")
    if not isinstance(surfaces, list) or not surfaces:
        raise ValueError("legacy surface inventory must contain surfaces")

    pinned_revision = manifest["reference"]["revision"]
    if inventory.get("reference", {}).get("revision") != pinned_revision:
        raise ValueError("legacy surface inventory revision is not pinned by manifest")

    required_surface = {"id", "category", "name", "source", "details"}
    surface_ids: set[str] = set()
    for surface in surfaces:
        if not isinstance(surface, dict):
            raise ValueError("each legacy surface must be an object")
        missing = sorted(required_surface - surface.keys())
        if missing:
            raise ValueError(
                f"legacy surface is missing fields: {', '.join(missing)}"
            )
        surface_id = surface["id"]
        if surface_id in surface_ids:
            raise ValueError(f"duplicate legacy surface ID: {surface_id}")
        surface_ids.add(surface_id)


def validate_inventory_only_item(item: dict[str, Any]) -> None:
    parity_id = item["id"]
    if item["legacyTestStatus"] != "not-authored":
        raise ValueError(f"{parity_id} inventory item has a legacy test status")
    if item["xplatTestStatus"] != "not-authored":
        raise ValueError(f"{parity_id} inventory item has an XPlat test status")
    if item["fixture"] is not None or item["evidence"] is not None:
        raise ValueError(f"{parity_id} inventory item must not claim test evidence")
    if item["failureCode"] is not None:
        raise ValueError(f"{parity_id} inventory item must not claim a failure")
    if item["targetAdapters"]:
        raise ValueError(f"{parity_id} inventory item must not claim adapters")


def validate_active_item(item: dict[str, Any]) -> None:
    parity_id = item["id"]
    for key in ("fixture", "evidence"):
        value = item[key]
        if not isinstance(value, str) or not value:
            raise ValueError(f"{parity_id} active item has no {key}")
        path = ROOT / value
        if not path.is_file():
            raise ValueError(f"{parity_id} {key} does not exist: {path}")

    if item["legacyTestStatus"] != "pass":
        raise ValueError(f"{parity_id} is not legacy-green")
    if not item["targetAdapters"]:
        raise ValueError(f"{parity_id} active item has no target adapters")
    if item["status"] == "legacy-green-xplat-red":
        if item["xplatTestStatus"] != "fail":
            raise ValueError(f"{parity_id} red status must record XPlat failure")
        if not item["failureCode"]:
            raise ValueError(f"{parity_id} red status must record a failure code")
    elif item["xplatTestStatus"] != "pass":
        raise ValueError(f"{parity_id} both-green status requires XPlat pass")


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


def validate_contest_catalog_item(
    items: list[dict[str, Any]],
    inventory: dict[str, Any],
) -> None:
    item = next(
        (entry for entry in items if entry["id"] == "catalog.contest-enumeration"),
        None,
    )
    if item is None:
        raise ValueError("contest enumeration has no manifest mapping")

    fixture = load_json(ROOT / item["fixture"])
    expected = item["assertions"]["orderedValues"]
    inventory_values = [
        surface["name"]
        for surface in inventory["surfaces"]
        if surface["category"] == "contest-enumeration"
    ]
    if fixture.get("values") != expected:
        raise ValueError("contest enumeration fixture and assertion differ")
    if inventory_values != expected:
        raise ValueError("contest enumeration inventory and assertion differ")


def render_report(
    manifest: dict[str, Any],
    inventory: dict[str, Any],
    mappings: dict[str, list[str]],
) -> str:
    items = manifest["items"]
    active_items = [item for item in items if item["status"] in ACTIVE_STATUSES]
    category_counts = Counter(
        surface["category"] for surface in inventory["surfaces"]
    )
    mapped_counts = Counter(
        parity_ids[0] for parity_ids in mappings.values()
    )
    both_green = sum(item["status"] == "both-green" for item in active_items)
    gaps = sum(
        item["status"] == "legacy-green-xplat-red" for item in active_items
    )

    lines = [
        "# MorseRunnerXPlat parity report",
        "",
        "Generated from `parity-manifest.json` and "
        "`legacy-surface-inventory.json`. Do not edit by hand.",
        "",
        "## Inventory",
        "",
        f"- Inventory status: `{manifest['inventoryStatus']}`",
        f"- Pinned legacy revision: `{manifest['reference']['revision']}`",
        f"- Discovered legacy surfaces: {len(inventory['surfaces'])}",
        f"- Mapped legacy surfaces: {len(mappings)}",
        f"- Unmapped legacy surfaces: {len(inventory['surfaces']) - len(mappings)}",
        f"- Pending audit surfaces: {len(manifest['pendingAuditSurfaces'])}",
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
            f"- Manifest capabilities: {len(items)}",
            f"- Acceptance cases authored: {len(active_items)}",
            f"- Inventory-only capabilities: {len(items) - len(active_items)}",
            f"- Both-green: {both_green}",
            f"- Legacy-green/XPlat-red: {gaps}",
            "- Skipped, waived, quarantined, disabled, or expected-failure: 0",
            "",
            "| Parity ID | Feature | Status | Legacy | XPlat | "
            "Mapped surfaces | Legacy source |",
            "|---|---|---|---|---|---:|---|",
        ]
    )
    for item in items:
        sources = "<br>".join(f"`{source}`" for source in item["legacySources"])
        lines.append(
            f"| `{item['id']}` | {item['feature']} | `{item['status']}` | "
            f"`{item['legacyTestStatus']}` | `{item['xplatTestStatus']}` | "
            f"{mapped_counts[item['id']]} | {sources} |"
        )

    lines.extend(["", "## Pending completeness audits", ""])
    for pending in manifest["pendingAuditSurfaces"]:
        lines.append(f"- {pending}")
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


def report(manifest: dict[str, Any], inventory: dict[str, Any], mode: str) -> int:
    items = manifest["items"]
    active_items = [item for item in items if item["status"] in ACTIVE_STATUSES]
    legacy_passed = sum(
        item["legacyTestStatus"] == "pass" for item in active_items
    )
    xplat_passed = sum(
        item["xplatTestStatus"] == "pass" for item in active_items
    )
    xplat_failed = sum(
        item["xplatTestStatus"] == "fail" for item in active_items
    )
    both_green = sum(item["status"] == "both-green" for item in active_items)
    gaps = sum(
        item["status"] == "legacy-green-xplat-red" for item in active_items
    )

    print(f"Manifest status:          {manifest['inventoryStatus']}")
    print(f"Manifest capabilities:    {len(items)}")
    print(f"Legacy surfaces:          {len(inventory['surfaces'])}")
    print(f"Acceptance tests authored: {len(active_items)}")
    print(f"Legacy passed:            {legacy_passed}")
    print(f"XPlat passed:             {xplat_passed}")
    print(f"XPlat failed:             {xplat_failed}")
    print(f"Both-green:               {both_green}")
    print(f"Functional gaps:          {gaps}")
    print("Skipped/waived cases:     0")
    print("Expected-failure cases:   0")
    print(
        "Pending audit surfaces:  "
        f"{len(manifest['pendingAuditSurfaces'])}"
    )

    if mode == "Release" and (
        manifest["inventoryStatus"] != "complete"
        or manifest["pendingAuditSurfaces"]
        or len(active_items) != len(items)
        or gaps != 0
        or both_green != len(items)
    ):
        print("Release parity gate: FAILED")
        return 1

    print(f"{mode} parity run: completed")
    return 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--mode",
        choices=("completeness", "Baseline", "PullRequest", "Development", "Release"),
        required=True,
    )
    parser.add_argument("--legacy-root", type=Path)
    parser.add_argument("--write-report", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    manifest = load_json(MANIFEST_PATH)
    try:
        inventory, mappings = validate_manifest(manifest, args.legacy_root)
        rendered_report = render_report(manifest, inventory, mappings)
        if args.write_report:
            REPORT_PATH.write_text(rendered_report, encoding="utf-8", newline="\n")
            print(f"Wrote generated parity report: {REPORT_PATH}")
        else:
            validate_report(rendered_report)
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
        print("Declared parity mappings and pinned observations are valid.")
        print(f"Discovered legacy surfaces: {len(inventory['surfaces'])}")
        print(f"Mapped legacy surfaces: {len(mappings)}")
        print("Unmapped legacy surfaces: 0")
        print(f"Inventory status: {manifest['inventoryStatus']}")
        print(
            "Pending audit surfaces: "
            f"{len(manifest['pendingAuditSurfaces'])}"
        )
        return 0

    return report(manifest, inventory, args.mode)


if __name__ == "__main__":
    raise SystemExit(main())
