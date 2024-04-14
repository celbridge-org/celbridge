namespace Celbridge.BaseLibrary.Console;

public interface ICommandHistory
{
    /// <summary>
    /// The maximum number of commands to store in the history.
    /// </summary>
    uint MaxHistorySize { get; set; }

    /// <summary>
    /// The number of commands currently stored in the history.
    /// </summary>
    uint NumCommands { get; }

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
    /// Returns the currently selected command in the history.
    /// </summary>
    Result<string> GetSelectedCommand();

    /// <summary>
    /// Returns true if there is a next command in the command history that can be selected.
    /// </summary>
    bool CanSelectNextCommand { get; }

    /// <summary>
    /// Select the next command in the history.
    /// </summary>
    Result SelectNextCommand();

    /// <summary>
    /// Returns true if there is a previous command in the history that can be selected.
    /// </summary>
    bool CanSelectPreviousCommand { get; }

    /// <summary>
    /// Select the previous command in the history.
    /// </summary>
    Result SelectPreviousCommand();
}
