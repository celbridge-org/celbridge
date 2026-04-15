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

## Architecture

- Workspace-scoped services are transient and must NOT be injected via constructor DI. Access them through `_workspaceWrapper.WorkspaceService`:
  - IWorkspaceSettingsService, IWorkspaceSettings, IResourceRegistry, IResourceTransferService, IResourceOperationService, IPythonService, IConsoleService, IDocumentsService, IExplorerService, IInspectorService, IDataTransferService, IEntityService, IGenerativeAIService, IActivityService
- Project configuration: use `IProjectService.CurrentProject` (singleton) to access the current project, and `project.Config` for its config. To parse `.celbridge` files outside of project loading, use `ProjectConfigParser.ParseFromFile()`
- The Foundation project (`Core\Celbridge.Foundation`) should only contain abstractions (interfaces, abstract classes), never concrete implementations
- Never bypass `ICommandService` to call methods directly. Every important operation goes through the command service for automation and auditing support. If a command-based flow has a bug, fix it within the command service pattern (e.g., add new command options or fix the command handling logic)

## MCP Tools

MCP tool classes in `Celbridge.Tools` use the MCP SDK's `XmlToDescriptionGenerator` source generator, which converts XML doc comments into `[Description]` attributes at build time. This means:

- Tool classes must be `partial class` and tool methods must be `partial`
- Use `/// <summary>` for tool and parameter descriptions (not `[Description]` attributes)
- Use `/// <param>` tags to describe parameters
- Use `/// <returns>` tags to document the return type for all tools that return a value — MCP has no output schema, so the return structure must be described in the documentation
- Do not add `using System.ComponentModel`
