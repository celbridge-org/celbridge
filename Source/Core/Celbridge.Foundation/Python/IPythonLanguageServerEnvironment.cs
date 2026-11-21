namespace Celbridge.Python;

/// <summary>
/// Gives what the Python language server needs from the Python environment. That is the paths that start the
/// server, and the inherited variables to leave out of the server's process.
/// </summary>
public interface IPythonLanguageServerEnvironment
{
    /// <summary>
    /// Returns the path of the language server's executable, whether or not the server is installed yet.
    /// </summary>
    string ServerExecutablePath { get; }

    /// <summary>
    /// Returns the path of the celbridge tool environment's interpreter, whether or not the environment is installed
    /// yet. The server asks the interpreter for the import search paths, so the server resolves the celbridge
    /// package.
    /// </summary>
    string InterpreterPath { get; }

    /// <summary>
    /// Returns the names of the variables in Celbridge's own environment that control uv or Python. A console's
    /// environment leaves the same variables out.
    /// </summary>
    IReadOnlyList<string> GetInheritedVariablesToRemove();
}
