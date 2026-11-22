using System.Collections;
using Celbridge.Workspace;

namespace Celbridge.Python.Services;

public sealed class PythonLanguageServerEnvironment : IPythonLanguageServerEnvironment
{
    private const string PythonFileExtension = ".py";
    private const string PythonSessionTypeId = "python";

    private readonly BasedpyrightDependency _basedpyrightDependency;
    private readonly CelbridgeWheelFolders _folders;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public PythonLanguageServerEnvironment(
        BasedpyrightDependency basedpyrightDependency,
        CelbridgeWheelFolders folders,
        IWorkspaceWrapper workspaceWrapper)
    {
        _basedpyrightDependency = basedpyrightDependency;
        _folders = folders;
        _workspaceWrapper = workspaceWrapper;
    }

    public string ServerExecutablePath => _basedpyrightDependency.ExecutablePath;

    public string InterpreterPath
    {
        get
        {
            var consoleEnvironmentFolder = GetConsoleEnvironmentFolder();
            if (string.IsNullOrEmpty(consoleEnvironmentFolder))
            {
                return _folders.CelbridgeToolInterpreterPath;
            }

            // uv builds a console's temporary environment as a virtual environment.
            return OperatingSystem.IsWindows()
                ? Path.Combine(consoleEnvironmentFolder, "Scripts", "python.exe")
                : Path.Combine(consoleEnvironmentFolder, "bin", "python");
        }
    }

    public IReadOnlyList<string> GetInheritedVariablesToRemove()
    {
        var environment = new Dictionary<string, string>();
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var name = entry.Key as string;
            if (!string.IsNullOrEmpty(name))
            {
                environment[name] = entry.Value as string ?? string.Empty;
            }
        }

        return PythonEnvironmentFilter.Apply(environment);
    }

    // The first Python console in the Run menu's list for a Python file runs in an environment with the
    // console's packages. Returns an empty string when no such console runs.
    private string GetConsoleEnvironmentFolder()
    {
        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return string.Empty;
        }

        var sessions = _workspaceWrapper.WorkspaceService.ConsoleService.Sessions;

        return sessions.GetRunEnvironmentFolder(PythonFileExtension, PythonSessionTypeId);
    }
}
