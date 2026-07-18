# .NET Instructions

- Target the SDK and language versions pinned by `global.json` and shared build
  properties once those files exist.
- Enable nullable reference types, implicit usings, deterministic builds, and
  warnings as errors in repository code.
- Centralize package versions in `Directory.Packages.props`.
- Prefer project references over copying shared code.
- Keep async cancellation explicit at I/O and service boundaries.
- Do not use `Task.Run` to conceal blocking work.
- Use `IAsyncDisposable` for asynchronous native or transport lifetimes.
- Avoid reflection and dynamic dispatch in trim-sensitive paths.
- Use invariant culture for persisted or transported numeric data.
- Do not suppress analyzers broadly. Justify narrow suppressions locally.
- Keep platform-specific code in adapter projects and guard it explicitly.
