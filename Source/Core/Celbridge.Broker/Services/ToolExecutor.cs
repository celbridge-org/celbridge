using System.Globalization;
using System.Reflection;

namespace Celbridge.Broker.Services;

/// <summary>
/// Deserializes call arguments to match a tool method's parameter signature
/// and invokes the static method. Handles void, Task, Task&lt;Result&gt;, and
/// value-returning methods.
/// </summary>
public class ToolExecutor
{
    private readonly ILogger<ToolExecutor> _logger;

    public ToolExecutor(ILogger<ToolExecutor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Invokes the tool described by the given descriptor, passing the supplied arguments.
    /// Arguments are coerced to match the method's parameter types.
    /// </summary>
    public async Task<ToolCallResult> ExecuteAsync(
        ToolDescriptor descriptor,
        IDictionary<string, object?> arguments)
    {
        var method = descriptor.Method;
        var parameterInfos = method.GetParameters();

        object?[] invokeArguments;
        try
        {
            invokeArguments = BuildInvokeArguments(descriptor, parameterInfos, arguments);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Parameter error for tool '{ToolName}': {Error}", descriptor.Name, ex.Message);
            return ToolCallResult.Failure(ex.Message);
        }

        try
        {
            var rawResult = method.Invoke(null, invokeArguments);
            return await ProcessReturnValue(descriptor, method, rawResult);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            _logger.LogWarning(ex.InnerException, "Tool '{ToolName}' threw an exception", descriptor.Name);
            return ToolCallResult.Failure(ex.InnerException.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invoke tool '{ToolName}'", descriptor.Name);
            return ToolCallResult.Failure(ex.Message);
        }
    }

    private object?[] BuildInvokeArguments(
        ToolDescriptor descriptor,
        ParameterInfo[] parameterInfos,
        IDictionary<string, object?> arguments)
    {
        var invokeArguments = new object?[parameterInfos.Length];

        for (int i = 0; i < parameterInfos.Length; i++)
        {
            var parameterInfo = parameterInfos[i];
            var parameterName = parameterInfo.Name ?? string.Empty;

            if (arguments.TryGetValue(parameterName, out var rawValue))
            {
                invokeArguments[i] = CoerceValue(rawValue, parameterInfo.ParameterType, parameterName);
            }
            else if (parameterInfo.HasDefaultValue)
            {
                invokeArguments[i] = parameterInfo.DefaultValue;
            }
            else
            {
                throw new ArgumentException(
                    $"Missing required parameter '{parameterName}' for tool '{descriptor.Name}'");
            }
        }

        return invokeArguments;
    }

    private async Task<ToolCallResult> ProcessReturnValue(
        ToolDescriptor descriptor,
        MethodInfo method,
        object? rawResult)
    {
        if (rawResult is null)
        {
            // void method or method that returned null
            return ToolCallResult.Success();
        }

        if (rawResult is Task task)
        {
            await task;

            // Check if the task has a result value (Task<T> rather than plain Task)
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

            // Plain Task (no return value)
            return ToolCallResult.Success();
        }

        // Synchronous return value
        return WrapReturnValue(rawResult);
    }

    private ToolCallResult WrapReturnValue(object? value)
    {
        if (value is null)
        {
            return ToolCallResult.Success();
        }

        // If the return value is a Result type, translate its success/failure
        if (value is Result result)
        {
            if (result.IsSuccess)
            {
                // Check if it's a Result<T> with a Value property
                var valueProperty = value.GetType().GetProperty("Value");
                var payload = valueProperty?.GetValue(value);
                return ToolCallResult.Success(payload);
            }

            return ToolCallResult.Failure(result.FirstErrorMessage);
        }

        return ToolCallResult.Success(value);
    }

    /// <summary>
    /// Coerces a raw argument value to the target parameter type.
    /// Handles common type conversions that occur when arguments arrive
    /// as strings or boxed primitives from JSON-RPC.
    /// </summary>
    private object? CoerceValue(object? rawValue, Type targetType, string parameterName)
    {
        if (rawValue is null)
        {
            if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null)
            {
                throw new ArgumentException(
                    $"Cannot assign null to non-nullable parameter '{parameterName}' of type '{targetType.Name}'");
            }
            return null;
        }

        var rawType = rawValue.GetType();

        // Already the correct type
        if (targetType.IsAssignableFrom(rawType))
        {
            return rawValue;
        }

        // String-based coercion for values that arrive as strings from JSON-RPC
        if (rawValue is string stringValue)
        {
            return CoerceFromString(stringValue, targetType, parameterName);
        }

        // Numeric coercion (e.g. long from JSON to int parameter)
        if (IsNumericType(targetType) && IsNumericType(rawType))
        {
            return Convert.ChangeType(rawValue, targetType, CultureInfo.InvariantCulture);
        }

        throw new ArgumentException(
            $"Cannot convert parameter '{parameterName}' from '{rawType.Name}' to '{targetType.Name}'");
    }

    private object CoerceFromString(string stringValue, Type targetType, string parameterName)
    {
        if (targetType == typeof(string))
        {
            return stringValue;
        }

        if (targetType == typeof(int))
        {
            if (int.TryParse(stringValue, CultureInfo.InvariantCulture, out var intResult))
            {
                return intResult;
            }
            throw new ArgumentException(
                $"Cannot parse '{stringValue}' as int for parameter '{parameterName}'");
        }

        if (targetType == typeof(long))
        {
            if (long.TryParse(stringValue, CultureInfo.InvariantCulture, out var longResult))
            {
                return longResult;
            }
            throw new ArgumentException(
                $"Cannot parse '{stringValue}' as long for parameter '{parameterName}'");
        }

        if (targetType == typeof(double))
        {
            if (double.TryParse(stringValue, CultureInfo.InvariantCulture, out var doubleResult))
            {
                return doubleResult;
            }
            throw new ArgumentException(
                $"Cannot parse '{stringValue}' as double for parameter '{parameterName}'");
        }

        if (targetType == typeof(bool))
        {
            if (bool.TryParse(stringValue, out var boolResult))
            {
                return boolResult;
            }
            throw new ArgumentException(
                $"Cannot parse '{stringValue}' as bool for parameter '{parameterName}'");
        }

        if (targetType == typeof(ResourceKey))
        {
            return new ResourceKey(stringValue);
        }

        throw new ArgumentException(
            $"Cannot convert string to '{targetType.Name}' for parameter '{parameterName}'");
    }

    private bool IsNumericType(Type type)
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
