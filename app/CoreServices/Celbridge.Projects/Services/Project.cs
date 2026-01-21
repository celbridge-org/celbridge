using Celbridge.Logging;
using Celbridge.Utilities;
using System.IO.Compression;

using Path = System.IO.Path;

namespace Celbridge.Projects.Services;

public class Project : IDisposable, IProject
{
    private readonly ILogger<Project> _logger;

    private IProjectConfigService? _projectConfig;
    public IProjectConfigService ProjectConfig => _projectConfig!;

    private MigrationResult _migrationResult = MigrationResult.Success();
    public MigrationResult MigrationResult => _migrationResult;

    private string? _projectFilePath;
    public string ProjectFilePath => _projectFilePath!;

    private string? _projectName;
    public string ProjectName => _projectName!;

    private string? _projectFolderPath;
    public string ProjectFolderPath => _projectFolderPath!;

    private string? _projectDataFolderPath;
    public string ProjectDataFolderPath => _projectDataFolderPath!;

    public Project(ILogger<Project> logger)
    {
        _logger = logger;
    }

    public static async Task<Result<IProject>> LoadProjectAsync(string projectFilePath, MigrationResult migrationResult)
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
            //
            // Create the project object
            //

            var project = ServiceLocator.AcquireService<IProject>() as Project;
            Guard.IsNotNull(project);
            project.PopulatePaths(projectFilePath);
            project._migrationResult = migrationResult;

            bool migrationSucceeded = migrationResult.OperationResult.IsSuccess;

            if (!migrationSucceeded)
            {
                // Log the error but continue loading the workspace
                project._logger.LogError(migrationResult.OperationResult, $"Failed to migrate project to latest version of Celbridge.");
            }
            
            //
            // Load project properties from the project file
            //

            var projectConfig = ServiceLocator.AcquireService<IProjectConfigService>() as ProjectConfigService;
            Guard.IsNotNull(projectConfig);
            project._projectConfig = projectConfig;

            if (migrationSucceeded)
            {
                var initResult = projectConfig.InitializeFromFile(projectFilePath);
                if (initResult.IsFailure)
                {
                    // Log an error but continue loading - the project config will be empty
                    project._logger.LogError(initResult, $"Failed to initialize project configuration");
                }
            }

            //
            // Ensure project data folder exists
            //

            if (!Directory.Exists(project.ProjectDataFolderPath))
            {
                Directory.CreateDirectory(project.ProjectDataFolderPath);
            }

            return Result<IProject>.Ok(project);
        }
        catch (Exception ex)
        {
            return Result<IProject>.Fail($"An exception occured when loading the project: {projectFilePath}")
                .WithException(ex);
        }
    }

    public static async Task<Result> CreateProjectAsync(string projectFilePath, ProjectTemplate template)
    {
        Guard.IsNotNullOrWhiteSpace(projectFilePath);

        // Use a temporary staging folder to prevent leftover files on failure
        var utilityService = ServiceLocator.AcquireService<IUtilityService>();
        var tempFile = utilityService.GetTemporaryFilePath("NewProject", string.Empty);
        var tempStagingPath = Path.GetDirectoryName(tempFile);

        try
        {
            if (string.IsNullOrEmpty(projectFilePath))
            {
                return Result.Fail("Project file path is empty");
            }

            if (File.Exists(projectFilePath))
            {
                return Result.Fail($"Project file already exists: {projectFilePath}");
            }

            var projectPath = Path.GetDirectoryName(projectFilePath);
            Guard.IsNotNull(projectPath);

            // Create the staging folder
            Directory.CreateDirectory(tempStagingPath);

            var stagingDataFolderPath = Path.Combine(tempStagingPath, ProjectConstants.MetaDataFolder);
            Directory.CreateDirectory(stagingDataFolderPath);

            // Get Celbridge application version
            var appVersion = utilityService.GetEnvironmentInfo().AppVersion;

            // Extract template zip to staging location
            var sourceZipFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri(template.TemplateAssetPath));

            var tempZipFile = await sourceZipFile.CopyAsync(
                ApplicationData.Current.TemporaryFolder,
                "template.zip",
                NameCollisionOption.ReplaceExisting);

            ZipFile.ExtractToDirectory(tempZipFile.Path, tempStagingPath, overwriteFiles: true);

            // Update the extracted project file with actual version values
            var extractedProjectFile = Path.Combine(tempStagingPath, template.TemplateProjectFileName);
            var projectFileContents = await File.ReadAllTextAsync(extractedProjectFile);

            projectFileContents = projectFileContents
                .Replace("<application-version>", appVersion)
                .Replace("<python-version>", ProjectConstants.DefaultPythonVersion);
            await File.WriteAllTextAsync(extractedProjectFile, projectFileContents);

            // Rename the project settings file to the user-specified name in staging
            var projectFileName = Path.GetFileName(projectFilePath);
            var stagedProjectFilePath = Path.Combine(tempStagingPath, projectFileName);
            File.Move(extractedProjectFile, stagedProjectFilePath);

            // All staging operations succeeded - now move to final location
            // Ensure the destination folder exists
            if (!Directory.Exists(projectPath))
            {
                Directory.CreateDirectory(projectPath);
            }

            // Move all files and folders from staging to the final project location
            foreach (var file in Directory.GetFiles(tempStagingPath))
            {
                var destFile = Path.Combine(projectPath, Path.GetFileName(file));
                File.Move(file, destFile);
            }

            foreach (var dir in Directory.GetDirectories(tempStagingPath))
            {
                var destDir = Path.Combine(projectPath, Path.GetFileName(dir));
                Directory.Move(dir, destDir);
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
            try
            {
                if (Directory.Exists(tempStagingPath))
                {
                    Directory.Delete(tempStagingPath, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        return Result.Ok();
    }

    private void PopulatePaths(string projectFilePath)
    {
        _projectFilePath = projectFilePath;

        _projectName = Path.GetFileNameWithoutExtension(projectFilePath);
        Guard.IsNotNullOrWhiteSpace(ProjectName);

        _projectFolderPath = Path.GetDirectoryName(projectFilePath)!;
        Guard.IsNotNullOrWhiteSpace(ProjectFolderPath);

        _projectDataFolderPath = Path.Combine(ProjectFolderPath, ProjectConstants.MetaDataFolder);
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
