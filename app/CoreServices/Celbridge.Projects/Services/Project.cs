using System.IO.Compression;
using Celbridge.Logging;
using Celbridge.Utilities;

using Path = System.IO.Path;

namespace Celbridge.Projects.Services;

public class Project : IDisposable, IProject
{
    private const string DefaultProjectVersion = "0.1.0";
    private const string DefaultPythonVersion = "3.12";
    private const string ExamplesZipAssetPath = "ms-appx:///Assets/Examples.zip";
    private const string ReadMeMDAssetPath = "ms-appx:///Assets/readme.md";

    private readonly ILogger<Project> _logger;

    private IProjectConfigService? _projectConfig;
    public IProjectConfigService ProjectConfig => _projectConfig!;

    private string? _projectFilePath;
    public string ProjectFilePath => _projectFilePath!;

    private string? _projectName;
    public string ProjectName => _projectName!;

    private string? _projectFolderPath;
    public string ProjectFolderPath => _projectFolderPath!;

    private string? _projectDataFolderPath;
    public string ProjectDataFolderPath => _projectDataFolderPath!;

    public Project(
        ILogger<Project> logger)
    {
        _logger = logger;
    }

    public static async Task<Result<IProject>> LoadProjectAsync(string projectFilePath)
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

            //
            // Migrate project to latest version of Celbridge
            //

            var migrationService = ServiceLocator.AcquireService<IProjectMigrationService>();
            var checkResult = migrationService.CheckNeedsMigration(projectFilePath);

            if (checkResult.IsSuccess)
            {
                if (checkResult.Value)
                {
                    var migrateResult = await migrationService.MigrateProjectAsync(projectFilePath);
                    if (migrateResult.IsFailure)
                    {
                        // Log a warning but continue loading - the project will need to migrated again
                        project._logger.LogWarning(migrateResult, $"Failed to migrate project to latest version of Celbridge.");
                    }
                }
            }
            else
            {
                // Log a warning but continue loading - the project config will be empty
                project._logger.LogWarning(checkResult, $"Failed to check if project needs migration: {projectFilePath}");
            }
            
            //
            // Load project properties from the project file
            //

            var projectConfig = ServiceLocator.AcquireService<IProjectConfigService>() as ProjectConfigService;
            Guard.IsNotNull(projectConfig);

            var initResult = projectConfig.InitializeFromFile(projectFilePath);
            if (initResult.IsFailure)
            {
                // Log a warning but continue loading - the project config will be empty
                project._logger.LogWarning(initResult, $"Failed to initialize project configuration");
            }

            project._projectConfig = projectConfig;

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

    public static async Task<Result> CreateProjectAsync(string projectFilePath, NewProjectConfigType configType)
    {
        Guard.IsNotNullOrWhiteSpace(projectFilePath);

        try
        {
            if (string.IsNullOrEmpty(projectFilePath))
            {
                return Result.Fail("Project file path is empty");
            }

            if (File.Exists(projectFilePath))
            {
                return Result.Fail($"Project file already exists exist: {projectFilePath}");
            }

            var projectPath = Path.GetDirectoryName(projectFilePath);
            Guard.IsNotNull(projectPath);

            var projectDataFolderPath = Path.Combine(projectPath, ProjectConstants.MetaDataFolder);

            if (!Directory.Exists(projectDataFolderPath))
            {
                Directory.CreateDirectory(projectDataFolderPath);
            }

            if (configType == NewProjectConfigType.Standard)
            {
                // Get Celbridge version
                var utilityService = ServiceLocator.AcquireService<IUtilityService>();
                var info = utilityService.GetEnvironmentInfo();

                var projectTOML = $"""
                [project]
                name = "{Path.GetFileNameWithoutExtension(projectFilePath)}"
                version = "{DefaultProjectVersion}"
                requires-python = "{DefaultPythonVersion}"
                dependencies = []

                [celbridge]
                version = "{info.AppVersion}"
                """;

                // Todo: Populate this with project configuration options
                await File.WriteAllTextAsync(projectFilePath, projectTOML);


                // Read from a given file in the project build, and also ensure we're not stomping an existing file.
                string readMePath = projectPath + Path.DirectorySeparatorChar + "readme.md";
                if (!File.Exists(readMePath))
                {
                    var sourceWelcomeFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri(ReadMeMDAssetPath));
                    var welcomeFileStream = (await sourceWelcomeFile.OpenReadAsync()).AsStreamForRead();
                
                    using (StreamReader reader = new StreamReader(welcomeFileStream, encoding: System.Text.Encoding.UTF8))
                    {
                        string fileContents = await reader.ReadToEndAsync();
                        await File.WriteAllTextAsync(readMePath, fileContents);
                    }
                    welcomeFileStream.Close();
                }
            }
            else
            {
                // Extract our Examples.zip file to the selected location.
                var sourceZipFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri(ExamplesZipAssetPath));
                var tempZipFile = await sourceZipFile.CopyAsync(ApplicationData.Current.TemporaryFolder, "Examples.zip", NameCollisionOption.ReplaceExisting);
                ZipFile.ExtractToDirectory(tempZipFile.Path, projectPath, overwriteFiles: true);

                // Rename the celbridge project file to the selected project file name.
                File.Move(projectPath + Path.DirectorySeparatorChar + "examples.celbridge", projectFilePath);
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when creating the project: {projectFilePath}")
                .WithException(ex);
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
