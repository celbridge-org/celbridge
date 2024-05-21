using Celbridge.BaseLibrary.Scripting;

namespace Celbridge.Scripting;

public class ScriptingService : IScriptingService
{
    public IScriptContext CreateScriptContext()
    {
        return new DotNetInteractiveContext();
    }
}
