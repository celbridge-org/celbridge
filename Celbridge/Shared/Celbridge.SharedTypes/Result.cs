using System.Runtime.CompilerServices;

namespace Celbridge.Foundation;

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

    private List<ErrorInfo> _errors = new List<ErrorInfo>();

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
    /// Gets the first error message in the errors list.
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

        if (!isSuccess && string.IsNullOrEmpty(error))
        {
            throw new ArgumentException("Error message must be provided if the result is a failure.");
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
    /// Initializes a new instance of the Result class with a success or failure state and an exception.
    /// </summary>
    protected Result(bool isSuccess, Exception exception, [CallerFilePath] string fileName = "", [CallerLineNumber] int lineNumber = 0)
    {
        IsSuccess = isSuccess;
        if (isSuccess && exception != null)
        {
            throw new ArgumentException("Exception must be null if the result is a success.");
        }

        if (!isSuccess && exception == null)
        {
            throw new ArgumentException("Exception must be provided if the result is a failure.");
        }

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
    public void AddError(Exception exception, [CallerFilePath] string fileName = "", [CallerLineNumber] int lineNumber = 0)
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
    }

    /// <summary>
    /// Adds errors from another result to this result.
    /// </summary>
    public Result AddErrors(Result otherResult)
    {
        _errors.InsertRange(0, otherResult._errors);
        return this;
    }

    /// <summary>
    /// Creates a failure result with an error message.
    /// </summary>
    public static Result Fail(string error, [CallerFilePath] string fileName = "", [CallerLineNumber] int lineNumber = 0)
    {
        if (string.IsNullOrEmpty(error))
        {
            throw new ArgumentException("Failure must have an error message.");
        }

        return new Failure(error, fileName, lineNumber);
    }

    /// <summary>
    /// Creates a failure result with an exception.
    /// </summary>
    public static Result Fail(Exception exception, [CallerFilePath] string fileName = "", [CallerLineNumber] int lineNumber = 0)
    {
        if (exception == null)
        {
            throw new ArgumentException("Failure must have an exception.");
        }

        return new Failure(exception, fileName, lineNumber);
    }

    /// <summary>
    /// Creates a success result.
    /// </summary>
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
        internal Failure(string error, string fileName, int lineNumber) : base(false, error, fileName, lineNumber) { }
        internal Failure(Exception exception, string fileName, int lineNumber) : base(false, exception, fileName, lineNumber) { }
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
    /// Initializes a new instance of the Result class with a failure state and an exception.
    /// </summary>
    private Result(Exception exception, string fileName, int lineNumber) : base(false, exception, fileName, lineNumber)
    {
        Value = default!;
    }

    /// <summary>
    /// Creates a success result with a value.
    /// </summary>
    public static Result<T> Ok(T value)
    {
        return new Result<T>(value);
    }

    /// <summary>
    /// Creates a failure result with an error message.
    /// </summary>
    public new static Result<T> Fail(string error, [CallerFilePath] string fileName = "", [CallerLineNumber] int lineNumber = 0)
    {
        return new Result<T>(error, fileName, lineNumber);
    }

    /// <summary>
    /// Creates a failure result with an exception.
    /// </summary>
    public new static Result<T> Fail(Exception exception, [CallerFilePath] string fileName = "", [CallerLineNumber] int lineNumber = 0)
    {
        return new Result<T>(exception, fileName, lineNumber);
    }

    /// <summary>
    /// Adds errors from another result to this result.
    /// </summary>
    public new Result<T> AddErrors(Result otherResult)
    {
        base.AddErrors(otherResult);
        return this;
    }
}
