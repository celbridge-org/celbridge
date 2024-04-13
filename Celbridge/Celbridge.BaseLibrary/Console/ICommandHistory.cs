namespace Celbridge.BaseLibrary.Console;

public interface ICommandHistory
{
    /// <summary>
    /// The maximum number of commands to store in the history.
    /// </summary>
    uint HistorySizeMax { get; set; }

    /// <summary>
    /// The nember of commands currently stored in the history.
    /// </summary>
    uint HistorySize { get; }

    /// <summary>
    /// Remove all commands from the history.
    /// </summary>
    void Clear();

    /// <summary>
    /// Add a command to the history.
    /// If this causes the HistorySizeMax limit to be exceeded, the oldest command is removed from the history.
    /// If the command is empty, or identical to the previously added command then this call has no effect on the history.
    /// </summary>
    void AddCommand(string command);

    /// <summary>
    /// Returns the currently active command in the history.
    /// </summary>
    Result<string> GetCurrentCommand();

    /// <summary>
    /// Returns true if there is a next command in the command history that can be selected.
    /// </summary>
    bool CanMoveToNextCommand { get; }

    /// <summary>
    /// Move to the next command in the history.
    /// </summary>
    Result MoveToNextCommand();

    /// <summary>
    /// Returns true if there is a previous command in the history that can be selected.
    /// </summary>
    bool CanMoveToPreviousCommand { get; }

    /// <summary>
    /// Move to the previous command in the history.
    /// </summary>
    Result MoveToPreviousCommand();
}
