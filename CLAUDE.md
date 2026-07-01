# Celbridge - Claude Code Instructions

## Project Overview

Celbridge is a cross-platform desktop application built with Uno Platform and WinUI. The solution is at `Celbridge.slnx` in the repo root.

## Building

We recommend building with the latest Visual Studio 2026. This is an Uno Platform project with XAML files targeting WinUI/WinAppSDK. The WinUI projects require MSBuild (not `dotnet build`) because Uno SDK raises error UNOB0008 when XAML files are present.

Use the MSBuild that ships with your Visual Studio installation:

```
msbuild Celbridge.slnx -t:Build -p:Configuration=Debug -verbosity:minimal -nologo
```

If `msbuild` is not on your PATH (e.g. outside of a Developer PowerShell), it is typically located at:

```
C:/Program Files/Microsoft Visual Studio/<version>/<edition>/MSBuild/Current/Bin/MSBuild.exe
```

For example, with VS 2026 Community: `C:/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe`.

## Running Tests

The test project does not contain XAML and can be built and run with `dotnet`:

```
dotnet test Source/Tests/Celbridge.Tests.csproj
```

Run JS tests from the `Source/` folder:

```
cd Source && npm test
```

Run Python tests using a virtual environment:

```
cd Source/Workspace/Celbridge.Python
python -m venv .venv
.venv\Scripts\activate
pip install -e "packages/celbridge[dev]"
python run_tests.py
```

## Git

- Never commit automatically; the user reviews all changes in GitHub Desktop before committing
- Do not add `Co-Authored-By` lines to commit messages

## Code Conventions

- Use full descriptive variable names, never abbreviate
- Do not add section marker comments like `// -- Initialization --`
- Never use `#region` / `#endregion`
- Order interface methods by lifecycle stage; match that order in implementations
- Use "folder" not "directory" in naming (exception: external APIs)
- Use CRLF line endings (Windows project)
- Follow the patterns in `ProjectConfigParser.cs` as a reference for coding style
- Prefer temporary variables over inline instances; break complex logic into simpler steps rather than chaining operations
- Define collection initialization using multiple lines, never on a single line
- Only use ternary expressions for trivial logic
- Prefer explicit record classes with meaningful property names over anonymous types for message contracts
- Code-behind files use `.xaml.cs` naming convention (e.g., `MyView.xaml.cs`)
- Never use `/// <param>` XML documentation — it is verbose and hard to keep synchronized (exception: MCP tool methods in `Celbridge.Tools` where the MCP SDK source generator requires them for parameter descriptions)
- Do not use special characters like arrows or emojis in code comments
- Use full stops rather than semicolons in comment and documentation prose. This applies to English text only, not C# statement terminators
- Always use localized strings for user-facing text: add entries to `Resources.resw` and access via `IStringLocalizer.GetString()` in code-behind, then bind with `{x:Bind}`
- Localized strings for the Settings page follow `Settings_<Group>_<Element>`, mirroring the descriptor groups defined in `SettingCatalog.cs` (the `SettingCatalog` catalog class). Strings shown elsewhere keep their existing `Section_Element` conventions
- Unit tests should cover the happy case and the most common failure modes; do not aim for complete coverage for its own sake
- Place `Dispose` implementation at the end of a class; declare all private fields at the top
- Split multi-condition `if` statements so each clause is on its own line, with the logical operator (`&&`, `||`) at the end of the preceding line
- Put a blank line between the final `return` of a method and the preceding code block (e.g., after a closing `}`)
- Keep the `async` keyword on `*Async` methods even when the body is synchronous; suppress CS1998 by adding `await Task.CompletedTask;` at the top of the body (precedent: `DocumentView.SaveDocumentContentAsync`)
- Use the Parameter Object pattern for methods with 4+ parameters: identity args (what/where) stay as direct arguments; behavioral/option args group into a record
- Prefer small record types over named tuples for multi-value returns, especially when nullable-wrapping the record can replace field-level nullability
- Colocate small helper types (under ~15 lines, single primary consumer) with their consumer rather than in dedicated files
- Use the project's `ILogger<T>` for all diagnostics; never use `Debug.WriteLine`, `Console.Write*`, or `Trace.Write*`. For abstract base classes where constructor injection would cascade, use `ServiceLocator.AcquireService<ILogger<T>>()` (precedent: `DocumentView`)
- When logging an exception, pass the exception object to the logger overload (e.g. `_logger.LogError(ex, "...")`); do not interpolate `ex.Message` or `ex.ToString()` into the message string
- Keep XML doc comments concise but informative: one or two `<summary>` sentences describing *what* the member does, written so a reader who hasn't seen the class can understand it. If one line would just rephrase the member name (e.g. `"Typed counterpart of X"`), use two — conciseness is the constraint, not the goal. Do not embed implementation rationale, caller behavior, or detail already carried by types (enums, records, nullable returns). Avoid inline formatting tags (`<c>`, `<list>`, `<item>`) and multi-paragraph `<remarks>` blocks; plain type names read fine without `<see cref>` prose in summaries
- Interface members and public types in `Celbridge.Foundation` must always carry a concise `<summary>` — the Foundation abstractions are how a reader understands the system, so every interface method, public record, and public enum there needs enough comment to stand alone. Conversely, skip xmldoc on concrete-class members by default: the interface they implement already documents them, and duplicated comments drift out of sync with the implementation. Exception: when the implementation has behavior that isn't obvious from the signature (unusual threading constraints, hidden side effects, non-obvious failure modes, subtle invariants), add a brief note. Treat the exception as rare — if the summary would just restate the name or repeat the interface comment, skip it
- Keep inline body comments terse — write only what a first-time reader needs to know that they can't read off the code. Don't narrate what the current change is about, don't recap rationale visible in the surrounding code, don't enumerate edge cases the reader can infer. If a comment approaches paragraph length, the code probably needs restructuring instead
- Model user or programmatic cancellation as a typed success outcome (e.g., `Result<OutcomeEnum>` with a `Cancelled` value), not as `Result.Fail`; `Result.Fail` stays reserved for genuine errors (precedent: `OpenDocumentOutcome`, `CloseDocumentOutcome`)
- Minimize `Result<T>` boilerplate at return sites: use implicit conversions (`return value;` for concrete types; `return Result.Fail("message");` for failures). For interface return types, use the `OkResult<T>()` extension from `ResultExtensions`. Always unpack `result.Value` into a named temporary variable before using it

## Architecture

- Workspace-scoped services are transient and must NOT be injected via constructor DI. Access them through `_workspaceWrapper.WorkspaceService`:
  - Directly on the workspace service: IWorkspaceSettingsService, IBindableWorkspaceSettings, IPythonService, IConsoleService, IDocumentsService, IExplorerService, IInspectorService, IDataTransferService, IEntityService, IGenerativeAIService, IActivityService
  - The resource-domain services live under `WorkspaceService.ResourceService`: Registry, RootHandlers, Monitor, Transfers, Operations, FileSystem, Policy, Trash, Scanner, Sidecars (e.g. `_workspaceWrapper.WorkspaceService.ResourceService.FileSystem`)
- Project configuration: use `IProjectService.CurrentProject` (singleton) to access the current project, and `project.Config` for its config. To parse `.celbridge` files outside of project loading, use `ProjectConfigParser.ParseFromFile()`
- The Foundation project (`Core\Celbridge.Foundation`) should only contain abstractions (interfaces, abstract classes), never concrete implementations
- Filesystem access goes through the `ILocalFileSystem` gateway, never the `System.IO` static facades (`File`, `Directory`, `FileInfo`, `DirectoryInfo`, `FileSystemWatcher`) directly. The `DirectFileSystemAccessAnalyzer` (`CEL_FS_001`, in `Celbridge.FileSystem.Analyzers`) fails the build on any direct use outside the `Celbridge.FileSystem` assembly. Legitimate exceptions (pre-DI bootstrap, embedded-resource reads, setting the process working directory) carry `[AllowDirectFileSystemAccess]` on the type or member. `System.IO.Path` (pure string manipulation) and stream types (`Stream`, `IOException`) are not gated
- Never bypass `ICommandService` to call methods directly. Every important operation goes through the command service for automation and auditing support. If a command-based flow has a bug, fix it within the command service pattern (e.g., add new command options or fix the command handling logic)

## Platform-specific code

All native interop and operating-system branching is contained in `Platform/` folders so platform code is discoverable by a single `**/Platform/**` glob. The convention has a few patterns plus a small set of documented exceptions.

- **The invariant.** No code outside a `Platform/` folder contains native interop (`DllImport`/P-Invoke, Objective-C runtime calls, WinRT interop, Uno-internals reflection). A layer that must vary by platform asks an injected capability rather than checking the OS inline. `PlatformContainmentTests` enforces the native-interop half (no `DllImport` outside `**/Platform/**`); the rest is convention plus the glob audit.
- **Homes.** Each project with native code has a `Platform/` folder (namespace `Celbridge.<Domain>.Platform`) holding its platform implementations and its platform DI selection (`Platform/PlatformServiceConfiguration.cs`). The two cross-cutting pieces — the shared `ObjectiveCRuntime` marshaling helper and `PlatformInfo` (the `IPlatformInfo` oracle) — live in `Celbridge.Utilities/Platform/`. Seam interfaces stay in their owning project (internal ones stay internal); `IPlatformInfo` and `IAppEnvironment` are the cross-cutting abstractions and live in `Celbridge.Foundation`.
- **Pattern A — native files move to `Platform/`.** A whole file of P-Invoke / Objective-C / WinRT interop, or a whole platform-only control (the Windows `TitleBar`), moves wholesale into the owning project's `Platform/` folder, keeping its existing framework types. Prefer unguarded native interop runtime-gated by `OperatingSystem.IsMacOS()` over an `#if !WINDOWS` file guard — DllImports are metadata until called, so they compile as harmless dead code on every head.
- **Pattern A-prime — per-head managed API behind an adapter seam.** Where a shared managed control (the WebView2 `CoreWebView2`) forks between an SDK call and the Skia/native path, route it through a DI-selected adapter (`IWebViewAdapter`) in `Platform/`, not an inline `#if`.
- **Pattern B — per-layer UI behaviour through `IPlatformInfo`.** A View, ViewModel, or helper that behaves differently per platform asks an injected `IPlatformInfo` capability (`UsesNativeMenuBar`, `CommandModifier`, `ReservesWindowCaptionButtons`, and so on), never `OperatingSystem.Is*`/`#if` inline. Use semantic capability names; a narrowly-named workaround capability (`RequiresMacOSSelectionRepaint`) is acceptable only where no semantic name exists, with a comment. Name such a workaround for the platform(s) it actually fires on (its `IsMacOS()`/`#if` value), not the rendering stack — `RequiresMacOS…`, not `RequiresSkia…`, since the Skia head also runs on Windows and Linux where the workaround does not apply. Each capability's `<summary>` ends with the platforms it holds on (e.g. "True on macOS.", "True on the packaged Windows head only.", "True on Windows (both the packaged and Skia heads)."). `IPlatformInfo` is the one Foundation interface that documents per-platform applicability — for this oracle that is part of the contract, and "packaged Windows head" means the WinAppSDK build, not the Skia head running on Windows.
- **Packaging and environment through `IAppEnvironment`.** Packaged-versus-unpackaged forks (bundled-asset paths, app-data and temp folders, app version) go through `IAppEnvironment` (Foundation interface, `AppEnvironment` impl in `Utilities/Platform/`), never an inline `#if WINDOWS`.

**Documented exceptions** — genuine OS branching outside `Platform/` that is neither native interop nor relocatable behind a capability. Keep these inline, with a comment explaining the platform difference:

- **Genuine compile-time TFM forks** — where a managed type truly differs or is absent on the Skia head, so it cannot be a runtime capability — are extracted into a `Platform/` seam (Windows impl + Skia no-op/alternative, DI-selected): see `IWindowActivationMonitor` (activation tint) and `IApplicationToolbarHost` (title-bar host). **Always probe before assuming a fork is compile-time** — a WinAppSDK member that *compiles* on Skia but no-ops there (present-but-stubbed) is a plain Type-B capability, not a fork (e.g. `ThemeHelper`'s title-bar colors → `ReservesWindowCaptionButtons`, `FilePickerService`'s HWND association → `PickersRequireWindowHandle`). After this work the only `#if WINDOWS` left outside `Platform/` is `App.xaml.cs` (next bullet).
- **Genuine runtime OS or backend selectors** where the behaviour depends on the actual running OS, not a UI head capability: the Python uv binary, archive, and shell (`PythonInstaller`, `PythonService`, `CommandLineBuilder`) and filesystem case-sensitivity (`PathComparison`).
- **App bootstrap / composition root.** `App.xaml.cs` is the one file outside `Platform/` that still uses `#if WINDOWS` — for the pre-DI log-folder path, the window-icon resizetizer workaround, and WinAppSDK file-activation. It runs before DI exists and is the natural composition root, so these stay inline (the same bootstrap exception the filesystem analyzer allows).
- **Tests** may assert platform-divergent behaviour directly; the convention governs production code.

## Save Model

Documents auto-save via `DocumentViewModel.OnDataChanged()` → per-view save timer (~1s). There is no user-facing Save command and no "unsaved changes" state; users recover via undo/redo.

- Do not add save commands, shortcuts, or UI affordances. If on-demand flushing is needed, route through `IDocumentView.SaveDocument()` (used by file-close and panel-close paths).
- Do not add "discard unsaved changes?" prompts on close — closing always saves.
- Programmatic edit commands (`EditFileCommand`, `MultiEditFileCommand`, `ReplaceFileCommand`, `ApplyRangeEditsCommand`, `WriteFileCommand`, `WriteBinaryFileCommand`) write straight to disk; there is no editor-routed code path. When the target file is open, the on-disk write triggers a watcher event and the document buffer reloads from disk via `editor.setValue`, which clears Monaco's undo history. Preserve that contract when adding new edit code paths — do not route writes through the open editor, and do not try to preserve undo state across a programmatic write.
- External edits always win: if a watcher event arrives while a save is queued or in flight, the save is discarded and the buffer reloads from disk. `DocumentViewModel.SaveTextToFileAsync` also raises `ReloadRequested` when the post-write disk hash differs from what we intended to write (i.e. an external write interleaved).
- `ResourceChangedMessage` fires on every save; `DocumentViewModel` filters self-triggered events by hash. New consumers should expect high-frequency events.

## MCP Tools

MCP tool classes in `Celbridge.Tools` use the MCP SDK's `XmlToDescriptionGenerator` source generator, which converts XML doc comments into `[Description]` attributes at build time. This means:

- Tool classes must be `partial class` and tool methods must be `partial`
- Use `/// <summary>` for tool descriptions (not `[Description]` attributes)
- The `<summary>` is a **discriminator**, not documentation: one short sentence (~100 chars) that helps an agent decide whether to **pick** this tool over other candidates. Do not add `<param>` or `<returns>` tags, do not write multi-paragraph blocks. Parameter semantics, return shape, gotchas, examples, and cross-references all go in the per-tool guide under `Source/Core/Celbridge.Tools/Guides/Tools/<tool_name>.md`
- Do not add `using System.ComponentModel`
