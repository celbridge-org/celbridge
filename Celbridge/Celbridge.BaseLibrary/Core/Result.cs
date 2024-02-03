namespace Celbridge.BaseLibrary.Core;

/**
 * A lightweight Result type for returning success or failure from methods.
 * This is used in situations where an operation is expected to fail in a well 
 * defined and understood manner. Exceptions are reserved for truly exceptional
 * situations where there is no clear way to handle the error.
 */
public class Result
{
    public bool IsSuccess { get; private set; }
    public string? Error { get; private set; }
    public bool IsFailure => !IsSuccess;

    protected Result(bool isSuccess, string? error)
    {
        if (isSuccess)
        {
            if (error != null)
            {
                throw new ArgumentException("Error must be null if the result is a success.");
            }
        }
        else 
        {
            if (string.IsNullOrEmpty(error))
            {
                throw new ArgumentException("Error message must be provided if the result is a failure.");
            }
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Fail(string message)
    {
        if (string.IsNullOrEmpty(message))
        { 
            throw new ArgumentException("Failure must have an error message.", nameof(message));
        }

        return new Result(false, message);
    }

    // Factory method for success results with no payload
    public static Result Ok()
    {
        return new Result(true, null);
    }

    public static Result<T> Ok<T>(T value) where T : notnull
    {
        return new Result<T>(value, true, null);
    }
}

public class Result<T> : Result where T : notnull
{
    private T _value;

    public T Value
    {
        get
        {
            if (IsFailure)
            {
                throw new InvalidOperationException("Cannot access the value of a failed result.");
            }
            return _value;
        }
    }

    public Result(T value, bool isSuccess, string? error)
        : base(isSuccess, error)
    {
        if (isSuccess && value == null)
        { 
            throw new ArgumentNullException(nameof(value), "Success result must have a non-null value.");
        }

        _value = value;
    }
}
