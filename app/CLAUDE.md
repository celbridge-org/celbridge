# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

The primary build target is WinAppSDK Packaged (`net9.0-windows10.0.22621`), which requires MSBuild (not `dotnet build`).

```bash
# Build the entire solution (use MSBuild, not dotnet build)
msbuild Celbridge.sln /p:Configuration=Debug /p:Platform=x64

# Build a specific project
msbuild Celbridge/Celbridge.csproj /p:Configuration=Debug /p:Platform=x64

# Run all tests (tests can use dotnet)
dotnet test Celbridge.Tests/Celbridge.Tests.csproj

# Run a single test by name
dotnet test Celbridge.Tests/Celbridge.Tests.csproj --filter "FullyQualifiedName~TestClassName.TestMethodName"
```

## Architecture

Celbridge is a visual programming IDE built on Uno Platform (.NET 9.0, C# 12+). The codebase follows a layered architecture:

**Core/** - Infrastructure and abstractions
- `Celbridge.Foundation` - Interfaces, abstract classes, message types, constants. **No concrete implementations allowed here.**
- `Celbridge.Commands` - Command pattern with async execution
- `Celbridge.Messaging` - Pub/Sub messaging system (IMessengerService)
- `Celbridge.Projects` - TOML-based project configuration (`.celbridge` files)
- `Celbridge.WebView` - WebView integration with JS interop
- Other core services: Logging (NLog), Settings, Modules, Host, Utilities

**Modules/** - Self-contained pluggable feature modules (Code editor via Monaco, Markdown, Spreadsheet, Notes, etc.)

**Workspace/** - Workspace-scoped features (Documents, Explorer, Console, Inspector, Entities, Python, GenerativeAI, etc.)

**Celbridge/** - Main WinUI application entry point

**Celbridge.Tests/** - NUnit tests with FluentAssertions and NSubstitute

## Critical Conventions

### Workspace-scoped services must NOT be injected via constructor DI
Access them through `_workspaceWrapper.WorkspaceService` instead. These services are transient with workspace scope:
`IWorkspaceSettingsService`, `IWorkspaceSettings`, `IResourceRegistry`, `IResourceTransferService`, `IResourceOperationService`, `IPythonService`, `IConsoleService`, `IDocumentsService`, `IExplorerService`, `IInspectorService`, `IDataTransferService`, `IEntityService`, `IGenerativeAIService`, `IActivityService`, `IWorkspaceFeatures`

### Project configuration access
- Current project: `IProjectService.CurrentProject` (singleton)
- Config: `project.Config` (property, not a service)
- Parse `.celbridge` files outside project loading: `ProjectConfigParser.ParseFromFile()`

### Feature flags
- Application-level: `IFeatureFlagService` (singleton, reads from appsettings.json)
- Workspace-level: `IWorkspaceFeatures` (workspace-scoped, checks `.celbridge` first, falls back to appsettings.json)
- Use kebab-case names (e.g., "console-panel")
- Use nullable types for optional features controlled by flags (e.g., `IConsolePanel?`), not the Null Object pattern

### Code style
- File-scoped namespaces (enforced as warning)
- Primary constructors preferred
- Use temporary variables to break down complex logic; avoid chaining multiple operations on one line
- Multi-line collection initialization, never single-line
- All user-facing text must use localized strings via `IStringLocalizer.GetString()` and `Resources.resw`
- `Dispose` implementations go at the end of a class
- Private fields declared at the top of the class
- No `/// <param>` XML doc tags
- No emojis or special characters in code comments
- Prefer record classes over anonymous types for message contracts
