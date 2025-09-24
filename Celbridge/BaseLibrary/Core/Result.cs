using System.Runtime.CompilerServices;

namespace Celbridge.Core;

public static class ResultExtensions
{
    /// <summary>
    /// Extension helper method that wraps a value in a success Result<T>.
    /// Allows you to use `return myValue.OkResult<T>()` instead of `return Result<T>.Ok(myValue)`
    /// Usually T can be inferred, so in general you can simply use `return myValue.OkResult()`
    /// </summary>
    public static Result<T> OkResult<T>(this T value) where T : notnull
    {
        return Result<T>.Ok(value);
    }
}

/**
 * A lightweight Result type for returning success or failure from methods.
 * This is used in situations where an operation is expected to fail in a well 
 * defined and understood manner. Exceptions are reserved for truly exceptional
 * situations where there is no clear way to handle the error.
 * For success results, the payload is guaranteed to not be null.
 */
public abstract class Result
{
    private struct ErrorInfo
    {
        public string Message;
        public string FileName;
        public int LineNumber;
        public Exception? Exception;
    }

    private readonly List<ErrorInfo> _errors = new();

    /// <summary>
    /// Gets a concatenated string of all error messages, including file names and line numbers.
    /// </summary>
    public string Error
    {
        get
        {
            var errorMessages = new List<string>();

            for (int i = _errors.Count - 1; i >= 0; i--)
            {
                var error = _errors[i];

                var errorMessage = $"{error.Message} ({error.FileName}:{error.LineNumber})";

                if (error.Exception != null)
                {
                    errorMessage += $"{Environment.NewLine}Exception: {error.Exception}";
                }

                errorMessages.Add(errorMessage);
            }

            return string.Join(Environment.NewLine, errorMessages);
        }
    }

    /// <summary>
    /// Returns the first error message in the errors list.
    /// </summary>
    public string FirstErrorMessage
    {
        get
        {
            if (_errors.Count == 0)
            {
                return string.Empty;
            }

            return _errors[0].Message;
        }
    }

    /// <summary>
    /// Returns the first exception in the errors list.
    /// </summary>
    public Exception? FirstException
    {
        get
        {
            foreach (var error in _errors)
            {
                if (error.Exception != null)
                {
                    return error.Exception;
                }
            }

            return null;
        }
    }

    public bool IsSuccess { get; protected set; }
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Initializes a new instance of the Result class with a success or failure state and an error message.
    /// </summary>
    protected Result(bool isSuccess, string error, [CallerFilePath] string fileName = "", [CallerLineNumber] int lineNumber = 0)
    {
        IsSuccess = isSuccess;

        if (isSuccess && !string.IsNullOrEmpty(error))
        {
            throw new ArgumentException("Error must be null if the result is a success.");
        }

        if (!string.IsNullOrEmpty(error))
        {
            _errors.Add(new ErrorInfo
            {
                Message = error,
                FileName = Path.GetFileName(fileName),
                LineNumber = lineNumber
            });
        }
    }

    /// <summary>
    /// Adds an error message to the result.
    /// </summary>
    public void AddError(string error, [CallerFilePath] string fileName = "", [CallerLineNumber] int lineNumber = 0)
    {
        if (!string.IsNullOrEmpty(error))
        {
            _errors.Add(new ErrorInfo
            {
                Message = error,
                FileName = Path.GetFileName(fileName),
                LineNumber = lineNumber
            });
        }
    }

    /// <summary>
    /// Adds an exception to the result.
    /// </summary>
    public Result WithException(Exception exception, [CallerFilePath] string fileName = "", [CallerLineNumber] int lineNumber = 0)
    {
        if (exception != null)
        {
            _errors.Add(new ErrorInfo
            {
                Message = exception.Message,
                FileName = Path.GetFileName(fileName),
                LineNumber = lineNumber,
                Exception = exception
            });
        }

        return this;
    }

    /// <summary>
    /// Adds errors from another result to this result (works across different generic payload types).
    /// </summary>
    public Result WithErrors(Result otherResult)
    {
        if (otherResult is null) return this;
        _errors.InsertRange(0, otherResult._errors);
        return this;
    }

    /// <summary>
    /// Creates a failure result with no error message.
    /// </summary>
    public static FailureResult Fail()
    {
        return new FailureResult(string.Empty, string.Empty, 0);
    }

    /// <summary>
    /// Creates a failure result with an error message.
    /// </summary>
    public static FailureResult Fail(string error, [CallerFilePath] string fileName = "", [CallerLineNumber] int lineNumber = 0)
    {
        return new FailureResult(error, fileName, lineNumber);
    }

    /// <summary>
    /// Creates a success result.
    /// </summary>
    public static Result Ok()
    {
        return new OkResult();
    }

    private sealed class OkResult : Result
    {
        public OkResult() : base(true, string.Empty) { }
    }

    /// <summary>
    /// This non-generic type can be implicitly converted to any Result<T> failure result.
    /// This avoids the need to specify the generic return type, e.g. Result<IProject>.Fail() -> Result.Fail() 
    /// </summary>
    public sealed class FailureResult : Result
    {
        public FailureResult(string error, string fileName, int lineNumber)
            : base(false, error, fileName, lineNumber) { }

        /// <summary>
        /// Redeclare the base method to keep the static type as FailureResult when chaining.
        /// </summary>
        public new FailureResult WithErrors(Result otherResult)
        {
            base.WithErrors(otherResult);
            return this;
        }

        /// <summary>
        /// Redeclare the base method to keep the static type as FailureResult when chaining.
        /// </summary>
        public new FailureResult WithException(Exception exception, [CallerFilePath] string fileName = "", [CallerLineNumber] int lineNumber = 0)
        {
            base.WithException(exception, fileName, lineNumber);
            return this;
        }
    }
}

public class Result<T> : Result where T : notnull
{
    public T Value { get; private set; }

    /// <summary>
    /// Initializes a new instance of the Result class with a success state and a value.
    /// </summary>
    private Result(T value) : base(true, string.Empty)
    {
        Value = value;
    }

    /// <summary>
    /// Initializes a new instance of the Result class with a failure state and an error message.
    /// </summary>
    private Result(string error, string fileName, int lineNumber) : base(false, error, fileName, lineNumber)
    {
        Value = default!;
    }

    /// <summary>
    /// Creates a success result with a value.
    /// </summary>
    public static Result<T> Ok(T value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        return new Result<T>(value);
    }

    /// <summary>
    /// Creates a failure result with no error message.
    /// </summary>
    public new static Result<T> Fail()
    {
        return new Result<T>(string.Empty, string.Empty, 0);
    }

    /// <summary>
    /// Creates a failure result with an error message.
    /// </summary>
    public new static Result<T> Fail(string error, [CallerFilePath] string fileName = "", [CallerLineNumber] int lineNumber = 0)
    {
        return new Result<T>(error, fileName, lineNumber);
    }

    /// <summary>
    /// Adds errors from another result to this result.
    /// </summary>
    public new Result<T> WithErrors(Result otherResult)
    {
        base.WithErrors(otherResult);
        return this;
    }

    /// <summary>
    /// Adds an exception to the result.
    /// </summary>
    public new Result<T> WithException(Exception exception, [CallerFilePath] string fileName = "", [CallerLineNumber] int lineNumber = 0)
    {
        base.WithException(exception, fileName, lineNumber);
        return this;
    }

    /// <summary>
    /// Implicitly convert a value of T into a success Result<T>.
    /// For concrete types, this allows you to return the value directly instead of explicitly wrapping it in a Result<T>.
    /// For interfaces, the C# compiler can't resolve the implicit operator, so use the .OkResult() extension method on the returned object to wrap it with a Result<T>.
    /// </summary>
    public static implicit operator Result<T>(T value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        return Result<T>.Ok(value);
    }

    /// <summary>
    /// Implicitly convert a concrete FailureResult into a generic Result<T>, copying all errors.
    /// This allows you to 
    /// </summary>
    public static implicit operator Result<T>(FailureResult failure)
    {
        var r = Result<T>.Fail();
        r.WithErrors(failure);
        return r;
    }
}
