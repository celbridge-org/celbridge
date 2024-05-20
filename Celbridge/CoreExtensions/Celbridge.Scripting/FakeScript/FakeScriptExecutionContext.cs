namespace Celbridge.Scripting.EchoScript;

public class FakeScriptExecutionContext : IScriptExecutionContext
{
    public string Command { get; } = string.Empty;

    // Todo: Asynchronously execute the command and report output and errors
}