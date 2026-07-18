from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
from collections import defaultdict
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_OUTPUT = ROOT / "tests" / "parity" / "legacy-surface-inventory.json"


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


def build_inventory(legacy_root: Path) -> dict[str, Any]:
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

    revision = subprocess.run(
        ["git", "-C", str(legacy_root), "rev-parse", "--verify", "HEAD"],
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()
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
    )

    return {
        "schemaVersion": 1,
        "reference": {
            "revision": revision,
            "sources": [
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
            ],
        },
        "surfaces": surfaces,
    }


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
