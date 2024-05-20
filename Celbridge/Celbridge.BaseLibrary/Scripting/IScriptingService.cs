namespace Celbridge.BaseLibrary.Scripting;

/// <summary>
/// The scripting service manages the excution of scripts using registered scripting engines.
/// </summary>
public interface IScriptingService
{
    Result RegisterScriptContextFactory(IScriptContextFactory factory);

    Result<IScriptContext> AcquireScriptContext(string language);
}
