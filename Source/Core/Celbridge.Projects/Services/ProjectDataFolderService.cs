using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Celbridge.Logging;
using Celbridge.Platform;

namespace Celbridge.Projects.Services;

internal sealed class ProjectDataFolderService : IProjectDataFolderService
{
    private const string ProjectsFolderName = "Projects";
    private const string ProjectInfoFileName = "project.json";

    // The folder name uses only the start of the project folder's name, to keep paths short. Tools the user
    // installs nest deep inside the folder, and Python on Windows can't open a path longer than 260 characters
    // unless long paths are enabled.
    private const int NameLength = 16;

    private const int HashLength = 16;

    private static readonly JsonSerializerOptions ProjectInfoJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    // project.json records the path of the project folder that the data folder belongs to.
    private sealed record ProjectInfo(string ProjectFolderPath);

    private readonly ILogger<ProjectDataFolderService> _logger;
    private readonly IAppEnvironment _appEnvironment;
    private readonly ILocalFileSystem _fileSystem;

    public ProjectDataFolderService(
        ILogger<ProjectDataFolderService> logger,
        IAppEnvironment appEnvironment,
        ILocalFileSystem fileSystem)
    {
        _logger = logger;
        _appEnvironment = appEnvironment;
        _fileSystem = fileSystem;
    }

    public string GetDataFolderPath(string projectFilePath)
    {
        var projectsFolderPath = Path.Combine(_appEnvironment.LocalApplicationDataFolderPath, ProjectsFolderName);

        return Path.Combine(projectsFolderPath, GetFolderName(projectFilePath));
    }

    public async Task<Result<string>> EnsureDataFolderAsync(string projectFilePath)
    {
        // Only create a data folder for a project file that exists.
        var projectFileResult = await _fileSystem.GetInfoAsync(projectFilePath);
        var projectFileExists = projectFileResult.IsSuccess
            && projectFileResult.Value.Kind == StorageItemKind.File;
        if (!projectFileExists)
        {
            return Result<string>.Fail($"Failed to create a data folder because the project file does not exist: '{projectFilePath}'");
        }

        var dataFolderPath = GetDataFolderPath(projectFilePath);

        // project.json is written last, so a folder that has it is complete.
        var projectInfoPath = Path.Combine(dataFolderPath, ProjectInfoFileName);
        var infoResult = await _fileSystem.GetInfoAsync(projectInfoPath);
        var hasProjectInfo = infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.File;
        if (hasProjectInfo)
        {
            return dataFolderPath;
        }

        var createResult = await _fileSystem.CreateFolderAsync(dataFolderPath);
        if (createResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to create the project data folder: '{dataFolderPath}'")
                .WithErrors(createResult);
        }

        var projectInfo = new ProjectInfo(ProjectFileHelper.GetProjectFolderPath(projectFilePath));
        var projectInfoJson = JsonSerializer.Serialize(projectInfo, ProjectInfoJsonOptions);

        var writeResult = await _fileSystem.WriteAllTextAsync(projectInfoPath, projectInfoJson);
        if (writeResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to write the project data folder's {ProjectInfoFileName}: '{projectInfoPath}'")
                .WithErrors(writeResult);
        }

        _logger.LogDebug($"Created the project data folder: '{dataFolderPath}'");

        return dataFolderPath;
    }

    public async Task<Result> ClearDataFolderAsync(string projectFilePath)
    {
        var dataFolderPath = GetDataFolderPath(projectFilePath);

        var infoResult = await _fileSystem.GetInfoAsync(dataFolderPath);
        var folderExists = infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.Folder;
        if (!folderExists)
        {
            return Result.Ok();
        }

        var deleteResult = await _fileSystem.DeleteFolderAsync(dataFolderPath, recursive: true);
        if (deleteResult.IsFailure)
        {
            return Result.Fail($"Failed to clear the project data folder: '{dataFolderPath}'")
                .WithErrors(deleteResult);
        }

        _logger.LogDebug($"Cleared the project data folder: '{dataFolderPath}'");

        return Result.Ok();
    }

    // A folder contains at most one project file, so the data folder belongs to the project folder, and renaming
    // the project file keeps its data. The name makes the folder easy to identify. The hash of the project
    // folder's path keeps apart two projects in folders with the same name. Links aren't resolved, so a project
    // opened through a link, a junction or another drive letter gets its own folder.
    private static string GetFolderName(string projectFilePath)
    {
        var projectFolderPath = ProjectFileHelper.GetProjectFolderPath(projectFilePath);

        // On a case-insensitive file system, paths that differ only in case name the same folder, so they must
        // share a data folder.
        var key = projectFolderPath;
        if (PathComparison.IsCaseInsensitiveFileSystem)
        {
            key = projectFolderPath.ToLowerInvariant();
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var hash = Convert.ToHexStringLower(hashBytes)[..HashLength];

        // A project at the root of a drive has no folder name, so its data folder is named by the hash alone.
        var name = Path.GetFileName(projectFolderPath);
        if (string.IsNullOrEmpty(name))
        {
            return hash;
        }

        if (name.Length > NameLength)
        {
            // Don't cut between the halves of a surrogate pair. Some file systems reject the resulting name.
            var cutLength = char.IsHighSurrogate(name[NameLength - 1])
                ? NameLength - 1
                : NameLength;

            name = name[..cutLength];
        }

        return $"{name}-{hash}";
    }
}
