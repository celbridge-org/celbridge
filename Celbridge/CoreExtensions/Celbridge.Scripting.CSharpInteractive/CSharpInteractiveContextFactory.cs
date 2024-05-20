using Celbridge.BaseLibrary.Scripting;

namespace Celbridge.Scripting.CSharpInteractive;

public class CSharpInteractiveContextFactory : IScriptContextFactory
{
    public string Language { get; } = "CSharpInteractive";

    public IScriptContext CreateScriptContext()
    {
        return new CSharpInteractiveContext();
    }
}
