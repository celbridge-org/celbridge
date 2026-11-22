namespace Celbridge.Python;

/// <summary>
/// Gives what the Python language server needs from the Python environment. That is the paths that start the
/// server, and the filter for the environment the server inherits.
/// </summary>
public interface IPythonLanguageServerEnvironment
{
    /// <summary>
    /// Returns the path of the language server's executable, whether or not the server is installed yet.
    /// </summary>
    string ServerExecutablePath { get; }

    /// <summary>
    /// Returns the path of the interpreter that gives the server the import search paths. While the first Python
    /// console in the Run menu's list runs, the interpreter belongs to that console's environment, which contains
    /// the console's packages. Otherwise the interpreter belongs to the celbridge tool environment, whether or not
    /// the environment is installed yet. The server resolves the celbridge package through either one.
    /// </summary>
    string InterpreterPath { get; }

    /// <summary>
    /// Removes the inherited variables that control uv or Python. A console's environment leaves the same
    /// variables out.
    /// </summary>
    void FilterInheritedEnvironment(IDictionary<string, string> inheritedEnvironment);
}
