namespace Celbridge.BaseLibrary.Logging;

public interface ILoggingService
{
    public void Info(string message);

    public void Warn(string message);

    public void Error(string message);
}
