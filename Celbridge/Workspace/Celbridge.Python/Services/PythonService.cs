using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Utilities;
using Celbridge.Workspace;

using Path = System.IO.Path;

namespace Celbridge.Python.Services;

public class PythonService : IPythonService, IDisposable
{
    private readonly IProjectService _projectService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IUtilityService _utilityService;

    public PythonService(
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper,
        IUtilityService utilityService)
    {
        _projectService = projectService;
        _workspaceWrapper = workspaceWrapper;
        _utilityService = utilityService;
    }

    public async Task<Result> InitializePython()
    {
        try
        {
            if (_projectService.CurrentProject is null)
            {
                return Result<string>.Fail("Failed to run python script as no project is loaded");
            }
            var workingDir = _projectService.CurrentProject.ProjectFolderPath;

            var installResult = await PythonInstaller.InstallPythonAsync();
            if (installResult.IsFailure)
            {
                return Result.Fail("Failed to ensure Python is installed")
                    .WithErrors(installResult);
            }
            var pythonFolder = installResult.Value;

            var pythonPath = Path.Combine(pythonFolder, "python.exe");
            pythonPath = GetSafeQuotedPath(pythonPath);

            var scriptPath = Path.Combine(pythonFolder, "startup.py");
            scriptPath = GetSafeQuotedPath(scriptPath);

            var iPythonPath = Path.Combine(workingDir, ".celbridge", "ipython");
            iPythonPath = GetSafeQuotedPath(iPythonPath);

            Directory.CreateDirectory(iPythonPath);

            SetCelbridgeVersion();

            // Run startup script then switch to IPython interactive mode
            var commandLine = $"{pythonPath} -m IPython --no-banner --ipython-dir={iPythonPath} -i {scriptPath}";

            var terminal = _workspaceWrapper.WorkspaceService.ConsoleService.Terminal;
            terminal.Start(commandLine, workingDir);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result<string>.Fail("An error occurred when initializing Python")
                .WithException(ex);
        }
    }

    private void SetCelbridgeVersion()
    {
        // Set the Celbridge version number as an environment variable so we can print it from Python at startup.
        var environmentInfo = _utilityService.GetEnvironmentInfo();
        var version = environmentInfo.AppVersion;
        var configuration = environmentInfo.Configuration;
        var celbridgeVersion = configuration == "Debug" ? $"{version} (Debug)" : $"{version}";
        Environment.SetEnvironmentVariable("CELBRIDGE_VERSION", $"{celbridgeVersion}");
    }

    private static string GetSafeQuotedPath(string path)
    {
        if (path.Any(char.IsWhiteSpace) &&
           !(path.StartsWith("\"") && path.EndsWith("\"")))
        {
            path = $"\"{path}\"";
        }

        return path;
    }

    private bool _disposed;

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

    ~PythonService()
    {
        Dispose(false);
    }
}
