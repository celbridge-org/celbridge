using System.Runtime.CompilerServices; // For [CallerFilePath] and [CallerLineNumber]

namespace Celbridge.Core;

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
    }

    private List<ErrorInfo> _errors = new List<ErrorInfo>();
    public string Error => string.Join(Environment.NewLine, _errors.Select(e => $"{e.Message} ({e.FileName}:{e.LineNumber})"));

    public void MergeErrors(Result otherResult)
    {
        _errors.InsertRange(0, otherResult._errors);
    }

    public bool IsSuccess { get; protected set; }
    public bool IsFailure => !IsSuccess;


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

        _errors.Add(new ErrorInfo 
        { 
            Message = error, 
            FileName = Path.GetFileName(fileName), 
            LineNumber = lineNumber 
        });
    }

    public static Result Fail(string error, [CallerFilePath] string fileName = "", [CallerLineNumber] int lineNumber = 0)
    {
        if (string.IsNullOrEmpty(error))
        {
            throw new ArgumentException("Failure must have an error message.");
        }

        return new Failure(error, fileName, lineNumber);
    }

    public static Result Ok()
    {
        return new OkResult();
    }

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

    private class OkResult : Result
    {
        public OkResult() : base(true, string.Empty) { }
    }

    private class Failure : Result
    {
        internal Failure(string error, string fileName, int lineNumber) : base(false, error, fileName, lineNumber) { }
    }
}

public class Result<T> : Result where T : notnull
{
    public T Value { get; private set; }

    private Result(T value) : base(true, string.Empty)
    {
        Value = value;
    }

    private Result(string error, string fileName, int lineNumber) : base(false, error, fileName, lineNumber)
    {
        // Placeholder for value, will never be accessed in case of failure
        Value = default!;
    }

    public static Result<T> Ok(T value)
    {
        return new Result<T>(value);
    }

    public new static Result<T> Fail(string error, [CallerFilePath] string fileName = "", [CallerLineNumber] int lineNumber = 0)
    {
        return new Result<T>(error, fileName, lineNumber);
    }
}
