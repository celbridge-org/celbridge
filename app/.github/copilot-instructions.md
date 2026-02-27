# Copilot Instructions

## General Guidelines
- To improve readability, prefer using a temporary variable rather than creating new instances inline, e.g.  
  `var message = new DocumentSaveRequestedMessage(resource);`  
  `_messengerService.Send(message);`
- Always use temporary variables to break down complex/convoluted logic into simpler steps for easier maintenance. Avoid chaining multiple operations (method calls, null coalescing, string replacements) in a single line.
- Always define collection initialization using multiple lines, never on a single line. 
- Only use ternary expressions for trivial logic, never complicated logic.
- Prefer explicit record classes with meaningful property names over anonymous types for message contracts. Records can be defined in the same file or class, whichever is more maintainable.

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

