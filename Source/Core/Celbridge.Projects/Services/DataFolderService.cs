using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Celbridge.Logging;
using Celbridge.Platform;

namespace Celbridge.Projects.Services;

internal sealed class DataFolderService : IDataFolderService
{
    private const string ProjectsFolderName = "Projects";
    private const string ProjectInfoFileName = "project.json";

    // Enough of the project's name to hint at the project. The cut keeps paths short: the tools a user
    // installs nest deep below the folder, and Python on Windows cannot open a path longer than 260
    // characters unless long paths are enabled.
    private const int NameLength = 16;

    private const int HashLength = 16;

    private static readonly JsonSerializerOptions ProjectInfoJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    // What project.json records, so the integration test driver and a person can tell folders apart.
    private sealed record ProjectInfo(string ProjectFilePath);

    private readonly ILogger<DataFolderService> _logger;
    private readonly IAppEnvironment _appEnvironment;
    private readonly ILocalFileSystem _fileSystem;

    public DataFolderService(
        ILogger<DataFolderService> logger,
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

        var projectInfo = new ProjectInfo(Path.GetFullPath(projectFilePath));
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

    // The name makes a folder easy to identify, and the hash of the project file's path keeps two projects
    // with one name apart. Links are not resolved, so a project reached through a link, a junction or another
    // drive letter gets a folder of its own.
    private static string GetFolderName(string projectFilePath)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectFilePath));

        // Two spellings of one path that differ only in case name one file where the file system ignores
        // case, so they share a folder there.
        var key = PathComparison.IsCaseInsensitiveFileSystem
            ? fullPath.ToLowerInvariant()
            : fullPath;

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var hash = Convert.ToHexStringLower(hashBytes)[..HashLength];

        var name = Path.GetFileNameWithoutExtension(fullPath);
        if (name.Length > NameLength)
        {
            // A cut between the halves of a surrogate pair would leave a name that not every file system takes.
            var cutLength = char.IsHighSurrogate(name[NameLength - 1])
                ? NameLength - 1
                : NameLength;

            name = name[..cutLength];
        }

        return $"{name}-{hash}";
    }
}
