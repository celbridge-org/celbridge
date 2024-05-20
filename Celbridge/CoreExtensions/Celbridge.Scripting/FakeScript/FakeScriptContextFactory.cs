using Celbridge.BaseLibrary.Scripting;

namespace Celbridge.Scripting.FakeScript;

public class FakeScriptContextFactory : IScriptContextFactory
{
    public string Language { get; } = "fake";

    public IScriptContext CreateScriptContext()
    {
        return new FakeScriptContext();
    }
}
