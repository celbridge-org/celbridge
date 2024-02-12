namespace Celbridge.BaseLibrary.Core;

/**
 * A lightweight Result type for returning success or failure from methods.
 * This is used in situations where an operation is expected to fail in a well 
 * defined and understood manner. Exceptions are reserved for truly exceptional
 * situations where there is no clear way to handle the error.
 * For success results, the payload is guaranteed to not be null.
 */
public abstract class Result
{
    public bool IsSuccess { get; protected set; }
    public string Error { get; protected set; } = string.Empty;
    public bool IsFailure => !IsSuccess;

    protected Result(bool isSuccess, string error)
    {
        if (isSuccess && !string.IsNullOrEmpty(error))
        {
            throw new ArgumentException("Error must be null if the result is a success.", nameof(error));
        }
        if (!isSuccess && string.IsNullOrEmpty(error))
        {
            throw new ArgumentException("Error message must be provided if the result is a failure.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Fail(string error)
    {
        if (string.IsNullOrEmpty(error))
        {
            throw new ArgumentException("Failure must have an error message.");
        }

        return new Failure(error);
    }

    // Factory method for success results with no payload
    public static Result Ok()
    {
        return new OkResult();
    }
    private class OkResult : Result
    {
        public OkResult() : base(true, string.Empty) { }
    }

    private class Failure : Result
    {
        internal Failure(string error) : base(false, error) { }
    }
}
public class Result<T> : Result where T : notnull
{
    public T Value { get; private set; }

    private Result(T value) : base(true, string.Empty)
    {
        Value = value;
    }

    private Result(string error) : base(false, error)
    {
        // Placeholder for value, will never be accessed in case of failure
        Value = default!;
    }

    public static Result<T> Ok(T value)
    {
        return new Result<T>(value);
    }

    public new static Result<T> Fail(string error)
    {
        return new Result<T>(error);
    }
}
