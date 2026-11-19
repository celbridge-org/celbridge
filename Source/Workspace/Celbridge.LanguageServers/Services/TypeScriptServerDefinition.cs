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

    public JsonNode? CreateInitializationOptions()
    {
        return null;
    }

    public JsonNode? CreateConfiguration(string section)
    {
        return null;
    }
}
