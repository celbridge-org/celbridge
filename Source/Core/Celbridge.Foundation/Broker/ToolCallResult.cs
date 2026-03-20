namespace Celbridge.Broker;

/// <summary>
/// The result of invoking a broker tool via CallToolAsync.
/// </summary>
public record class ToolCallResult
{
    /// <summary>
    /// True if the tool executed successfully.
    /// </summary>
    public required bool IsSuccess { get; init; }

    /// <summary>
    /// An error message if IsSuccess is false, otherwise empty.
    /// </summary>
    public string ErrorMessage { get; init; } = string.Empty;

    /// <summary>
    /// An optional return value from the tool method.
    /// </summary>
    public object? Value { get; init; }

    public static ToolCallResult Success(object? value = null)
    {
        return new ToolCallResult
        {
            IsSuccess = true,
            Value = value
        };
    }

    public static ToolCallResult Failure(string errorMessage)
    {
        return new ToolCallResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }
}
