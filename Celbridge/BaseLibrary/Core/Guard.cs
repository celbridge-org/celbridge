namespace Celbridge.Core;

/// <summary>
/// A simple guard class for common assert checks.
/// </summary>
public static class Guard
{
    /// <summary>
    /// Ensures the specified value is null.
    /// </summary>
    public static void IsNull<T>(T? value, string parameterName) where T : class
    {
        if (value is not null)
        {
            throw new InvalidOperationException($"Value '{parameterName}' must be null.");
        }
    }

    /// <summary>
    /// Ensures the specified value is null.
    /// </summary>
    public static void IsNull<T>(T? value) where T : class
    {
        if (value is not null)
        {
            throw new InvalidOperationException("Value must be null.");
        }
    }

    /// <summary>
    /// Ensures the specified value is not null.
    /// </summary>
    public static void IsNotNull<T>([System.Diagnostics.CodeAnalysis.NotNull] T? value, string parameterName) where T : class
    {
        if (value is null)
        {
            throw new InvalidOperationException($"Value '{parameterName}' cannot be null.");
        }
    }

    /// <summary>
    /// Ensures the specified value is not null.
    /// </summary>
    public static void IsNotNull<T>([System.Diagnostics.CodeAnalysis.NotNull] T? value) where T : class
    {
        if (value is null)
        {
            throw new InvalidOperationException("Value cannot be null.");
        }
    }

    /// <summary>
    /// Ensures the specified string is not null or empty.
    /// </summary>
    public static void IsNotNullOrEmpty([System.Diagnostics.CodeAnalysis.NotNull] string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException($"Value '{parameterName}' cannot be null or empty.");
        }
    }

    /// <summary>
    /// Ensures the specified string is not null or empty.
    /// </summary>
    public static void IsNotNullOrEmpty([System.Diagnostics.CodeAnalysis.NotNull] string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException("Value cannot be null or empty.");
        }
    }

    /// <summary>
    /// Ensures the specified string is not null or whitespace.
    /// </summary>
    public static void IsNotNullOrWhiteSpace([System.Diagnostics.CodeAnalysis.NotNull] string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Value '{parameterName}' cannot be null or whitespace.");
        }
    }

    /// <summary>
    /// Ensures the specified string is not null or whitespace.
    /// </summary>
    public static void IsNotNullOrWhiteSpace([System.Diagnostics.CodeAnalysis.NotNull] string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Value cannot be null or whitespace.");
        }
    }

    /// <summary>
    /// Ensures the specified value is within the specified range.
    /// </summary>
    public static void IsInRange<T>(T value, T min, T max, string parameterName) where T : IComparable<T>
    {
        if (value.CompareTo(min) < 0 || value.CompareTo(max) > 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"Value '{parameterName}' must be in the range [{min}, {max}].");
        }
    }

    /// <summary>
    /// Ensures the specified value is within the specified range.
    /// </summary>
    public static void IsInRange<T>(T value, T min, T max) where T : IComparable<T>
    {
        if (value.CompareTo(min) < 0 || value.CompareTo(max) > 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"Value must be in the range [{min}, {max}].");
        }
    }

    /// <summary>
    /// Ensures the specified condition is true.
    /// </summary>
    public static void IsTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>
    /// Ensures the specified condition is true.
    /// </summary>
    public static void IsTrue(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Condition must be true.");
        }
    }

    /// <summary>
    /// Ensures the specified condition is false.
    /// </summary>
    public static void IsFalse(bool condition)
    {
        if (condition)
        {
            throw new InvalidOperationException("Condition must be true.");
        }
    }
}
