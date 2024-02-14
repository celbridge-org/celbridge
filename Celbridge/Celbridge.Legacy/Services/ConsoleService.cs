using IronPython.Hosting;
using Microsoft.Scripting.Hosting;
using System.Reflection;
using CommunityToolkit.WinUI.Helpers;
using CommunityToolkit.Mvvm.Messaging;
using Celbridge.BaseLibrary.Messaging;

namespace Celbridge.Legacy.Services;

public interface IConsoleService
{
    event Action<string, ConsoleLogType>? OnWriteMessage;
    event Action? OnClearMessages;

    void WriteMessage(string message, ConsoleLogType logType);
    void ClearMessages();
    Result ExecuteCommand(string commandText);
    Result<string> CycleHistory(bool forwards);
    void ClearHistory();
    public Result EnterChatMode(string textFile, string context);
    public Result ExitChatMode();
}

public enum ConsoleLogType
{
    Info,
    Ok,
    Error,
    Warn
}

public class ConsoleService : IConsoleService
{
    const string HistoryFile = "ConsoleHistory.json";
    const int HistoryLinesMax = 200;

    private readonly IChatService _chatService;

    public event Action<string, ConsoleLogType>? OnWriteMessage;
    public event Action? OnClearMessages;

    private readonly ScriptEngine _scriptEngine;
    private readonly ScriptScope _scriptScope;
    private readonly MemoryStream _outputStream;

    private List<string> _history = new();
    private int _historyIndex;

    private bool _isSaveRequested;
    private bool _isSaving;
    private readonly DispatcherTimer _saveTimer;

    private bool _isChatModeEnabled;
    private string _chatFile = string.Empty;

    public ConsoleService(IMessengerService messengerService, IChatService chatService)
    {
        _chatService = chatService;

        // Todo: Get UNO logging working.
        // I ended up using Serilog to implement logging because I couldn't figure out
        // how to use the built in Uno logging stuff. This approach means we don't get any
        // logs prior to the ConsoleService init.

        messengerService.Register<ApplicationClosingMessage>(this, OnApplicationClosing);
        messengerService.Register<ActiveProjectChangedMessage>(this, OnActiveProjectChanged);

        Log.Logger = new LoggerConfiguration()
            .WriteTo.ConsoleService(this) // Our custom sink that writes to the Console panel in the app
            .WriteTo.Debug() // Writes to the Visual Studio debug Output window (uses a Nuget package)
            .CreateLogger();

        _outputStream = new MemoryStream();
        _scriptEngine = Python.CreateEngine();
        _scriptEngine.Runtime.IO.SetOutput(_outputStream, new UTF8Encoding(true, false));
        _scriptScope = _scriptEngine.CreateScope();

        var assembly = Assembly.GetExecutingAssembly();
        var assemblyPath = assembly.Location;
        var assemblyFolder = Path.GetDirectoryName(assemblyPath);
        var assemblyName = assembly.GetName();

        Execute($"import clr");
        // Execute($"clr.AddReferenceToFileAndPath(\"{assemblyFolder}\")"); // May not be necessary?
        Execute($"clr.AddReference(\"{assemblyName}\")");
        Execute($"import Celbridge.Legacy.Utils");
        Execute($"utils = Celbridge.Legacy.Utils.PythonBindings");

        LoadHistory();

        _saveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _saveTimer.Tick += OnSaveTimerTick;
        _saveTimer.Start();
    }

    private void OnApplicationClosing(object recipient, ApplicationClosingMessage message)
    {
        // Flush the console log before closing
        Close();
    }

    private void OnActiveProjectChanged(object recipient, ActiveProjectChangedMessage message)
    {
        ClearMessages();
    }

    private void OnSaveTimerTick(object? sender, object e)
    {
        if (_isSaveRequested && !_isSaving)
        {
            _isSaveRequested = false;
            SaveHistoryAsync();
        }
    }

    public void WriteMessage(string message, ConsoleLogType logType)
    {
        OnWriteMessage?.Invoke(message, logType);
    }

    public void ClearMessages()
    {
        OnClearMessages?.Invoke();
    }

    public Result ExecuteCommand(string commandText)
    {
        return Execute(commandText, true);
    }

    private Result Execute(string command, bool addToHistory = false)
    {
        var commandText = command.Trim();
        if (commandText.Length == 0)
        {
            return new ErrorResult("Failed to execute command because command is empty");
        }

        if (addToHistory)
        {
            bool repeatedCommand = _history.Count > 0 && _history[_history.Count - 1] == commandText;
            if (!repeatedCommand)
            {
                _history.Add(commandText);
                _isSaveRequested = true;
            }
        }

        // Reset the history index to the most recent command
        _historyIndex = Math.Max(0, _history.Count - 1);

        Log.Information($"> {commandText}");
        try
        {
            string output = string.Empty;
            var result = ExecuteInternalCommand(commandText);
            if (result.Success)
            {
                output = result.Data!;
            }
            else
            {
                // Execute the command as a Python script
                // Have to pass the scope in to persist state between calls!
                //_scriptEngine.Execute(commandText, _scriptScope);
                //output = Encoding.Default.GetString(_outputStream.ToArray());
                //outputStream.SetLength(0);
            }

            if (output.Length > 0) 
            {
                Log.Information($"{output}");
            }

            return new SuccessResult();
        }
        catch (Exception ex)
        {
            Log.Information($"Error: {ex.Message}");
            return new ErrorResult(ex.Message);
        }
    }

    private Result<string> ExecuteInternalCommand(string commandText)
    {
        string command = commandText.Trim();

        if (_isChatModeEnabled)
        {
            DoChatInput(commandText);
            return new SuccessResult<string>(string.Empty);
        }

        switch (command)
        {
            case "Clear":
                ClearMessages();
                return new SuccessResult<string>(string.Empty);
            case "ClearHistory":
                ClearHistory();
                return new SuccessResult<string>("Cleared command history");
        }

        return new ErrorResult<string>("Unknown internal command");
    }

    private void DoChatInput(string commandText)
    {
        if (commandText.Trim().EndsWith("EndChat()"))
        {
            ExitChatMode();
            return;
        }

        async Task AddChatUserInput()
        {
            var response = await _chatService.Ask(commandText);
            if (string.IsNullOrEmpty(response))
            {
                Log.Information(response);

                try
                {
                    var directory = Path.GetDirectoryName(_chatFile);
                    Guard.IsNotNull(directory);

                    Directory.CreateDirectory(directory);
                    File.WriteAllText(_chatFile, response);
                }
                catch (Exception ex)
                {
                    Log.Error(ex.Message);
                }
            }
        }

        _ = AddChatUserInput();
    }

    public Result<string> CycleHistory(bool forwards)
    {
        if (_history.Count == 0)
        {
            return new ErrorResult<string>("Failed to cycle history because history is empty.");
        }

        var line = _history[_historyIndex];
        if (forwards)
        {
            if (_historyIndex < _history.Count - 1)
            {
                _historyIndex++;
            }
        }
        else
        {
            if (_historyIndex > 0)
            {
                _historyIndex--;
            }
        }

        return new SuccessResult<string>(line);
    }

    private async void SaveHistoryAsync()
    {
        try
        {
            _isSaving = true;

            // Avoid the history file growing unbounded
            List<string> clippedHistory;
            if (_history.Count == 0)
            {
                clippedHistory = new List<string>();
            }
            else if (_history.Count <= HistoryLinesMax)
            {
                clippedHistory = new List<string>(_history);
            }
            else
            {
                clippedHistory = new List<string>(_history.Skip(_history.Count - HistoryLinesMax));
            }

            string json = JsonConvert.SerializeObject(clippedHistory, Formatting.Indented);

            StorageFolder storageFolder = ApplicationData.Current.LocalFolder;
            StorageFile file = await storageFolder.CreateFileAsync(HistoryFile, CreationCollisionOption.ReplaceExisting);
            using (Stream stream = await file.OpenStreamForWriteAsync())
            {
                using StreamWriter writer = new(stream);
                await writer.WriteAsync(json);
            }

            _isSaving = false;
        }
        catch (Exception ex)
        {
            _isSaving = false;
            Log.Error($"Failed to save command history. {ex.Message}");
        }
    }

    private async void LoadHistory()
    {
        try
        {
            StorageFolder storageFolder = ApplicationData.Current.LocalFolder;

            var fileExists = await storageFolder.FileExistsAsync(HistoryFile);
            if (!fileExists)
            {
                return;
            }

            StorageFile file = await storageFolder.GetFileAsync(HistoryFile);
            using (Stream stream = await file.OpenStreamForReadAsync())
            {
                using StreamReader reader = new(stream);
                var json = reader.ReadToEnd();

                // If Json fails to deserialize, default to an empty history
                _history = JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>();
            }
            _historyIndex = Math.Max(_history.Count - 1, 0);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load command history. {ex.Message}");
        }
    }

    public void ClearHistory()
    {
        _history.Clear();
        _isSaveRequested = true;
    }

    public Result EnterChatMode(string chatFile, string context)
    {
        if (_isChatModeEnabled)
        {
            return new ErrorResult("Failed to start Chat Mode because a chat is already active"); 
        }
        _isChatModeEnabled = true;
        _chatFile = chatFile;

        _chatService.StartChat(context);

        return new SuccessResult();
    }

    public Result ExitChatMode()
    {
        if (!_isChatModeEnabled)
        {
            return new ErrorResult("Chat mode is already enabled");
        }
        _isChatModeEnabled = false;
        _chatFile = string.Empty;

        _chatService.EndChat();

        return new SuccessResult();
    }

    private void Close()
    {
        Log.CloseAndFlush();
    }
}
