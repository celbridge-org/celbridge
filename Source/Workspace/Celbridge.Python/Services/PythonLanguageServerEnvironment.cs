using System.Collections;

namespace Celbridge.Python.Services;

public sealed class PythonLanguageServerEnvironment : IPythonLanguageServerEnvironment
{
    private readonly BasedpyrightDependency _basedpyrightDependency;
    private readonly CelbridgeWheelFolders _folders;

    public PythonLanguageServerEnvironment(BasedpyrightDependency basedpyrightDependency, CelbridgeWheelFolders folders)
    {
        _basedpyrightDependency = basedpyrightDependency;
        _folders = folders;
    }

    public string ServerExecutablePath => _basedpyrightDependency.ExecutablePath;

    public string InterpreterPath => _folders.CelbridgeToolInterpreterPath;

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
}
