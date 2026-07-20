from __future__ import annotations

import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS_DIR = Path(__file__).resolve().parent
ROOT = TOOLS_DIR.parents[1]
INVENTORY_PATH = ROOT / "tests" / "parity" / "legacy-surface-inventory.json"
sys.path.insert(0, str(TOOLS_DIR))

from inventory_legacy import (  # noqa: E402
    materialize_canonical_git_tree,
    parse_bundled_assets,
    parse_data_parser_paths,
    parse_external_integrations,
    parse_form_resource,
    parse_pascal_project,
    parse_pascal_tests,
    serialize_inventory,
    tracked_files_sha256,
    validate_tracked_file_classification,
)


class CommittedInventoryTests(unittest.TestCase):
    original_surface_count = 1501
    original_id_sha256 = (
        "984b87f2370505c84c9bae8f5412bc528ff467b55e4b24c0892a8573b0d74f16"
    )
    pinned_tracked_file_count = 143
    pinned_tracked_files_sha256 = (
        "7bb5b7a8a92996fefd67fdb752ba276"
        "bea31e87b6c355482ae511080412b589c"
    )

    def load_inventory(self) -> tuple[str, dict[str, object]]:
        rendered = INVENTORY_PATH.read_text(encoding="utf-8")
        return rendered, json.loads(rendered)

    def test_expansion_is_append_only_for_every_original_surface_id(
        self,
    ) -> None:
        _, inventory = self.load_inventory()
        surfaces = inventory["surfaces"]
        original_ids = [
            surface["id"]
            for surface in surfaces[: self.original_surface_count]
        ]
        digest = hashlib.sha256(
            "\n".join(original_ids).encode("utf-8")
        ).hexdigest()

        self.assertGreater(len(surfaces), self.original_surface_count)
        self.assertEqual(self.original_id_sha256, digest)

    def test_committed_inventory_is_canonical_and_has_unique_ids(self) -> None:
        rendered, inventory = self.load_inventory()
        surface_ids = [
            surface["id"]
            for surface in inventory["surfaces"]
        ]

        self.assertEqual(rendered, serialize_inventory(inventory))
        self.assertEqual(len(surface_ids), len(set(surface_ids)))

    def test_every_pinned_tracked_file_is_classified_exactly_once(
        self,
    ) -> None:
        _, inventory = self.load_inventory()
        reference = inventory["reference"]
        sources = reference["sources"]
        exclusions = reference["exclusions"]
        classified_files = sources + [
            exclusion["path"]
            for exclusion in exclusions
        ]

        self.assertEqual(2, inventory["schemaVersion"])
        self.assertEqual(
            self.pinned_tracked_file_count,
            reference["trackedFileCount"],
        )
        self.assertEqual(
            self.pinned_tracked_files_sha256,
            reference["trackedFilesSha256"],
        )
        self.assertEqual(
            reference["trackedFileCount"],
            len(classified_files),
        )
        self.assertEqual(
            reference["trackedFilesSha256"],
            tracked_files_sha256(classified_files),
        )
        validate_tracked_file_classification(
            classified_files,
            sources,
            exclusions,
        )

    def test_representative_omitted_surfaces_are_now_discovered(self) -> None:
        _, inventory = self.load_inventory()
        surfaces = inventory["surfaces"]
        surface_ids = {surface["id"] for surface in surfaces}

        self.assertIn(
            "legacy.form.lfm.main.object.mainform",
            surface_ids,
        )
        self.assertIn(
            "legacy.form.dfm.scoredlg.object.scoredialog",
            surface_ids,
        )
        self.assertIn(
            "legacy.project.definition.morserunner-lpi",
            surface_ids,
        )
        self.assertIn("legacy.asset.data.master-dta", surface_ids)
        self.assertIn(
            (
                "legacy.integration.network-client.main-pas."
                "tmainform.posthiscore"
            ),
            surface_ids,
        )
        self.assertIn(
            (
                "legacy.data.parser.calllst-pas."
                "tcalllist.loadcalllist"
            ),
            surface_ids,
        )

    def test_runtime_build_and_resource_inputs_are_inventoried(self) -> None:
        _, inventory = self.load_inventory()
        sources = set(inventory["reference"]["sources"])
        surface_ids = {
            surface["id"]
            for surface in inventory["surfaces"]
        }

        expected_sources = {
            ".gitattributes",
            "MorseRunner.cmds",
            "MorseRunner.lst",
            "MorseRunner.otares",
            "MorseRunner171a.pdf",
            "PerlRegEx/PerlRegEx.cnt",
            "PerlRegEx/PerlRegEx.hlp",
            "PerlRegEx/PerlRegEx.pas",
            "PerlRegEx/README.txt",
            "PerlRegEx/pcre.pas",
            "PerlRegEx/pcre/makefile.mak",
            "PerlRegEx/pcre/pcre_compile.obj",
            "VCL/VolmSldr.dcr",
            "VCL/VolumCtl.dcr",
            "tools/verify-normalization.sh",
        }
        self.assertTrue(expected_sources.issubset(sources))
        self.assertIn(
            "legacy.asset.build.morserunner-cmds",
            surface_ids,
        )
        self.assertIn(
            "legacy.asset.build.perlregex-pcre-pcre-compile-obj",
            surface_ids,
        )
        self.assertIn(
            "legacy.asset.resource.vcl-volmsldr-dcr",
            surface_ids,
        )
        self.assertTrue(
            any(
                surface_id.startswith(
                    "legacy.data.parser.regex.delphi-wrapper.routine."
                )
                for surface_id in surface_ids
            )
        )
        self.assertTrue(
            any(
                surface_id.startswith(
                    "legacy.data.parser.regex.pcre-binding.type."
                )
                for surface_id in surface_ids
            )
        )

    def test_exclusions_are_narrow_nonfunctional_metadata(self) -> None:
        _, inventory = self.load_inventory()
        exclusions = inventory["reference"]["exclusions"]

        self.assertEqual(12, len(exclusions))
        self.assertEqual(
            {
                ".gitignore",
                ".github/CODE_OF_CONDUCT.md",
                ".github/CONTRIBUTING.md",
                ".github/DEVELOPERS.md",
                ".github/ISSUE_TEMPLATE/bug-report.md",
                ".github/ISSUE_TEMPLATE/contest-request.md",
                ".github/ISSUE_TEMPLATE/feature-request.md",
                ".github/ISSUE_TEMPLATE/feedback.md",
                ".github/ISSUE_TEMPLATE/question-support.md",
                "Lazarus/README.md",
                "LICENSE.md",
                "README.md",
            },
            {
                exclusion["path"]
                for exclusion in exclusions
            },
        )
        self.assertTrue(
            all(exclusion["rationale"] for exclusion in exclusions)
        )

    def test_pascal_test_inventory_preserves_all_declared_cases(self) -> None:
        _, inventory = self.load_inventory()
        categories: dict[str, int] = {}
        for surface in inventory["surfaces"]:
            category = surface["category"]
            categories[category] = categories.get(category, 0) + 1

        self.assertEqual(
            961,
            categories["legacy-test-case"]
            + categories["legacy-test-disabled-case"],
        )
        self.assertEqual(
            24,
            categories["legacy-test-declaration"]
            + categories["legacy-test-disabled-declaration"],
        )
        self.assertEqual(4, categories["legacy-test-disabled-declaration"])
        self.assertEqual(19, categories["legacy-test-lifecycle"])
        self.assertEqual(3, categories["legacy-smoke-test"])
        self.assertEqual(10, categories["legacy-test-fixture"])


class InventoryParserTests(unittest.TestCase):
    def test_canonical_git_tree_ignores_checkout_line_endings(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            repository = root / "repository"
            repository.mkdir()

            def git(*arguments: str) -> subprocess.CompletedProcess[bytes]:
                return subprocess.run(
                    ["git", "-C", str(repository), *arguments],
                    check=True,
                    capture_output=True,
                )

            git("init", "--quiet")
            git("config", "user.name", "Parity Test")
            git("config", "user.email", "parity@example.invalid")
            source = repository / "Sample.pas"
            source.write_bytes(b"line one\nline two\n")
            git("add", "Sample.pas")
            git("commit", "--quiet", "-m", "canonical blob")
            revision = git("rev-parse", "HEAD").stdout.decode().strip()

            source.write_bytes(b"line one\r\nline two\r\n")
            exported = root / "exported"
            materialize_canonical_git_tree(
                repository,
                revision,
                ["Sample.pas"],
                exported,
            )

            self.assertEqual(
                b"line one\nline two\n",
                (exported / "Sample.pas").read_bytes(),
            )

    def test_canonical_git_tree_rejects_nonportable_paths(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            for source_name in (
                "../Escape.pas",
                "Nested\\Windows.pas",
                "Drive:Name.pas",
            ):
                with self.subTest(source_name=source_name):
                    with self.assertRaisesRegex(
                        ValueError,
                        "cannot be materialized portably",
                    ):
                        materialize_canonical_git_tree(
                            root,
                            "HEAD",
                            [source_name],
                            root / "exported",
                        )

    def test_newly_tracked_file_cannot_escape_classification(self) -> None:
        with self.assertRaisesRegex(ValueError, "not classified"):
            validate_tracked_file_classification(
                ["Ini.pas", "NewRuntime.pas"],
                ["Ini.pas"],
                [],
            )

    def test_omitting_tracked_file_from_sources_fails(self) -> None:
        with self.assertRaisesRegex(ValueError, "not classified"):
            validate_tracked_file_classification(
                ["OmittedResource.dcr"],
                [],
                [],
            )

    def test_file_cannot_be_inventoried_and_excluded(self) -> None:
        with self.assertRaisesRegex(ValueError, "exactly once"):
            validate_tracked_file_classification(
                ["README.md"],
                ["README.md"],
                [
                    {
                        "path": "README.md",
                        "kind": "repository-documentation",
                        "rationale": "Human documentation only.",
                    }
                ],
            )

    def test_exclusions_cannot_hide_functional_input_extensions(self) -> None:
        protected_paths = [
            "Runtime.pas",
            "PerlRegEx/pcre/runtime.obj",
            "VCL/Control.dcr",
            "Build/build.ps1",
            "MorseRunner.cmds",
            "Native/pcre.dll",
            "Data.txt",
            "Operator.pdf",
        ]
        for path in protected_paths:
            with self.subTest(path=path):
                with self.assertRaisesRegex(
                    ValueError,
                    "cannot be excluded",
                ):
                    validate_tracked_file_classification(
                        [path],
                        [],
                        [
                            {
                                "path": path,
                                "kind": "hidden-input",
                                "rationale": (
                                    "This attempted exclusion must fail."
                                ),
                            }
                        ],
                    )

    def test_classified_file_must_exist_in_pinned_tree(self) -> None:
        with self.assertRaisesRegex(ValueError, "absent from the pinned tree"):
            validate_tracked_file_classification(
                ["Ini.pas"],
                ["Ini.pas", "Removed.pas"],
                [],
            )

    def test_form_generation_is_deterministic_and_keeps_behavioral_properties(
        self,
    ) -> None:
        source = (
            "object Dialog: TDialog\n"
            "  Caption = 'Results'\n"
            "  OnCreate = FormCreate\n"
            "  object SubmitButton: TButton\n"
            "    Caption = '&Submit'\n"
            "    Default = True\n"
            "    ShortCut = 16467\n"
            "    OnClick = SubmitClick\n"
            "  end\n"
            "end\n"
        )

        first = parse_form_resource(source, "Result.lfm")
        second = parse_form_resource(source, "Result.lfm")

        self.assertEqual(first, second)
        self.assertEqual(
            [
                "form-object",
                "form-event-binding",
                "form-object",
                "form-event-binding",
                "form-keyboard-shortcut",
            ],
            [surface["category"] for surface in first],
        )
        button = next(
            surface
            for surface in first
            if surface["id"].endswith(".object.submitbutton")
        )
        self.assertEqual("True", button["details"]["default"])
        self.assertEqual("Submit", button["name"].replace("&", ""))

    def test_pascal_test_generation_is_deterministic_and_marks_disabled_cases(
        self,
    ) -> None:
        source = (
            "unit SampleTest;\n"
            "interface\n"
            "implementation\n"
            "type\n"
            "  [TestFixture]\n"
            "  TSample = class\n"
            "  public\n"
            "    [Setup]\n"
            "    procedure Setup;\n"
            "    [Test(False)]\n"
            "    [TestCase('Disabled', 'input,expected')]\n"
            "    //[TestCase('Commented', 'input,expected')]\n"
            "    procedure DisabledCase(const Input, Expected: string);\n"
            "  end;\n"
            "initialization\n"
            "  TDUnitX.RegisterTestFixture(TSample);\n"
            "end.\n"
        )
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            test_dir = root / "Test"
            test_dir.mkdir()
            (test_dir / "SampleTest.pas").write_text(
                source,
                encoding="utf-8",
            )
            tracked = ["Test/SampleTest.pas"]

            first, first_sources = parse_pascal_tests(root, tracked)
            second, second_sources = parse_pascal_tests(root, tracked)

        self.assertEqual(first, second)
        self.assertEqual(first_sources, second_sources)
        categories = [surface["category"] for surface in first]
        self.assertIn("legacy-test-disabled-declaration", categories)
        self.assertIn("legacy-test-disabled-method", categories)
        self.assertIn("legacy-test-disabled-case", categories)
        self.assertIn("legacy-test-commented-case", categories)
        self.assertIn("legacy-test-lifecycle", categories)

    def test_project_generation_keeps_units_lifecycle_and_resources(
        self,
    ) -> None:
        source = (
            "program Runner;\n"
            "uses\n"
            "  Forms,\n"
            "  Main in 'Main.pas' {MainForm};\n"
            "{$R *.RES}\n"
            "begin\n"
            "  Application.Initialize;\n"
            "  Application.CreateForm(TMainForm, MainForm);\n"
            "  Application.Run;\n"
            "end.\n"
        )

        first = parse_pascal_project(source, "Runner.dpr")
        second = parse_pascal_project(source, "Runner.dpr")

        self.assertEqual(first, second)
        categories = [surface["category"] for surface in first]
        self.assertEqual(2, categories.count("project-unit-reference"))
        self.assertEqual(3, categories.count("project-lifecycle"))
        self.assertEqual(1, categories.count("project-resource-reference"))

    def test_integrations_assets_and_parser_paths_are_deterministic(
        self,
    ) -> None:
        source = (
            "unit Loader;\n"
            "interface\n"
            "implementation\n"
            "procedure Load;\n"
            "begin\n"
            "  Items.LoadFromFile(ParamStr(1) + 'CALLS.TXT');\n"
            "  ShellExecute(0, 'open', 'CALLS.TXT', '', '', 1);\n"
            "end;\n"
            "end.\n"
        )
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "Loader.pas").write_text(source, encoding="utf-8")
            (root / "CALLS.TXT").write_bytes(b"K1ABC\n")
            tracked = ["CALLS.TXT", "Loader.pas"]

            first_integrations, _ = parse_external_integrations(root, tracked)
            second_integrations, _ = parse_external_integrations(root, tracked)
            parser_paths, _ = parse_data_parser_paths(root, tracked)
            assets, _ = parse_bundled_assets(root, tracked)

        self.assertEqual(first_integrations, second_integrations)
        self.assertEqual(
            {"command-line-path", "shell-launch"},
            {
                surface["details"]["integrationKind"]
                for surface in first_integrations
            },
        )
        self.assertEqual(["CALLS.TXT"], parser_paths[0]["details"]["references"])
        self.assertEqual(6, assets[0]["details"]["bytes"])


if __name__ == "__main__":
    unittest.main()
