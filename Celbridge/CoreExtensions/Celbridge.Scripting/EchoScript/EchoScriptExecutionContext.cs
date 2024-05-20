namespace Celbridge.Scripting.EchoScript;

public class EchoScriptExecutionContext : IScriptExecutionContext
{
    public string Command { get; } = string.Empty;

    // Todo: Asynchronously execute the command and report output and errors
}