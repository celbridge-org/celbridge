using System.IO.Compression;
using Celbridge.ApplicationEnvironment;
using Celbridge.Python;
using Microsoft.Extensions.Localization;

namespace Celbridge.Projects.Services;

public class ProjectTemplateService : IProjectTemplateService
{
    private const string TemplateProjectFileName = "project.celbridge";
    private const string ProjectsModuleFolder = "Celbridge.Projects";

    private readonly List<ProjectTemplate> _templates;
    private readonly IPythonConfigService _pythonConfigService;
    private readonly ILocalFileSystem _fileSystem;
    private readonly IAppEnvironment _appEnvironment;

    public ProjectTemplateService(
        IStringLocalizer stringLocalizer,
        IPythonConfigService pythonConfigService,
        ILocalFileSystem fileSystem,
        IAppEnvironment appEnvironment)
    {
        _pythonConfigService = pythonConfigService;
        _fileSystem = fileSystem;
        _appEnvironment = appEnvironment;

        _templates =
        [
            CreateTemplate(stringLocalizer, "Empty", "file-earmark"),
            CreateTemplate(stringLocalizer, "Examples", "collection")
        ];
    }

    private static ProjectTemplate CreateTemplate(IStringLocalizer stringLocalizer, string id, string icon)
    {
        return new ProjectTemplate
        {
            Id = id,
            Name = stringLocalizer.GetString($"Template_{id}_Name"),
            Description = stringLocalizer.GetString($"Template_{id}_Description"),
            Icon = icon
        };
    }

    public IReadOnlyList<ProjectTemplate> GetTemplates() =>
        _templates;

    public ProjectTemplate GetDefaultTemplate() =>
        _templates.First(t => t.Id == "Empty");

    public async Task<Result> CreateFromTemplateAsync(string projectFilePath, ProjectTemplate template)
    {
        Guard.IsNotNullOrWhiteSpace(projectFilePath);

        // Use a temporary staging folder to prevent leftover files on failure.
        // The project doesn't exist yet, so temp: isn't available; fall back to
        // the application's temp folder.
        var tempRootPath = _appEnvironment.TemporaryFolderPath;
        var tempStagingPath = Path.Combine(
            tempRootPath,
            "NewProject",
            Path.GetFileNameWithoutExtension(Path.GetRandomFileName()));

        try
        {
            if (string.IsNullOrEmpty(projectFilePath))
            {
                return Result.Fail("Project file path is empty");
            }

            var existingProjectInfo = await _fileSystem.GetInfoAsync(projectFilePath);
            if (existingProjectInfo.IsSuccess && existingProjectInfo.Value.Kind == StorageItemKind.File)
            {
                return Result.Fail($"Project file already exists: {projectFilePath}");
            }

            var projectPath = Path.GetDirectoryName(projectFilePath);
            Guard.IsNotNull(projectPath);

            // Create the staging folder
            var createStagingResult = await _fileSystem.CreateFolderAsync(tempStagingPath!);
            if (createStagingResult.IsFailure)
            {
                return Result.Fail($"Failed to create staging folder: {tempStagingPath}")
                    .WithErrors(createStagingResult);
            }

            // Get Celbridge application version
            var appVersion = _appEnvironment.GetEnvironmentInfo().AppVersion;

            // Extract the bundled template zip to the staging location. The zip is read as a real file
            // from the install location: the package root on the packaged Windows head, the
            // Celbridge.Projects content folder beside the app on the Skia heads.
            var sourceZipPath = _appEnvironment.GetBundledAssetPath(
                ProjectsModuleFolder, $"Assets/Templates/{template.Id}.zip");

            ZipFile.ExtractToDirectory(sourceZipPath, tempStagingPath!, overwriteFiles: true);

            // Update the extracted project file with actual version values
            var extractedProjectFile = Path.Combine(tempStagingPath!, TemplateProjectFileName);
            var readResult = await _fileSystem.ReadAllTextAsync(extractedProjectFile);
            if (readResult.IsFailure)
            {
                return Result.Fail($"Failed to read extracted template project file: {extractedProjectFile}")
                    .WithErrors(readResult);
            }

            var projectFileContents = readResult.Value
                .Replace("<application-version>", appVersion)
                .Replace("<python-version>", _pythonConfigService.DefaultPythonVersion);

            var writeResult = await _fileSystem.WriteAllTextAsync(extractedProjectFile, projectFileContents);
            if (writeResult.IsFailure)
            {
                return Result.Fail($"Failed to write extracted template project file: {extractedProjectFile}")
                    .WithErrors(writeResult);
            }

            // Rename the project settings file to the user-specified name in staging
            var projectFileName = Path.GetFileName(projectFilePath);
            var stagedProjectFilePath = Path.Combine(tempStagingPath!, projectFileName);
            var renameResult = await _fileSystem.MoveFileAsync(extractedProjectFile, stagedProjectFilePath);
            if (renameResult.IsFailure)
            {
                return Result.Fail($"Failed to rename staged project file: {extractedProjectFile}")
                    .WithErrors(renameResult);
            }

            // All staging operations succeeded - now move to final location
            // Ensure the destination folder exists
            var destFolderInfo = await _fileSystem.GetInfoAsync(projectPath);
            if (!destFolderInfo.IsSuccess || destFolderInfo.Value.Kind != StorageItemKind.Folder)
            {
                var createDestResult = await _fileSystem.CreateFolderAsync(projectPath);
                if (createDestResult.IsFailure)
                {
                    return Result.Fail($"Failed to create project folder: {projectPath}")
                        .WithErrors(createDestResult);
                }
            }

            // Move all files and folders from staging to the final project location
            var stagedEntriesResult = await _fileSystem.EnumerateAsync(tempStagingPath!, "*", recursive: false);
            if (stagedEntriesResult.IsFailure)
            {
                return Result.Fail($"Failed to enumerate staged items: {tempStagingPath}")
                    .WithErrors(stagedEntriesResult);
            }

            foreach (var entry in stagedEntriesResult.Value)
            {
                if (entry.IsFolder)
                {
                    continue;
                }
                var destFile = Path.Combine(projectPath, Path.GetFileName(entry.FullPath));
                var moveFileResult = await _fileSystem.MoveFileAsync(entry.FullPath, destFile);
                if (moveFileResult.IsFailure)
                {
                    return Result.Fail($"Failed to move staged file to final location: {entry.FullPath}")
                        .WithErrors(moveFileResult);
                }
            }

            foreach (var entry in stagedEntriesResult.Value)
            {
                if (!entry.IsFolder)
                {
                    continue;
                }
                var destFolder = Path.Combine(projectPath, Path.GetFileName(entry.FullPath));
                var moveFolderResult = await _fileSystem.MoveFolderAsync(entry.FullPath, destFolder);
                if (moveFolderResult.IsFailure)
                {
                    return Result.Fail($"Failed to move staged folder to final location: {entry.FullPath}")
                        .WithErrors(moveFolderResult);
                }
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when creating the project: {projectFilePath}")
                .WithException(ex);
        }
        finally
        {
            // Clean up the staging folder regardless of success or failure
            var stagingInfo = await _fileSystem.GetInfoAsync(tempStagingPath);
            if (stagingInfo.IsSuccess && stagingInfo.Value.Kind == StorageItemKind.Folder)
            {
                // Best-effort cleanup; failures here should not mask the primary outcome.
                await _fileSystem.DeleteFolderAsync(tempStagingPath, recursive: true);
            }
        }

        return Result.Ok();
    }
}
