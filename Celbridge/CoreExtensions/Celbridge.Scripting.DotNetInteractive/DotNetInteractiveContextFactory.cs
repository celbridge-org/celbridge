using Celbridge.BaseLibrary.Scripting;

namespace Celbridge.Scripting.DotNetInteractive;

public class DotNetInteractiveContextFactory : IScriptContextFactory
{
    public string Language { get; } = "DotNetInteractive";

    public IScriptContext CreateScriptContext()
    {
        return new DotNetInteractiveContext();
    }
}
