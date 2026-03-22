using System.Reflection;
using System.Text.Json;
using Celbridge.ApplicationEnvironment;
using Celbridge.Commands;
using Celbridge.Console;
using ModelContextProtocol.Server;

namespace Celbridge.MCPTools.Tools;

/// <summary>
/// General application tools for version info, logging, and client configuration.
/// </summary>
[McpServerToolType]
public class AppTools
{
    private readonly IEnvironmentService _environmentService;
    private readonly ICommandService _commandService;

    public AppTools(
        IEnvironmentService environmentService,
        ICommandService commandService)
    {
        _environmentService = environmentService;
        _commandService = commandService;
    }

    /// <summary>
    /// Returns the application version string.
    /// </summary>
    [McpServerTool(Name = "app_version", ReadOnly = true, Idempotent = true)]
    [ToolAlias("app_version")]
    public string AppVersion()
    {
        var environmentInfo = _environmentService.GetEnvironmentInfo();
        return environmentInfo.AppVersion;
    }

    /// <summary>
    /// Logs an informational message to the application log.
    /// </summary>
    /// <param name="message">The message to log.</param>
    [McpServerTool(Name = "log_info", ReadOnly = false, Idempotent = true)]
    [ToolAlias("log")]
    public void LogInfo(string message)
    {
        ExecuteLog(message, MessageType.Information);
    }

    /// <summary>
    /// Logs a warning message to the application log.
    /// </summary>
    /// <param name="message">The warning message to log.</param>
    [McpServerTool(Name = "log_warning", ReadOnly = false, Idempotent = true)]
    [ToolAlias("log_warning")]
    public void LogWarning(string message)
    {
        ExecuteLog(message, MessageType.Warning);
    }

    /// <summary>
    /// Logs an error message to the application log.
    /// </summary>
    /// <param name="message">The error message to log.</param>
    [McpServerTool(Name = "log_error", ReadOnly = false, Idempotent = true)]
    [ToolAlias("log_error")]
    public void LogError(string message)
    {
        ExecuteLog(message, MessageType.Error);
    }

    /// <summary>
    /// Returns context information for AI agents working with Celbridge projects.
    /// Call this tool first to understand resource key conventions, project structure,
    /// and how to use the other tools effectively.
    /// </summary>
    [McpServerTool(Name = "get_context", ReadOnly = true, Idempotent = true)]
    [ToolAlias("get_context")]
    public string GetContext()
    {
        return LoadEmbeddedResource("Celbridge.MCPTools.Assets.AgentContext.md");
    }

    /// <summary>
    /// Returns the alias mapping for all tools, used by scripting clients to build friendly method names.
    /// </summary>
    [McpServerTool(Name = "get_client_aliases", ReadOnly = true, Idempotent = true)]
    [ToolAlias("get_client_aliases")]
    public string GetClientAliases()
    {
        var aliasMapping = BuildAliasMapping();
        return JsonSerializer.Serialize(aliasMapping);
    }

    private void ExecuteLog(string message, MessageType messageType)
    {
        _commandService.Execute<IPrintCommand>(command =>
        {
            command.Message = message;
            command.MessageType = messageType;
        });
    }

    private static string LoadEmbeddedResource(string resourceName)
    {
        var assembly = typeof(AppTools).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return $"Resource '{resourceName}' not found.";
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Scans all McpServerTool methods in the MCPTools assembly for ToolAlias
    /// attributes and builds a mapping of MCP tool name to alias.
    /// </summary>
    private static Dictionary<string, string> BuildAliasMapping()
    {
        var mapping = new Dictionary<string, string>();
        var assembly = typeof(AppTools).Assembly;

        var toolTypes = assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null);

        foreach (var toolType in toolTypes)
        {
            var toolMethods = toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null);

            foreach (var method in toolMethods)
            {
                var toolAttribute = method.GetCustomAttribute<McpServerToolAttribute>()!;
                var aliasAttribute = method.GetCustomAttribute<ToolAliasAttribute>();

                var toolName = toolAttribute.Name ?? method.Name;
                var alias = aliasAttribute?.Alias ?? string.Empty;

                if (!string.IsNullOrEmpty(alias))
                {
                    mapping[toolName] = alias;
                }
            }
        }

        return mapping;
    }
}
