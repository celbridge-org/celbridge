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

