using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Celbridge.Server.Services;

/// <summary>
/// Bridges JSON-RPC tool calls from TCP clients (Python REPL) to the MCP
/// HTTP server. Registered as an RPC target on the TCP transport, it forwards
/// tools/list and tools/call requests to the MCP endpoint rather than
/// reimplementing tool discovery and invocation.
/// </summary>
public class McpToolBridge
{
    private readonly IServerService _serverService;
    private readonly ILogger<McpToolBridge> _logger;
    private readonly HttpClient _httpClient;

    private int _nextRequestId;

    public McpToolBridge(
        IServerService serverService,
        ILogger<McpToolBridge> logger)
    {
        _serverService = serverService;
        _logger = logger;
        _httpClient = new HttpClient();
    }

    [JsonRpcMethod("tools/list")]
    public async Task<object> ToolsList()
    {
        var response = await SendMcpRequestAsync("tools/list", null);
        if (response is null)
        {
            return Array.Empty<object>();
        }

        var toolsNode = response["tools"];
        if (toolsNode is not JsonArray toolsArray)
        {
            return Array.Empty<object>();
        }

        var toolInfoList = new List<object>(toolsArray.Count);
        foreach (var toolNode in toolsArray)
        {
            if (toolNode is not JsonObject tool)
            {
                continue;
            }

            var name = tool["name"]?.GetValue<string>() ?? string.Empty;
            var description = tool["description"]?.GetValue<string>() ?? string.Empty;

            var parameters = new List<object>();
            var inputSchema = tool["inputSchema"]?["properties"]?.AsObject();
            var requiredArray = tool["inputSchema"]?["required"]?.AsArray();
            var requiredNames = new HashSet<string>();
            if (requiredArray is not null)
            {
                foreach (var requiredName in requiredArray)
                {
                    var requiredString = requiredName?.GetValue<string>();
                    if (requiredString is not null)
                    {
                        requiredNames.Add(requiredString);
                    }
                }
            }

            if (inputSchema is not null)
            {
                foreach (var property in inputSchema)
                {
                    var parameterName = property.Key;
                    var parameterSchema = property.Value?.AsObject();
                    var parameterType = parameterSchema?["type"]?.GetValue<string>() ?? "string";
                    var parameterDescription = parameterSchema?["description"]?.GetValue<string>() ?? string.Empty;
                    var hasDefaultValue = !requiredNames.Contains(parameterName);

                    object? defaultValue = null;
                    if (hasDefaultValue && parameterSchema?["default"] is JsonNode defaultNode)
                    {
                        defaultValue = defaultNode.GetValue<object>();
                    }

                    var parameterInfo = new Dictionary<string, object?>
                    {
                        ["name"] = parameterName,
                        ["type"] = parameterType,
                        ["description"] = parameterDescription,
                        ["hasDefaultValue"] = hasDefaultValue,
                        ["defaultValue"] = defaultValue
                    };
                    parameters.Add(parameterInfo);
                }
            }

            // Extract alias from annotations if present
            var alias = tool["annotations"]?["alias"]?.GetValue<string>() ?? string.Empty;

            var toolInfo = new Dictionary<string, object?>
            {
                ["name"] = name,
                ["alias"] = alias,
                ["description"] = description,
                ["returnType"] = string.Empty,
                ["parameters"] = parameters
            };
            toolInfoList.Add(toolInfo);
        }

        return toolInfoList;
    }

    [JsonRpcMethod("tools/call")]
    public async Task<object> ToolsCall(string name, JObject? arguments)
    {
        _logger.LogDebug("RPC tools/call: {ToolName}", name);

        var argumentsNode = ConvertJObjectToJsonObject(arguments);

        var callParams = new JsonObject
        {
            ["name"] = name
        };
        if (argumentsNode is not null)
        {
            callParams["arguments"] = argumentsNode;
        }

        try
        {
            var response = await SendMcpRequestAsync("tools/call", callParams);
            if (response is null)
            {
                return new Dictionary<string, object?>
                {
                    ["isSuccess"] = false,
                    ["errorMessage"] = "No response from MCP server",
                    ["value"] = null
                };
            }

            var isError = response["isError"]?.GetValue<bool>() ?? false;
            var contentArray = response["content"]?.AsArray();

            string? textValue = null;
            if (contentArray is not null)
            {
                foreach (var contentItem in contentArray)
                {
                    if (contentItem is JsonObject contentObject &&
                        contentObject["type"]?.GetValue<string>() == "text")
                    {
                        textValue = contentObject["text"]?.GetValue<string>();
                        break;
                    }
                }
            }

            return new Dictionary<string, object?>
            {
                ["isSuccess"] = !isError,
                ["errorMessage"] = isError ? (textValue ?? "Tool call failed") : string.Empty,
                ["value"] = isError ? null : textValue
            };
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Tool '{ToolName}' failed", name);
            return new Dictionary<string, object?>
            {
                ["isSuccess"] = false,
                ["errorMessage"] = exception.Message,
                ["value"] = null
            };
        }
    }

    private async Task<JsonObject?> SendMcpRequestAsync(string method, JsonObject? methodParams)
    {
        var port = _serverService.Port;
        if (port == 0)
        {
            _logger.LogWarning("MCP HTTP server not started, cannot forward {Method}", method);
            return null;
        }

        var requestId = Interlocked.Increment(ref _nextRequestId);

        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["id"] = requestId
        };
        if (methodParams is not null)
        {
            request["params"] = methodParams;
        }

        var url = $"http://127.0.0.1:{port}/mcp";
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Content = JsonContent.Create(request);
        httpRequest.Headers.Accept.Clear();
        httpRequest.Headers.Accept.ParseAdd("application/json");

        var httpResponse = await _httpClient.SendAsync(httpRequest);
        httpResponse.EnsureSuccessStatusCode();

        var responseJson = await httpResponse.Content.ReadAsStringAsync();
        var responseNode = JsonNode.Parse(responseJson);

        return responseNode?["result"]?.AsObject();
    }

    private static JsonObject? ConvertJObjectToJsonObject(JObject? jObject)
    {
        if (jObject is null)
        {
            return null;
        }

        var jsonString = jObject.ToString();
        return JsonNode.Parse(jsonString)?.AsObject();
    }
}
