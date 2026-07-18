# Python Tooling Instructions

Python is permitted for repository tooling and validation, not for the shipped
MorseRunnerXPlat runtime.

- Use the Python version pinned by `.python-version`.
- Declare every dependency in `pyproject.toml`.
- Commit `uv.lock`.
- Use `uv sync --locked` to create or verify `.venv`.
- Use `uv run --locked` for scripts.
- Use `uv add` and `uv remove` for dependency changes.
- Never use `pip install`, Poetry, Pipenv, requirements files, or a manually
  managed virtual environment.
- Keep Python code under `tools\`, test tooling, or build automation.
- Keep `.venv`, caches, and bytecode out of Git.
- Do not introduce Python into `src\` or any runtime-critical audio, DSP,
  engine, transport, or UX path.
