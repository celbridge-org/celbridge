# Copilot Instructions

## General Guidelines

* First general instruction
* Second general instruction

## Code Style

* Use specific formatting rules
* Follow naming conventions

## Project-Specific Rules

* In Celbridge, the following are transient services with Workspace scope that should NOT be injected via constructor DI. Instead, access them through \_workspaceWrapper.WorkspaceService to get the correct instance:
* IWorkspaceSettingsService
* IWorkspaceSettings
* IResourceRegistry
* IResourceTransferService
* IResourceOperationService
* IPythonService
* IConsoleService
* IDocumentsService
* IExplorerService    
* IInspectorService
* IDataTransferService
* IEntityService
* IGenerativeAIService
* IActivityService

