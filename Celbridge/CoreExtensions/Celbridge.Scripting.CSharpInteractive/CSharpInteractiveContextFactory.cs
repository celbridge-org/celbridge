using Celbridge.BaseLibrary.Scripting;

namespace Celbridge.Scripting.CSharpInteractive;

public class CSharpInteractiveContextFactory : IScriptContextFactory
{
    public string Language { get; } = "fake";

    public IScriptContext CreateScriptContext()
    {
        return new CSharpInteractiveContext();
    }
}
