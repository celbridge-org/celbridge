using System.Globalization;
using System.Reflection;
using Celbridge.MCPTools.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Celbridge.Broker.Services;

/// <summary>
/// JSON-RPC target that exposes tool discovery and invocation to connected
/// TCP clients (Python REPL). Scans the MCPTools assembly for
/// [McpServerTool] methods and serves them via tools/list and tools/call.
/// </summary>
public class BrokerRpcHandler
{
    private readonly IApplicationServiceProvider _applicationServices;
    private readonly ILogger<BrokerRpcHandler> _logger;
    private readonly List<ToolDescriptor> _tools = new();

    public BrokerRpcHandler(
        IApplicationServiceProvider applicationServices,
        ILogger<BrokerRpcHandler> logger)
    {
        _applicationServices = applicationServices;
        _logger = logger;

        DiscoverTools();
    }

    private void DiscoverTools()
    {
        var assembly = typeof(AppTools).Assembly;

        var toolTypes = assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null);

        foreach (var toolType in toolTypes)
        {
            var methods = toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null);

            foreach (var method in methods)
            {
                var toolAttribute = method.GetCustomAttribute<McpServerToolAttribute>()!;
                var aliasAttribute = method.GetCustomAttribute<ToolAliasAttribute>();

                var parameters = new List<ToolParameterDescriptor>();
                foreach (var parameterInfo in method.GetParameters())
                {
                    // Skip the injected IApplicationServiceProvider parameter
                    if (parameterInfo.ParameterType == typeof(IApplicationServiceProvider))
                    {
                        continue;
                    }

                    parameters.Add(new ToolParameterDescriptor
                    {
                        Name = parameterInfo.Name ?? string.Empty,
                        TypeName = MapClrTypeToSimpleName(parameterInfo.ParameterType),
                        ParameterType = parameterInfo.ParameterType,
                        Description = string.Empty,
                        HasDefaultValue = parameterInfo.HasDefaultValue,
                        DefaultValue = parameterInfo.HasDefaultValue ? parameterInfo.DefaultValue : null
                    });
                }

                var descriptor = new ToolDescriptor
                {
                    Name = toolAttribute.Name ?? method.Name,
                    Alias = aliasAttribute?.Alias ?? string.Empty,
                    Description = string.Empty,
                    ReturnType = MapReturnType(method.ReturnType),
                    Parameters = parameters,
                    Method = method,
                };

                _tools.Add(descriptor);
            }
        }

        _logger.LogInformation("Discovered {ToolCount} tools from MCPTools assembly", _tools.Count);
    }

    [JsonRpcMethod("tools/list")]
    public object ToolsList()
    {
        var toolInfoList = new List<object>(_tools.Count);
        foreach (var tool in _tools)
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
                ["alias"] = tool.Alias,
                ["description"] = tool.Description,
                ["returnType"] = tool.ReturnType,
                ["parameters"] = parameters
            };
            toolInfoList.Add(toolInfo);
        }

        return toolInfoList;
    }

    [JsonRpcMethod("tools/call")]
    public async Task<object> ToolsCall(string name, JObject? arguments)
    {
        var descriptor = _tools.Find(tool => tool.Name == name);
        if (descriptor is null)
        {
            _logger.LogWarning("Unknown tool '{ToolName}'", name);
            return new Dictionary<string, object?>
            {
                ["isSuccess"] = false,
                ["errorMessage"] = $"Unknown tool '{name}'",
                ["value"] = null
            };
        }

        var argumentsDictionary = ConvertJObjectArguments(arguments);

        _logger.LogDebug("RPC tools/call: {ToolName}", name);

        try
        {
            var result = await InvokeToolAsync(descriptor, argumentsDictionary);
            return new Dictionary<string, object?>
            {
                ["isSuccess"] = result.IsSuccess,
                ["errorMessage"] = result.ErrorMessage,
                ["value"] = result.Value
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

    private async Task<ToolCallResult> InvokeToolAsync(
        ToolDescriptor descriptor,
        IDictionary<string, object?> arguments)
    {
        var method = descriptor.Method;
        var declaringType = method.DeclaringType!;

        // Create an instance of the tool class with IApplicationServiceProvider
        var toolInstance = Activator.CreateInstance(declaringType, _applicationServices);

        var parameterInfos = method.GetParameters();
        var invokeArguments = new object?[parameterInfos.Length];

        for (int i = 0; i < parameterInfos.Length; i++)
        {
            var parameterInfo = parameterInfos[i];
            var parameterName = parameterInfo.Name ?? string.Empty;

            if (arguments.TryGetValue(parameterName, out var rawValue))
            {
                invokeArguments[i] = CoerceValue(rawValue, parameterInfo.ParameterType, parameterName, descriptor.Name);
            }
            else if (parameterInfo.HasDefaultValue)
            {
                invokeArguments[i] = parameterInfo.DefaultValue;
            }
            else
            {
                return ToolCallResult.Failure(
                    $"Missing required parameter '{parameterName}' for tool '{descriptor.Name}'");
            }
        }

        try
        {
            var rawResult = method.Invoke(toolInstance, invokeArguments);
            return await ProcessReturnValue(rawResult);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            return ToolCallResult.Failure(exception.InnerException.Message);
        }
    }

    private async Task<ToolCallResult> ProcessReturnValue(object? rawResult)
    {
        if (rawResult is null)
        {
            return ToolCallResult.Success();
        }

        if (rawResult is Task task)
        {
            await task;

            var taskType = task.GetType();
            if (taskType.IsGenericType)
            {
                var resultProperty = taskType.GetProperty("Result");
                if (resultProperty is not null)
                {
                    var taskResult = resultProperty.GetValue(task);
                    return WrapReturnValue(taskResult);
                }
            }

            return ToolCallResult.Success();
        }

        return WrapReturnValue(rawResult);
    }

    private ToolCallResult WrapReturnValue(object? value)
    {
        if (value is null)
        {
            return ToolCallResult.Success();
        }

        if (value is Result result)
        {
            if (result.IsSuccess)
            {
                var valueProperty = value.GetType().GetProperty("Value");
                var payload = valueProperty?.GetValue(value);
                return ToolCallResult.Success(payload);
            }

            return ToolCallResult.Failure(result.FirstErrorMessage);
        }

        return ToolCallResult.Success(value);
    }

    private object? CoerceValue(object? rawValue, Type targetType, string parameterName, string toolName)
    {
        if (rawValue is null)
        {
            return null;
        }

        var rawType = rawValue.GetType();

        if (targetType.IsAssignableFrom(rawType))
        {
            return rawValue;
        }

        if (rawValue is string stringValue)
        {
            if (targetType == typeof(bool) && bool.TryParse(stringValue, out var boolResult))
            {
                return boolResult;
            }
            if (targetType == typeof(int) && int.TryParse(stringValue, CultureInfo.InvariantCulture, out var intResult))
            {
                return intResult;
            }
        }

        if (IsNumericType(targetType) && IsNumericType(rawType))
        {
            return Convert.ChangeType(rawValue, targetType, CultureInfo.InvariantCulture);
        }

        throw new ArgumentException(
            $"Cannot convert parameter '{parameterName}' from '{rawType.Name}' to '{targetType.Name}' for tool '{toolName}'");
    }

    private Dictionary<string, object?> ConvertJObjectArguments(JObject? arguments)
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

    private static string MapClrTypeToSimpleName(Type type)
    {
        if (type == typeof(string)) return "str";
        if (type == typeof(int) || type == typeof(long)) return "int";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(double) || type == typeof(float)) return "float";
        return type.Name;
    }

    private static string MapReturnType(Type returnType)
    {
        if (returnType == typeof(void)) return "";
        if (returnType == typeof(string)) return "str";
        if (returnType == typeof(int)) return "int";
        if (returnType == typeof(bool)) return "bool";
        if (returnType == typeof(Task)) return "";
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var innerType = returnType.GetGenericArguments()[0];
            return MapReturnType(innerType);
        }
        return "";
    }

    private static bool IsNumericType(Type type)
    {
        return type == typeof(int)
            || type == typeof(long)
            || type == typeof(double)
            || type == typeof(float)
            || type == typeof(decimal)
            || type == typeof(short)
            || type == typeof(byte);
    }
}
