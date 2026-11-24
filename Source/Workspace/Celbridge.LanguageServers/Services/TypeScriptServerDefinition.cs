using System.Text.Json.Nodes;
using Celbridge.Settings;

namespace Celbridge.LanguageServers.Services;

/// <summary>
/// The language server for TypeScript and JavaScript, which is the TypeScript compiler started in its language
/// server mode.
/// </summary>
public sealed class TypeScriptServerDefinition : ILanguageServerDefinition
{
    private readonly TypeScriptDependency _typeScriptDependency;

    public TypeScriptServerDefinition(TypeScriptDependency typeScriptDependency)
    {
        _typeScriptDependency = typeScriptDependency;
    }

    public string Id => "typescript";

    public FeatureFlagDescriptor Feature => FeatureFlagCatalog.TypeScript;

    public IReadOnlyDictionary<string, string> LanguageIds { get; } = new Dictionary<string, string>
    {
        [".ts"] = "typescript",
        [".mts"] = "typescript",
        [".cts"] = "typescript",
        [".tsx"] = "typescriptreact",
        [".js"] = "javascript",
        [".mjs"] = "javascript",
        [".cjs"] = "javascript",
        [".jsx"] = "javascriptreact",
    };

    // The compiler watches the project's files itself.
    public ReportedFiles? ReportedFiles => null;

    public bool UseConsoleEnvironment => false;

    public LanguageServerInvocation CreateInvocation()
    {
        var arguments = new List<string>
        {
            "--lsp",
            "--stdio",
        };

        return new LanguageServerInvocation(
            _typeScriptDependency.ExecutablePath,
            arguments,
            new Dictionary<string, string>());
    }

    // The compiler runs with the inherited environment unchanged.
    public void FilterInheritedEnvironment(IDictionary<string, string> inheritedEnvironment)
    {
    }

    public JsonNode? CreateInitializationOptions()
    {
        return null;
    }

    // The compiler runs with its default settings.
    public JsonNode? CreateConfiguration(string section)
    {
        return null;
    }
}
