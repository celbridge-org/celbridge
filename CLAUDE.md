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
- Always use localized strings for user-facing text: add entries to `Resources.resw` and access via `IStringLocalizer.GetString()` in code-behind, then bind with `{x:Bind}`
- Unit tests should cover the happy case and the most common failure modes; do not aim for complete coverage for its own sake
- Place `Dispose` implementation at the end of a class; declare all private fields at the top
- Split multi-condition `if` statements so each clause is on its own line, with the logical operator (`&&`, `||`) at the end of the preceding line
- Put a blank line between the final `return` of a method and the preceding code block (e.g., after a closing `}`)
- Keep the `async` keyword on `*Async` methods even when the body is synchronous; suppress CS1998 by adding `await Task.CompletedTask;` at the top of the body (precedent: `DocumentView.SaveDocumentContentAsync`)
- Use the Parameter Object pattern for methods with 4+ parameters: identity args (what/where) stay as direct arguments; behavioral/option args group into a record
- Prefer small record types over named tuples for multi-value returns, especially when nullable-wrapping the record can replace field-level nullability
- Colocate small helper types (under ~15 lines, single primary consumer) with their consumer rather than in dedicated files
- Use the project's `ILogger<T>` for all diagnostics; never use `Debug.WriteLine`, `Console.Write*`, or `Trace.Write*`. For abstract base classes where constructor injection would cascade, use `ServiceLocator.AcquireService<ILogger<T>>()` (precedent: `WebViewDocumentView`, `CodeEditorDocumentView`)
- When logging an exception, pass the exception object to the logger overload (e.g. `_logger.LogError(ex, "...")`); do not interpolate `ex.Message` or `ex.ToString()` into the message string
- Keep XML doc comments concise but informative: one or two `<summary>` sentences describing *what* the member does, written so a reader who hasn't seen the class can understand it. If one line would just rephrase the member name (e.g. `"Typed counterpart of X"`), use two — conciseness is the constraint, not the goal. Do not embed implementation rationale, caller behavior, or detail already carried by types (enums, records, nullable returns). Avoid inline formatting tags (`<c>`, `<list>`, `<item>`) and multi-paragraph `<remarks>` blocks; plain type names read fine without `<see cref>` prose in summaries
- Interface members and public types in `Celbridge.Foundation` must always carry a concise `<summary>` — the Foundation abstractions are how a reader understands the system, so every interface method, public record, and public enum there needs enough comment to stand alone. Conversely, skip xmldoc on concrete-class members by default: the interface they implement already documents them, and duplicated comments drift out of sync with the implementation. Exception: when the implementation has behavior that isn't obvious from the signature (unusual threading constraints, hidden side effects, non-obvious failure modes, subtle invariants), add a brief note. Treat the exception as rare — if the summary would just restate the name or repeat the interface comment, skip it
- Model user or programmatic cancellation as a typed success outcome (e.g., `Result<OutcomeEnum>` with a `Cancelled` value), not as `Result.Fail`; `Result.Fail` stays reserved for genuine errors (precedent: `OpenDocumentOutcome`, `CloseDocumentOutcome`)
- Minimize `Result<T>` boilerplate at return sites: use implicit conversions (`return value;` for concrete types; `return Result.Fail("message");` for failures). For interface return types, use the `OkResult<T>()` extension from `ResultExtensions`. Always unpack `result.Value` into a named temporary variable before using it

## Architecture

- Workspace-scoped services are transient and must NOT be injected via constructor DI. Access them through `_workspaceWrapper.WorkspaceService`:
  - IWorkspaceSettingsService, IWorkspaceSettings, IResourceRegistry, IResourceTransferService, IResourceOperationService, IPythonService, IConsoleService, IDocumentsService, IExplorerService, IInspectorService, IDataTransferService, IEntityService, IGenerativeAIService, IActivityService
- Project configuration: use `IProjectService.CurrentProject` (singleton) to access the current project, and `project.Config` for its config. To parse `.celbridge` files outside of project loading, use `ProjectConfigParser.ParseFromFile()`
- The Foundation project (`Core\Celbridge.Foundation`) should only contain abstractions (interfaces, abstract classes), never concrete implementations
- Never bypass `ICommandService` to call methods directly. Every important operation goes through the command service for automation and auditing support. If a command-based flow has a bug, fix it within the command service pattern (e.g., add new command options or fix the command handling logic)

## Save Model

Documents auto-save via `DocumentViewModel.OnDataChanged()` → per-view save timer (~1s). There is no user-facing Save command and no "unsaved changes" state; users recover via undo/redo.

- Do not add save commands, shortcuts, or UI affordances. If on-demand flushing is needed, route through `IDocumentView.SaveDocument()` (precedent: `ApplyEditsCommand.ForceSave`).
- Do not add "discard unsaved changes?" prompts on close — closing always saves.
- Route programmatic edits through `IDocumentView.ApplyEditsAsync` so they join the editor's undo stack. `ApplyEditsCommand` with `OpenDocument=false` is the explicit exception for background edits where an undo entry has no meaning.
- `MonitoredResourceChangedMessage` fires on every save; `DocumentViewModel` filters self-triggered events via `IsSavingFile` + hash. New consumers should expect high-frequency events.

## MCP Tools

MCP tool classes in `Celbridge.Tools` use the MCP SDK's `XmlToDescriptionGenerator` source generator, which converts XML doc comments into `[Description]` attributes at build time. This means:

- Tool classes must be `partial class` and tool methods must be `partial`
- Use `/// <summary>` for tool and parameter descriptions (not `[Description]` attributes)
- Use `/// <param>` tags to describe parameters
- Use `/// <returns>` tags to document the return type for all tools that return a value — MCP has no output schema, so the return structure must be described in the documentation
- Do not add `using System.ComponentModel`
