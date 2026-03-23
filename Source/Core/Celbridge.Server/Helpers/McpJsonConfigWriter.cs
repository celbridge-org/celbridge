using System.Text.Json;
using System.Text.Json.Nodes;

namespace Celbridge.Server.Helpers;

/// <summary>
/// Manages the .mcp.json configuration file that tells MCP clients
/// (e.g. Claude CLI) how to connect to the agent server's MCP HTTP endpoint.
/// Preserves any user-defined MCP server entries when writing or removing
/// the Celbridge entry.
/// </summary>
public static class McpJsonConfigWriter
{
    private const string ConfigFileName = ".mcp.json";
    private const string ServerEntryName = "celbridge";

    /// <summary>
    /// Adds or updates the Celbridge entry in .mcp.json, preserving any
    /// other MCP server entries the user has configured.
    /// </summary>
    public static void WriteConfigFile(string folderPath, int port, ILogger logger)
    {
        var configFilePath = System.IO.Path.Combine(folderPath, ConfigFileName);

        var root = LoadExistingConfig(configFilePath);

        var mcpServers = root["mcpServers"]?.AsObject();
        if (mcpServers is null)
        {
            mcpServers = new JsonObject();
            root["mcpServers"] = mcpServers;
        }

        var celbridgeEntry = new JsonObject
        {
            ["type"] = "http",
            ["url"] = $"http://127.0.0.1:{port}/mcp"
        };

        mcpServers[ServerEntryName] = celbridgeEntry;

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = root.ToJsonString(jsonOptions);
        File.WriteAllText(configFilePath, json);

        logger.LogInformation("Wrote MCP config to {ConfigFilePath}", configFilePath);
    }

    /// <summary>
    /// Removes the Celbridge entry from .mcp.json, preserving any other
    /// MCP server entries. Deletes the file entirely if no entries remain.
    /// </summary>
    public static void RemoveConfigEntry(string folderPath, ILogger logger)
    {
        var configFilePath = System.IO.Path.Combine(folderPath, ConfigFileName);

        if (!File.Exists(configFilePath))
        {
            return;
        }

        var root = LoadExistingConfig(configFilePath);
        var mcpServers = root["mcpServers"]?.AsObject();

        if (mcpServers is null)
        {
            return;
        }

        mcpServers.Remove(ServerEntryName);

        if (mcpServers.Count == 0)
        {
            File.Delete(configFilePath);
            logger.LogInformation("Deleted empty MCP config at {ConfigFilePath}", configFilePath);
        }
        else
        {
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = root.ToJsonString(jsonOptions);
            File.WriteAllText(configFilePath, json);
            logger.LogInformation("Removed Celbridge entry from {ConfigFilePath}", configFilePath);
        }
    }

    private static JsonObject LoadExistingConfig(string configFilePath)
    {
        if (!File.Exists(configFilePath))
        {
            return new JsonObject();
        }

        try
        {
            var existingJson = File.ReadAllText(configFilePath);
            var parsed = JsonNode.Parse(existingJson);
            return parsed?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }
}
