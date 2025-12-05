namespace Celbridge.Console.Services;

public class Terminal : ITerminal, IDisposable
{
#if WINDOWS
    private ConPtyTerminal _terminal = new ConPtyTerminal();
#endif

#pragma warning disable CS0067 // Event is used in Windows platform-specific code
    public event EventHandler<string>? OutputReceived;
    public event EventHandler? ProcessExited;
#pragma warning restore CS0067

    public string CommandBuffer { get; set; } = string.Empty;

    public Terminal()
    {
#if WINDOWS
        _terminal.OutputReceived += (sender, output) =>
        {
            OutputReceived?.Invoke(sender, output);
        };

        _terminal.ProcessExited += (sender, e) =>
        {
            ProcessExited?.Invoke(sender, e);
        };
#else
        throw new NotImplementedException();
#endif
    }

    public void Start(string commandLine, string workingDir)
    {
#if WINDOWS
        _terminal.Start(commandLine, workingDir);
#else
        throw new NotImplementedException();
#endif
    }

    public void Write(string input)
    {
#if WINDOWS
        _terminal.Write(input);
#else
        throw new NotImplementedException();
#endif
    }

    public void SetSize(int cols, int rows)
    {
#if WINDOWS
        _terminal.SetSize(cols, rows);
#else
        throw new NotImplementedException();
#endif
    }

    public void Dispose()
    {
#if WINDOWS
        _terminal?.Dispose();
#endif
    }
}
