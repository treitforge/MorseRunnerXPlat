from __future__ import annotations

import re
import sys
from pathlib import Path
from typing import Any

import yaml


ROOT = Path(__file__).resolve().parents[2]
NAME_PATTERN = re.compile(r"^[a-z0-9-]+$")
REQUIRED_INTERFACE_KEYS = {"display_name", "short_description", "default_prompt"}


class ValidationFailure(Exception):
    pass


def relative(path: Path) -> str:
    return str(path.relative_to(ROOT))


def read_yaml(path: Path) -> Any:
    try:
        return yaml.safe_load(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, yaml.YAMLError) as error:
        raise ValidationFailure(f"{relative(path)}: invalid YAML: {error}") from error


def read_frontmatter(path: Path) -> dict[str, Any]:
    try:
        text = path.read_text(encoding="utf-8")
    except (OSError, UnicodeError) as error:
        raise ValidationFailure(f"{relative(path)}: cannot read file: {error}") from error

    if not text.startswith("---\n"):
        raise ValidationFailure(f"{relative(path)}: frontmatter must start on line 1")

    closing = text.find("\n---\n", 4)
    if closing < 0:
        raise ValidationFailure(f"{relative(path)}: frontmatter is not closed")

    try:
        parsed = yaml.safe_load(text[4:closing])
    except yaml.YAMLError as error:
        raise ValidationFailure(
            f"{relative(path)}: invalid frontmatter YAML: {error}"
        ) from error

    if not isinstance(parsed, dict):
        raise ValidationFailure(f"{relative(path)}: frontmatter must be a mapping")

    return parsed


def validate_named_frontmatter(path: Path, expected_name: str | None = None) -> None:
    frontmatter = read_frontmatter(path)
    name = frontmatter.get("name")
    description = frontmatter.get("description")

    if not isinstance(name, str) or not NAME_PATTERN.fullmatch(name):
        raise ValidationFailure(
            f"{relative(path)}: name must contain lowercase letters, digits, or hyphens"
        )
    if expected_name is not None and name != expected_name:
        raise ValidationFailure(
            f"{relative(path)}: name {name!r} does not match {expected_name!r}"
        )
    if not isinstance(description, str) or len(description.strip()) < 20:
        raise ValidationFailure(
            f"{relative(path)}: description must contain at least 20 characters"
        )


def validate_skill(skill_dir: Path) -> None:
    name = skill_dir.name
    skill_path = skill_dir / "SKILL.md"
    metadata_path = skill_dir / "agents" / "openai.yaml"
    mirror_path = ROOT / ".github" / "skills" / name / "SKILL.md"

    if not skill_path.is_file():
        raise ValidationFailure(f"{relative(skill_path)}: missing")
    if not metadata_path.is_file():
        raise ValidationFailure(f"{relative(metadata_path)}: missing")
    if not mirror_path.is_file():
        raise ValidationFailure(f"{relative(mirror_path)}: missing")

    validate_named_frontmatter(skill_path, name)

    metadata = read_yaml(metadata_path)
    if not isinstance(metadata, dict) or not isinstance(metadata.get("interface"), dict):
        raise ValidationFailure(
            f"{relative(metadata_path)}: interface mapping is required"
        )

    missing = REQUIRED_INTERFACE_KEYS - metadata["interface"].keys()
    if missing:
        raise ValidationFailure(
            f"{relative(metadata_path)}: missing interface keys {sorted(missing)}"
        )

    if skill_path.read_bytes() != mirror_path.read_bytes():
        raise ValidationFailure(f"{name}: shared and Copilot SKILL.md files differ")


def main() -> int:
    errors: list[str] = []

    for skill_dir in sorted((ROOT / ".agents" / "skills").iterdir()):
        if not skill_dir.is_dir():
            continue
        try:
            validate_skill(skill_dir)
        except ValidationFailure as error:
            errors.append(str(error))

    frontmatter_patterns = (
        ".github/agents/*.agent.md",
        ".github/prompts/*.prompt.md",
    )
    for pattern in frontmatter_patterns:
        for path in sorted(ROOT.glob(pattern)):
            try:
                validate_named_frontmatter(path)
            except ValidationFailure as error:
                errors.append(str(error))

    if errors:
        print("YAML and skill validation failed:", file=sys.stderr)
        for error in errors:
            print(f" - {error}", file=sys.stderr)
        return 1

    print("YAML and skill validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
