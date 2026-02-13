using System.IO.Compression;
using Celbridge.ApplicationEnvironment;
using Celbridge.Python;
using Celbridge.Utilities;
using Microsoft.Extensions.Localization;

namespace Celbridge.Projects.Services;

public class ProjectTemplateService : IProjectTemplateService
{
    private const string TemplateProjectFileName = "project.celbridge";

    private readonly List<ProjectTemplate> _templates;
    private readonly IEnvironmentService _environmentService;
    private readonly IPythonConfigService _pythonConfigService;

    public ProjectTemplateService(
        IStringLocalizer stringLocalizer,
        IEnvironmentService environmentService,
        IPythonConfigService pythonConfigService)
    {
        _environmentService = environmentService;
        _pythonConfigService = pythonConfigService;

        _templates =
        [
            CreateTemplate(stringLocalizer, "Empty", "\uE8A5"),
            CreateTemplate(stringLocalizer, "Examples", "\uE736")
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

        // Use a temporary staging folder to prevent leftover files on failure
        var tempFile = PathHelper.GetTemporaryFilePath("NewProject", string.Empty);
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
            Directory.CreateDirectory(tempStagingPath!);

            var stagingDataFolderPath = Path.Combine(tempStagingPath!, ProjectConstants.MetaDataFolder);
            Directory.CreateDirectory(stagingDataFolderPath);

            // Get Celbridge application version
            var appVersion = _environmentService.GetEnvironmentInfo().AppVersion;

            // Extract template zip to staging location
            var templateAsset = new Uri($"ms-appx:///Assets/Templates/{template.Id}.zip");
            var sourceZipFile = await StorageFile.GetFileFromApplicationUriAsync(templateAsset);

            var tempZipFile = await sourceZipFile.CopyAsync(
                ApplicationData.Current.TemporaryFolder,
                "template.zip",
                NameCollisionOption.ReplaceExisting);

            ZipFile.ExtractToDirectory(tempZipFile.Path, tempStagingPath!, overwriteFiles: true);

            // Update the extracted project file with actual version values
            var extractedProjectFile = Path.Combine(tempStagingPath!, TemplateProjectFileName);
            var projectFileContents = await File.ReadAllTextAsync(extractedProjectFile);

            projectFileContents = projectFileContents
                .Replace("<application-version>", appVersion)
                .Replace("<python-version>", _pythonConfigService.DefaultPythonVersion);
            await File.WriteAllTextAsync(extractedProjectFile, projectFileContents);

            // Rename the project settings file to the user-specified name in staging
            var projectFileName = Path.GetFileName(projectFilePath);
            var stagedProjectFilePath = Path.Combine(tempStagingPath!, projectFileName);
            File.Move(extractedProjectFile, stagedProjectFilePath);

            // All staging operations succeeded - now move to final location
            // Ensure the destination folder exists
            if (!Directory.Exists(projectPath))
            {
                Directory.CreateDirectory(projectPath);
            }

            // Move all files and folders from staging to the final project location
            foreach (var file in Directory.GetFiles(tempStagingPath!))
            {
                var destFile = Path.Combine(projectPath, Path.GetFileName(file));
                File.Move(file, destFile);
            }

            foreach (var dir in Directory.GetDirectories(tempStagingPath!))
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
}
