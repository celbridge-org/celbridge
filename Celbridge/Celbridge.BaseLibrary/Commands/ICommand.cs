namespace Celbridge.BaseLibrary.Commands;

/// <summary>
/// A command that can be executed via the command service.
/// </summary>
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
