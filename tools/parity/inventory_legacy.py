from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import tempfile
from collections import defaultdict
from pathlib import Path, PurePosixPath
from typing import Any


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_OUTPUT = ROOT / "tests" / "parity" / "legacy-surface-inventory.json"

PROTECTED_FUNCTIONAL_INPUT_SUFFIXES = {
    ".a",
    ".adi",
    ".bin",
    ".bmp",
    ".c",
    ".cfg",
    ".cmds",
    ".conf",
    ".cnt",
    ".csv",
    ".deployproj",
    ".dcr",
    ".dfm",
    ".dll",
    ".dpk",
    ".dpr",
    ".dproj",
    ".dta",
    ".dylib",
    ".exe",
    ".groupproj",
    ".h",
    ".hlp",
    ".ico",
    ".inc",
    ".ini",
    ".json",
    ".lfm",
    ".lib",
    ".log",
    ".lpi",
    ".lpr",
    ".list",
    ".lst",
    ".mak",
    ".manifest",
    ".o",
    ".obj",
    ".otares",
    ".pas",
    ".pdf",
    ".ps1",
    ".rc",
    ".res",
    ".sh",
    ".so",
    ".txt",
    ".toml",
    ".wav",
    ".xml",
    ".yaml",
    ".yml",
    ".zip",
}
PROTECTED_FUNCTIONAL_INPUT_NAMES = {
    ".gitattributes",
    "dockerfile",
    "makefile",
}

NONFUNCTIONAL_TRACKED_FILE_EXCLUSIONS = {
    ".github/CODE_OF_CONDUCT.md": {
        "kind": "community-documentation",
        "rationale": (
            "Repository conduct policy is not read by the application, "
            "compiler, tests, or packaging tools."
        ),
    },
    ".github/CONTRIBUTING.md": {
        "kind": "community-documentation",
        "rationale": (
            "Contributor guidance is not read by the application, compiler, "
            "tests, or packaging tools."
        ),
    },
    ".github/DEVELOPERS.md": {
        "kind": "developer-documentation",
        "rationale": (
            "Human developer notes are not read by the application, "
            "compiler, tests, or packaging tools."
        ),
    },
    ".github/ISSUE_TEMPLATE/bug-report.md": {
        "kind": "repository-template",
        "rationale": (
            "GitHub issue form prose is not read by the application, "
            "compiler, tests, or packaging tools."
        ),
    },
    ".github/ISSUE_TEMPLATE/contest-request.md": {
        "kind": "repository-template",
        "rationale": (
            "GitHub issue form prose is not read by the application, "
            "compiler, tests, or packaging tools."
        ),
    },
    ".github/ISSUE_TEMPLATE/feature-request.md": {
        "kind": "repository-template",
        "rationale": (
            "GitHub issue form prose is not read by the application, "
            "compiler, tests, or packaging tools."
        ),
    },
    ".github/ISSUE_TEMPLATE/feedback.md": {
        "kind": "repository-template",
        "rationale": (
            "GitHub issue form prose is not read by the application, "
            "compiler, tests, or packaging tools."
        ),
    },
    ".github/ISSUE_TEMPLATE/question-support.md": {
        "kind": "repository-template",
        "rationale": (
            "GitHub issue form prose is not read by the application, "
            "compiler, tests, or packaging tools."
        ),
    },
    ".gitignore": {
        "kind": "repository-metadata",
        "rationale": (
            "Git ignore rules affect only untracked-file presentation and "
            "are not read by the application, compiler, tests, or packaging "
            "tools."
        ),
    },
    "Lazarus/README.md": {
        "kind": "developer-documentation",
        "rationale": (
            "Human Lazarus build notes are not consumed by the build script "
            "or any shipped runtime."
        ),
    },
    "LICENSE.md": {
        "kind": "legal-documentation",
        "rationale": (
            "The legal license text is not read by the application, "
            "compiler, tests, or packaging tools."
        ),
    },
    "README.md": {
        "kind": "repository-documentation",
        "rationale": (
            "Repository landing-page prose is not read by the application, "
            "compiler, tests, or packaging tools."
        ),
    },
}


def mask_pascal_comments(source: str) -> str:
    masked = list(source)
    index = 0
    in_string = False

    while index < len(source):
        if in_string:
            if source[index] == "'":
                if index + 1 < len(source) and source[index + 1] == "'":
                    index += 2
                    continue
                in_string = False
            index += 1
            continue

        if source[index] == "'":
            in_string = True
            index += 1
            continue

        if source.startswith("//", index):
            end = source.find("\n", index)
            end = len(source) if end == -1 else end
            _mask_range(masked, source, index, end)
            index = end
            continue

        if source[index] == "{":
            end = source.find("}", index + 1)
            end = len(source) - 1 if end == -1 else end
            _mask_range(masked, source, index, end + 1)
            index = end + 1
            continue

        if source.startswith("(*", index):
            end = source.find("*)", index + 2)
            end = len(source) - 2 if end == -1 else end
            _mask_range(masked, source, index, end + 2)
            index = end + 2
            continue

        index += 1

    return "".join(masked)


def _mask_range(masked: list[str], source: str, start: int, end: int) -> None:
    for index in range(start, end):
        if source[index] not in "\r\n":
            masked[index] = " "


def line_number(source: str, offset: int) -> int:
    return source.count("\n", 0, offset) + 1


def pascal_string(value: str) -> str:
    if not value.startswith("'") or not value.endswith("'"):
        raise ValueError(f"not a Pascal string literal: {value}")
    return value[1:-1].replace("''", "'")


def slug(value: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", value.lower()).strip("-")


def parse_enum(
    source: str,
    type_name: str,
    category: str,
    id_prefix: str,
    source_name: str = "Ini.pas",
) -> list[dict[str, Any]]:
    match = re.search(
        rf"\b{re.escape(type_name)}\s*=\s*\((?P<values>.*?)\);",
        source,
        flags=re.DOTALL,
    )
    if match is None:
        raise ValueError(f"{type_name} enumeration not found")

    values_text = match.group("values")
    values = [
        value.strip()
        for value in values_text.split(",")
        if value.strip()
    ]
    surfaces: list[dict[str, Any]] = []
    search_offset = 0
    for ordinal, value in enumerate(values):
        value_offset = values_text.find(value, search_offset)
        search_offset = value_offset + len(value)
        surfaces.append(
            {
                "id": f"{id_prefix}.{slug(value)}",
                "category": category,
                "name": value,
                "source": (
                    f"{source_name}:"
                    f"{line_number(source, match.start('values') + value_offset)}"
                ),
                "details": {
                    "type": type_name,
                    "ordinal": ordinal,
                },
            }
        )
    return surfaces


def parse_contest_definitions(source: str) -> list[dict[str, Any]]:
    table_match = re.search(
        (
            r"ContestDefinitions\s*:\s*array\[TSimContest\]\s+of\s+"
            r"TContestDefinition\s*=\s*\((?P<body>.*?)\n\s*\);"
        ),
        source,
        flags=re.DOTALL,
    )
    if table_match is None:
        raise ValueError("ContestDefinitions table not found")

    record_pattern = re.compile(
        (
            r"\(\s*Name:\s*(?P<name>'(?:''|[^'])*')\s*;"
            r"(?P<body>.*?)\bT:\s*(?P<enum>[A-Za-z0-9_]+)\s*\)"
        ),
        flags=re.DOTALL,
    )
    field_pattern = re.compile(
        r"\b(?P<name>Key|ExchType1|ExchType2|ExchFieldEditable|ExchDefault)"
        r"\s*:\s*(?P<value>'(?:''|[^'])*'|[A-Za-z0-9_]+)\s*;",
        flags=re.DOTALL,
    )
    field_names = {
        "Key": "key",
        "ExchType1": "exchangeType1",
        "ExchType2": "exchangeType2",
        "ExchFieldEditable": "exchangeFieldEditable",
        "ExchDefault": "exchangeDefault",
    }

    surfaces: list[dict[str, Any]] = []
    for ordinal, record_match in enumerate(
        record_pattern.finditer(table_match.group("body"))
    ):
        fields: dict[str, Any] = {}
        for field_match in field_pattern.finditer(record_match.group("body")):
            value = field_match.group("value")
            fields[field_names[field_match.group("name")]] = (
                pascal_string(value) if value.startswith("'") else value
            )

        key = fields.get("key")
        if not isinstance(key, str):
            raise ValueError("contest definition is missing Key")

        source_offset = table_match.start("body") + record_match.start()
        surfaces.append(
            {
                "id": f"legacy.ini.contest-definition.{slug(key)}",
                "category": "contest-definition",
                "name": pascal_string(record_match.group("name")),
                "source": f"Ini.pas:{line_number(source, source_offset)}",
                "details": {
                    "ordinal": ordinal,
                    "enum": record_match.group("enum"),
                    "key": key,
                    **fields,
                },
            }
        )

    if not surfaces:
        raise ValueError("ContestDefinitions table contains no records")
    return surfaces


def parse_section_names(source: str) -> dict[str, str]:
    return {
        match.group("constant"): pascal_string(match.group("value"))
        for match in re.finditer(
            (
                r"^\s*(?P<constant>SEC_[A-Z]+)\s*=\s*"
                r"(?P<value>'(?:''|[^'])*')\s*;"
            ),
            source,
            flags=re.MULTILINE,
        )
    }


def parse_serial_keys(source: str) -> dict[str, str]:
    type_match = re.search(
        r"TSerialNRTypes\s*=\s*\((?P<values>.*?)\);",
        source,
        flags=re.DOTALL,
    )
    table_match = re.search(
        (
            r"SerialNRSettings\s*:\s*array\[TSerialNRTypes\]\s+of\s+"
            r"TSerialNRSettings\s*=\s*\((?P<body>.*?)\n\s*\);"
        ),
        source,
        flags=re.DOTALL,
    )
    if type_match is None or table_match is None:
        raise ValueError("serial number setting declarations not found")

    enum_values = [
        value.strip()
        for value in type_match.group("values").split(",")
        if value.strip()
    ]
    keys = [
        pascal_string(match.group("key"))
        for match in re.finditer(
            r"\(\s*Key\s*:\s*(?P<key>'(?:''|[^'])*')",
            table_match.group("body"),
        )
    ]
    if len(enum_values) != len(keys):
        raise ValueError("serial number setting keys do not match their enum")
    return dict(zip(enum_values, keys, strict=True))


def parse_persisted_settings(
    source: str,
    contest_definitions: list[dict[str, Any]],
) -> list[dict[str, Any]]:
    section_names = parse_section_names(source)
    serial_keys = parse_serial_keys(source)
    contest_exchange_keys = [
        f"{surface['details']['key']}Exchange"
        for surface in contest_definitions
    ]
    method_pattern = re.compile(
        (
            r"\b(?P<method>ReadString|ReadInteger|ReadBool|WriteString|"
            r"WriteInteger|WriteBool|DeleteKey|ValueExists)\s*\(\s*"
            r"(?P<section>SEC_[A-Z]+)\s*,\s*"
            r"(?P<key>"
            r"'(?:''|[^'])*'|"
            r"KeyName|"
            r"pRange\.Key|"
            r"Ini\.SerialNRSettings\[(?P<serial>[A-Za-z0-9_]+)\]\.Key"
            r")"
        ),
        flags=re.DOTALL,
    )
    operation_names = {
        "ReadString": "read",
        "ReadInteger": "read",
        "ReadBool": "read",
        "WriteString": "write",
        "WriteInteger": "write",
        "WriteBool": "write",
        "DeleteKey": "delete",
        "ValueExists": "exists",
    }
    discovered: dict[tuple[str, str], dict[str, Any]] = defaultdict(
        lambda: {"operations": set(), "sources": set()}
    )

    for match in method_pattern.finditer(source):
        section_constant = match.group("section")
        section = section_names.get(section_constant)
        if section is None:
            raise ValueError(f"unknown INI section constant: {section_constant}")

        key_expression = match.group("key")
        if key_expression.startswith("'"):
            keys = [pascal_string(key_expression)]
        elif key_expression == "KeyName":
            keys = contest_exchange_keys
        elif key_expression == "pRange.Key":
            keys = [
                serial_keys[name]
                for name in ("snMidContest", "snEndContest", "snCustomRange")
            ]
        else:
            serial_name = match.group("serial")
            if serial_name not in serial_keys:
                raise ValueError(f"unknown serial setting key: {serial_name}")
            keys = [serial_keys[serial_name]]

        source_reference = f"Ini.pas:{line_number(source, match.start())}"
        for key in keys:
            entry = discovered[(section, key)]
            entry["operations"].add(operation_names[match.group("method")])
            entry["sources"].add(source_reference)

    surfaces: list[dict[str, Any]] = []
    for (section, key), entry in sorted(
        discovered.items(),
        key=lambda item: (item[0][0].lower(), item[0][1].lower()),
    ):
        surfaces.append(
            {
                "id": f"legacy.ini.setting.{slug(section)}.{slug(key)}",
                "category": "persisted-setting",
                "name": f"[{section}] {key}",
                "source": sorted(
                    entry["sources"],
                    key=lambda value: int(value.split(":")[1]),
                )[0],
                "details": {
                    "section": section,
                    "key": key,
                    "operations": sorted(entry["operations"]),
                    "sources": sorted(
                        entry["sources"],
                        key=lambda value: int(value.split(":")[1]),
                    ),
                },
            }
        )
    return surfaces


def dfm_string(value: str) -> str:
    parts = re.findall(r"'(?:''|[^'])*'|#\d+", value)
    if not parts:
        return value.strip()

    decoded: list[str] = []
    for part in parts:
        if part.startswith("'"):
            decoded.append(pascal_string(part))
        else:
            decoded.append(chr(int(part[1:])))
    return "".join(decoded)


def parse_main_dfm(
    source: str,
) -> tuple[
    list[dict[str, Any]],
    list[dict[str, Any]],
    list[dict[str, Any]],
    set[str],
]:
    object_pattern = re.compile(
        r"^(?P<indent>\s*)(?:object|inherited)\s+"
        r"(?P<name>[A-Za-z0-9_]+)\s*:\s*(?P<type>[A-Za-z0-9_.]+)",
        flags=re.MULTILINE,
    )
    property_pattern = re.compile(
        r"^(?P<indent>\s*)(?P<name>[A-Za-z][A-Za-z0-9_]*)\s*=\s*(?P<value>.+)$"
    )
    lines = source.splitlines()
    objects: list[dict[str, Any]] = []
    stack: list[dict[str, Any]] = []

    for line_index, line in enumerate(lines, start=1):
        object_match = object_pattern.match(line)
        if object_match is not None:
            indent = len(object_match.group("indent"))
            while stack and stack[-1]["indent"] >= indent:
                stack.pop()
            entry: dict[str, Any] = {
                "name": object_match.group("name"),
                "type": object_match.group("type"),
                "line": line_index,
                "indent": indent,
                "parent": stack[-1]["name"] if stack else None,
                "properties": {},
                "propertyLines": {},
            }
            objects.append(entry)
            stack.append(entry)
            continue

        property_match = property_pattern.match(line)
        if property_match is None or not stack:
            continue
        indent = len(property_match.group("indent"))
        while stack and stack[-1]["indent"] >= indent:
            stack.pop()
        if not stack:
            continue
        owner = stack[-1]
        if indent != owner["indent"] + 2:
            continue
        property_name = property_match.group("name")
        owner["properties"][property_name] = property_match.group("value").strip()
        owner["propertyLines"][property_name] = line_index

    object_surfaces: list[dict[str, Any]] = []
    event_surfaces: list[dict[str, Any]] = []
    shortcut_surfaces: list[dict[str, Any]] = []
    handlers: set[str] = set()
    for entry in objects:
        name = entry["name"]
        object_type = entry["type"]
        properties = entry["properties"]
        is_menu_item = object_type == "TMenuItem"
        category = "main-menu-item" if is_menu_item else "main-form-object"
        id_kind = "menu-item" if is_menu_item else "object"
        details: dict[str, Any] = {
            "objectName": name,
            "objectType": object_type,
            "parent": entry["parent"],
        }
        for property_name in (
            "Caption",
            "Hint",
            "TabOrder",
            "TabStop",
            "Visible",
            "Enabled",
            "Tag",
        ):
            if property_name in properties:
                raw_value = properties[property_name]
                details[property_name[0].lower() + property_name[1:]] = (
                    dfm_string(raw_value)
                    if property_name in {"Caption", "Hint"}
                    else raw_value
                )

        object_surfaces.append(
            {
                "id": f"legacy.main.{id_kind}.{slug(name)}",
                "category": category,
                "name": details.get("caption") or name,
                "source": f"Main.dfm:{entry['line']}",
                "details": details,
            }
        )

        for property_name, raw_value in properties.items():
            if not re.fullmatch(r"On[A-Za-z0-9_]+", property_name):
                continue
            handler = raw_value.strip()
            handlers.add(handler)
            event_surfaces.append(
                {
                    "id": (
                        "legacy.main.event-binding."
                        f"{slug(name)}.{slug(property_name)}"
                    ),
                    "category": "main-event-binding",
                    "name": f"{name}.{property_name}",
                    "source": (
                        f"Main.dfm:{entry['propertyLines'][property_name]}"
                    ),
                    "details": {
                        "objectName": name,
                        "event": property_name,
                        "handler": handler,
                    },
                }
            )

        if "ShortCut" in properties:
            raw_shortcut = properties["ShortCut"]
            shortcut_surfaces.append(
                {
                    "id": f"legacy.main.shortcut.dfm.{slug(name)}",
                    "category": "keyboard-shortcut",
                    "name": f"{name}: {decode_shortcut(raw_shortcut)}",
                    "source": f"Main.dfm:{entry['propertyLines']['ShortCut']}",
                    "details": {
                        "objectName": name,
                        "rawValue": raw_shortcut,
                        "display": decode_shortcut(raw_shortcut),
                    },
                }
            )

    return object_surfaces, event_surfaces, shortcut_surfaces, handlers


def parse_form_resource(
    source: str,
    source_name: str,
) -> list[dict[str, Any]]:
    object_pattern = re.compile(
        r"^(?P<indent>\s*)(?:object|inherited|inline)\s+"
        r"(?P<name>[A-Za-z0-9_]+)\s*:\s*(?P<type>[A-Za-z0-9_.]+)",
        flags=re.MULTILINE,
    )
    property_pattern = re.compile(
        r"^(?P<indent>\s*)(?P<name>[A-Za-z][A-Za-z0-9_]*)\s*=\s*(?P<value>.+)$"
    )
    lines = source.splitlines()
    objects: list[dict[str, Any]] = []
    stack: list[dict[str, Any]] = []

    for line_index, line in enumerate(lines, start=1):
        object_match = object_pattern.match(line)
        if object_match is not None:
            indent = len(object_match.group("indent"))
            while stack and stack[-1]["indent"] >= indent:
                stack.pop()
            entry: dict[str, Any] = {
                "name": object_match.group("name"),
                "type": object_match.group("type"),
                "line": line_index,
                "indent": indent,
                "parent": stack[-1]["name"] if stack else None,
                "properties": {},
                "propertyLines": {},
            }
            objects.append(entry)
            stack.append(entry)
            continue

        property_match = property_pattern.match(line)
        if property_match is None or not stack:
            continue
        indent = len(property_match.group("indent"))
        while stack and stack[-1]["indent"] >= indent:
            stack.pop()
        if not stack:
            continue
        owner = stack[-1]
        if indent != owner["indent"] + 2:
            continue
        property_name = property_match.group("name")
        owner["properties"][property_name] = property_match.group("value").strip()
        owner["propertyLines"][property_name] = line_index

    resource_path = Path(source_name)
    resource_format = resource_path.suffix.lower().lstrip(".")
    resource_slug = slug(resource_path.with_suffix("").as_posix())
    id_root = f"legacy.form.{resource_format}.{resource_slug}"
    root_name = objects[0]["name"] if objects else resource_path.stem
    surfaces: list[dict[str, Any]] = []

    for entry in objects:
        properties = entry["properties"]
        details: dict[str, Any] = {
            "form": root_name,
            "resource": source_name,
            "resourceFormat": resource_format,
            "objectName": entry["name"],
            "objectType": entry["type"],
            "parent": entry["parent"],
        }
        for property_name in (
            "Caption",
            "Hint",
            "TabOrder",
            "TabStop",
            "Visible",
            "Enabled",
            "ReadOnly",
            "Default",
            "ModalResult",
            "Tag",
        ):
            if property_name not in properties:
                continue
            raw_value = properties[property_name]
            details[property_name[0].lower() + property_name[1:]] = (
                dfm_string(raw_value)
                if property_name in {"Caption", "Hint"}
                else raw_value
            )

        object_name = entry["name"]
        surfaces.append(
            {
                "id": f"{id_root}.object.{slug(object_name)}",
                "category": "form-object",
                "name": details.get("caption") or object_name,
                "source": f"{source_name}:{entry['line']}",
                "details": details,
            }
        )

        for property_name, raw_value in properties.items():
            if not re.fullmatch(r"On[A-Za-z0-9_]+", property_name):
                continue
            surfaces.append(
                {
                    "id": (
                        f"{id_root}.event-binding.{slug(object_name)}."
                        f"{slug(property_name)}"
                    ),
                    "category": "form-event-binding",
                    "name": f"{object_name}.{property_name}",
                    "source": (
                        f"{source_name}:"
                        f"{entry['propertyLines'][property_name]}"
                    ),
                    "details": {
                        "form": root_name,
                        "resource": source_name,
                        "resourceFormat": resource_format,
                        "objectName": object_name,
                        "event": property_name,
                        "handler": raw_value.strip(),
                    },
                }
            )

        if "ShortCut" in properties:
            raw_shortcut = properties["ShortCut"]
            surfaces.append(
                {
                    "id": f"{id_root}.shortcut.{slug(object_name)}",
                    "category": "form-keyboard-shortcut",
                    "name": (
                        f"{object_name}: {decode_shortcut(raw_shortcut)}"
                    ),
                    "source": (
                        f"{source_name}:"
                        f"{entry['propertyLines']['ShortCut']}"
                    ),
                    "details": {
                        "form": root_name,
                        "resource": source_name,
                        "resourceFormat": resource_format,
                        "objectName": object_name,
                        "rawValue": raw_shortcut,
                        "display": decode_shortcut(raw_shortcut),
                    },
                }
            )

    return surfaces


def decode_shortcut(raw_value: str) -> str:
    try:
        value = int(raw_value)
    except ValueError:
        return raw_value

    modifiers: list[str] = []
    if value & 0x2000:
        modifiers.append("Shift")
    if value & 0x4000:
        modifiers.append("Ctrl")
    if value & 0x8000:
        modifiers.append("Alt")

    key_code = value & 0xFF
    if 112 <= key_code <= 123:
        key = f"F{key_code - 111}"
    elif key_code == 13:
        key = "Enter"
    elif 32 <= key_code <= 126:
        key = chr(key_code).upper()
    else:
        key = f"VK_{key_code}"
    return "+".join(modifiers + [key])


def find_main_form_methods(source: str) -> dict[str, tuple[int, str]]:
    method_pattern = re.compile(
        r"^(?:procedure|function)\s+TMainForm\."
        r"(?P<name>[A-Za-z0-9_]+)\b.*?(?=^(?:procedure|function)\s+"
        r"TMainForm\.|\Z)",
        flags=re.MULTILINE | re.DOTALL,
    )
    return {
        match.group("name"): (
            line_number(source, match.start()),
            match.group(0),
        )
        for match in method_pattern.finditer(source)
    }


def parse_main_handlers(
    source: str,
    bound_handlers: set[str],
) -> list[dict[str, Any]]:
    methods = find_main_form_methods(source)
    surfaces: list[dict[str, Any]] = []
    for handler in sorted(bound_handlers, key=str.lower):
        implementation = methods.get(handler)
        surfaces.append(
            {
                "id": f"legacy.main.handler.{slug(handler)}",
                "category": "main-event-handler",
                "name": handler,
                "source": (
                    f"Main.pas:{implementation[0]}"
                    if implementation is not None
                    else "Main.dfm"
                ),
                "details": {
                    "handler": handler,
                    "implementationFound": implementation is not None,
                },
            }
        )
    return surfaces


def parse_main_keyboard_branches(source: str) -> list[dict[str, Any]]:
    methods = find_main_form_methods(source)
    keyboard_methods = {
        "Edit1KeyPress",
        "Edit2KeyPress",
        "Edit3KeyPress",
        "Edit3KeyUp",
        "FormKeyPress",
        "FormKeyDown",
        "FormKeyUp",
    }
    token_pattern = re.compile(
        r"\bVK_[A-Z0-9_]+\b|"
        r"^\s*(?P<label>"
        r"(?:(?:#\d+|'(?:''|[^'])*'|\d+)(?:\s*,\s*)?)+"
        r")\s*:",
        flags=re.MULTILINE,
    )
    grouped: dict[tuple[str, str], set[str]] = defaultdict(set)

    for method_name in sorted(keyboard_methods):
        implementation = methods.get(method_name)
        if implementation is None:
            continue
        method_line, body = implementation
        for match in token_pattern.finditer(body):
            if match.group("label") is not None:
                tokens = re.findall(
                    r"#\d+|'(?:''|[^'])*'|\d+",
                    match.group("label"),
                )
            else:
                tokens = [match.group(0)]
            token_line = method_line + body.count("\n", 0, match.start())
            for token in tokens:
                grouped[(method_name, token)].add(f"Main.pas:{token_line}")

    surfaces: list[dict[str, Any]] = []
    for (method_name, token), sources in sorted(
        grouped.items(),
        key=lambda item: (item[0][0].lower(), item[0][1].lower()),
    ):
        ordered_sources = sorted(
            sources,
            key=lambda value: int(value.split(":")[1]),
        )
        surfaces.append(
            {
                "id": (
                    "legacy.main.shortcut.code."
                    f"{slug(method_name)}.{keyboard_token_id(token)}"
                ),
                "category": "keyboard-branch",
                "name": f"{method_name}: {token}",
                "source": ordered_sources[0],
                "details": {
                    "handler": method_name,
                    "token": token,
                    "sources": ordered_sources,
                },
            }
        )
    return surfaces


def keyboard_token_id(token: str) -> str:
    if token.startswith("VK_"):
        return slug(token)
    if token.startswith("#"):
        return f"char-code-{int(token[1:])}"
    if token.startswith("'"):
        value = pascal_string(token)
        code_points = "-".join(f"u{ord(character):04x}" for character in value)
        return f"char-{code_points}"
    return f"key-code-{int(token)}"


def parse_record_fields(
    source: str,
    record_name: str,
    id_prefix: str,
    category: str,
) -> list[dict[str, Any]]:
    record_match = re.search(
        rf"\b{re.escape(record_name)}\s*=\s*record(?P<body>.*?)^\s*end\s*;",
        source,
        flags=re.MULTILINE | re.DOTALL,
    )
    if record_match is None:
        raise ValueError(f"{record_name} record not found")

    field_pattern = re.compile(
        r"^\s*(?P<names>[A-Za-z_][A-Za-z0-9_, ]*)\s*:\s*"
        r"(?P<type>[^;()\n]+)\s*;",
        flags=re.MULTILINE,
    )
    surfaces: list[dict[str, Any]] = []
    for match in field_pattern.finditer(record_match.group("body")):
        field_type = match.group("type").strip()
        source_line = line_number(
            source,
            record_match.start("body") + match.start(),
        )
        for field_name in (
            value.strip() for value in match.group("names").split(",")
        ):
            surfaces.append(
                {
                    "id": f"{id_prefix}.{slug(field_name)}",
                    "category": category,
                    "name": field_name,
                    "source": f"Log.pas:{source_line}",
                    "details": {
                        "record": record_name,
                        "field": field_name,
                        "type": field_type,
                    },
                }
            )
    return surfaces


def parse_log_routines(source: str) -> list[dict[str, Any]]:
    implementation_match = re.search(r"^implementation\s*$", source, re.MULTILINE)
    if implementation_match is None:
        raise ValueError("Log.pas implementation section not found")
    implementation = source[implementation_match.end() :]
    base_offset = implementation_match.end()
    routine_pattern = re.compile(
        r"^(?P<kind>procedure|function|constructor|destructor)\s+"
        r"(?:(?P<owner>[A-Za-z0-9_]+)\.)?"
        r"(?P<name>[A-Za-z0-9_]+)\b(?P<signature>[^;]*);",
        flags=re.MULTILINE | re.IGNORECASE,
    )
    grouped: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
    for match in routine_pattern.finditer(implementation):
        owner = match.group("owner") or "Log"
        grouped[(owner, match.group("name"))].append(
            {
                "kind": match.group("kind").lower(),
                "signature": " ".join(match.group("signature").split()),
                "source": (
                    f"Log.pas:"
                    f"{line_number(source, base_offset + match.start())}"
                ),
            }
        )

    surfaces: list[dict[str, Any]] = []
    for (owner, name), declarations in sorted(
        grouped.items(),
        key=lambda item: (item[0][0].lower(), item[0][1].lower()),
    ):
        surfaces.append(
            {
                "id": f"legacy.log.routine.{slug(owner)}.{slug(name)}",
                "category": "log-routine",
                "name": f"{owner}.{name}",
                "source": declarations[0]["source"],
                "details": {
                    "owner": owner,
                    "routine": name,
                    "declarations": declarations,
                },
            }
        )
    return surfaces


def parse_score_columns(source: str) -> list[dict[str, Any]]:
    surfaces: list[dict[str, Any]] = []
    pattern = re.compile(
        r"^\s*(?P<name>[A-Z][A-Z0-9_]*_COL)\s*=\s*"
        r"(?P<value>'(?:''|[^'])*')\s*;",
        flags=re.MULTILINE,
    )
    for match in pattern.finditer(source):
        surfaces.append(
            {
                "id": f"legacy.log.score-column.{slug(match.group('name'))}",
                "category": "score-column",
                "name": match.group("name"),
                "source": f"Log.pas:{line_number(source, match.start())}",
                "details": {
                    "definition": pascal_string(match.group("value")),
                },
            }
        )
    return surfaces


def parse_log_surfaces(source: str) -> list[dict[str, Any]]:
    return (
        parse_enum(
            source,
            "TLogError",
            "log-error-code",
            "legacy.log.error",
            "Log.pas",
        )
        + parse_record_fields(
            source,
            "TQso",
            "legacy.log.qso-field",
            "qso-field",
        )
        + parse_log_routines(source)
        + parse_score_columns(source)
    )


def parse_all_unit_enums(
    source: str,
    source_name: str,
    id_root: str = "legacy.sim.enum",
    category: str = "simulation-enum",
) -> list[dict[str, Any]]:
    pattern = re.compile(
        r"\b(?P<type>T[A-Za-z0-9_]+)\s*=\s*"
        r"\((?P<values>.*?)\);",
        flags=re.DOTALL,
    )
    surfaces: list[dict[str, Any]] = []
    unit_slug = slug(Path(source_name).stem)
    for type_match in pattern.finditer(source):
        values_text = type_match.group("values")
        values = [
            value.strip()
            for value in values_text.split(",")
            if re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", value.strip())
        ]
        if not values:
            continue
        search_offset = 0
        for ordinal, value in enumerate(values):
            value_offset = values_text.find(value, search_offset)
            search_offset = value_offset + len(value)
            surfaces.append(
                {
                    "id": (
                        f"{id_root}."
                        f"{unit_slug}.{slug(type_match.group('type'))}.{slug(value)}"
                    ),
                    "category": category,
                    "name": f"{type_match.group('type')}.{value}",
                    "source": (
                        f"{source_name}:"
                        f"{line_number(source, type_match.start('values') + value_offset)}"
                    ),
                    "details": {
                        "type": type_match.group("type"),
                        "value": value,
                        "ordinal": ordinal,
                    },
                }
            )
    return surfaces


def find_unit_routine_matches(
    source: str,
) -> list[re.Match[str]]:
    implementation_match = re.search(r"^implementation\s*$", source, re.MULTILINE)
    if implementation_match is None:
        raise ValueError("unit implementation section not found")
    pattern = re.compile(
        r"^(?P<kind>procedure|function|constructor|destructor)\s+"
        r"(?:(?P<owner>[A-Za-z0-9_]+)\.)?"
        r"(?P<name>[A-Za-z0-9_]+)\b(?P<signature>[^;]*);"
        r"(?P<body>.*?)(?=^(?:procedure|function|constructor|destructor)\s+|\Z)",
        flags=re.MULTILINE | re.DOTALL | re.IGNORECASE,
    )
    return list(pattern.finditer(source, implementation_match.end()))


def parse_simulation_routines(
    source: str,
    source_name: str,
) -> list[dict[str, Any]]:
    unit_name = Path(source_name).stem
    unit_slug = slug(unit_name)
    grouped: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
    for match in find_unit_routine_matches(source):
        owner = match.group("owner") or unit_name
        grouped[(owner, match.group("name"))].append(
            {
                "kind": match.group("kind").lower(),
                "signature": " ".join(match.group("signature").split()),
                "source": f"{source_name}:{line_number(source, match.start())}",
            }
        )

    surfaces: list[dict[str, Any]] = []
    for (owner, name), declarations in sorted(
        grouped.items(),
        key=lambda item: (item[0][0].lower(), item[0][1].lower()),
    ):
        surfaces.append(
            {
                "id": (
                    "legacy.sim.routine."
                    f"{unit_slug}.{slug(owner)}.{slug(name)}"
                ),
                "category": "simulation-routine",
                "name": f"{owner}.{name}",
                "source": declarations[0]["source"],
                "details": {
                    "unit": unit_name,
                    "owner": owner,
                    "routine": name,
                    "declarations": declarations,
                },
            }
        )
    return surfaces


def parse_state_transitions(
    source: str,
    source_name: str,
) -> list[dict[str, Any]]:
    unit_slug = slug(Path(source_name).stem)
    transition_pattern = re.compile(
        r"\b(?:(?:Self\.)?State\s*:=\s*|SetState\s*\(\s*)"
        r"(?P<state>(?:os|st)[A-Za-z0-9_]+)",
        flags=re.IGNORECASE,
    )
    grouped: dict[tuple[str, str, str], set[str]] = defaultdict(set)

    for routine in find_unit_routine_matches(source):
        owner = routine.group("owner") or Path(source_name).stem
        routine_name = routine.group("name")
        body = routine.group("body")
        for transition in transition_pattern.finditer(body):
            state = transition.group("state")
            source_line = line_number(
                source,
                routine.start("body") + transition.start(),
            )
            grouped[(owner, routine_name, state)].add(
                f"{source_name}:{source_line}"
            )

    surfaces: list[dict[str, Any]] = []
    for (owner, routine_name, state), sources in sorted(
        grouped.items(),
        key=lambda item: (
            item[0][0].lower(),
            item[0][1].lower(),
            item[0][2].lower(),
        ),
    ):
        ordered_sources = sorted(
            sources,
            key=lambda value: int(value.split(":")[1]),
        )
        surfaces.append(
            {
                "id": (
                    "legacy.sim.transition."
                    f"{unit_slug}.{slug(owner)}.{slug(routine_name)}.{slug(state)}"
                ),
                "category": "state-transition",
                "name": f"{owner}.{routine_name} -> {state}",
                "source": ordered_sources[0],
                "details": {
                    "unit": Path(source_name).stem,
                    "owner": owner,
                    "routine": routine_name,
                    "targetState": state,
                    "sources": ordered_sources,
                },
            }
        )
    return surfaces


def parse_simulation_units(legacy_root: Path) -> list[dict[str, Any]]:
    source_names = [
        "Contest.pas",
        "Station.pas",
        "DxOper.pas",
        "DxStn.pas",
        "StnColl.pas",
        "MyStn.pas",
        "QrmStn.pas",
        "QrnStn.pas",
    ]
    surfaces: list[dict[str, Any]] = []
    for source_name in source_names:
        path = legacy_root / source_name
        if not path.is_file():
            raise ValueError(f"legacy simulation source not found: {path}")
        source = mask_pascal_comments(path.read_text(encoding="utf-8-sig"))
        surfaces.extend(parse_all_unit_enums(source, source_name))
        surfaces.extend(parse_simulation_routines(source, source_name))
        surfaces.extend(parse_state_transitions(source, source_name))
    return surfaces


def parse_unit_declarations(
    source: str,
    source_name: str,
    id_root: str,
    category_root: str,
) -> list[dict[str, Any]]:
    unit_name = Path(source_name).stem
    unit_slug = slug(unit_name)
    surfaces: list[dict[str, Any]] = []

    type_pattern = re.compile(
        r"^\s*(?P<name>[TEP][A-Za-z0-9_]+)\s*=\s*"
        r"(?P<definition>[^;\r\n]+)",
        flags=re.MULTILINE,
    )
    grouped_types: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for match in type_pattern.finditer(source):
        grouped_types[match.group("name")].append(
            {
                "source": f"{source_name}:{line_number(source, match.start())}",
                "definition": " ".join(match.group("definition").split()),
            }
        )
    for type_name, declarations in sorted(
        grouped_types.items(),
        key=lambda item: item[0].lower(),
    ):
        surfaces.append(
            {
                "id": (
                    f"{id_root}.type."
                    f"{unit_slug}.{slug(type_name)}"
                ),
                "category": f"{category_root}-type",
                "name": f"{unit_name}.{type_name}",
                "source": declarations[0]["source"],
                "details": {
                    "unit": unit_name,
                    "type": type_name,
                    "declarations": declarations,
                },
            }
        )

    constant_pattern = re.compile(
        r"^\s*(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*"
        r"(?::\s*[^=\r\n]+)?=\s*(?P<value>[^\r\n]+)",
        flags=re.MULTILINE,
    )
    interface_end = re.search(r"^implementation\s*$", source, re.MULTILINE)
    interface_source = (
        source[: interface_end.start()]
        if interface_end is not None
        else source
    )
    const_section_pattern = re.compile(
        r"^const\s*$"
        r"(?P<body>.*?)"
        r"(?=^(?:type|var|threadvar|procedure|function|implementation)\b|\Z)",
        flags=re.MULTILINE | re.DOTALL | re.IGNORECASE,
    )
    for section in const_section_pattern.finditer(interface_source):
        for match in constant_pattern.finditer(section.group("body")):
            name = match.group("name")
            absolute_start = section.start("body") + match.start()
            surfaces.append(
                {
                    "id": (
                        f"{id_root}.constant."
                        f"{unit_slug}.{slug(name)}"
                    ),
                    "category": f"{category_root}-constant",
                    "name": f"{unit_name}.{name}",
                    "source": (
                        f"{source_name}:"
                        f"{line_number(interface_source, absolute_start)}"
                    ),
                    "details": {
                        "unit": unit_name,
                        "constant": name,
                        "definition": " ".join(match.group("value").split()),
                    },
                }
            )

    property_pattern = re.compile(
        r"^\s*property\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)\b"
        r"(?P<definition>[^;]*);",
        flags=re.MULTILINE | re.IGNORECASE,
    )
    class_pattern = re.compile(
        r"^\s*(?P<owner>T[A-Za-z0-9_]+)\s*=\s*class\b[^\r\n]*"
        r"(?P<body>.*?)^\s*end;",
        flags=re.MULTILINE | re.DOTALL | re.IGNORECASE,
    )
    for class_match in class_pattern.finditer(interface_source):
        owner = class_match.group("owner")
        for match in property_pattern.finditer(class_match.group("body")):
            absolute_start = class_match.start("body") + match.start()
            surfaces.append(
                {
                    "id": (
                        f"{id_root}.property."
                        f"{unit_slug}.{slug(owner)}.{slug(match.group('name'))}"
                    ),
                    "category": f"{category_root}-property",
                    "name": f"{owner}.{match.group('name')}",
                    "source": (
                        f"{source_name}:"
                        f"{line_number(interface_source, absolute_start)}"
                    ),
                    "details": {
                        "unit": unit_name,
                        "owner": owner,
                        "property": match.group("name"),
                        "definition": " ".join(match.group("definition").split()),
                    },
                }
            )
    return surfaces


def parse_unit_routines(
    source: str,
    source_name: str,
    id_root: str,
    category_root: str,
) -> list[dict[str, Any]]:
    unit_name = Path(source_name).stem
    unit_slug = slug(unit_name)
    grouped: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
    for match in find_unit_routine_matches(source):
        owner = match.group("owner") or unit_name
        grouped[(owner, match.group("name"))].append(
            {
                "kind": match.group("kind").lower(),
                "signature": " ".join(match.group("signature").split()),
                "source": f"{source_name}:{line_number(source, match.start())}",
            }
        )

    surfaces: list[dict[str, Any]] = []
    for (owner, name), declarations in sorted(
        grouped.items(),
        key=lambda item: (item[0][0].lower(), item[0][1].lower()),
    ):
        surfaces.append(
            {
                "id": (
                    f"{id_root}.routine."
                    f"{unit_slug}.{slug(owner)}.{slug(name)}"
                ),
                "category": f"{category_root}-routine",
                "name": f"{owner}.{name}",
                "source": declarations[0]["source"],
                "details": {
                    "unit": unit_name,
                    "owner": owner,
                    "routine": name,
                    "declarations": declarations,
                },
            }
        )
    return surfaces


def parse_vcl_units(legacy_root: Path) -> tuple[list[dict[str, Any]], list[str]]:
    units_by_concern = {
        "dsp": [
            "Crc32.pas",
            "FarnsKeyer.pas",
            "Mixers.pas",
            "MorseKey.pas",
            "MorseTbl.pas",
            "MovAvg.pas",
            "QuickAvg.pas",
            "SndTypes.pas",
            "VolumCtl.pas",
        ],
        "adapter": [
            "BaseComp.pas",
            "SndCustm.pas",
            "SndOut.pas",
            "WavFile.pas",
        ],
        "ui": [
            "PermHint.pas",
            "VolmSldr.pas",
        ],
    }
    surfaces: list[dict[str, Any]] = []
    sources: list[str] = []
    for concern, source_names in units_by_concern.items():
        for file_name in source_names:
            relative_name = f"VCL/{file_name}"
            path = legacy_root / "VCL" / file_name
            if not path.is_file():
                raise ValueError(f"legacy VCL source not found: {path}")
            source = mask_pascal_comments(path.read_text(encoding="utf-8-sig"))
            surfaces.extend(
                parse_unit_declarations(
                    source,
                    relative_name,
                    f"legacy.vcl.{concern}",
                    f"vcl-{concern}",
                )
            )
            surfaces.extend(
                parse_all_unit_enums(
                    source,
                    relative_name,
                    f"legacy.vcl.{concern}.enum",
                    f"vcl-{concern}-enum",
                )
            )
            surfaces.extend(
                parse_unit_routines(
                    source,
                    relative_name,
                    f"legacy.vcl.{concern}",
                    f"vcl-{concern}",
                )
            )
            sources.append(relative_name)
    return surfaces, sources


def parse_support_units(
    legacy_root: Path,
) -> tuple[list[dict[str, Any]], list[str], dict[str, str]]:
    units_by_concern = {
        "contest": [
            "ACAG.pas",
            "ALLJA.pas",
            "ArrlDx.pas",
            "ArrlFd.pas",
            "ArrlSS.pas",
            "CqWpx.pas",
            "CqWW.pas",
            "CWOPS.pas",
            "CWSST.pas",
            "DualExchContest.pas",
            "IaruHf.pas",
            "NaQp.pas",
        ],
        "data": [
            "CallLst.pas",
            "DXCC.pas",
            "ExchFields.pas",
            "SerNRGen.pas",
            "Util/ArrlSections.pas",
            "Util/CallsignUtils.pas",
            "Util/Lexer.pas",
            "Util/SSExchParser.pas",
        ],
        "effect": [
            "Qsb.pas",
            "RndFunc.pas",
        ],
        "ui": [
            "ScoreDlg.pas",
        ],
    }
    surfaces: list[dict[str, Any]] = []
    sources: list[str] = []
    source_texts: dict[str, str] = {}
    for concern, source_names in units_by_concern.items():
        for source_name in source_names:
            path = legacy_root / Path(source_name)
            if not path.is_file():
                raise ValueError(f"legacy support source not found: {path}")
            source = mask_pascal_comments(path.read_text(encoding="utf-8-sig"))
            surfaces.extend(
                parse_unit_declarations(
                    source,
                    source_name,
                    f"legacy.support.{concern}",
                    f"support-{concern}",
                )
            )
            surfaces.extend(
                parse_all_unit_enums(
                    source,
                    source_name,
                    f"legacy.support.{concern}.enum",
                    f"support-{concern}-enum",
                )
            )
            surfaces.extend(
                parse_unit_routines(
                    source,
                    source_name,
                    f"legacy.support.{concern}",
                    f"support-{concern}",
                )
            )
            sources.append(source_name)
            source_texts[source_name] = source
    return surfaces, sources, source_texts


def parse_interface_type_declarations(
    source: str,
    source_name: str,
    id_root: str,
    category: str,
) -> list[dict[str, Any]]:
    implementation_match = re.search(
        r"^implementation\s*$",
        source,
        flags=re.MULTILINE | re.IGNORECASE,
    )
    interface_source = (
        source[: implementation_match.start()]
        if implementation_match is not None
        else source
    )
    type_section_pattern = re.compile(
        r"^type\s*$"
        r"(?P<body>.*?)"
        r"(?=^(?:const|var|threadvar|resourcestring|procedure|function|"
        r"implementation)\b|\Z)",
        flags=re.MULTILINE | re.DOTALL | re.IGNORECASE,
    )
    declaration_pattern = re.compile(
        r"^\s*(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*"
        r"(?P<definition>[^\r\n]+)",
        flags=re.MULTILINE,
    )
    grouped: dict[str, list[dict[str, str]]] = defaultdict(list)
    for section in type_section_pattern.finditer(interface_source):
        for match in declaration_pattern.finditer(section.group("body")):
            absolute_start = section.start("body") + match.start()
            grouped[match.group("name")].append(
                {
                    "source": (
                        f"{source_name}:"
                        f"{line_number(interface_source, absolute_start)}"
                    ),
                    "definition": " ".join(
                        match.group("definition").split()
                    ),
                }
            )

    surfaces: list[dict[str, Any]] = []
    source_slug = slug(source_name)
    for type_name, declarations in sorted(
        grouped.items(),
        key=lambda item: item[0].casefold(),
    ):
        surfaces.append(
            {
                "id": (
                    f"{id_root}.type.{source_slug}.{slug(type_name)}"
                ),
                "category": category,
                "name": f"{Path(source_name).stem}.{type_name}",
                "source": declarations[0]["source"],
                "details": {
                    "unit": Path(source_name).stem,
                    "type": type_name,
                    "declarations": declarations,
                },
            }
        )
    return surfaces


def parse_regex_units(
    legacy_root: Path,
    tracked_files: list[str],
) -> tuple[list[dict[str, Any]], list[str]]:
    regex_units = {
        "Lazarus/PerlRegEx.pas": "lazarus-wrapper",
        "PerlRegEx/PerlRegEx.pas": "delphi-wrapper",
        "PerlRegEx/pcre.pas": "pcre-binding",
    }
    tracked_lookup = {
        name.replace("\\", "/").casefold(): name
        for name in tracked_files
    }
    surfaces: list[dict[str, Any]] = []
    sources: list[str] = []
    for expected_name, concern in regex_units.items():
        source_name = tracked_lookup.get(expected_name.casefold())
        if source_name is None:
            raise ValueError(
                f"legacy regular-expression source not tracked: {expected_name}"
            )
        path = legacy_root / Path(source_name)
        source = mask_pascal_comments(
            path.read_text(encoding="utf-8-sig")
        )
        id_root = f"legacy.data.parser.regex.{concern}"
        surfaces.extend(
            parse_interface_type_declarations(
                source,
                source_name,
                id_root,
                "third-party-regex-type",
            )
        )
        surfaces.extend(
            surface
            for surface in parse_unit_declarations(
                source,
                source_name,
                id_root,
                "third-party-regex",
            )
            if surface["category"] != "third-party-regex-type"
        )
        surfaces.extend(
            parse_all_unit_enums(
                source,
                source_name,
                f"{id_root}.enum",
                "third-party-regex-enum",
            )
        )
        routines_by_id: dict[str, dict[str, Any]] = {}
        for routine_surface in parse_unit_routines(
            source,
            source_name,
            id_root,
            "third-party-regex",
        ):
            existing = routines_by_id.get(routine_surface["id"])
            if existing is None:
                routines_by_id[routine_surface["id"]] = routine_surface
                continue
            existing["details"]["declarations"].extend(
                routine_surface["details"]["declarations"]
            )
        surfaces.extend(
            routines_by_id[surface_id]
            for surface_id in sorted(routines_by_id, key=str.casefold)
        )
        sources.append(source_name)
    return surfaces, sources


def parse_data_references(
    legacy_root: Path,
    source_texts: dict[str, str],
) -> list[dict[str, Any]]:
    reference_pattern = re.compile(
        r"'(?P<reference>[^'\r\n]*\.(?:txt|list|dta|ini|wav|log|adi|csv))'",
        flags=re.IGNORECASE,
    )
    grouped: dict[str, list[str]] = defaultdict(list)
    display_names: dict[str, str] = {}
    for source_name, source in source_texts.items():
        for match in reference_pattern.finditer(source):
            reference = match.group("reference")
            if ":" in reference or "\\" in reference:
                continue
            normalized = reference.lower()
            display_names.setdefault(normalized, reference)
            grouped[normalized].append(
                f"{source_name}:{line_number(source, match.start())}"
            )

    root_files = {
        path.name.lower(): path
        for path in legacy_root.iterdir()
        if path.is_file()
    }
    surfaces: list[dict[str, Any]] = []
    for normalized, sources in sorted(grouped.items()):
        display_name = display_names[normalized]
        asset = root_files.get(Path(display_name).name.lower())
        details: dict[str, Any] = {
            "reference": display_name,
            "sources": sorted(set(sources)),
            "kind": (
                "generated"
                if display_name.startswith(".")
                or display_name.lower() == "hstresults.txt"
                else "bundled"
            ),
        }
        if asset is not None:
            payload = asset.read_bytes()
            details["asset"] = asset.name
            details["bytes"] = len(payload)
            details["sha256"] = hashlib.sha256(payload).hexdigest()
        surfaces.append(
            {
                "id": f"legacy.data.reference.{slug(normalized)}",
                "category": "data-file-reference",
                "name": display_name,
                "source": details["sources"][0],
                "details": details,
            }
        )
    return surfaces


def parse_operational_paths(
    source_texts: dict[str, str],
) -> list[dict[str, Any]]:
    operation_pattern = re.compile(
        r"\b(?P<operation>"
        r"raise|ShowMessage|MessageDlg|FileExists|LoadFromFile|SaveToFile|"
        r"TFileStream\.Create|TIniFile\.Create"
        r")\b",
        flags=re.IGNORECASE,
    )
    surfaces: list[dict[str, Any]] = []
    for source_name, source in sorted(source_texts.items()):
        unit_slug = slug(Path(source_name).stem)
        for routine in find_unit_routine_matches(source):
            owner = routine.group("owner") or Path(source_name).stem
            routine_name = routine.group("name")
            counts: dict[str, int] = defaultdict(int)
            for match in operation_pattern.finditer(routine.group("body")):
                operation = match.group("operation")
                operation_key = operation.lower()
                counts[operation_key] += 1
                ordinal = counts[operation_key]
                absolute_start = routine.start("body") + match.start()
                surfaces.append(
                    {
                        "id": (
                            "legacy.operation."
                            f"{unit_slug}.{slug(owner)}.{slug(routine_name)}."
                            f"{slug(operation_key)}.{ordinal}"
                        ),
                        "category": "operational-path",
                        "name": (
                            f"{owner}.{routine_name}: "
                            f"{operation} #{ordinal}"
                        ),
                        "source": (
                            f"{source_name}:"
                            f"{line_number(source, absolute_start)}"
                        ),
                        "details": {
                            "unit": Path(source_name).stem,
                            "owner": owner,
                            "routine": routine_name,
                            "operation": operation,
                            "ordinal": ordinal,
                        },
                    }
                )
    return surfaces


def list_tracked_files(
    legacy_root: Path,
    revision: str = "HEAD",
) -> list[str]:
    result = subprocess.run(
        [
            "git",
            "-C",
            str(legacy_root),
            "ls-tree",
            "-r",
            "--name-only",
            "-z",
            revision,
        ],
        check=True,
        capture_output=True,
        text=True,
    )
    return sorted(
        (name for name in result.stdout.split("\0") if name),
        key=str.casefold,
    )


def tracked_files_sha256(tracked_files: list[str]) -> str:
    return hashlib.sha256(
        "\n".join(sorted(tracked_files, key=str.casefold)).encode("utf-8")
    ).hexdigest()


def materialize_canonical_git_tree(
    legacy_root: Path,
    revision: str,
    tracked_files: list[str],
    destination_root: Path,
) -> None:
    """Materialize exact Git blobs, independent of checkout line endings."""

    destination_root.mkdir(parents=True, exist_ok=True)
    portable_paths: set[str] = set()
    for source_name in tracked_files:
        source_path = PurePosixPath(source_name)
        if (
            not source_name
            or "\\" in source_name
            or ":" in source_name
            or source_path.is_absolute()
            or any(part in {"", ".", ".."} for part in source_path.parts)
        ):
            raise ValueError(
                "tracked Git path cannot be materialized portably: "
                f"{source_name!r}"
            )

        portable_key = source_name.casefold()
        if portable_key in portable_paths:
            raise ValueError(
                "tracked Git paths collide on a case-insensitive filesystem: "
                f"{source_name}"
            )
        portable_paths.add(portable_key)

        blob = subprocess.run(
            [
                "git",
                "-C",
                str(legacy_root),
                "cat-file",
                "blob",
                f"{revision}:{source_name}",
            ],
            check=True,
            capture_output=True,
        ).stdout
        destination = destination_root.joinpath(*source_path.parts)
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(blob)


def tracked_file_exclusions(
    tracked_files: list[str],
) -> list[dict[str, str]]:
    tracked_lookup = {
        name.replace("\\", "/").casefold(): name
        for name in tracked_files
    }
    exclusions: list[dict[str, str]] = []
    for path, definition in sorted(
        NONFUNCTIONAL_TRACKED_FILE_EXCLUSIONS.items(),
        key=lambda item: item[0].casefold(),
    ):
        tracked_path = tracked_lookup.get(path.casefold())
        if tracked_path is None:
            continue
        exclusions.append(
            {
                "path": tracked_path,
                "kind": definition["kind"],
                "rationale": definition["rationale"],
            }
        )
    return exclusions


def validate_tracked_file_classification(
    tracked_files: list[str],
    inventoried_sources: list[str],
    exclusions: list[dict[str, str]],
) -> None:
    def normalized_paths(
        paths: list[str],
        description: str,
    ) -> dict[str, str]:
        normalized: dict[str, str] = {}
        for path in paths:
            if not isinstance(path, str) or not path.strip():
                raise ValueError(f"{description} contains an invalid path")
            canonical = path.replace("\\", "/").casefold()
            if canonical in normalized:
                raise ValueError(
                    f"{description} contains duplicate path: {path}"
                )
            normalized[canonical] = path
        return normalized

    tracked = normalized_paths(tracked_files, "tracked file list")
    sources = normalized_paths(
        inventoried_sources,
        "inventory reference sources",
    )

    exclusion_paths: list[str] = []
    for exclusion in exclusions:
        if not isinstance(exclusion, dict) or set(exclusion) != {
            "path",
            "kind",
            "rationale",
        }:
            raise ValueError(
                "tracked file exclusions require path, kind, and rationale"
            )
        path = exclusion["path"]
        kind = exclusion["kind"]
        rationale = exclusion["rationale"]
        if not all(
            isinstance(value, str) and value.strip()
            for value in (path, kind, rationale)
        ):
            raise ValueError(
                "tracked file exclusions require nonempty path, kind, "
                "and rationale"
            )
        normalized_path = path.replace("\\", "/")
        suffix = Path(normalized_path).suffix.casefold()
        if (
            suffix in PROTECTED_FUNCTIONAL_INPUT_SUFFIXES
            or Path(normalized_path).name.casefold()
            in PROTECTED_FUNCTIONAL_INPUT_NAMES
        ):
            raise ValueError(
                "runtime, build, or resource input cannot be excluded: "
                f"{path}"
            )
        exclusion_paths.append(path)

    excluded = normalized_paths(
        exclusion_paths,
        "tracked file exclusions",
    )
    overlap = sorted(
        set(sources).intersection(excluded),
        key=str.casefold,
    )
    if overlap:
        raise ValueError(
            "tracked files must be classified exactly once; both inventoried "
            "and excluded: "
            + ", ".join(sources[path] for path in overlap)
        )

    classified = set(sources).union(excluded)
    unclassified = sorted(
        set(tracked).difference(classified),
        key=str.casefold,
    )
    if unclassified:
        raise ValueError(
            "tracked files are not classified: "
            + ", ".join(tracked[path] for path in unclassified)
        )

    not_tracked = sorted(
        classified.difference(tracked),
        key=str.casefold,
    )
    if not_tracked:
        display_names = {
            **sources,
            **excluded,
        }
        raise ValueError(
            "inventory classifies files absent from the pinned tree: "
            + ", ".join(display_names[path] for path in not_tracked)
        )


def validate_reference_source_surfaces(
    inventoried_sources: list[str],
    surfaces: list[dict[str, Any]],
) -> None:
    surface_sources = {
        surface["source"].replace("\\", "/").split(":", maxsplit=1)[0].casefold()
        for surface in surfaces
    }
    missing = [
        source
        for source in inventoried_sources
        if source.replace("\\", "/").casefold() not in surface_sources
    ]
    if missing:
        raise ValueError(
            "inventory reference sources have no surface or input record: "
            + ", ".join(sorted(missing, key=str.casefold))
        )


def parse_additional_form_resources(
    legacy_root: Path,
    tracked_files: list[str],
) -> tuple[list[dict[str, Any]], list[str]]:
    resource_names = [
        name
        for name in tracked_files
        if Path(name).suffix.lower() == ".lfm"
        or (
            Path(name).suffix.lower() == ".dfm"
            and name.casefold() != "main.dfm"
        )
    ]
    surfaces: list[dict[str, Any]] = []
    for source_name in resource_names:
        source = (legacy_root / Path(source_name)).read_text(
            encoding="utf-8-sig"
        )
        surfaces.extend(parse_form_resource(source, source_name))
    return surfaces, resource_names


def project_role(source_name: str) -> str:
    normalized = source_name.replace("\\", "/").casefold()
    suffix = Path(source_name).suffix.lower()
    if suffix == ".dpk":
        return "component-package"
    if normalized.endswith("regexsmoketest.lpr"):
        return "smoke-test"
    if normalized.endswith(("unittests.dpr", "unittests.dproj")):
        return "test-runner"
    return "application"


def project_file_kind(source_name: str) -> str:
    suffix = Path(source_name).suffix.lower()
    return {
        ".lpr": "lazarus-program",
        ".dpr": "delphi-program",
        ".dpk": "delphi-package",
        ".lpi": "lazarus-project",
        ".dproj": "delphi-project",
        ".deployproj": "delphi-deployment",
        ".groupproj": "delphi-project-group",
    }[suffix]


def project_definition_surface(
    legacy_root: Path,
    source_name: str,
) -> dict[str, Any]:
    payload = (legacy_root / Path(source_name)).read_bytes()
    return {
        "id": f"legacy.project.definition.{slug(source_name)}",
        "category": "project-definition",
        "name": source_name,
        "source": source_name,
        "details": {
            "file": source_name,
            "kind": project_file_kind(source_name),
            "role": project_role(source_name),
            "bytes": len(payload),
            "sha256": hashlib.sha256(payload).hexdigest(),
        },
    }


def parse_pascal_project(
    source: str,
    source_name: str,
) -> list[dict[str, Any]]:
    project_slug = slug(source_name)
    surfaces: list[dict[str, Any]] = []
    clause_pattern = re.compile(
        r"\b(?P<clause>uses|requires|contains)\b(?P<body>.*?);",
        flags=re.DOTALL | re.IGNORECASE,
    )
    for clause_match in clause_pattern.finditer(source):
        clause = clause_match.group("clause").lower()
        body = clause_match.group("body")
        masked_body = mask_pascal_comments(body)
        explicit_units: set[str] = set()
        explicit_pattern = re.compile(
            r"\b(?P<unit>[A-Za-z_][A-Za-z0-9_.]*)\s+in\s+"
            r"'(?P<path>(?:''|[^'])*)'"
            r"(?:\s+\{(?P<form>[^}]+)\})?",
            flags=re.IGNORECASE,
        )
        for ordinal, match in enumerate(
            explicit_pattern.finditer(body),
            start=1,
        ):
            unit_name = match.group("unit")
            explicit_units.add(unit_name.casefold())
            absolute_offset = clause_match.start("body") + match.start()
            details: dict[str, Any] = {
                "project": source_name,
                "role": project_role(source_name),
                "clause": clause,
                "unit": unit_name,
                "sourcePath": pascal_string(
                    f"'{match.group('path')}'"
                ).replace("\\", "/"),
                "ordinal": ordinal,
            }
            if match.group("form"):
                details["form"] = match.group("form").strip()
            surfaces.append(
                {
                    "id": (
                        f"legacy.project.unit.{project_slug}."
                        f"{clause}.{slug(unit_name)}"
                    ),
                    "category": "project-unit-reference",
                    "name": f"{source_name}: {unit_name}",
                    "source": (
                        f"{source_name}:"
                        f"{line_number(source, absolute_offset)}"
                    ),
                    "details": details,
                }
            )

        builtin_ordinal = len(explicit_units)
        for item in masked_body.split(","):
            match = re.match(
                r"\s*(?P<unit>[A-Za-z_][A-Za-z0-9_.]*)",
                item,
            )
            if match is None:
                continue
            unit_name = match.group("unit")
            if unit_name.casefold() in explicit_units:
                continue
            builtin_ordinal += 1
            unit_offset = source.find(
                unit_name,
                clause_match.start("body"),
                clause_match.end("body"),
            )
            surfaces.append(
                {
                    "id": (
                        f"legacy.project.unit.{project_slug}."
                        f"{clause}.{slug(unit_name)}"
                    ),
                    "category": "project-unit-reference",
                    "name": f"{source_name}: {unit_name}",
                    "source": (
                        f"{source_name}:"
                        f"{line_number(source, unit_offset)}"
                    ),
                    "details": {
                        "project": source_name,
                        "role": project_role(source_name),
                        "clause": clause,
                        "unit": unit_name,
                        "sourcePath": None,
                        "ordinal": builtin_ordinal,
                    },
                }
            )

    lifecycle_pattern = re.compile(
        r"(?P<operation>"
        r"RequireDerivedFormResource\s*:=|"
        r"Application\.Scaled\s*:=|"
        r"Application\.Initialize\b|"
        r"Application\.Title\s*:=|"
        r"Application\.CreateForm\b|"
        r"Application\.Run\b|"
        r"TestInsight\.DUnitX\.RunRegisteredTests\b|"
        r"TDUnitX\.CheckCommandLine\b|"
        r"TDUnitX\.CreateRunner\b|"
        r"runner\.Execute\b"
        r")",
        flags=re.IGNORECASE,
    )
    lifecycle_counts: dict[str, int] = defaultdict(int)
    masked_source = mask_pascal_comments(source)
    for match in lifecycle_pattern.finditer(masked_source):
        operation = re.sub(r"\s*:=\s*$", "", match.group("operation"))
        operation_key = slug(operation)
        lifecycle_counts[operation_key] += 1
        ordinal = lifecycle_counts[operation_key]
        surfaces.append(
            {
                "id": (
                    f"legacy.project.lifecycle.{project_slug}."
                    f"{operation_key}.{ordinal}"
                ),
                "category": "project-lifecycle",
                "name": f"{source_name}: {operation}",
                "source": (
                    f"{source_name}:{line_number(source, match.start())}"
                ),
                "details": {
                    "project": source_name,
                    "role": project_role(source_name),
                    "operation": operation,
                    "ordinal": ordinal,
                },
            }
        )

    resource_pattern = re.compile(
        r"\{\$R\s+(?P<resource>[^}\r\n]+)\}",
        flags=re.IGNORECASE,
    )
    for ordinal, match in enumerate(
        resource_pattern.finditer(source),
        start=1,
    ):
        resource = match.group("resource").strip()
        surfaces.append(
            {
                "id": (
                    f"legacy.project.resource-directive.{project_slug}."
                    f"{slug(resource)}.{ordinal}"
                ),
                "category": "project-resource-reference",
                "name": f"{source_name}: {resource}",
                "source": (
                    f"{source_name}:{line_number(source, match.start())}"
                ),
                "details": {
                    "project": source_name,
                    "role": project_role(source_name),
                    "reference": resource,
                    "referenceKind": "compiler-resource",
                    "ordinal": ordinal,
                },
            }
        )
    return surfaces


def parse_project_metadata(
    legacy_root: Path,
    tracked_files: list[str],
) -> tuple[list[dict[str, Any]], list[str]]:
    project_suffixes = {
        ".lpr",
        ".dpr",
        ".dpk",
        ".lpi",
        ".dproj",
        ".deployproj",
        ".groupproj",
    }
    project_names = [
        name
        for name in tracked_files
        if Path(name).suffix.lower() in project_suffixes
    ]
    surfaces = [
        project_definition_surface(legacy_root, name)
        for name in project_names
    ]

    pascal_suffixes = {".lpr", ".dpr", ".dpk"}
    for source_name in project_names:
        if Path(source_name).suffix.lower() not in pascal_suffixes:
            continue
        source = (legacy_root / Path(source_name)).read_text(
            encoding="utf-8-sig"
        )
        surfaces.extend(parse_pascal_project(source, source_name))

    reference_patterns = {
        "deployment-file": re.compile(
            r"<DeployFile\b[^>]*(?:Include|LocalName)="
            r"\"(?P<reference>[^\"]+)\"",
            flags=re.IGNORECASE,
        ),
        "project-member": re.compile(
            r"<Projects\s+Include=\"(?P<reference>[^\"]+)\"",
            flags=re.IGNORECASE,
        ),
        "application-icon": re.compile(
            r"<Icon_MainIcon>(?P<reference>[^<]+)</Icon_MainIcon>",
            flags=re.IGNORECASE,
        ),
        "application-manifest": re.compile(
            r"<Manifest_File>(?P<reference>[^<]+)</Manifest_File>",
            flags=re.IGNORECASE,
        ),
    }
    configuration_pattern = re.compile(
        r"<(?P<property>"
        r"MainSource|FrameworkType|AppType|DCC_Platform|"
        r"TargetOS|TargetCPU|ResourceType|SyntaxMode"
        r")(?:\s+Value=\"(?P<attribute_value>[^\"]*)\")?\s*"
        r"(?:>(?P<text_value>[^<]*)</(?P=property)>|/>)",
        flags=re.IGNORECASE,
    )
    tracked_lookup = {name.replace("\\", "/").casefold() for name in tracked_files}

    for source_name in project_names:
        if Path(source_name).suffix.lower() in pascal_suffixes:
            continue
        source = (legacy_root / Path(source_name)).read_text(
            encoding="utf-8-sig"
        )
        project_slug = slug(source_name)
        for reference_kind, pattern in reference_patterns.items():
            if (
                reference_kind == "project-member"
                and Path(source_name).suffix.lower() != ".groupproj"
            ):
                continue
            if (
                reference_kind != "project-member"
                and source_name.casefold()
                not in {"morserunner.dproj", "morserunner.deployproj"}
            ):
                continue
            grouped: dict[str, list[str]] = defaultdict(list)
            display_names: dict[str, str] = {}
            for match in pattern.finditer(source):
                reference = match.group("reference").strip()
                normalized = reference.replace("\\", "/").casefold()
                display_names.setdefault(normalized, reference)
                grouped[normalized].append(
                    f"{source_name}:{line_number(source, match.start())}"
                )
            for normalized, sources in sorted(grouped.items()):
                reference = display_names[normalized]
                relative_reference = reference.replace("\\", "/")
                exists = relative_reference.casefold() in tracked_lookup
                surfaces.append(
                    {
                        "id": (
                            f"legacy.project.reference.{project_slug}."
                            f"{slug(reference_kind)}.{slug(reference)}"
                        ),
                        "category": (
                            "project-member-reference"
                            if reference_kind == "project-member"
                            else "project-resource-reference"
                        ),
                        "name": f"{source_name}: {reference}",
                        "source": sources[0],
                        "details": {
                            "project": source_name,
                            "reference": reference,
                            "referenceKind": reference_kind,
                            "trackedReferenceExists": exists,
                            "sources": sources,
                        },
                    }
                )

        configurations: dict[tuple[str, str], list[str]] = defaultdict(list)
        for match in configuration_pattern.finditer(source):
            value = (
                match.group("attribute_value")
                if match.group("attribute_value") is not None
                else match.group("text_value")
            ).strip()
            if not value or value.casefold() == match.group(
                "property"
            ).casefold():
                continue
            property_name = match.group("property")
            configurations[(property_name, value)].append(
                f"{source_name}:{line_number(source, match.start())}"
            )
        for (property_name, value), sources in sorted(
            configurations.items(),
            key=lambda item: (
                item[0][0].casefold(),
                item[0][1].casefold(),
            ),
        ):
            surfaces.append(
                {
                    "id": (
                        f"legacy.project.configuration.{project_slug}."
                        f"{slug(property_name)}.{slug(value)}"
                    ),
                    "category": "project-configuration",
                    "name": f"{source_name}: {property_name}={value}",
                    "source": sources[0],
                    "details": {
                        "project": source_name,
                        "property": property_name,
                        "value": value,
                        "sources": sources,
                    },
                }
            )
    return surfaces, project_names


def parse_form_resource_directives(
    legacy_root: Path,
    tracked_files: list[str],
) -> tuple[list[dict[str, Any]], list[str]]:
    source_names = [
        name
        for name in tracked_files
        if Path(name).suffix.lower() == ".pas"
        and not name.replace("\\", "/").startswith("PerlRegEx/")
    ]
    pattern = re.compile(
        r"\{\$R\s+(?P<resource>(?:\*|[A-Za-z0-9_.\\/]+)"
        r"\.(?:dfm|lfm))\}",
        flags=re.IGNORECASE,
    )
    surfaces: list[dict[str, Any]] = []
    for source_name in source_names:
        source = (legacy_root / Path(source_name)).read_text(
            encoding="utf-8-sig"
        )
        for ordinal, match in enumerate(pattern.finditer(source), start=1):
            resource = match.group("resource")
            surfaces.append(
                {
                    "id": (
                        "legacy.form.resource-directive."
                        f"{slug(source_name)}.{slug(resource)}.{ordinal}"
                    ),
                    "category": "form-resource-reference",
                    "name": f"{source_name}: {resource}",
                    "source": (
                        f"{source_name}:"
                        f"{line_number(source, match.start())}"
                    ),
                    "details": {
                        "unit": source_name,
                        "resource": resource,
                        "ordinal": ordinal,
                    },
                }
            )
    return surfaces, source_names


def parse_bundled_assets(
    legacy_root: Path,
    tracked_files: list[str],
) -> tuple[list[dict[str, Any]], list[str]]:
    data_suffixes = {".txt", ".list", ".dta"}
    application_resource_names = {
        "manifest.xml",
        "morserunner.ico",
        "morserunner_icon.ico",
        "morserunner_icon1.ico",
        "morserunner_icon2.ico",
        "morserunner_icon3.ico",
        "morserunner_icon4.ico",
        "morserunner16.bmp",
        "morserunner32.bmp",
    }
    asset_names = [
        name
        for name in tracked_files
        if Path(name).parent == Path(".")
        and (
            Path(name).suffix.lower() in data_suffixes
            or name.casefold() in application_resource_names
        )
    ]
    surfaces: list[dict[str, Any]] = []
    for source_name in asset_names:
        path = legacy_root / Path(source_name)
        payload = path.read_bytes()
        is_data = Path(source_name).suffix.lower() in data_suffixes
        if source_name.casefold() == "hstresults.txt":
            role = "mutable-results-seed"
        elif source_name.casefold() == "readme.txt":
            role = "operator-help"
        elif is_data:
            role = "simulation-data"
        else:
            role = "application-resource"
        surfaces.append(
            {
                "id": (
                    f"legacy.asset.{'data' if is_data else 'resource'}."
                    f"{slug(source_name)}"
                ),
                "category": (
                    "bundled-data-file"
                    if is_data
                    else "bundled-application-resource"
                ),
                "name": source_name,
                "source": source_name,
                "details": {
                    "file": source_name,
                    "role": role,
                    "bytes": len(payload),
                    "sha256": hashlib.sha256(payload).hexdigest(),
                },
            }
        )
    return surfaces, asset_names


def additional_input_classification(
    source_name: str,
) -> tuple[str, str, str] | None:
    normalized = source_name.replace("\\", "/")
    normalized_lower = normalized.casefold()
    suffix = Path(normalized).suffix.casefold()

    exact_inputs = {
        ".gitattributes": (
            "build",
            "source-normalization-policy",
            "repository-build-input",
        ),
        "perlregex/readme.txt": (
            "documentation",
            "third-party-component-notice",
            "bundled-documentation",
        ),
        "tools/verify-normalization.sh": (
            "build",
            "source-normalization-verifier",
            "repository-build-input",
        ),
    }
    exact = exact_inputs.get(normalized_lower)
    if exact is not None:
        return exact

    suffix_inputs = {
        ".cmds": (
            "build",
            "delphi-compiler-command-metadata",
            "repository-build-input",
        ),
        ".cnt": (
            "resource",
            "third-party-help-index",
            "bundled-application-resource",
        ),
        ".dcr": (
            "resource",
            "delphi-component-resource",
            "bundled-application-resource",
        ),
        ".hlp": (
            "resource",
            "third-party-help-content",
            "bundled-application-resource",
        ),
        ".lst": (
            "build",
            "delphi-linker-listing",
            "repository-build-input",
        ),
        ".mak": (
            "build",
            "native-object-build-script",
            "repository-build-input",
        ),
        ".obj": (
            "build",
            "native-static-link-object",
            "repository-build-input",
        ),
        ".otares": (
            "resource",
            "delphi-ide-resource-metadata",
            "bundled-application-resource",
        ),
        ".pdf": (
            "documentation",
            "operator-manual",
            "bundled-documentation",
        ),
    }
    return suffix_inputs.get(suffix)


def parse_additional_repository_inputs(
    legacy_root: Path,
    tracked_files: list[str],
) -> tuple[list[dict[str, Any]], list[str]]:
    input_names = [
        name
        for name in tracked_files
        if additional_input_classification(name) is not None
    ]
    surfaces: list[dict[str, Any]] = []
    for source_name in input_names:
        classification = additional_input_classification(source_name)
        if classification is None:
            raise AssertionError("additional input classification disappeared")
        input_kind, role, category = classification
        payload = (legacy_root / Path(source_name)).read_bytes()
        surfaces.append(
            {
                "id": (
                    f"legacy.asset.{input_kind}.{slug(source_name)}"
                ),
                "category": category,
                "name": source_name,
                "source": source_name,
                "details": {
                    "file": source_name,
                    "inputKind": input_kind,
                    "role": role,
                    "bytes": len(payload),
                    "sha256": hashlib.sha256(payload).hexdigest(),
                },
            }
        )
    return surfaces, input_names


def parse_distribution_files(
    legacy_root: Path,
    tracked_files: list[str],
) -> tuple[list[dict[str, Any]], list[str]]:
    script_names = [
        name
        for name in ("Lazarus/build.ps1", "tools/make-install.sh")
        if name in tracked_files
    ]
    grouped: dict[str, list[str]] = defaultdict(list)
    display_names: dict[str, str] = {}
    for source_name in script_names:
        source = (legacy_root / Path(source_name)).read_text(
            encoding="utf-8-sig"
        )
        if source_name.endswith(".ps1"):
            block_match = re.search(
                r"\$runtimeFiles\s*=\s*@\((?P<body>.*?)\)",
                source,
                flags=re.DOTALL,
            )
            quote_pattern = re.compile(r"'(?P<file>[^']+)'")
        else:
            block_match = re.search(
                r"FILES=\((?P<body>.*?)\)",
                source,
                flags=re.DOTALL,
            )
            quote_pattern = re.compile(r'"(?P<file>[^"]+)"')
        if block_match is None:
            raise ValueError(f"runtime file list not found: {source_name}")
        for match in quote_pattern.finditer(block_match.group("body")):
            file_name = match.group("file")
            normalized = file_name.replace("\\", "/").casefold()
            display_names.setdefault(normalized, file_name)
            absolute_offset = block_match.start("body") + match.start()
            grouped[normalized].append(
                f"{source_name}:{line_number(source, absolute_offset)}"
            )

    tracked_lookup = {name.replace("\\", "/").casefold() for name in tracked_files}
    surfaces: list[dict[str, Any]] = []
    for normalized, sources in sorted(grouped.items()):
        file_name = display_names[normalized]
        is_project_output = normalized.endswith(".exe")
        surfaces.append(
            {
                "id": f"legacy.distribution.file.{slug(file_name)}",
                "category": "distribution-file",
                "name": file_name,
                "source": sources[0],
                "details": {
                    "file": file_name,
                    "kind": (
                        "project-output"
                        if is_project_output
                        else "runtime-data"
                    ),
                    "trackedSourceExists": (
                        is_project_output or normalized in tracked_lookup
                    ),
                    "sources": sources,
                },
            }
        )
    return surfaces, script_names


def routine_identity_at_offset(
    source_name: str,
    routines: list[re.Match[str]],
    offset: int,
) -> tuple[str, str]:
    for routine in routines:
        if routine.start() <= offset < routine.end():
            return (
                routine.group("owner") or Path(source_name).stem,
                routine.group("name"),
            )
    return Path(source_name).stem, "unit-scope"


def parse_external_integrations(
    legacy_root: Path,
    tracked_files: list[str],
) -> tuple[list[dict[str, Any]], list[str]]:
    source_names = [
        name
        for name in tracked_files
        if Path(name).suffix.lower() == ".pas"
        and not name.replace("\\", "/").startswith("PerlRegEx/")
    ]
    patterns = {
        "command-line-path": re.compile(
            r"\b(?:ParamStr|ParamCount)\b",
            flags=re.IGNORECASE,
        ),
        "shell-launch": re.compile(
            r"\b(?:ShellExecute|GetDesktopWindow)\b",
            flags=re.IGNORECASE,
        ),
        "network-client": re.compile(
            r"\b(?:TFPHTTPClient|TIdHTTP|fphttpclient|opensslsockets|"
            r"FormPost|ResponseStatusCode)\b",
            flags=re.IGNORECASE,
        ),
        "native-window-message": re.compile(
            r"\b(?:Windows\.)?PostMessage\b",
            flags=re.IGNORECASE,
        ),
    }
    grouped: dict[
        tuple[str, str, str, str],
        list[dict[str, str]],
    ] = defaultdict(list)
    for source_name in source_names:
        source = mask_pascal_comments(
            (legacy_root / Path(source_name)).read_text(
                encoding="utf-8-sig"
            )
        )
        routines = find_unit_routine_matches(source)
        for kind, pattern in patterns.items():
            for match in pattern.finditer(source):
                owner, routine = routine_identity_at_offset(
                    source_name,
                    routines,
                    match.start(),
                )
                grouped[(source_name, owner, routine, kind)].append(
                    {
                        "operation": match.group(0),
                        "source": (
                            f"{source_name}:"
                            f"{line_number(source, match.start())}"
                        ),
                    }
                )

    surfaces: list[dict[str, Any]] = []
    for (source_name, owner, routine, kind), occurrences in sorted(
        grouped.items(),
        key=lambda item: tuple(value.casefold() for value in item[0]),
    ):
        surfaces.append(
            {
                "id": (
                    f"legacy.integration.{slug(kind)}."
                    f"{slug(source_name)}.{slug(owner)}.{slug(routine)}"
                ),
                "category": "external-integration",
                "name": f"{owner}.{routine}: {kind}",
                "source": occurrences[0]["source"],
                "details": {
                    "unit": Path(source_name).stem,
                    "owner": owner,
                    "routine": routine,
                    "integrationKind": kind,
                    "occurrences": occurrences,
                },
            }
        )
    return surfaces, source_names


def parse_data_parser_paths(
    legacy_root: Path,
    tracked_files: list[str],
) -> tuple[list[dict[str, Any]], list[str]]:
    source_names = [
        name
        for name in tracked_files
        if Path(name).suffix.lower() == ".pas"
        and not name.replace("\\", "/").startswith("PerlRegEx/")
    ]
    operation_pattern = re.compile(
        r"\b(?P<operation>"
        r"LoadFromFile|TFileStream\.Create|ReadLn|BlockRead|OpenRead"
        r")\b",
        flags=re.IGNORECASE,
    )
    reference_pattern = re.compile(
        r"'(?P<reference>[^'\r\n]*\.(?:txt|list|dta|wav|lst))'",
        flags=re.IGNORECASE,
    )
    surfaces: list[dict[str, Any]] = []
    for source_name in source_names:
        source = mask_pascal_comments(
            (legacy_root / Path(source_name)).read_text(
                encoding="utf-8-sig"
            )
        )
        for routine in find_unit_routine_matches(source):
            operations: list[dict[str, str]] = []
            body = routine.group("body")
            for match in operation_pattern.finditer(body):
                absolute_offset = routine.start("body") + match.start()
                operations.append(
                    {
                        "operation": match.group("operation"),
                        "source": (
                            f"{source_name}:"
                            f"{line_number(source, absolute_offset)}"
                        ),
                    }
                )
            if not operations:
                continue
            owner = routine.group("owner") or Path(source_name).stem
            routine_name = routine.group("name")
            references = sorted(
                {
                    match.group("reference")
                    for match in reference_pattern.finditer(body)
                },
                key=str.casefold,
            )
            surfaces.append(
                {
                    "id": (
                        f"legacy.data.parser.{slug(source_name)}."
                        f"{slug(owner)}.{slug(routine_name)}"
                    ),
                    "category": "data-parser-path",
                    "name": f"{owner}.{routine_name}",
                    "source": operations[0]["source"],
                    "details": {
                        "unit": Path(source_name).stem,
                        "owner": owner,
                        "routine": routine_name,
                        "operations": operations,
                        "references": references,
                    },
                }
            )
    return surfaces, source_names


def attribute_label(arguments: str) -> str:
    match = re.match(r"\s*(?P<label>'(?:''|[^'])*')", arguments)
    if match is None:
        return ""
    return pascal_string(match.group("label"))


def attribute_values(
    attributes: list[dict[str, Any]],
    name: str,
) -> list[str]:
    return [
        attribute["arguments"]
        for attribute in attributes
        if attribute["name"].casefold() == name.casefold()
    ]


def parse_test_fixture_body(
    source: str,
    source_name: str,
    fixture: str,
    body_start: int,
    body_end: int,
    enabled: bool,
) -> list[dict[str, Any]]:
    active_attribute_pattern = re.compile(
        r"^\s*\[(?P<name>[A-Za-z_][A-Za-z0-9_]*)"
        r"(?:\((?P<arguments>.*)\))?\]\s*(?://.*)?$"
    )
    commented_attribute_pattern = re.compile(
        r"^\s*//\s*\[(?P<name>[A-Za-z_][A-Za-z0-9_]*)"
        r"(?:\((?P<arguments>.*)\))?\]"
    )
    method_pattern = re.compile(
        r"^\s*(?P<kind>procedure|function)\s+"
        r"(?P<name>[A-Za-z_][A-Za-z0-9_]*)\b",
        flags=re.IGNORECASE,
    )
    pending_active: list[dict[str, Any]] = []
    pending_commented: list[dict[str, Any]] = []
    surfaces: list[dict[str, Any]] = []
    body = source[body_start:body_end]
    offset = body_start

    for line in body.splitlines(keepends=True):
        active_match = active_attribute_pattern.match(line.rstrip("\r\n"))
        if active_match is not None:
            pending_active.append(
                {
                    "name": active_match.group("name"),
                    "arguments": active_match.group("arguments") or "",
                    "line": line_number(source, offset),
                }
            )
            offset += len(line)
            continue
        commented_match = commented_attribute_pattern.match(
            line.rstrip("\r\n")
        )
        if commented_match is not None:
            pending_commented.append(
                {
                    "name": commented_match.group("name"),
                    "arguments": commented_match.group("arguments") or "",
                    "line": line_number(source, offset),
                }
            )
            offset += len(line)
            continue

        method_match = method_pattern.match(line)
        if method_match is None:
            offset += len(line)
            continue

        method_name = method_match.group("name")
        test_cases = attribute_values(pending_active, "TestCase")
        commented_cases = attribute_values(pending_commented, "TestCase")
        test_attributes = [
            attribute
            for attribute in pending_active
            if attribute["name"].casefold() == "test"
        ]
        explicit_test = bool(test_attributes)
        declared_enabled = not any(
            attribute["arguments"].strip().casefold() == "false"
            for attribute in test_attributes
        )
        method_enabled = enabled and declared_enabled
        categories = [
            attribute_label(value)
            for value in attribute_values(pending_active, "Category")
        ]
        method_id_root = (
            f"legacy.test.{slug(source_name)}."
            f"{slug(fixture)}.{slug(method_name)}"
        )

        if test_cases or explicit_test:
            surfaces.append(
                {
                    "id": f"{method_id_root}.method",
                    "category": (
                        "legacy-test-method"
                        if method_enabled
                        else "legacy-test-disabled-method"
                    ),
                    "name": f"{fixture}.{method_name}",
                    "source": (
                        f"{source_name}:{line_number(source, offset)}"
                    ),
                    "details": {
                        "fixture": fixture,
                        "method": method_name,
                        "kind": method_match.group("kind").lower(),
                        "enabled": method_enabled,
                        "fixtureEnabled": enabled,
                        "declaredEnabled": declared_enabled,
                        "explicitTest": explicit_test,
                        "categories": categories,
                        "testCaseCount": len(test_cases),
                    },
                }
            )

        for ordinal, attribute in enumerate(test_attributes, start=1):
            attribute_enabled = (
                enabled
                and attribute["arguments"].strip().casefold() != "false"
            )
            surfaces.append(
                {
                    "id": (
                        f"{method_id_root}.test-declaration.{ordinal}"
                    ),
                    "category": (
                        "legacy-test-declaration"
                        if attribute_enabled
                        else "legacy-test-disabled-declaration"
                    ),
                    "name": (
                        f"{fixture}.{method_name}: Test"
                        f"({attribute['arguments']})"
                    ),
                    "source": f"{source_name}:{attribute['line']}",
                    "details": {
                        "fixture": fixture,
                        "method": method_name,
                        "arguments": attribute["arguments"],
                        "enabled": attribute_enabled,
                        "fixtureEnabled": enabled,
                        "declaredEnabled": (
                            attribute["arguments"].strip().casefold()
                            != "false"
                        ),
                        "ordinal": ordinal,
                    },
                }
            )

        active_case_occurrences: dict[str, int] = defaultdict(int)
        for arguments in test_cases:
            label = attribute_label(arguments)
            fingerprint = hashlib.sha256(
                arguments.encode("utf-8")
            ).hexdigest()[:12]
            active_case_occurrences[fingerprint] += 1
            duplicate_suffix = (
                f".{active_case_occurrences[fingerprint]}"
                if active_case_occurrences[fingerprint] > 1
                else ""
            )
            attribute = next(
                item
                for item in pending_active
                if item["name"].casefold() == "testcase"
                and item["arguments"] == arguments
                and not item.get("used")
            )
            attribute["used"] = True
            surfaces.append(
                {
                    "id": (
                        f"{method_id_root}.case.{slug(label) or 'unnamed'}."
                        f"{fingerprint}{duplicate_suffix}"
                    ),
                    "category": (
                        "legacy-test-case"
                        if method_enabled
                        else "legacy-test-disabled-case"
                    ),
                    "name": f"{fixture}.{method_name}: {label}",
                    "source": f"{source_name}:{attribute['line']}",
                    "details": {
                        "fixture": fixture,
                        "method": method_name,
                        "label": label,
                        "arguments": arguments,
                        "enabled": method_enabled,
                        "fixtureEnabled": enabled,
                        "declaredEnabled": declared_enabled,
                        "categories": categories,
                    },
                }
            )

        commented_case_occurrences: dict[str, int] = defaultdict(int)
        for arguments in commented_cases:
            label = attribute_label(arguments)
            fingerprint = hashlib.sha256(
                arguments.encode("utf-8")
            ).hexdigest()[:12]
            commented_case_occurrences[fingerprint] += 1
            duplicate_suffix = (
                f".{commented_case_occurrences[fingerprint]}"
                if commented_case_occurrences[fingerprint] > 1
                else ""
            )
            attribute = next(
                item
                for item in pending_commented
                if item["name"].casefold() == "testcase"
                and item["arguments"] == arguments
                and not item.get("used")
            )
            attribute["used"] = True
            surfaces.append(
                {
                    "id": (
                        f"{method_id_root}.commented-case."
                        f"{slug(label) or 'unnamed'}.{fingerprint}"
                        f"{duplicate_suffix}"
                    ),
                    "category": "legacy-test-commented-case",
                    "name": f"{fixture}.{method_name}: {label}",
                    "source": f"{source_name}:{attribute['line']}",
                    "details": {
                        "fixture": fixture,
                        "method": method_name,
                        "label": label,
                        "arguments": arguments,
                        "enabled": False,
                        "disabledBy": "commented-attribute",
                        "categories": categories,
                    },
                }
            )

        lifecycle_names = {
            "setup",
            "teardown",
            "setupfixture",
            "teardownfixture",
        }
        for attribute in pending_active:
            attribute_name = attribute["name"].casefold()
            if attribute_name not in lifecycle_names:
                continue
            surfaces.append(
                {
                    "id": (
                        f"{method_id_root}.lifecycle."
                        f"{slug(attribute_name)}"
                    ),
                    "category": "legacy-test-lifecycle",
                    "name": (
                        f"{fixture}.{method_name}: {attribute['name']}"
                    ),
                    "source": f"{source_name}:{attribute['line']}",
                    "details": {
                        "fixture": fixture,
                        "method": method_name,
                        "lifecycle": attribute["name"],
                        "enabled": enabled,
                    },
                }
            )

        pending_active = []
        pending_commented = []
        offset += len(line)
    return surfaces


def parse_pascal_tests(
    legacy_root: Path,
    tracked_files: list[str],
) -> tuple[list[dict[str, Any]], list[str]]:
    source_names = [
        name
        for name in tracked_files
        if name.replace("\\", "/").startswith("Test/")
        and Path(name).suffix.lower() == ".pas"
    ]
    sources = {
        name: (legacy_root / Path(name)).read_text(encoding="utf-8-sig")
        for name in source_names
    }
    active_registration_pattern = re.compile(
        r"^\s*TDUnitX\.RegisterTestFixture\("
        r"(?P<fixture>[A-Za-z_][A-Za-z0-9_]*)\s*\);",
        flags=re.MULTILINE | re.IGNORECASE,
    )
    disabled_registration_pattern = re.compile(
        r"^\s*//\s*TDUnitX\.RegisterTestFixture\("
        r"(?P<fixture>[A-Za-z_][A-Za-z0-9_]*)\s*\);",
        flags=re.MULTILINE | re.IGNORECASE,
    )
    active_registrations: dict[str, list[str]] = defaultdict(list)
    disabled_registrations: dict[str, list[str]] = defaultdict(list)
    display_names: dict[str, str] = {}
    for source_name, source in sources.items():
        for match in active_registration_pattern.finditer(source):
            fixture = match.group("fixture")
            key = fixture.casefold()
            display_names.setdefault(key, fixture)
            active_registrations[key].append(
                f"{source_name}:{line_number(source, match.start())}"
            )
        for match in disabled_registration_pattern.finditer(source):
            fixture = match.group("fixture")
            key = fixture.casefold()
            display_names.setdefault(key, fixture)
            disabled_registrations[key].append(
                f"{source_name}:{line_number(source, match.start())}"
            )

    surfaces: list[dict[str, Any]] = []
    for key, registration_sources in sorted(active_registrations.items()):
        fixture = display_names[key]
        surfaces.append(
            {
                "id": f"legacy.test.registration.{slug(fixture)}",
                "category": "legacy-test-registration",
                "name": fixture,
                "source": registration_sources[0],
                "details": {
                    "fixture": fixture,
                    "enabled": True,
                    "sources": registration_sources,
                },
            }
        )
    for key, registration_sources in sorted(disabled_registrations.items()):
        fixture = display_names[key]
        surfaces.append(
            {
                "id": f"legacy.test.disabled-registration.{slug(fixture)}",
                "category": "legacy-test-disabled-registration",
                "name": fixture,
                "source": registration_sources[0],
                "details": {
                    "fixture": fixture,
                    "enabled": False,
                    "disabledBy": "commented-registration",
                    "sources": registration_sources,
                },
            }
        )

    fixture_pattern = re.compile(
        r"^\s*\[TestFixture(?P<arguments>[^\]\r\n]*)\]\s*\r?\n"
        r"^\s*(?P<fixture>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*class\b"
        r"[^\r\n]*",
        flags=re.MULTILINE | re.IGNORECASE,
    )
    class_end_pattern = re.compile(r"^\s*end;", flags=re.MULTILINE)
    for source_name, source in sorted(sources.items()):
        search_end = len(source)
        for fixture_match in fixture_pattern.finditer(source, 0, search_end):
            fixture = fixture_match.group("fixture")
            fixture_key = fixture.casefold()
            enabled = fixture_key in active_registrations
            class_end = class_end_pattern.search(
                source,
                fixture_match.end(),
                search_end,
            )
            if class_end is None:
                raise ValueError(
                    f"test fixture declaration is not terminated: "
                    f"{source_name}:{fixture}"
                )
            fixture_arguments = fixture_match.group("arguments").strip()
            surfaces.append(
                {
                    "id": (
                        f"legacy.test.fixture.{slug(source_name)}."
                        f"{slug(fixture)}"
                    ),
                    "category": "legacy-test-fixture",
                    "name": fixture,
                    "source": (
                        f"{source_name}:"
                        f"{line_number(source, fixture_match.start())}"
                    ),
                    "details": {
                        "fixture": fixture,
                        "arguments": (
                            fixture_arguments[1:-1]
                            if fixture_arguments.startswith("(")
                            and fixture_arguments.endswith(")")
                            else fixture_arguments
                        ),
                        "enabled": enabled,
                        "registrationSources": (
                            active_registrations.get(fixture_key)
                            or disabled_registrations.get(fixture_key)
                            or []
                        ),
                    },
                }
            )
            surfaces.extend(
                parse_test_fixture_body(
                    source,
                    source_name,
                    fixture,
                    fixture_match.end(),
                    class_end.start(),
                    enabled,
                )
            )
    return surfaces, source_names


def parse_smoke_tests(
    legacy_root: Path,
    tracked_files: list[str],
) -> tuple[list[dict[str, Any]], list[str]]:
    source_names = [
        name
        for name in tracked_files
        if Path(name).suffix.lower() == ".lpr"
        and "smoketest" in Path(name).stem.casefold()
    ]
    pattern = re.compile(
        r"^procedure\s+(?P<name>Test[A-Za-z0-9_]+)\s*;"
        r"(?P<body>.*?)(?=^procedure\s+|\Z)",
        flags=re.MULTILINE | re.DOTALL | re.IGNORECASE,
    )
    surfaces: list[dict[str, Any]] = []
    for source_name in source_names:
        source = (legacy_root / Path(source_name)).read_text(
            encoding="utf-8-sig"
        )
        for match in pattern.finditer(mask_pascal_comments(source)):
            test_name = match.group("name")
            invocation_pattern = re.compile(
                rf"^\s*{re.escape(test_name)}\s*;",
                flags=re.MULTILINE | re.IGNORECASE,
            )
            surfaces.append(
                {
                    "id": (
                        f"legacy.test.smoke.{slug(source_name)}."
                        f"{slug(test_name)}"
                    ),
                    "category": "legacy-smoke-test",
                    "name": test_name,
                    "source": (
                        f"{source_name}:"
                        f"{line_number(source, match.start())}"
                    ),
                    "details": {
                        "project": source_name,
                        "test": test_name,
                        "invoked": bool(invocation_pattern.search(source)),
                        "checkCount": len(
                            re.findall(
                                r"\bCheck\s*\(",
                                match.group("body"),
                                flags=re.IGNORECASE,
                            )
                        ),
                    },
                }
            )
    return surfaces, source_names


def parse_unit_lifecycle(
    legacy_root: Path,
    tracked_files: list[str],
) -> tuple[list[dict[str, Any]], list[str]]:
    source_names = [
        name
        for name in tracked_files
        if Path(name).suffix.lower() == ".pas"
        and not name.replace("\\", "/").startswith("PerlRegEx/")
    ]
    pattern = re.compile(
        r"^(?P<phase>initialization|finalization)\s*$",
        flags=re.MULTILINE | re.IGNORECASE,
    )
    surfaces: list[dict[str, Any]] = []
    for source_name in source_names:
        source = mask_pascal_comments(
            (legacy_root / Path(source_name)).read_text(
                encoding="utf-8-sig"
            )
        )
        counts: dict[str, int] = defaultdict(int)
        for match in pattern.finditer(source):
            phase = match.group("phase").lower()
            counts[phase] += 1
            surfaces.append(
                {
                    "id": (
                        f"legacy.lifecycle.unit.{slug(source_name)}."
                        f"{phase}.{counts[phase]}"
                    ),
                    "category": "unit-lifecycle",
                    "name": f"{source_name}: {phase}",
                    "source": (
                        f"{source_name}:"
                        f"{line_number(source, match.start())}"
                    ),
                    "details": {
                        "unit": Path(source_name).stem,
                        "phase": phase,
                        "ordinal": counts[phase],
                    },
                }
            )
    return surfaces, source_names


def validate_unique_surface_ids(surfaces: list[dict[str, Any]]) -> None:
    counts: dict[str, int] = defaultdict(int)
    for surface in surfaces:
        counts[surface["id"]] += 1
    duplicates = sorted(
        surface_id
        for surface_id, count in counts.items()
        if count > 1
    )
    if duplicates:
        raise ValueError(
            "legacy inventory contains duplicate surface IDs: "
            + ", ".join(duplicates)
        )


def build_inventory_from_canonical_tree(
    legacy_root: Path,
    revision: str,
    tracked_files: list[str],
) -> dict[str, Any]:
    ini_path = legacy_root / "Ini.pas"
    if not ini_path.is_file():
        raise ValueError(f"legacy Ini.pas not found: {ini_path}")
    main_pas_path = legacy_root / "Main.pas"
    main_dfm_path = legacy_root / "Main.dfm"
    if not main_pas_path.is_file() or not main_dfm_path.is_file():
        raise ValueError("legacy Main.pas and Main.dfm are required")
    log_path = legacy_root / "Log.pas"
    if not log_path.is_file():
        raise ValueError("legacy Log.pas is required")

    original_source = ini_path.read_text(encoding="utf-8-sig")
    source = mask_pascal_comments(original_source)

    contest_enumeration = parse_enum(
        source,
        "TSimContest",
        "contest-enumeration",
        "legacy.ini.contest",
    )
    run_modes = parse_enum(
        source,
        "TRunMode",
        "run-mode",
        "legacy.ini.run-mode",
    )
    contest_definitions = parse_contest_definitions(source)
    settings = parse_persisted_settings(source, contest_definitions)
    main_source = mask_pascal_comments(
        main_pas_path.read_text(encoding="utf-8-sig")
    )
    main_dfm_source = main_dfm_path.read_text(encoding="utf-8-sig")
    (
        main_objects,
        main_events,
        dfm_shortcuts,
        bound_handlers,
    ) = parse_main_dfm(main_dfm_source)
    main_handlers = parse_main_handlers(main_source, bound_handlers)
    keyboard_branches = parse_main_keyboard_branches(main_source)
    log_surfaces = parse_log_surfaces(
        mask_pascal_comments(log_path.read_text(encoding="utf-8-sig"))
    )
    simulation_surfaces = parse_simulation_units(legacy_root)
    vcl_surfaces, vcl_sources = parse_vcl_units(legacy_root)
    support_surfaces, support_sources, support_source_texts = (
        parse_support_units(legacy_root)
    )
    operational_source_texts = {
        "Ini.pas": source,
        "Main.pas": main_source,
        "Log.pas": mask_pascal_comments(
            log_path.read_text(encoding="utf-8-sig")
        ),
        "Station.pas": mask_pascal_comments(
            (legacy_root / "Station.pas").read_text(encoding="utf-8-sig")
        ),
        **support_source_texts,
    }
    for source_name in vcl_sources:
        operational_source_texts[source_name] = mask_pascal_comments(
            (legacy_root / Path(source_name)).read_text(encoding="utf-8-sig")
        )
    data_references = parse_data_references(
        legacy_root,
        operational_source_texts,
    )
    operational_paths = parse_operational_paths(operational_source_texts)
    additional_forms, additional_form_sources = (
        parse_additional_form_resources(legacy_root, tracked_files)
    )
    project_surfaces, project_sources = parse_project_metadata(
        legacy_root,
        tracked_files,
    )
    form_resource_directives, form_directive_sources = (
        parse_form_resource_directives(legacy_root, tracked_files)
    )
    bundled_assets, asset_sources = parse_bundled_assets(
        legacy_root,
        tracked_files,
    )
    distribution_files, distribution_sources = parse_distribution_files(
        legacy_root,
        tracked_files,
    )
    external_integrations, integration_sources = (
        parse_external_integrations(legacy_root, tracked_files)
    )
    data_parser_paths, data_parser_sources = parse_data_parser_paths(
        legacy_root,
        tracked_files,
    )
    pascal_tests, pascal_test_sources = parse_pascal_tests(
        legacy_root,
        tracked_files,
    )
    smoke_tests, smoke_test_sources = parse_smoke_tests(
        legacy_root,
        tracked_files,
    )
    unit_lifecycle, lifecycle_sources = parse_unit_lifecycle(
        legacy_root,
        tracked_files,
    )
    regex_surfaces, regex_sources = parse_regex_units(
        legacy_root,
        tracked_files,
    )
    additional_inputs, additional_input_sources = (
        parse_additional_repository_inputs(legacy_root, tracked_files)
    )
    surfaces = (
        contest_enumeration
        + run_modes
        + contest_definitions
        + settings
        + main_objects
        + main_events
        + main_handlers
        + dfm_shortcuts
        + keyboard_branches
        + log_surfaces
        + simulation_surfaces
        + vcl_surfaces
        + support_surfaces
        + data_references
        + operational_paths
        + additional_forms
        + project_surfaces
        + form_resource_directives
        + bundled_assets
        + distribution_files
        + external_integrations
        + data_parser_paths
        + pascal_tests
        + smoke_tests
        + unit_lifecycle
        + regex_surfaces
        + additional_inputs
    )
    validate_unique_surface_ids(surfaces)

    original_reference_sources = [
        "Ini.pas",
        "Main.pas",
        "Main.dfm",
        "Log.pas",
        "Contest.pas",
        "Station.pas",
        "DxOper.pas",
        "DxStn.pas",
        "StnColl.pas",
        "MyStn.pas",
        "QrmStn.pas",
        "QrnStn.pas",
        *vcl_sources,
        *support_sources,
    ]
    expanded_reference_sources = sorted(
        {
            *additional_form_sources,
            *project_sources,
            *form_directive_sources,
            *asset_sources,
            *distribution_sources,
            *integration_sources,
            *data_parser_sources,
            *pascal_test_sources,
            *smoke_test_sources,
            *lifecycle_sources,
            *regex_sources,
            *additional_input_sources,
        }.difference(original_reference_sources),
        key=str.casefold,
    )
    inventoried_sources = (
        original_reference_sources + expanded_reference_sources
    )
    exclusions = tracked_file_exclusions(tracked_files)
    validate_tracked_file_classification(
        tracked_files,
        inventoried_sources,
        exclusions,
    )
    validate_reference_source_surfaces(inventoried_sources, surfaces)

    return {
        "schemaVersion": 2,
        "reference": {
            "revision": revision,
            "trackedFileCount": len(tracked_files),
            "trackedFilesSha256": tracked_files_sha256(tracked_files),
            "sources": inventoried_sources,
            "exclusions": exclusions,
        },
        "surfaces": surfaces,
    }


def build_inventory(legacy_root: Path) -> dict[str, Any]:
    revision = subprocess.run(
        ["git", "-C", str(legacy_root), "rev-parse", "--verify", "HEAD"],
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()
    tracked_files = list_tracked_files(legacy_root, revision)

    with tempfile.TemporaryDirectory(
        prefix="morse-runner-legacy-inventory-"
    ) as temporary_directory:
        canonical_root = Path(temporary_directory)
        materialize_canonical_git_tree(
            legacy_root,
            revision,
            tracked_files,
            canonical_root,
        )
        return build_inventory_from_canonical_tree(
            canonical_root,
            revision,
            tracked_files,
        )


def serialize_inventory(inventory: dict[str, Any]) -> str:
    return json.dumps(inventory, indent=2, ensure_ascii=False) + "\n"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--legacy-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--check", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    inventory = build_inventory(args.legacy_root.resolve())
    rendered = serialize_inventory(inventory)

    if args.check:
        if not args.output.is_file():
            raise SystemExit(f"inventory file not found: {args.output}")
        if args.output.read_text(encoding="utf-8") != rendered:
            raise SystemExit(
                "legacy surface inventory is stale; regenerate it with "
                "tools/parity/inventory_legacy.py"
            )
        print(f"Legacy surface inventory is current: {len(inventory['surfaces'])}")
        return 0

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(rendered, encoding="utf-8", newline="\n")
    print(f"Wrote {len(inventory['surfaces'])} surfaces to {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
