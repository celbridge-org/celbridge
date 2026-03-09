# Copilot Instructions

## General Guidelines
- To improve readability, prefer using a temporary variable rather than creating new instances inline, e.g.  
  `var message = new DocumentSaveRequestedMessage(resource);`  
  `_messengerService.Send(message);`
- Always use temporary variables to break down complex/convoluted logic into simpler steps for easier maintenance. Avoid chaining multiple operations (method calls, null coalescing, string replacements) in a single line.
- Always define collection initialization using multiple lines, never on a single line. 
- Only use ternary expressions for trivial logic, never complicated logic.
- Prefer explicit record classes with meaningful property names over anonymous types for message contracts. Records can be defined in the same file or class, whichever is more maintainable.
- Code-behind files should always follow the standard .xaml.cs naming convention (e.g., MyView.xaml.cs, not MyView.cs).
- Never use `/// <param>` XML documentation in doc strings because they are verbose and hard to keep synchronized.
- Do not use special characters like arrows or emojis in code comments. Use only standard ASCII characters.
- Always use localized strings for all user-facing text in this codebase. Never hardcode strings directly in XAML or C# UI code — add entries to Resources.resw and access them via IStringLocalizer.GetString() in code-behind, then bind with `{x:Bind}`.
- Unit tests should be practical and cover the happy case and the most common failure modes. Do not aim for complete code coverage for its own sake — keep the test set tight and focused.

## Code Style
- Use specific formatting rules
- Follow naming conventions

## Project-Specific Rules
- In Celbridge, the following are transient services with Workspace scope that should NOT be injected via constructor DI. Instead, access them through `_workspaceWrapper.WorkspaceService` to get the correct instance:
  - IWorkspaceSettingsService
  - IWorkspaceSettings
  - IResourceRegistry
  - IResourceTransferService
  - IResourceOperationService
  - IPythonService
  - IConsoleService
  - IDocumentsService
  - IExplorerService    
  - IInspectorService
  - IDataTransferService
  - IEntityService
  - IGenerativeAIService
  - IActivityService
  - IWorkspaceFeatures

- Project Configuration:
  - To access the current project, use `IProjectService.CurrentProject` (singleton)
  - Project config is accessed via `project.Config` (simple property, not a service)
  - To parse .celbridge files outside of project loading, use the static `ProjectConfigParser.ParseFromFile()` method
  - Example: `var config = _projectService.CurrentProject?.Config;`

- Feature Flags:
  - For application-level feature checks, use `IFeatureFlagService` (singleton, reads from appsettings.json)
  - For workspace-aware feature checks, use `IWorkspaceFeatures` (workspace-scoped, checks .celbridge file first, then falls back to appsettings.json)
  - Feature flag names use kebab-case (e.g., "notes-editor", "console")
  - In appsettings.json, feature flags are under the "FeatureFlags" section using kebab-case
  - In .celbridge files, users can override app-level features using the top-level [features] section
  - For optional features controlled by feature flags, use nullable types (e.g., `IConsolePanel?`) instead of the Null Object pattern. This is more scalable, maintainable, and honest. Make the service/panel nullable, return null when the feature is disabled, and add null checks at call sites. Never use Null Object implementations for feature flags.

- In Celbridge architecture, the Foundation project (Core\Celbridge.Foundation) should only contain abstractions (interfaces, abstract classes). Never place concrete implementations in Foundation - they belong in workspace or module projects.

