using Celbridge.BaseLibrary.Scripting;

namespace Celbridge.Scripting.EchoScript;

public class FakeScriptContextFactory : IScriptContextFactory
{
    public string Language { get; } = "fake";

    public IScriptContext CreateScriptContext()
    {
        return new FakeScriptContext();
    }
}
