using Celbridge.BaseLibrary.Scripting;

namespace Celbridge.Scripting.EchoScript;

public class EchoScriptContextFactory : IScriptContextFactory
{
    public string Language { get; } = "echo";

    public IScriptContext CreateScriptContext()
    {
        return new EchoScriptContext();
    }
}
