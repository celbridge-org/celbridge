# Project Loading Architecture Refactoring

## Overview

This document describes a refactoring plan for the project loading system in Celbridge. The current architecture has several concerns mixed together, making it difficult to understand, test, and maintain.

## Current Problems

### 1. `Project` Class Has Too Many Responsibilities

The `Project` class currently:
- Contains static factory methods (`LoadProjectAsync`, `CreateProjectAsync`) that use `ServiceLocator`
- Manages path computation (`PopulatePaths`)
- Loads configuration
- Creates data folders
- Acts as both a data container and has loading logic

### 2. Multiple "Loading" Concepts Are Conflated

Three distinct concepts are mixed together:
- **Config inspection**: Reading `.celbridge` file to check version/validity *before* committing to full load
- **Project loading**: Creating the `IProject` instance with config
- **Workspace loading**: Setting up the full workspace environment (resources, Python, etc.)

### 3. Confusing Dependency Flow

The current flow is:
```
LoadProjectCommand → ProjectLoader → ProjectService → Project.LoadProjectAsync()
```

`ProjectLoader` also triggers `WorkspaceLoader` indirectly via navigation.

### 4. `ServiceLocator` in Static Methods

Using `ServiceLocator` in static methods makes testing difficult and hides dependencies.

---

## Proposed Architecture

### New Class Responsibilities

| Class | Responsibility |
|-------|----------------|
| `ProjectConfigReader` | Read `.celbridge` file metadata without loading |
| `ProjectMigrationService` | Check version compatibility, perform upgrades |
| `ProjectFactory` | Create `Project` instances |
| `ProjectTemplateService` | Create new projects from templates |
| `Project` | Pure data container (no logic) |
| `ProjectService` | Manage current project, recent projects list |
| `ProjectLoader` | Orchestrate full load workflow with dialogs |
| `WorkspaceLoader` | Initialize workspace services after project loads |

### Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Pre-Load Inspection                                │
│  ┌─────────────────────┐         ┌──────────────────┐                       │
│  │ ProjectConfigReader │ ──────► │ ProjectMetadata  │                       │
│  └─────────────────────┘         └──────────────────┘                       │
└─────────────────────────────────────────────────────────────────────────────┘
                                         │
                                         ▼ (used by)
┌─────────────────────────────────────────────────────────────────────────────┐
│                              Migration                                       │
│  ┌──────────────────────────┐         ┌──────────────────┐                  │
│  │ ProjectMigrationService  │ ──────► │ MigrationResult  │                  │
│  └──────────────────────────┘         └──────────────────┘                  │
└─────────────────────────────────────────────────────────────────────────────┘
                                         │
                                         ▼ (passed to)
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Project Loading                                    │
│  ┌───────────────┐    ┌────────────────┐    ┌─────────────────────┐         │
│  │ ProjectLoader │───►│ ProjectFactory │───►│ Project (data only) │         │
│  └───────────────┘    └────────────────┘    └─────────────────────┘         │
│          │                                                                   │
│          ▼                                                                   │
│  ┌────────────────┐                                                          │
│  │ ProjectService │                                                          │
│  └────────────────┘                                                          │
└─────────────────────────────────────────────────────────────────────────────┘
                                         │
                                         ▼ (triggers)
┌─────────────────────────────────────────────────────────────────────────────┐
│                          Workspace Loading                                   │
│  ┌─────────────────┐    ┌──────────────────────────────────────────┐        │
│  │ WorkspaceLoader │───►│ Explorer, Documents, Python, Activities  │        │
│  └─────────────────┘    └──────────────────────────────────────────┘        │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│                             Creation                                         │
│  ┌────────────────────────┐         ┌──────────────────────┐                │
│  │ ProjectTemplateService │ ──────► │ New .celbridge file  │                │
│  └────────────────────────┘         └──────────────────────┘                │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## New Classes

### 1. `ProjectConfigReader`

Reads and validates project configuration files without loading the full project. Use this to inspect a project's version and validity before committing to a full load.

**Location**: `CoreServices/Celbridge.Projects/Services/ProjectConfigReader.cs`

```csharp
namespace Celbridge.Projects.Services;

/// <summary>
/// Reads and validates project configuration files without loading the full project.
/// Use this to inspect a project's version and validity before committing to a full load.
/// </summary>
public class ProjectConfigReader
{
    /// <summary>
    /// Reads project metadata from a .celbridge file for inspection.
    /// Does not create a Project instance or modify any state.
    /// </summary>
    public Result<ProjectMetadata> ReadProjectMetadata(string projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
        {
            return Result<ProjectMetadata>.Fail("Project file path is empty");
        }

        if (!File.Exists(projectFilePath))
        {
            return Result<ProjectMetadata>.Fail($"Project file does not exist: '{projectFilePath}'");
        }

        try
        {
            var projectName = Path.GetFileNameWithoutExtension(projectFilePath);
            var projectFolderPath = Path.GetDirectoryName(projectFilePath)!;

            // Parse the TOML config to extract metadata
            var configService = new ProjectConfigService();
            var initResult = configService.InitializeFromFile(projectFilePath);

            // Even if parsing fails, we can return partial metadata
            var celbridgeVersion = configService.Config.Celbridge.Version;

            var metadata = new ProjectMetadata(
                ProjectFilePath: projectFilePath,
                ProjectName: projectName,
                ProjectFolderPath: projectFolderPath,
                CelbridgeVersion: celbridgeVersion,
                IsConfigValid: initResult.IsSuccess);

            return Result<ProjectMetadata>.Ok(metadata);
        }
        catch (Exception ex)
        {
            return Result<ProjectMetadata>.Fail($"Failed to read project metadata: {projectFilePath}")
                .WithException(ex);
        }
    }
}

/// <summary>
/// Lightweight metadata about a project, read without loading the full project.
/// </summary>
public record ProjectMetadata(
    string ProjectFilePath,
    string ProjectName,
    string ProjectFolderPath,
    string? CelbridgeVersion,
    bool IsConfigValid);
```

### 2. Simplified `Project` Class

The `Project` class becomes a pure data container with no logic.

**Location**: `CoreServices/Celbridge.Projects/Services/Project.cs`

```csharp
using Celbridge.Logging;

namespace Celbridge.Projects.Services;

/// <summary>
/// Represents a loaded Celbridge project. This is a data container only.
/// Use ProjectFactory to create instances.
/// </summary>
public class Project : IDisposable, IProject
{
    public string ProjectFilePath { get; }
    public string ProjectName { get; }
    public string ProjectFolderPath { get; }
    public string ProjectDataFolderPath { get; }
    public IProjectConfigService ProjectConfig { get; }
    public MigrationResult MigrationResult { get; }

    internal Project(
        string projectFilePath,
        string projectName,
        string projectFolderPath,
        string projectDataFolderPath,
        IProjectConfigService projectConfig,
        MigrationResult migrationResult)
    {
        ProjectFilePath = projectFilePath;
        ProjectName = projectName;
        ProjectFolderPath = projectFolderPath;
        ProjectDataFolderPath = projectDataFolderPath;
        ProjectConfig = projectConfig;
        MigrationResult = migrationResult;
    }

    private bool _disposed = false;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed objects here
            }
            _disposed = true;
        }
    }

    ~Project()
    {
        Dispose(false);
    }
}
```

### 3. `ProjectFactory`

Factory for creating and loading Project instances with proper dependency injection.

**Location**: `CoreServices/Celbridge.Projects/Services/ProjectFactory.cs`

```csharp
using Celbridge.Logging;

namespace Celbridge.Projects.Services;

/// <summary>
/// Factory for creating and loading Project instances.
/// </summary>
public class ProjectFactory
{
    private readonly ILogger<ProjectFactory> _logger;
    private readonly IServiceProvider _serviceProvider;

    public ProjectFactory(
        ILogger<ProjectFactory> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Loads a project from the specified file path.
    /// </summary>
    public async Task<Result<IProject>> LoadAsync(string projectFilePath, MigrationResult migrationResult)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
        {
            return Result<IProject>.Fail("Project file path is empty");
        }

        if (!File.Exists(projectFilePath))
        {
            return Result<IProject>.Fail($"Project file does not exist: '{projectFilePath}'");
        }

        try
        {
            var projectName = Path.GetFileNameWithoutExtension(projectFilePath);
            var projectFolderPath = Path.GetDirectoryName(projectFilePath)!;
            var projectDataFolderPath = Path.Combine(projectFolderPath, ProjectConstants.MetaDataFolder);

            // Load config if migration succeeded
            var projectConfig = _serviceProvider.GetRequiredService<IProjectConfigService>();
            if (migrationResult.OperationResult.IsSuccess)
            {
                var initResult = (projectConfig as ProjectConfigService)!.InitializeFromFile(projectFilePath);
                if (initResult.IsFailure)
                {
                    _logger.LogError(initResult, "Failed to initialize project configuration");
                }
            }
            else
            {
                _logger.LogError(migrationResult.OperationResult, "Failed to migrate project to latest version");
            }

            // Ensure data folder exists
            if (!Directory.Exists(projectDataFolderPath))
            {
                Directory.CreateDirectory(projectDataFolderPath);
            }

            var project = new Project(
                projectFilePath,
                projectName,
                projectFolderPath,
                projectDataFolderPath,
                projectConfig,
                migrationResult);

            return Result<IProject>.Ok(project);
        }
        catch (Exception ex)
        {
            return Result<IProject>.Fail($"An exception occurred when loading the project: {projectFilePath}")
                .WithException(ex);
        }
    }
}
```

### 4. `ProjectTemplateService`

Handles project creation from templates.

**Location**: `CoreServices/Celbridge.Projects/Services/ProjectTemplateService.cs`

```csharp
using Celbridge.Utilities;
using System.IO.Compression;

namespace Celbridge.Projects.Services;

/// <summary>
/// Handles project creation from templates.
/// </summary>
public class ProjectTemplateService
{
    private readonly IUtilityService _utilityService;

    public ProjectTemplateService(IUtilityService utilityService)
    {
        _utilityService = utilityService;
    }

    public async Task<Result> CreateFromTemplateAsync(string projectFilePath, ProjectTemplate template)
    {
        if (string.IsNullOrEmpty(projectFilePath))
        {
            return Result.Fail("Project file path is empty");
        }

        if (File.Exists(projectFilePath))
        {
            return Result.Fail($"Project file already exists: {projectFilePath}");
        }

        try
        {
            var projectPath = Path.GetDirectoryName(projectFilePath)!;
            var projectDataFolderPath = Path.Combine(projectPath, ProjectConstants.MetaDataFolder);

            if (!Directory.Exists(projectDataFolderPath))
            {
                Directory.CreateDirectory(projectDataFolderPath);
            }

            var appVersion = _utilityService.GetEnvironmentInfo().AppVersion;

            // Extract template
            var sourceZipFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri(template.TemplateAssetPath));
            var tempZipFile = await sourceZipFile.CopyAsync(
                ApplicationData.Current.TemporaryFolder,
                "template.zip",
                NameCollisionOption.ReplaceExisting);

            ZipFile.ExtractToDirectory(tempZipFile.Path, projectPath, overwriteFiles: true);

            // Update version placeholders
            var extractedProjectFile = Path.Combine(projectPath, template.TemplateProjectFileName);
            var projectFileContents = await File.ReadAllTextAsync(extractedProjectFile);

            projectFileContents = projectFileContents
                .Replace("<application-version>", appVersion)
                .Replace("<python-version>", ProjectConstants.DefaultPythonVersion);

            await File.WriteAllTextAsync(extractedProjectFile, projectFileContents);
            File.Move(extractedProjectFile, projectFilePath);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when creating the project: {projectFilePath}")
                .WithException(ex);
        }
    }
}
```

---

## Required Changes

### Files to Create

1. `CoreServices/Celbridge.Projects/Services/ProjectConfigReader.cs`
2. `CoreServices/Celbridge.Projects/Services/ProjectFactory.cs`
3. `CoreServices/Celbridge.Projects/Services/ProjectTemplateService.cs`
4. `BaseLibrary/Projects/ProjectMetadata.cs` (if exposing via interface)

### Files to Modify

1. **`CoreServices/Celbridge.Projects/Services/Project.cs`**
   - Remove static `LoadProjectAsync` and `CreateProjectAsync` methods
   - Remove `PopulatePaths` method
   - Convert to immutable properties set via constructor
   - Remove `ServiceLocator` usage

2. **`CoreServices/Celbridge.Projects/Services/ProjectService.cs`**
   - Inject `ProjectFactory` instead of calling `Project.LoadProjectAsync`
   - Inject `ProjectTemplateService` for `CreateProjectAsync`

3. **`CoreServices/Celbridge.Projects/ServiceConfiguration.cs`**
   - Register new services: `ProjectConfigReader`, `ProjectFactory`, `ProjectTemplateService`

4. **`CoreServices/Celbridge.Projects/Services/ProjectMigrationService.cs`** (if exists)
   - Consider using `ProjectConfigReader` for initial version checks

### Service Registration

Add to `ServiceConfiguration.cs`:

```csharp
services.AddTransient<ProjectConfigReader>();
services.AddTransient<ProjectFactory>();
services.AddTransient<ProjectTemplateService>();
```

---

## Benefits

1. **Single Responsibility**: Each class has one clear purpose
2. **Testability**: No `ServiceLocator` in business logic; all dependencies are injected
3. **Clarity**: Clear separation between "inspecting a config" vs "loading a project" vs "initializing workspace"
4. **Immutability**: `Project` becomes a simple immutable data container
5. **Flexibility**: Can inspect project metadata without committing to full load

---

## Migration Strategy

1. Create new classes alongside existing code
2. Update `ProjectService` to use new classes
3. Update tests to use new architecture
4. Remove deprecated static methods from `Project`
5. Clean up any remaining `ServiceLocator` usage in project loading code

---

## Open Questions

1. Should `ProjectMetadata` be exposed via `BaseLibrary` for use by other components?
2. Should `ProjectConfigReader` be merged into `ProjectMigrationService` since migration needs to read config anyway?
3. Do we need an `IProjectFactory` interface, or is the concrete class sufficient?
