using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Celbridge.Broker.Services;

/// <summary>
/// JSON-RPC target that exposes broker tool discovery and invocation
/// to connected clients. Registered as an RPC target on each TCP connection.
/// Method names follow the MCP convention (tools/list, tools/call).
/// </summary>
public class BrokerRpcHandler
{
    private readonly IBrokerService _brokerService;
    private readonly ILogger<BrokerRpcHandler> _logger;

    public BrokerRpcHandler(
        IBrokerService brokerService,
        ILogger<BrokerRpcHandler> logger)
    {
        _brokerService = brokerService;
        _logger = logger;
    }

    /// <summary>
    /// Returns the list of all discovered tools with their metadata.
    /// Called by clients as the "tools/list" JSON-RPC method.
    /// </summary>
    [JsonRpcMethod("tools/list")]
    public object ToolsList()
    {
        var tools = _brokerService.GetTools();

        var toolInfoList = new List<object>(tools.Count);
        foreach (var tool in tools)
        {
            var parameters = new List<object>(tool.Parameters.Count);
            foreach (var parameter in tool.Parameters)
            {
                var parameterInfo = new Dictionary<string, object?>
                {
                    ["name"] = parameter.Name,
                    ["type"] = parameter.TypeName,
                    ["description"] = parameter.Description,
                    ["hasDefaultValue"] = parameter.HasDefaultValue,
                    ["defaultValue"] = parameter.DefaultValue
                };
                parameters.Add(parameterInfo);
            }

            var toolInfo = new Dictionary<string, object?>
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = parameters
            };
            toolInfoList.Add(toolInfo);
        }

        return toolInfoList;
    }

    /// <summary>
    /// Invokes a tool by name with the given arguments.
    /// Called by clients as the "tools/call" JSON-RPC method.
    /// </summary>
    [JsonRpcMethod("tools/call")]
    public async Task<object> ToolsCall(string name, JObject? arguments)
    {
        var argumentsDictionary = ConvertArguments(arguments);

        _logger.LogDebug("RPC tools/call: {ToolName}", name);
        var result = await _brokerService.CallToolAsync(name, argumentsDictionary);

        return new Dictionary<string, object?>
        {
            ["isSuccess"] = result.IsSuccess,
            ["errorMessage"] = result.ErrorMessage,
            ["value"] = result.Value
        };
    }

    /// <summary>
    /// Converts a JObject from the JSON-RPC layer into a plain dictionary
    /// with primitive .NET values suitable for the tool executor.
    /// </summary>
    private Dictionary<string, object?> ConvertArguments(JObject? arguments)
    {
        var result = new Dictionary<string, object?>();

        if (arguments is null)
        {
            return result;
        }

        foreach (var property in arguments.Properties())
        {
            result[property.Name] = ConvertJToken(property.Value);
        }

        return result;
    }

    private object? ConvertJToken(JToken token)
    {
        switch (token.Type)
        {
            case JTokenType.String:
                return token.Value<string>();
            case JTokenType.Integer:
                return token.Value<long>();
            case JTokenType.Float:
                return token.Value<double>();
            case JTokenType.Boolean:
                return token.Value<bool>();
            case JTokenType.Null:
            case JTokenType.Undefined:
                return null;
            default:
                return token.ToString();
        }
    }
}
