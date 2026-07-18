from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path


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
from validate_parity import map_surfaces  # noqa: E402


class SurfaceMappingTests(unittest.TestCase):
    def test_unmapped_surface_fails_completeness(self) -> None:
        items = [
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
            map_surfaces(items, surfaces)

    def test_surface_mapped_twice_fails_completeness(self) -> None:
        items = [
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
            map_surfaces(items, surfaces)


class PascalSourceTests(unittest.TestCase):
    def test_comment_masking_preserves_lines_and_ignores_comment_content(self) -> None:
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

    def test_unit_inventory_keeps_type_property_and_overload_identity(self) -> None:
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
            ["legacy.test.type.filter.tfilter",
             "legacy.test.property.filter.tfilter.gain"],
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
            "  if FileExists('CALLS.TXT') then List.LoadFromFile('CALLS.TXT');\n"
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


if __name__ == "__main__":
    unittest.main()
