using Celbridge.BaseLibrary.Scripting;

namespace Celbridge.Scripting.FakeScript;

public class FakeScriptContextFactory : IScriptContextFactory
{
    public string Language { get; } = "FakeScript";

    public IScriptContext CreateScriptContext()
    {
        return new FakeScriptContext();
    }
}
