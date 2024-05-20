namespace Celbridge.BaseLibrary.Scripting;

public interface IScriptContextFactory
{
    string Language { get;}

    IScriptContext CreateScriptContext();
}
