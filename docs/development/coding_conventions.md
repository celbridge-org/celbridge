# Coding Conventions

Conventions for every language in the codebase. The general section applies everywhere; the
per-language sections add to it.

Formatting (indent width, line endings, trailing whitespace, final newline) is owned by
`Source/.editorconfig` and is not restated here.

## General

- Use full descriptive variable names, never abbreviate
- Do not add section marker comments like `// -- Initialization --`
- Use "folder" not "directory" in naming (exception: external APIs)
- Use LF line endings. Enforced by `.gitattributes` at the repo root and `Source/.editorconfig`. Coding agents do not need to do anything special: write LF as normal. Contributors need no manual Git config; a `core.autocrlf=true` checkout is overridden by `.gitattributes`
- Prefer temporary variables over inline instances; break complex logic into simpler steps rather than chaining operations
- Define collection initialization using multiple lines, never on a single line
- Only use ternary expressions for trivial logic
- Split multi-condition `if` statements so each clause is on its own line, with the logical operator (`&&`, `||`) at the end of the preceding line
- Do not use special characters like arrows or emojis in code comments
- Use full stops rather than semicolons in comment and documentation prose. This applies to English text only, not C# statement terminators
- Keep inline body comments terse — write only what a first-time reader needs to know that they can't read off the code. Don't narrate what the current change is about, don't recap rationale visible in the surrounding code, don't enumerate edge cases the reader can infer. If a comment approaches paragraph length, the code probably needs restructuring instead
- Unit tests should cover the happy case and the most common failure modes; do not aim for complete coverage for its own sake

## C#

- Never use `#region` / `#endregion`
- Order interface methods by lifecycle stage; match that order in implementations
- Follow the patterns in `ProjectConfigParser.cs` as a reference for coding style
- Prefer explicit record classes with meaningful property names over anonymous types for message contracts
- Code-behind files use `.xaml.cs` naming convention (e.g., `MyView.xaml.cs`)
- Never use `/// <param>` XML documentation — it is verbose and hard to keep synchronized (exception: MCP tool methods in `Celbridge.Tools` where the MCP SDK source generator requires them for parameter descriptions)
- Always use localized strings for user-facing text: add entries to `Resources.resw` and access via `IStringLocalizer.GetString()` in code-behind, then bind with `{x:Bind}`
- Localized strings for the settings dialog follow `Settings_<Category>_<Element>`, where the category is the one the string appears under in the dialog rail (Appearance, Workshop, Web View), not the `SettingCatalog.cs` descriptor group. The two mostly coincide, but the categories are a presentation grouping: Appearance shows `SettingCatalog.Application.Theme`, and Web View has no catalog group at all. Strings shown elsewhere keep their existing `Section_Element` conventions
- Place `Dispose` implementation at the end of a class; declare all private fields at the top
- Put a blank line between the final `return` of a method and the preceding code block (e.g., after a closing `}`)
- Keep the `async` keyword on `*Async` methods even when the body is synchronous; suppress CS1998 by adding `await Task.CompletedTask;` at the top of the body (precedent: `DocumentView.SaveDocumentContentAsync`)
- Use the Parameter Object pattern for methods with 4+ parameters: identity args (what/where) stay as direct arguments; behavioral/option args group into a record
- Prefer small record types over named tuples for multi-value returns, especially when nullable-wrapping the record can replace field-level nullability
- Colocate small helper types (under ~15 lines, single primary consumer) with their consumer rather than in dedicated files
- Use the project's `ILogger<T>` for all diagnostics; never use `Debug.WriteLine`, `Console.Write*`, or `Trace.Write*`. For abstract base classes where constructor injection would cascade, use `ServiceLocator.AcquireService<ILogger<T>>()` (precedent: `DocumentView`)
- When logging an exception, pass the exception object to the logger overload (e.g. `_logger.LogError(ex, "...")`); do not interpolate `ex.Message` or `ex.ToString()` into the message string
- Keep XML doc comments concise but informative: one or two `<summary>` sentences describing *what* the member does, written so a reader who hasn't seen the class can understand it. If one line would just rephrase the member name (e.g. `"Typed counterpart of X"`), use two — conciseness is the constraint, not the goal. Do not embed implementation rationale, caller behavior, or detail already carried by types (enums, records, nullable returns). Avoid inline formatting tags (`<c>`, `<list>`, `<item>`) and multi-paragraph `<remarks>` blocks; plain type names read fine without `<see cref>` prose in summaries
- Interface members and public types in `Celbridge.Foundation` must always carry a concise `<summary>` — the Foundation abstractions are how a reader understands the system, so every interface method, public record, and public enum there needs enough comment to stand alone. Conversely, skip xmldoc on concrete-class members by default: the interface they implement already documents them, and duplicated comments drift out of sync with the implementation. Exception: when the implementation has behavior that isn't obvious from the signature (unusual threading constraints, hidden side effects, non-obvious failure modes, subtle invariants), add a brief note. Treat the exception as rare — if the summary would just restate the name or repeat the interface comment, skip it
- Model user or programmatic cancellation as a typed success outcome (e.g., `Result<OutcomeEnum>` with a `Cancelled` value), not as `Result.Fail`; `Result.Fail` stays reserved for genuine errors (precedent: `OpenDocumentOutcome`, `CloseDocumentOutcome`)
- Minimize `Result<T>` boilerplate at return sites: use implicit conversions (`return value;` for concrete types; `return Result.Fail("message");` for failures). For interface return types, use the `OkResult<T>()` extension from `ResultExtensions`. Always unpack `result.Value` into a named temporary variable before using it

## JavaScript

Our own JavaScript is the shared client (`Source/Core/Celbridge.WebHost/Web/celbridge-client/`), the
WebView editors under `Source/Modules/`, the console web app, and the vendoring scripts. Third party
bundles under `lib/` and `min/vs/` are not ours: see Vendored code below.

### Modules

- Everything is an ES module. Every `package.json` declares `"type": "module"`, including the workspaces root at `Source/package.json`
- Config files are plain `.js`. Do not add `.mjs` or `.cjs` files: declaring the module type in `package.json` is what makes `.js` unambiguous, and the per-file extensions exist only to work around not doing that
- Browser code is loaded by URL from the loopback file server, so imports use root-relative paths (`/assets/celbridge-client/...`) rather than package specifiers

### Linting

- ESLint runs as a single invocation from `Source/` (`npm run lint`), configured by `Source/eslint.config.js`. It is deliberately not wired per workspace: several linted folders (the Notes, FileViewer and UtilityDemo editors, the Spreadsheet package, the vendoring scripts) are not npm workspaces, and a per-workspace script would silently skip them
- The rule set is `@eslint/js` recommended and nothing else. Do not add stylistic or formatting rules, because `.editorconfig` owns formatting
- `preserve-caught-error` is disabled. Rethrowing without attaching the caught error as a `cause` is accepted here
- Libraries that arrive from a script tag rather than an import (`monaco` and the Monaco AMD `require`, SpreadJS `GC`, the xterm `Terminal` and its addons) are declared as globals per directory in the ESLint config. Declare a new one there rather than working around `no-undef` in code
- Use `catch { }` with no binding when the caught error is unused

### Tests

- Vitest, run from `Source/` with `npm test`. Each workspace has its own `test` script
- Import `describe`, `it`, `expect` and friends explicitly from `vitest`; globals are not enabled

### Vendored code

- The `lib/` folders (xterm, TipTap) and `min/vs/` (Monaco) hold third party bundles. Never edit or lint them
- They are regenerated by the vendoring scripts, run from `Source/` as `npm run vendor:icons`, `npm run vendor:notes` and `npm run vendor:console`

## Python

The Python package is `Source/Workspace/Celbridge.Python/packages/celbridge`, targeting Python 3.10
and later.

No linter or formatter is configured, so unlike C# and JavaScript nothing here is enforced by tooling.
The conventions below describe the existing code, and new code should match it.

- PEP 8 naming: `snake_case` for modules, functions and variables, `PascalCase` for classes, a leading underscore for non-public names
- Every module opens with a docstring: a one-sentence summary, then a blank line and a paragraph where the module needs more explanation
- Public functions and classes carry a docstring. One line is enough where one line says it
- Use double-quoted strings
- Use modern builtin generics (`list[dict]`, not `typing.List`). `from __future__ import annotations` is not used
- Acquire a module-level logger as `logger = logging.getLogger(__name__)`
- Type hints appear on some public helpers but are not applied consistently across the package. There is no rule today either way

### Tests

There are two suites, and they are run in different ways.

- **Unit tests** live in `packages/celbridge/tests/` and run under pytest via `run_tests.py`. Write them as plain functions (`def test_...`) using bare `assert`. `unittest.TestCase` is not used, though `unittest.mock` is used for mocking
- Give each test a one-line docstring saying what it establishes
- **Integration tests** live in `src/celbridge/integration_tests/` and ship inside the wheel, because they run against a live Celbridge application from the Python REPL rather than in CI. Shared fixtures are session-scoped in `conftest.py`
