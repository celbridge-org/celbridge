using Celbridge.Services;

namespace Celbridge.Tasks
{
    public class PlayCelTask
    {
        private IProjectService _projectService;
        private ISettingsService _settingsService;
        private ICelScriptService _celScriptService;

        public PlayCelTask(IProjectService projectService,
                           ISettingsService settingsService,
                           ICelScriptService celScriptService)
        {
            _projectService = projectService;
            _settingsService = settingsService;
            _celScriptService = celScriptService;
        }

        public async Task<Result> PlayCel(string celScriptName, string celName)
        {
            try
            {
                var activeProject = _projectService.ActiveProject;
                if (activeProject == null)
                {
                    return new ErrorResult("Failed to Play Cel. No active project.");
                }

                var projectFolder = activeProject.ProjectFolder;
                var libraryFolder = activeProject.LibraryFolder;

                Guard.IsNotNull(_settingsService.EditorSettings);
                var chatAPIKey = _settingsService.EditorSettings.OpenAIKey;
                var sheetsAPIKey = _settingsService.EditorSettings.SheetsAPIKey;

                var startResult = await _celScriptService.StartApplication(celScriptName, celName, projectFolder, libraryFolder, chatAPIKey, sheetsAPIKey);
                if (startResult is ErrorResult startError)
                {
                    return new ErrorResult($"Failed to start application. {startError.Message}");
                }

                return new SuccessResult();
            }
            catch (Exception ex)
            {
                return new ErrorResult<string>($"Failed to Play Cel. {ex.Message}");
            }
        }
    }
}
