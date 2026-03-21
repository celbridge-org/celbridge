using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Celbridge.Broker.Services;

/// <summary>
/// Scans assemblies for static methods decorated with [McpTool] and builds
/// a registry of tool descriptors. Provides lookup by tool name.
/// </summary>
public class ToolRegistry
{
    private readonly ILogger<ToolRegistry> _logger;
    private readonly Dictionary<string, ToolDescriptor> _toolsByName = new();
    private readonly List<ToolDescriptor> _tools = new();

    public ToolRegistry(ILogger<ToolRegistry> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Scans the given assemblies for [McpTool] attributed static methods
    /// and populates the registry.
    /// </summary>
    public void DiscoverTools(IEnumerable<Assembly> assemblies)
    {
        foreach (var assembly in assemblies)
        {
            DiscoverToolsInAssembly(assembly);
        }

        _logger.LogInformation("Discovered {ToolCount} broker tools", _tools.Count);
    }

    /// <summary>
    /// Returns all discovered tool descriptors.
    /// </summary>
    public IReadOnlyList<ToolDescriptor> GetTools()
    {
        return _tools;
    }

    /// <summary>
    /// Finds a tool descriptor by its slash-separated name.
    /// Returns null if no tool with that name exists.
    /// </summary>
    public ToolDescriptor? FindTool(string toolName)
    {
        _toolsByName.TryGetValue(toolName, out var descriptor);
        return descriptor;
    }

    private void DiscoverToolsInAssembly(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Some types may fail to load (e.g. missing dependencies).
            // Process the types that did load successfully.
            types = ex.Types.Where(t => t is not null).ToArray()!;
            _logger.LogWarning(
                "Some types in assembly {Assembly} could not be loaded",
                assembly.FullName);
        }

        foreach (var type in types)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);

            foreach (var method in methods)
            {
                var toolAttribute = method.GetCustomAttribute<McpToolAttribute>();
                if (toolAttribute is null)
                {
                    continue;
                }

                var descriptor = BuildDescriptor(method, toolAttribute);
                if (descriptor is not null)
                {
                    RegisterTool(descriptor);
                }
            }
        }
    }

    private ToolDescriptor? BuildDescriptor(MethodInfo method, McpToolAttribute toolAttribute)
    {
        var toolName = toolAttribute.Name;

        if (string.IsNullOrWhiteSpace(toolName))
        {
            _logger.LogWarning(
                "Skipping tool on {Type}.{Method}: tool name is empty",
                method.DeclaringType?.Name,
                method.Name);
            return null;
        }

        var alias = toolAttribute.Alias;

        if (string.IsNullOrWhiteSpace(alias))
        {
            _logger.LogWarning(
                "Skipping tool '{ToolName}' on {Type}.{Method}: alias is empty",
                toolName,
                method.DeclaringType?.Name,
                method.Name);
            return null;
        }

        var parameters = BuildParameterDescriptors(method);
        var returnType = GetSimpleReturnType(method.ReturnType);

        return new ToolDescriptor
        {
            Name = toolName,
            Alias = alias,
            Description = toolAttribute.Description,
            ReturnType = returnType,
            Parameters = parameters,
            Method = method
        };
    }

    private List<ToolParameterDescriptor> BuildParameterDescriptors(MethodInfo method)
    {
        var parameterInfos = method.GetParameters();
        var descriptors = new List<ToolParameterDescriptor>(parameterInfos.Length);

        foreach (var parameterInfo in parameterInfos)
        {
            var mcpParamAttribute = parameterInfo.GetCustomAttribute<McpParamAttribute>();
            var description = mcpParamAttribute?.Description ?? string.Empty;

            var descriptor = new ToolParameterDescriptor
            {
                Name = parameterInfo.Name ?? string.Empty,
                TypeName = MapToSimpleTypeName(parameterInfo.ParameterType),
                ParameterType = parameterInfo.ParameterType,
                Description = description,
                HasDefaultValue = parameterInfo.HasDefaultValue,
                DefaultValue = parameterInfo.HasDefaultValue ? parameterInfo.DefaultValue : null
            };

            descriptors.Add(descriptor);
        }

        return descriptors;
    }

    /// <summary>
    /// Maps a CLR return type to a simple client-friendly type name.
    /// Returns empty string for void, Task, and Task&lt;Result&gt; (no payload).
    /// </summary>
    private string GetSimpleReturnType(Type returnType)
    {
        if (returnType == typeof(void))
        {
            return string.Empty;
        }

        if (returnType == typeof(Task))
        {
            return string.Empty;
        }

        if (returnType == typeof(Task<Result>))
        {
            return string.Empty;
        }

        // Task<Result<T>> -> extract T
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var innerType = returnType.GetGenericArguments()[0];
            if (innerType.IsGenericType && innerType.GetGenericTypeDefinition() == typeof(Result<>))
            {
                var payloadType = innerType.GetGenericArguments()[0];
                return MapToSimpleTypeName(payloadType);
            }
        }

        return MapToSimpleTypeName(returnType);
    }

    /// <summary>
    /// Maps a CLR type to a simple Python-style type name for display.
    /// </summary>
    private string MapToSimpleTypeName(Type type)
    {
        if (type == typeof(string) || type == typeof(ResourceKey))
        {
            return "str";
        }

        if (type == typeof(int) || type == typeof(long))
        {
            return "int";
        }

        if (type == typeof(bool))
        {
            return "bool";
        }

        if (type == typeof(double) || type == typeof(float))
        {
            return "float";
        }

        return type.Name;
    }

    private void RegisterTool(ToolDescriptor descriptor)
    {
        if (_toolsByName.ContainsKey(descriptor.Name))
        {
            _logger.LogWarning(
                "Duplicate tool name '{ToolName}' on {Type}.{Method}, skipping",
                descriptor.Name,
                descriptor.Method.DeclaringType?.Name,
                descriptor.Method.Name);
            return;
        }

        _toolsByName[descriptor.Name] = descriptor;
        _tools.Add(descriptor);

        _logger.LogDebug("Registered tool '{ToolName}'", descriptor.Name);
    }
}
