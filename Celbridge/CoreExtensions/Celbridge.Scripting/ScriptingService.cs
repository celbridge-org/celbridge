using Celbridge.BaseLibrary.Scripting;

namespace Celbridge.Scripting;

public class ScriptingService : IScriptingService
{
    public List<IScriptContextFactory> ScriptContextFactories { get; } = new();

    public Result RegisterScriptContextFactory(IScriptContextFactory factory)
    {
        if (ScriptContextFactories.Any(f => f.GetType() == factory.GetType()))
        {
            return Result.Fail("ScriptContextFactory already registered.");
        }

        ScriptContextFactories.Add(factory);

        return Result.Ok();
    }

    public Result<IScriptContext> AcquireScriptContext(string language)
    {
        foreach (var factory in ScriptContextFactories)
        {
            if (factory.Language.CompareTo(language) == 0)
            {
                var scriptContext = factory.CreateScriptContext();
                return Result<IScriptContext>.Ok(scriptContext);
            }
        }

        return Result<IScriptContext>.Fail($"No ScriptContextFactory registered for language {language}.");
    }
}
