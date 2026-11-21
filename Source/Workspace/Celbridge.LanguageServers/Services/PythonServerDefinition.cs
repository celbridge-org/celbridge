using System.Text.Json.Nodes;
using Celbridge.Python;
using Celbridge.Settings;

namespace Celbridge.LanguageServers.Services;

/// <summary>
/// The language server for Python, which is basedpyright.
/// </summary>
public sealed class PythonServerDefinition : ILanguageServerDefinition
{
    private const string PythonSection = "python";
    private const string BasedpyrightSection = "basedpyright";
    private const string AnalysisSection = "basedpyright.analysis";
    private const string AnalysisProperty = "analysis";
    private const string PythonPathProperty = "pythonPath";

    // basedpyright's own default mode marks far more code than pyright's default, the standard mode. A console may
    // install a package that no environment on disk contains, so an unresolved import is a warning.
    private const string AnalysisSettingsJson = """
        {
          "typeCheckingMode": "standard",
          "diagnosticSeverityOverrides": {
            "reportMissingImports": "warning"
          }
        }
        """;

    private readonly IPythonLanguageServerEnvironment _environment;

    public PythonServerDefinition(IPythonLanguageServerEnvironment environment)
    {
        _environment = environment;
    }

    public string Id => "python";

    public FeatureFlagDescriptor Feature => FeatureFlagCatalog.Python;

    public IReadOnlyDictionary<string, string> LanguageIds { get; } = new Dictionary<string, string>
    {
        [".py"] = "python",
        [".pyi"] = "python",
    };

    // basedpyright does not watch the project's files. A config file changes which files the server checks, and how.
    // A py.typed marker file changes how the server resolves the imports of a package.
    public ReportedFiles? ReportedFiles { get; } = new ReportedFiles(
        new HashSet<string> { ".py", ".pyi" },
        new HashSet<string> { "pyrightconfig.json", "pyproject.toml", "py.typed" });

    public LanguageServerInvocation CreateInvocation()
    {
        var arguments = new List<string>
        {
            "--stdio",
        };

        // A user's own Python variables would change the launcher's interpreter and the import search paths. A
        // console's environment leaves the same variables out.
        return new LanguageServerInvocation(
            _environment.ServerExecutablePath,
            arguments,
            new Dictionary<string, string>(),
            _environment.GetInheritedVariablesToRemove());
    }

    public JsonNode? CreateInitializationOptions()
    {
        return null;
    }

    public JsonNode? CreateConfiguration(string section)
    {
        switch (section)
        {
            case PythonSection:
                // The server asks this interpreter for its import search paths.
                return new JsonObject
                {
                    [PythonPathProperty] = _environment.InterpreterPath,
                };

            case BasedpyrightSection:
                return new JsonObject
                {
                    [AnalysisProperty] = JsonNode.Parse(AnalysisSettingsJson),
                };

            case AnalysisSection:
                return JsonNode.Parse(AnalysisSettingsJson);

            default:
                return null;
        }
    }
}
