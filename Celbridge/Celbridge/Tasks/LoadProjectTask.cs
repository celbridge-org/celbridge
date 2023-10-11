using System.Threading.Tasks;
using System;
using Celbridge.Utils;
using System.IO;
using Newtonsoft.Json;
using Celbridge.Models;
using Celbridge.Services;
using Serilog;
using Newtonsoft.Json.Serialization;

namespace Celbridge.Tasks
{
    public class LoadProjectTask
    {
        private readonly ISettingsService _settingsService;
        private readonly IResourceService _resourceService;
        private readonly ICelScriptService _celScriptService;
        private readonly LoadCustomAssembliesTask _loadCustomAssembliesTask;

        public LoadProjectTask(ISettingsService settingsService,
                               IResourceService resourceService,
                               ICelScriptService celScriptService,
                               LoadCustomAssembliesTask loadCustomAssembliesTask)
        {
            _settingsService = settingsService;
            _resourceService = resourceService;
            _celScriptService = celScriptService;
            _loadCustomAssembliesTask = loadCustomAssembliesTask;
        }

        public async Task<Result<Project>> Load(string projectPath)
        {
            if (!File.Exists(projectPath))
            {
                return new ErrorResult<Project>($"Project file not found: {projectPath}");
            }

            // Generate the Cel Signatures assembly
            // Todo: Use a static function on Project class to do this name lookup consistently
            string projectFolder = Path.GetDirectoryName(projectPath);
            string libraryFolder = Path.Combine(projectFolder, "Library");
            var generateResult = await _celScriptService.GenerateCelSignatures(projectFolder, libraryFolder);
            if (generateResult is ErrorResult<string> generateError)
            {
                return new ErrorResult<Project>($"Failed to generate Cel Signatures: {projectPath}. {generateError.Message}");
            }
            var assemblyFile = generateResult.Data;

            // Load the custom assemblies
            var loadAssembliesResult = _loadCustomAssembliesTask.Load(assemblyFile);
            if (loadAssembliesResult is ErrorResult loadAssembliesError)
            {
                return new ErrorResult<Project>($"Failed to load custom assemblies: {projectPath}. {loadAssembliesError.Message}");
            }

            // Load and deserialize the project file data
            string json = await File.ReadAllTextAsync(projectPath);
            var deserializeResult = DeserializeProject(json);
            if (deserializeResult is ErrorResult<Project> deserializeError)
            {
                return new ErrorResult<Project>($"Failed to load project file: {projectPath}. {deserializeError.Message}");
            }
            var project = deserializeResult.Data;

            // Set the path so we know where to save the file later.
            // The path is not serialized with the project data.
            project.ProjectPath = projectPath;

            // Load any previously persisted project settings
            _settingsService.LoadProjectSettings(project.Id);

            // Remember this as the last opened project
            _settingsService.EditorSettings.PreviousActiveProjectPath = projectPath;

            // Populate the resource registry
            var updateRegistryResult = await _resourceService.UpdateProjectResources(project);
            if (updateRegistryResult is ErrorResult<RegistryUpdateSummary> updateRegistryError)
            {
                return new ErrorResult<Project>($"Failed to update resource registry: {projectPath}. {updateRegistryError.Message}");
            }
            var summary = updateRegistryResult.Data;
            if (summary.WasRegistryModified)
            {
                Log.Information($"Updated resources: Added {summary.Added.Count}, Changed {summary.Changed.Count}, Deleted {summary.Deleted.Count}");
            }

            // Load all Cel Scripts in the project
            var loadCelScriptsResult = await _celScriptService.LoadAllCelScripts(project);
            if (loadCelScriptsResult is ErrorResult loadCelScriptsError)
            {
                return new ErrorResult<Project>($"Failed to load Cel Scripts for project: {projectPath}. {loadCelScriptsError.Message}");
            }

            return new SuccessResult<Project>(project);
        }

        private static Result<Project> DeserializeProject(string json)
        {
            try
            {
                JsonSerializerSettings settings = new()
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                };

                var project = JsonConvert.DeserializeObject<Project>(json, settings);
                return new SuccessResult<Project>(project);
            }
            catch (Exception ex)
            {
                return new ErrorResult<Project>(ex.Message);
            }
        }
    }
}
