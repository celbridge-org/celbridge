namespace Celbridge.Console;

public class ConsoleHistory
{
    const int HistoryLinesMax = 200;

    private List<string> _history = new();
    private int _historyIndex;

    public void Add(string commandText)
    {
        // If the same command is entered repeatedly, only store the first instance
        bool repeatedCommand = _history.Count > 0 && _history[_history.Count - 1] == commandText;
        if (!repeatedCommand)
        {
            _history.Add(commandText);
        }

        // Limit how long the history can grow
        while (_history.Count > HistoryLinesMax) 
        {
            _history.RemoveAt(0);
        }

        // Set the history index to the most recently added command
        _historyIndex = Math.Max(0, _history.Count - 1);
    }

    public Result<string> CycleForward()
    {
        if (_history.Count == 0)
        {
            return Result<string>.Fail("Failed to cycle history because history is empty.");
        }

        if (_historyIndex < _history.Count - 1)
        {
            _historyIndex++;
        }
        var line = _history[_historyIndex];

        return Result<string>.Ok(line);
    }

    public Result<string> CycleBackward()
    {
        if (_history.Count == 0)
        {
            return Result<string>.Fail("Failed to cycle history because history is empty.");
        }

        var line = _history[_historyIndex];
        if (_historyIndex > 0)
        {
            _historyIndex--;
        }

        return Result<string>.Ok(line);
    }
}
