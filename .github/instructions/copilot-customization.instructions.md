# Copilot Customization Instructions

## Canonical ownership

- Durable project rules live in root `AGENTS.md`.
- Shared reusable skills live in `.agents/skills/<name>/`.
- Copilot skill adapters mirror them under `.github/skills/<name>/`.
- Named Copilot agents live in `.github/agents/`.
- Reusable prompts live in `.github/prompts/`.
- Codex-specific runtime configuration lives in `.codex/`.

## Frontmatter

Skills, agents, and prompts start on line 1 with YAML frontmatter containing
lowercase hyphenated `name` and a descriptive `description`. A skill name must
match its folder.

Instruction files are plain Markdown without frontmatter.

## Maintenance

- Keep shared and Copilot skill copies byte-identical.
- Keep provider adapters concise and point to `AGENTS.md`.
- Manage repository Python tooling only through `uv`; keep `uv.lock` current.
- Run `uv sync --locked` and
  `uv run --locked python tools/agent_scaffolding/validate_yaml.py`.
- Run `.github/hooks/scripts/validate-agent-scaffolding.ps1` after changes.
- Do not add provider credentials or personal MCP configuration.
