namespace Celbridge.BaseLibrary.Commands;

public interface ICommand
{
    /// <summary>
    /// Unique identifier for the command.
    /// </summary>
    CommandId Id { get; }

    /// <summary>
    /// Execute the command.
    /// </summary>
    Task<Result> ExecuteAsync();
}
